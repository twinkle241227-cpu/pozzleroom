using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Captures the authored pipe-tile rotations as the puzzle's correct state.
/// Interaction is intentionally not implemented here yet; this component only
/// establishes a stable reference pose for the next rotation-puzzle step.
/// </summary>
public sealed class PipeRotationPuzzleController : MonoBehaviour
{
    private readonly Dictionary<Transform, Quaternion> correctLocalRotations =
        new Dictionary<Transform, Quaternion>();

    [SerializeField] private float rotationStepDegrees = 90f;

    private Camera targetCamera;
    private Transform tilesRoot;
    private PipePuzzleViewController viewController;

    public IReadOnlyDictionary<Transform, Quaternion> CorrectLocalRotations => correctLocalRotations;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachAndCaptureCorrectState()
    {
        Transform pipeMove = FindSceneTransform("PipeMove");
        if (pipeMove != null && pipeMove.GetComponent<PipeRotationPuzzleController>() == null)
        {
            pipeMove.gameObject.AddComponent<PipeRotationPuzzleController>();
        }
    }

    private void Awake()
    {
        targetCamera = Camera.main;
        tilesRoot = transform.Find("pipe");
        viewController = GetComponent<PipePuzzleViewController>();
        CaptureCorrectState();
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (viewController == null)
        {
            viewController = GetComponent<PipePuzzleViewController>();
        }

        if (targetCamera == null || viewController == null || !viewController.IsFocused ||
            !Input.GetMouseButtonDown(0) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        TryRotateTileUnderPointer();
    }

    public void CaptureCorrectState()
    {
        correctLocalRotations.Clear();

        if (tilesRoot == null)
        {
            tilesRoot = transform.Find("pipe");
        }

        if (tilesRoot == null)
        {
            Debug.LogWarning("[PipePuzzle] Could not find the 'pipe' tile root under PipeMove.", this);
            return;
        }

        foreach (Transform tile in tilesRoot)
        {
            if (tile.GetComponentInChildren<Renderer>(true) == null)
            {
                continue;
            }

            correctLocalRotations.Add(tile, tile.localRotation);
        }

        Debug.Log($"[PipePuzzle] Recorded the current correct local rotations for {correctLocalRotations.Count} pipe tiles. Tile '8' is recorded but will be excluded from completion checks.", this);
    }

    public bool TryGetCorrectLocalRotation(Transform tile, out Quaternion correctRotation)
    {
        return correctLocalRotations.TryGetValue(tile, out correctRotation);
    }

    private void TryRotateTileUnderPointer()
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        foreach (RaycastHit hit in Physics.RaycastAll(
                     ray,
                     Mathf.Infinity,
                     Physics.DefaultRaycastLayers,
                     QueryTriggerInteraction.Collide))
        {
            Transform tile = FindDirectTileParent(hit.collider.transform);
            if (tile == null)
            {
                continue;
            }

            // Rotate every tile around the board's shared world-space normal.
            // This is independent of the imported model's inconsistent local
            // axes and remains correct when the whole room is rotated.
            Vector3 lockedWorldPosition = tile.position;
            Vector3 wallNormal = transform.forward.normalized;
            Quaternion rotationStep = Quaternion.AngleAxis(-rotationStepDegrees, wallNormal);
            tile.rotation = rotationStep * tile.rotation;
            tile.position = lockedWorldPosition;
            Physics.SyncTransforms();
            Debug.Log($"[PipePuzzle] Rotated tile '{tile.name}' clockwise by {rotationStepDegrees:0}° around the PipeMove wall normal {wallNormal:F3}.", tile);
            return;
        }
    }

    private Transform FindDirectTileParent(Transform candidate)
    {
        while (candidate != null && candidate.parent != tilesRoot)
        {
            candidate = candidate.parent;
        }

        return candidate != null && candidate.parent == tilesRoot ? candidate : null;
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
