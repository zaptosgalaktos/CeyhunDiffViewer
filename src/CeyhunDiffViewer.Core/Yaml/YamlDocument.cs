using System.Text.RegularExpressions;

namespace CeyhunDiffViewer.Core.Yaml;

/// <summary>
/// One Unity YAML document (a single "--- !u!classId &fileId" block) together with
/// lightweight, regex-based accessors for the fields we care about.
/// </summary>
public sealed class YamlDocument
{
    private static readonly Regex FileIdRegex =
        new(@"fileID:\s*(?<id>-?\d+)", RegexOptions.Compiled);

    private static readonly Regex GuidRegex =
        new(@"guid:\s*(?<guid>[a-fA-F0-9]{32})", RegexOptions.Compiled);

    private readonly Dictionary<string, string> _simpleValues;

    public int ClassId { get; }
    public long FileId { get; }
    public bool IsStripped { get; }
    public int StartLine { get; }
    public string Block { get; }

    public YamlDocument(int classId, long fileId, bool isStripped, int startLine, string block)
    {
        ClassId = classId;
        FileId = fileId;
        IsStripped = isStripped;
        StartLine = startLine;
        Block = block;
        _simpleValues = ParseSimpleValues(block);
    }

    /// <summary>Top-level "key: value" pairs (first occurrence wins).</summary>
    public IReadOnlyDictionary<string, string> SimpleValues => _simpleValues;

    public long GetFileId(string propertyName)
    {
        var match = Regex.Match(
            Block,
            @"^\s*" + Regex.Escape(propertyName) + @":\s*\{[^}]*fileID:\s*(?<id>-?\d+)",
            RegexOptions.Multiline);
        return match.Success && long.TryParse(match.Groups["id"].Value, out var result) ? result : 0;
    }

    public string GetGuid(string propertyName)
    {
        var match = Regex.Match(
            Block,
            @"^\s*" + Regex.Escape(propertyName) + @":\s*\{[^}]*guid:\s*(?<guid>[a-fA-F0-9]{32})",
            RegexOptions.Multiline);
        return match.Success ? match.Groups["guid"].Value.ToLowerInvariant() : "";
    }

    public string GetString(string propertyName)
        => _simpleValues.TryGetValue(propertyName, out var value) ? NormalizeYamlString(value) : "";

    /// <summary>Parses the m_Modifications list (only meaningful on class 1001 documents).</summary>
    public List<PrefabModification> GetPrefabModifications()
    {
        var result = new List<PrefabModification>();
        long targetFileId = 0;
        string targetGuid = "", propertyPath = "", value = "", objRef = "";
        var inEntry = false;

        void Flush()
        {
            if (!inEntry) return;
            result.Add(new PrefabModification
            {
                TargetFileId = targetFileId,
                TargetGuid = targetGuid,
                PropertyPath = propertyPath,
                Value = value,
                ObjectReference = objRef
            });
        }

        foreach (var rawLine in Block.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("- target:", StringComparison.Ordinal))
            {
                Flush();
                inEntry = true;
                targetFileId = ExtractFileId(line);
                targetGuid = ExtractGuid(line);
                propertyPath = value = objRef = "";
                continue;
            }

            if (!inEntry) continue;

            if (line.StartsWith("propertyPath:", StringComparison.Ordinal))
                propertyPath = line["propertyPath:".Length..].Trim();
            else if (line.StartsWith("value:", StringComparison.Ordinal))
                value = line["value:".Length..].Trim();
            else if (line.StartsWith("objectReference:", StringComparison.Ordinal))
                objRef = line["objectReference:".Length..].Trim();
        }

        Flush();
        return result;
    }

    private static long ExtractFileId(string value)
    {
        var match = FileIdRegex.Match(value);
        return match.Success && long.TryParse(match.Groups["id"].Value, out var id) ? id : 0;
    }

    private static string ExtractGuid(string value)
    {
        var match = GuidRegex.Match(value);
        return match.Success ? match.Groups["guid"].Value.ToLowerInvariant() : "";
    }

    private static Dictionary<string, string> ParseSimpleValues(string block)
    {
        var result = new Dictionary<string, string>();

        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.Trim();
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            if (key.StartsWith("- ")) continue;
            if (!result.ContainsKey(key)) result[key] = value;
        }

        return result;
    }

    private static string NormalizeYamlString(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }
}
