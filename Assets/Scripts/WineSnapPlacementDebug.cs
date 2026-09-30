using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Read-only diagnostics for wine/glass snapping. It records the clicked
/// model before dragging and reports every compatible slot again after the
/// puzzle controller has processed the mouse release.
/// </summary>
public sealed class WineSnapPlacementDebug : MonoBehaviour
{
    [SerializeField] private bool logPointerDown = true;
    [SerializeField] private bool logAfterRelease = true;

    private WineRotationPuzzleController puzzle;
    private Transform trackedItem;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToWineDisplay()
    {
        string[] rootNames = { "酒瓶", "酒杯", "酒瓶酒杯" };
        foreach (string rootName in rootNames)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null)
            {
                continue;
            }

            if (root.GetComponent<WineSnapPlacementDebug>() == null)
            {
                root.AddComponent<WineSnapPlacementDebug>();
            }
            return;
        }
    }

    private void Awake()
    {
        puzzle = GetComponent<WineRotationPuzzleController>();
    }

    private void Update()
    {
        if (puzzle == null)
        {
            puzzle = GetComponent<WineRotationPuzzleController>();
            if (puzzle == null) return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TrackPointerDown();
        }

        if (trackedItem != null && Input.GetMouseButtonUp(0))
        {
            StartCoroutine(LogAfterPuzzleRelease(trackedItem));
            trackedItem = null;
        }
    }

    private void TrackPointerDown()
    {
        Camera cameraToUse = Camera.main;
        if (cameraToUse == null) return;

        RaycastHit[] hits = Physics.RaycastAll(
            cameraToUse.ScreenPointToRay(Input.mousePosition),
            Mathf.Infinity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (!puzzle.TryBuildSnapDebugReport(hit.collider.transform, "PointerDown", out Transform playable, out string report))
            {
                continue;
            }

            trackedItem = playable;
            if (logPointerDown) Debug.Log(report, this);
            return;
        }
    }

    private IEnumerator LogAfterPuzzleRelease(Transform releasedItem)
    {
        // Update order between MonoBehaviours is not guaranteed. Waiting until
        // end of frame ensures the puzzle has already snapped or rejected it.
        yield return new WaitForEndOfFrame();
        if (!logAfterRelease || releasedItem == null) yield break;

        if (puzzle.TryBuildSnapDebugReport(releasedItem, "AfterRelease", out _, out string report))
        {
            Debug.Log(report, this);
        }
    }
}
