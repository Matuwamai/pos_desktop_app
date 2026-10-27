using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Core.Sync;
using Pos.Infra.Api;
using Pos.Infra.Storage;

namespace Pos.Infra.Sync;

/// <summary>
/// The pull side of offline-first: repeatedly calls <c>GET /sync/pull</c> starting from
/// the cursor stored in <c>meta</c>, upserting every page into SQLite, until the server
/// says <c>hasMore: false</c> — then the cursor is saved as <c>nextSince</c> for next
/// time. A row is never deleted locally from a pull: the server's tombstone is
/// <c>isActive: false</c>, which upserts like anything else (callers filter it out when
/// reading, same as the server's own admin UI would).
/// </summary>
public sealed class SyncEngine(LocalDatabase db, ApiClient api)
{
    private const string CursorKey = "sync_since";
    private const string LastSyncedKey = "last_synced_at";
    private const string TenantSingletonId = "self";

    public async Task<SyncResult> PullAllAsync(string deviceToken, CancellationToken ct = default)
    {
        var since = await GetCursorAsync(ct);
        var pages = 0;
        PullResponse last;
        do
        {
            last = await api.PullAsync(deviceToken, since, ct: ct);
            await ApplyAsync(last.Entities, ct);
            if (last.Entities.Tenant is { } tenant) await SyncTenantLogoAsync(tenant, ct);
            since = last.NextSince;
            await SetCursorAsync(since, ct);
            pages++;
        } while (last.HasMore);

        var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(last.ServerTime);
        await SetMetaAsync(LastSyncedKey, serverTime.ToString("O"), ct);
        return new SyncResult(pages, since, serverTime);
    }

    /// <summary>Drains the local outbox: every <c>PENDING</c> row (batched ≤100, the
    /// server's per-push limit), pushed as-is — the JSON written at sale/drawer/customer
    /// creation time round-trips verbatim, never rebuilt here. Each result updates the
    /// outbox row and, for an <c>order.create</c> op, the matching local order's
    /// <c>sync_status</c> (a no-op update for other op types, since they have no
    /// matching <c>orders.op_id</c>). A <c>REJECTED</c> op is left exactly as it is, not
    /// retried automatically — same reasoning as the server's own push design.</summary>
    public async Task<PushResult> PushPendingAsync(string deviceToken, CancellationToken ct = default)
    {
        var pending = await LoadPendingOutboxAsync(ct);
        if (pending.Count == 0) return new PushResult(0, 0, 0);

        var envelopes = pending
            .Select(p => new SyncOpEnvelope { OpId = p.OpId, Type = p.Type, ClientTime = p.ClientTime, Payload = JsonSerializer.Deserialize<JsonElement>(p.PayloadJson) })
            .ToList();
        var response = await api.PushAsync(deviceToken, envelopes, ct);

        int created = 0, duplicate = 0, rejected = 0;
        await using var tx = db.Connection.BeginTransaction();
        foreach (var result in response.Results)
        {
            var resultJson = result.Result.ValueKind == JsonValueKind.Undefined ? "null" : result.Result.GetRawText();
            await UpdateOutboxStatusAsync(tx, result.OpId, result.Status, resultJson, ct);
            await UpdateOrderSyncStatusAsync(tx, result.OpId, result.Status, ct);
            switch (result.Status)
            {
                case "CREATED": created++; break;
                case "DUPLICATE": duplicate++; break;
                default: rejected++; break;
            }
        }
        await tx.CommitAsync(ct);
        return new PushResult(created, duplicate, rejected);
    }

    /// <summary>Push, then pull — in that order, so the pull's inventory numbers already
    /// reflect whatever this terminal just pushed, not a stale pre-push snapshot.</summary>
    public async Task<SyncResult> SyncNowAsync(string deviceToken, CancellationToken ct = default)
    {
        await PushPendingAsync(deviceToken, ct);
        return await PullAllAsync(deviceToken, ct);
    }

