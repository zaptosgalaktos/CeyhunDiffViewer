using CeyhunDiffViewer.Core.Yaml;

namespace CeyhunDiffViewer.Core.Diff;

/// <summary>
/// Layer 2: compares two parsed versions of the same asset into a set of semantic
/// changes. Everything is compared as sets keyed by identity (fileId, or
/// target+propertyPath for overrides) so a reordered/shifted YAML block never
/// shows up as spurious add/remove noise.
/// </summary>
public sealed class AssetDiffEngine
{
    private static readonly HashSet<string> SkippedFields = new(StringComparer.Ordinal)
    {
        "m_Component", "m_Children", "serializedVersion", "m_Modifications"
    };

    public AssetDiff Diff(
        string? guid, string assetType, string status,
        string? basePath, string? targetPath,
        YamlAsset? baseAsset, YamlAsset? targetAsset,
        Func<string, string?> guidToPathBase,
        Func<string, string?> guidToPathTarget,
        Func<string, YamlAsset?> assetByGuidBase,
        Func<string, YamlAsset?> assetByGuidTarget)
    {
        var changes = new List<Change>();

        // Whole-asset add/delete: nothing to compare document-by-document.
        if (baseAsset == null || targetAsset == null)
            return new AssetDiff(guid, assetType, status, basePath, targetPath, changes);

        var baseResolver = new ReferenceResolver(baseAsset, guidToPathBase);
        var targetResolver = new ReferenceResolver(targetAsset, guidToPathTarget);

        // 1. Documents added / removed (whole GameObjects, components, instances).
        foreach (var (fileId, document) in targetAsset.Documents)
            if (!baseAsset.Documents.ContainsKey(fileId))
                changes.Add(new Change(ChangeKind.DocumentAdded,
                    MakeObjectRef(targetAsset, document, guidToPathTarget), null, null, null));

        foreach (var (fileId, document) in baseAsset.Documents)
            if (!targetAsset.Documents.ContainsKey(fileId))
                changes.Add(new Change(ChangeKind.DocumentRemoved,
                    MakeObjectRef(baseAsset, document, guidToPathBase), null, null, null));

        // 2. Field / override changes within matched documents.
        var targetTargeting = new TargetResolver(assetByGuidTarget, guidToPathTarget);
        var baseTargeting = new TargetResolver(assetByGuidBase, guidToPathBase);

        foreach (var (fileId, targetDoc) in targetAsset.Documents)
        {
            if (!baseAsset.Documents.TryGetValue(fileId, out var baseDoc)) continue;

            if (targetDoc.ClassId == 1001)
                DiffModifications(baseDoc, targetDoc, targetAsset,
                    baseResolver, targetResolver, guidToPathTarget,
                    baseTargeting, targetTargeting, changes);
            else if (FlowMakerEnricher.Matches(targetDoc))
                FlowMakerEnricher.Diff(baseDoc, targetDoc,
                    MakeObjectRef(targetAsset, targetDoc, guidToPathTarget),
                    baseResolver, targetResolver, changes);
            else
                DiffFields(baseDoc, targetDoc, targetAsset,
                    baseResolver, targetResolver, guidToPathTarget, changes);
        }

        return new AssetDiff(guid, assetType, status, basePath, targetPath, changes);
    }

    private static void DiffFields(
        YamlDocument baseDoc, YamlDocument targetDoc, YamlAsset targetAsset,
        ReferenceResolver baseResolver, ReferenceResolver targetResolver,
        Func<string, string?> guidToPathTarget, List<Change> changes)
    {
        var obj = MakeObjectRef(targetAsset, targetDoc, guidToPathTarget);

        var keys = new HashSet<string>(baseDoc.SimpleValues.Keys, StringComparer.Ordinal);
        keys.UnionWith(targetDoc.SimpleValues.Keys);

        foreach (var key in keys)
        {
            if (SkippedFields.Contains(key)) continue;

            baseDoc.SimpleValues.TryGetValue(key, out var beforeValue);
            targetDoc.SimpleValues.TryGetValue(key, out var afterValue);
            beforeValue ??= "";
            afterValue ??= "";

            if (string.Equals(beforeValue, afterValue, StringComparison.Ordinal)) continue;

            changes.Add(new Change(ChangeKind.FieldChanged, obj, key,
                new ValueRef(beforeValue, baseResolver.Resolve(beforeValue)),
                new ValueRef(afterValue, targetResolver.Resolve(afterValue))));
        }
    }

