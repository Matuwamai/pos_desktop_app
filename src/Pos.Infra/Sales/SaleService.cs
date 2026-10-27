using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Core.Domain;
using Pos.Core.Pricing;
using Pos.Core.Sync;
using Pos.Infra.Json;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Infra.Sales;

public sealed record SaleResult(string OrderId, string TerminalReceiptNo, OrderTotals Totals);
public sealed record OpenDrawerSession(string Id, decimal OpeningFloat, DateTimeOffset OpenedAt);

/// <summary>
/// The one local-first write path for everything a cashier does that has to reach the
/// server eventually: ring up a sale, open the drawer, close the drawer. Each writes its
/// local rows and an <c>outbox</c> row in the same SQLite transaction — the write
/// succeeds or none of it does, and whether the server is reachable right now never
/// changes what gets written, only when <see cref="SyncEngine.PushPendingAsync"/>
/// actually gets it there (see the Phase 2 plan's "one write path" decision).
/// </summary>
public sealed class SaleService(LocalDatabase db, string terminalId)
{
    public async Task<SaleResult> RingUpSaleAsync(
        Cart cart, string cashierId, string locationId, string? drawerSessionId,
        IReadOnlyList<OfflineOrderPayment> payments, CancellationToken ct = default)
    {
        if (cart.Items.Count == 0) throw new InvalidOperationException("Cannot ring up an empty cart");

        var orderId = Guid.NewGuid().ToString();
        var opId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        await using var tx = db.Connection.BeginTransaction();

        var receiptNo = await ReceiptNumberGenerator.NextAsync(db.Connection, tx, terminalId, now, ct);
        var payload = cart.ToOfflineOrderPayload(orderId, locationId, cashierId, drawerSessionId, receiptNo, now, payments);

        await SyncEngine.UpsertAsync(tx, "orders", "id",
            ("id", orderId), ("location_id", locationId), ("cash_drawer_session_id", drawerSessionId),
            ("cashier_id", cashierId), ("customer_id", cart.CustomerId), ("terminal_receipt_no", receiptNo),
            ("created_at", now.ToString("O")), ("subtotal", SqlFormat.Money(payload.Subtotal)),
            ("discount_total", SqlFormat.Money(payload.DiscountTotal)), ("tax_total", SqlFormat.Money(payload.TaxTotal)),
            ("total", SqlFormat.Money(payload.Total)), ("discount_reason", cart.DiscountReason),
            ("sync_status", "PENDING"), ("op_id", opId));

        foreach (var line in cart.Items)
        {
            var itemId = Guid.NewGuid().ToString();
            var priced = PricingMath.PriceLine(line.BaseUnitPrice, line.ModifiersTotal, line.Quantity, line.TaxRate);
            await SyncEngine.UpsertAsync(tx, "order_items", "id",
                ("id", itemId), ("order_id", orderId), ("variant_id", line.VariantId), ("product_name", line.ProductName),
                ("sku", line.Sku), ("quantity", line.Quantity.ToString(CultureInfo.InvariantCulture)),
                ("unit_price", SqlFormat.Money(line.BaseUnitPrice + line.ModifiersTotal)),
                ("tax_amount", SqlFormat.Money(priced.TaxAmount)), ("line_total", SqlFormat.Money(priced.LineTotal)));

            foreach (var modifier in line.Modifiers)
                await SyncEngine.UpsertAsync(tx, "order_item_modifiers", "id",
                    ("id", Guid.NewGuid().ToString()), ("order_item_id", itemId), ("modifier_id", modifier.Id),
                    ("name", modifier.Name), ("price_adjustment", SqlFormat.Money(modifier.PriceAdjustment)));

            await AdjustInventoryIfTrackedAsync(tx, line.VariantId, line.Quantity, ct);
        }

        if (cart.DiscountAmount.HasValue && cart.DiscountAmount.Value > 0)
            await SyncEngine.UpsertAsync(tx, "order_discounts", "id",
                ("id", Guid.NewGuid().ToString()), ("order_id", orderId), ("applied_by_user_id", cashierId),
                ("amount", SqlFormat.Money(cart.DiscountAmount.Value)), ("reason", cart.DiscountReason));

        foreach (var payment in payments)
            await SyncEngine.UpsertAsync(tx, "payments", "id",
                ("id", Guid.NewGuid().ToString()), ("order_id", orderId), ("method", payment.Method),
                ("amount", SqlFormat.Money(payment.Amount)), ("transaction_ref", payment.TransactionRef));

        await InsertOutboxRowAsync(tx, opId, SyncOpType.OrderCreate, payload, now, ct);

        await tx.CommitAsync(ct);
        // Reuse the totals already computed once for the payload rather than repricing the cart again.
        var totals = new OrderTotals(payload.Subtotal, payload.DiscountTotal, payload.TaxTotal, payload.Total);
        return new SaleResult(orderId, receiptNo, totals);
    }

