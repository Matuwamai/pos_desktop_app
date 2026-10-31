namespace Pos.Infra;

/// <summary>
/// Where this terminal's local state lives. Defaults to the OS per-user app-data
/// folder (so an installed app never writes into its own program directory); set
/// <c>POS_DESKTOP_DATA_DIR</c> to point it at the repo's gitignored <c>local-data/</c>
/// during development instead.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = ResolveRoot();
    public static string DatabasePath => Path.Combine(DataDirectory, "pos.db");
    public static string SecretsDirectory => Path.Combine(DataDirectory, "secrets");
    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    private static string ResolveRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("POS_DESKTOP_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PosDesktop");
    }
}
