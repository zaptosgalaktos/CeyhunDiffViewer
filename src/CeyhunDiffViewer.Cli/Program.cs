using CeyhunDiffViewer.Cli.Output;
using CeyhunDiffViewer.Cli.Serve;
using CeyhunDiffViewer.Core;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0])
    {
        case "scan":
            return RunScan(args[1..]);
        case "diff":
            return RunDiff(args[1..]);
        case "serve":
            return ServeCommand.Run(args[1..]);
        case "difftool":
            return DiffToolCommand.Run(args[1..]);
        case "-h" or "--help" or "help":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"unknown command: {args[0]}");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

int RunScan(string[] a)
{
    var (positional, flags) = ParseArgs(a);
    if (positional.Count < 3)
    {
        Console.Error.WriteLine(
            "usage: uadiff scan <repo> <baseRef> <targetRef> [--filter '*.prefab,*.asset'] [--json]");
        return 1;
    }

    var baseRef = positional[1];
    var targetRef = positional[2];
    var filters = (flags.GetValueOrDefault("filter") ?? "*.prefab")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var changes = new DiffService(positional[0]).Scan(baseRef, targetRef, filters);

    if (flags.ContainsKey("json"))
        Console.WriteLine(JsonOutput.Scan(baseRef, targetRef, changes));
    else
        PrettyWriter.WriteScan(baseRef, targetRef, changes);

    return 0;
}

int RunDiff(string[] a)
{
    var (positional, flags) = ParseArgs(a);
    if (positional.Count < 3)
    {
        Console.Error.WriteLine(
            "usage: uadiff diff <repo> <baseRef> <targetRef> (--path <p> | --guid <g>) [--json]");
        return 1;
    }

    flags.TryGetValue("path", out var path);
    flags.TryGetValue("guid", out var guid);
    if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(guid))
    {
        Console.Error.WriteLine("diff requires --path <p> or --guid <g>");
        return 1;
    }

    var diff = new DiffService(positional[0]).Diff(positional[1], positional[2], path, guid);

    if (flags.ContainsKey("json"))
        Console.WriteLine(JsonOutput.Diff(diff));
    else
        PrettyWriter.WriteDiff(diff);

    return 0;
}

static (List<string> Positional, Dictionary<string, string?> Flags) ParseArgs(string[] a)
{
    var positional = new List<string>();
    var flags = new Dictionary<string, string?>(StringComparer.Ordinal);

    for (var i = 0; i < a.Length; i++)
    {
        var token = a[i];
        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var name = token[2..];
            if (name == "json")
                flags[name] = null;
            else if (i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal))
                flags[name] = a[++i];
            else
                flags[name] = null;
        }
        else
        {
            positional.Add(token);
        }
    }

    return (positional, flags);
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        uadiff — semantic diff for Unity YAML assets (read-only, local git)

        Commands:
          scan <repo> <baseRef> <targetRef> [--filter '*.prefab,*.asset'] [--json]
              List assets that changed between two commits (added/deleted/modified/renamed/replaced).

          diff <repo> <baseRef> <targetRef> (--path <assetPath> | --guid <guid>) [--json]
              Semantic diff of one asset: object add/remove, field changes, and
              prefab overrides compared by (target + propertyPath) — no line-shift noise.

          serve [repo] [--port <n>]
              Launch the local web UI (default port 5099) and open it in the browser.

          difftool <leftFile> <rightFile> [--name <repoPath>] [--repo <dir>]
              Render one asset's semantic diff from two extracted file versions and open
              it in the browser. Used as a git/Fork external diff tool.

        Examples:
          uadiff scan . HEAD~1 HEAD
          uadiff diff . HEAD~1 HEAD --path Assets/UI/Popup_Profile.prefab
          uadiff serve .

        Notes:
          - Refs are anything git understands (SHA, branch, tag, FETCH_HEAD).
          - Nothing is written; only `git show/diff/grep` are used, so your working tree is untouched.
        """);
}
