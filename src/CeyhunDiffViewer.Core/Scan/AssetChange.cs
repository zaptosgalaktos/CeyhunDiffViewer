namespace CeyhunDiffViewer.Core.Scan;

public enum AssetStatus
{
    Added,
    Deleted,
    Modified,
    Renamed,
    Replaced
}

/// <summary>One changed asset between two commits, keyed by its stable guid.</summary>
public sealed record AssetChange(
    string? Guid,
    AssetStatus Status,
    string? BasePath,
    string? TargetPath,
    string AssetType,
    bool ContentChanged);
