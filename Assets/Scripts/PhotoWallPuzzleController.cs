using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>Owns photo-to-pin slots, drag interaction, and number-based completion.</summary>
public sealed class PhotoWallPuzzleController : MonoBehaviour
{
    public static PhotoWallPuzzleController ActiveInstance { get; private set; }

    [Header("Drag")]
    [SerializeField, Min(0f)] private float snapDistance = 0.04f;
    [SerializeField, Min(0.00001f)] private float smallerPhotoFrontOffset = 0.001f;
    [SerializeField] private bool ignorePointerOverUi = true;

    [Header("Completion")]
    private Color correctOutlineColor = new Color(0.25f, 1f, 0.38f, 1f);
    [SerializeField] private UnityEvent onPuzzleSolved;

    private readonly List<PhotoState> photos = new List<PhotoState>();
    private readonly List<PinState> pins = new List<PinState>();
    private readonly List<Transform> shuffledPhotos = new List<Transform>();
    private readonly List<Transform> shuffledPins = new List<Transform>();
    private PhotoWallShuffleController shuffleController;
    private Camera targetCamera;
    private PhotoWallViewController viewController;
    private Plane photoWallPlane;
    private PhotoState draggedPhoto;
    private PinState originalPin;
    private int originalSlot;
    private Vector3 pointerToPhotoOffset;
    private float draggedPhotoPlaneOffset;
    private bool initialized;
    private bool isSolved;
    private bool areAllPhotosLocked;
    private float nextInitializationAttemptTime;
    private Transform discoveredWallRoot;
    private string lastDiscoveryReport;

    public bool IsDragging => draggedPhoto != null;

    public void ConfigureCorrectOutline(Color color)
    {
        correctOutlineColor = color;

        foreach (PhotoState photo in photos)
        {
            if (!photo.IsCorrect || photo.Transform == null)
            {
                continue;
            }

            PhotoHoverOutline outline = photo.Transform.GetComponent<PhotoHoverOutline>();
            if (outline != null)
            {
                outline.SetCorrectState(correctOutlineColor);
            }
        }
    }

    private sealed class PhotoState
    {
        public Transform Transform;
        public int SequenceNumber;
        public Quaternion CorrectRotation;
        public PinState CurrentPin;
        public int CurrentSlot;
        public bool IsCorrect;
        public float WallPlaneOffset;
        public Vector3 SnappedBasePosition;
        public bool HasSnappedBasePosition;
    }

    private sealed class PinState
    {
        public Transform Transform;
        public int SequenceNumber;
        public readonly PhotoState[] Slots = new PhotoState[2];
    }

    private void Awake()
    {
        ActiveInstance = this;
        targetCamera = Camera.main;
        viewController = GetComponentInChildren<PhotoWallViewController>(true);
        if (GetComponent<PhotoWallDragMovementDebug>() == null)
        {
            gameObject.AddComponent<PhotoWallDragMovementDebug>();
        }
    }

    private void OnDestroy()
    {
        if (ActiveInstance == this) ActiveInstance = null;
    }

    public void Initialize(PhotoWallShuffleController shuffle)
    {
        shuffleController = shuffle;
        targetCamera = targetCamera == null ? Camera.main : targetCamera;
        CollectStates();
        initialized = photos.Count > 0 && pins.Count > 0;
        isSolved = false;
        areAllPhotosLocked = false;
        string rootName = discoveredWallRoot == null ? "none" : discoveredWallRoot.name;
        string report = $"photos={photos.Count}, pins={pins.Count}, wallRoot={rootName}";
        if (initialized || report != lastDiscoveryReport)
        {
            Debug.Log($"[PhotoWallPuzzle] Discovery: {report}.", this);
            lastDiscoveryReport = report;
        }
    }

