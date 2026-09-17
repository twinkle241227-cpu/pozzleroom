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
    [SerializeField] private RoomPivotDragController roomRotation;
    [SerializeField] private BookPuzzleInteractionController interactionController;

    [Header("Interaction")]
    [SerializeField, Min(0f)] private float clickablePaddingPixels = 60f;
    [SerializeField] private bool ignorePointerOverUi = true;

    [Header("Front View")]
    [SerializeField, Min(0f)] private float framingPadding = 0.25f;
    [SerializeField] private Vector2 framingOffset = Vector2.zero;
    [SerializeField, Min(0f)] private float transitionDuration = 0.35f;

    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousRoomRotationEnabled;
    private bool isFocused;
    private bool isTransitioning;

    public bool IsFocused => isFocused;
    public bool IsTransitioning => isTransitioning;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (targetCamera == null || booksRoot == null || isTransitioning)
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
        Renderer[] renderers = booksRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return false;
        }

        bool hasVisibleCorner = false;
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 screenPoint = targetCamera.WorldToScreenPoint(corner);
                        if (screenPoint.z <= 0f)
                        {
                            continue;
                        }

                        hasVisibleCorner = true;
                        minimum = Vector2.Min(minimum, screenPoint);
                        maximum = Vector2.Max(maximum, screenPoint);
                    }
                }
            }
        }

        if (!hasVisibleCorner)
        {
            return false;
        }

        Rect clickableArea = Rect.MinMaxRect(
            minimum.x - clickablePaddingPixels,
            minimum.y - clickablePaddingPixels,
            maximum.x + clickablePaddingPixels,
            maximum.y + clickablePaddingPixels);

        return clickableArea.Contains(pointerPosition);
    }

    private void EnterFrontView()
    {
        if (!TryGetBookBounds(out Bounds bounds))
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

        Vector3 viewDirection = cameraDirection != null ? cameraDirection.forward : booksRoot.forward;
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
