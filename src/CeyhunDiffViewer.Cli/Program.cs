using CeyhunDiffViewer.Cli.Output;
using CeyhunDiffViewer.Core;
using CeyhunDiffViewer.Core.Diff;
using CeyhunDiffViewer.Core.Git;
using CeyhunDiffViewer.Core.Scan;
using CeyhunDiffViewer.Core.Yaml;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0])
    {
        case "scan":
            return RunScan(args[1..]);
        case "diff":
            return RunDiff(args[1..]);
        case "-h" or "--help" or "help":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"unknown command: {args[0]}");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

int RunScan(string[] a)
{
    var (positional, flags) = ParseArgs(a);
    if (positional.Count < 3)
    {
        Console.Error.WriteLine(
            "usage: uadiff scan <repo> <baseRef> <targetRef> [--filter '*.prefab,*.asset'] [--json]");
        return 1;
    }

    var repo = positional[0];
    var baseRef = positional[1];
    var targetRef = positional[2];
    var filters = (flags.GetValueOrDefault("filter") ?? "*.prefab")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var git = new GitClient(repo);
    var changes = new CommitScanner(git).Scan(baseRef, targetRef, filters);

    if (flags.ContainsKey("json"))
        Console.WriteLine(JsonOutput.Scan(baseRef, targetRef, changes));
    else
        PrettyWriter.WriteScan(baseRef, targetRef, changes);

    return 0;
}

int RunDiff(string[] a)
{
    var (positional, flags) = ParseArgs(a);
    if (positional.Count < 3)
    {
        Console.Error.WriteLine(
            "usage: uadiff diff <repo> <baseRef> <targetRef> (--path <p> | --guid <g>) [--json]");
        return 1;
    }

    var repo = positional[0];
    var baseRef = positional[1];
    var targetRef = positional[2];
    var git = new GitClient(repo);

    flags.TryGetValue("path", out var path);
    flags.TryGetValue("guid", out var guidFlag);

    string? basePath;
    string? targetPath;
    string? guid;

    if (!string.IsNullOrEmpty(path))
    {
        // User points at a path (usually as seen in the target commit / PR).
        targetPath = git.ReadBlob(targetRef, path) != null ? path : null;
        var targetGuid = targetPath != null ? git.ReadAssetGuid(targetRef, targetPath) : null;
        var baseGuidSamePath = git.ReadBlob(baseRef, path) != null ? git.ReadAssetGuid(baseRef, path) : null;

        guid = targetGuid ?? baseGuidSamePath;
        basePath = guid != null
            ? git.FindPathByGuid(baseRef, guid)
            : (git.ReadBlob(baseRef, path) != null ? path : null);
        targetPath ??= guid != null ? git.FindPathByGuid(targetRef, guid) : null;
    }
    else if (!string.IsNullOrEmpty(guidFlag))
    {
        guid = guidFlag.ToLowerInvariant();
        basePath = git.FindPathByGuid(baseRef, guid);
        targetPath = git.FindPathByGuid(targetRef, guid);
    }
    else
    {
        Console.Error.WriteLine("diff requires --path <p> or --guid <g>");
        return 1;
    }

    if (basePath == null && targetPath == null)
    {
        Console.Error.WriteLine("could not locate the asset in either commit.");
        return 1;
    }

    var status = DetermineStatus(basePath, targetPath);
    var assetType = AssetKinds.Of(targetPath ?? basePath ?? "");

    var baseText = basePath != null ? git.ReadBlob(baseRef, basePath) : null;
    var targetText = targetPath != null ? git.ReadBlob(targetRef, targetPath) : null;
    var baseAsset = baseText != null ? YamlAsset.Parse(baseText) : null;
    var targetAsset = targetText != null ? YamlAsset.Parse(targetText) : null;

    var baseCache = new Dictionary<string, string?>(StringComparer.Ordinal);
    var targetCache = new Dictionary<string, string?>(StringComparer.Ordinal);
    string? ResolveBase(string g) => baseCache.TryGetValue(g, out var v) ? v : baseCache[g] = git.FindPathByGuid(baseRef, g);
    string? ResolveTarget(string g) => targetCache.TryGetValue(g, out var v) ? v : targetCache[g] = git.FindPathByGuid(targetRef, g);

    // Load + parse a referenced asset (e.g. a source prefab) by guid, cached per run.
    var baseAssetCache = new Dictionary<string, YamlAsset?>(StringComparer.Ordinal);
    var targetAssetCache = new Dictionary<string, YamlAsset?>(StringComparer.Ordinal);
    YamlAsset? AssetByGuidBase(string g)
    {
        if (baseAssetCache.TryGetValue(g, out var cached)) return cached;
        var p = ResolveBase(g);
        var text = p != null ? git.ReadBlob(baseRef, p) : null;
        return baseAssetCache[g] = text != null ? YamlAsset.Parse(text) : null;
    }
    YamlAsset? AssetByGuidTarget(string g)
    {
        if (targetAssetCache.TryGetValue(g, out var cached)) return cached;
        var p = ResolveTarget(g);
        var text = p != null ? git.ReadBlob(targetRef, p) : null;
        return targetAssetCache[g] = text != null ? YamlAsset.Parse(text) : null;
    }

    var diff = new AssetDiffEngine().Diff(
        guid, assetType, status, basePath, targetPath,
        baseAsset, targetAsset, ResolveBase, ResolveTarget,
        AssetByGuidBase, AssetByGuidTarget);

    if (flags.ContainsKey("json"))
        Console.WriteLine(JsonOutput.Diff(diff));
    else
        PrettyWriter.WriteDiff(diff);

    return 0;
}

static string DetermineStatus(string? basePath, string? targetPath) => (basePath, targetPath) switch
{
    (null, not null) => "added",
    (not null, null) => "deleted",
    (not null, not null) => string.Equals(basePath, targetPath, StringComparison.Ordinal) ? "modified" : "renamed",
    _ => "unknown"
};

static (List<string> Positional, Dictionary<string, string?> Flags) ParseArgs(string[] a)
{
    var positional = new List<string>();
    var flags = new Dictionary<string, string?>(StringComparer.Ordinal);

    for (var i = 0; i < a.Length; i++)
    {
        var token = a[i];
        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var name = token[2..];
            if (name == "json")
                flags[name] = null;
            else if (i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal))
                flags[name] = a[++i];
            else
                flags[name] = null;
        }
        else
        {
            positional.Add(token);
        }
    }

    return (positional, flags);
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        uadiff — semantic diff for Unity YAML assets (read-only, local git)

        Commands:
          scan <repo> <baseRef> <targetRef> [--filter '*.prefab,*.asset'] [--json]
              List assets that changed between two commits (added/deleted/modified/renamed/replaced).

          diff <repo> <baseRef> <targetRef> (--path <assetPath> | --guid <guid>) [--json]
              Semantic diff of one asset: object add/remove, field changes, and
              prefab overrides compared by (target + propertyPath) — no line-shift noise.

        Examples:
          uadiff scan . HEAD~1 HEAD
          uadiff diff . HEAD~1 HEAD --path Assets/UI/Popup_Profile.prefab
          uadiff diff . main pr-branch --guid c910a4271b4de414288a0eca230dbc57 --json

        Notes:
          - Refs are anything git understands (SHA, branch, tag, FETCH_HEAD).
          - Nothing is written; only `git show/diff/grep` are used, so your working tree is untouched.
        """);
}
