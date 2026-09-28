using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the utensil puzzle's authored answer data.  This first version only
/// captures the current, correct board layout; it deliberately does not move
/// or randomise any utensils yet.
/// </summary>
public sealed class UtensilPuzzleController : MonoBehaviour
{
    [Serializable]
    private sealed class UtensilState
    {
        public Transform Item;
        public Transform Target;
        public Vector3 CorrectWorldPosition;
        public Quaternion CorrectWorldRotation;
        public Vector3 CorrectLocalScale;
        public Bounds CorrectWorldBounds;
        public Rigidbody Rigidbody;
        public bool IsLocked;
        public bool HasBeenDropped;
    }

    [Header("Scene References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform utensilRoot;
    [SerializeField] private Transform targetRoot;
    [SerializeField] private Collider emptySpace;

    [Header("Initial Scatter")]
    [SerializeField] private bool scatterOnStart = true;
    [SerializeField, Min(1)] private int maxPlacementAttempts = 80;
    [SerializeField, Min(0f)] private float scatterPadding = 0.02f;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.002f;
    [SerializeField] private Vector2 randomYawRange = new Vector2(-25f, 25f);

    [Header("Pickup")]
    [SerializeField, Min(0.1f)] private float pickupDistance = 20f;
    [SerializeField, Min(0.001f)] private float targetSnapDistance = 0.12f;

    [Header("Captured Answer Data (read-only at runtime)")]
    [SerializeField] private List<UtensilState> utensils = new List<UtensilState>();

    private bool hasCapturedCorrectLayout;
    private bool hasScattered;
    private UtensilState heldUtensil;
    private Plane dragPlane;
    private Vector3 dragOffset;
    private bool isDragging;

    public bool HasCapturedCorrectLayout => hasCapturedCorrectLayout;
    public int UtensilCount => utensils.Count;
    public Transform HeldUtensil => heldUtensil == null ? null : heldUtensil.Item;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateControllerForUtensilRoot()
    {
        Transform root = FindUtensilRoot();
        if (root != null && root.GetComponent<UtensilPuzzleController>() == null)
        {
            root.gameObject.AddComponent<UtensilPuzzleController>();
        }
    }

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (utensilRoot == null)
        {
            utensilRoot = transform;
        }
    }

    private void Start()
    {
        // This runs before any future scatter stage. The current visual board
        // arrangement is therefore the exact solution for every utensil.
        CaptureCurrentCorrectLayout();

        if (scatterOnStart)
        {
            ScatterUtensils();
        }
    }

    private void Update()
    {
        // In the normal room view the entrance Plane owns the left-click.
        // Utensil colliders only become interactive after that view controller
        // finishes moving the camera to its front-facing puzzle view.
        if (KitchenUtensilViewController.Active == null ||
            !KitchenUtensilViewController.Active.CanInteractWithUtensils)
        {
            CancelDrag();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryPickUtensilUnderMouse();
        }

        if (isDragging && Input.GetMouseButton(0))
        {
            MoveHeldUtensilToCursor();
        }

        if (isDragging && Input.GetMouseButtonUp(0))
        {
            PlaceHeldUtensil();
        }
    }

    /// <summary>
    /// First pickup stage: selects the foremost utensil collider under the
    /// cursor, freezes its Rigidbody, and aligns it to its authored answer
    /// rotation. Position-following is intentionally added in the next stage.
    /// </summary>
    private void TryPickUtensilUnderMouse()
    {
        if (!hasCapturedCorrectLayout || targetCamera == null)
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, pickupDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            UtensilState state = FindUtensilState(hit.collider.transform);
            if (state == null)
            {
                continue;
            }

            HoldUtensil(state);
            return;
        }
    }

