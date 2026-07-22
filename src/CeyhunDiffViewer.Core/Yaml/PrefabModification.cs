namespace CeyhunDiffViewer.Core.Yaml;

/// <summary>A single entry from a PrefabInstance's m_Modifications list.</summary>
public sealed class PrefabModification
{
    public long TargetFileId { get; init; }
    public string TargetGuid { get; init; } = "";
    public string PropertyPath { get; init; } = "";

    /// <summary>Scalar value (e.g. "283", "Hello"). Empty for object-reference overrides.</summary>
    public string Value { get; init; } = "";

    /// <summary>Raw objectReference, e.g. "{fileID: 0}" or "{fileID: 21300000, guid: ..., type: 3}".</summary>
    public string ObjectReference { get; init; } = "";

    /// <summary>The value that actually carries meaning for this override.</summary>
    public string ComparableValue =>
        !string.IsNullOrEmpty(Value) ? Value
        : !string.IsNullOrEmpty(ObjectReference) ? ObjectReference
        : "";
}
