namespace Pos.Infra.Storage.Migrations;

/// <summary>
/// Phase 2: local sales tables, mirroring the server's Order/OrderItem/
/// OrderItemModifier/OrderDiscount/Payment/CashDrawerSession fields
/// (server/prisma/schema.prisma) plus two columns the server has no equivalent for —
/// <c>orders.sync_status</c> and <c>orders.op_id</c> — so "how many sales are waiting to
/// sync" is a plain query against <c>orders</c>, with no join against <c>outbox</c>
/// needed. <c>held_tickets</c> has no server counterpart at all (see Cart.cs / the Phase 2
/// plan notes: the server's <c>OrderStatus.PARKED</c> is dead enum surface, never
/// implemented) — holding a sale is purely a local concept here.
/// </summary>
internal static class Migration0002Sales
{
    public const int Version = 2;

    public const string Sql = """
        CREATE TABLE orders (
            id TEXT PRIMARY KEY,
            location_id TEXT NOT NULL,
            cash_drawer_session_id TEXT,
            cashier_id TEXT NOT NULL,
            customer_id TEXT,
            terminal_receipt_no TEXT NOT NULL,
            created_at TEXT NOT NULL,
            subtotal TEXT NOT NULL,
            discount_total TEXT NOT NULL,
            tax_total TEXT NOT NULL,
            total TEXT NOT NULL,
            discount_reason TEXT,
            -- 'PENDING' until a push resolves it; then 'CREATED' | 'DUPLICATE' | 'REJECTED',
            -- the same vocabulary as SyncOpResult.Status (see SyncEngine.PushPendingAsync).
            sync_status TEXT NOT NULL DEFAULT 'PENDING',
            op_id TEXT NOT NULL
        );
        CREATE INDEX idx_orders_sync_status ON orders(sync_status);
        CREATE INDEX idx_orders_op_id ON orders(op_id);

        CREATE TABLE order_items (
            id TEXT PRIMARY KEY,
            order_id TEXT NOT NULL,
            variant_id TEXT NOT NULL,
            product_name TEXT NOT NULL,
            sku TEXT NOT NULL,
            quantity TEXT NOT NULL,
            unit_price TEXT NOT NULL,
            tax_amount TEXT NOT NULL,
            line_total TEXT NOT NULL
        );
        CREATE INDEX idx_order_items_order ON order_items(order_id);

        CREATE TABLE order_item_modifiers (
            id TEXT PRIMARY KEY,
            order_item_id TEXT NOT NULL,
            modifier_id TEXT NOT NULL,
            name TEXT NOT NULL,
            price_adjustment TEXT NOT NULL
        );
        CREATE INDEX idx_order_item_modifiers_item ON order_item_modifiers(order_item_id);

        CREATE TABLE order_discounts (
            id TEXT PRIMARY KEY,
            order_id TEXT NOT NULL,
            applied_by_user_id TEXT NOT NULL,
            amount TEXT NOT NULL,
            reason TEXT
        );
        CREATE INDEX idx_order_discounts_order ON order_discounts(order_id);

        CREATE TABLE payments (
            id TEXT PRIMARY KEY,
            order_id TEXT NOT NULL,
            method TEXT NOT NULL,
            amount TEXT NOT NULL,
            transaction_ref TEXT
        );
        CREATE INDEX idx_payments_order ON payments(order_id);

        CREATE TABLE cash_drawer_sessions (
            id TEXT PRIMARY KEY,
            location_id TEXT NOT NULL,
            opened_by_user_id TEXT NOT NULL,
            opening_float TEXT NOT NULL,
            closing_cash TEXT,
            status TEXT NOT NULL DEFAULT 'OPEN',
            opened_at TEXT NOT NULL,
            closed_at TEXT,
            op_id TEXT NOT NULL,
            close_op_id TEXT
        );
        CREATE INDEX idx_drawer_sessions_status ON cash_drawer_sessions(status);

        -- A held sale is just a serialized Cart (see Pos.Core.Domain.Cart) plus a label —
        -- never pushed anywhere; resuming one deserializes it straight back into the
        -- cashier screen's working cart.
        CREATE TABLE held_tickets (
            id TEXT PRIMARY KEY,
            label TEXT NOT NULL,
            held_at TEXT NOT NULL,
            cart_json TEXT NOT NULL
        );
        """;
}
