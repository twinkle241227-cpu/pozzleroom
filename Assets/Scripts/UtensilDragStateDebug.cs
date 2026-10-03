using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Read-only runtime diagnostics for the utensil drag interaction. This
/// component never changes a utensil; it only records the state before pickup,
/// while dragging, and immediately after a drop or successful snap.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class UtensilDragStateDebug : MonoBehaviour
{
    [Header("Logging")]
    [SerializeField] private bool logInitialState = true;
    [SerializeField] private bool logPickup = true;
    [SerializeField] private bool logRelease = true;
    [SerializeField] private bool logRotationChanges = true;
    [SerializeField] private bool logPositionChangesWhileDragging;
    [SerializeField, Min(0f)] private float rotationChangeThreshold = 0.05f;
    [SerializeField, Min(0f)] private float positionChangeThreshold = 0.001f;

    [Header("Manual Diagnostics")]
    [SerializeField] private KeyCode dumpAllStatesKey = KeyCode.F8;

    private readonly Dictionary<Transform, Snapshot> snapshots = new Dictionary<Transform, Snapshot>();
    private UtensilPuzzleController controller;
    private Transform previousHeld;
    private bool isReady;

    private struct Snapshot
    {
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Transform Parent;
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

            if (candidate.GetComponent<UtensilDragStateDebug>() == null)
            {
                candidate.gameObject.AddComponent<UtensilDragStateDebug>();
            }

            return;
        }
    }

    private void Awake()
    {
        controller = GetComponent<UtensilPuzzleController>();
    }

    private IEnumerator Start()
    {
        // The puzzle scatters utensils in Start. Use the completed scatter pose
        // as the diagnostic baseline instead of the authored answer pose.
        yield return null;
        CaptureBaseline();
        previousHeld = controller == null ? null : controller.HeldUtensil;
        isReady = true;
    }

    private void LateUpdate()
    {
        if (!isReady)
        {
            return;
        }

        if (Input.GetKeyDown(dumpAllStatesKey))
        {
            DumpAllStates();
        }

        Transform currentHeld = controller == null ? null : controller.HeldUtensil;
        if (currentHeld != previousHeld)
        {
            if (previousHeld != null && logRelease)
            {
                LogState("RELEASE", previousHeld, GetSnapshot(previousHeld), true);
            }

            if (currentHeld != null && logPickup)
            {
                LogState("PICKUP", currentHeld, GetSnapshot(currentHeld), true);
            }
        }

        foreach (Transform item in transform)
        {
            if (!IsUtensil(item))
            {
                continue;
            }

            Snapshot current = GetSnapshot(item);
            if (!snapshots.TryGetValue(item, out Snapshot previous))
            {
                snapshots[item] = current;
                continue;
            }

            float worldRotationDelta = Quaternion.Angle(previous.WorldRotation, current.WorldRotation);
            float localRotationDelta = Quaternion.Angle(previous.LocalRotation, current.LocalRotation);
            float positionDelta = Vector3.Distance(previous.WorldPosition, current.WorldPosition);
            bool parentChanged = previous.Parent != current.Parent;
            bool isHeld = item == currentHeld;

            if (logRotationChanges &&
                (worldRotationDelta >= rotationChangeThreshold ||
                 localRotationDelta >= rotationChangeThreshold || parentChanged))
            {
                Quaternion worldDelta = current.WorldRotation * Quaternion.Inverse(previous.WorldRotation);
                worldDelta.ToAngleAxis(out float deltaAngle, out Vector3 deltaAxis);
                if (deltaAngle > 180f)
                {
                    deltaAngle -= 360f;
                }

                Debug.Log(
                    $"[UtensilDragStateDebug][ROTATION_CHANGE] item={item.name}; held={isHeld}; " +
                    $"worldEuler={Format(previous.WorldRotation.eulerAngles)} -> {Format(current.WorldRotation.eulerAngles)}; " +
                    $"localEuler={Format(previous.LocalRotation.eulerAngles)} -> {Format(current.LocalRotation.eulerAngles)}; " +
                    $"worldDelta={worldRotationDelta:F3}deg; localDelta={localRotationDelta:F3}deg; " +
                    $"deltaAxis={Format(deltaAxis)}; signedDelta={deltaAngle:F3}deg; " +
                    $"parent={Path(previous.Parent)} -> {Path(current.Parent)}; frame={Time.frameCount}",
                    item);
            }

            if (isHeld && logPositionChangesWhileDragging && positionDelta >= positionChangeThreshold)
            {
                Debug.Log(
                    $"[UtensilDragStateDebug][DRAG_MOVE] item={item.name}; " +
                    $"worldPosition={Format(previous.WorldPosition)} -> {Format(current.WorldPosition)}; " +
                    $"delta={positionDelta:F5}; frame={Time.frameCount}",
                    item);
            }

            snapshots[item] = current;
        }

        previousHeld = currentHeld;
    }

    [ContextMenu("Debug/Dump All Utensil States")]
    public void DumpAllStates()
    {
        int count = 0;
        foreach (Transform item in transform)
        {
            if (!IsUtensil(item))
            {
                continue;
            }

            LogState("MANUAL_DUMP", item, GetSnapshot(item), true);
            count++;
        }

        Debug.Log($"[UtensilDragStateDebug] Dumped {count} utensil states at frame {Time.frameCount}.", this);
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

            Snapshot snapshot = GetSnapshot(item);
            snapshots[item] = snapshot;
            count++;
            if (logInitialState)
            {
                LogState("BASELINE_AFTER_SCATTER", item, snapshot, false);
            }
        }

        Debug.Log(
            $"[UtensilDragStateDebug] Ready. Tracking {count} utensils. " +
            $"Drag an item or press {dumpAllStatesKey} to dump every state.",
            this);
    }

    private void LogState(string phase, Transform item, Snapshot snapshot, bool includeTarget)
    {
        Rigidbody[] bodies = item.GetComponentsInChildren<Rigidbody>(true);
        Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
        int kinematicBodies = 0;
        int gravityBodies = 0;
        int enabledColliders = 0;
        Vector3 totalVelocity = Vector3.zero;
        Vector3 totalAngularVelocity = Vector3.zero;

        foreach (Rigidbody body in bodies)
        {
            if (body.isKinematic)
            {
                kinematicBodies++;
            }

            if (body.useGravity)
            {
                gravityBodies++;
            }

            totalVelocity += body.velocity;
            totalAngularVelocity += body.angularVelocity;
        }

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider.enabled)
            {
                enabledColliders++;
            }
        }

        bool lockEvidence = colliders.Length > 0 && enabledColliders == 0 &&
            (bodies.Length == 0 || kinematicBodies == bodies.Length);
        bool dropEvidence = gravityBodies > 0 || kinematicBodies < bodies.Length;

        StringBuilder message = new StringBuilder(512);
        message.Append($"[UtensilDragStateDebug][{phase}] item={item.name}; frame={Time.frameCount}; ");
        message.Append($"parent={Path(snapshot.Parent)}; ");
        message.Append($"worldPosition={Format(snapshot.WorldPosition)}; localPosition={Format(snapshot.LocalPosition)}; ");
        message.Append($"worldEuler={Format(snapshot.WorldRotation.eulerAngles)}; localEuler={Format(snapshot.LocalRotation.eulerAngles)}; ");
        message.Append($"rigidbodies={bodies.Length}; kinematic={kinematicBodies}; gravity={gravityBodies}; ");
        message.Append($"velocitySum={Format(totalVelocity)}; angularVelocitySum={Format(totalAngularVelocity)}; ");
        message.Append($"colliders={enabledColliders}/{colliders.Length} enabled; ");
        message.Append($"lockEvidence={lockEvidence}; dropEvidence={dropEvidence}");

        if (includeTarget && TryFindTarget(item, out Transform target))
        {
            message.Append($"; target={Path(target)}; targetPosition={Format(target.position)}; ");
            message.Append($"targetWorldEuler={Format(target.rotation.eulerAngles)}; ");
            message.Append($"distanceToTarget={Vector3.Distance(item.position, target.position):F5}; ");
            message.Append($"angleToTarget={Quaternion.Angle(item.rotation, target.rotation):F3}deg");
        }
        else if (includeTarget)
        {
            message.Append("; target=NOT_FOUND");
        }

        Debug.Log(message.ToString(), item);
    }

    private bool TryFindTarget(Transform item, out Transform target)
    {
        string targetName = item.name + " Target";
        foreach (Transform child in transform)
        {
            if (!string.Equals(child.name, "Utensil Targets (Runtime)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Transform candidate in child)
            {
                if (string.Equals(candidate.name, targetName, StringComparison.Ordinal))
                {
                    target = candidate;
                    return true;
                }
            }
        }

        target = null;
        return false;
    }

    private static Snapshot GetSnapshot(Transform item)
    {
        return new Snapshot
        {
            WorldPosition = item.position,
            WorldRotation = item.rotation,
            LocalPosition = item.localPosition,
            LocalRotation = item.localRotation,
            Parent = item.parent
        };
    }

    private static bool IsUtensil(Transform candidate)
    {
        if (candidate == null ||
            string.Equals(candidate.name, "Plane", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.name, "Utensil Targets (Runtime)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return candidate.GetComponent<MeshRenderer>() != null ||
               candidate.GetComponentInChildren<MeshRenderer>(true) != null;
    }

    private static string Path(Transform target)
    {
        if (target == null)
        {
            return "<none>";
        }

        string result = target.name;
        Transform parent = target.parent;
        while (parent != null)
        {
            result = parent.name + "/" + result;
            parent = parent.parent;
        }

        return result;
    }

    private static string Format(Vector3 value)
    {
        return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
    }
}
