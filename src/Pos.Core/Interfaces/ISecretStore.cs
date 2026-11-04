namespace Pos.Core.Interfaces;

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