    private void Update()
    {
        if (!initialized && Time.unscaledTime >= nextInitializationAttemptTime &&
            (viewController == null || viewController.IsFocused))
        {
            nextInitializationAttemptTime = Time.unscaledTime + 0.5f;
            PhotoWallShuffleController availableShuffle = shuffleController != null
                ? shuffleController
                : GetComponent<PhotoWallShuffleController>();
            if (availableShuffle != null)
            {
                Initialize(availableShuffle);
            }
        }

        if (isSolved || targetCamera == null ||
            (viewController != null && !viewController.IsFocused))
        {
            return;
        }

        if (draggedPhoto == null)
        {
            if (Input.GetMouseButtonDown(0) &&
                (!ignorePointerOverUi || EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                TryBeginDrag();
            }
            return;
        }

        UpdateDraggedPhoto();
        if (Input.GetMouseButtonUp(0))
        {
            FinishDrag();
        }
    }

    private void TryBeginDrag()
    {
        PhotoHoverOutline hovered = GetPhotoUnderPointer();

        // The photo-wall prefab can be instantiated beneath a wrapper transform at
        // runtime. Use the clicked photo itself as the discovery seed: this also
        // works when Unity enters Play Mode without re-running component Awake().
        if (!initialized)
        {
            CollectStates(hovered == null ? null : hovered.transform);
            initialized = photos.Count > 0 && pins.Count > 0;
        }

        if (hovered == null)
        {
            Debug.Log("[PhotoWallPuzzle] Drag did not start: no photo collider was under the pointer.", this);
            return;
        }

        if (!TryFindPhoto(hovered.transform, out PhotoState photo))
        {
            BeginFreeDrag(hovered.transform);
            return;
        }

        if (photo.IsCorrect)
        {
            // Correctness is visual only: a player may still move this photo.
            photo.IsCorrect = false;
            PhotoHoverOutline existingOutline = photo.Transform.GetComponent<PhotoHoverOutline>();
            if (existingOutline != null) existingOutline.ClearCorrectState();
        }

        draggedPhoto = photo;
        originalPin = photo.CurrentPin;
        originalSlot = photo.CurrentSlot;
        if (originalPin != null)
        {
            originalPin.Slots[originalSlot] = null;
            ApplyVisualStacking(originalPin);
        }

        // A selected photo automatically returns to its authored, correct angle.
        draggedPhoto.Transform.rotation = draggedPhoto.CorrectRotation;
        if (originalPin != null && shuffleController.TryGetPhotoSlotPosition(draggedPhoto.Transform, originalPin.Transform, originalSlot, out Vector3 correctedPosition))
        {
            draggedPhoto.Transform.position = ConstrainToPhotoWallPlane(correctedPosition, draggedPhoto.WallPlaneOffset);
        }

        // Keep a photo's intentional layer depth, but never let drag input add
        // a new component along the photo-wall normal.
        draggedPhotoPlaneOffset = photoWallPlane.GetDistanceToPoint(draggedPhoto.Transform.position);

        if (TryGetPointerOnWall(out Vector3 pointerPosition))
        {
            pointerToPhotoOffset = Vector3.ProjectOnPlane(
                draggedPhoto.Transform.position - pointerPosition,
                photoWallPlane.normal);
        }
        else
        {
            pointerToPhotoOffset = Vector3.zero;
        }

        PhotoHoverOutline.SetHighlighted(null);
        Debug.Log($"[PhotoWallPuzzle] Drag started: {draggedPhoto.Transform.name} from {originalPin.Transform.name} slot {originalSlot}.", this);
    }

    private void UpdateDraggedPhoto()
    {
        if (!TryGetPointerOnWall(out Vector3 pointerPosition))
        {
            return;
        }

        Vector3 candidatePosition = pointerPosition + pointerToPhotoOffset;
        draggedPhoto.Transform.position = ConstrainToPhotoWallPlane(candidatePosition, draggedPhotoPlaneOffset);
        draggedPhoto.Transform.rotation = draggedPhoto.CorrectRotation;
    }

    private void FinishDrag()
    {
        // Never make dragging depend on pin discovery. This fallback keeps the
        // photo at its mouse-driven position if the scene has no discoverable pins.
        if (pins.Count == 0)
        {
            Debug.Log($"[PhotoWallPuzzle] Drag finished without pin snapping: {draggedPhoto.Transform.name}.", this);
            draggedPhoto = null;
            originalPin = null;
            return;
        }

        PinState destination = FindClosestAvailablePin(draggedPhoto.Transform.position, out float distance);
        if (destination != null && distance <= snapDistance)
        {
            int slot = destination.Slots[0] == null ? 0 : 1;
            if (originalPin == null)
            {
                SnapFreeDraggedPhoto(destination, slot);
            }
            else
            {
                PlacePhoto(draggedPhoto, destination, slot);
            }
        }
        else if (originalPin != null)
        {
            PlacePhoto(draggedPhoto, originalPin, originalSlot);
        }

        PhotoState finishedPhoto = draggedPhoto;
        draggedPhoto = null;
        originalPin = null;
        UpdateCorrectState(finishedPhoto);
        CheckForCompletion();
    }

    private void SnapFreeDraggedPhoto(PinState pin, int slot)
    {
        draggedPhoto.CurrentPin = pin;
        draggedPhoto.CurrentSlot = slot;
        pin.Slots[slot] = draggedPhoto;

        if (shuffleController != null &&
            shuffleController.TryGetPhotoSlotPosition(draggedPhoto.Transform, pin.Transform, slot, out Vector3 position))
        {
            draggedPhoto.Transform.position = ConstrainToPhotoWallPlane(position, draggedPhoto.WallPlaneOffset);
        }
        else
        {
            draggedPhoto.Transform.position = ConstrainToPhotoWallPlane(pin.Transform.position, draggedPhoto.WallPlaneOffset);
        }

        draggedPhoto.SnappedBasePosition = draggedPhoto.Transform.position;
        draggedPhoto.HasSnappedBasePosition = true;
        ApplyVisualStacking(pin);
    }

    private void BeginFreeDrag(Transform photoTransform)
    {
        draggedPhoto = new PhotoState
        {
            Transform = photoTransform,
            SequenceNumber = TryGetSequenceNumber(photoTransform.name, "photo", out int sequenceNumber)
                ? sequenceNumber
                : -1,
            CorrectRotation = photoTransform.rotation,
            CurrentPin = null,
            CurrentSlot = -1,
            WallPlaneOffset = 0f
        };
        originalPin = null;
        originalSlot = -1;

        // The wall is seen face-on while playing this puzzle. A plane parallel to
        // the camera view is a reliable fallback even if pin objects were renamed.
        photoWallPlane = new Plane(-targetCamera.transform.forward, photoTransform.position);
        draggedPhoto.WallPlaneOffset = photoWallPlane.GetDistanceToPoint(photoTransform.position);
        draggedPhotoPlaneOffset = draggedPhoto.WallPlaneOffset;
        pointerToPhotoOffset = Vector3.zero;
        if (TryGetPointerOnWall(out Vector3 pointerPosition))
        {
            pointerToPhotoOffset = Vector3.ProjectOnPlane(
                photoTransform.position - pointerPosition,
                photoWallPlane.normal);
        }

        PhotoHoverOutline.SetHighlighted(null);
        Debug.Log($"[PhotoWallPuzzle] Free drag started: {photoTransform.name}.", this);
    }

    private void PlacePhoto(PhotoState photo, PinState pin, int slot)
    {
        photo.CurrentPin = pin;
        photo.CurrentSlot = slot;
        pin.Slots[slot] = photo;
        photo.Transform.rotation = photo.CorrectRotation;
        if (shuffleController.TryGetPhotoSlotPosition(photo.Transform, pin.Transform, slot, out Vector3 position))
        {
            photo.Transform.position = ConstrainToPhotoWallPlane(position, photo.WallPlaneOffset);
        }

        photo.SnappedBasePosition = photo.Transform.position;
        photo.HasSnappedBasePosition = true;
        ApplyVisualStacking(pin);
    }

    private void ApplyVisualStacking(PinState pin)
    {
        if (pin == null) return;

        PhotoState first = pin.Slots[0];
        PhotoState second = pin.Slots[1];
        if (first != null && !first.HasSnappedBasePosition)
        {
            first.SnappedBasePosition = first.Transform.position;
            first.HasSnappedBasePosition = true;
        }
        if (second != null && !second.HasSnappedBasePosition)
        {
            second.SnappedBasePosition = second.Transform.position;
            second.HasSnappedBasePosition = true;
        }

        if (first != null) first.Transform.position = first.SnappedBasePosition;
        if (second != null) second.Transform.position = second.SnappedBasePosition;
        if (first == null || second == null) return;

        PhotoState smaller = GetPhotoArea(first.Transform) <= GetPhotoArea(second.Transform) ? first : second;
        Vector3 towardCamera = photoWallPlane.normal.sqrMagnitude > 0f
            ? photoWallPlane.normal.normalized
            : -targetCamera.transform.forward;
        smaller.Transform.position = smaller.SnappedBasePosition + towardCamera * smallerPhotoFrontOffset;
    }

    private static float GetPhotoArea(Transform photo)
    {
        float totalArea = 0f;
        foreach (MeshFilter filter in photo.GetComponentsInChildren<MeshFilter>(true))
        {
            if (IsRuntimeOutline(filter.transform) || filter.sharedMesh == null) continue;
            Vector3 size = filter.sharedMesh.bounds.size;
            Vector3 x = filter.transform.TransformVector(Vector3.right * size.x);
            Vector3 y = filter.transform.TransformVector(Vector3.up * size.y);
            Vector3 z = filter.transform.TransformVector(Vector3.forward * size.z);
            float first = x.magnitude;
            float second = y.magnitude;
            float third = z.magnitude;
            if (first < second) { float swap = first; first = second; second = swap; }
            if (second < third) { float swap = second; second = third; third = swap; }
            if (first < second) { float swap = first; first = second; second = swap; }
            totalArea += first * second;
        }

        if (totalArea > 0f) return totalArea;
        Collider collider = photo.GetComponent<Collider>();
        if (collider == null) return 0f;
        Vector3 fallback = collider.bounds.size;
        return fallback.x * fallback.y + fallback.x * fallback.z + fallback.y * fallback.z;
    }

    private void UpdateCorrectState(PhotoState photo)
    {
        if (photo == null)
        {
            return;
        }

        bool isCorrect = photo.CurrentPin != null &&
                         photo.SequenceNumber >= 0 &&
                         photo.SequenceNumber == photo.CurrentPin.SequenceNumber;
        photo.IsCorrect = isCorrect;

        PhotoHoverOutline outline = photo.Transform.GetComponent<PhotoHoverOutline>();
        if (outline == null)
        {
            return;
        }

        if (isCorrect)
        {
            outline.SetCorrectState(correctOutlineColor);
            Debug.Log($"[PhotoWallPuzzle] Correct number match: {photo.Transform.name} -> {photo.CurrentPin.Transform.name} (#{photo.SequenceNumber}).", this);
        }
        else
        {
            outline.ClearCorrectState();
        }
    }

    private PinState FindClosestAvailablePin(Vector3 position, out float distance)
    {
        PinState closest = null;
        float closestDistanceSquared = float.PositiveInfinity;
        foreach (PinState pin in pins)
        {
            if (pin.Slots[0] != null && pin.Slots[1] != null)
            {
                continue;
            }

            float candidateDistanceSquared = (pin.Transform.position - position).sqrMagnitude;
            if (candidateDistanceSquared < closestDistanceSquared)
            {
                closestDistanceSquared = candidateDistanceSquared;
                closest = pin;
            }
        }

        distance = closest == null ? float.PositiveInfinity : Mathf.Sqrt(closestDistanceSquared);
        return closest;
    }

    private PhotoHoverOutline GetPhotoUnderPointer()
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, ~0, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        PhotoHoverOutline selected = null;
        float nearestDistance = float.PositiveInfinity;
        const float distanceTieTolerance = 0.0001f;
        foreach (RaycastHit hit in hits)
        {
            PhotoHoverOutline candidate = hit.collider.GetComponentInParent<PhotoHoverOutline>();
            if (candidate == null || !candidate.isActiveAndEnabled) continue;
            if (hit.distance < nearestDistance - distanceTieTolerance ||
                (Mathf.Abs(hit.distance - nearestDistance) <= distanceTieTolerance &&
                 (selected == null || candidate.GetStableHoverPriority() > selected.GetStableHoverPriority())))
            {
                selected = candidate;
                nearestDistance = hit.distance;
            }
        }

        return selected;
    }

