namespace CeyhunDiffViewer.Core;

/// <summary>Maps Unity YAML class IDs to human-readable type names.</summary>
public static class UnityTypes
{
    public static string Name(int classId) => classId switch
    {
        1 => "GameObject",
        4 => "Transform",
        20 => "Camera",
        21 => "Material",
        23 => "MeshRenderer",
        25 => "Renderer",
        33 => "MeshFilter",
        43 => "Mesh",
        54 => "Rigidbody",
        61 => "BoxCollider2D",
        65 => "BoxCollider",
        81 => "AudioListener",
        82 => "AudioSource",
        95 => "Animator",
        114 => "MonoBehaviour",
        115 => "MonoScript",
        120 => "LineRenderer",
        135 => "SphereCollider",
        136 => "CapsuleCollider",
        137 => "SkinnedMeshRenderer",
        198 => "ParticleSystem",
        199 => "ParticleSystemRenderer",
        212 => "SpriteRenderer",
        222 => "CanvasRenderer",
        223 => "Canvas",
        224 => "RectTransform",
        225 => "CanvasGroup",
        1001 => "PrefabInstance",
        _ => $"Unity Object ({classId})"
    };
}
