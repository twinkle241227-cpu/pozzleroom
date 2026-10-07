using UnityEngine;

/// <summary>
/// Read-only rotation diagnostics for Main Camera and Directional Light.
/// Console entries start with [CameraLightDebug]. This component never writes
/// to either transform.
/// </summary>
[DefaultExecutionOrder(11000)]
[DisallowMultipleComponent]
public sealed class CameraDirectionalLightRotationDebug : MonoBehaviour
{
    private const float RotationChangeThreshold = 0.1f;

    [Header("日志")]
    [SerializeField] private bool logWhenRotationChanges = true;
    [SerializeField] private bool logOnLeftMouseRelease = true;

    private Camera targetCamera;
    private Light targetDirectionalLight;
    private Quaternion previousCameraRotation;
    private Quaternion previousLightRotation;
    private bool isReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        GameObject lightObject = GameObject.Find("Directional Light");
        if (lightObject == null || lightObject.GetComponent<CameraDirectionalLightRotationDebug>() != null)
        {
            return;
        }

        lightObject.AddComponent<CameraDirectionalLightRotationDebug>();
    }

    private void Start()
    {
        ResolveReferences();
        if (targetCamera == null || targetDirectionalLight == null)
        {
            Debug.LogWarning("[CameraLightDebug] Main Camera or Directional Light is missing; no rotation data can be read.", this);
            return;
        }

        previousCameraRotation = targetCamera.transform.rotation;
        previousLightRotation = targetDirectionalLight.transform.rotation;
        isReady = true;
        LogSnapshot("Initial capture");
    }

    private void LateUpdate()
    {
        if (!isReady)
        {
            ResolveReferences();
            return;
        }

        bool cameraChanged = Quaternion.Angle(previousCameraRotation, targetCamera.transform.rotation) >= RotationChangeThreshold;
        bool lightChanged = Quaternion.Angle(previousLightRotation, targetDirectionalLight.transform.rotation) >= RotationChangeThreshold;

        if (logWhenRotationChanges && (cameraChanged || lightChanged))
        {
            LogSnapshot(cameraChanged && lightChanged ? "Camera and light rotation changed" :
                cameraChanged ? "Camera rotation changed" : "Directional Light rotation changed");
        }

        if (logOnLeftMouseRelease && Input.GetMouseButtonUp(0))
        {
            LogSnapshot("Left mouse up");
        }

        previousCameraRotation = targetCamera.transform.rotation;
        previousLightRotation = targetDirectionalLight.transform.rotation;
    }

    [ContextMenu("读取当前相机和光源旋转")]
    private void ReadCurrentRotation()
    {
        ResolveReferences();
        LogSnapshot("Manual read");
    }

    private void ResolveReferences()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetDirectionalLight == null)
        {
            GameObject lightObject = GameObject.Find("Directional Light");
            if (lightObject != null)
            {
                Light candidate = lightObject.GetComponent<Light>();
                if (candidate != null && candidate.type == LightType.Directional)
                {
                    targetDirectionalLight = candidate;
                }
            }
        }
    }

    private void LogSnapshot(string reason)
    {
        if (targetCamera == null || targetDirectionalLight == null)
        {
            Debug.LogWarning($"[CameraLightDebug] {reason}; missing camera or directional light.", this);
            return;
        }

        Transform cameraTransform = targetCamera.transform;
        Transform lightTransform = targetDirectionalLight.transform;

        Debug.Log(
            $"[CameraLightDebug] {reason}; " +
            $"camera worldEuler={cameraTransform.eulerAngles:F2}, localEuler={cameraTransform.localEulerAngles:F2}; " +
            $"directionalLight worldEuler={lightTransform.eulerAngles:F2}, localEuler={lightTransform.localEulerAngles:F2}; " +
            $"cameraY={Mathf.Repeat(cameraTransform.eulerAngles.y, 360f):F2}; " +
            $"lightY={Mathf.Repeat(lightTransform.eulerAngles.y, 360f):F2}", this);
    }
}