    /// <summary>
    /// Creates a customer <b>online only</b> — a direct, single-op push, not queued
    /// through the outbox like <see cref="PushPendingAsync"/> drains. Nothing is written
    /// locally unless the server actually confirms it (see the Phase 2 plan's decision
    /// #4): a customer isn't a sale, there's no "write it now, reconcile later" fallback
    /// for it in v1, so a connectivity failure here is reported back as-is rather than
    /// silently queued.
    /// </summary>
    public async Task<CustomerCreateResult> CreateCustomerAsync(string deviceToken, string name, string? phone, string? email, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        var envelope = new SyncOpEnvelope
        {
            OpId = Guid.NewGuid().ToString("N"),
            Type = SyncOpType.CustomerCreate,
            ClientTime = DateTimeOffset.UtcNow,
            Payload = new CustomerCreatePayload { Id = id, Name = name, Phone = phone, Email = email },
        };

        PushResponse response;
        try
        {
            response = await api.PushAsync(deviceToken, [envelope], ct);
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            return new CustomerCreateResult(false, null, "No connection to the server — customers can only be created while online.");
        }

        var result = response.Results.FirstOrDefault();
        if (result is null) return new CustomerCreateResult(false, null, "No response from the server.");

        if (result.Status is "CREATED" or "DUPLICATE")
        {
            var customerId = result.Result.ValueKind == JsonValueKind.Object && result.Result.TryGetProperty("customerId", out var idProp)
                ? idProp.GetString() ?? id
                : id;
            await using var tx = db.Connection.BeginTransaction();
            await UpsertAsync(tx, "customers", "id",
                ("id", customerId), ("customer_group_id", null), ("name", name), ("phone", phone), ("email", email),
                ("store_credit_balance", "0"), ("is_active", 1), ("updated_at", DateTimeOffset.UtcNow.ToString("O")));
            await tx.CommitAsync(ct);
            return new CustomerCreateResult(true, customerId, null);
        }

        var error = result.Result.ValueKind == JsonValueKind.Object && result.Result.TryGetProperty("error", out var errProp)
            ? errProp.GetString()
            : "The server rejected this customer.";
        return new CustomerCreateResult(false, null, error);
    }

