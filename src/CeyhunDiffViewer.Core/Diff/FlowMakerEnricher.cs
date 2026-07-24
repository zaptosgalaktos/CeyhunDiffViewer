using System.Text.RegularExpressions;
using CeyhunDiffViewer.Core.Yaml;

namespace CeyhunDiffViewer.Core.Diff;

/// <summary>
/// Domain-aware diff for CastleRoyale's FlowMaker component. The engine calls this for
/// any MonoBehaviour whose m_Script is FlowMaker; it walks the nested flowSteps list,
/// matches steps by their flowElement identity (so inserting/removing a step doesn't
/// produce shift noise), translates stepType to its enum name, and resolves references.
/// </summary>
public static class FlowMakerEnricher
{
    public const string ScriptGuid = "98bf9a91568194d91a84544bdf14b8e3";

    // Index == serialized integer value.
    private static readonly string[] StepTypeNames =
    {
        "Animation", "Delay", "SetEnable", "EmitParticle", "PlayFlow", "VoidMethod", "PlaySound",
        "StopSound", "Silence", "Animator", "AnimState", "StringMethod", "IntMethod", "FloatMethod",
        "BoolMethod", "StopFlow", "WaitClick", "ScreenRequest", "KillParticle", "StopParticle"
    };

    private static readonly HashSet<string> TopLevelFields = new(StringComparer.Ordinal)
        { "m_Enabled", "m_Name", "autoPlay", "autoPlayWarmupFrames" };

    private static readonly HashSet<string> RefFields = new(StringComparer.Ordinal)
        { "flowElement", "targetFlowObject", "flowToStop", "targetScript", "soundClip" };

    private static readonly HashSet<string> BoolFields = new(StringComparer.Ordinal)
        { "loop", "awaitSelection", "useUnscaledTime", "setEnable", "emitParticle", "boolParam", "animatorBoolValue" };

    // Index fields that select an entry from a sibling name list — shown as the name.
    private static readonly Dictionary<string, string> IndexToList = new(StringComparer.Ordinal)
    {
        ["selectedAnimationIndex"] = "animationClips",
        ["selectedAnimatorParamIndex"] = "animatorParamNames",
        ["selectedAnimatorStateIndex"] = "animatorStateNames",
        ["selectedMethodIndex"] = "methodNames"
    };

    private static readonly Dictionary<string, string> FriendlyName = new(StringComparer.Ordinal)
    {
        ["selectedAnimationIndex"] = "animation",
        ["selectedAnimatorParamIndex"] = "animatorParam",
        ["selectedAnimatorStateIndex"] = "animatorState",
        ["selectedMethodIndex"] = "method"
    };

    public static bool Matches(YamlDocument document)
        => document.ClassId == 114 &&
           string.Equals(document.GetGuid("m_Script"), ScriptGuid, StringComparison.OrdinalIgnoreCase);

    public static void Diff(
        YamlDocument baseDoc, YamlDocument targetDoc, ObjectRef obj,
        ReferenceResolver baseResolver, ReferenceResolver targetResolver, List<Change> changes)
    {
        DiffTopLevel(baseDoc, targetDoc, obj, changes);
        DiffSteps(baseDoc, targetDoc, obj, baseResolver, targetResolver, changes);
    }

    // --- top-level component fields (autoPlay, warmup, enabled, name) ---

    private static void DiffTopLevel(YamlDocument baseDoc, YamlDocument targetDoc, ObjectRef obj, List<Change> changes)
    {
        var b = TopLevel(baseDoc.Block);
        var t = TopLevel(targetDoc.Block);

        foreach (var key in TopLevelFields)
        {
            b.TryGetValue(key, out var bv);
            t.TryGetValue(key, out var tv);
            bv ??= "";
            tv ??= "";
            if (string.Equals(bv, tv, StringComparison.Ordinal)) continue;

            changes.Add(new Change(ChangeKind.FieldChanged, obj, key,
                new ValueRef(bv, bv.Length == 0 ? "(none)" : bv),
                new ValueRef(tv, tv.Length == 0 ? "(none)" : tv)));
        }
    }

