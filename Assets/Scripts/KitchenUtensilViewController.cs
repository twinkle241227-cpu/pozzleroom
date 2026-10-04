using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens a front-facing view of the utensil wall when its CookerPlane collider is
/// clicked.  The controller is attached automatically at runtime so the FBX
/// hierarchy does not need to be modified manually.
/// </summary>
public sealed class KitchenUtensilViewController : MonoBehaviour
{
    public static KitchenUtensilViewController Active { get; private set; }
    public bool IsInUtensilView => isFocused;
    public bool CanInteractWithUtensils => isFocused && !isTransitioning;

    [SerializeField, Min(0f)] private float transitionDuration = 0.4f;
    [Header("Front View Framing")]
    [SerializeField, Min(0f)] private float framingPadding = 0.02f;
    [SerializeField, Range(-1f, 1f)] private float horizontalFramingOffset = -0.03f;
    [SerializeField, Range(-1f, 1f)] private float verticalFramingOffset;
    [SerializeField, Range(0.5f, 1f)] private float framingScale = 1f;

    private Camera targetCamera;
    private Collider entranceCollider;
    private RoomPivotDragController roomRotation;
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousOrthographic;
    private float previousOrthographicSize;
    private bool previousRoomRotationEnabled;
    private bool isFocused;
    private bool isTransitioning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToUtensilPlane()
    {
        GameObject utensilPlane = GameObject.Find("CookerPlane");
        if (utensilPlane != null && utensilPlane.GetComponent<KitchenUtensilViewController>() == null)
        {
            utensilPlane.AddComponent<KitchenUtensilViewController>();
        }
    }

    private void Awake()
    {
        Active = this;
        targetCamera = Camera.main;
        entranceCollider = GetComponent<Collider>();
        roomRotation = FindObjectOfType<RoomPivotDragController>();
    }

    private void OnDestroy()
    {
        if (Active == this)
        {
            Active = null;
        }
    }

    private void Update()
    {
        if (targetCamera == null || entranceCollider == null || isTransitioning)
        {
            return;
        }

        // The photo wall is a modal puzzle view. Its photos can overlap this
        // entrance collider in screen space, so never react to left-clicks
        // while that view (or either of its transitions) owns the camera.
        if (!isFocused && PhotoWallViewController.IsPhotoWallInteractionActive)
        {
            return;
        }

        if (isFocused)
        {
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                StartCoroutine(MoveCamera(previousCameraPosition, previousCameraRotation, false));
            }

            return;
        }

        if (!Input.GetMouseButtonDown(0) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            // The entrance is intentionally checked across every hit. Utensil
            // colliders can sit in front of this Plane in the room view.
            if (hit.collider == entranceCollider)
            {
                EnterFrontView(hit.normal);
                return;
            }
        }
    }

    private void EnterFrontView(Vector3 hitNormal)
    {
        if (!TryGetUtensilBounds(out Bounds bounds))
        {
            return;
        }

        previousCameraPosition = targetCamera.transform.position;
        previousCameraRotation = targetCamera.transform.rotation;
        previousOrthographic = targetCamera.orthographic;
        previousOrthographicSize = targetCamera.orthographicSize;
        if (roomRotation != null)
        {
            previousRoomRotationEnabled = roomRotation.enabled;
            roomRotation.enabled = false;
        }

        Vector3 outwardNormal = hitNormal.normalized;
        if (Vector3.Dot(outwardNormal, targetCamera.transform.position - bounds.center) < 0f)
        {
            outwardNormal = -outwardNormal;
        }

        Quaternion targetRotation = Quaternion.LookRotation(-outwardNormal, Vector3.up);
        Vector3 targetCenter = CalculateFramingCenter(bounds, targetRotation);
        float distance = CalculateOrthographicViewDistance(bounds, targetRotation);
        Vector3 targetPosition = targetCenter + outwardNormal * distance;

        // A frontal puzzle view reads best without perspective distortion.
        // Size is calculated from the wall bounds so all utensils remain visible.
        targetCamera.orthographic = true;
        targetCamera.orthographicSize = CalculateOrthographicSize(bounds, targetRotation);
        entranceCollider.enabled = false;
        StartCoroutine(MoveCamera(targetPosition, targetRotation, true));
    }

    private IEnumerator MoveCamera(Vector3 destinationPosition, Quaternion destinationRotation, bool entering)
    {
        isTransitioning = true;
        Vector3 startPosition = targetCamera.transform.position;
        Quaternion startRotation = targetCamera.transform.rotation;
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = transitionDuration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);
            targetCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, destinationPosition, t),
                Quaternion.Slerp(startRotation, destinationRotation, t));
            yield return null;
        }

        targetCamera.transform.SetPositionAndRotation(destinationPosition, destinationRotation);
        isFocused = entering;
        isTransitioning = false;

        if (!entering)
        {
            targetCamera.orthographic = previousOrthographic;
            targetCamera.orthographicSize = previousOrthographicSize;
            entranceCollider.enabled = true;
            if (roomRotation != null)
            {
                roomRotation.enabled = previousRoomRotationEnabled;
            }
        }
    }

    private bool TryGetUtensilBounds(out Bounds bounds)
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            bounds = renderer.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    private float CalculateOrthographicSize(Bounds bounds, Quaternion viewRotation)
    {
        Vector3 extents = bounds.extents;
        float halfWidth = ProjectExtents(extents, viewRotation * Vector3.right);
        float halfHeight = ProjectExtents(extents, viewRotation * Vector3.up);
        float fittedSize = Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.001f, targetCamera.aspect));
        return Mathf.Max(0.01f, (fittedSize + framingPadding) * framingScale);
    }

    private Vector3 CalculateFramingCenter(Bounds bounds, Quaternion viewRotation)
    {
        Vector3 extents = bounds.extents;
        Vector3 viewRight = viewRotation * Vector3.right;
        Vector3 viewUp = viewRotation * Vector3.up;
        float halfWidth = ProjectExtents(extents, viewRight);
        float halfHeight = ProjectExtents(extents, viewUp);

        // The cabinet ends close to the right edge of the utensil board. Move
        // the camera's framing center slightly into the room so the view keeps
        // the authored wall geometry on screen instead of exposing the skybox.
        return bounds.center +
               viewRight * (halfWidth * horizontalFramingOffset) +
               viewUp * (halfHeight * verticalFramingOffset);
    }

    private float CalculateOrthographicViewDistance(Bounds bounds, Quaternion viewRotation)
    {
        float halfDepth = ProjectExtents(bounds.extents, viewRotation * Vector3.forward);
        return Mathf.Max(0.5f, halfDepth + 1f);
    }

    private static float ProjectExtents(Vector3 extents, Vector3 axis)
    {
        axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        return Vector3.Dot(extents, axis);
    }
}
