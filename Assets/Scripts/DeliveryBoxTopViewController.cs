using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Moves the main camera to the hand-authored delivery box camera pose after the ending door opens.
/// </summary>
public sealed class DeliveryBoxTopViewController : MonoBehaviour
{
    [Header("Delivery Box View")]
    [Tooltip("A scene camera used only as the destination position and rotation. If left empty, 'deliveryboxcamera' is used.")]
    [SerializeField] private Camera deliveryBoxCamera;
    [Tooltip("Time taken for Main Camera to move to the delivery box view.")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.7f;

    [Header("Open Box Objects")]
    [Tooltip("The second closed-box object. If left empty, the scene object named 'deliverybox1' is used.")]
    [SerializeField] private Transform deliveryBoxAlternate;
    [Tooltip("The opened box object. If left empty, the scene object named 'openbox' is used.")]
    [SerializeField] private Transform openBox;

    private Camera mainCamera;
    private BoxCollider fallbackCollider;
    private bool isTransitioning;
    private bool hasEnteredTopView;
    private bool hasOpenedBox;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDeliveryBox()
    {
        Transform deliveryBox = FindSceneTransform("deliverybox");
        if (deliveryBox != null && deliveryBox.GetComponent<DeliveryBoxTopViewController>() == null)
        {
            deliveryBox.gameObject.AddComponent<DeliveryBoxTopViewController>();
        }
    }

    private void Awake()
    {
        mainCamera = Camera.main;
        FindDeliveryBoxCamera();
        SetCameraActive(deliveryBoxCamera, false);
        FindOpenBoxObjects();
        if (openBox != null)
        {
            openBox.gameObject.SetActive(false);
        }
        EnsureCollider();
    }

    private void Update()
    {
        if (hasOpenedBox || isTransitioning || !EndingDoorController.IsEndingDoorOpen ||
            !Input.GetMouseButtonDown(0) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null || !IsPointerOverDeliveryBox())
        {
            return;
        }

        if (hasEnteredTopView)
        {
            OpenBox();
        }
        else
        {
            StartCoroutine(MoveMainCameraToDeliveryBoxView());
        }
    }

    private bool IsPointerOverDeliveryBox()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator MoveMainCameraToDeliveryBoxView()
    {
        if (!FindDeliveryBoxCamera())
        {
            Debug.LogWarning("[DeliveryBox] Cannot find a Camera named 'deliveryboxcamera'.", this);
            yield break;
        }

        isTransitioning = true;
        Vector3 startPosition = mainCamera.transform.position;
        Quaternion startRotation = mainCamera.transform.rotation;
        Vector3 targetPosition = deliveryBoxCamera.transform.position;
        Quaternion targetRotation = deliveryBoxCamera.transform.rotation;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = transitionDuration <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);
            mainCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, targetPosition, progress),
                Quaternion.Slerp(startRotation, targetRotation, progress));
            yield return null;
        }

        mainCamera.transform.SetPositionAndRotation(targetPosition, targetRotation);
        isTransitioning = false;
        hasEnteredTopView = true;
        Debug.Log("[DeliveryBox] Main Camera moved to deliveryboxcamera view.", this);
    }

    private void OpenBox()
    {
        FindOpenBoxObjects();
        if (openBox == null)
        {
            Debug.LogWarning("[DeliveryBox] Cannot find an object named 'openbox'.", this);
            return;
        }

        // An opened box cannot remain under an object that is about to be hidden.
        if (openBox.IsChildOf(transform))
        {
            openBox.SetParent(transform.parent, true);
        }
        else if (deliveryBoxAlternate != null && openBox.IsChildOf(deliveryBoxAlternate))
        {
            openBox.SetParent(deliveryBoxAlternate.parent, true);
        }

        openBox.gameObject.SetActive(true);
        if (deliveryBoxAlternate != null)
        {
            deliveryBoxAlternate.gameObject.SetActive(false);
        }

        gameObject.SetActive(false);
        hasOpenedBox = true;
        Debug.Log("[DeliveryBox] Opened box displayed.", this);
    }

    private void FindOpenBoxObjects()
    {
        if (deliveryBoxAlternate == null)
        {
            deliveryBoxAlternate = FindSceneTransform("deliverybox1");
        }

        if (openBox == null)
        {
            openBox = FindSceneTransform("openbox");
        }
    }

    private bool FindDeliveryBoxCamera()
    {
        if (deliveryBoxCamera != null)
        {
            return true;
        }

        foreach (Camera sceneCamera in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (sceneCamera.gameObject.scene.IsValid() &&
                string.Equals(sceneCamera.name, "deliveryboxcamera", System.StringComparison.OrdinalIgnoreCase))
            {
                deliveryBoxCamera = sceneCamera;
                return true;
            }
        }

        return false;
    }

    private static void SetCameraActive(Camera sceneCamera, bool isActive)
    {
        if (sceneCamera == null)
        {
            return;
        }

        sceneCamera.enabled = isActive;
        AudioListener listener = sceneCamera.GetComponent<AudioListener>();
        if (listener != null)
        {
            listener.enabled = isActive;
        }
    }

    private void EnsureCollider()
    {
        if (GetComponentsInChildren<Collider>(true).Length > 0 || !TryGetBounds(out Bounds bounds))
        {
            return;
        }

        fallbackCollider = gameObject.AddComponent<BoxCollider>();
        fallbackCollider.center = transform.InverseTransformPoint(bounds.center);
        fallbackCollider.size = transform.InverseTransformVector(bounds.size);
    }

    private bool TryGetBounds(out Bounds bounds)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return true;
    }

    private static Transform FindSceneTransform(string objectName)
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate.gameObject.scene.IsValid() &&
                string.Equals(candidate.name, objectName, System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
