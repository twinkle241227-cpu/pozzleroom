using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Read-only diagnostics for pipe puzzle tiles. It reports both local and
/// world transform values and identifies the axis used by each rotation.
/// </summary>
public sealed class PipePuzzleTransformDebug : MonoBehaviour
{
    private const float PositionChangeEpsilon = 0.0001f;
    private const float RotationChangeEpsilon = 0.01f;

    private readonly Dictionary<Transform, Vector3> previousLocalPositions =
        new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Quaternion> previousLocalRotations =
        new Dictionary<Transform, Quaternion>();

    private Transform tilesRoot;

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
        tilesRoot = transform.Find("pipe");
        CaptureInitialState();
    }

    private void LateUpdate()
    {
        foreach (Transform tile in previousLocalRotations.Keys)
        {
            if (tile == null)
            {
                continue;
            }

            bool positionChanged = Vector3.Distance(previousLocalPositions[tile], tile.localPosition) > PositionChangeEpsilon;
            bool rotationChanged = Quaternion.Angle(previousLocalRotations[tile], tile.localRotation) > RotationChangeEpsilon;
            if (!positionChanged && !rotationChanged)
            {
                continue;
            }

            Debug.Log(BuildStateReport(tile, positionChanged, rotationChanged), tile);
            previousLocalPositions[tile] = tile.localPosition;
            previousLocalRotations[tile] = tile.localRotation;
        }
    }

    private void CaptureInitialState()
    {
        if (tilesRoot == null)
        {
            Debug.LogWarning("[PipeTransformDebug] Missing PipeMove/pipe root; no state was recorded.", this);
            return;
        }

        foreach (Transform tile in tilesRoot)
        {
            if (tile.GetComponentInChildren<Renderer>(true) == null)
            {
                continue;
            }

            previousLocalPositions[tile] = tile.localPosition;
            previousLocalRotations[tile] = tile.localRotation;
            Debug.Log(BuildStateReport(tile, false, false), tile);
        }
    }

    private string BuildStateReport(Transform tile, bool positionChanged, bool rotationChanged)
    {
        StringBuilder report = new StringBuilder("[PipeTransformDebug] ");
        report.Append($"tile='{tile.name}'; changedPos={positionChanged}; changedRot={rotationChanged}; ");
        report.Append($"LOCAL: pos={tile.localPosition:F4}, euler={tile.localEulerAngles:F2}; ");
        report.Append($"WORLD: pos={tile.position:F4}, euler={tile.eulerAngles:F2}; ");
        report.Append($"rotationWrite=tile.localRotation (Local space, local X axis); ");

        if (rotationChanged)
        {
            // The interaction appends its step to tile.localRotation, so this
            // order reports the actual axis in the tile's local rotation space.
            Quaternion delta = Quaternion.Inverse(previousLocalRotations[tile]) * tile.localRotation;
            delta.ToAngleAxis(out float angle, out Vector3 localAxis);
            NormalizeAngleAndAxis(ref angle, ref localAxis);
            Vector3 worldAxis = tile.parent != null
                ? tile.parent.TransformDirection(localAxis).normalized
                : localAxis;
            report.Append($"deltaLocal: angle={angle:F2}°, axis={localAxis:F3}; ");
            report.Append($"sameAxisInWorld={worldAxis:F3}; ");
        }

        report.Append($"tileAxesInWorld: X={tile.right:F3}, Y={tile.up:F3}, Z={tile.forward:F3}");
        return report.ToString();
    }

    private static void NormalizeAngleAndAxis(ref float angle, ref Vector3 axis)
    {
        if (angle > 180f)
        {
            angle = 360f - angle;
            axis = -axis;
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