    private void HoldUtensil(UtensilState utensil)
    {
        if (utensil.IsLocked)
        {
            return;
        }

        heldUtensil = utensil;
        Transform item = utensil.Item;

        FreezeAllRigidbodies(item);
        if (utensil.HasBeenDropped)
        {
            item.rotation = utensil.CorrectWorldRotation;
        }

        // All correct targets are on the utensil board. Dragging on this plane
        // lets an item move from the horizontal tabletop back onto that board.
        dragPlane = new Plane(-targetCamera.transform.forward, utensil.Target.position);
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        if (dragPlane.Raycast(ray, out float enter))
        {
            Vector3 cursorPoint = ray.GetPoint(enter);
            dragOffset = Vector3.ProjectOnPlane(item.position - cursorPoint, dragPlane.normal);
        }
        else
        {
            dragOffset = Vector3.zero;
        }

        isDragging = true;
        Physics.SyncTransforms();
        string rotationMessage = utensil.HasBeenDropped
            ? "restored its correct rotation after a drop"
            : "preserved its initial scatter rotation";
        Debug.Log($"[UtensilPuzzle] Picked {item.name}; {rotationMessage}; following the cursor on the board plane.", item);
    }

    private void MoveHeldUtensilToCursor()
    {
        if (heldUtensil == null || heldUtensil.Item == null)
        {
            CancelDrag();
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        if (!dragPlane.Raycast(ray, out float enter))
        {
            return;
        }

        heldUtensil.Item.position = ray.GetPoint(enter) + dragOffset;
        Physics.SyncTransforms();
    }

    private void PlaceHeldUtensil()
    {
        if (heldUtensil != null && heldUtensil.Item != null && heldUtensil.Target != null)
        {
            float distanceToTarget = Vector3.Distance(heldUtensil.Item.position, heldUtensil.Target.position);
            if (distanceToTarget <= targetSnapDistance)
            {
                heldUtensil.Item.SetPositionAndRotation(heldUtensil.Target.position, heldUtensil.CorrectWorldRotation);
                LockUtensil(heldUtensil);
            }
            else
            {
                DropUtensil(heldUtensil, distanceToTarget);
            }
        }

        CancelDrag();
        Physics.SyncTransforms();
    }

    private void CancelDrag()
    {
        isDragging = false;
        heldUtensil = null;
        dragOffset = Vector3.zero;
    }

    private static void FreezeAllRigidbodies(Transform item)
    {
        foreach (Rigidbody body in item.GetComponentsInChildren<Rigidbody>(true))
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    private static void LockUtensil(UtensilState utensil)
    {
        utensil.IsLocked = true;
        FreezeAllRigidbodies(utensil.Item);

        // A completed item should neither be selectable nor block clicks on a
        // smaller utensil that may be visually behind it.
        foreach (Collider collider in utensil.Item.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        Debug.Log($"[UtensilPuzzle] Locked {utensil.Item.name} on its correct target.", utensil.Item);
    }

    private static void DropUtensil(UtensilState utensil, float distanceToTarget)
    {
        utensil.HasBeenDropped = true;
        int releasedBodies = 0;
        foreach (Rigidbody body in utensil.Item.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();
            releasedBodies++;
        }

        Debug.Log(
            $"[UtensilPuzzle] Dropped {utensil.Item.name}; target distance: {distanceToTarget:F3}; released rigidbodies: {releasedBodies}.",
            utensil.Item);
    }

    private UtensilState FindUtensilState(Transform hitTransform)
    {
        foreach (UtensilState state in utensils)
        {
            if (!state.IsLocked && state.Item != null &&
                (hitTransform == state.Item || hitTransform.IsChildOf(state.Item)))
            {
                return state;
            }
        }

        return null;
    }

    public void CaptureCurrentCorrectLayout()
    {
        if (hasCapturedCorrectLayout)
        {
            return;
        }

        EnsureTargetRoot();
        utensils.Clear();

        foreach (Transform candidate in utensilRoot)
        {
            if (!IsPuzzleUtensil(candidate))
            {
                continue;
            }

            if (!TryGetCombinedBounds(candidate, out Bounds bounds))
            {
                continue;
            }

            GameObject targetObject = new GameObject(candidate.name + " Target");
            targetObject.hideFlags = HideFlags.DontSave;
            targetObject.transform.SetParent(targetRoot, true);
            targetObject.transform.SetPositionAndRotation(candidate.position, candidate.rotation);
            targetObject.transform.localScale = candidate.localScale;

            utensils.Add(new UtensilState
            {
                Item = candidate,
                Target = targetObject.transform,
                CorrectWorldPosition = candidate.position,
                CorrectWorldRotation = candidate.rotation,
                CorrectLocalScale = candidate.localScale,
                CorrectWorldBounds = bounds,
                Rigidbody = candidate.GetComponent<Rigidbody>()
            });
        }

        hasCapturedCorrectLayout = utensils.Count > 0;
        Debug.Log($"[UtensilPuzzle] Captured {utensils.Count} correct utensil targets. No utensils have been moved.", this);
    }

    public void ScatterUtensils()
    {
        if (!hasCapturedCorrectLayout || hasScattered)
        {
            return;
        }

        if (emptySpace == null)
        {
            emptySpace = FindEmptySpaceCollider();
        }

        if (emptySpace == null)
        {
            Debug.LogError("[UtensilPuzzle] Cannot scatter: no Collider was found on an object named EmptySpace.", this);
            return;
        }

        Bounds zone = emptySpace.bounds;
        List<UtensilState> ordered = new List<UtensilState>(utensils);
        ordered.Sort((left, right) => Footprint(right.CorrectWorldBounds).CompareTo(Footprint(left.CorrectWorldBounds)));

        List<Bounds> occupied = new List<Bounds>();
        int placedCount = 0;
        foreach (UtensilState utensil in ordered)
        {
            if (TryScatterUtensil(utensil, zone, occupied, out Bounds placedBounds))
            {
                occupied.Add(placedBounds);
                placedCount++;
            }
            else if (TryScatterUtensilAllowOverlap(utensil, zone, out placedBounds))
            {
                // The tabletop cannot hold every authored mesh without some
                // overlap. This fallback keeps the remaining utensils on the
                // table instead of leaving them on the answer board.
                occupied.Add(placedBounds);
                placedCount++;
                Debug.LogWarning($"[UtensilPuzzle] EmptySpace is full; placed {utensil.Item.name} on the tabletop with overlap allowed.", utensil.Item);
            }
            else
            {
                PlaceAtEmptySpaceCenter(utensil, zone, out placedBounds);
                occupied.Add(placedBounds);
                placedCount++;
                Debug.LogWarning($"[UtensilPuzzle] {utensil.Item.name} exceeds EmptySpace bounds; centered it on the tabletop.", utensil.Item);
            }

            // Imported utensils may have their Rigidbodies on child meshes.
            // Freeze the complete hierarchy after its final scatter pose.
            FreezeAllRigidbodies(utensil.Item);
        }

        Physics.SyncTransforms();
        hasScattered = true;
        Debug.Log($"[UtensilPuzzle] Scattered {placedCount}/{utensils.Count} utensils inside EmptySpace. Rigidbodies are kinematic until the later pickup stage.", this);
    }

    private bool TryScatterUtensil(UtensilState utensil, Bounds zone, List<Bounds> occupied, out Bounds placedBounds)
    {
        Transform item = utensil.Item;
        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            float x = UnityEngine.Random.Range(zone.min.x + scatterPadding, zone.max.x - scatterPadding);
            float z = UnityEngine.Random.Range(zone.min.z + scatterPadding, zone.max.z - scatterPadding);
            Quaternion rotation = Quaternion.AngleAxis(UnityEngine.Random.Range(randomYawRange.x, randomYawRange.y), Vector3.up) * utensil.CorrectWorldRotation;

            item.SetPositionAndRotation(new Vector3(x, zone.max.y, z), rotation);
            if (!TryGetCombinedBounds(item, out Bounds bounds))
            {
                continue;
            }

            item.position += Vector3.up * (zone.max.y + surfaceOffset - bounds.min.y);
            if (!TryGetCombinedBounds(item, out bounds) || !IsInsideScatterZone(bounds, zone) || OverlapsAny(bounds, occupied))
            {
                continue;
            }

            placedBounds = bounds;
            return true;
        }

        placedBounds = default;
        return false;
    }

    private bool TryScatterUtensilAllowOverlap(UtensilState utensil, Bounds zone, out Bounds placedBounds)
    {
        Transform item = utensil.Item;
        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            float x = UnityEngine.Random.Range(zone.min.x + scatterPadding, zone.max.x - scatterPadding);
            float z = UnityEngine.Random.Range(zone.min.z + scatterPadding, zone.max.z - scatterPadding);
            Quaternion rotation = Quaternion.AngleAxis(UnityEngine.Random.Range(randomYawRange.x, randomYawRange.y), Vector3.up) * utensil.CorrectWorldRotation;

            item.SetPositionAndRotation(new Vector3(x, zone.max.y, z), rotation);
            if (!TryGetCombinedBounds(item, out Bounds bounds))
            {
                continue;
            }

            item.position += Vector3.up * (zone.max.y + surfaceOffset - bounds.min.y);
            if (TryGetCombinedBounds(item, out bounds) && IsInsideScatterZone(bounds, zone))
            {
                placedBounds = bounds;
                return true;
            }
        }

        placedBounds = default;
        return false;
    }

    private void PlaceAtEmptySpaceCenter(UtensilState utensil, Bounds zone, out Bounds placedBounds)
    {
        Transform item = utensil.Item;
        Vector3 position = new Vector3(zone.center.x, zone.max.y, zone.center.z);
        item.SetPositionAndRotation(position, utensil.CorrectWorldRotation);

        if (TryGetCombinedBounds(item, out Bounds bounds))
        {
            item.position += Vector3.up * (zone.max.y + surfaceOffset - bounds.min.y);
            TryGetCombinedBounds(item, out placedBounds);
            return;
        }

        placedBounds = new Bounds(item.position, Vector3.zero);
    }

    private bool IsInsideScatterZone(Bounds itemBounds, Bounds zone)
    {
        return itemBounds.min.x >= zone.min.x + scatterPadding &&
               itemBounds.max.x <= zone.max.x - scatterPadding &&
               itemBounds.min.z >= zone.min.z + scatterPadding &&
               itemBounds.max.z <= zone.max.z - scatterPadding;
    }

    private static bool OverlapsAny(Bounds candidate, List<Bounds> occupied)
    {
        foreach (Bounds existing in occupied)
        {
            if (candidate.Intersects(existing))
            {
                return true;
            }
        }

        return false;
    }

    private static float Footprint(Bounds bounds)
    {
        return bounds.size.x * bounds.size.z;
    }

    private void EnsureTargetRoot()
    {
        if (targetRoot != null)
        {
            return;
        }

        GameObject root = new GameObject("Utensil Targets (Runtime)");
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(utensilRoot, false);
        targetRoot = root.transform;
    }

    private bool IsPuzzleUtensil(Transform candidate)
    {
        if (candidate == null || candidate == targetRoot ||
            string.Equals(candidate.name, "Plane", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return candidate.GetComponent<MeshRenderer>() != null ||
            candidate.GetComponentInChildren<MeshRenderer>(true) != null;
    }

    private static bool TryGetCombinedBounds(Transform item, out Bounds bounds)
    {
        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
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

    private static Transform FindUtensilRoot()
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid())
            {
                continue;
            }

            if (string.Equals(candidate.name, "厨具", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Collider FindEmptySpaceCollider()
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid())
            {
                continue;
            }

            if (string.Equals(candidate.name, "EmptySpace", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.GetComponent<Collider>();
            }
        }

        return null;
    }
}
