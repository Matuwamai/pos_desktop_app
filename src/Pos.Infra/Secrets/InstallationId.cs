namespace Pos.Infra.Secrets;

public static class InstallationId
{
    private const string FileName = "installation-id";

    public static string GetOrCreate(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length > 0) return existing;
        }

        var id = Guid.NewGuid().ToString("N")[..12];
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, id);
        return id;
    }
}
