namespace Pos.Core.Interfaces;

/// <summary>
/// Local-only secret storage: the enrolled device token and the local database's
/// encryption key. Implemented in Pos.Infra with
/// <c>Microsoft.AspNetCore.DataProtection</c> (cross-platform, no OS keychain
/// dependency), writing under the app's <c>secrets/</c> directory — never committed
/// (see <c>.gitignore</c>) and never synced to the server.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>Well-known keys <see cref="ISecretStore"/> is used with.</summary>
public static class SecretKeys
{
    public const string DeviceToken = "device_token";
    public const string DatabaseKey = "database_key";
}
