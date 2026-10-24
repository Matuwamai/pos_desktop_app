using System.Text.Json;
using Pos.Core.Interfaces;

namespace Pos.Infra.Secrets;

/// <summary>
/// The enrollment record a terminal needs to skip the Setup wizard on every launch:
/// the server URL, which terminal this device *is*, and its device token (the token
/// itself goes through <see cref="ISecretStore"/>; everything else is not secret and
/// is stored alongside it as plain JSON under <see cref="AppPaths.SecretsDirectory"/>).
/// </summary>
public sealed record DeviceEnrollment(string ServerUrl, string TerminalId, string LocationId, string TenantSubdomain);

public sealed class DeviceSessionStore(ISecretStore secrets, string directory)
{
    private string EnrollmentPath => Path.Combine(directory, "enrollment.json");

    public async Task<(DeviceEnrollment Enrollment, string DeviceToken)?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(EnrollmentPath)) return null;
        var token = await secrets.GetAsync(SecretKeys.DeviceToken, ct);
        if (token is null) return null;

        var json = await File.ReadAllTextAsync(EnrollmentPath, ct);
        var enrollment = JsonSerializer.Deserialize<DeviceEnrollment>(json);
        return enrollment is null ? null : (enrollment, token);
    }

    public async Task SaveAsync(DeviceEnrollment enrollment, string deviceToken, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(EnrollmentPath, JsonSerializer.Serialize(enrollment), ct);
        await secrets.SetAsync(SecretKeys.DeviceToken, deviceToken, ct);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(EnrollmentPath)) File.Delete(EnrollmentPath);
        await secrets.DeleteAsync(SecretKeys.DeviceToken, ct);
    }
}