    private bool TryGetPointerOnWall(out Vector3 point)
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        if (photoWallPlane.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }

        point = default;
        return false;
    }

    private void CollectStates(Transform clickedPhoto = null)
    {
        photos.Clear();
        pins.Clear();
        discoveredWallRoot = null;

        // Hover components are attached to the actual instantiated photo objects,
        // so they remain trustworthy even when this controller itself is placed on
        // a wrapper rather than on the wall hierarchy.
        List<Transform> photoRoots = new List<Transform>();
        if (clickedPhoto != null)
        {
            discoveredWallRoot = FindWallRoot(clickedPhoto, null);
            if (discoveredWallRoot == null)
            {
                discoveredWallRoot = clickedPhoto.parent;
            }
            if (discoveredWallRoot != null)
            {
                foreach (Transform candidate in discoveredWallRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate == discoveredWallRoot || IsRuntimeOutline(candidate) ||
                        !TryGetSequenceNumber(candidate.name, "photo", out _) || candidate.GetComponent<Collider>() == null)
                    {
                        continue;
                    }

                    photoRoots.Add(candidate);
                }
            }
        }
        else
        {
            foreach (PhotoHoverOutline outline in PhotoHoverOutline.GetRegisteredPhotos())
            {
                Transform candidate = outline.transform;
                if (candidate == null || IsRuntimeOutline(candidate) || !TryGetSequenceNumber(candidate.name, "photo", out _)) continue;
                photoRoots.Add(candidate);
                discoveredWallRoot = FindWallRoot(candidate, discoveredWallRoot);
            }
        }

        if (discoveredWallRoot == null || photoRoots.Count == 0) return;

        HashSet<Transform> uniquePins = new HashSet<Transform>();
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid() ||
                IsRuntimeOutline(candidate) ||
                candidate.name.IndexOf("pin", StringComparison.OrdinalIgnoreCase) < 0 ||
                !uniquePins.Add(candidate))
            {
                continue;
            }

            if (!TryGetSequenceNumber(candidate.name, "pin", out int sequenceNumber))
            {
                Debug.LogWarning($"[PhotoWallPuzzle] Pin '{candidate.name}' has no numeric suffix and will not participate in completion.", candidate);
                continue;
            }

            pins.Add(new PinState { Transform = candidate, SequenceNumber = sequenceNumber });
        }

        if (pins.Count == 0) return;
        Vector3 planePoint = Vector3.zero;
        foreach (PinState pin in pins) planePoint += pin.Transform.position;
        planePoint /= pins.Count;
        Vector3 planeNormal = GetWallNormal();
        photoWallPlane = new Plane(planeNormal, planePoint);

        foreach (Transform candidate in photoRoots)
        {
            if (candidate == null || !TryGetSequenceNumber(candidate.name, "photo", out int sequenceNumber)) continue;
            if (!shuffleController.TryGetInitialRotation(candidate, out Quaternion correctRotation))
            {
                correctRotation = candidate.rotation;
            }

            PinState nearest = FindClosestPin(candidate.position);
            int slot = nearest.Slots[0] == null ? 0 : 1;
            PhotoState state = new PhotoState
            {
                Transform = candidate,
                SequenceNumber = sequenceNumber,
                CorrectRotation = correctRotation,
                CurrentPin = nearest,
                CurrentSlot = slot,
                WallPlaneOffset = photoWallPlane.GetDistanceToPoint(candidate.position),
                SnappedBasePosition = candidate.position,
                HasSnappedBasePosition = true
            };
            nearest.Slots[slot] = state;
            photos.Add(state);
        }

        foreach (PinState pin in pins)
        {
            ApplyVisualStacking(pin);
        }
    }

    private Vector3 GetWallNormal()
    {
        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (PinState pin in pins)
        {
            Vector3 localPosition = discoveredWallRoot.InverseTransformPoint(pin.Transform.position);
            minimum = Vector3.Min(minimum, localPosition);
            maximum = Vector3.Max(maximum, localPosition);
        }

        Vector3 spread = maximum - minimum;
        Vector3 localNormal = spread.x <= spread.y && spread.x <= spread.z ? Vector3.right :
            spread.y <= spread.z ? Vector3.up : Vector3.forward;
        Vector3 worldNormal = discoveredWallRoot.TransformDirection(localNormal);
        return Vector3.Dot(worldNormal, targetCamera.transform.position - discoveredWallRoot.position) >= 0f ? worldNormal : -worldNormal;
    }

    private Vector3 ConstrainToPhotoWallPlane(Vector3 position, float planeOffset)
    {
        float currentOffset = photoWallPlane.GetDistanceToPoint(position);
        return position - photoWallPlane.normal * (currentOffset - planeOffset);
    }

    private PinState FindClosestPin(Vector3 position)
    {
        PinState closest = null;
        float closestDistanceSquared = float.PositiveInfinity;
        foreach (PinState pin in pins)
        {
            float distanceSquared = (pin.Transform.position - position).sqrMagnitude;
            if (distanceSquared < closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                closest = pin;
            }
        }

        return closest;
    }

    private bool TryFindPhoto(Transform transformToFind, out PhotoState photo)
    {
        foreach (PhotoState candidate in photos)
        {
            if (candidate.Transform == transformToFind)
            {
                photo = candidate;
                return true;
            }
        }

        photo = null;
        return false;
    }

    private void CheckForCompletion()
    {
        if (areAllPhotosLocked || photos.Count == 0)
        {
            return;
        }

        foreach (PhotoState photo in photos)
        {
            if (!photo.IsCorrect)
            {
                return;
            }
        }

        isSolved = true;
        areAllPhotosLocked = true;
        foreach (PhotoState photo in photos)
        {
            PhotoHoverOutline outline = photo.Transform.GetComponent<PhotoHoverOutline>();
            if (outline != null)
            {
                outline.SetCorrectState(correctOutlineColor);
            }

            // Final lock only: until the whole wall is correct, individual photos
            // remain draggable even while they have a green correctness outline.
            foreach (Collider collider in photo.Transform.GetComponents<Collider>())
            {
                collider.enabled = false;
            }
        }

        Debug.Log("Photo-wall puzzle solved.", this);
        onPuzzleSolved?.Invoke();
    }

    /// <summary>
    /// Reads the first integer after a marker, so Aphoto01 and pin01 both map
    /// to sequence number 1. Prefix letters do not affect the puzzle result.
    /// </summary>
    private static bool TryGetSequenceNumber(string name, string marker, out int sequenceNumber)
    {
        int markerIndex = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            sequenceNumber = -1;
            return false;
        }

        int digitStart = markerIndex + marker.Length;
        while (digitStart < name.Length && !char.IsDigit(name[digitStart]))
        {
            digitStart++;
        }

        if (digitStart >= name.Length)
        {
            sequenceNumber = -1;
            return false;
        }

        int digitEnd = digitStart;
        while (digitEnd < name.Length && char.IsDigit(name[digitEnd]))
        {
            digitEnd++;
        }

        return int.TryParse(name.Substring(digitStart, digitEnd - digitStart), out sequenceNumber);
    }

    private static bool IsRuntimeOutline(Transform candidate)
    {
        return (candidate.gameObject.hideFlags & HideFlags.DontSave) != 0 ||
            candidate.name.IndexOf("Hover Outline", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Transform FindWallRoot(Transform photo, Transform currentRoot)
    {
        Transform best = currentRoot;
        for (Transform ancestor = photo.parent; ancestor != null; ancestor = ancestor.parent)
        {
            int pinCount = 0;
            foreach (Transform child in ancestor.GetComponentsInChildren<Transform>(true))
            {
                if (TryGetSequenceNumber(child.name, "pin", out _)) pinCount++;
            }

            if (pinCount > 0)
            {
                // Prefer the smallest common hierarchy containing both object sets.
                return ancestor;
            }
        }

        return best;
    }
}
