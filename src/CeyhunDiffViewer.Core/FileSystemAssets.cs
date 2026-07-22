using System.Text.RegularExpressions;
using CeyhunDiffViewer.Core.Yaml;

namespace CeyhunDiffViewer.Core;

/// <summary>
/// Resolves guids against a Unity project on disk. Used by difftool mode, which is
/// handed two extracted file versions with no surrounding git refs, but still needs
/// to name referenced assets (source prefabs, scripts, sprites).
/// </summary>
public sealed class FileSystemAssets
{
    private static readonly Regex GuidLine =
        new(@"^guid:\s*(?<guid>[a-fA-F0-9]{32})", RegexOptions.Compiled);

    private readonly string _root;
    private Dictionary<string, string>? _guidToAssetPath;

    public FileSystemAssets(string root) => _root = root;

    public string? GuidToPath(string guid)
    {
        EnsureIndex();
        return _guidToAssetPath!.TryGetValue(guid, out var abs)
            ? Path.GetRelativePath(_root, abs)
            : null;
    }

    public YamlAsset? AssetByGuid(string guid)
    {
        EnsureIndex();
        if (!_guidToAssetPath!.TryGetValue(guid, out var abs) || !File.Exists(abs)) return null;
        try { return YamlAsset.Parse(File.ReadAllText(abs)); }
        catch { return null; }
    }

    private void EnsureIndex()
    {
        if (_guidToAssetPath != null) return;
        _guidToAssetPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scanRoot in ScanRoots())
        {
            IEnumerable<string> metas;
            try { metas = Directory.EnumerateFiles(scanRoot, "*.meta", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var meta in metas)
            {
                try
                {
                    foreach (var line in File.ReadLines(meta))
                    {
                        var match = GuidLine.Match(line);
                        if (!match.Success) continue;
                        _guidToAssetPath[match.Groups["guid"].Value.ToLowerInvariant()] =
                            meta[..^".meta".Length];
                        break;
                    }
                }
                catch { /* unreadable .meta — skip */ }
            }
        }
    }

    private IEnumerable<string> ScanRoots()
    {
        var assets = Path.Combine(_root, "Assets");
        var packages = Path.Combine(_root, "Packages");
        if (Directory.Exists(assets)) yield return assets;
        if (Directory.Exists(packages)) yield return packages;
        // If neither exists, index nothing. Never scan an arbitrary (possibly huge) root
        // like "/" — that would hang. Guids simply stay unresolved instead.
    }
}
