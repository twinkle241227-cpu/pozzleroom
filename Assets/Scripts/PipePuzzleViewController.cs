using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens the pipe assembly in a perspective front view when the player
/// clicks a collider belonging to PipeMove or one of its pipe children.
/// </summary>
public sealed class PipePuzzleViewController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float transitionDuration = 0.35f;
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPipeAssembly()
    {
        Transform pipeRoot = FindSceneTransform("PipeMove");
        if (pipeRoot != null && pipeRoot.GetComponent<PipePuzzleViewController>() == null)
        {
            pipeRoot.gameObject.AddComponent<PipePuzzleViewController>();
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
        foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                EnterFrontView();
                return;
            }
        }
    }

    private void EnterFrontView()
    {
        if (!TryGetPipeBounds(out Bounds bounds))
        {
            Debug.LogWarning("[PipeView] No Renderer was found under PipeMove.", this);
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

        // PipeMove is authored with its local forward axis pointing out of the
        // pipe board. Re-evaluate it on every entry so room rotation is safe.
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
        float distance = CalculatePerspectiveDistance(bounds, targetRotation);
        Vector3 targetPosition = bounds.center + outwardNormal * distance;

        targetCamera.orthographic = false;
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
            float progress = transitionDuration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);
            targetCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, destinationPosition, progress),
                Quaternion.Slerp(startRotation, destinationRotation, progress));
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
        }
    }

    private bool TryGetPipeBounds(out Bounds bounds)
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

    private float CalculatePerspectiveDistance(Bounds bounds, Quaternion viewRotation)
    {
        float halfWidth = ProjectExtents(bounds.extents, viewRotation * Vector3.right);
        float halfHeight = ProjectExtents(bounds.extents, viewRotation * Vector3.up);
        float halfDepth = ProjectExtents(bounds.extents, viewRotation * Vector3.forward);
        float verticalHalfFov = targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * targetCamera.aspect);
        float verticalDistance = halfHeight / Mathf.Max(0.001f, Mathf.Tan(verticalHalfFov));
        float horizontalDistance = halfWidth / Mathf.Max(0.001f, Mathf.Tan(horizontalHalfFov));
        return Mathf.Max(0.5f, verticalDistance, horizontalDistance) + halfDepth + framingPadding;
    }

    private static float ProjectExtents(Vector3 extents, Vector3 axis)
    {
        axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        return Vector3.Dot(extents, axis);
    }

    private static Transform FindSceneTransform(string objectName)
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate != null && candidate.gameObject.scene.IsValid() &&
                string.Equals(candidate.name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
