using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Installs the screen-grain renderer feature and the camera Inspector
/// controller once after scripts finish compiling. The setup stays editable in
/// the scene and in the URP renderer asset afterwards.
/// </summary>
[InitializeOnLoad]
internal static class StaticScreenGrainInstaller
{
    private const string RendererPath = "Assets/Settings/URP-HighFidelity-Renderer.asset";
    private const string MaterialPath = "Assets/Resources/StaticScreenGrain.mat";

    static StaticScreenGrainInstaller()
    {
        EditorApplication.delayCall += EnsureInstalled;
    }

    [MenuItem("Tools/Pozzle Room/Install Static Screen Grain")]
    private static void EnsureInstalled()
    {
        Shader shader = Shader.Find("Hidden/PozzleRoom/Static Screen Grain");
        if (shader == null)
        {
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "StaticScreenGrain" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData != null)
        {
            StaticScreenGrainRendererFeature feature = null;
            foreach (ScriptableRendererFeature candidate in rendererData.rendererFeatures)
            {
                feature = candidate as StaticScreenGrainRendererFeature;
                if (feature != null)
                {
                    break;
                }
            }

            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<StaticScreenGrainRendererFeature>();
                feature.name = "Static Screen Grain";
                rendererData.rendererFeatures.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, rendererData);
            }

            feature.settings.material = material;
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.GetComponent<StaticScreenGrainController>() == null)
        {
            mainCamera.gameObject.AddComponent<StaticScreenGrainController>();
            EditorSceneManager.MarkSceneDirty(mainCamera.gameObject.scene);
        }

        AssetDatabase.SaveAssets();
    }
}
