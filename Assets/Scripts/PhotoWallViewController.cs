using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Focuses the main camera on a hand-authored photo-wall view when any part of
/// the photo wall is clicked. Right-click restores the room view.
/// </summary>
public sealed class PhotoWallViewController : MonoBehaviour
{
    /// <summary>
    /// True from the moment the photo-wall transition starts until the player
    /// finishes leaving it. Other puzzle entrances use this as a modal input
    /// lock so overlapping colliders cannot steal a photo click.
    /// </summary>
    public static bool IsPhotoWallInteractionActive { get; private set; }

    [Header("Scene References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform photoWallCameraTarget;
    [SerializeField] private Transform photoWallRoot;
    [SerializeField] private RoomPivotDragController roomRotation;

    [Header("Transition")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.4f;
    [SerializeField] private bool ignorePointerOverUi = true;

    [Header("Automatic Framing Fallback")]
    [Tooltip("Main Camera 进入照片墙正视角后，到照片墙平面的距离。数值越小越靠近。")]
    [SerializeField, Min(0.01f)] private float cameraDistance = 0.8f;
    [SerializeField, Min(0f)] private float framingPadding = 0.35f;
    [SerializeField] private Vector2 framingOffset = Vector2.zero;

    [Header("Photo Outlines")]
    [SerializeField] private Color hoverOutlineColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField, Min(0.00001f)] private float hoverOutlineWidth = 0.001f;
    [Tooltip("照片放到正确编号位置后持续显示的描边颜色。")]
    [SerializeField] private Color correctOutlineColor = new Color(0.25f, 1f, 0.38f, 1f);

    [Header("Entrance Hit Area")]
    [SerializeField, Min(0f)] private float entrancePadding = 0.001f;
    [SerializeField, Min(0.00001f)] private float entranceDepth = 0.001f;
    [SerializeField, Range(0f, 1f)] private float minimumFrontFacingDot = 0.342f;

    private Collider frameCollider;
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousRoomRotationEnabled;
    private bool isFocused;
    private bool isTransitioning;
    private PhotoWallShuffleController shuffleController;
    private PhotoWallPuzzleController puzzleController;
    private bool hasShuffled;
    private int entranceNormalAxis;
    private float entranceOutwardSign = 1f;
    public bool IsFocused => isFocused;
    public bool IsTransitioning => isTransitioning;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (photoWallRoot == null) photoWallRoot = FindPhotoWallRoot();
        if (roomRotation == null) roomRotation = FindObjectOfType<RoomPivotDragController>();
        if (photoWallRoot != null)
        {
            shuffleController = photoWallRoot.GetComponent<PhotoWallShuffleController>();
            if (shuffleController == null)
            {
                shuffleController = photoWallRoot.gameObject.AddComponent<PhotoWallShuffleController>();
            }

            shuffleController.ConfigureHoverOutline(hoverOutlineColor, hoverOutlineWidth);
            puzzleController = photoWallRoot.GetComponent<PhotoWallPuzzleController>();
            if (puzzleController == null)
            {
                puzzleController = photoWallRoot.gameObject.AddComponent<PhotoWallPuzzleController>();
            }
            puzzleController.ConfigureCorrectOutline(correctOutlineColor);
            if (photoWallRoot.GetComponent<PhotoWallHoverDebug>() == null)
            {
                photoWallRoot.gameObject.AddComponent<PhotoWallHoverDebug>();
            }
        }
        frameCollider = FindFrameCollider();
        if (frameCollider == null)
            Debug.LogWarning("Photo-wall entry could not find the authored frame Collider; it will use the clicked photo-wall Collider normal.", this);
    }

    private void OnValidate()
    {
        if (photoWallRoot == null)
        {
            return;
        }

        PhotoWallPuzzleController controller = photoWallRoot.GetComponent<PhotoWallPuzzleController>();
        if (controller != null)
        {
            controller.ConfigureCorrectOutline(correctOutlineColor);
        }
    }

    private void Start()
    {
        // The puzzle must already look scrambled in the room before the player
        // clicks it.  Keep hasShuffled so re-entering the view does not reset
        // the player's progress.
        if (Application.isPlaying && !hasShuffled && shuffleController != null)
        {
            shuffleController.ShufflePhotos();
            hasShuffled = true;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || targetCamera == null || photoWallRoot == null || isTransitioning) return;

        if (PuzzleViewLock.IsLockedByOther(this)) return;

        if (isFocused)
        {
            if (Input.GetMouseButtonDown(1))
                StartCoroutine(MoveCamera(previousCameraPosition, previousCameraRotation, false));
            return;
        }

        if (!Input.GetMouseButtonDown(0) ||
            (ignorePointerOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        if (TryGetPhotoWallHit(ray, out RaycastHit hit))
            EnterPhotoWallView(hit.normal);
    }

    public void EnterPhotoWallView()
    {
        EnterPhotoWallView(transform.forward);
    }

    private void EnterPhotoWallView(Vector3 surfaceNormal)
    {
        if (isFocused || isTransitioning || targetCamera == null)
        {
            return;
        }

        if (!PuzzleViewLock.TryAcquire(this))
        {
            return;
        }

        previousCameraPosition = targetCamera.transform.position;
        previousCameraRotation = targetCamera.transform.rotation;
        IsPhotoWallInteractionActive = true;
        if (roomRotation != null)
        {
            previousRoomRotationEnabled = roomRotation.enabled;
            roomRotation.enabled = false;
        }

        if (photoWallCameraTarget != null)
        {
            StartCoroutine(MoveCamera(photoWallCameraTarget.position, photoWallCameraTarget.rotation, true));
            return;
        }

        if (!TryGetPhotoWallBounds(out Bounds bounds))
        {
            Debug.LogWarning("Photo-wall automatic framing found no renderers.", this);
            if (roomRotation != null) roomRotation.enabled = previousRoomRotationEnabled;
            IsPhotoWallInteractionActive = false;
            PuzzleViewLock.Release(this);
            return;
        }

        Vector3 outwardNormal = surfaceNormal.normalized;
        if (Vector3.Dot(outwardNormal, targetCamera.transform.position - bounds.center) < 0f)
            outwardNormal = -outwardNormal;

        Quaternion targetRotation = Quaternion.LookRotation(-outwardNormal, Vector3.up);
        // The distance is deliberately authored in the Inspector so the
        // designer can tune the photo-wall close-up without moving an anchor.
        float distance = cameraDistance;
        Vector3 targetCenter = bounds.center +
            targetRotation * Vector3.right * framingOffset.x +
            targetRotation * Vector3.up * framingOffset.y;

        StartCoroutine(MoveCamera(targetCenter + outwardNormal * distance, targetRotation, true));
    }

    public void ExitPhotoWallView()
    {
        if (!isFocused || isTransitioning || targetCamera == null) return;
        StartCoroutine(MoveCamera(previousCameraPosition, previousCameraRotation, false));
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
        if (entering && puzzleController != null)
        {
            puzzleController.Initialize(shuffleController);
        }
        isTransitioning = false;
        if (!entering)
        {
            IsPhotoWallInteractionActive = false;
            PuzzleViewLock.Release(this);
            if (roomRotation != null) roomRotation.enabled = previousRoomRotationEnabled;
        }
    }

    private void OnDisable()
    {
        PuzzleViewLock.Release(this);
        if (isFocused || isTransitioning)
        {
            IsPhotoWallInteractionActive = false;
        }
    }

    private Collider FindFrameCollider()
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid() ||
                !string.Equals(candidate.name, "frame", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Collider collider = candidate.GetComponent<Collider>();
            if (collider == null)
            {
                collider = candidate.GetComponentInChildren<Collider>(true);
            }

            if (collider != null)
            {
                return collider;
            }
        }

        return null;
    }

    private static bool IsFrameCollider(Collider collider)
    {
        for (Transform candidate = collider.transform; candidate != null; candidate = candidate.parent)
        {
            if (string.Equals(candidate.name, "frame", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the nearest front-facing hit belonging to the photo-wall hierarchy.
    /// RaycastAll is intentional: the photos and a wall collider sit in front of
    /// the authored frame collider, so a single Physics.Raycast cannot reach the
    /// photo wall reliably.
    /// </summary>
    private bool TryGetPhotoWallHit(Ray ray, out RaycastHit selectedHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        float nearestDistance = float.PositiveInfinity;
        selectedHit = default;

        foreach (RaycastHit hit in hits)
        {
            if (!IsPhotoWallCollider(hit.collider) || !IsFrontEntranceClick(hit))
            {
                continue;
            }

            if (hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            selectedHit = hit;
        }

        return nearestDistance < float.PositiveInfinity;
    }

    private bool IsPhotoWallCollider(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        Transform hitTransform = collider.transform;
        return photoWallRoot != null &&
               (hitTransform == photoWallRoot || hitTransform.IsChildOf(photoWallRoot));
    }

    private bool IsFrontEntranceClick(RaycastHit hit)
    {
        Vector3 wallToCamera = targetCamera.transform.position - hit.point;
        if (wallToCamera.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        // Use the actual frame collider face.  This remains valid even when
        // the room has rotated, unlike the former wall-sized entry box.
        float viewAlignment = Vector3.Dot(hit.normal.normalized, wallToCamera.normalized);
        return viewAlignment >= minimumFrontFacingDot;
    }

    private Transform FindPhotoWallRoot()
    {
        for (Transform candidateRoot = transform; candidateRoot != null; candidateRoot = candidateRoot.parent)
        {
            bool foundPhoto = false;
            bool foundPin = false;
            foreach (Transform candidate in candidateRoot.GetComponentsInChildren<Transform>(true))
            {
                string name = candidate.name;
                foundPhoto |= name.IndexOf("photo", System.StringComparison.OrdinalIgnoreCase) >= 0;
                foundPin |= name.IndexOf("pin", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (foundPhoto && foundPin)
                {
                    return candidateRoot;
                }
            }
        }

        return transform.parent != null ? transform.parent : transform;
    }

    private bool TryGetLocalPhotoWallBounds(out Vector3 minimum, out Vector3 maximum)
    {
        minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        Renderer[] renderers = photoWallRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 worldCorner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                minimum = Vector3.Min(minimum, localCorner);
                maximum = Vector3.Max(maximum, localCorner);
            }
        }

        return true;
    }

    private static int GetSmallestAxis(Vector3 value)
    {
        return value.x <= value.y && value.x <= value.z ? 0 : value.y <= value.z ? 1 : 2;
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static void SetAxis(ref Vector3 value, int axis, float component)
    {
        if (axis == 0) value.x = component;
        else if (axis == 1) value.y = component;
        else value.z = component;
    }

    private bool TryGetPhotoWallBounds(out Bounds combinedBounds)
    {
        if (photoWallRoot == null)
        {
            combinedBounds = default;
            return false;
        }

        Renderer[] renderers = photoWallRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            combinedBounds = default;
            return false;
        }

        combinedBounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            combinedBounds.Encapsulate(renderers[index].bounds);
        return true;
    }

    private float CalculateFitDistance(Bounds bounds, Quaternion viewRotation)
    {
        Vector3 extents = bounds.extents;
        float halfWidth = ProjectExtents(extents, viewRotation * Vector3.right);
        float halfHeight = ProjectExtents(extents, viewRotation * Vector3.up);
        float halfDepth = ProjectExtents(extents, viewRotation * Vector3.forward);
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
