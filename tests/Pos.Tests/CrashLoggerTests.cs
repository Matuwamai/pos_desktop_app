using Pos.Infra.Diagnostics;

namespace Pos.Tests;

public class CrashLoggerTests
{
    [Fact]
    public void FilesToDelete_keeps_only_the_20_most_recent_by_name()
    {
        // The timestamp is encoded in the filename itself (crash-yyyyMMdd-HHmmss-fff.log),
        // so ordinal string order is chronological order — these don't need to be real
        // timestamps for the rule to be exercised correctly.
        var files = Enumerable.Range(1, 25).Select(i => $"crash-{i:D2}.log").ToList();

        var toDelete = CrashLogger.FilesToDelete(files).ToList();

        Assert.Equal(5, toDelete.Count);
        Assert.All(toDelete, f => Assert.True(int.Parse(f[6..8], System.Globalization.CultureInfo.InvariantCulture) <= 5)); // the 5 oldest (01-05)
    }

    [Fact]
    public void FilesToDelete_deletes_nothing_when_at_or_under_the_limit()
    {
        var files = Enumerable.Range(1, 20).Select(i => $"crash-{i:D2}.log").ToList();

        Assert.Empty(CrashLogger.FilesToDelete(files));
    }
}
