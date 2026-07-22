using CeyhunDiffViewer.Core.Diff;
using CeyhunDiffViewer.Core.Git;
using CeyhunDiffViewer.Core.Scan;
using CeyhunDiffViewer.Core.Yaml;

namespace CeyhunDiffViewer.Core;

public sealed record GitRef(string Kind, string Value, string? Label);

/// <summary>
/// Front-end-agnostic entry point that wires GitClient + CommitScanner +
/// AssetDiffEngine together. Both the CLI and the web UI use this.
/// </summary>
public sealed class DiffService
{
    private readonly GitClient _git;

    public DiffService(string repoPath)
    {
        _git = new GitClient(repoPath);
        RepoMemory.Save(repoPath);
    }

    public IReadOnlyList<AssetChange> Scan(
        string baseRef, string targetRef, IReadOnlyList<string> filters)
        => new CommitScanner(_git).Scan(baseRef, targetRef, filters);

    public AssetDiff Diff(string baseRef, string targetRef, string? path, string? guid)
    {
        string? basePath;
        string? targetPath;
        string? resolvedGuid;

        if (!string.IsNullOrEmpty(path))
        {
            targetPath = _git.ReadBlob(targetRef, path) != null ? path : null;
            var targetGuid = targetPath != null ? _git.ReadAssetGuid(targetRef, targetPath) : null;
            var baseGuidSamePath = _git.ReadBlob(baseRef, path) != null ? _git.ReadAssetGuid(baseRef, path) : null;

            resolvedGuid = targetGuid ?? baseGuidSamePath;
            basePath = resolvedGuid != null
                ? _git.FindPathByGuid(baseRef, resolvedGuid)
                : (_git.ReadBlob(baseRef, path) != null ? path : null);
            targetPath ??= resolvedGuid != null ? _git.FindPathByGuid(targetRef, resolvedGuid) : null;
        }
        else if (!string.IsNullOrEmpty(guid))
        {
            resolvedGuid = guid.ToLowerInvariant();
            basePath = _git.FindPathByGuid(baseRef, resolvedGuid);
            targetPath = _git.FindPathByGuid(targetRef, resolvedGuid);
        }
        else
        {
            throw new ArgumentException("A path or guid is required.");
        }

        if (basePath == null && targetPath == null)
            throw new InvalidOperationException("Asset not found in either commit.");

        var status = DetermineStatus(basePath, targetPath);
        var assetType = AssetKinds.Of(targetPath ?? basePath ?? "");

        var baseText = basePath != null ? _git.ReadBlob(baseRef, basePath) : null;
        var targetText = targetPath != null ? _git.ReadBlob(targetRef, targetPath) : null;
        var baseAsset = baseText != null ? YamlAsset.Parse(baseText) : null;
        var targetAsset = targetText != null ? YamlAsset.Parse(targetText) : null;

        var baseCache = new Dictionary<string, string?>(StringComparer.Ordinal);
        var targetCache = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? ResolveBase(string g) => baseCache.TryGetValue(g, out var v) ? v : baseCache[g] = _git.FindPathByGuid(baseRef, g);
        string? ResolveTarget(string g) => targetCache.TryGetValue(g, out var v) ? v : targetCache[g] = _git.FindPathByGuid(targetRef, g);

        var baseAssetCache = new Dictionary<string, YamlAsset?>(StringComparer.Ordinal);
        var targetAssetCache = new Dictionary<string, YamlAsset?>(StringComparer.Ordinal);
        YamlAsset? AssetByGuidBase(string g)
        {
            if (baseAssetCache.TryGetValue(g, out var cached)) return cached;
            var p = ResolveBase(g);
            var text = p != null ? _git.ReadBlob(baseRef, p) : null;
            return baseAssetCache[g] = text != null ? YamlAsset.Parse(text) : null;
        }
        YamlAsset? AssetByGuidTarget(string g)
        {
            if (targetAssetCache.TryGetValue(g, out var cached)) return cached;
            var p = ResolveTarget(g);
            var text = p != null ? _git.ReadBlob(targetRef, p) : null;
            return targetAssetCache[g] = text != null ? YamlAsset.Parse(text) : null;
        }

        return new AssetDiffEngine().Diff(
            resolvedGuid, assetType, status, basePath, targetPath,
            baseAsset, targetAsset, ResolveBase, ResolveTarget,
            AssetByGuidBase, AssetByGuidTarget);
    }

    /// <summary>Branches, tags, and recent commits for ref pickers.</summary>
    public IReadOnlyList<GitRef> ListRefs(int commitLimit = 50)
    {
        var refs = new List<GitRef>();

        var named = _git.Run("for-each-ref", "--sort=-creatordate",
            "--format=%(refname)%09%(refname:short)", "refs/heads", "refs/remotes", "refs/tags");
        foreach (var line in named.StdOut.Split('\n'))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var kind = parts[0].StartsWith("refs/tags/", StringComparison.Ordinal) ? "tag" : "branch";
            refs.Add(new GitRef(kind, parts[1], null));
        }

        var log = _git.Run("log", $"-{commitLimit}", "--format=%h%x09%s");
        foreach (var line in log.StdOut.Split('\n'))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t', 2);
            refs.Add(new GitRef("commit", parts[0], parts.Length > 1 ? parts[1] : null));
        }

        return refs;
    }

    private static string DetermineStatus(string? basePath, string? targetPath) => (basePath, targetPath) switch
    {
        (null, not null) => "added",
        (not null, null) => "deleted",
        (not null, not null) => string.Equals(basePath, targetPath, StringComparison.Ordinal) ? "modified" : "renamed",
        _ => "unknown"
    };
}
