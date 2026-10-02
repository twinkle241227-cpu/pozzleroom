using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Drag-and-drop puzzle for the wine display. Target names ending in
/// "correct" determine the final slots. Numbered items use their same-name
/// start marker initially, but may finish in any free slot of the same type.
/// </summary>
public sealed class WineRotationPuzzleController : MonoBehaviour
{
    [Serializable]
    private sealed class WineItemState
    {
        public string PairId;
        public string ItemType;
        public Transform InitialItem;
        public Transform PlayableItem;
        public Transform CorrectItem;
        public Vector3 InitialWorldPosition;
        public Quaternion InitialWorldRotation;
        public Vector3 InitialLocalScale;
        public Vector3 CorrectWorldPosition;
        public Vector3 CorrectVisualCenter;
        public Quaternion CorrectWorldRotation;
        // The display can be rotated by RoomPivotDragController after Start.
        // Keep the authored target in this display's local space as well, so
        // its world pose stays valid after that room rotation.
        public Vector3 CorrectLocalPosition;
        public Vector3 CorrectVisualCenterLocal;
        public Quaternion CorrectLocalRotation;
        public Vector3 CorrectLocalScale;
        public Vector3 VisualLocalAxis;
        public bool IsOccupied;
        public bool IsLocked;
    }

    [Header("Correct Pose Validation")]
    [SerializeField, Min(0.01f)] private float positionTolerance = 0.12f;
    [SerializeField, Min(0.1f)] private float rotationStepDegrees = 15f;
    [SerializeField, Range(0f, 15f)] private float rotationToleranceDegrees = 7.5f;
    [SerializeField, Min(1f)] private float minimumDragPixels = 6f;
    [SerializeField, Min(0.01f)] private float supportThickness = 0.08f;
    [SerializeField, Min(0f)] private float supportPadding = 0.12f;
    [SerializeField] private List<WineItemState> items = new List<WineItemState>();

    private Camera targetCamera;
    private WineDisplayViewController displayView;
    private WineItemState draggedItem;
    private Plane dragPlane;
    private Vector3 dragPlanePoint;
    private Vector3 dragOffset;
    private Vector2 dragStartScreenPosition;
    private Vector3 dragStartWorldPosition;
    private bool dragMoved;
    private Rigidbody draggedBody;
    private bool draggedBodyWasKinematic;
    private bool draggedBodyUsedGravity;
    private bool hasCapturedCorrectLayout;
    private bool puzzlePrepared;
    private bool isComplete;
    private Transform physicsSupport;
    private Vector3 interactionPlaneNormal = Vector3.right;
    private Vector3 interactionPlanePoint;

    public bool HasCapturedCorrectLayout => hasCapturedCorrectLayout;
    public int ItemCount => items.Count;
    public bool IsComplete => isComplete;

