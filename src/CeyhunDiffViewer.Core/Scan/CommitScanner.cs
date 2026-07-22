using CeyhunDiffViewer.Core.Git;

namespace CeyhunDiffViewer.Core.Scan;

/// <summary>
/// Layer 1: lists the assets that changed between two commits and classifies each
/// (added / deleted / modified / renamed / replaced) using guid identity.
/// </summary>
public sealed class CommitScanner
{
    private readonly GitClient _git;

    public CommitScanner(GitClient git) => _git = git;

    public IReadOnlyList<AssetChange> Scan(
        string baseRef, string targetRef, IReadOnlyList<string> pathspecs)
    {
        var entries = _git.DiffNameStatus(baseRef, targetRef, pathspecs);
        var result = new List<AssetChange>();

        foreach (var entry in entries)
        {
            var assetType = AssetKinds.Of(entry.NewPath ?? entry.OldPath ?? "");

            switch (entry.Status)
            {
                case ChangeStatusCode.Added:
                    result.Add(new AssetChange(
                        _git.ReadAssetGuid(targetRef, entry.NewPath!),
                        AssetStatus.Added, null, entry.NewPath, assetType, true));
                    break;

                case ChangeStatusCode.Deleted:
                    result.Add(new AssetChange(
                        _git.ReadAssetGuid(baseRef, entry.OldPath!),
                        AssetStatus.Deleted, entry.OldPath, null, assetType, true));
                    break;

                case ChangeStatusCode.Modified:
                {
                    var baseGuid = _git.ReadAssetGuid(baseRef, entry.OldPath!);
                    var targetGuid = _git.ReadAssetGuid(targetRef, entry.NewPath!);
                    var status = Differ(baseGuid, targetGuid) ? AssetStatus.Replaced : AssetStatus.Modified;
                    result.Add(new AssetChange(targetGuid ?? baseGuid, status,
                        entry.OldPath, entry.NewPath, assetType, true));
                    break;
                }

                case ChangeStatusCode.Renamed:
                {
                    var baseGuid = _git.ReadAssetGuid(baseRef, entry.OldPath!);
                    var targetGuid = _git.ReadAssetGuid(targetRef, entry.NewPath!);
                    var status = Differ(baseGuid, targetGuid) ? AssetStatus.Replaced : AssetStatus.Renamed;
                    var contentChanged = entry.Score is null or < 100;
                    result.Add(new AssetChange(targetGuid ?? baseGuid, status,
                        entry.OldPath, entry.NewPath, assetType, contentChanged));
                    break;
                }
            }
        }

        return result;
    }

    private static bool Differ(string? a, string? b)
        => a != null && b != null && !string.Equals(a, b, StringComparison.Ordinal);
}
