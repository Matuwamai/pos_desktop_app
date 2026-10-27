using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Infra.Printing;

public static class PrinterTransportKind
{
    public const string None = "NONE";
    public const string Network = "NETWORK";
    public const string Serial = "SERIAL";
    public const string System = "SYSTEM";
}

/// <summary>A terminal's printer configuration — a singleton row in <c>printer_settings</c>.
/// Only the fields for <see cref="Transport"/> are meaningful; the rest sit null/default.</summary>
public sealed record PrinterSettings(
    string Transport,
    string? NetworkHost,
    int? NetworkPort,
    string? SerialPortName,
    int? SerialBaudRate,
    string? SystemPrinterName,
    int CharsPerLine,
    bool OpenDrawerOnCash)
{
    public static readonly PrinterSettings Unconfigured = new(PrinterTransportKind.None, null, null, null, null, null, 48, true);
}

public static class PrinterSettingsStore
{
    private const string SingletonId = "default";

    public static async Task<PrinterSettings> LoadAsync(LocalDatabase db, CancellationToken ct = default)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT transport, network_host, network_port, serial_port_name, serial_baud_rate, system_printer_name, chars_per_line, open_drawer_on_cash FROM printer_settings WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", SingletonId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return PrinterSettings.Unconfigured;

        return new PrinterSettings(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7) != 0);
    }

    public static async Task SaveAsync(LocalDatabase db, PrinterSettings settings, CancellationToken ct = default)
    {
        await using var tx = db.Connection.BeginTransaction();
        await SyncEngine.UpsertAsync(tx, "printer_settings", "id",
            ("id", SingletonId), ("transport", settings.Transport),
            ("network_host", settings.NetworkHost), ("network_port", settings.NetworkPort),
            ("serial_port_name", settings.SerialPortName), ("serial_baud_rate", settings.SerialBaudRate),
            ("system_printer_name", settings.SystemPrinterName),
            ("chars_per_line", settings.CharsPerLine), ("open_drawer_on_cash", settings.OpenDrawerOnCash ? 1 : 0));
        await tx.CommitAsync(ct);
    }
}