    /// <summary>Builds a read-only placement report for WineSnapPlacementDebug.</summary>
    public bool TryBuildSnapDebugReport(Transform hitTransform, string phase, out Transform playableItem, out string report)
    {
        WineItemState source = null;
        foreach (WineItemState item in items)
        {
            if (item.PlayableItem != null &&
                (hitTransform == item.PlayableItem || hitTransform.IsChildOf(item.PlayableItem)))
            {
                source = item;
                break;
            }
        }

        if (source == null)
        {
            playableItem = null;
            report = null;
            return false;
        }

        playableItem = source.PlayableItem;
        Vector3 sourceCenter = GetVisualCenter(source.PlayableItem);
        Bounds sourceBounds = GetVisualBounds(source.PlayableItem);
        StringBuilder builder = new StringBuilder();
        builder.Append($"[WineSnapDebug] {phase}; source={source.PlayableItem.name}; pair={source.PairId}; type={source.ItemType}; ");
        builder.Append($"locked={source.IsLocked}; rootPos={source.PlayableItem.position:F5}; visualCenter={sourceCenter:F5}; ");
        builder.Append($"rotation={source.PlayableItem.eulerAngles:F2}; localAxis={source.VisualLocalAxis:F2}; bounds={sourceBounds.size:F5}");

        foreach (WineItemState candidate in items)
        {
            if (!string.Equals(candidate.ItemType, source.ItemType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Vector3 candidateCenter = GetCurrentCorrectVisualCenter(candidate);
            Vector3 candidatePosition = GetCurrentCorrectWorldPosition(candidate);
            Quaternion candidateRotation = GetCurrentCorrectWorldRotation(candidate);
            Vector3 delta = sourceCenter - candidateCenter;
            float depthError = Mathf.Abs(Vector3.Dot(delta, interactionPlaneNormal));
            float planeError = Vector3.ProjectOnPlane(delta, interactionPlaneNormal).magnitude;
            float angleError = GetScreenAngleError(source, candidateRotation);
            builder.Append($" || slot={candidate.PairId}; occupied={candidate.IsOccupied}; targetRoot={candidatePosition:F5}; ");
            builder.Append($"targetCenter={candidateCenter:F5}; centerDelta={delta:F5}; ");
            builder.Append($"planeError={planeError:F5}; depthError={depthError:F5}; angleError={angleError:F2}");
        }

        report = builder.ToString();
        return true;
    }

    /// <summary>Read-only access for WineCorrectPoseDebug.</summary>
    public bool TryGetCorrectPoseForDebug(
        Transform hitTransform,
        out Transform playableItem,
        out string pairId,
        out Vector3 targetPosition,
        out Quaternion targetRotation)
    {
        foreach (WineItemState item in items)
        {
            if (item.PlayableItem != null &&
                (hitTransform == item.PlayableItem || hitTransform.IsChildOf(item.PlayableItem)))
            {
                playableItem = item.PlayableItem;
                pairId = item.PairId;
                targetPosition = GetCurrentCorrectWorldPosition(item);
                targetRotation = GetCurrentCorrectWorldRotation(item);
                return true;
            }
        }

        playableItem = null;
        pairId = null;
        targetPosition = default;
        targetRotation = default;
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToWineDisplay()
    {
        Transform root = FindWineDisplayRoot();
        if (root != null && root.GetComponent<WineRotationPuzzleController>() == null)
        {
            root.gameObject.AddComponent<WineRotationPuzzleController>();
        }
    }

    private void Start()
    {
        targetCamera = Camera.main;
        displayView = GetComponent<WineDisplayViewController>();
        CaptureCurrentCorrectLayout();
        PreparePuzzle();
    }

    private void Update()
    {
        if (!CanInteract())
        {
            return;
        }

        if (draggedItem != null)
        {
            UpdateDraggedItem();
            if (Input.GetMouseButtonUp(0))
            {
                ReleaseDraggedItem();
            }

            return;
        }

        TryRotateHoveredItem();

        if (Input.GetMouseButtonDown(0))
        {
            TryStartDragging();
        }
    }

    public void CaptureCurrentCorrectLayout()
    {
        if (hasCapturedCorrectLayout)
        {
            return;
        }

        items.Clear();

        Dictionary<string, Transform> initialItems = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Transform> correctItems = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

        foreach (Transform candidate in transform)
        {
            if (!IsWineItem(candidate))
            {
                continue;
            }

            string pairId = GetPairId(candidate.name);
            if (IsCorrectItem(candidate))
            {
                correctItems[pairId] = candidate;
            }
            else
            {
                initialItems[pairId] = candidate;
            }
        }

        int unmatchedCorrectItems = 0;
        foreach (KeyValuePair<string, Transform> correctEntry in correctItems)
        {
            Transform initialItem;
            if (!initialItems.TryGetValue(correctEntry.Key, out initialItem))
            {
                unmatchedCorrectItems++;
                Debug.LogWarning($"[WineRotationPuzzle] Correct item '{correctEntry.Value.name}' has no same-name initial item ('{correctEntry.Key}').", this);
            }

            items.Add(new WineItemState
            {
                PairId = correctEntry.Key,
                ItemType = GetItemType(correctEntry.Key),
                InitialItem = initialItem,
                // Correct is the only visible and interactive model. The
                // start object is merely an authored pose marker.
                PlayableItem = correctEntry.Value,
                CorrectItem = correctEntry.Value,
                InitialWorldPosition = initialItem == null ? Vector3.zero : initialItem.position,
                InitialWorldRotation = initialItem == null ? Quaternion.identity : initialItem.rotation,
                InitialLocalScale = initialItem == null ? Vector3.one : initialItem.localScale,
                CorrectWorldPosition = correctEntry.Value.position,
                CorrectVisualCenter = GetVisualCenter(correctEntry.Value),
                CorrectWorldRotation = correctEntry.Value.rotation,
                CorrectLocalPosition = transform.InverseTransformPoint(correctEntry.Value.position),
                CorrectVisualCenterLocal = transform.InverseTransformPoint(GetVisualCenter(correctEntry.Value)),
                CorrectLocalRotation = Quaternion.Inverse(transform.rotation) * correctEntry.Value.rotation,
                CorrectLocalScale = correctEntry.Value.localScale,
                VisualLocalAxis = GetLongestVisualLocalAxis(correctEntry.Value)
            });

            Debug.Log($"[WineRotationPuzzle] Recorded '{correctEntry.Key}': initial=" +
                      $"{(initialItem == null ? "<missing>" : initialItem.name)}, correct={correctEntry.Value.name}, " +
                      $"initialPosition={(initialItem == null ? Vector3.zero : initialItem.position)}, " +
                      $"initialRotation={(initialItem == null ? Vector3.zero : initialItem.eulerAngles)}, " +
                      $"correctPosition={correctEntry.Value.position}, correctRotation={correctEntry.Value.eulerAngles}.", this);
        }

        ConfigureInteractionPlaneFromCorrectPoses();
        hasCapturedCorrectLayout = items.Count > 0;
        Debug.Log($"[WineRotationPuzzle] Recorded {items.Count} correct slots from '*correct' objects; " +
                  $"unmatched initial items: {unmatchedCorrectItems}. Final placement accepts any free slot of the same type.", this);
    }

    private void ConfigureInteractionPlaneFromCorrectPoses()
    {
        if (items.Count == 0)
        {
            return;
        }

        Vector3 minimum = GetCurrentCorrectWorldPosition(items[0]);
        Vector3 maximum = minimum;
        Vector3 sum = Vector3.zero;
        foreach (WineItemState item in items)
        {
            Vector3 position = GetCurrentCorrectWorldPosition(item);
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
            sum += position;
        }

        Vector3 span = maximum - minimum;
        // Use the authored correct-pose layout to identify the display board
        // plane. This prevents dragging toward/through the cabinet.
        if (span.x <= span.y && span.x <= span.z)
        {
            interactionPlaneNormal = Vector3.right;
        }
        else if (span.y <= span.z)
        {
            interactionPlaneNormal = Vector3.up;
        }
        else
        {
            interactionPlaneNormal = Vector3.forward;
        }

        interactionPlanePoint = sum / items.Count;
        Debug.Log($"[WineRotationPuzzle] Interaction plane: normal={interactionPlaneNormal}, point={interactionPlanePoint}, targetSpan={span}.", this);
    }

    private Vector3 GetCurrentCorrectWorldPosition(WineItemState item)
    {
        return transform.TransformPoint(item.CorrectLocalPosition);
    }

    private Vector3 GetCurrentCorrectVisualCenter(WineItemState item)
    {
        return transform.TransformPoint(item.CorrectVisualCenterLocal);
    }

    private Quaternion GetCurrentCorrectWorldRotation(WineItemState item)
    {
        return transform.rotation * item.CorrectLocalRotation;
    }

    private void PreparePuzzle()
    {
        if (!hasCapturedCorrectLayout || puzzlePrepared)
        {
            return;
        }

        int usableItemCount = 0;
        foreach (WineItemState item in items)
        {
            if (item.InitialItem == null || item.CorrectItem == null || string.IsNullOrEmpty(item.ItemType))
            {
                continue;
            }

            item.IsOccupied = false;
            item.IsLocked = false;
            // The correct model is the only gameplay object. Store its target
            // pose above, then place that same model at its start marker pose.
            item.CorrectItem.SetPositionAndRotation(item.InitialWorldPosition, item.InitialWorldRotation);
            item.CorrectItem.localScale = item.InitialLocalScale;
            item.CorrectItem.gameObject.SetActive(true);
            SetRenderersVisible(item.CorrectItem, true);
            SetCollidersEnabled(item.CorrectItem, true);
            EnsurePlayableCollider(item.CorrectItem);
            item.InitialItem.gameObject.SetActive(false);

            usableItemCount++;
        }

        CreateOrUpdatePhysicsSupport();

        foreach (WineItemState item in items)
        {
            if (item.InitialItem == null || item.CorrectItem == null || string.IsNullOrEmpty(item.ItemType))
            {
                continue;
            }

            foreach (Rigidbody body in item.CorrectItem.GetComponentsInChildren<Rigidbody>(true))
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }
        }

        puzzlePrepared = usableItemCount > 0;
        Debug.Log($"[WineRotationPuzzle] Prepared {usableItemCount} direct correct models at their start-marker poses.", this);
    }

    private void CreateOrUpdatePhysicsSupport()
    {
        bool hasBounds = false;
        Bounds combinedBounds = default;

        foreach (WineItemState item in items)
        {
            if (item.CorrectItem == null || !item.CorrectItem.gameObject.activeInHierarchy)
            {
                continue;
            }

            Renderer[] renderers = item.CorrectItem.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (!hasBounds)
                {
                    combinedBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }
        }

        if (!hasBounds)
        {
            return;
        }

        if (physicsSupport == null)
        {
            GameObject support = new GameObject("Wine Shelf Physics Support (Runtime)");
            support.hideFlags = HideFlags.DontSave;
            physicsSupport = support.transform;
        }

        Vector3 size = combinedBounds.size + new Vector3(supportPadding * 2f, supportThickness, supportPadding * 2f);
        physicsSupport.position = new Vector3(combinedBounds.center.x, combinedBounds.min.y - supportThickness * 0.5f, combinedBounds.center.z);
        physicsSupport.rotation = Quaternion.identity;

        BoxCollider supportCollider = physicsSupport.GetComponent<BoxCollider>();
        if (supportCollider == null)
        {
            supportCollider = physicsSupport.gameObject.AddComponent<BoxCollider>();
        }

        supportCollider.isTrigger = false;
        supportCollider.center = Vector3.zero;
        supportCollider.size = size;
    }

    private bool CanInteract()
    {
        return puzzlePrepared && !isComplete && targetCamera != null && displayView != null && displayView.IsFocused;
    }

    private void TryStartDragging()
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            WineItemState selected = FindPlayableItem(hit.collider.transform);
            if (selected == null)
            {
                continue;
            }

            draggedItem = selected;
            // The drag surface must be the plane the player is looking at,
            // rather than a guessed world X/Y/Z plane.  Each item's correct
            // pose supplies the depth of that plane, so it can never be
            // dragged through the display or out behind the cabinet.
            interactionPlaneNormal = targetCamera.transform.forward.normalized;
            interactionPlanePoint = GetCurrentCorrectWorldPosition(selected);
            dragPlanePoint = interactionPlanePoint;
            dragPlane = new Plane(interactionPlaneNormal, dragPlanePoint);
            if (!dragPlane.Raycast(ray, out float enterDistance))
            {
                draggedItem = null;
                return;
            }

            // Offset is deliberately kept only inside the screen plane.
            // Keeping a depth component here is what previously let an item
            // jump behind the cabinet after the first mouse movement.
            dragOffset = Vector3.ProjectOnPlane(
                selected.PlayableItem.position - ray.GetPoint(enterDistance),
                interactionPlaneNormal);
            dragStartScreenPosition = Input.mousePosition;
            dragStartWorldPosition = selected.PlayableItem.position;
            dragMoved = false;
            draggedBody = selected.PlayableItem.GetComponent<Rigidbody>();
            if (draggedBody != null)
            {
                draggedBodyWasKinematic = draggedBody.isKinematic;
                draggedBodyUsedGravity = draggedBody.useGravity;
                draggedBody.velocity = Vector3.zero;
                draggedBody.angularVelocity = Vector3.zero;
                draggedBody.isKinematic = true;
                draggedBody.useGravity = false;
            }

            return;
        }
    }

