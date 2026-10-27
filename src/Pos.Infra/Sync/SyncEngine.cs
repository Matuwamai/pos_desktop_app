using System.Globalization;
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

    public async Task<SyncResult> PullAllAsync(string deviceToken, CancellationToken ct = default)
    {
        var since = await GetCursorAsync(ct);
        var pages = 0;
        PullResponse last;
        do
        {
            last = await api.PullAsync(deviceToken, since, ct: ct);
            await ApplyAsync(last.Entities, ct);
            since = last.NextSince;
            await SetCursorAsync(since, ct);
            pages++;
        } while (last.HasMore);

        var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(last.ServerTime);
        await SetMetaAsync(LastSyncedKey, serverTime.ToString("O"), ct);
        return new SyncResult(pages, since, serverTime);
    }

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
            await UpsertAsync(tx, "locations", "id", ("id", l.Id), ("name", l.Name), ("timezone", l.Timezone), ("is_active", Bool(l.IsActive)), ("updated_at", l.UpdatedAt));

        foreach (var c in entities.Categories)
            await UpsertAsync(tx, "categories", "id", ("id", c.Id), ("name", c.Name), ("parent_id", c.ParentId), ("is_active", Bool(c.IsActive)), ("updated_at", c.UpdatedAt));

        foreach (var p in entities.Products)
            await UpsertAsync(tx, "products", "id",
                ("id", p.Id), ("category_id", p.CategoryId), ("name", p.Name), ("description", p.Description),
                ("is_composite", Bool(p.IsComposite)), ("track_inventory", Bool(p.TrackInventory)), ("is_active", Bool(p.IsActive)),
                ("updated_at", p.UpdatedAt), ("modifier_group_ids_json", System.Text.Json.JsonSerializer.Serialize(p.ModifierGroupIds)));

        foreach (var v in entities.Variants)
            await UpsertAsync(tx, "variants", "id",
                ("id", v.Id), ("product_id", v.ProductId), ("sku", v.Sku), ("barcode", v.Barcode),
                ("attributes_json", v.Attributes.ValueKind == System.Text.Json.JsonValueKind.Undefined ? null : v.Attributes.GetRawText()),
                ("price", Money(v.Price)), ("cost", v.Cost.HasValue ? Money(v.Cost.Value) : null), ("tax_rate_id", v.TaxRateId),
                ("is_active", Bool(v.IsActive)), ("updated_at", v.UpdatedAt));

        foreach (var g in entities.ModifierGroups)
            await UpsertAsync(tx, "modifier_groups", "id", ("id", g.Id), ("name", g.Name), ("min_select", g.MinSelect), ("max_select", g.MaxSelect), ("is_active", Bool(g.IsActive)), ("updated_at", g.UpdatedAt));

        foreach (var m in entities.Modifiers)
            await UpsertAsync(tx, "modifiers", "id", ("id", m.Id), ("modifier_group_id", m.ModifierGroupId), ("name", m.Name), ("price_adjustment", Money(m.PriceAdjustment)), ("is_active", Bool(m.IsActive)), ("updated_at", m.UpdatedAt));

        foreach (var t in entities.TaxRates)
            await UpsertAsync(tx, "tax_rates", "id", ("id", t.Id), ("name", t.Name), ("rate", Money(t.Rate)), ("is_default", Bool(t.IsDefault)), ("is_active", Bool(t.IsActive)), ("updated_at", t.UpdatedAt));

        foreach (var p in entities.Promotions)
            await UpsertAsync(tx, "promotions", "id",
                ("id", p.Id), ("name", p.Name), ("code", p.Code), ("type", p.Type), ("value", Money(p.Value)),
                ("min_subtotal", p.MinSubtotal.HasValue ? Money(p.MinSubtotal.Value) : null), ("starts_at", p.StartsAt), ("ends_at", p.EndsAt),
                ("is_active", Bool(p.IsActive)), ("updated_at", p.UpdatedAt));

        foreach (var g in entities.CustomerGroups)
            await UpsertAsync(tx, "customer_groups", "id", ("id", g.Id), ("name", g.Name), ("discount_percentage", Money(g.DiscountPercentage)), ("is_active", Bool(g.IsActive)), ("updated_at", g.UpdatedAt));

        foreach (var c in entities.Customers)
            await UpsertAsync(tx, "customers", "id",
                ("id", c.Id), ("customer_group_id", c.CustomerGroupId), ("name", c.Name), ("phone", c.Phone), ("email", c.Email),
                ("store_credit_balance", Money(c.StoreCreditBalance)), ("is_active", Bool(c.IsActive)), ("updated_at", c.UpdatedAt));

        foreach (var i in entities.Inventory)
            await UpsertAsync(tx, "inventory", "variant_id", ("variant_id", i.VariantId), ("quantity", Money(i.Quantity)), ("low_stock_threshold", i.LowStockThreshold.HasValue ? Money(i.LowStockThreshold.Value) : null), ("updated_at", i.UpdatedAt));

        foreach (var u in entities.Users)
            await UpsertAsync(tx, "users", "id",
                ("id", u.Id), ("name", u.Name), ("role", u.Role), ("permissions_json", System.Text.Json.JsonSerializer.Serialize(u.Permissions)),
                ("pin_hash", u.PinHash), ("must_change_password", Bool(u.MustChangePassword)), ("updated_at", u.UpdatedAt));

        await tx.CommitAsync(ct);
    }

    private static async Task UpsertAsync(SqliteTransaction tx, string table, string keyColumn, params (string Column, object? Value)[] columns)
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

    private static int Bool(bool value) => value ? 1 : 0;

    // Invariant-culture, full precision — this is the only place a decimal becomes
    // text, and it must round-trip exactly back through decimal.Parse.
    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

public sealed record SyncResult(int PagesPulled, long Cursor, DateTimeOffset ServerTime);
