namespace Glimpse.Core;

/// <summary>Resolves where PNGs + manifest are written: per-repo when in a repo, central otherwise.</summary>
public static class OutputLocator
{
    public const string RepoSubPath = ".claude/tmp/ui-snapshots";
    public const string CentralSubPath = ".claude/ui-snapshots";

    public static string Resolve()
        => Resolve(
            Directory.GetCurrentDirectory(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string Resolve(string startDirectory, string homeDirectory)
    {
        var repoRoot = FindRepoRoot(startDirectory);
        return repoRoot is not null
            ? Path.Combine(repoRoot, RepoSubPath)
            : Path.Combine(homeDirectory, CentralSubPath);
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
            if (Path.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;

        return null;
    }
}