    private void TryRotateHoveredItem()
    {
        float scrollDelta = Input.mouseScrollDelta.y;
        if (Mathf.Approximately(scrollDelta, 0f))
        {
            return;
        }

        WineItemState hoveredItem = FindPlayableItemUnderCursor();
        if (hoveredItem == null)
        {
            return;
        }

        int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(scrollDelta)));
        // Rotate around the focused camera normal, which is always exactly
        // perpendicular to the player's current view plane.
        float angle = -Mathf.Sign(scrollDelta) * rotationStepDegrees * steps;
        Vector3 rotationCenter = GetVisualCenter(hoveredItem.PlayableItem);
        hoveredItem.PlayableItem.RotateAround(rotationCenter, targetCamera.transform.forward, angle);
    }

    private WineItemState FindPlayableItemUnderCursor()
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            WineItemState selected = FindPlayableItem(hit.collider.transform);
            if (selected != null)
            {
                return selected;
            }
        }

        return null;
    }

    private static Vector3 GetVisualCenter(Transform item)
    {
        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return item.position;
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            combinedBounds.Encapsulate(renderers[index].bounds);
        }

        return combinedBounds.center;
    }

    private static Bounds GetVisualBounds(Transform item)
    {
        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(item.position, Vector3.zero);
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            combinedBounds.Encapsulate(renderers[index].bounds);
        }

        return combinedBounds;
    }

    private float GetScreenAngleError(WineItemState source, Quaternion targetRotation)
    {
        if (targetCamera == null)
        {
            return 180f;
        }

        Vector3 viewNormal = targetCamera.transform.forward;
        Vector3 targetDirection = GetDirectionInViewPlane(targetRotation, source.VisualLocalAxis, viewNormal);
        Vector3 sourceDirection = GetDirectionInViewPlane(source.PlayableItem.rotation, source.VisualLocalAxis, viewNormal);
        return Mathf.Abs(Vector3.SignedAngle(targetDirection, sourceDirection, viewNormal));
    }

    private static Vector3 GetDirectionInViewPlane(Quaternion rotation, Vector3 localAxis, Vector3 viewNormal)
    {
        Vector3 direction = Vector3.ProjectOnPlane(rotation * localAxis, viewNormal);
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.ProjectOnPlane(rotation * Vector3.up, viewNormal);
        }

        return direction.sqrMagnitude < 0.0001f ? Vector3.up : direction.normalized;
    }

    private static Vector3 GetLongestVisualLocalAxis(Transform item)
    {
        bool hasBounds = false;
        Bounds localBounds = default;
        foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            Bounds meshBounds = filter.sharedMesh.bounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 meshCorner = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(x, y, z));
                Vector3 itemLocalCorner = item.InverseTransformPoint(filter.transform.TransformPoint(meshCorner));
                if (!hasBounds)
                {
                    localBounds = new Bounds(itemLocalCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(itemLocalCorner);
                }
            }
        }

        if (!hasBounds)
        {
            return Vector3.up;
        }

        Vector3 size = localBounds.size;
        if (size.x >= size.y && size.x >= size.z) return Vector3.right;
        return size.y >= size.z ? Vector3.up : Vector3.forward;
    }

    private static void SetRenderersVisible(Transform item, bool visible)
    {
        foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }
    }

    private static void SetCollidersEnabled(Transform item, bool enabled)
    {
        foreach (Collider collider in item.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = enabled;
        }
    }

    private static void EnsurePlayableCollider(Transform item)
    {
        if (item.GetComponentInChildren<Collider>(true) != null)
        {
            return;
        }

        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        bool hasBounds = false;
        Bounds localBounds = default;
        foreach (Renderer renderer in renderers)
        {
            Bounds worldBounds = renderer.bounds;
            Vector3[] corners =
            {
                new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.min.z),
                new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.max.z),
                new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.min.z),
                new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.max.z),
                new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.min.z),
                new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.max.z),
                new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.min.z),
                new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.max.z)
            };

            foreach (Vector3 corner in corners)
            {
                Vector3 localCorner = item.InverseTransformPoint(corner);
                if (!hasBounds)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        if (!hasBounds)
        {
            return;
        }

        BoxCollider collider = item.gameObject.AddComponent<BoxCollider>();
        collider.center = localBounds.center;
        collider.size = localBounds.size;
    }

    private void UpdateDraggedItem()
    {
        Vector2 screenOffset = (Vector2)Input.mousePosition - dragStartScreenPosition;
        if (screenOffset.sqrMagnitude < minimumDragPixels * minimumDragPixels)
        {
            return;
        }

        // Crossing the pointer threshold is enough to count as a deliberate
        // drag. Some shuffled items begin very close to another compatible
        // slot, so requiring a large world-space displacement made those
        // otherwise valid placements impossible to confirm.
        dragMoved = true;

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        if (dragPlane.Raycast(ray, out float enterDistance))
        {
            Vector3 nextPosition = ray.GetPoint(enterDistance) + dragOffset;
            // Hard-project every mouse position back onto the current front
            // view plane.  The object therefore moves only left/right/up/down
            // as seen by the player, with no motion toward or away from them.
            nextPosition = Vector3.ProjectOnPlane(nextPosition - dragPlanePoint, interactionPlaneNormal) + dragPlanePoint;
            draggedItem.PlayableItem.position = nextPosition;

        }
    }

    private void ReleaseDraggedItem()
    {
        if (!dragMoved)
        {
            draggedItem.PlayableItem.position = dragStartWorldPosition;
            RestoreDraggedBody();
            draggedItem = null;
            draggedBody = null;
            return;
        }

        WineItemState target = FindNearestFreeTarget(draggedItem);
        if (target != null)
        {
            // A target represents an authored slot, not ownership by one
            // numbered model. Any item of the same visual type may occupy it.
            draggedItem.PlayableItem.rotation = GetCurrentCorrectWorldRotation(target);
            draggedItem.PlayableItem.localScale = target.CorrectLocalScale;
            Vector3 snappedVisualCenter = GetVisualCenter(draggedItem.PlayableItem);
            draggedItem.PlayableItem.position += GetCurrentCorrectVisualCenter(target) - snappedVisualCenter;
            draggedItem.PlayableItem.gameObject.SetActive(true);
            SetRenderersVisible(draggedItem.PlayableItem, true);
            SetCollidersEnabled(draggedItem.PlayableItem, false);
            target.IsOccupied = true;
            draggedItem.IsLocked = true;
            LockCorrectItem(draggedItem.PlayableItem);
            Debug.Log($"[WineRotationPuzzle] Locked {draggedItem.PlayableItem.name} in compatible {target.ItemType} slot {target.PairId}.", this);
        }
        else
        {
            RestoreDraggedBody();
        }

        draggedItem = null;
        draggedBody = null;

        if (AreAllTargetsOccupied())
        {
            isComplete = true;
            Debug.Log("[WineRotationPuzzle] Complete: all wine items are placed and locked.", this);
        }
    }

    private WineItemState FindNearestFreeTarget(WineItemState source)
    {
        WineItemState nearest = null;
        float nearestDistance = positionTolerance;
        float nearestDepthError = 0f;
        float nearestAngleError = 0f;
        WineItemState closestSameType = null;
        float closestSameTypeDistance = float.PositiveInfinity;
        float closestSameTypeDepthError = 0f;
        float closestSameTypeAngleError = 0f;
        foreach (WineItemState candidate in items)
        {
            if (candidate.IsOccupied ||
                !string.Equals(candidate.ItemType, source.ItemType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Compare position inside the display plane separately from depth,
            // then compare only the visual in-screen rotation angle.
            Vector3 positionDelta = GetVisualCenter(source.PlayableItem) - GetCurrentCorrectVisualCenter(candidate);
            float depthError = Mathf.Abs(Vector3.Dot(positionDelta, interactionPlaneNormal));
            float distance = Vector3.ProjectOnPlane(positionDelta, interactionPlaneNormal).magnitude;
            float angleError = GetScreenAngleError(source, GetCurrentCorrectWorldRotation(candidate));
            if (distance < closestSameTypeDistance)
            {
                closestSameType = candidate;
                closestSameTypeDistance = distance;
                closestSameTypeDepthError = depthError;
                closestSameTypeAngleError = angleError;
            }

            if (distance <= nearestDistance && angleError <= rotationToleranceDegrees)
            {
                nearestDistance = distance;
                nearestDepthError = depthError;
                nearestAngleError = angleError;
                nearest = candidate;
            }
        }

        if (nearest != null)
        {
            Debug.Log($"[WineRotationPuzzle] Correct pose: '{source.PlayableItem.name}' → '{nearest.CorrectItem.name}'; " +
                      $"plane error={nearestDistance:F3} / {positionTolerance:F3}, " +
                      $"locked Z offset={nearestDepthError:F3}, " +
                      $"screen angle error={nearestAngleError:F1}° / {rotationToleranceDegrees:F1}°.", this);
        }
        else if (closestSameType != null)
        {
            Debug.Log($"[WineRotationPuzzle] Not placed: '{source.PlayableItem.name}' → '{closestSameType.CorrectItem.name}'; " +
                      $"plane error={closestSameTypeDistance:F3} / {positionTolerance:F3}, " +
                      $"locked Z offset={closestSameTypeDepthError:F3}, " +
                      $"screen angle error={closestSameTypeAngleError:F1}° / {rotationToleranceDegrees:F1}°. " +
                      $"sourcePos={source.PlayableItem.position}, targetPos={GetCurrentCorrectWorldPosition(closestSameType)}, " +
                      $"sourceRot={source.PlayableItem.eulerAngles}, targetRot={GetCurrentCorrectWorldRotation(closestSameType).eulerAngles}.", this);
        }

        return nearest;
    }

    private WineItemState FindPlayableItem(Transform hitTransform)
    {
        foreach (WineItemState item in items)
        {
            if (!item.IsLocked && item.PlayableItem != null && item.PlayableItem.gameObject.activeInHierarchy &&
                (hitTransform == item.PlayableItem || hitTransform.IsChildOf(item.PlayableItem)))
            {
                return item;
            }
        }

        return null;
    }

    private bool AreAllTargetsOccupied()
    {
        foreach (WineItemState item in items)
        {
            if (item.CorrectItem != null && !item.IsOccupied)
            {
                return false;
            }
        }

        return true;
    }

    private void RestoreDraggedBody()
    {
        if (draggedBody == null)
        {
            return;
        }

        draggedBody.isKinematic = draggedBodyWasKinematic;
        draggedBody.useGravity = draggedBodyUsedGravity;
    }

    private static void LockCorrectItem(Transform item)
    {
        foreach (Rigidbody body in item.GetComponentsInChildren<Rigidbody>(true))
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }
    }

    private static string GetItemType(string itemName)
    {
        string normalized = itemName.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        if (normalized.Contains("glassa"))
        {
            return "glassA";
        }

        if (normalized.Contains("glassb"))
        {
            return "glassB";
        }

        return normalized.Contains("bottle") ? "bottle" : null;
    }

    private bool IsWineItem(Transform candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        return candidate.GetComponent<Renderer>() != null ||
               candidate.GetComponentInChildren<Renderer>(true) != null;
    }

    private static bool IsCorrectItem(Transform candidate)
    {
        return candidate != null && candidate.name.EndsWith("correct", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPairId(string itemName)
    {
        string pairId = itemName;
        string[] stateSuffixes = { "correct", "start" };
        foreach (string suffix in stateSuffixes)
        {
            if (pairId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                pairId = pairId.Substring(0, pairId.Length - suffix.Length);
                break;
            }
        }

        return pairId;
    }

    private static Transform FindWineDisplayRoot()
    {
        string[] rootNames = { "酒瓶", "酒杯", "酒瓶酒杯" };
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid())
            {
                continue;
            }

            foreach (string rootName in rootNames)
            {
                if (string.Equals(candidate.name, rootName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
