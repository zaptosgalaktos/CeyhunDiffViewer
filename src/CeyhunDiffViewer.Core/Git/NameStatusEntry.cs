namespace CeyhunDiffViewer.Core.Git;

public enum ChangeStatusCode
{
    Added,
    Deleted,
    Modified,
    Renamed
}

/// <summary>One line of `git diff --name-status -M` output.</summary>
public sealed record NameStatusEntry(
    ChangeStatusCode Status,
    string? OldPath,
    string? NewPath,
    int? Score);
