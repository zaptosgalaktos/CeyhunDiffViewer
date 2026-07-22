using System.Diagnostics;
using CeyhunDiffViewer.Cli.Output;
using CeyhunDiffViewer.Core;
using CeyhunDiffViewer.Core.Diff;
using CeyhunDiffViewer.Core.Yaml;

namespace CeyhunDiffViewer.Cli.Serve;

/// <summary>
/// difftool mode: git / Fork hand us two already-extracted file versions (LOCAL, REMOTE)
/// plus optionally the real repo-relative path. We diff them, resolve guids against the
/// project on disk, render a self-contained HTML page and open it.
/// </summary>
public static class DiffToolCommand
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "uadiff-difftool.log");

    public static int Run(string[] args)
    {
        Log($"invoked  cwd={Directory.GetCurrentDirectory()}  args=[{string.Join(" | ", args)}]");
        try
        {
            return RunCore(args);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            return 1;
        }
    }

    private static int RunCore(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (positional.Count < 2)
        {
            Log("usage error: need two file paths");
            Console.Error.WriteLine("usage: uadiff difftool <leftFile> <rightFile> [--name <repoPath>] [--repo <dir>]");
            return 1;
        }

        var name = GetOption(args, "--name");
        var repo = GetOption(args, "--repo");

        var baseText = ReadIfPresent(positional[0]);
        var targetText = ReadIfPresent(positional[1]);
        var baseAsset = baseText != null ? YamlAsset.Parse(baseText) : null;
        var targetAsset = targetText != null ? YamlAsset.Parse(targetText) : null;

        var root = FindRepoRoot(repo);
        Log($"repoRoot={root}");
        var fs = new FileSystemAssets(root);
        Func<string, string?> guidToPath = fs.GuidToPath;
        Func<string, YamlAsset?> assetByGuid = fs.AssetByGuid;

        var displayName = name ?? Path.GetFileName(positional[1]);
        var status = baseAsset == null ? "added" : targetAsset == null ? "deleted" : "modified";
        var assetType = AssetKinds.Of(name ?? positional[1]);

        var diff = new AssetDiffEngine().Diff(
            null, assetType, status, name, name,
            baseAsset, targetAsset, guidToPath, guidToPath, assetByGuid, assetByGuid);

        var html = WebUi.StaticPage(JsonOutput.Diff(diff), displayName);
        var outPath = Path.Combine(Path.GetTempPath(), $"uadiff-{Guid.NewGuid():N}.html");
        File.WriteAllText(outPath, html);
        Log($"wrote {outPath}; opening");
        OpenBrowser(outPath);
        return 0;
    }

    private static string? ReadIfPresent(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/dev/null" || !File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string FindRepoRoot(string? explicitRepo)
    {
        if (!string.IsNullOrEmpty(explicitRepo) && Directory.Exists(explicitRepo))
        {
            RepoMemory.Save(explicitRepo);
            return explicitRepo;
        }

        var detected = DetectUpward(Directory.GetCurrentDirectory());
        if (detected != null)
        {
            RepoMemory.Save(detected);
            return detected;
        }

        var remembered = RepoMemory.Load();
        if (remembered != null)
        {
            Log($"using remembered repo {remembered}");
            return remembered;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? DetectUpward(string start)
    {
        var dir = start;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "Assets")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static string? GetOption(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }

    private static void OpenBrowser(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log("open failed: " + ex.Message + "  (file: " + path + ")");
            Console.WriteLine(path);
        }
    }

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n"); }
        catch { /* ignore */ }
    }
}
