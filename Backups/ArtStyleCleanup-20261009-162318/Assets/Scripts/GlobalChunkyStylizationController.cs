using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Applies the room-wide chunky painted look without modifying source materials.
/// Transparent objects, particles, UI and explicitly excluded roots are left intact.
/// </summary>
[DisallowMultipleComponent]
public sealed class GlobalChunkyStylizationController : MonoBehaviour
{
    [Header("全局开关")]
    [SerializeField] private bool applyOnStart = false;
    [Tooltip("启用后从 Resources/GlobalChunkyStyleTemplate 材质读取可持久保存的风格参数。")]
    [SerializeField] private bool useTemplateValues = true;
    [SerializeField] private bool includeInactiveObjects;
    [SerializeField] private LayerMask affectedLayers = ~0;

    [Header("表面波点")]
    [SerializeField, Range(0f, 1f)] private float patternStrength = 0.42f;
    [SerializeField, Range(0.1f, 20f)] private float patternScale = 2.4f;
    [SerializeField, Range(0f, 1f)] private float dotStrength = 0.22f;

    [Header("连续冷暖与高光")]
    [SerializeField] private Color warmLitColor = new Color(1f, 0.38f, 0.16f, 1f);
    [SerializeField] private Color coolShadowColor = new Color(0.16f, 0.30f, 0.62f, 1f);
    [SerializeField] private Color highlightColor = new Color(1f, 0.96f, 0.86f, 1f);
    [SerializeField, Range(0f, 1f)] private float highlightStrength = 0.72f;

    [Header("排除")]
    [Tooltip("这些节点及其全部子物体不会被替换材质。")]
    [SerializeField] private List<Transform> excludedRoots = new List<Transform>();
    [SerializeField] private bool excludeTransparentMaterials = true;
    [SerializeField] private bool excludeCutoutMaterials = true;
    [SerializeField] private bool excludeOutlineMaterials = true;

    private readonly Dictionary<Material, Material> generatedMaterials = new Dictionary<Material, Material>();
    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private Shader stylizedShader;
    private bool isApplying;

    private void Start()
    {
        if (applyOnStart)
        {
            ApplyToScene();
        }
    }

    [ContextMenu("应用到整个场景")]
    public void ApplyToScene()
    {
        if (isApplying)
        {
            return;
        }
        isApplying = true;
        RestoreOriginalMaterials();
        Material template = Resources.Load<Material>("GlobalChunkyStyleTemplate");
        stylizedShader = template != null ? template.shader : Shader.Find("PozzleRoom/Global Chunky Stylized");
        if (stylizedShader == null)
        {
            Debug.LogError("[GlobalChunkyStyle] 找不到风格化 Shader。", this);
            isApplying = false;
            return;
        }

        if (useTemplateValues && template != null)
        {
            ReadStyleFromTemplate(template);
        }

        Renderer[] renderers = FindObjectsOfType<Renderer>(includeInactiveObjects);
        int rendererCount = 0;
        int materialCount = 0;

        foreach (Renderer sceneRenderer in renderers)
        {
            if (!ShouldStyle(sceneRenderer))
            {
                continue;
            }

            Material[] sourceMaterials = sceneRenderer.sharedMaterials;
            Material[] replacements = new Material[sourceMaterials.Length];
            bool changed = false;

            for (int index = 0; index < sourceMaterials.Length; index++)
            {
                Material source = sourceMaterials[index];
                if (!ShouldStyle(source))
                {
                    replacements[index] = source;
                    continue;
                }

                replacements[index] = GetOrCreateStylizedMaterial(source);
                changed = true;
                materialCount++;
            }

            if (!changed)
            {
                continue;
            }

            originalMaterials[sceneRenderer] = sourceMaterials;
            sceneRenderer.sharedMaterials = replacements;
            ApplyPerObjectVariation(sceneRenderer);
            rendererCount++;
        }

        Debug.Log($"[GlobalChunkyStyle] 已处理 {rendererCount} 个 Renderer、{materialCount} 个材质槽。源材质未被修改。", this);
        isApplying = false;
    }

    [ContextMenu("恢复原始材质")]
    public void RestoreOriginalMaterials()
    {
        foreach (KeyValuePair<Renderer, Material[]> pair in originalMaterials)
        {
            if (pair.Key != null)
            {
                pair.Key.sharedMaterials = pair.Value;
            }
        }

        originalMaterials.Clear();
        foreach (Material generated in generatedMaterials.Values)
        {
            if (generated != null)
            {
                DestroyGeneratedMaterial(generated);
            }
        }
        generatedMaterials.Clear();
    }

