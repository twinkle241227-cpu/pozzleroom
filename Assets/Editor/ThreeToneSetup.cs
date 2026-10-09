using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// One-time installation for this requested change. No runtime material replacement.
[InitializeOnLoad]
internal static class ThreeToneSetup
{
    private const string Marker = "Library/ThreeToneSurface-v1-installed.txt";
    private const string Report = "Temp/ThreeToneSurface-validation.txt";
    static ThreeToneSetup()
    {
        EditorApplication.delayCall += InstallOnce;
        EditorApplication.delayCall += RepairEmissionOnce;
    }

    private static void RepairEmissionOnce()
    {
        const string repairMarker = "Library/ThreeToneSurface-emission-repaired.txt";
        const string originalBackup = "Backups/ThreeTone-20261009-164925";
        if (File.Exists(repairMarker) || !Directory.Exists(originalBackup)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RepairEmissionOnce;
            return;
        }
        int repaired = 0;
        string backup = "Backups/ThreeToneEmissionRepair-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (string original in Directory.GetFiles(originalBackup, "*.mat", SearchOption.AllDirectories))
        {
            string path = original.Substring(originalBackup.Length + 1).Replace('\\', '/');
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null || material.shader.name != "PozzleRoom/Three Tone Surface") continue;
            string yaml = File.ReadAllText(original);
            var keywords = System.Text.RegularExpressions.Regex.Match(yaml,
                @"(?ms)^  m_ValidKeywords:(.*?)(?=^  \w|\z)").Groups[1].Value;
            bool enabled = System.Text.RegularExpressions.Regex.IsMatch(keywords, @"\b_EMISSION\b");
            if (material.IsKeywordEnabled("_EMISSION") == enabled) continue;
            string copy = Path.Combine(backup, path);
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            File.Copy(path, copy, false);
            Undo.RecordObject(material, "Restore original emission state");
            SetKeyword(material, "_EMISSION", enabled);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            repaired++;
        }
        string result = "Restored original emission keywords: " + repaired +
            "\nBase colors, textures, lights and pipeline unchanged.\nBackup: " + backup;
        File.WriteAllText(repairMarker, result);
        File.WriteAllText("Temp/ThreeToneSurface-emission-repair.txt", result);
        SceneView.RepaintAll();
        Debug.Log("[ThreeToneSurface] " + result);
    }

    private static void InstallOnce()
    {
        if (File.Exists(Marker)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += InstallOnce;
            return;
        }
        Apply();
    }

    [MenuItem("Tools/PozzleRoom/Apply Three Tone Surface")]
    private static void Apply()
    {
        var shader = Shader.Find("PozzleRoom/Three Tone Surface");
        var report = new List<string>();
        if (shader == null) { File.WriteAllText(Report, "Shader not imported; no materials changed."); return; }
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
        {
            report.Add(message.severity + ": " + message.message);
            if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
            {
                File.WriteAllLines(Report, report);
                Debug.LogError("Three Tone shader has errors. Materials have not been changed.");
                return;
            }
        }
        var backup = "Backups/ThreeTone-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null ||
                material.shader.name != "Universal Render Pipeline/Lit") continue;
            if (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f) continue;
            string copy = Path.Combine(backup, path);
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            File.Copy(path, copy, false);
            Undo.RecordObject(material, "Apply Three Tone Surface");
            bool emissionEnabled = material.IsKeywordEnabled("_EMISSION");
            material.shader = shader;
            material.SetColor("_ToneLight", new Color(1f, 0.97f, 0.9f, 1f));
            material.SetColor("_ToneMid", new Color(0.65f, 0.65f, 0.65f, 1f));
            material.SetColor("_ToneDark", new Color(0.25f, 0.28f, 0.34f, 1f));
            material.SetFloat("_ToneLow", 0.2f);
            material.SetFloat("_ToneHigh", 0.65f);
            material.SetFloat("_ToneSoftness", 0.035f);
            SetKeyword(material, "_NORMALMAP", material.GetTexture("_BumpMap") != null);
            SetKeyword(material, "_ALPHATEST_ON", material.GetFloat("_AlphaClip") > 0.5f);
            SetKeyword(material, "_OCCLUSIONMAP", material.GetTexture("_OcclusionMap") != null);
            SetKeyword(material, "_EMISSION", emissionEnabled);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            count++;
        }
        report.Add("Applied opaque/cutout materials: " + count);
        report.Add("Backup: " + backup);
        report.Add("Pipeline assets, lights, transparent materials, outlines and skybox unchanged.");
        File.WriteAllLines(Report, report);
        File.WriteAllLines(Marker, report);
        SceneView.RepaintAll();
        Debug.Log("[ThreeToneSurface] Applied to " + count + " materials. " + Report);
    }

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
    }
}
