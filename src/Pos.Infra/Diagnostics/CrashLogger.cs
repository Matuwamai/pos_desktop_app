using System.Reflection;
using System.Runtime.InteropServices;

namespace Pos.Infra.Diagnostics;

public static class CrashLogger
{
    private const int MaxRetainedFiles = 20;
    private static bool _initialized;
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteAsync("UnhandledException", e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()));

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteAsync("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
    public static void LogHandledException(Exception ex, string context) => WriteAsync(context, ex);

    private static void WriteAsync(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            var path = Path.Combine(AppPaths.LogsDirectory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log");
            var content =
                $"""
                Time (UTC): {DateTime.UtcNow:O}
                Context: {context}
                App version: {version}
                OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})

                {ex}
                """;
            File.WriteAllText(path, content);
            Rotate();
        }
        catch
        {

        }
    }

    private static void Rotate()
    {
        var files = Directory.GetFiles(AppPaths.LogsDirectory, "crash-*.log");
        foreach (var file in FilesToDelete(files)) File.Delete(file);
    }
    internal static IEnumerable<string> FilesToDelete(IEnumerable<string> existingFiles) =>
        existingFiles
            .OrderByDescending(f => f, StringComparer.Ordinal) // the timestamp prefix sorts chronologically
            .Skip(MaxRetainedFiles);
}
