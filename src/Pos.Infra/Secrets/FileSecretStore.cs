using Microsoft.AspNetCore.DataProtection;
using Pos.Core.Interfaces;

namespace Pos.Infra.Secrets;

/// <summary>
/// <see cref="ISecretStore"/> backed by ASP.NET Core Data Protection: each secret is
/// protected (AES, machine-specific key ring under <c>secrets/keys/</c>) and written as
/// its own file under <c>secrets/</c>. Cross-platform and dependency-light, unlike an
/// OS keychain — the trade-off (Phase 5 risk, noted in the plan) is that the key ring
/// itself lives on disk next to the app, so a stolen device plus a stolen disk defeats
/// it; that's why the local database is also encrypted and offline logins have a max
/// window, not relied on alone.
/// </summary>
public sealed class FileSecretStore : ISecretStore
{
    private readonly string _directory;
    private readonly IDataProtector _protector;

    public FileSecretStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_directory, "keys")));
        _protector = provider.CreateProtector("Pos.Desktop.Secrets.v1");
    }

    private string PathFor(string key) => Path.Combine(_directory, SafeFileName(key) + ".bin");

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, ct);
        try
        {
            var plainBytes = _protector.Unprotect(protectedBytes);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Key ring unreadable/rotated out from under it — treat as "not set" rather
            // than crash the caller; they'll re-enroll / regenerate as needed.
            return null;
        }
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var protectedBytes = _protector.Protect(plainBytes);
        await File.WriteAllBytesAsync(PathFor(key), protectedBytes, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private static string SafeFileName(string key) => string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
}
