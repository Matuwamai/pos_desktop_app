namespace Pos.Infra.Storage.Migrations;

/// <summary>
/// Phase 3: the two tables printing needs that have no catalog-entity equivalent —
/// both are singletons (always exactly one row, enforced by always writing to the same
/// fixed id, never by a schema constraint — simplest way to model "there is only one of
/// these on this terminal").
/// </summary>
internal static class Migration0003Printer
{
    public const int Version = 3;

    public const string Sql = """
        -- This terminal's own tenant — name/logo for the on-screen and printed receipt.
        -- See services/sync.js: sent in full on every pull, not filtered by "changed
        -- since", so there is nothing to cursor here either; this row is just overwritten.
        CREATE TABLE tenant (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            logo_url TEXT,
            logo_local_path TEXT,
            updated_at TEXT NOT NULL
        );

        -- transport = 'NONE' | 'NETWORK' | 'SERIAL' | 'SYSTEM'. Only the columns for the
        -- selected transport are meaningful; the rest sit NULL.
        CREATE TABLE printer_settings (
            id TEXT PRIMARY KEY,
            transport TEXT NOT NULL DEFAULT 'NONE',
            network_host TEXT,
            network_port INTEGER,
            serial_port_name TEXT,
            serial_baud_rate INTEGER,
            system_printer_name TEXT,
            chars_per_line INTEGER NOT NULL DEFAULT 48,
            open_drawer_on_cash INTEGER NOT NULL DEFAULT 1
        );
        """;
}
