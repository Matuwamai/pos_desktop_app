using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Infra.Sales;

public static class ReceiptNumberGenerator
{
    public static async Task<string> NextAsync(SqliteConnection connection, SqliteTransaction tx, string terminalId, DateTimeOffset now, CancellationToken ct = default)
    {
        var datePart = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var metaKey = $"receipt_seq_{datePart}";

        await using var selectCmd = connection.CreateCommand();
        selectCmd.Transaction = tx;
        selectCmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        selectCmd.Parameters.AddWithValue("$k", metaKey);
        var existing = await selectCmd.ExecuteScalarAsync(ct);
        var next = existing is null ? 1 : int.Parse((string)existing, CultureInfo.InvariantCulture) + 1;

        await using var upsertCmd = connection.CreateCommand();
        upsertCmd.Transaction = tx;
        upsertCmd.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        upsertCmd.Parameters.AddWithValue("$k", metaKey);
        upsertCmd.Parameters.AddWithValue("$v", next.ToString(CultureInfo.InvariantCulture));
        await upsertCmd.ExecuteNonQueryAsync(ct);

        var terminalPart = terminalId.Length > 8 ? terminalId[..8] : terminalId;
        return $"T{terminalPart}-{datePart}-{next:0000}";
    }
}
