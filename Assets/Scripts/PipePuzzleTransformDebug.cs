using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Read-only diagnostics for the pipe puzzle. Reports the selected tile,
/// its local/world axes, and the actual rotation delta applied during a click.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class PipePuzzleTransformDebug : MonoBehaviour
{
    private const float PositionChangeEpsilon = 0.0001f;
    private const float RotationChangeEpsilon = 0.01f;

    [Header("Logging")]
    [SerializeField] private bool logInitialState;
    [SerializeField] private bool logPointerHits = true;
    [SerializeField] private bool logTransformChanges = true;

    private readonly List<Transform> trackedTiles = new List<Transform>();
    private readonly Dictionary<Transform, Vector3> previousLocalPositions = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Quaternion> previousLocalRotations = new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, Quaternion> previousWorldRotations = new Dictionary<Transform, Quaternion>();

    private Camera targetCamera;
    private Transform tilesRoot;
    private PipePuzzleViewController viewController;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPipePuzzle()
    {
        Transform pipeMove = FindSceneTransform("PipeMove");
        if (pipeMove != null && pipeMove.GetComponent<PipePuzzleTransformDebug>() == null)
        {
            pipeMove.gameObject.AddComponent<PipePuzzleTransformDebug>();
        }
    }

    private void Awake()
    {
        targetCamera = Camera.main;
        tilesRoot = transform.Find("pipe");
        viewController = GetComponent<PipePuzzleViewController>();
        CaptureInitialState();
    }

    private void LateUpdate()
    {
        // Run after the rotation controller so this frame's report contains
        // the rotation that was actually applied by the click.
        if (logPointerHits && Input.GetMouseButtonDown(0) && viewController != null && viewController.IsFocused)
        {
            LogPointerHitReport();
        }

        for (int index = 0; index < trackedTiles.Count; index++)
        {
            Transform tile = trackedTiles[index];
            if (tile == null)
            {
                continue;
            }

            Vector3 previousPosition = previousLocalPositions[tile];
            Quaternion previousLocalRotation = previousLocalRotations[tile];
            Quaternion previousWorldRotation = previousWorldRotations[tile];
            bool positionChanged = Vector3.Distance(previousPosition, tile.localPosition) > PositionChangeEpsilon;
            bool rotationChanged = Quaternion.Angle(previousLocalRotation, tile.localRotation) > RotationChangeEpsilon;

            if (logTransformChanges && (positionChanged || rotationChanged))
            {
                Debug.Log(BuildStateReport(tile, positionChanged, rotationChanged, previousLocalRotation, previousWorldRotation), tile);
            }

            // Iterating a separate List avoids invalidating a Dictionary enumerator.
            previousLocalPositions[tile] = tile.localPosition;
            previousLocalRotations[tile] = tile.localRotation;
            previousWorldRotations[tile] = tile.rotation;
        }
    }

    private void CaptureInitialState()
    {
        if (tilesRoot == null)
        {
            Debug.LogWarning("[PipeAxisDebug] Missing PipeMove/pipe root; no state was recorded.", this);
            return;
        }

        foreach (Transform tile in tilesRoot)
        {
            if (tile.GetComponentInChildren<Renderer>(true) == null)
            {
                continue;
            }

            trackedTiles.Add(tile);
            previousLocalPositions[tile] = tile.localPosition;
            previousLocalRotations[tile] = tile.localRotation;
            previousWorldRotations[tile] = tile.rotation;

            if (logInitialState)
            {
                Debug.Log(BuildStateReport(tile, false, false, tile.localRotation, tile.rotation), tile);
            }
        }

        Debug.Log($"[PipeAxisDebug] Tracking {trackedTiles.Count} pipe tiles. Click a tile in the focused view to print its axes and actual rotation delta.", this);
    }

    private void LogPointerHitReport()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null || tilesRoot == null)
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        StringBuilder report = new StringBuilder("[PipeAxisDebug][CLICK] ");
        report.Append($"screen={Input.mousePosition}; rayOrigin={ray.origin:F3}; rayDirection={ray.direction:F3}; ");

        Transform firstTile = null;
        for (int index = 0; index < hits.Length; index++)
        {
            Transform tile = FindDirectTileParent(hits[index].collider.transform);
            if (tile == null)
            {
                continue;
            }

            if (firstTile == null)
            {
                firstTile = tile;
            }

            report.Append($"hit[{index}] collider='{GetPath(hits[index].collider.transform)}', tile='{tile.name}', distance={hits[index].distance:F3}; ");
        }

        if (firstTile == null)
        {
            report.Append("no pipe tile was hit.");
            Debug.Log(report.ToString(), this);
            return;
        }

        report.Append($"nearestTile='{firstTile.name}'; ");
        report.Append($"tileAxesWorld: X(right)={firstTile.right:F3}, Y(up)={firstTile.up:F3}, Z(forward)={firstTile.forward:F3}; ");
        report.Append($"controllerCommand=rotate around PipeMove wall normal; wallNormal={transform.forward.normalized:F3}. ");
        report.Append("The tile should rotate inside the wall plane and keep its world position.");
        Debug.Log(report.ToString(), firstTile);
    }

    private string BuildStateReport(Transform tile, bool positionChanged, bool rotationChanged,
        Quaternion previousLocalRotation, Quaternion previousWorldRotation)
    {
        StringBuilder report = new StringBuilder("[PipeAxisDebug][CHANGE] ");
        report.Append($"tile='{tile.name}'; changedPos={positionChanged}; changedRot={rotationChanged}; ");
        report.Append($"LOCAL: pos={tile.localPosition:F4}, euler={tile.localEulerAngles:F2}; ");
        report.Append($"WORLD: pos={tile.position:F4}, euler={tile.eulerAngles:F2}; ");
        report.Append($"axesWorld: X(right)={tile.right:F3}, Y(up)={tile.up:F3}, Z(forward)={tile.forward:F3}; ");

        if (rotationChanged)
        {
            Quaternion localDelta = Quaternion.Inverse(previousLocalRotation) * tile.localRotation;
            localDelta.ToAngleAxis(out float localAngle, out Vector3 localAxis);
            NormalizeAngleAndAxis(ref localAngle, ref localAxis);

            Quaternion worldDelta = tile.rotation * Quaternion.Inverse(previousWorldRotation);
            worldDelta.ToAngleAxis(out float worldAngle, out Vector3 worldAxis);
            NormalizeAngleAndAxis(ref worldAngle, ref worldAxis);

            Vector3 commandedWallNormal = transform.forward.normalized;
            float axisAgreement = Mathf.Abs(Vector3.Dot(worldAxis.normalized, commandedWallNormal));
            report.Append($"actualDeltaLocal: angle={localAngle:F2} deg, axis={localAxis:F3}; ");
            report.Append($"actualDeltaWorld: angle={worldAngle:F2} deg, axis={worldAxis:F3}; ");
            report.Append($"commandedWallNormal={commandedWallNormal:F3}; axisAgreement={axisAgreement:F4}; ");
            report.Append(axisAgreement > 0.999f
                ? "RESULT=rotation is around the wall normal."
                : "RESULT=rotation axis does NOT match the wall normal.");
        }

        return report.ToString();
    }

    private Transform FindDirectTileParent(Transform candidate)
    {
        while (candidate != null && candidate.parent != tilesRoot)
        {
            candidate = candidate.parent;
        }

        return candidate != null && candidate.parent == tilesRoot ? candidate : null;
    }

    private static string GetPath(Transform value)
    {
        if (value == null)
        {
            return "<null>";
        }

        string path = value.name;
        while (value.parent != null)
        {
            value = value.parent;
            path = value.name + "/" + path;
        }

        return path;
    }

    private static void NormalizeAngleAndAxis(ref float angle, ref Vector3 axis)
    {
        if (angle > 180f)
        {
            angle = 360f - angle;
            axis = -axis;
        }

        if (axis.sqrMagnitude > 0.000001f)
        {
            axis.Normalize();
        }
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
