#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Rebinds loaded-scene opaque renderers to persistent copies that use the
/// PozzleRoom chunky shader. Source FlatKit materials are never modified.
/// </summary>
[InitializeOnLoad]
internal static class ChunkyMaterialSceneMigrator
{
    private const string ShaderName = "PozzleRoom/Global Chunky Stylized";
    private const string GeneratedFolder = "Assets/Generated/ChunkyMaterials";
    private const string SessionKey = "PozzleRoom.ChunkyMaterialMigration.V7";
    private const string PatternAtlasPath = "Assets/Textures/Stylized/ChunkyHandpaintAtlas.png";

    static ChunkyMaterialSceneMigrator()
    {
        EditorApplication.delayCall += RunOnceAfterImport;
    }

    [MenuItem("Tools/PozzleRoom/重新应用全场景手绘材质")]
    private static void RunFromMenu()
    {
        ConvertLoadedScenes();
    }

    private static void RunOnceAfterImport()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, true);
        ConvertLoadedScenes();
    }

    private static void ConvertLoadedScenes()
    {
        Shader targetShader = Shader.Find(ShaderName);
        if (targetShader == null || !targetShader.isSupported)
        {
            Debug.LogError($"[ChunkyMaterialMigration] Shader '{ShaderName}' 不可用，已中止材质替换，原材质保持不变。");
            return;
        }

        EnsureFolders();
        Material template = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Resources/GlobalChunkyStyleTemplate.mat");
        Texture2D patternAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(PatternAtlasPath);
        if (template != null && patternAtlas != null)
        {
            template.SetTexture("_PatternAtlas", patternAtlas);
            EditorUtility.SetDirty(template);
        }
        UpdateGeneratedStyleParameters(template, targetShader);
        Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
        HashSet<Scene> changedScenes = new HashSet<Scene>();
        int rendererCount = 0;
        int slotCount = 0;
        int skippedSpecial = 0;

        foreach (Renderer sceneRenderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (sceneRenderer == null || !sceneRenderer.gameObject.scene.IsValid()) continue;
            if (sceneRenderer is ParticleSystemRenderer || sceneRenderer is TrailRenderer || sceneRenderer is LineRenderer)
            {
                continue;
            }

            Material[] materials = sceneRenderer.sharedMaterials;
            Material[] replacements = (Material[])materials.Clone();
            bool changed = false;

            for (int index = 0; index < materials.Length; index++)
            {
                Material source = materials[index];
                if (source == null || source.shader == null) continue;
                if (source.shader.name == ShaderName) continue;
                if (IsSpecialMaterial(source))
                {
                    skippedSpecial++;
                    continue;
                }

                if (!converted.TryGetValue(source, out Material replacement))
                {
                    replacement = CreateOrUpdateMaterial(source, targetShader, template);
                    converted[source] = replacement;
                }

                if (replacement == null) continue;
                replacements[index] = replacement;
                changed = true;
                slotCount++;
            }

            if (!changed)
            {
                bool alreadyStylized = Array.Exists(materials,
                    material => material != null && material.shader != null && material.shader.name == ShaderName);
                if (alreadyStylized) ApplyPerRendererVariation(sceneRenderer);
                continue;
            }
            Undo.RecordObject(sceneRenderer, "Apply chunky stylized material");
            sceneRenderer.sharedMaterials = replacements;
            ApplyPerRendererVariation(sceneRenderer);
            EditorUtility.SetDirty(sceneRenderer);
            changedScenes.Add(sceneRenderer.gameObject.scene);
            rendererCount++;
        }

        foreach (Scene scene in changedScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();
        Debug.Log($"[ChunkyMaterialMigration] 完成：{rendererCount} 个 Renderer、{slotCount} 个材质槽已切换；" +
                  $"生成/复用 {converted.Count} 个独立材质；保留 {skippedSpecial} 个透明、裁切或特殊效果材质。" +
                  "场景已标记为未保存，可用 Ctrl+S 确认保存。", targetShader);
    }

    private static void ApplyPerRendererVariation(Renderer sceneRenderer)
    {
        int id = sceneRenderer.gameObject.GetInstanceID();
        System.Random random = new System.Random(id);
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        sceneRenderer.GetPropertyBlock(block);
        block.SetVector("_RandomOffset", new Vector4(
            (float)random.NextDouble() * 37f,
            (float)random.NextDouble() * 37f,
            (float)random.NextDouble() * 37f,
            0f));

        Bounds bounds;
        if (sceneRenderer is SkinnedMeshRenderer skinned)
        {
            bounds = skinned.localBounds;
        }
        else
        {
            MeshFilter filter = sceneRenderer.GetComponent<MeshFilter>();
            bounds = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);
        }
        block.SetVector("_ObjectBoundsCenter", bounds.center);
        block.SetVector("_ObjectBoundsSize", bounds.size);
        Vector3 lossyScale = sceneRenderer.transform.lossyScale;
        block.SetVector("_ObjectWorldScale", new Vector4(lossyScale.x, lossyScale.y, lossyScale.z, 0f));
        Vector3 worldSize = sceneRenderer.bounds.size;
        float largestWorldDimension = Mathf.Max(worldSize.x, worldSize.y, worldSize.z);
        // Large room surfaces/furniture need a fixed density. Small props keep
        // object-relative mapping so their brush marks remain readable.
        block.SetFloat("_MappingMode", largestWorldDimension >= 3.5f ? 1f : 0f);
        sceneRenderer.SetPropertyBlock(block);
    }

    private static Material CreateOrUpdateMaterial(Material source, Shader targetShader, Material template)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string sourceGuid = string.IsNullOrEmpty(sourcePath)
            ? Mathf.Abs(source.GetInstanceID()).ToString()
            : AssetDatabase.AssetPathToGUID(sourcePath);
        string safeName = MakeSafeFileName(source.name);
        string generatedPath = $"{GeneratedFolder}/{safeName}_{sourceGuid}.mat";
        Material generated = AssetDatabase.LoadAssetAtPath<Material>(generatedPath);

        if (generated == null)
        {
            generated = new Material(targetShader);
            AssetDatabase.CreateAsset(generated, generatedPath);
        }
        else
        {
            generated.shader = targetShader;
        }

        if (template != null)
        {
            generated.CopyPropertiesFromMaterial(template);
            generated.shader = targetShader;
        }

        generated.name = source.name + " [Chunky]";
        CopyBaseSurface(source, generated);
        EditorUtility.SetDirty(generated);
        return generated;
    }

    private static void UpdateGeneratedStyleParameters(Material template, Shader targetShader)
    {
        if (template == null || !AssetDatabase.IsValidFolder(GeneratedFolder)) return;

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { GeneratedFolder }))
        {
            Material generated = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (generated == null) continue;

            Texture baseTexture = generated.HasProperty("_BaseMap") ? generated.GetTexture("_BaseMap") : null;
            Color baseColor = generated.HasProperty("_BaseColor") ? generated.GetColor("_BaseColor") : Color.white;
            Vector2 scale = generated.HasProperty("_BaseMap") ? generated.GetTextureScale("_BaseMap") : Vector2.one;
            Vector2 offset = generated.HasProperty("_BaseMap") ? generated.GetTextureOffset("_BaseMap") : Vector2.zero;
            generated.CopyPropertiesFromMaterial(template);
            generated.shader = targetShader;
            generated.SetTexture("_BaseMap", baseTexture);
            generated.SetColor("_BaseColor", baseColor);
            generated.SetTextureScale("_BaseMap", scale);
            generated.SetTextureOffset("_BaseMap", offset);
            EditorUtility.SetDirty(generated);
        }
    }

    private static void CopyBaseSurface(Material source, Material destination)
    {
        Texture baseTexture = ReadTexture(source, "_BaseMap", "_MainTex");
        Color baseColor = ReadColor(source, Color.white, "_BaseColor", "_Color");
        destination.SetTexture("_BaseMap", baseTexture);
        destination.SetColor("_BaseColor", baseColor);

        string textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap" :
            source.HasProperty("_MainTex") ? "_MainTex" : null;
        if (textureProperty != null)
        {
            destination.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
            destination.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
        }
    }

    private static bool IsSpecialMaterial(Material material)
    {
        string shaderName = material.shader.name;
        if (shaderName.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shaderName.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shaderName.IndexOf("Skybox", StringComparison.OrdinalIgnoreCase) >= 0 ||
            material.renderQueue >= (int)RenderQueue.Transparent)
        {
            return true;
        }

        bool transparentSurface = material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f;
        bool alphaClip = material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f;
        return transparentSurface || alphaClip;
    }

    private static Texture ReadTexture(Material source, params string[] names)
    {
        foreach (string property in names)
        {
            if (source.HasProperty(property)) return source.GetTexture(property);
        }
        return null;
    }

    private static Color ReadColor(Material source, Color fallback, params string[] names)
    {
        foreach (string property in names)
        {
            if (source.HasProperty(property)) return source.GetColor(property);
        }
        return fallback;
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(value) ? "Material" : value;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated"))
            AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder(GeneratedFolder))
            AssetDatabase.CreateFolder("Assets/Generated", "ChunkyMaterials");
    }
}
#endif
