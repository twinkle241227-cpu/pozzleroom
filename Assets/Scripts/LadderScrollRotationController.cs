using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Lets the player click the ladder to subtract 90 degrees from the ladder's own
/// local Rotation X. Its position and local Y/Z rotation remain unchanged.
/// </summary>
public sealed class LadderScrollRotationController : MonoBehaviour
{
    [Header("Rotation")]
    [SerializeField, Range(0f, 180f)] private float maximumAngle = 90f;
    [Tooltip("How quickly the ladder opens or closes, in degrees per second.")]
    [SerializeField, Min(1f)] private float rotationSpeedDegreesPerSecond = 180f;

    [Header("References")]
    [SerializeField] private Transform ladderPivot;

    private Camera targetCamera;
    private bool isRotated;
    private bool hasInitialPose;
    private float initialLocalX;
    private float targetLocalX;

    /// <summary>True after the ladder has been lowered by its first click.</summary>
    public bool IsLowered => isRotated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToLadder()
    {
        Transform ladder = FindSceneTransform("ladder");
        Transform pivot = FindSceneTransform("ladderPivot");
        if (ladder == null || pivot == null || ladder.GetComponent<LadderScrollRotationController>() != null)
        {
            return;
        }

        LadderScrollRotationController controller = ladder.gameObject.AddComponent<LadderScrollRotationController>();
        controller.ladderPivot = pivot;
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureCollider();
    }

    private void Update()
    {
        if (!ResolveReferences())
        {
            return;
        }

        UpdateRotation();

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (!IsPointerOverLadder())
        {
            return;
        }

        // This is deliberately the ladder's own local X rotation, not an
        // orbit around ladderPivot. A second click can reverse the animation
        // even before the first movement has finished.
        isRotated = !isRotated;
        targetLocalX = isRotated ? initialLocalX - maximumAngle : initialLocalX;
        Debug.Log(isRotated
            ? $"[LadderRotation] Opening to X={targetLocalX:0.##}° at {rotationSpeedDegreesPerSecond:0.##}°/s."
            : $"[LadderRotation] Closing to X={targetLocalX:0.##}° at {rotationSpeedDegreesPerSecond:0.##}°/s.", this);
    }

    private bool ResolveReferences()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (ladderPivot == null)
        {
            ladderPivot = FindSceneTransform("ladderPivot");
        }

        if (targetCamera == null || ladderPivot == null)
        {
            return false;
        }

        if (!hasInitialPose)
        {
            hasInitialPose = true;
            initialLocalX = NormalizeSignedAngle(transform.localEulerAngles.x);
            targetLocalX = initialLocalX;
            Debug.Log($"[LadderRotation] Ready: ladder='{name}', pivot='{ladderPivot.name}'.", this);
        }

        return true;
    }

    private bool IsPointerOverLadder()
    {
        if (targetCamera == null ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return false;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform) ||
                hitTransform == ladderPivot || hitTransform.IsChildOf(ladderPivot))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateRotation()
    {
        Vector3 localEuler = transform.localEulerAngles;
        float currentLocalX = NormalizeSignedAngle(localEuler.x);
        float nextLocalX = Mathf.MoveTowardsAngle(
            currentLocalX,
            targetLocalX,
            rotationSpeedDegreesPerSecond * Time.deltaTime);
        if (Mathf.Approximately(currentLocalX, nextLocalX))
        {
            return;
        }

        localEuler.x = nextLocalX;
        transform.localEulerAngles = localEuler;
    }

    private static float NormalizeSignedAngle(float angle)
    {
        return Mathf.DeltaAngle(0f, angle);
    }

    private void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>(true) != null)
        {
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
        Vector3 localSize = transform.InverseTransformVector(bounds.size);
        localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        BoxCollider collider = gameObject.AddComponent<BoxCollider>();
        collider.center = localCenter;
        collider.size = localSize;
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
