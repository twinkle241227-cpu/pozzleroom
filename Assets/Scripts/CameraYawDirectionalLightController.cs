using UnityEngine;

/// <summary>
/// Makes a directional light follow the Main Camera's horizontal viewing angle
/// along a closed cycle. The light's X and Z rotations remain fixed.
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class CameraYawDirectionalLightController : MonoBehaviour
{
    [Header("对象")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Light targetDirectionalLight;

    [Header("相机 Y 角度（循环）")]
    [Tooltip("相机在此角度时，光源使用起始 Y 角度。")]
    [SerializeField, Range(0f, 360f)] private float cameraYawStart = 0f;
    [Tooltip("相机转到此角度时，光源到达最小 Y 角度。")]
    [SerializeField, Range(0.01f, 359.99f)] private float cameraYawTurnPoint = 180f;
    [Tooltip("相机回到 360°（与 0° 相同）时，光源会回到起始 Y 角度。")]
    [SerializeField, Range(0f, 360f)] private float cameraYawEnd = 360f;

    [Header("光源 Y 角度")]
    [SerializeField, Range(-360f, 720f)] private float lightYawAtStart = 153f;
    [SerializeField, Range(-360f, 720f)] private float lightYawAtTurnPoint = 123f;

    [Header("保持不变的光源轴")]
    [SerializeField] private float fixedLightPitch = 19.9f;
    [SerializeField] private float fixedLightRoll = -439f;

    private bool rotationCaptured;

    private void Awake()
    {
        ResolveReferences();
        CaptureFixedAxesIfNeeded();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        CaptureFixedAxesIfNeeded();
        ApplyLightRotation();
    }

    private void OnValidate()
    {
        cameraYawTurnPoint = Mathf.Clamp(cameraYawTurnPoint, cameraYawStart + 0.01f, cameraYawEnd - 0.01f);
        if (!Application.isPlaying && targetDirectionalLight != null && targetCamera != null)
        {
            ApplyLightRotation();
        }
    }

    /// <summary>Sets the camera and light when this component is created automatically at runtime.</summary>
    public void Configure(Camera cameraToFollow, Light lightToRotate)
    {
        targetCamera = cameraToFollow;
        targetDirectionalLight = lightToRotate;
        CaptureFixedAxesIfNeeded();
        ApplyLightRotation();
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

    private void CaptureFixedAxesIfNeeded()
    {
        if (rotationCaptured || targetDirectionalLight == null)
        {
            return;
        }

        Vector3 currentRotation = targetDirectionalLight.transform.eulerAngles;
        fixedLightPitch = currentRotation.x;
        fixedLightRoll = currentRotation.z;
        rotationCaptured = true;
    }

    private void ApplyLightRotation()
    {
        if (targetCamera == null || targetDirectionalLight == null)
        {
            return;
        }

        float cameraYaw = Mathf.Repeat(targetCamera.transform.eulerAngles.y, 360f);
        float targetLightYaw = EvaluateLightYaw(cameraYaw);
        targetDirectionalLight.transform.rotation = Quaternion.Euler(fixedLightPitch, targetLightYaw, fixedLightRoll);
    }

    private float EvaluateLightYaw(float cameraYaw)
    {
        if (cameraYaw <= cameraYawTurnPoint)
        {
            float firstHalf = Mathf.InverseLerp(cameraYawStart, cameraYawTurnPoint, cameraYaw);
            return Mathf.Lerp(lightYawAtStart, lightYawAtTurnPoint, firstHalf);
        }

        float secondHalf = Mathf.InverseLerp(cameraYawTurnPoint, cameraYawEnd, cameraYaw);
        return Mathf.Lerp(lightYawAtTurnPoint, lightYawAtStart, secondHalf);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureControllerExists()
    {
        if (FindObjectOfType<CameraYawDirectionalLightController>() != null)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        GameObject lightObject = GameObject.Find("Directional Light");
        Light directionalLight = lightObject != null ? lightObject.GetComponent<Light>() : null;
        if (mainCamera == null || directionalLight == null || directionalLight.type != LightType.Directional)
        {
            return;
        }

        GameObject controllerObject = new GameObject("Camera Yaw Directional Light Controller");
        CameraYawDirectionalLightController controller = controllerObject.AddComponent<CameraYawDirectionalLightController>();
        controller.Configure(mainCamera, directionalLight);
    }
}