    private bool ShouldStyle(Renderer sceneRenderer)
    {
        if (sceneRenderer == null || !sceneRenderer.enabled ||
            (affectedLayers.value & (1 << sceneRenderer.gameObject.layer)) == 0)
        {
            return false;
        }

        if (sceneRenderer is ParticleSystemRenderer || sceneRenderer is TrailRenderer || sceneRenderer is LineRenderer)
        {
            return false;
        }

        foreach (Transform root in excludedRoots)
        {
            if (root != null && sceneRenderer.transform.IsChildOf(root))
            {
                return false;
            }
        }

        return true;
    }

    private bool ShouldStyle(Material material)
    {
        if (material == null || material.shader == null)
        {
            return false;
        }

        string shaderName = material.shader.name;
        if (shaderName == "PozzleRoom/Global Chunky Stylized")
        {
            return false;
        }

        if (excludeOutlineMaterials &&
            (shaderName.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0 ||
             material.name.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return false;
        }

        if (excludeTransparentMaterials)
        {
            bool transparentQueue = material.renderQueue >= (int)RenderQueue.Transparent;
            bool transparentSurface = material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f;
            if (transparentQueue || transparentSurface)
            {
                return false;
            }
        }

        if (excludeCutoutMaterials)
        {
            bool alphaTestQueue = material.renderQueue >= (int)RenderQueue.AlphaTest &&
                                  material.renderQueue < (int)RenderQueue.Transparent;
            bool alphaClip = material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f;
            if (alphaTestQueue || alphaClip)
            {
                return false;
            }
        }

        return true;
    }

    private Material GetOrCreateStylizedMaterial(Material source)
    {
        if (generatedMaterials.TryGetValue(source, out Material existing))
        {
            return existing;
        }

        Material generated = new Material(stylizedShader)
        {
            name = source.name + " [Chunky Runtime]",
            hideFlags = HideFlags.DontSave
        };

        Texture baseTexture = ReadTexture(source, "_BaseMap", "_MainTex");
        Color baseColor = ReadColor(source, Color.white, "_BaseColor", "_Color");
        if (baseTexture != null)
        {
            generated.SetTexture("_BaseMap", baseTexture);
        }
        generated.SetColor("_BaseColor", baseColor);
        generated.SetColor("_WarmLitColor", warmLitColor);
        generated.SetColor("_CoolShadowColor", coolShadowColor);
        generated.SetColor("_HighlightColor", highlightColor);
        generated.SetFloat("_PatternScale", patternScale);
        generated.SetFloat("_PatternStrength", patternStrength);
        generated.SetFloat("_DotStrength", dotStrength);
        generated.SetFloat("_HighlightStrength", highlightStrength);
        generated.SetFloat("_Smoothness", ReadFloat(source, 0.18f, "_Smoothness", "_Glossiness"));
        generated.SetFloat("_Metallic", ReadFloat(source, 0f, "_Metallic"));

        if (source.HasProperty("_BaseMap"))
        {
            generated.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
            generated.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
        }
        else if (source.HasProperty("_MainTex"))
        {
            generated.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
            generated.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
        }

        generatedMaterials[source] = generated;
        return generated;
    }

    private void ReadStyleFromTemplate(Material template)
    {
        patternStrength = template.GetFloat("_PatternStrength");
        patternScale = template.GetFloat("_PatternScale");
        dotStrength = template.GetFloat("_DotStrength");
        warmLitColor = template.GetColor("_WarmLitColor");
        coolShadowColor = template.GetColor("_CoolShadowColor");
        highlightColor = template.GetColor("_HighlightColor");
        highlightStrength = template.GetFloat("_HighlightStrength");
    }

    private static void ApplyPerObjectVariation(Renderer sceneRenderer)
    {
        int id = sceneRenderer.gameObject.GetInstanceID();
        System.Random random = new System.Random(id);
        Vector4 offset = new Vector4(
            (float)random.NextDouble() * 20f,
            (float)random.NextDouble() * 20f,
            (float)random.NextDouble() * 20f,
            0f);
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        sceneRenderer.GetPropertyBlock(block);
        block.SetVector("_RandomOffset", offset);
        Bounds localBounds = GetLocalBounds(sceneRenderer);
        block.SetVector("_ObjectBoundsCenter", localBounds.center);
        block.SetVector("_ObjectBoundsSize", localBounds.size);
        Vector3 lossyScale = sceneRenderer.transform.lossyScale;
        block.SetVector("_ObjectWorldScale", new Vector4(lossyScale.x, lossyScale.y, lossyScale.z, 0f));
        Vector3 worldBoundsSize = sceneRenderer.bounds.size;
        float largestWorldDimension = Mathf.Max(worldBoundsSize.x, worldBoundsSize.y, worldBoundsSize.z);
        block.SetFloat("_MappingMode", largestWorldDimension >= 3.5f ? 1f : 0f);
        sceneRenderer.SetPropertyBlock(block);
    }

    private static Bounds GetLocalBounds(Renderer sceneRenderer)
    {
        if (sceneRenderer is SkinnedMeshRenderer skinnedRenderer)
        {
            return skinnedRenderer.localBounds;
        }

        MeshFilter meshFilter = sceneRenderer.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            return meshFilter.sharedMesh.bounds;
        }

        // Fallback for uncommon Renderer types. Convert the world AABB size to
        // an approximate local size while retaining a stable model-space origin.
        Vector3 lossyScale = sceneRenderer.transform.lossyScale;
        Vector3 worldSize = sceneRenderer.bounds.size;
        Vector3 localSize = new Vector3(
            worldSize.x / Mathf.Max(Mathf.Abs(lossyScale.x), 0.0001f),
            worldSize.y / Mathf.Max(Mathf.Abs(lossyScale.y), 0.0001f),
            worldSize.z / Mathf.Max(Mathf.Abs(lossyScale.z), 0.0001f));
        return new Bounds(Vector3.zero, localSize);
    }

