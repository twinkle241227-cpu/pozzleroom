using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens the wine-bottle and wine-glass display as a focused front view.
/// Any Collider on the display root or a child can be used as the entrance.
/// </summary>
public sealed class WineDisplayViewController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float transitionDuration = 0.4f;
    [SerializeField, Min(0f)] private float framingPadding = 0.15f;

    private Camera targetCamera;
    private RoomPivotDragController roomRotation;
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousOrthographic;
    private float previousOrthographicSize;
    private bool previousRoomRotationEnabled;
    private bool isFocused;
    private bool isTransitioning;

    public bool IsFocused => isFocused && !isTransitioning;
    public Camera TargetCamera => targetCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToWineDisplay()
    {
        Transform root = FindWineDisplayRoot();
        if (root != null && root.GetComponent<WineDisplayViewController>() == null)
        {
            root.gameObject.AddComponent<WineDisplayViewController>();
        }
    }

    private void Awake()
    {
        targetCamera = Camera.main;
        roomRotation = FindObjectOfType<RoomPivotDragController>();
    }

    private void Update()
    {
        if (targetCamera == null || isTransitioning)
        {
            return;
        }

        if (PuzzleViewLock.IsLockedByOther(this))
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
        foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
            {
                EnterFrontView();
                return;
            }
        }
    }

    private void EnterFrontView()
    {
        if (!TryGetDisplayBounds(out Bounds bounds))
        {
            Debug.LogWarning("[WineDisplay] No Renderer was found under the wine display.", this);
            return;
        }

        if (!PuzzleViewLock.TryAcquire(this))
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

        // This group was authored facing its local forward direction. Using
        // that axis gives a true display-front view even when the player is
        // standing off to one side of the shelf.
        Vector3 outwardNormal = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (outwardNormal.sqrMagnitude < 0.0001f)
        {
            outwardNormal = targetCamera.transform.position - bounds.center;
            outwardNormal = Vector3.ProjectOnPlane(outwardNormal, Vector3.up);
        }

        outwardNormal.Normalize();
        if (Vector3.Dot(outwardNormal, targetCamera.transform.position - bounds.center) < 0f)
        {
            outwardNormal = -outwardNormal;
        }

        Quaternion targetRotation = Quaternion.LookRotation(-outwardNormal, Vector3.up);
        float distance = Mathf.Max(0.5f, ProjectExtents(bounds.extents, targetRotation * Vector3.forward) + 1f);
        Vector3 targetPosition = bounds.center + outwardNormal * distance;

        targetCamera.orthographic = true;
        targetCamera.orthographicSize = CalculateOrthographicSize(bounds, targetRotation);
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
            if (roomRotation != null)
            {
                roomRotation.enabled = previousRoomRotationEnabled;
            }
            PuzzleViewLock.Release(this);
        }
    }

    private void OnDisable()
    {
        PuzzleViewLock.Release(this);
    }

    private bool TryGetDisplayBounds(out Bounds bounds)
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

    private float CalculateOrthographicSize(Bounds bounds, Quaternion viewRotation)
    {
        float halfWidth = ProjectExtents(bounds.extents, viewRotation * Vector3.right);
        float halfHeight = ProjectExtents(bounds.extents, viewRotation * Vector3.up);
        return Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.001f, targetCamera.aspect)) + framingPadding;
    }

    private static float ProjectExtents(Vector3 extents, Vector3 axis)
    {
        axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        return Vector3.Dot(extents, axis);
    }

    private static Transform FindWineDisplayRoot()
    {
        string[] rootNames = { "酒瓶", "酒杯", "酒瓶酒杯" };
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid())
            {
                continue;
            }

            foreach (string rootName in rootNames)
            {
                if (string.Equals(candidate.name, rootName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
