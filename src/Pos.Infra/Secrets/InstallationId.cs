namespace Pos.Infra.Secrets;

/// <summary>
/// A short id generated once per local install and persisted alongside the other local
/// state, used as part of <c>Terminal.deviceIdentifier</c> — which is a hard, *global*
/// <c>@unique</c> constraint in the server schema (server/prisma/schema.prisma), not
/// tenant-scoped.
///
/// Deriving the identifier purely from machine name + OS (the original approach) collides
/// on every re-enrollment from the same physical machine — after a crash, a wiped data
/// directory, or a reinstall, the Setup wizard would 409 against the orphaned terminal row
/// from the previous install, with no way to recover short of an admin deleting that row.
/// Persisting a generated id instead means: the same install reuses the same identifier
/// (retrying Setup after a failed attempt still matches), and a wiped install reasonably
/// looks like a new device to the server — consistent with it having no local state at
/// all, including no enrollment to lose. Cleaning up the orphaned terminal row left behind
/// is then an ordinary admin action (deactivate/revoke on the Terminals page), not a
/// database operation.
/// </summary>
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