    /// <summary>
    /// Runs after <see cref="ApplyAsync"/>'s transaction commits — this does a network
    /// call, which has no business happening inside an open SQLite write transaction.
    /// Best-effort: a failure here (offline, 404, whatever) just means printing falls
    /// back to no logo until the next pull that succeeds, never blocks the pull itself.
    /// </summary>
    private async Task SyncTenantLogoAsync(TenantRow tenant, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tenant.LogoUrl)) return;

        await using (var checkCmd = db.Connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT 1 FROM tenant WHERE id = $id AND logo_url = $url AND logo_local_path IS NOT NULL";
            checkCmd.Parameters.AddWithValue("$id", TenantSingletonId);
            checkCmd.Parameters.AddWithValue("$url", tenant.LogoUrl);
            if (await checkCmd.ExecuteScalarAsync(ct) is not null) return; // already have exactly this logo cached
        }

        byte[] bytes;
        try
        {
            bytes = await api.DownloadBytesAsync(tenant.LogoUrl, ct);
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            return;
        }

        var ext = Path.GetExtension(tenant.LogoUrl) is { Length: > 0 } e ? e : ".img";
        var localPath = Path.Combine(Pos.Infra.AppPaths.DataDirectory, $"logo{ext}");
        Directory.CreateDirectory(Pos.Infra.AppPaths.DataDirectory);
        await File.WriteAllBytesAsync(localPath, bytes, ct);

        await using var updateCmd = db.Connection.CreateCommand();
        updateCmd.CommandText = "UPDATE tenant SET logo_local_path = $path WHERE id = $id";
        updateCmd.Parameters.AddWithValue("$path", localPath);
        updateCmd.Parameters.AddWithValue("$id", TenantSingletonId);
        await updateCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Sales not yet confirmed by the server — drives the status bar's pending count.</summary>
    public async Task<int> GetPendingSaleCountAsync(CancellationToken ct = default)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM orders WHERE sync_status = 'PENDING'";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<List<PendingOutboxRow>> LoadPendingOutboxAsync(CancellationToken ct)
    {
        var rows = new List<PendingOutboxRow>();
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT op_id, type, payload_json, client_time FROM outbox WHERE status = 'PENDING' ORDER BY created_at LIMIT 100";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var clientTime = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
            rows.Add(new PendingOutboxRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), clientTime));
        }
        return rows;
    }

    private static async Task UpdateOutboxStatusAsync(SqliteTransaction tx, string opId, string status, string resultJson, CancellationToken ct)
    {
        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE outbox SET status = $status, result_json = $result, synced_at = $syncedAt WHERE op_id = $opId";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$result", resultJson);
        cmd.Parameters.AddWithValue("$syncedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$opId", opId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // A no-op (0 rows affected) for op types with no matching order, e.g. drawer.open or
    // customer.create — deliberately unconditional rather than branching on op type.
    private static async Task UpdateOrderSyncStatusAsync(SqliteTransaction tx, string opId, string status, CancellationToken ct)
    {
        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE orders SET sync_status = $status WHERE op_id = $opId";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$opId", opId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed record PendingOutboxRow(string OpId, string Type, string PayloadJson, DateTimeOffset ClientTime);

    /// <summary>Null if this device has never completed a pull. Drives the offline PIN
    /// login's max-offline-window check (<see cref="Pos.Infra.Auth.PinVerifier"/>).</summary>
    public async Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken ct = default)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", LastSyncedKey);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null ? null : DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture);
    }

    private async Task SetMetaAsync(string key, string value, CancellationToken ct)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<long> GetCursorAsync(CancellationToken ct)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", CursorKey);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null ? 0L : long.Parse((string)value, CultureInfo.InvariantCulture);
    }

    private async Task SetCursorAsync(long since, CancellationToken ct)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$k", CursorKey);
        cmd.Parameters.AddWithValue("$v", since.ToString(CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task ApplyAsync(PullEntities entities, CancellationToken ct)
    {
        await using var tx = db.Connection.BeginTransaction();

        foreach (var l in entities.Locations)
            await UpsertAsync(tx, "locations", "id", ("id", l.Id), ("name", l.Name), ("timezone", l.Timezone), ("is_active", SqlFormat.Bool(l.IsActive)), ("updated_at", l.UpdatedAt));

        foreach (var c in entities.Categories)
            await UpsertAsync(tx, "categories", "id", ("id", c.Id), ("name", c.Name), ("parent_id", c.ParentId), ("is_active", SqlFormat.Bool(c.IsActive)), ("updated_at", c.UpdatedAt));

        foreach (var p in entities.Products)
            await UpsertAsync(tx, "products", "id",
                ("id", p.Id), ("category_id", p.CategoryId), ("name", p.Name), ("description", p.Description),
                ("is_composite", SqlFormat.Bool(p.IsComposite)), ("track_inventory", SqlFormat.Bool(p.TrackInventory)), ("is_active", SqlFormat.Bool(p.IsActive)),
                ("updated_at", p.UpdatedAt), ("modifier_group_ids_json", System.Text.Json.JsonSerializer.Serialize(p.ModifierGroupIds)));

        foreach (var v in entities.Variants)
            await UpsertAsync(tx, "variants", "id",
                ("id", v.Id), ("product_id", v.ProductId), ("sku", v.Sku), ("barcode", v.Barcode),
                ("attributes_json", v.Attributes.ValueKind == System.Text.Json.JsonValueKind.Undefined ? null : v.Attributes.GetRawText()),
                ("price", SqlFormat.Money(v.Price)), ("cost", v.Cost.HasValue ? SqlFormat.Money(v.Cost.Value) : null), ("tax_rate_id", v.TaxRateId),
                ("is_active", SqlFormat.Bool(v.IsActive)), ("updated_at", v.UpdatedAt));

        foreach (var g in entities.ModifierGroups)
            await UpsertAsync(tx, "modifier_groups", "id", ("id", g.Id), ("name", g.Name), ("min_select", g.MinSelect), ("max_select", g.MaxSelect), ("is_active", SqlFormat.Bool(g.IsActive)), ("updated_at", g.UpdatedAt));

        foreach (var m in entities.Modifiers)
            await UpsertAsync(tx, "modifiers", "id", ("id", m.Id), ("modifier_group_id", m.ModifierGroupId), ("name", m.Name), ("price_adjustment", SqlFormat.Money(m.PriceAdjustment)), ("is_active", SqlFormat.Bool(m.IsActive)), ("updated_at", m.UpdatedAt));

        foreach (var t in entities.TaxRates)
            await UpsertAsync(tx, "tax_rates", "id", ("id", t.Id), ("name", t.Name), ("rate", SqlFormat.Money(t.Rate)), ("is_default", SqlFormat.Bool(t.IsDefault)), ("is_active", SqlFormat.Bool(t.IsActive)), ("updated_at", t.UpdatedAt));

        foreach (var p in entities.Promotions)
            await UpsertAsync(tx, "promotions", "id",
                ("id", p.Id), ("name", p.Name), ("code", p.Code), ("type", p.Type), ("value", SqlFormat.Money(p.Value)),
                ("min_subtotal", p.MinSubtotal.HasValue ? SqlFormat.Money(p.MinSubtotal.Value) : null), ("starts_at", p.StartsAt), ("ends_at", p.EndsAt),
                ("is_active", SqlFormat.Bool(p.IsActive)), ("updated_at", p.UpdatedAt));

        foreach (var g in entities.CustomerGroups)
            await UpsertAsync(tx, "customer_groups", "id", ("id", g.Id), ("name", g.Name), ("discount_percentage", SqlFormat.Money(g.DiscountPercentage)), ("is_active", SqlFormat.Bool(g.IsActive)), ("updated_at", g.UpdatedAt));

        foreach (var c in entities.Customers)
            await UpsertAsync(tx, "customers", "id",
                ("id", c.Id), ("customer_group_id", c.CustomerGroupId), ("name", c.Name), ("phone", c.Phone), ("email", c.Email),
                ("store_credit_balance", SqlFormat.Money(c.StoreCreditBalance)), ("is_active", SqlFormat.Bool(c.IsActive)), ("updated_at", c.UpdatedAt));

        foreach (var i in entities.Inventory)
            await UpsertAsync(tx, "inventory", "variant_id", ("variant_id", i.VariantId), ("quantity", SqlFormat.Money(i.Quantity)), ("low_stock_threshold", i.LowStockThreshold.HasValue ? SqlFormat.Money(i.LowStockThreshold.Value) : null), ("updated_at", i.UpdatedAt));

        foreach (var u in entities.Users)
            await UpsertAsync(tx, "users", "id",
                ("id", u.Id), ("name", u.Name), ("role", u.Role), ("permissions_json", System.Text.Json.JsonSerializer.Serialize(u.Permissions)),
                ("pin_hash", u.PinHash), ("must_change_password", SqlFormat.Bool(u.MustChangePassword)), ("updated_at", u.UpdatedAt));

        // logo_local_path is deliberately NOT in this column list — SyncLogoIfNeededAsync
        // (run after this transaction commits, since it does a network call) owns that
        // column separately, so re-upserting the tenant's text fields on every pull never
        // clobbers an already-cached logo path.
        if (entities.Tenant is { } tenant)
            await UpsertAsync(tx, "tenant", "id", ("id", TenantSingletonId), ("name", tenant.Name), ("logo_url", tenant.LogoUrl),
                ("mpesa_type", tenant.MpesaType), ("mpesa_shortcode", tenant.MpesaShortcode), ("updated_at", tenant.UpdatedAt));

        await tx.CommitAsync(ct);
    }

    // internal: SaleService reuses this for order-table writes too (ON CONFLICT DO UPDATE
    // is harmless for a fresh insert, and gives idempotency for free if a write ever ran twice).
    internal static async Task UpsertAsync(SqliteTransaction tx, string table, string keyColumn, params (string Column, object? Value)[] columns)
    {
        var columnNames = string.Join(", ", columns.Select(c => c.Column));
        var paramNames = string.Join(", ", columns.Select(c => "$" + c.Column));
        var updates = string.Join(", ", columns.Where(c => c.Column != keyColumn).Select(c => $"{c.Column} = excluded.{c.Column}"));

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            INSERT INTO {table} ({columnNames}) VALUES ({paramNames})
            ON CONFLICT({keyColumn}) DO UPDATE SET {updates}
            """;
        foreach (var (column, value) in columns)
            cmd.Parameters.AddWithValue("$" + column, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

}

public sealed record SyncResult(int PagesPulled, long Cursor, DateTimeOffset ServerTime);
public sealed record PushResult(int Created, int Duplicate, int Rejected);
public sealed record CustomerCreateResult(bool Success, string? CustomerId, string? Error);
