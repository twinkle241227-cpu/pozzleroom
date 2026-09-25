using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Focuses the main camera on a hand-authored photo-wall view when this object's
/// collider is clicked. Right-click or Escape restores the room view.
/// </summary>
public sealed class PhotoWallViewController : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform photoWallCameraTarget;
    [SerializeField] private Transform photoWallRoot;
    [SerializeField] private RoomPivotDragController roomRotation;

    [Header("Transition")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.4f;
    [SerializeField] private bool ignorePointerOverUi = true;

    [Header("Automatic Framing Fallback")]
    [SerializeField, Min(0f)] private float framingPadding = 0.35f;
    [SerializeField] private Vector2 framingOffset = Vector2.zero;

    [Header("Photo Hover Outline")]
    [SerializeField] private Color hoverOutlineColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField, Min(0.00001f)] private float hoverOutlineWidth = 0.001f;

    [Header("Entrance Hit Area")]
    [SerializeField, Min(0f)] private float entrancePadding = 0.001f;
    [SerializeField, Min(0.00001f)] private float entranceDepth = 0.001f;

    private BoxCollider entranceCollider;
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousRoomRotationEnabled;
    private bool isFocused;
    private bool isTransitioning;
    private PhotoWallShuffleController shuffleController;
    private PhotoWallPuzzleController puzzleController;
    private bool hasShuffled;
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
            if (photoWallRoot.GetComponent<PhotoWallHoverDebug>() == null)
            {
                photoWallRoot.gameObject.AddComponent<PhotoWallHoverDebug>();
            }
        }
        CreateEntranceCollider();
    }

    private void Update()
    {
        if (!Application.isPlaying || targetCamera == null || entranceCollider == null || isTransitioning) return;

        if (isFocused)
        {
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                StartCoroutine(MoveCamera(previousCameraPosition, previousCameraRotation, false));
            return;
        }

        if (!Input.GetMouseButtonDown(0) ||
            (ignorePointerOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider == entranceCollider)
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

        previousCameraPosition = targetCamera.transform.position;
        previousCameraRotation = targetCamera.transform.rotation;
        if (roomRotation != null)
        {
            previousRoomRotationEnabled = roomRotation.enabled;
            roomRotation.enabled = false;
        }

        if (photoWallCameraTarget != null)
        {
            entranceCollider.enabled = false;
            StartCoroutine(MoveCamera(photoWallCameraTarget.position, photoWallCameraTarget.rotation, true));
            return;
        }

        if (!TryGetPhotoWallBounds(out Bounds bounds))
        {
            Debug.LogWarning("Photo-wall automatic framing found no renderers.", this);
            if (roomRotation != null) roomRotation.enabled = previousRoomRotationEnabled;
            return;
        }

        Vector3 outwardNormal = surfaceNormal.normalized;
        if (Vector3.Dot(outwardNormal, targetCamera.transform.position - bounds.center) < 0f)
            outwardNormal = -outwardNormal;

        Quaternion targetRotation = Quaternion.LookRotation(-outwardNormal, Vector3.up);
        float distance = CalculateFitDistance(bounds, targetRotation);
        Vector3 targetCenter = bounds.center +
            targetRotation * Vector3.right * framingOffset.x +
            targetRotation * Vector3.up * framingOffset.y;

        entranceCollider.enabled = false;
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
        if (entering && !hasShuffled && shuffleController != null)
        {
            shuffleController.ShufflePhotos();
            hasShuffled = true;
        }
        isFocused = entering;
        if (entering && puzzleController != null)
        {
            puzzleController.Initialize(shuffleController);
        }
        isTransitioning = false;
        if (!entering)
        {
            if (roomRotation != null) roomRotation.enabled = previousRoomRotationEnabled;
            if (entranceCollider != null) entranceCollider.enabled = true;
        }
    }

    private void CreateEntranceCollider()
    {
        if (photoWallRoot == null || targetCamera == null || !TryGetLocalPhotoWallBounds(out Vector3 minimum, out Vector3 maximum))
        {
            Debug.LogWarning("Photo-wall entrance collider could not be created.", this);
            return;
        }

        entranceCollider = gameObject.AddComponent<BoxCollider>();
        entranceCollider.isTrigger = false;

        Vector3 size = maximum - minimum;
        Vector3 localCenter = (minimum + maximum) * 0.5f;
        int normalAxis = GetSmallestAxis(size);
        float cameraCoordinate = GetAxis(transform.InverseTransformPoint(targetCamera.transform.position), normalAxis);
        float wallCoordinate = GetAxis(localCenter, normalAxis);
        float outwardSign = cameraCoordinate >= wallCoordinate ? 1f : -1f;
        float outwardSurface = outwardSign > 0f ? GetAxis(maximum, normalAxis) : GetAxis(minimum, normalAxis);

        SetAxis(ref size, normalAxis, entranceDepth);
        SetAxis(ref localCenter, normalAxis, outwardSurface + outwardSign * entranceDepth * 0.5f);
        size += new Vector3(entrancePadding * 2f, entrancePadding * 2f, entrancePadding * 2f);
        SetAxis(ref size, normalAxis, entranceDepth);

        entranceCollider.center = localCenter;
        entranceCollider.size = size;
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
