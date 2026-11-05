using Velopack;
using Velopack.Sources;

namespace Pos.Infra.Updates;

public static class UpdateChecker
{
    private const string RepoUrl = "https://github.com/Matuwamai/pos_desktop_app";

    private static readonly UpdateManager Manager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    private static UpdateInfo? _pending;
    public static async Task<bool> CheckForUpdateAsync()
    {
        try
        {
            if (!Manager.IsInstalled) return false; // e.g. running via `dotnet run` in development

            var newVersion = await Manager.CheckForUpdatesAsync();
            if (newVersion is null) return false;

            await Manager.DownloadUpdatesAsync(newVersion);
            _pending = newVersion;
            return true;
        }
        catch
        {
            return false;
        }
    }
    public static void ApplyAndRestart()
    {
        if (_pending is { } update) Manager.ApplyUpdatesAndRestart(update);
    }
}
