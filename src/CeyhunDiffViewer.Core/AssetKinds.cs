namespace CeyhunDiffViewer.Core;

/// <summary>Classifies a Unity asset by file extension.</summary>
public static class AssetKinds
{
    public static string Of(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".prefab" => "prefab",
            ".unity" => "scene",
            ".asset" => "asset",
            ".mat" => "material",
            ".controller" => "animatorController",
            _ => string.IsNullOrEmpty(ext) ? "unknown" : ext.TrimStart('.')
        };
    }
}
