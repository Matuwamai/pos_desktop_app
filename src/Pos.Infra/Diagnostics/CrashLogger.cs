using System.Reflection;
using System.Runtime.InteropServices;

namespace Pos.Infra.Diagnostics;

/// <summary>
/// A hand-written file logger for crashes and other silently-swallowed failures — not a
/// logging framework, matching this codebase's existing preference for small, dependency-
/// free code (native <c>fetch</c> on the server, a hand-written ESC/POS encoder) over a
/// library for something this narrow. Before this existed, an unhandled exception or a
/// startup failure left nothing behind for a support conversation to point at beyond "the
/// window was blank."
/// </summary>
public static class CrashLogger
{
    private const int MaxRetainedFiles = 20;
    private static bool _initialized;

    /// <summary>Must be called before anything else in <c>Program.Main</c> (after
    /// Velopack's own required first call) so even a crash during Avalonia's own startup
    /// is caught.</summary>
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

    /// <summary>For a failure that's already being handled (caught and recovered from)
    /// but is still worth a record of — e.g. a startup failure that falls back to the
    /// Setup screen instead of crashing outright.</summary>
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
            // A logger that itself throws (disk full, permissions) must never take down
            // the process it's trying to document the death of.
        }
    }

    private static void Rotate()
    {
        var files = Directory.GetFiles(AppPaths.LogsDirectory, "crash-*.log");
        foreach (var file in FilesToDelete(files)) File.Delete(file);
    }

    /// <summary>The retention rule itself, factored out as a pure function (no
    /// filesystem access) so it's unit-testable without writing real files or touching
    /// AppPaths.LogsDirectory.</summary>
    internal static IEnumerable<string> FilesToDelete(IEnumerable<string> existingFiles) =>
        existingFiles
            .OrderByDescending(f => f, StringComparer.Ordinal) // the timestamp prefix sorts chronologically
            .Skip(MaxRetainedFiles);
}
