using System.Text.RegularExpressions;

namespace CeyhunDiffViewer.Core.Yaml;

/// <summary>
/// A parsed Unity YAML asset (prefab/scene/.asset): its documents plus the
/// GameObject/Transform graph needed to reconstruct hierarchy paths.
/// Port of the PrefabYamlDatabase logic, decoupled from the Unity Editor.
/// </summary>
public sealed class YamlAsset
{
    private static readonly Regex DocumentHeaderRegex = new(
        @"^--- !u!(?<classId>-?\d+) &(?<fileId>-?\d+)(?<stripped>\s+stripped)?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly Dictionary<long, YamlDocument> _documents;
    private readonly Dictionary<long, long> _transformByGameObject;

    public IReadOnlyDictionary<long, YamlDocument> Documents => _documents;
    public int DocumentCount => _documents.Count;

    private YamlAsset(Dictionary<long, YamlDocument> documents)
    {
        _documents = documents;
        _transformByGameObject = BuildTransformLookup();
    }

    public static YamlAsset Parse(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var matches = DocumentHeaderRegex.Matches(normalized);
        var documents = new Dictionary<long, YamlDocument>();

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var start = match.Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : normalized.Length;
            var block = normalized.Substring(start, end - start);

            if (!int.TryParse(match.Groups["classId"].Value, out var classId)) continue;
            if (!long.TryParse(match.Groups["fileId"].Value, out var fileId)) continue;

            var startLine = CountLinesBefore(normalized, start) + 1;
            documents[fileId] = new YamlDocument(
                classId, fileId, match.Groups["stripped"].Success, startLine, block);
        }

        return new YamlAsset(documents);
    }

    public bool TryGetDocument(long fileId, out YamlDocument document)
        => _documents.TryGetValue(fileId, out document!);

    /// <summary>Walks parents to build a "Root/Panel/Button" style path for a GameObject.</summary>
    public string? GetGameObjectHierarchyPath(long gameObjectId)
    {
        if (gameObjectId == 0) return null;

        var parts = new List<string>();
        var visited = new HashSet<long>();
        var current = gameObjectId;

        while (current != 0 && visited.Add(current))
        {
            if (!TryGetDocument(current, out var go)) break;

            var name = go.GetString("m_Name");
            parts.Add(string.IsNullOrEmpty(name) ? $"<{current}>" : name);

            if (!_transformByGameObject.TryGetValue(current, out var transformId)) break;
            if (!TryGetDocument(transformId, out var transform)) break;

            var fatherId = transform.GetFileId("m_Father");
            if (fatherId == 0) break;
            if (!TryGetDocument(fatherId, out var father)) break;

            current = father.GetFileId("m_GameObject");
        }

        parts.Reverse();
        return parts.Count > 0 ? string.Join("/", parts) : null;
    }

    public string GetDocumentDisplayName(YamlDocument document, Func<string, string?>? guidToPath = null)
    {
        if (document.ClassId == 1)
            return Fallback(document.GetString("m_Name"), "<unnamed GameObject>");

        if (document.ClassId == 1001)
        {
            var sourceGuid = document.GetGuid("m_SourcePrefab");
            var path = guidToPath?.Invoke(sourceGuid);
            return Fallback(path, "<PrefabInstance>");
        }

        var gameObjectId = document.GetFileId("m_GameObject");
        if (gameObjectId != 0)
            return Fallback(GetGameObjectHierarchyPath(gameObjectId), $"GameObject &{gameObjectId}");

        return UnityTypes.Name(document.ClassId);
    }

    /// <summary>Where a PrefabInstance sits in this asset (its parent GameObject's hierarchy path).</summary>
    public string? GetPrefabInstanceLocation(YamlDocument instanceDocument)
    {
        var parentTransformId = instanceDocument.GetFileId("m_TransformParent");
        if (parentTransformId == 0) return "prefab root";
        if (!TryGetDocument(parentTransformId, out var parentTransform)) return null;

        var parentGameObjectId = parentTransform.GetFileId("m_GameObject");
        return GetGameObjectHierarchyPath(parentGameObjectId);
    }

    private Dictionary<long, long> BuildTransformLookup()
    {
        var result = new Dictionary<long, long>();

        foreach (var document in _documents.Values)
        {
            if (document.ClassId != 4 && document.ClassId != 224) continue;

            var gameObjectId = document.GetFileId("m_GameObject");
            if (gameObjectId != 0) result[gameObjectId] = document.FileId;
        }

        return result;
    }

    private static int CountLinesBefore(string text, int characterIndex)
    {
        var count = 0;
        for (var i = 0; i < characterIndex; i++)
            if (text[i] == '\n') count++;
        return count;
    }

    private static string Fallback(string? value, string fallback)
        => string.IsNullOrEmpty(value) ? fallback : value!;
}
