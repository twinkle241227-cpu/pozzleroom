using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Shows a URP outline around a photo while its collider is hovered.</summary>
[DisallowMultipleComponent]
public sealed class PhotoHoverOutline : MonoBehaviour
{
    private static readonly HashSet<PhotoHoverOutline> RegisteredPhotos = new HashSet<PhotoHoverOutline>();
    private static readonly HashSet<PhotoHoverOutline> HighlightedPhotos = new HashSet<PhotoHoverOutline>();
    private static PhotoHoverOutline currentHighlighted;
    private static bool outlinesEnabledForGameplay = true;
    [SerializeField] private Color outlineColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField, Min(0.00001f)] private float outlineWidth = 0.001f;

    private readonly List<OutlineMesh> outlineMeshes = new List<OutlineMesh>();
    private Material outlineMaterial;
    private bool isCorrectState;
    private Color normalOutlineColor;
    private float normalOutlineWidth;

    private sealed class OutlineMesh
    {
        public Transform Transform;
        public Renderer Renderer;
        public Vector3 MeshSize;
    }

    private void Awake()
    {
        RegisteredPhotos.Add(this);
        normalOutlineColor = outlineColor;
        normalOutlineWidth = outlineWidth;
        BuildOutlineMeshes();
        ApplyStyle();
        SetOutlineVisible(false);
    }

    private void OnDisable()
    {
        SetOutlineVisible(false);
        HighlightedPhotos.Remove(this);
        if (currentHighlighted == this) currentHighlighted = null;
    }

    private void OnDestroy()
    {
        HighlightedPhotos.Remove(this);
        RegisteredPhotos.Remove(this);
        if (currentHighlighted == this) currentHighlighted = null;
    }

    public void Configure(Color color, float width)
    {
        normalOutlineColor = color;
        normalOutlineWidth = Mathf.Max(0.00001f, width);
        if (!isCorrectState)
        {
            outlineColor = normalOutlineColor;
            outlineWidth = normalOutlineWidth;
            ApplyStyle();
        }
    }

    public string GetDebugDescription()
    {
        StringBuilder generatedMeshes = new StringBuilder();
        foreach (OutlineMesh outlineMesh in outlineMeshes)
        {
            if (generatedMeshes.Length > 0) generatedMeshes.Append(", ");
            generatedMeshes.Append(outlineMesh.Transform.name);
        }

        return $"photo={GetPath(transform)}, outlineColor={outlineColor}, width={outlineWidth:F5}, " +
            $"generatedRuntimeMeshes=[{generatedMeshes}]";
    }

    public static string GetHighlightedDebugDescriptions()
    {
        if (HighlightedPhotos.Count == 0)
        {
            return "none";
        }

        StringBuilder result = new StringBuilder();
        foreach (PhotoHoverOutline photo in HighlightedPhotos)
        {
            if (photo == null) continue;
            if (result.Length > 0) result.Append(" || ");
            result.Append(photo.GetDebugDescription());
        }

        return result.ToString();
    }

    public static IEnumerable<PhotoHoverOutline> GetRegisteredPhotos()
    {
        return RegisteredPhotos;
    }

    public static void SetHighlighted(PhotoHoverOutline next)
    {
        if (currentHighlighted == next)
        {
            return;
        }

        if (currentHighlighted != null)
        {
            if (!currentHighlighted.isCorrectState)
            {
                currentHighlighted.SetOutlineVisible(false);
            }
            HighlightedPhotos.Remove(currentHighlighted);
        }

        currentHighlighted = next;
        if (currentHighlighted != null)
        {
            currentHighlighted.SetOutlineVisible(true);
            HighlightedPhotos.Add(currentHighlighted);
            Debug.Log($"[PhotoHoverDebug] Selected single hover target {currentHighlighted.GetDebugDescription()}");
        }
    }

    /// <summary>Hides every outline outside the photo-wall interaction view.</summary>
    public static void SetGameplayOutlineVisibility(bool visible)
    {
        if (outlinesEnabledForGameplay == visible) return;
        outlinesEnabledForGameplay = visible;
        foreach (PhotoHoverOutline outline in Resources.FindObjectsOfTypeAll<PhotoHoverOutline>())
        {
            if (outline == null || !outline.gameObject.scene.IsValid()) continue;
            outline.SetOutlineVisible(visible && outline.isCorrectState);
        }
    }

    public int GetStableHoverPriority()
    {
        return transform.GetSiblingIndex();
    }

    /// <summary>Keeps a green correctness outline visible without blocking interaction.</summary>
    public void SetCorrectState(Color color)
    {
        isCorrectState = true;
        outlineColor = color;
        ApplyStyle();
        SetOutlineVisible(true);
    }

    public void ClearCorrectState()
    {
        if (!isCorrectState) return;
        isCorrectState = false;
        outlineColor = normalOutlineColor;
        outlineWidth = normalOutlineWidth;
        ApplyStyle();
        SetOutlineVisible(false);
    }

    private void BuildOutlineMeshes()
    {
        Shader shader = Shader.Find("PhotoWall/HoverOutline");
        if (shader == null)
        {
            Debug.LogError("Photo-wall hover outline shader was not found.", this);
            return;
        }

        outlineMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        foreach (MeshFilter sourceFilter in GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null || sourceFilter.sharedMesh == null)
            {
                continue;
            }

            GameObject outlineObject = new GameObject(sourceFilter.name + " Hover Outline");
            outlineObject.hideFlags = HideFlags.DontSave;
            outlineObject.transform.SetParent(sourceFilter.transform, false);
            MeshFilter outlineFilter = outlineObject.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMaterial;
            outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineMeshes.Add(new OutlineMesh
            {
                Transform = outlineObject.transform,
                Renderer = outlineRenderer,
                MeshSize = sourceFilter.sharedMesh.bounds.size
            });
        }
    }

    private void ApplyStyle()
    {
        if (outlineMaterial == null)
        {
            return;
        }

        outlineMaterial.SetColor("_OutlineColor", outlineColor);
        outlineMaterial.SetFloat("_OutlineWidth", outlineWidth);
        foreach (OutlineMesh outlineMesh in outlineMeshes)
        {
            outlineMesh.Transform.localScale = GetOutlineScale(outlineMesh.MeshSize);
        }
    }

    private void SetOutlineVisible(bool visible)
    {
        foreach (OutlineMesh outlineMesh in outlineMeshes)
        {
            Renderer outlineRenderer = outlineMesh.Renderer;
            if (outlineRenderer != null)
            {
                outlineRenderer.enabled = visible && outlinesEnabledForGameplay;
            }
        }
    }

    private Vector3 GetOutlineScale(Vector3 meshSize)
    {
        int normalAxis = meshSize.x <= meshSize.y && meshSize.x <= meshSize.z ? 0 :
            meshSize.y <= meshSize.z ? 1 : 2;
        Vector3 scale = Vector3.one;
        if (normalAxis != 0) scale.x += outlineWidth / Mathf.Max(0.00001f, meshSize.x);
        if (normalAxis != 1) scale.y += outlineWidth / Mathf.Max(0.00001f, meshSize.y);
        if (normalAxis != 2) scale.z += outlineWidth / Mathf.Max(0.00001f, meshSize.z);
        return scale;
    }

    private static string GetPath(Transform target)
    {
        List<string> parts = new List<string>();
        while (target != null)
        {
            parts.Add(target.name);
            target = target.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }
}
