namespace CeyhunDiffViewer.Core;

/// <summary>
/// Remembers the last Unity project root the tool was used against. difftool mode
/// (invoked by Fork/git with an unknown working directory) falls back to this so it
/// can still resolve guids without being told the repo path.
/// </summary>
public static class RepoMemory
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".uadiff-repo");

    public static void Save(string repoPath)
    {
        try
        {
            var full = Path.GetFullPath(repoPath);
            if (Directory.Exists(Path.Combine(full, ".git")) || Directory.Exists(Path.Combine(full, "Assets")))
                File.WriteAllText(StorePath, full);
        }
        catch { /* best effort */ }
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return null;
            var path = File.ReadAllText(StorePath).Trim();
            return Directory.Exists(path) ? path : null;
        }
        catch { return null; }
    }
}
