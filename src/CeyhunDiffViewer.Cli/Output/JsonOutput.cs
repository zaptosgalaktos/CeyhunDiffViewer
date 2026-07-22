using System.Text.Json;
using System.Text.Json.Serialization;
using CeyhunDiffViewer.Core.Diff;
using CeyhunDiffViewer.Core.Scan;

namespace CeyhunDiffViewer.Cli.Output;

/// <summary>Canonical machine-readable output. UI / CLI / tooling all consume this.</summary>
public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Scan(string baseRef, string targetRef, IReadOnlyList<AssetChange> changes)
    {
        var payload = new
        {
            baseRef,
            targetRef,
            counts = changes
                .GroupBy(c => c.Status.ToString().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count()),
            assets = changes.Select(c => new
            {
                guid = c.Guid,
                assetType = c.AssetType,
                status = c.Status.ToString().ToLowerInvariant(),
                basePath = c.BasePath,
                targetPath = c.TargetPath,
                contentChanged = c.ContentChanged
            })
        };

        return JsonSerializer.Serialize(payload, Options);
    }

    public static string Diff(AssetDiff diff)
    {
        var payload = new
        {
            guid = diff.Guid,
            assetType = diff.AssetType,
            status = diff.Status,
            basePath = diff.BasePath,
            targetPath = diff.TargetPath,
            changes = diff.Changes.Select(ch => new
            {
                kind = ToCamel(ch.Kind),
                @object = new
                {
                    fileId = ch.Object.FileId,
                    type = ch.Object.Type,
                    hierarchy = ch.Object.Hierarchy,
                    name = ch.Object.Name,
                    location = ch.Object.Location
                },
                target = ch.Target,
                propertyPath = ch.PropertyPath,
                before = ch.Before is null ? null : (object)new { raw = ch.Before.Raw, resolved = ch.Before.Resolved },
                after = ch.After is null ? null : (object)new { raw = ch.After.Raw, resolved = ch.After.Resolved }
            })
        };

        return JsonSerializer.Serialize(payload, Options);
    }

    private static string ToCamel(ChangeKind kind)
    {
        var s = kind.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }
}
