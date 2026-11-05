namespace Pos.Infra;

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
