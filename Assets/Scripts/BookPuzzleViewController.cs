using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Enters a front-facing view when the player clicks the book group and restores
/// the previous room view when the player presses the right mouse button.
/// </summary>
public sealed class BookPuzzleViewController : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform booksRoot;
    [SerializeField] private Transform cameraDirection;
    [Tooltip("Only this collider, or a collider belonging to an individual book, can enter the book view.")]
    [SerializeField] private Collider entranceCollider;
    [SerializeField] private RoomPivotDragController roomRotation;
    [SerializeField] private BookPuzzleInteractionController interactionController;

    [Header("Interaction")]
    [SerializeField] private bool ignorePointerOverUi = true;
    [SerializeField, Min(0f)] private float hotspotPadding = 0.03f;
    [SerializeField, Min(0.001f)] private float hotspotMinimumDepth = 0.03f;

    [Header("Front View")]
    [SerializeField, Min(0f)] private float framingPadding = 0.25f;
    [SerializeField] private Vector2 framingOffset = Vector2.zero;
    [SerializeField, Min(0f)] private float transitionDuration = 0.35f;

    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousRoomRotationEnabled;
    private bool isFocused;
    private bool isTransitioning;
    private Quaternion cameraDirectionInBooksRoot;
    private bool hasCameraDirectionInBooksRoot;

    public bool IsFocused => isFocused;
    public bool IsTransitioning => isTransitioning;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        // The authored hotspot is a sibling of the Book root. Resolve it when
        // this controller was attached at runtime and create a fitted trigger
        // if the hierarchy was moved without an authored Collider.
        if (entranceCollider == null && booksRoot != null && booksRoot.parent != null)
        {
            Transform hotspot = booksRoot.parent.Find("InteractionHotspot");
            if (hotspot != null)
            {
                entranceCollider = hotspot.GetComponent<Collider>();
                if (entranceCollider == null)
                {
                    entranceCollider = CreateFittedEntranceCollider(hotspot);
                }
            }
        }

        // CameraDirection may be authored outside BookPuzzleRoot. Store its
        // orientation relative to the book group once, then rebuild it from
        // the book group's current rotation when the room has been turned.
        if (booksRoot != null && cameraDirection != null)
        {
            cameraDirectionInBooksRoot = Quaternion.Inverse(booksRoot.rotation) * cameraDirection.rotation;
            hasCameraDirectionInBooksRoot = true;
        }
    }

    private void Update()
    {
        if (targetCamera == null || booksRoot == null || isTransitioning)
        {
            return;
        }

        if (PuzzleViewLock.IsLockedByOther(this))
        {
            return;
        }

        if (isFocused)
        {
            if (Input.GetMouseButtonDown(1) &&
                (interactionController == null || !interactionController.IsBusy))
            {
                ExitFrontView();
            }

            return;
        }

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (ignorePointerOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (IsPointerOverBookArea(Input.mousePosition))
        {
            EnterFrontView();
        }
    }

    private bool IsPointerOverBookArea(Vector3 pointerPosition)
    {
        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            return false;
        }

        Transform hitTransform = hit.collider.transform;
        if (entranceCollider != null &&
            (hit.collider == entranceCollider || hitTransform.IsChildOf(entranceCollider.transform)))
        {
            return true;
        }

        // A collider belonging to one of the immediate Book1...Book9 children
        // is also an explicit, valid entrance target.
        Transform candidate = hitTransform;
        while (candidate != null && candidate.parent != booksRoot)
        {
            candidate = candidate.parent;
        }

        return candidate != null && candidate.parent == booksRoot;
    }

    private void EnterFrontView()
    {
        if (!TryGetBookBounds(out Bounds bounds))
        {
            return;
        }

        if (!PuzzleViewLock.TryAcquire(this))
        {
            return;
        }

        previousCameraPosition = targetCamera.transform.position;
        previousCameraRotation = targetCamera.transform.rotation;

        if (roomRotation != null)
        {
            previousRoomRotationEnabled = roomRotation.enabled;
            roomRotation.enabled = false;
        }

        Vector3 viewDirection = hasCameraDirectionInBooksRoot
            ? (booksRoot.rotation * cameraDirectionInBooksRoot) * Vector3.forward
            : booksRoot.forward;
        if (viewDirection.sqrMagnitude < 0.0001f)
        {
            viewDirection = Vector3.forward;
        }

        viewDirection.Normalize();
        Quaternion targetRotation = Quaternion.LookRotation(viewDirection, Vector3.up);
        float distance = CalculateFitDistance(bounds, targetRotation);

        Vector3 right = targetRotation * Vector3.right;
        Vector3 up = targetRotation * Vector3.up;
        Vector3 targetCenter = bounds.center + right * framingOffset.x + up * framingOffset.y;
        Vector3 targetPosition = targetCenter - viewDirection * distance;

        isFocused = true;
        StartCoroutine(MoveCamera(targetPosition, targetRotation, true));
    }

    private void ExitFrontView()
    {
        StartCoroutine(MoveCamera(previousCameraPosition, previousCameraRotation, false));
    }

    private IEnumerator MoveCamera(Vector3 destinationPosition, Quaternion destinationRotation, bool entering)
    {
        isTransitioning = true;
        Vector3 startPosition = targetCamera.transform.position;
        Quaternion startRotation = targetCamera.transform.rotation;

        if (transitionDuration <= 0f)
        {
            targetCamera.transform.SetPositionAndRotation(destinationPosition, destinationRotation);
        }
        else
        {
            float elapsed = 0f;
            while (elapsed < transitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));
                targetCamera.transform.position = Vector3.Lerp(startPosition, destinationPosition, t);
                targetCamera.transform.rotation = Quaternion.Slerp(startRotation, destinationRotation, t);
                yield return null;
            }

            targetCamera.transform.SetPositionAndRotation(destinationPosition, destinationRotation);
        }

        isFocused = entering;
        isTransitioning = false;

        if (!entering && roomRotation != null)
        {
            roomRotation.enabled = previousRoomRotationEnabled;
        }

        if (!entering)
        {
            PuzzleViewLock.Release(this);
        }
    }

    private void OnDisable()
    {
        PuzzleViewLock.Release(this);
    }

    private bool TryGetBookBounds(out Bounds combinedBounds)
    {
        Renderer[] renderers = booksRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            combinedBounds = default;
            return false;
        }

        combinedBounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            combinedBounds.Encapsulate(renderers[index].bounds);
        }

        return true;
    }

    private Collider CreateFittedEntranceCollider(Transform hotspot)
    {
        if (!TryGetBookBounds(out Bounds worldBounds))
        {
            Debug.LogWarning("[BookPuzzleView] Cannot create the entrance hotspot because no book Renderer was found.", this);
            return null;
        }

        Vector3 localMinimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 localMaximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 worldCorner = worldBounds.center + Vector3.Scale(worldBounds.extents, new Vector3(x, y, z));
            Vector3 localCorner = hotspot.InverseTransformPoint(worldCorner);
            localMinimum = Vector3.Min(localMinimum, localCorner);
            localMaximum = Vector3.Max(localMaximum, localCorner);
        }

        BoxCollider hotspotCollider = hotspot.gameObject.AddComponent<BoxCollider>();
        hotspotCollider.isTrigger = true;
        hotspotCollider.center = (localMinimum + localMaximum) * 0.5f;
        Vector3 localSize = localMaximum - localMinimum + Vector3.one * (hotspotPadding * 2f);
        localSize.z = Mathf.Max(localSize.z, hotspotMinimumDepth);
        hotspotCollider.size = localSize;
        Debug.Log($"[BookPuzzleView] Created a fitted InteractionHotspot collider: center={hotspotCollider.center:F4}, size={hotspotCollider.size:F4}.", hotspot);
        return hotspotCollider;
    }

    private float CalculateFitDistance(Bounds bounds, Quaternion viewRotation)
    {
        Vector3 right = viewRotation * Vector3.right;
        Vector3 up = viewRotation * Vector3.up;
        Vector3 forward = viewRotation * Vector3.forward;
        Vector3 extents = bounds.extents;

        float halfWidth = ProjectExtents(extents, right);
        float halfHeight = ProjectExtents(extents, up);
        float halfDepth = ProjectExtents(extents, forward);

        float verticalHalfFov = targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * targetCamera.aspect);
        float verticalDistance = halfHeight / Mathf.Max(0.001f, Mathf.Tan(verticalHalfFov));
        float horizontalDistance = halfWidth / Mathf.Max(0.001f, Mathf.Tan(horizontalHalfFov));

        return Mathf.Max(verticalDistance, horizontalDistance) + halfDepth + framingPadding;
    }

    private static float ProjectExtents(Vector3 extents, Vector3 axis)
    {
        axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        return Vector3.Dot(extents, axis);
    }
}
