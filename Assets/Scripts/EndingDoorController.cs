using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Enables the ending door only after the clothes puzzle reaches EndingCamera.
/// The door root rotates around the scene marker named "door pivot".
/// </summary>
public sealed class EndingDoorController : MonoBehaviour
{
    /// <summary>True after the ending door has completed its opening animation.</summary>
    public static bool IsEndingDoorOpen { get; private set; }

    [Header("References")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private Transform doorRoot;
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private Collider handleCollider;

    [Header("Door Opening")]
    [Tooltip("Positive and negative values choose the opening direction.")]
    [SerializeField, Range(-180f, 180f)] private float openAngle = 90f;
    [SerializeField, Min(0f)] private float openDuration = 0.8f;

    [Header("Fallback Handle Hit Area")]
    [Tooltip("Used only when the door model has no object whose name contains 'handle'.")]
    [SerializeField, Range(0.25f, 1f)] private float fallbackHandleSide = 0.78f;
    [SerializeField, Min(0.02f)] private float fallbackHandleRadius = 0.09f;

    private bool isOpening;
    private bool isOpen;
    private bool isAvailable;
    private Vector3 hingePosition;
    private Vector3 hingeAxis;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDoorState()
    {
        IsEndingDoorOpen = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDoorPivot()
    {
        Transform pivot = FindSceneTransform("door pivot");
        if (pivot != null && pivot.GetComponent<EndingDoorController>() == null)
        {
            pivot.gameObject.AddComponent<EndingDoorController>();
        }
    }

    private void Awake()
    {
        IsEndingDoorOpen = false;
        ResolveReferences();
        CreateFallbackHandleIfNeeded();
    }

    private void Update()
    {
        if (!isAvailable)
        {
            isAvailable = BedPileViewController.IsEndingViewActive;
            if (!isAvailable)
            {
                return;
            }

            ResolveReferences();
            CreateFallbackHandleIfNeeded();
            Debug.Log("[EndingDoor] Ending view active. Click the door handle to open it.", this);
        }

        if (isOpen || isOpening || !Input.GetMouseButtonDown(0) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        if (interactionCamera == null || handleCollider == null)
        {
            return;
        }

        Ray ray = interactionCamera.ScreenPointToRay(Input.mousePosition);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (hit.collider == handleCollider || hit.collider.transform.IsChildOf(handleCollider.transform))
            {
                StartCoroutine(OpenDoor());
                break;
            }
        }
    }

    private void ResolveReferences()
    {
        if (doorPivot == null)
        {
            doorPivot = transform;
        }

        // In the supplied scene, door pivot is a child of Door1.003.
        // Rotate that parent so all visual door pieces move together.
        if (doorRoot == null && doorPivot != null)
        {
            doorRoot = doorPivot.parent;
        }

        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }

        if (handleCollider == null && doorRoot != null)
        {
            foreach (Collider candidate in doorRoot.GetComponentsInChildren<Collider>(true))
            {
                if (candidate.name.ToLowerInvariant().Contains("handle"))
                {
                    handleCollider = candidate;
                    break;
                }
            }
        }
    }

    private void CreateFallbackHandleIfNeeded()
    {
        if (handleCollider != null || doorRoot == null || doorPivot == null)
        {
            return;
        }

        if (!TryGetRendererBounds(doorRoot, out Bounds bounds))
        {
            Debug.LogWarning("[EndingDoor] Could not calculate the door bounds for its fallback handle hit area.", this);
            return;
        }

        Vector3 axis = doorPivot.up;
        Vector3 acrossDoor = Vector3.ProjectOnPlane(bounds.center - doorPivot.position, axis);
        if (acrossDoor.sqrMagnitude < 0.0001f)
        {
            acrossDoor = Vector3.ProjectOnPlane(doorRoot.right, axis);
        }

        acrossDoor.Normalize();
        float extent = GetProjectedExtent(bounds, acrossDoor);
        Vector3 position = bounds.center + acrossDoor * extent * fallbackHandleSide;

        GameObject hitArea = new GameObject("EndingDoorHandleHitArea");
        hitArea.transform.position = position;
        SphereCollider sphere = hitArea.AddComponent<SphereCollider>();
        sphere.radius = Mathf.Max(fallbackHandleRadius, Mathf.Min(bounds.extents.y * 0.12f, 0.16f));
        sphere.isTrigger = true;
        handleCollider = sphere;

        Debug.Log("[EndingDoor] No handle object was found; created an invisible handle click area on the handle side of the door.", this);
    }

    private IEnumerator OpenDoor()
    {
        if (doorRoot == null || doorPivot == null)
        {
            yield break;
        }

        isOpening = true;
        // doorPivot moves together with doorRoot, so keep a fixed world-space hinge.
        hingePosition = doorPivot.position;
        // The door leaf sits in door pivot's local XY plane. Its local Z
        // (forward) vector is therefore the hinge axis, even if NewPivot has
        // moved or rotated the whole room in world space.
        hingeAxis = doorPivot.forward.normalized;

        float elapsed = 0f;
        float rotatedAngle = 0f;
        while (elapsed < openDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = openDuration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, elapsed / openDuration);
            float targetAngle = openAngle * progress;
            doorRoot.RotateAround(hingePosition, hingeAxis, targetAngle - rotatedAngle);
            rotatedAngle = targetAngle;
            yield return null;
        }

        doorRoot.RotateAround(hingePosition, hingeAxis, openAngle - rotatedAngle);
        isOpen = true;
        isOpening = false;
        IsEndingDoorOpen = true;
        Debug.Log("[EndingDoor] Door opened. Delivery box is now available.", this);
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return true;
    }

    private static float GetProjectedExtent(Bounds bounds, Vector3 direction)
    {
        Vector3 extents = bounds.extents;
        return Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y + Mathf.Abs(direction.z) * extents.z;
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