    private static Texture ReadTexture(Material source, params string[] names)
    {
        foreach (string propertyName in names)
        {
            if (source.HasProperty(propertyName))
            {
                return source.GetTexture(propertyName);
            }
        }
        return null;
    }

    private static Color ReadColor(Material source, Color fallback, params string[] names)
    {
        foreach (string propertyName in names)
        {
            if (source.HasProperty(propertyName))
            {
                return source.GetColor(propertyName);
            }
        }
        return fallback;
    }

    private static float ReadFloat(Material source, float fallback, params string[] names)
    {
        foreach (string propertyName in names)
        {
            if (source.HasProperty(propertyName))
            {
                return source.GetFloat(propertyName);
            }
        }
        return fallback;
    }

    private void OnDestroy()
    {
        RestoreOriginalMaterials();
    }

    private static void DestroyGeneratedMaterial(Material generated)
    {
        if (generated == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(generated);
            return;
        }
#endif
        Destroy(generated);
    }
}

#if UNITY_EDITOR
[InitializeOnLoad]
internal static class GlobalChunkyStylizationEditorPreview
{
    private const string RuntimeSuffix = " [Chunky Runtime]";

    static GlobalChunkyStylizationEditorPreview()
    {
        EditorApplication.delayCall += InitializePreview;
        AssemblyReloadEvents.beforeAssemblyReload += ResetSceneViewCameras;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += ResetSceneViewCameras;
    }

    private static void InitializePreview()
    {
        RecoverOrphanedPreviewMaterials();
        ResetSceneViewCameras();
        SceneView.RepaintAll();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode)
        {
            ResetSceneViewCameras();
        }
    }

    private static void ResetSceneViewCameras()
    {
        foreach (SceneView sceneView in SceneView.sceneViews)
        {
            if (sceneView != null && sceneView.camera != null)
            {
                sceneView.camera.ResetReplacementShader();
            }
        }
    }

    private static void RecoverOrphanedPreviewMaterials()
    {
        foreach (Renderer sceneRenderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (sceneRenderer == null || !sceneRenderer.gameObject.scene.IsValid()) continue;

            Material[] current = sceneRenderer.sharedMaterials;
            bool changed = false;
            for (int index = 0; index < current.Length; index++)
            {
                Material material = current[index];
                if (material == null || !material.name.EndsWith(RuntimeSuffix, StringComparison.Ordinal)) continue;

                string originalName = material.name.Substring(0, material.name.Length - RuntimeSuffix.Length);
                string[] candidates = AssetDatabase.FindAssets($"{originalName} t:Material");
                foreach (string guid in candidates)
                {
                    Material asset = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset == null || asset.name != originalName) continue;
                    current[index] = asset;
                    changed = true;
                    break;
                }
            }

            if (changed)
            {
                sceneRenderer.sharedMaterials = current;
            }
        }
    }
}
#endif
