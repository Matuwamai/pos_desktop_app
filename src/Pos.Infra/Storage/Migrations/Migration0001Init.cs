namespace Pos.Infra.Storage.Migrations;

/// <summary>
/// First schema version: the sync cursor/meta table, one table per catalog entity the
/// pull endpoint sends (named after <c>PullEntities</c> in Pos.Core), and the outbox for
/// offline sales/drawer actions not yet pushed. Columns mirror the pull DTOs; money/qty
/// columns are TEXT (SQLite has no native decimal) so they round-trip exactly — never REAL.
/// </summary>
internal static class Migration0001Init
{
    public const int Version = 1;

    public const string Sql = """
        CREATE TABLE meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE locations (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            timezone TEXT NOT NULL,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE categories (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            parent_id TEXT,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE products (
            id TEXT PRIMARY KEY,
            category_id TEXT,
            name TEXT NOT NULL,
            description TEXT,
            is_composite INTEGER NOT NULL,
            track_inventory INTEGER NOT NULL,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL,
            modifier_group_ids_json TEXT NOT NULL DEFAULT '[]'
        );
        CREATE INDEX idx_products_category ON products(category_id);

        CREATE TABLE variants (
            id TEXT PRIMARY KEY,
            product_id TEXT NOT NULL,
            sku TEXT NOT NULL,
            barcode TEXT,
            attributes_json TEXT,
            price TEXT NOT NULL,
            cost TEXT NOT NULL,
            tax_rate_id TEXT,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX idx_variants_product ON variants(product_id);
        CREATE INDEX idx_variants_barcode ON variants(barcode);

        CREATE TABLE modifier_groups (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            min_select INTEGER NOT NULL,
            max_select INTEGER NOT NULL,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE modifiers (
            id TEXT PRIMARY KEY,
            modifier_group_id TEXT NOT NULL,
            name TEXT NOT NULL,
            price_adjustment TEXT NOT NULL,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX idx_modifiers_group ON modifiers(modifier_group_id);

        CREATE TABLE tax_rates (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            rate TEXT NOT NULL,
            is_default INTEGER NOT NULL,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE promotions (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            code TEXT,
            type TEXT NOT NULL,
            value TEXT NOT NULL,
            min_subtotal TEXT,
            starts_at TEXT,
            ends_at TEXT,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE customer_groups (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            discount_percentage TEXT NOT NULL,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE customers (
            id TEXT PRIMARY KEY,
            customer_group_id TEXT,
            name TEXT NOT NULL,
            phone TEXT,
            email TEXT,
            store_credit_balance TEXT NOT NULL,
            is_active INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        -- This terminal's location only (the pull endpoint already scopes it that way).
        CREATE TABLE inventory (
            variant_id TEXT PRIMARY KEY,
            quantity TEXT NOT NULL,
            low_stock_threshold TEXT,
            updated_at TEXT NOT NULL
        );

        -- Offline-login roster: permissions pre-resolved server-side, PIN as a bcrypt
        -- hash only. No password hash ever lands here.
        CREATE TABLE users (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            role TEXT NOT NULL,
            permissions_json TEXT NOT NULL DEFAULT '[]',
            pin_hash TEXT,
            must_change_password INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        -- The outbox: one row per op not yet confirmed by the server. `status` starts
        -- 'PENDING' and becomes 'CREATED'/'DUPLICATE' (both mean: drop it, it is settled)
        -- or 'REJECTED' (kept for the cashier/support to see, not auto-retried as-is).
        CREATE TABLE outbox (
            op_id TEXT PRIMARY KEY,
            type TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            client_time TEXT NOT NULL,
            created_at TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'PENDING',
            result_json TEXT,
            synced_at TEXT
        );
        CREATE INDEX idx_outbox_status ON outbox(status);
        """;
}
