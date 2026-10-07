using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Moves the gameplay camera to the authored PipeCamera viewpoint when the
/// player clicks PipeMove or one of its pipe children.
/// </summary>
public sealed class PipePuzzleViewController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float transitionDuration = 0.35f;
    [SerializeField] private Camera pipeCamera;
    [SerializeField, Min(0.001f)] private float focusedNearClipPlane = 0.01f;

    private Camera targetCamera;
    private RoomPivotDragController roomRotation;
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousOrthographic;
    private float previousOrthographicSize;
    private float previousFieldOfView;
    private float previousNearClipPlane;
    private float previousFarClipPlane;
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

        if (pipeCamera == null)
        {
            Transform pipeCameraTransform = FindSceneTransform("PipeCamera");
            if (pipeCameraTransform != null)
            {
                pipeCamera = pipeCameraTransform.GetComponent<Camera>();
            }
        }

        // PipeCamera is an authored viewpoint, not a second live renderer.
        // The main camera stays active so existing Camera.main raycasts work.
        if (pipeCamera != null && pipeCamera != targetCamera)
        {
            pipeCamera.enabled = false;
            AudioListener pipeListener = pipeCamera.GetComponent<AudioListener>();
            if (pipeListener != null)
            {
                pipeListener.enabled = false;
            }
        }
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
        foreach (RaycastHit hit in Physics.RaycastAll(
                     ray,
                     Mathf.Infinity,
                     Physics.DefaultRaycastLayers,
                     QueryTriggerInteraction.Collide))
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                EnterPipeCameraView();
                return;
            }
        }
    }

    private void EnterPipeCameraView()
    {
        if (pipeCamera == null)
        {
            Debug.LogWarning("[PipeView] Could not find the authored 'PipeCamera' viewpoint.", this);
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
        previousFieldOfView = targetCamera.fieldOfView;
        previousNearClipPlane = targetCamera.nearClipPlane;
        previousFarClipPlane = targetCamera.farClipPlane;

        if (roomRotation != null)
        {
            previousRoomRotationEnabled = roomRotation.enabled;
            roomRotation.enabled = false;
        }

        ApplyPipeCameraProjection();
        StartCoroutine(MoveCamera(pipeCamera.transform.position, pipeCamera.transform.rotation, true));
    }

    private void ApplyPipeCameraProjection()
    {
        targetCamera.orthographic = pipeCamera.orthographic;
        targetCamera.orthographicSize = pipeCamera.orthographicSize;
        targetCamera.fieldOfView = pipeCamera.fieldOfView;
        // PipeCamera sits very close to the wall and some fixed elbows protrude
        // toward it. A conventional 0.3 near plane cuts those meshes away, so
        // cap the focused view's near plane at a close-up-safe value.
        targetCamera.nearClipPlane = Mathf.Min(pipeCamera.nearClipPlane, focusedNearClipPlane);
        targetCamera.farClipPlane = pipeCamera.farClipPlane;
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
            float progress = transitionDuration <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);
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
            targetCamera.fieldOfView = previousFieldOfView;
            targetCamera.nearClipPlane = previousNearClipPlane;
            targetCamera.farClipPlane = previousFarClipPlane;

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