    public async Task<string> OpenDrawerAsync(string locationId, string cashierId, decimal openingFloat, CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var opId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var payload = new DrawerOpenPayload { Id = sessionId, LocationId = locationId, CashierId = cashierId, OpeningFloat = openingFloat, OpenedAt = now };

        await using var tx = db.Connection.BeginTransaction();
        await SyncEngine.UpsertAsync(tx, "cash_drawer_sessions", "id",
            ("id", sessionId), ("location_id", locationId), ("opened_by_user_id", cashierId),
            ("opening_float", SqlFormat.Money(openingFloat)), ("status", "OPEN"), ("opened_at", now.ToString("O")), ("op_id", opId));
        await InsertOutboxRowAsync(tx, opId, SyncOpType.DrawerOpen, payload, now, ct);
        await tx.CommitAsync(ct);
        return sessionId;
    }

    public async Task CloseDrawerAsync(string sessionId, string cashierId, decimal closingCash, CancellationToken ct = default)
    {
        var opId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var payload = new DrawerClosePayload { Id = sessionId, CashierId = cashierId, ClosingCash = closingCash, ClosedAt = now };

        await using var tx = db.Connection.BeginTransaction();
        await using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE cash_drawer_sessions SET status = 'CLOSED', closing_cash = $cash, closed_at = $closedAt, close_op_id = $opId WHERE id = $id";
            cmd.Parameters.AddWithValue("$cash", SqlFormat.Money(closingCash));
            cmd.Parameters.AddWithValue("$closedAt", now.ToString("O"));
            cmd.Parameters.AddWithValue("$opId", opId);
            cmd.Parameters.AddWithValue("$id", sessionId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await InsertOutboxRowAsync(tx, opId, SyncOpType.DrawerClose, payload, now, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<OpenDrawerSession?> GetOpenDrawerSessionAsync(string locationId, CancellationToken ct = default)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, opening_float, opened_at FROM cash_drawer_sessions WHERE location_id = $loc AND status = 'OPEN' ORDER BY opened_at DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$loc", locationId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new OpenDrawerSession(
            reader.GetString(0),
            decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture));
    }

    private static async Task InsertOutboxRowAsync(SqliteTransaction tx, string opId, string type, object payload, DateTimeOffset now, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload, payload.GetType(), SyncJson.Options);
        await SyncEngine.UpsertAsync(tx, "outbox", "op_id",
            ("op_id", opId), ("type", type), ("payload_json", payloadJson), ("client_time", now.ToString("O")),
            ("created_at", now.ToString("O")), ("status", "PENDING"));
    }

    // A till can sell a product that was never stocked at this location (matches the
    // server's own behaviour in importOfflineOrder): the row is created at zero, then
    // decremented like any other. Allows negative — oversold stock is a warning on the
    // server side, not a local rejection; Phase 2 doesn't surface that warning yet.
    private async Task AdjustInventoryIfTrackedAsync(SqliteTransaction tx, string variantId, int quantity, CancellationToken ct)
    {
        await using var checkCmd = db.Connection.CreateCommand();
        checkCmd.Transaction = tx;
        checkCmd.CommandText = "SELECT p.track_inventory FROM variants v JOIN products p ON p.id = v.product_id WHERE v.id = $id";
        checkCmd.Parameters.AddWithValue("$id", variantId);
        var tracks = await checkCmd.ExecuteScalarAsync(ct);
        if (tracks is null || Convert.ToInt32(tracks, CultureInfo.InvariantCulture) == 0) return;

        var now = DateTimeOffset.UtcNow.ToString("O");
        await using (var ensureCmd = db.Connection.CreateCommand())
        {
            ensureCmd.Transaction = tx;
            ensureCmd.CommandText = "INSERT INTO inventory (variant_id, quantity, updated_at) VALUES ($id, '0', $now) ON CONFLICT(variant_id) DO NOTHING";
            ensureCmd.Parameters.AddWithValue("$id", variantId);
            ensureCmd.Parameters.AddWithValue("$now", now);
            await ensureCmd.ExecuteNonQueryAsync(ct);
        }

        decimal current;
        await using (var selectCmd = db.Connection.CreateCommand())
        {
            selectCmd.Transaction = tx;
            selectCmd.CommandText = "SELECT quantity FROM inventory WHERE variant_id = $id";
            selectCmd.Parameters.AddWithValue("$id", variantId);
            current = decimal.Parse((string)(await selectCmd.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
        }

        await using var updateCmd = db.Connection.CreateCommand();
        updateCmd.Transaction = tx;
        updateCmd.CommandText = "UPDATE inventory SET quantity = $q, updated_at = $now WHERE variant_id = $id";
        updateCmd.Parameters.AddWithValue("$q", SqlFormat.Money(current - quantity));
        updateCmd.Parameters.AddWithValue("$now", now);
        updateCmd.Parameters.AddWithValue("$id", variantId);
        await updateCmd.ExecuteNonQueryAsync(ct);
    }
}
