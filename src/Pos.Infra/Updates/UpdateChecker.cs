using Velopack;
using Velopack.Sources;

namespace Pos.Infra.Updates;

/// <summary>
/// Thin wrapper over Velopack's <see cref="UpdateManager"/>, pointed at this repo's own
/// GitHub Releases (confirmed remote: github.com/Matuwamai/pos_desktop_app) — already
/// free, and already where <c>release.yml</c> publishes each tagged build, so this needs
/// no separate hosting decision. Swapping to a different source later (S3, a self-hosted
/// feed) is a one-line change to the <see cref="GithubSource"/> construction below, not a
/// redesign.
/// </summary>
public static class UpdateChecker
{
    private const string RepoUrl = "https://github.com/Matuwamai/pos_desktop_app";

    private static readonly UpdateManager Manager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    private static UpdateInfo? _pending;

    /// <summary>Checks and, if one exists, downloads a new version — never applies it
    /// (that would kill the process mid-shift). Returns whether a downloaded update is
    /// now waiting for <see cref="ApplyAndRestart"/>. Any failure (no network, not an
    /// installed/packaged build, no releases published yet) is swallowed: this is a
    /// best-effort background check, not something that should ever surface as an error
    /// to a cashier.</summary>
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

    /// <summary>Installs the already-downloaded update and restarts the app. Only valid
    /// to call after <see cref="CheckForUpdateAsync"/> returned true in this same
    /// process.</summary>
    public static void ApplyAndRestart()
    {
        if (_pending is { } update) Manager.ApplyUpdatesAndRestart(update);
    }
}
