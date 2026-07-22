using CeyhunDiffViewer.Core.Diff;
using CeyhunDiffViewer.Core.Scan;

namespace CeyhunDiffViewer.Cli.Output;

public static class PrettyWriter
{
    public static void WriteScan(string baseRef, string targetRef, IReadOnlyList<AssetChange> changes)
    {
        Console.WriteLine($"{baseRef} .. {targetRef}");
        Console.WriteLine(new string('=', 64));

        if (changes.Count == 0)
        {
            Console.WriteLine("No matching asset changes.");
            return;
        }

        foreach (var change in changes)
        {
            var location = change.Status switch
            {
                AssetStatus.Renamed => $"{change.BasePath} → {change.TargetPath}",
                AssetStatus.Replaced => $"{change.BasePath} (guid changed)",
                AssetStatus.Deleted => change.BasePath,
                _ => change.TargetPath
            };

            Console.WriteLine($"[{change.Status,-8}] {change.AssetType,-10} {location}");
            if (change.Guid != null) Console.WriteLine($"             guid {change.Guid}");
        }

        Console.WriteLine(new string('-', 64));
        var counts = changes
            .GroupBy(c => c.Status)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key.ToString().ToLowerInvariant()}: {g.Count()}");
        Console.WriteLine(string.Join("   ", counts));
    }

    public static void WriteDiff(AssetDiff diff)
    {
        Console.WriteLine(new string('=', 64));
        Console.WriteLine($"ASSET:  {diff.TargetPath ?? diff.BasePath}");
        if (diff.BasePath != null && diff.TargetPath != null &&
            !string.Equals(diff.BasePath, diff.TargetPath, StringComparison.Ordinal))
            Console.WriteLine($"WAS:    {diff.BasePath}");
        Console.WriteLine($"GUID:   {diff.Guid}");
        Console.WriteLine($"STATUS: {diff.Status}");
        Console.WriteLine(new string('=', 64));

        if (diff.Changes.Count == 0)
        {
            Console.WriteLine(diff.Status switch
            {
                "added" => "Whole asset added (new file).",
                "deleted" => "Whole asset deleted.",
                _ => "No semantic changes (content identical or metadata-only)."
            });
            return;
        }

        foreach (var change in diff.Changes)
        {
            var where = change.Object.Hierarchy
                        ?? change.Object.Name
                        ?? $"&{change.Object.FileId}";
            var location = change.Object.Location != null ? $"  @ {change.Object.Location}" : "";

            Console.WriteLine();
            Console.WriteLine($"• {Label(change.Kind)}  [{change.Object.Type}]  {where}{location}");
            if (change.Target != null) Console.WriteLine($"    target:   {change.Target}");
            if (change.PropertyPath != null) Console.WriteLine($"    property: {change.PropertyPath}");
            if (change.Before != null) Console.WriteLine($"    before:   {change.Before.Resolved}");
            if (change.After != null) Console.WriteLine($"    after:    {change.After.Resolved}");
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', 64));
        Console.WriteLine($"{diff.Changes.Count} change(s)");
    }

    private static string Label(ChangeKind kind) => kind switch
    {
        ChangeKind.DocumentAdded => "OBJECT ADDED",
        ChangeKind.DocumentRemoved => "OBJECT REMOVED",
        ChangeKind.FieldChanged => "FIELD CHANGED",
        ChangeKind.OverrideAdded => "OVERRIDE ADDED",
        ChangeKind.OverrideRemoved => "OVERRIDE REMOVED",
        ChangeKind.OverrideChanged => "OVERRIDE CHANGED",
        _ => kind.ToString()
    };
}