    private static Dictionary<string, string> TopLevel(string block)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in block.Split('\n'))
        {
            var match = Regex.Match(line, @"^  ([A-Za-z_]\w*):\s?(.*)$");
            if (!match.Success) continue; // exactly two spaces, not a list item
            var key = match.Groups[1].Value;
            if (!result.ContainsKey(key)) result[key] = match.Groups[2].Value.Trim();
        }
        return result;
    }

    // --- flowSteps list ---

    private static void DiffSteps(
        YamlDocument baseDoc, YamlDocument targetDoc, ObjectRef obj,
        ReferenceResolver baseResolver, ReferenceResolver targetResolver, List<Change> changes)
    {
        var baseSteps = ParseSteps(baseDoc.Block);
        var targetSteps = ParseSteps(targetDoc.Block);
        var baseBySig = Group(baseSteps);
        var targetBySig = Group(targetSteps);

        var signatures = new HashSet<string>(baseBySig.Keys, StringComparer.Ordinal);
        signatures.UnionWith(targetBySig.Keys);

        foreach (var sig in signatures)
        {
            var baseList = baseBySig.GetValueOrDefault(sig) ?? new List<Step>();
            var targetList = targetBySig.GetValueOrDefault(sig) ?? new List<Step>();
            var paired = Math.Min(baseList.Count, targetList.Count);

            for (var k = 0; k < paired; k++)
                DiffStepPair(baseList[k], targetList[k], obj, baseResolver, targetResolver, changes);

            for (var k = paired; k < targetList.Count; k++)
                changes.Add(StepChange(ChangeKind.StepAdded, targetList[k], obj, targetResolver));

            for (var k = paired; k < baseList.Count; k++)
                changes.Add(StepChange(ChangeKind.StepRemoved, baseList[k], obj, baseResolver));
        }

        DetectReorder(baseSteps, targetSteps, obj, targetResolver, changes);
    }

    private static void DetectReorder(
        List<Step> baseSteps, List<Step> targetSteps, ObjectRef obj,
        ReferenceResolver resolver, List<Change> changes)
    {
        var baseOrder = baseSteps.Select(s => s.FlowElement).ToList();
        var targetOrder = targetSteps.Select(s => s.FlowElement).ToList();

        if (baseOrder.SequenceEqual(targetOrder)) return;   // order unchanged
        if (!SameMultiset(baseOrder, targetOrder)) return;  // steps added/removed — reported elsewhere
        // Only reliable when every step has a distinct, non-zero flowElement to key on.
        if (baseOrder.Any(e => e == "0")) return;
        if (baseOrder.Distinct(StringComparer.Ordinal).Count() != baseOrder.Count) return;

        var baseIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < baseOrder.Count; i++) baseIndex[baseOrder[i]] = i;

        var seq = new int[targetOrder.Count];
        for (var i = 0; i < targetOrder.Count; i++) seq[i] = baseIndex[targetOrder[i]];

        var stay = LongestIncreasingSubsequence(seq);
        for (var j = 0; j < targetSteps.Count; j++)
        {
            if (stay.Contains(j)) continue;
            var step = targetSteps[j];
            changes.Add(new Change(ChangeKind.StepMoved, obj, StepLabel(step, resolver),
                new ValueRef("", $"position {baseIndex[step.FlowElement] + 1}"),
                new ValueRef("", $"position {j + 1}")));
        }
    }

    private static bool SameMultiset(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in a) counts[x] = counts.GetValueOrDefault(x) + 1;
        foreach (var x in b)
        {
            if (!counts.TryGetValue(x, out var c) || c == 0) return false;
            counts[x] = c - 1;
        }
        return true;
    }

    private static HashSet<int> LongestIncreasingSubsequence(int[] seq)
    {
        var keep = new HashSet<int>();
        var n = seq.Length;
        if (n == 0) return keep;

        var tailValue = new List<int>();
        var tailIndex = new List<int>();
        var prev = new int[n];

        for (var i = 0; i < n; i++)
        {
            int lo = 0, hi = tailValue.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (tailValue[mid] < seq[i]) lo = mid + 1; else hi = mid;
            }
            prev[i] = lo > 0 ? tailIndex[lo - 1] : -1;
            if (lo == tailValue.Count) { tailValue.Add(seq[i]); tailIndex.Add(i); }
            else { tailValue[lo] = seq[i]; tailIndex[lo] = i; }
        }

        var k = tailIndex.Count > 0 ? tailIndex[^1] : -1;
        while (k != -1) { keep.Add(k); k = prev[k]; }
        return keep;
    }

    private static void DiffStepPair(
        Step b, Step t, ObjectRef obj,
        ReferenceResolver baseResolver, ReferenceResolver targetResolver, List<Change> changes)
    {
        var label = StepLabel(t, targetResolver);

        var keys = new HashSet<string>(b.Fields.Keys, StringComparer.Ordinal);
        keys.UnionWith(t.Fields.Keys);

        foreach (var key in keys)
        {
            b.Fields.TryGetValue(key, out var bv);
            t.Fields.TryGetValue(key, out var tv);
            bv ??= "";
            tv ??= "";
            if (string.Equals(bv, tv, StringComparison.Ordinal)) continue;

            changes.Add(new Change(ChangeKind.FieldChanged, obj, FriendlyName.GetValueOrDefault(key) ?? key,
                new ValueRef(bv, Translate(key, bv, b, baseResolver)),
                new ValueRef(tv, Translate(key, tv, t, targetResolver)), label));
        }

        var baseClips = b.Lists.GetValueOrDefault("animationClips") ?? new List<string>();
        var targetClips = t.Lists.GetValueOrDefault("animationClips") ?? new List<string>();

        foreach (var clip in targetClips.Except(baseClips, StringComparer.Ordinal))
            changes.Add(new Change(ChangeKind.FieldChanged, obj, "animationClips",
                null, new ValueRef(clip, "added '" + clip + "'"), label));

        foreach (var clip in baseClips.Except(targetClips, StringComparer.Ordinal))
            changes.Add(new Change(ChangeKind.FieldChanged, obj, "animationClips",
                new ValueRef(clip, "removed '" + clip + "'"), null, label));
    }

    private static Change StepChange(ChangeKind kind, Step step, ObjectRef obj, ReferenceResolver resolver)
    {
        var value = new ValueRef("", StepLabel(step, resolver));
        return kind == ChangeKind.StepAdded
            ? new Change(kind, obj, null, null, value)
            : new Change(kind, obj, null, value, null);
    }

    private static string Translate(string key, string value, Step step, ReferenceResolver resolver)
    {
        if (key == "stepType" && int.TryParse(value, out var idx) && idx >= 0 && idx < StepTypeNames.Length)
            return StepTypeNames[idx];
        if (RefFields.Contains(key))
            return resolver.Resolve(value);
        if (IndexToList.TryGetValue(key, out var listName) && int.TryParse(value, out var ci)
            && step.Lists.TryGetValue(listName, out var options) && ci >= 0 && ci < options.Count)
            return options[ci];
        if (BoolFields.Contains(key))
            return value == "1" ? "true" : value == "0" ? "false" : value;
        return value;
    }

    private static string StepLabel(Step step, ReferenceResolver resolver)
    {
        var type = "Step";
        if (step.Fields.TryGetValue("stepType", out var st) && int.TryParse(st, out var i) &&
            i >= 0 && i < StepTypeNames.Length)
            type = StepTypeNames[i];

        if (step.FlowElement != "0" && step.Fields.TryGetValue("flowElement", out var fe))
            return $"{type} → {ShortName(resolver.Resolve(fe))}";
        return type;
    }

    private static string ShortName(string resolved)
    {
        var s = resolved;
        var arrow = s.IndexOf("› ", StringComparison.Ordinal);
        if (arrow >= 0) s = s[(arrow + 2)..];
        var paren = s.LastIndexOf(" (fileID", StringComparison.Ordinal);
        if (paren >= 0) s = s[..paren];
        var slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        return s;
    }

    private static Dictionary<string, List<Step>> Group(List<Step> steps)
    {
        var map = new Dictionary<string, List<Step>>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (!map.TryGetValue(step.FlowElement, out var list))
                map[step.FlowElement] = list = new List<Step>();
            list.Add(step);
        }
        return map;
    }

    private static List<Step> ParseSteps(string block)
    {
        var lines = block.Split('\n');
        var steps = new List<Step>();

        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("  ", StringComparison.Ordinal) && lines[i].Trim() == "flowSteps:")
            {
                start = i;
                break;
            }
        }
        if (start < 0) return steps;

        Step? current = null;
        string? listKey = null;

        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;

            var indent = Indent(line);
            var trimmed = line.TrimStart();

            if (indent == 2 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                current = new Step();
                steps.Add(current);
                listKey = null;
                AddField(current, trimmed[2..], ref listKey);
                continue;
            }

            if (indent < 4) break; // dedented out of the flowSteps list
            if (current == null) continue;

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (listKey != null)
                {
                    if (!current.Lists.TryGetValue(listKey, out var items))
                        current.Lists[listKey] = items = new List<string>();
                    items.Add(trimmed[2..].Trim());
                }
                continue;
            }

            AddField(current, trimmed, ref listKey);
        }

        return steps;
    }

    private static void AddField(Step step, string content, ref string? listKey)
    {
        var colon = content.IndexOf(':');
        if (colon <= 0) return;

        var key = content[..colon].Trim();
        var value = content[(colon + 1)..].Trim();

        if (value.Length == 0)
        {
            listKey = key; // a nested list/map header
            return;
        }

        listKey = null;
        if (!step.Fields.ContainsKey(key)) step.Fields[key] = value;
        if (key == "flowElement") step.FlowElement = ExtractFileId(value);
    }

    private static string ExtractFileId(string value)
    {
        var match = Regex.Match(value, @"fileID:\s*(-?\d+)");
        return match.Success ? match.Groups[1].Value : "0";
    }

    private static int Indent(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    private sealed class Step
    {
        public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Lists { get; } = new(StringComparer.Ordinal);
        public string FlowElement { get; set; } = "0";
    }
}
