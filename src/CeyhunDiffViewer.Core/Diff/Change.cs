namespace CeyhunDiffViewer.Core.Diff;

public enum ChangeKind
{
    DocumentAdded,
    DocumentRemoved,
    FieldChanged,
    OverrideAdded,
    OverrideRemoved,
    OverrideChanged
}

/// <summary>The object a change happened on, resolved to human-readable form.</summary>
/// <param name="Location">For a PrefabInstance: where it sits in the containing asset.</param>
public sealed record ObjectRef(long FileId, string Type, string? Hierarchy, string? Name, string? Location = null);

/// <summary>A value both as raw YAML and resolved to a name (asset path / hierarchy).</summary>
public sealed record ValueRef(string Raw, string Resolved);

public sealed record Change(
    ChangeKind Kind,
    ObjectRef Object,
    string? PropertyPath,
    ValueRef? Before,
    ValueRef? After,
    string? Target = null);

/// <summary>Layer 2 result: the semantic diff of a single asset.</summary>
public sealed record AssetDiff(
    string? Guid,
    string AssetType,
    string Status,
    string? BasePath,
    string? TargetPath,
    IReadOnlyList<Change> Changes);
