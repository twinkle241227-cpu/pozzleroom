using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-only diagnostic logger for the utensil puzzle. It records the
/// state after the initial scatter has finished, then reports every later
/// position or rotation change made to any direct child utensil.
/// </summary>
public sealed class UtensilTransformDebug : MonoBehaviour
{
    [SerializeField] private bool logInitialState = true;
    [SerializeField, Min(0f)] private float positionChangeThreshold = 0.0001f;
    [SerializeField, Min(0f)] private float rotationChangeThreshold = 0.05f;

    private readonly Dictionary<Transform, TransformSnapshot> snapshots = new Dictionary<Transform, TransformSnapshot>();
    private bool isReady;

    private struct TransformSnapshot
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToUtensilRoot()
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid() ||
                !string.Equals(candidate.name, "厨具", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate.GetComponent<UtensilTransformDebug>() == null)
            {
                candidate.gameObject.AddComponent<UtensilTransformDebug>();
            }

            return;
        }
    }

    private IEnumerator Start()
    {
        // UtensilPuzzleController performs its scatter in Start. Waiting one
        // frame gives this debugger the post-scatter state as its baseline.
        yield return null;
        CaptureBaseline();
        isReady = true;
    }

    private void Update()
    {
        if (!isReady)
        {
            return;
        }

        foreach (Transform item in transform)
        {
            if (!IsUtensil(item) || !snapshots.TryGetValue(item, out TransformSnapshot previous))
            {
                continue;
            }

            Vector3 currentPosition = item.position;
            Quaternion currentRotation = item.rotation;
            float positionDelta = Vector3.Distance(previous.Position, currentPosition);
            float rotationDelta = Quaternion.Angle(previous.Rotation, currentRotation);
            if (positionDelta < positionChangeThreshold && rotationDelta < rotationChangeThreshold)
            {
                continue;
            }

            Debug.Log(
                $"[UtensilTransformDebug] CHANGED item={item.name}; " +
                $"position={previous.Position:F4} -> {currentPosition:F4}; deltaPosition={positionDelta:F5}; " +
                $"rotationEuler={previous.Rotation.eulerAngles:F2} -> {currentRotation.eulerAngles:F2}; deltaAngle={rotationDelta:F3}°; frame={Time.frameCount}",
                item);

            snapshots[item] = new TransformSnapshot
            {
                Position = currentPosition,
                Rotation = currentRotation
            };
        }
    }

    private void CaptureBaseline()
    {
        snapshots.Clear();
        int count = 0;
        foreach (Transform item in transform)
        {
            if (!IsUtensil(item))
            {
                continue;
            }

            TransformSnapshot snapshot = new TransformSnapshot
            {
                Position = item.position,
                Rotation = item.rotation
            };
            snapshots.Add(item, snapshot);
            count++;

            if (logInitialState)
            {
                Debug.Log(
                    $"[UtensilTransformDebug] BASELINE item={item.name}; " +
                    $"position={snapshot.Position:F4}; rotationEuler={snapshot.Rotation.eulerAngles:F2}; frame={Time.frameCount}",
                    item);
            }
        }

        Debug.Log($"[UtensilTransformDebug] Baseline captured for {count} utensils after initial scatter. Watching later transform changes.", this);
    }

    private static bool IsUtensil(Transform candidate)
    {
        if (candidate == null || string.Equals(candidate.name, "Plane", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return candidate.GetComponent<MeshRenderer>() != null ||
               candidate.GetComponentInChildren<MeshRenderer>(true) != null;
    }
}
