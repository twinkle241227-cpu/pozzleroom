using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Installs Mesh Colliders on the currently selected kitchen utensils.
/// Dense render meshes receive a lightweight 12-triangle bounds proxy so they
/// remain inexpensive for raycasts and physics queries.
/// </summary>
[InitializeOnLoad]
public static class SelectedUtensilMeshColliderInstaller
{
    private const int SimplifyAboveTriangleCount = 1500;
    private const string ProxyFolder = "Assets/Generated/UtensilColliderMeshes";
    private const string AutomaticRunSessionKey = "SelectedUtensilMeshColliderInstaller.HasRun";

    static SelectedUtensilMeshColliderInstaller()
    {
        // Run once after this script has compiled, using the objects the user
        // already selected in the Hierarchy.
        EditorApplication.delayCall += InstallForCurrentSelectionOnce;
    }

    [MenuItem("Tools/Pozzle Room/Add Mesh Colliders To Selected Utensils")]
    private static void InstallForCurrentSelectionFromMenu()
    {
        InstallForCurrentSelection();
    }

    private static void InstallForCurrentSelectionOnce()
    {
        if (SessionState.GetBool(AutomaticRunSessionKey, false))
        {
            return;
        }

        if (Selection.gameObjects != null && Selection.gameObjects.Length > 0)
        {
            SessionState.SetBool(AutomaticRunSessionKey, true);
            InstallForCurrentSelection();
        }
    }

    private static void InstallForCurrentSelection()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            return;
        }

        int exactCount = 0;
        int proxyCount = 0;
        foreach (GameObject gameObject in selected)
        {
            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            Mesh sourceMesh = meshFilter.sharedMesh;
            int triangleCount = GetTriangleCount(sourceMesh);
            MeshCollider collider = gameObject.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = Undo.AddComponent<MeshCollider>(gameObject);
            }
            else
            {
                Undo.RecordObject(collider, "Configure utensil mesh collider");
            }

            if (triangleCount > SimplifyAboveTriangleCount)
            {
                collider.sharedMesh = CreateBoundsProxy(sourceMesh, gameObject.name);
                proxyCount++;
            }
            else
            {
                collider.sharedMesh = sourceMesh;
                exactCount++;
            }

            collider.convex = false;
            collider.cookingOptions =
                MeshColliderCookingOptions.EnableMeshCleaning |
                MeshColliderCookingOptions.WeldColocatedVertices |
                MeshColliderCookingOptions.UseFastMidphase;
            EditorUtility.SetDirty(collider);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Utensil Colliders] Added/updated {exactCount} exact Mesh Colliders and {proxyCount} simplified collision proxies.");
    }

    private static Mesh CreateBoundsProxy(Mesh sourceMesh, string objectName)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated"))
        {
            AssetDatabase.CreateFolder("Assets", "Generated");
        }

        if (!AssetDatabase.IsValidFolder(ProxyFolder))
        {
            AssetDatabase.CreateFolder("Assets/Generated", "UtensilColliderMeshes");
        }

        Bounds bounds = sourceMesh.bounds;
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Mesh proxy = new Mesh
        {
            name = objectName + "_CollisionProxy"
        };
        proxy.vertices = new[]
        {
            new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)
        };
        proxy.triangles = new[]
        {
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
        };
        proxy.RecalculateBounds();

        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            ProxyFolder + "/" + SanitizeFileName(objectName) + "_Collider.asset");
        AssetDatabase.CreateAsset(proxy, assetPath);
        return proxy;
    }

    private static int GetTriangleCount(Mesh mesh)
    {
        long indexCount = 0;
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            indexCount += (long)mesh.GetIndexCount(subMesh);
        }

        return (int)(indexCount / 3L);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '_');
        }

        return value;
    }
}
