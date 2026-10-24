using Microsoft.Data.Sqlite;
using Pos.Infra.Storage.Migrations;

namespace Pos.Infra.Storage;

/// <summary>
/// Owns the one encrypted SQLite file a terminal keeps. Opens it with the SQLCipher
/// bundle (<c>PRAGMA key</c>, set once per connection, before anything else touches
/// the file) and brings the schema up to date via <c>PRAGMA user_version</c> — plain
/// SQL migrations in <see cref="Migrations"/>, applied in order, never re-run.
/// </summary>
public sealed class LocalDatabase : IAsyncDisposable
{
    private static readonly (int Version, string Sql)[] Migrations =
    [
        (Migration0001Init.Version, Migration0001Init.Sql),
    ];

    private readonly SqliteConnection _connection;

    private LocalDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public SqliteConnection Connection => _connection;

    public static async Task<LocalDatabase> OpenAsync(string path, string encryptionKey, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync(ct);

        // Must be the very first statement on this connection — SQLCipher needs the
        // key before any page is read, including the schema check migrations run next.
        await ExecuteAsync(connection, $"PRAGMA key = '{EscapeForPragma(encryptionKey)}';", ct);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", ct);

        var db = new LocalDatabase(connection);
        await db.MigrateAsync(ct);
        return db;
    }

    private async Task MigrateAsync(CancellationToken ct)
    {
        var current = await GetUserVersionAsync(ct);
        foreach (var (version, sql) in Migrations)
        {
            if (version <= current) continue;
            await using var tx = _connection.BeginTransaction();
            await ExecuteAsync(_connection, sql, ct, tx);
            await ExecuteAsync(_connection, $"PRAGMA user_version = {version};", ct, tx);
            await tx.CommitAsync(ct);
        }
    }

    private async Task<int> GetUserVersionAsync(CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct, SqliteTransaction? tx = null)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // SqliteConnectionStringBuilder has no slot for the SQLCipher key, and the key
    // itself is the one place in this app where SQL is built by string interpolation —
    // guard against a key containing a stray quote breaking the PRAGMA statement.
    private static string EscapeForPragma(string key) => key.Replace("'", "''");

    public ValueTask DisposeAsync()
    {
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
