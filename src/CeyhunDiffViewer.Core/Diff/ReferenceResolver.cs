using System.Text.RegularExpressions;
using CeyhunDiffViewer.Core.Yaml;

namespace CeyhunDiffViewer.Core.Diff;

/// <summary>
/// Turns a YAML value into something a human can read. A value is either a scalar
/// ("283", "Hello") or an object reference {fileID, guid, type}. References are
/// resolved to an asset path (guid present) or an in-asset hierarchy (same file).
/// </summary>
public sealed class ReferenceResolver
{
    private static readonly Regex RefRegex = new(
        @"\{fileID:\s*(?<fileId>-?\d+)(?:,\s*guid:\s*(?<guid>[a-fA-F0-9]{32}))?(?:,\s*type:\s*(?<type>-?\d+))?\s*\}",
        RegexOptions.Compiled);

    private readonly YamlAsset _asset;
    private readonly Func<string, string?> _guidToPath;

    public ReferenceResolver(YamlAsset asset, Func<string, string?> guidToPath)
    {
        _asset = asset;
        _guidToPath = guidToPath;
    }

    public string Resolve(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "(empty)";

        var match = RefRegex.Match(raw);
        if (!match.Success) return raw.Trim(); // plain scalar

        var fileId = long.Parse(match.Groups["fileId"].Value);
        var guid = match.Groups["guid"].Success ? match.Groups["guid"].Value.ToLowerInvariant() : null;

        if (fileId == 0 && guid == null) return "None";

        if (guid != null)
        {
            var path = _guidToPath(guid);
            var label = path ?? $"guid {guid}";
            return $"{label} (fileID {fileId})";
        }

        // Same-file reference: resolve within this asset.
        if (_asset.TryGetDocument(fileId, out var document))
        {
            var type = UnityTypes.Name(document.ClassId);
            var display = _asset.GetDocumentDisplayName(document, _guidToPath);
            return $"{type} › {display} (fileID {fileId})";
        }

        return $"fileID {fileId} (unresolved)";
    }
}
