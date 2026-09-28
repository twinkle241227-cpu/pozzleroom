using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens a front-facing view of the utensil wall when its Plane collider is
/// clicked.  The controller is attached automatically at runtime so the FBX
/// hierarchy does not need to be modified manually.
/// </summary>
public sealed class KitchenUtensilViewController : MonoBehaviour
{
    public static KitchenUtensilViewController Active { get; private set; }
    public bool IsInUtensilView => isFocused;
    public bool CanInteractWithUtensils => isFocused && !isTransitioning;

    [SerializeField, Min(0f)] private float transitionDuration = 0.4f;
    [SerializeField, Min(0f)] private float framingPadding = 0.2f;

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
        GameObject utensilPlane = GameObject.Find("Plane");
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
        float distance = CalculateOrthographicViewDistance(bounds, targetRotation);
        Vector3 targetPosition = bounds.center + outwardNormal * distance;

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
        return Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.001f, targetCamera.aspect)) + framingPadding;
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
