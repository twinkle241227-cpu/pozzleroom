using System;
using UnityEngine;

/// <summary>
/// Read-only diagnostics for the wine puzzle. It reports the actual pose of
/// the clicked direct correct model against the target pose recorded before
/// gameplay began. It never changes any transforms or puzzle state.
/// </summary>
public sealed class WineCorrectPoseDebug : MonoBehaviour
{
    [SerializeField] private bool logPointerDown = true;
    [SerializeField] private bool logPointerRelease = true;

    private WineRotationPuzzleController puzzle;
    private Transform trackedItem;
    private string trackedPairId;
    private Vector3 trackedTargetPosition;
    private Quaternion trackedTargetRotation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToWineDisplay()
    {
        GameObject root = GameObject.Find("酒瓶");
        if (root != null && root.GetComponent<WineCorrectPoseDebug>() == null)
        {
            root.AddComponent<WineCorrectPoseDebug>();
        }
    }

    private void Start()
    {
        puzzle = GetComponent<WineRotationPuzzleController>();
    }

    private void Update()
    {
        if (puzzle == null)
        {
            puzzle = GetComponent<WineRotationPuzzleController>();
            if (puzzle == null)
            {
                return;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryTrackPointerDown();
        }

        if (trackedItem != null && Input.GetMouseButtonUp(0))
        {
            if (logPointerRelease)
            {
                LogPose("Release");
            }

            trackedItem = null;
            trackedPairId = null;
        }
    }

    private void TryTrackPointerDown()
    {
        Camera cameraToUse = Camera.main;
        if (cameraToUse == null)
        {
            Debug.LogWarning("[WinePoseDebug] PointerDown: Main Camera was not found.", this);
            return;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            cameraToUse.ScreenPointToRay(Input.mousePosition),
            Mathf.Infinity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (!puzzle.TryGetCorrectPoseForDebug(
                    hit.collider.transform,
                    out Transform playable,
                    out string pairId,
                    out Vector3 targetPosition,
                    out Quaternion targetRotation))
            {
                continue;
            }

            trackedItem = playable;
            trackedPairId = pairId;
            trackedTargetPosition = targetPosition;
            trackedTargetRotation = targetRotation;

            if (logPointerDown)
            {
                LogPose("PointerDown");
            }

            return;
        }

        Debug.Log("[WinePoseDebug] PointerDown: no registered wine model was hit.", this);
    }

    private void LogPose(string phase)
    {
        if (trackedItem == null)
        {
            return;
        }

        float positionError = Vector3.Distance(trackedItem.position, trackedTargetPosition);
        float rotationError = Quaternion.Angle(trackedItem.rotation, trackedTargetRotation);
        Debug.Log(
            $"[WinePoseDebug] {phase}; item={trackedItem.name}; pair={trackedPairId}; " +
            $"actualPosition={trackedItem.position}; targetPosition={trackedTargetPosition}; " +
            $"positionError={positionError:F4}; actualRotation={trackedItem.eulerAngles}; " +
            $"targetRotation={trackedTargetRotation.eulerAngles}; rotationError={rotationError:F2}°.",
            this);
    }
}
