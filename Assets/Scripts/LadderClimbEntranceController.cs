using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens a front-facing ladder view only when the ladder has been lowered and
/// the player clicks the dedicated stair collider, then scrolls to climb.
/// </summary>
public sealed class LadderClimbEntranceController : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("Distance from the ladder center to the front-view camera.")]
    [SerializeField, Min(0.1f)] private float cameraDistance = 2f;

    private const float TransitionDuration = 0.35f;
    private const float ScrollStepFraction = 0.12f;
    private const float ClimbSmoothTime = 0.12f;
    private Camera targetCamera;
    private Transform ladder;
    private Transform stair;
    private LadderScrollRotationController ladderRotation;
    private RoomPivotDragController roomRotation;

    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousOrthographic;
    private float previousOrthographicSize;
    private bool previousRoomRotationEnabled;
    private bool isFocused;
    private bool isTransitioning;
    private Vector3 climbBasePosition;
    private Quaternion climbRotation;
    private float maximumClimbHeight;
    private float targetClimbHeight;
    private float currentClimbHeight;
    private float climbVelocity;
    private bool externalViewActive;

    public bool IsAtTop => isFocused && !isTransitioning &&
                           maximumClimbHeight > 0f &&
                           currentClimbHeight >= maximumClimbHeight - 0.01f &&
                           targetClimbHeight >= maximumClimbHeight - 0.01f;
    public Camera TargetCamera => targetCamera;

    /// <summary>Temporarily stops this controller from overwriting the camera while a child interaction owns it.</summary>
    public void SetExternalViewActive(bool active)
    {
        externalViewActive = active;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToLadder()
    {
        Transform ladder = FindSceneTransform("ladder");
        if (ladder != null && ladder.GetComponent<LadderClimbEntranceController>() == null)
        {
            ladder.gameObject.AddComponent<LadderClimbEntranceController>();
        }
    }

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (ladder == null)
        {
            ladder = transform;
        }

        if (ladderRotation == null)
        {
            ladderRotation = ladder.GetComponent<LadderScrollRotationController>();
        }

        if (roomRotation == null)
        {
            roomRotation = FindObjectOfType<RoomPivotDragController>();
        }

        if (stair == null)
        {
            stair = FindSceneTransform("stair");
        }
    }

    private void Update()
    {
        if (targetCamera == null || isTransitioning || externalViewActive)
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
                return;
            }

            UpdateClimbFromScroll();
            return;
        }

        if (!Input.GetMouseButtonDown(0) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        if (ladderRotation == null)
        {
            ladderRotation = ladder != null ? ladder.GetComponent<LadderScrollRotationController>() : null;
        }

        if (stair == null)
        {
            stair = FindSceneTransform("stair");
        }

        if (ladderRotation == null || !ladderRotation.IsLowered || stair == null || !IsPointerOverStair())
        {
            return;
        }

        EnterLadderFrontView();
    }

    private bool IsPointerOverStair()
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == stair || hitTransform.IsChildOf(stair))
            {
                return true;
            }
        }

        return false;
    }

    private void EnterLadderFrontView()
    {
        if (!TryGetLadderBounds(out Bounds bounds))
        {
            Debug.LogWarning("[LadderClimb] No ladder renderer was found.", this);
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

        // The stair collider's local axes are not authored as a reliable view
        // reference. The ladder's parent X axis is its stable front normal,
        // including after the ladder itself has been lowered.
        Vector3 outwardNormal = ladder != null && ladder.parent != null
            ? Vector3.ProjectOnPlane(ladder.parent.right, Vector3.up)
            : Vector3.zero;
        if (outwardNormal.sqrMagnitude < 0.0001f)
        {
            outwardNormal = stair != null
                ? Vector3.ProjectOnPlane(stair.forward, Vector3.up)
                : Vector3.ProjectOnPlane(targetCamera.transform.position - bounds.center, Vector3.up);
        }

        outwardNormal.Normalize();
        if (Vector3.Dot(outwardNormal, targetCamera.transform.position - bounds.center) < 0f)
        {
            outwardNormal = -outwardNormal;
        }

        Quaternion targetRotation = Quaternion.LookRotation(-outwardNormal, Vector3.up);
        Vector3 targetPosition = bounds.center + outwardNormal * cameraDistance;

        // Scroll movement follows the vertical ladder route. The ladder's
        // world-space height determines the top limit, so the camera cannot
        // climb through the bed or below the first rung.
        climbBasePosition = targetPosition;
        climbRotation = targetRotation;
        maximumClimbHeight = Mathf.Max(0.1f, bounds.size.y * 0.85f);
        targetClimbHeight = 0f;
        currentClimbHeight = 0f;
        climbVelocity = 0f;

        targetCamera.orthographic = false;
        StartCoroutine(MoveCamera(targetPosition, targetRotation, true));
        Debug.Log("[LadderClimb] Ladder is lowered; entered its front view through stair.", this);
    }

    private IEnumerator MoveCamera(Vector3 destinationPosition, Quaternion destinationRotation, bool entering)
    {
        isTransitioning = true;
        Vector3 startPosition = targetCamera.transform.position;
        Quaternion startRotation = targetCamera.transform.rotation;
        float elapsed = 0f;

        while (elapsed < TransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / TransitionDuration);
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
            PuzzleViewLock.Release(this);
        }
    }

    private void OnDisable()
    {
        PuzzleViewLock.Release(this);
    }

    private void UpdateClimbFromScroll()
    {
        if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
        {
            float scroll = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                targetClimbHeight = Mathf.Clamp(
                    targetClimbHeight + Mathf.Sign(scroll) * maximumClimbHeight * ScrollStepFraction,
                    0f,
                    maximumClimbHeight);
                Debug.Log($"[LadderClimb] Climb progress: {targetClimbHeight / maximumClimbHeight:P0}.", this);
            }
        }

        currentClimbHeight = Mathf.SmoothDamp(
            currentClimbHeight,
            targetClimbHeight,
            ref climbVelocity,
            ClimbSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
        targetCamera.transform.SetPositionAndRotation(
            climbBasePosition + Vector3.up * currentClimbHeight,
            climbRotation);
    }

    private bool TryGetLadderBounds(out Bounds bounds)
    {
        Renderer[] renderers = ladder != null
            ? ladder.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
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