    private static void DiffModifications(
        YamlDocument baseDoc, YamlDocument targetDoc, YamlAsset targetAsset,
        ReferenceResolver baseResolver, ReferenceResolver targetResolver,
        Func<string, string?> guidToPathTarget,
        TargetResolver baseTargeting, TargetResolver targetTargeting,
        List<Change> changes)
    {
        var obj = MakeObjectRef(targetAsset, targetDoc, guidToPathTarget);

        var baseByKey = Index(baseDoc.GetPrefabModifications());
        var targetByKey = Index(targetDoc.GetPrefabModifications());

        foreach (var (key, target) in targetByKey)
        {
            if (!baseByKey.TryGetValue(key, out var baseMod))
            {
                changes.Add(new Change(ChangeKind.OverrideAdded, obj, target.PropertyPath,
                    null, new ValueRef(target.ComparableValue, targetResolver.Resolve(target.ComparableValue)),
                    targetTargeting.Describe(target)));
            }
            else if (!string.Equals(baseMod.ComparableValue, target.ComparableValue, StringComparison.Ordinal))
            {
                changes.Add(new Change(ChangeKind.OverrideChanged, obj, target.PropertyPath,
                    new ValueRef(baseMod.ComparableValue, baseResolver.Resolve(baseMod.ComparableValue)),
                    new ValueRef(target.ComparableValue, targetResolver.Resolve(target.ComparableValue)),
                    targetTargeting.Describe(target)));
            }
        }

        foreach (var (key, baseMod) in baseByKey)
        {
            if (targetByKey.ContainsKey(key)) continue;
            changes.Add(new Change(ChangeKind.OverrideRemoved, obj, baseMod.PropertyPath,
                new ValueRef(baseMod.ComparableValue, baseResolver.Resolve(baseMod.ComparableValue)), null,
                baseTargeting.Describe(baseMod)));
        }
    }

    private static Dictionary<string, PrefabModification> Index(IEnumerable<PrefabModification> mods)
    {
        var map = new Dictionary<string, PrefabModification>(StringComparer.Ordinal);
        foreach (var mod in mods)
            map[$"{mod.TargetGuid}|{mod.TargetFileId}|{mod.PropertyPath}"] = mod;
        return map;
    }

    private static ObjectRef MakeObjectRef(
        YamlAsset asset, YamlDocument document, Func<string, string?> guidToPath)
    {
        var type = UnityTypes.Name(document.ClassId);
        string? hierarchy = null;
        string? name = null;
        string? location = null;

        if (document.ClassId == 1)
        {
            name = document.GetString("m_Name");
            hierarchy = asset.GetGameObjectHierarchyPath(document.FileId);
        }
        else if (document.ClassId == 1001)
        {
            name = asset.GetDocumentDisplayName(document, guidToPath);
            location = asset.GetPrefabInstanceLocation(document);
        }
        else
        {
            var gameObjectId = document.GetFileId("m_GameObject");
            if (gameObjectId != 0) hierarchy = asset.GetGameObjectHierarchyPath(gameObjectId);
        }

        return new ObjectRef(document.FileId, type, hierarchy, name, location);
    }

    /// <summary>
    /// Describes the sub-object an override targets inside the source prefab. Because
    /// that source may be a variant, the targeted object is often a stripped stub whose
    /// name lives further up the variant chain — so we follow m_CorrespondingSourceObject
    /// into the base prefab until we can name it (script name for MonoBehaviours, else
    /// the owning GameObject's hierarchy).
    /// </summary>
    private sealed class TargetResolver
    {
        private const int MaxDepth = 6;

        private readonly Func<string, YamlAsset?> _assetByGuid;
        private readonly Func<string, string?> _guidToPath;

        public TargetResolver(Func<string, YamlAsset?> assetByGuid, Func<string, string?> guidToPath)
        {
            _assetByGuid = assetByGuid;
            _guidToPath = guidToPath;
        }

        public string? Describe(PrefabModification mod)
        {
            if (mod.TargetFileId == 0 || string.IsNullOrEmpty(mod.TargetGuid)) return null;
            var source = _assetByGuid(mod.TargetGuid);
            return source == null ? null : Describe(source, mod.TargetFileId, 0);
        }

        private string? Describe(YamlAsset asset, long fileId, int depth)
        {
            if (depth > MaxDepth || !asset.TryGetDocument(fileId, out var document)) return null;
            var type = UnityTypes.Name(document.ClassId);

            // MonoBehaviour: the script name is the most useful label.
            if (document.ClassId == 114)
            {
                var scriptGuid = document.GetGuid("m_Script");
                if (!string.IsNullOrEmpty(scriptGuid))
                {
                    var scriptPath = _guidToPath(scriptGuid);
                    if (!string.IsNullOrEmpty(scriptPath))
                        return $"{Path.GetFileNameWithoutExtension(scriptPath)} (MonoBehaviour)";
                }
            }

            // Otherwise use the owning GameObject's hierarchy, if we can name it.
            var gameObjectId = document.ClassId == 1 ? document.FileId : document.GetFileId("m_GameObject");
            if (gameObjectId != 0)
            {
                var owner = asset.GetGameObjectHierarchyPath(gameObjectId);
                if (owner != null && !owner.Contains('<')) return $"{type} on {owner}";
            }

            // Stripped stub in a variant: follow the source object into the base prefab.
            var sourceGuid = document.GetGuid("m_CorrespondingSourceObject");
            var sourceId = document.GetFileId("m_CorrespondingSourceObject");
            if (sourceId != 0 && !string.IsNullOrEmpty(sourceGuid))
            {
                var baseAsset = _assetByGuid(sourceGuid);
                if (baseAsset != null)
                {
                    var resolved = Describe(baseAsset, sourceId, depth + 1);
                    if (resolved != null) return resolved;
                }
            }

            return $"{type} (fileID {fileId})";
        }
    }
}
