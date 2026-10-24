using Microsoft.Data.Sqlite;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Infra.Auth;

public sealed record RosterUser(string Id, string Name, string Role, IReadOnlyList<string> Permissions, bool MustChangePassword);

public enum PinLoginResult
{
    Success,
    UserNotFound,
    WrongPin,
    MustChangePasswordOnline,
    OfflineWindowExpired,
}

/// <summary>
/// Verifies a cashier's PIN against the bcrypt hash pulled down in the roster
/// (<c>services/sync.js</c> sends <c>pinHash</c>, never a password hash) and enforces
/// the max-offline-window policy from the Phase 0/1 plan: once this device hasn't
/// completed a pull in <see cref="MaxOfflineWindow"/>, PIN login is refused — the
/// cashier must get the device online (a pull, which also refreshes the roster and
/// catches a revoked/deactivated account) before it will unlock again.
/// </summary>
public sealed class PinVerifier(LocalDatabase db, SyncEngine sync)
{
    public static readonly TimeSpan MaxOfflineWindow = TimeSpan.FromDays(7);

    public async Task<(PinLoginResult Result, RosterUser? User)> TryLoginAsync(string userId, string pin, CancellationToken ct = default)
    {
        var lastSyncedAt = await sync.GetLastSyncedAtAsync(ct);
        if (lastSyncedAt is null || DateTimeOffset.UtcNow - lastSyncedAt.Value > MaxOfflineWindow)
            return (PinLoginResult.OfflineWindowExpired, null);

        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, role, permissions_json, pin_hash, must_change_password FROM users WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (PinLoginResult.UserNotFound, null);

        var pinHash = reader.IsDBNull(4) ? null : reader.GetString(4);
        var mustChangePassword = reader.GetInt32(5) != 0;
        var permissions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [];
        var user = new RosterUser(reader.GetString(0), reader.GetString(1), reader.GetString(2), permissions, mustChangePassword);

        // Mirrors services/offlineSync.js's assertCashier: a forced password change
        // can only be satisfied online, so offline PIN login is refused until then.
        if (mustChangePassword) return (PinLoginResult.MustChangePasswordOnline, user);
        if (string.IsNullOrEmpty(pinHash) || !BCrypt.Net.BCrypt.Verify(pin, pinHash)) return (PinLoginResult.WrongPin, user);

        return (PinLoginResult.Success, user);
    }

    public async Task<List<RosterUser>> ListActiveUsersAsync(CancellationToken ct = default)
    {
        var results = new List<RosterUser>();
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, role, permissions_json, must_change_password FROM users ORDER BY name";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var permissions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [];
            results.Add(new RosterUser(reader.GetString(0), reader.GetString(1), reader.GetString(2), permissions, reader.GetInt32(4) != 0));
        }
        return results;
    }
}
