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
    private readonly Dictionary<Transform, Vector3> correctWorldPositions =
        new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Quaternion> correctWorldRotations =
        new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, char> tileTypes =
        new Dictionary<Transform, char>();
    private readonly Dictionary<Transform, int> quarterTurnOffsets =
        new Dictionary<Transform, int>();

    [SerializeField] private float rotationStepDegrees = 90f;
    [SerializeField] private bool shuffleOnStart = true;

    private Camera targetCamera;
    private Transform tilesRoot;
    private PipePuzzleViewController viewController;
    private bool isSolved;

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

    private void Start()
    {
        if (shuffleOnStart)
        {
            ShuffleTiles();
        }
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

        if (isSolved || targetCamera == null || viewController == null || !viewController.IsFocused ||
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
        correctWorldPositions.Clear();
        correctWorldRotations.Clear();
        tileTypes.Clear();
        quarterTurnOffsets.Clear();

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
            correctWorldPositions.Add(tile, tile.position);
            correctWorldRotations.Add(tile, tile.rotation);
            tileTypes.Add(tile, GetTileType(tile));
            quarterTurnOffsets.Add(tile, 0);
        }

        Debug.Log($"[PipePuzzle] Recorded {correctLocalRotations.Count} correct tile poses: A=straight (0/180), B=elbow (0), C=four-way (all), D=double-elbow (0/180). Tile '8' is excluded from completion checks.", this);
    }

    public bool TryGetCorrectLocalRotation(Transform tile, out Quaternion correctRotation)
    {
        return correctLocalRotations.TryGetValue(tile, out correctRotation);
    }

    /// <summary>
    /// Scrambles every pipe tile, including tile 8, using only non-zero
    /// quarter turns around the shared PipeMove wall normal.
    /// </summary>
    public void ShuffleTiles()
    {
        if (correctWorldRotations.Count == 0)
        {
            return;
        }

        Vector3 wallNormal = transform.forward.normalized;
        foreach (KeyValuePair<Transform, Quaternion> entry in correctWorldRotations)
        {
            Transform tile = entry.Key;
            if (tile == null)
            {
                continue;
            }

            int quarterTurns = UnityEngine.Random.Range(1, 4);
            float angle = rotationStepDegrees * quarterTurns;
            tile.rotation = Quaternion.AngleAxis(-angle, wallNormal) * entry.Value;
            tile.position = correctWorldPositions[tile];
            quarterTurnOffsets[tile] = quarterTurns;
            Debug.Log($"[PipePuzzle] Shuffled tile '{tile.name}' by {angle:0}° around the PipeMove wall normal.", tile);
        }

        Physics.SyncTransforms();
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
            quarterTurnOffsets[tile] = (quarterTurnOffsets[tile] + 1) % 4;
            Physics.SyncTransforms();
            Debug.Log($"[PipePuzzle] Rotated tile '{tile.name}' clockwise by {rotationStepDegrees:0}° around the PipeMove wall normal {wallNormal:F3}.", tile);
            CheckForCompletion();
            return;
        }
    }

    private void CheckForCompletion()
    {
        foreach (KeyValuePair<Transform, int> entry in quarterTurnOffsets)
        {
            Transform tile = entry.Key;
            if (tile == null || string.Equals(tile.name, "8", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsCorrectTurn(tileTypes[tile], entry.Value))
            {
                return;
            }
        }

        isSolved = true;
        Debug.Log("[PipePuzzle] Solved: all pipe tiles are now locked, including tile '8'.", this);
    }

    private static char GetTileType(Transform tile)
    {
        if (tile == null || string.IsNullOrEmpty(tile.name))
        {
            return '\0';
        }

        return char.ToUpperInvariant(tile.name[0]);
    }

    private static bool IsCorrectTurn(char tileType, int quarterTurns)
    {
        switch (tileType)
        {
            case 'A': // Straight pipe: 0° and 180° have the same connections.
            case 'D': // Two mirrored elbows: 0° and 180° are equivalent.
                return quarterTurns == 0 || quarterTurns == 2;
            case 'C': // Four-way pipe: every quarter turn is equivalent.
                return true;
            case 'B': // Single elbow: only the recorded reference direction fits.
            default:
                return quarterTurns == 0;
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
