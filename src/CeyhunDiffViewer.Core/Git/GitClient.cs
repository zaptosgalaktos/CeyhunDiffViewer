using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CeyhunDiffViewer.Core.Git;

/// <summary>
/// Thin, read-only wrapper over the git CLI. Every method only reads history
/// (show / diff / grep) and never touches the working tree, index, or remotes
/// beyond what the caller already fetched.
/// </summary>
public sealed class GitClient
{
    private static readonly Regex GuidRegex =
        new(@"guid:\s*(?<guid>[a-fA-F0-9]{32})", RegexOptions.Compiled);

    private readonly string _repoPath;

    public GitClient(string repoPath) => _repoPath = repoPath;

    public GitResult Run(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git. Is it on PATH?");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>Reads a blob at &lt;ref&gt;:&lt;path&gt;. Returns null if it does not exist there.</summary>
    public string? ReadBlob(string reference, string path)
    {
        var result = Run("show", $"{reference}:{path}");
        return result.ExitCode == 0 ? result.StdOut : null;
    }

    public IReadOnlyList<NameStatusEntry> DiffNameStatus(
        string baseRef, string targetRef, IReadOnlyList<string> pathspecs)
    {
        var args = new List<string>
        {
            "diff", "--name-status", "--find-renames", $"{baseRef}..{targetRef}", "--"
        };
        args.AddRange(pathspecs);

        var result = Run(args.ToArray());
        var entries = new List<NameStatusEntry>();

        foreach (var line in result.StdOut.Split('\n'))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;

            var code = parts[0];
            switch (code[0])
            {
                case 'R' when parts.Length >= 3:
                    entries.Add(new NameStatusEntry(ChangeStatusCode.Renamed, parts[1], parts[2], ParseScore(code)));
                    break;
                case 'A':
                    entries.Add(new NameStatusEntry(ChangeStatusCode.Added, null, parts[1], null));
                    break;
                case 'D':
                    entries.Add(new NameStatusEntry(ChangeStatusCode.Deleted, parts[1], null, null));
                    break;
                case 'M':
                case 'T': // type change – treat as modification
                    entries.Add(new NameStatusEntry(ChangeStatusCode.Modified, parts[1], parts[1], null));
                    break;
                case 'C' when parts.Length >= 3: // copy
                    entries.Add(new NameStatusEntry(ChangeStatusCode.Added, null, parts[2], null));
                    break;
            }
        }

        return entries;
    }

    /// <summary>Finds the asset whose .meta declares the given guid at a ref (tracks renames by identity).</summary>
    public string? FindPathByGuid(string reference, string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;

        var result = Run("grep", "-l", "-F", "-e", $"guid: {guid}", reference, "--", "*.meta");
        if (result.ExitCode != 0) return null;

        foreach (var line in result.StdOut.Split('\n'))
        {
            if (line.Length == 0) continue;

            // Output format is "<ref>:<path>.meta"; refs never contain ':'.
            var colon = line.IndexOf(':');
            var path = colon >= 0 ? line[(colon + 1)..] : line;

            return path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? path[..^".meta".Length]
                : path;
        }

        return null;
    }

    /// <summary>Reads the guid an asset's .meta declares at a ref.</summary>
    public string? ReadAssetGuid(string reference, string assetPath)
    {
        var meta = ReadBlob(reference, assetPath + ".meta");
        if (meta == null) return null;

        var match = GuidRegex.Match(meta);
        return match.Success ? match.Groups["guid"].Value.ToLowerInvariant() : null;
    }

    private static int? ParseScore(string code)
        => int.TryParse(code.AsSpan(1), out var score) ? score : null;
}

public sealed record GitResult(int ExitCode, string StdOut, string StdErr);
