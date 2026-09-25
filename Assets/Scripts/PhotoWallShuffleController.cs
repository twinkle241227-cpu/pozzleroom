using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Randomly reassigns photos to existing pins without moving the pins themselves.
/// This first stage changes positions only; photo rotations stay untouched.
/// </summary>
public sealed class PhotoWallShuffleController : MonoBehaviour
{
    [SerializeField] private Transform photoWallRoot;
    [SerializeField, Min(1)] private int maxPhotosPerPin = 2;
    [SerializeField] private Vector3 secondPhotoLocalOffset = new Vector3(0.0015f, -0.001f, 0.0015f);

    [Header("Random Rotation")]
    [SerializeField] private float[] rotationAngles = { -15f, -10f, -5f, 0f, 5f, 10f, 15f };

    [Header("Photo Hover Outline")]
    [SerializeField] private Color hoverOutlineColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField, Min(0.00001f)] private float hoverOutlineWidth = 0.001f;

    private readonly List<Transform> pins = new List<Transform>();
    private readonly List<PhotoState> photos = new List<PhotoState>();
    private int wallNormalAxis;
    private float pinPlaneCoordinate;
    private float photoPlaneOffset;

    private sealed class PhotoState
    {
        public Transform Transform;
        public Vector3 InitialPosition;
        public Quaternion InitialRotation;
        public Vector3 PlanarPinOffset;
    }

    private sealed class PinSlot
    {
        public Transform Pin;
        public int Index;
    }

    private void Awake()
    {
        if (photoWallRoot == null)
        {
            photoWallRoot = transform;
        }

        CollectPhotoWallObjects();
    }

    public void ConfigureHoverOutline(Color color, float width)
    {
        hoverOutlineColor = color;
        hoverOutlineWidth = Mathf.Max(0.00001f, width);
        foreach (PhotoHoverOutline outline in GetComponentsInChildren<PhotoHoverOutline>(true))
        {
            outline.Configure(hoverOutlineColor, hoverOutlineWidth);
        }
    }

    public bool TryGetPhotoSlotPosition(Transform photo, Transform pin, int slotIndex, out Vector3 position)
    {
        foreach (PhotoState state in photos)
        {
            if (state.Transform != photo)
            {
                continue;
            }

            Vector3 stackingOffset = slotIndex == 0 ? Vector3.zero : secondPhotoLocalOffset;
            SetAxis(ref stackingOffset, wallNormalAxis, 0f);
            Vector3 localPositionOffset = state.PlanarPinOffset + stackingOffset;
            SetAxis(ref localPositionOffset, wallNormalAxis, photoPlaneOffset);
            position = pin.position + photoWallRoot.TransformVector(localPositionOffset);
            return true;
        }

        position = default;
        return false;
    }

    public bool TryGetInitialRotation(Transform photo, out Quaternion rotation)
    {
        foreach (PhotoState state in photos)
        {
            if (state.Transform == photo)
            {
                rotation = state.InitialRotation;
                return true;
            }
        }

        rotation = default;
        return false;
    }

    public void CopyPhotoWallObjects(List<Transform> photoDestination, List<Transform> pinDestination)
    {
        photoDestination.Clear();
        pinDestination.Clear();
        foreach (PhotoState photo in photos)
        {
            photoDestination.Add(photo.Transform);
        }

        pinDestination.AddRange(pins);
    }

    /// <summary>Moves photos only; fixed pins are never moved and each pin has at most two slots.</summary>
    public void ShufflePhotos()
    {
        CollectPhotoWallObjects();
        if (pins.Count == 0 || photos.Count == 0)
        {
            Debug.LogWarning("Photo-wall shuffle found no pins or no photos.", this);
            return;
        }

        List<PinSlot> availableSlots = new List<PinSlot>(pins.Count * maxPhotosPerPin);
        foreach (Transform pin in pins)
        {
            for (int index = 0; index < maxPhotosPerPin; index++)
            {
                availableSlots.Add(new PinSlot { Pin = pin, Index = index });
            }
        }

        if (photos.Count > availableSlots.Count)
        {
            Debug.LogWarning($"Photo-wall shuffle needs {photos.Count} slots, but only {availableSlots.Count} are available.", this);
            return;
        }

        for (int index = availableSlots.Count - 1; index > 0; index--)
        {
            int otherIndex = UnityEngine.Random.Range(0, index + 1);
            PinSlot temporary = availableSlots[index];
            availableSlots[index] = availableSlots[otherIndex];
            availableSlots[otherIndex] = temporary;
        }

        for (int index = 0; index < photos.Count; index++)
        {
            PhotoState photo = photos[index];
            PinSlot slot = availableSlots[index];
            Vector3 stackingOffset = slot.Index == 0
                ? Vector3.zero
                : secondPhotoLocalOffset;
            SetAxis(ref stackingOffset, wallNormalAxis, 0f);

            Vector3 localPositionOffset = photo.PlanarPinOffset + stackingOffset;
            SetAxis(ref localPositionOffset, wallNormalAxis, photoPlaneOffset);

            float angle = GetRandomRotationAngle();
            Vector3 localRotationAxis = GetAxisVector(wallNormalAxis);
            Quaternion localRotation = Quaternion.AngleAxis(angle, localRotationAxis);
            photo.Transform.position = slot.Pin.position + photoWallRoot.TransformVector(localRotation * localPositionOffset);
            photo.Transform.rotation = Quaternion.AngleAxis(angle, photoWallRoot.TransformDirection(localRotationAxis)) * photo.InitialRotation;
        }
    }

    /// <summary>Restores the original photo positions and rotations.</summary>
    public void ResetPhotos()
    {
        foreach (PhotoState photo in photos)
        {
            photo.Transform.position = photo.InitialPosition;
            photo.Transform.rotation = photo.InitialRotation;
        }
    }

    private void CollectPhotoWallObjects()
    {
        pins.Clear();
        photos.Clear();

        foreach (Transform candidate in photoWallRoot.GetComponentsInChildren<Transform>(true))
        {
            if (candidate == photoWallRoot || IsRuntimeOutline(candidate))
            {
                continue;
            }

            if (candidate.name.IndexOf("pin", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                pins.Add(candidate);
            }
        }

        if (pins.Count == 0)
        {
            return;
        }

        wallNormalAxis = GetSmallestSpreadAxis(pins);
        float pinCoordinateSum = 0f;
        foreach (Transform pin in pins)
        {
            pinCoordinateSum += GetAxis(photoWallRoot.InverseTransformPoint(pin.position), wallNormalAxis);
        }

        pinPlaneCoordinate = pinCoordinateSum / pins.Count;
        float photoOffsetSum = 0f;
        int photoOffsetCount = 0;

        foreach (Transform candidate in photoWallRoot.GetComponentsInChildren<Transform>(true))
        {
            if (candidate == photoWallRoot ||
                candidate.name.IndexOf("photo", StringComparison.OrdinalIgnoreCase) < 0 ||
                candidate.GetComponent<Collider>() == null ||
                IsRuntimeOutline(candidate))
            {
                continue;
            }

            Transform originalPin = FindNearestPin(candidate.position);
            if (originalPin == null)
            {
                continue;
            }

            Vector3 localPinOffset = photoWallRoot.InverseTransformPoint(candidate.position) -
                photoWallRoot.InverseTransformPoint(originalPin.position);
            photoOffsetSum += GetAxis(photoWallRoot.InverseTransformPoint(candidate.position), wallNormalAxis) - pinPlaneCoordinate;
            photoOffsetCount++;
            SetAxis(ref localPinOffset, wallNormalAxis, 0f);

            PhotoHoverOutline hoverOutline = candidate.GetComponent<PhotoHoverOutline>();
            if (hoverOutline == null)
            {
                hoverOutline = candidate.gameObject.AddComponent<PhotoHoverOutline>();
            }
            hoverOutline.Configure(hoverOutlineColor, hoverOutlineWidth);

            photos.Add(new PhotoState
            {
                Transform = candidate,
                InitialPosition = candidate.position,
                InitialRotation = candidate.rotation,
                PlanarPinOffset = localPinOffset
            });
        }

        float originalPlaneOffset = photoOffsetCount > 0 ? photoOffsetSum / photoOffsetCount : 0f;
        Camera activeCamera = Camera.main;
        if (activeCamera == null)
        {
            photoPlaneOffset = originalPlaneOffset;
            return;
        }

        float cameraCoordinate = GetAxis(photoWallRoot.InverseTransformPoint(activeCamera.transform.position), wallNormalAxis);
        float outwardSign = cameraCoordinate >= pinPlaneCoordinate ? 1f : -1f;
        photoPlaneOffset = outwardSign * Mathf.Max(0.0001f, Mathf.Abs(originalPlaneOffset));
    }

    private Transform FindNearestPin(Vector3 position)
    {
        Transform nearestPin = null;
        float nearestDistanceSquared = float.PositiveInfinity;

        foreach (Transform pin in pins)
        {
            float distanceSquared = (pin.position - position).sqrMagnitude;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestPin = pin;
            }
        }

        return nearestPin;
    }

    private int GetSmallestSpreadAxis(IReadOnlyList<Transform> transforms)
    {
        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Transform candidate in transforms)
        {
            Vector3 localPosition = photoWallRoot.InverseTransformPoint(candidate.position);
            minimum = Vector3.Min(minimum, localPosition);
            maximum = Vector3.Max(maximum, localPosition);
        }

        Vector3 spread = maximum - minimum;
        return spread.x <= spread.y && spread.x <= spread.z ? 0 : spread.y <= spread.z ? 1 : 2;
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static void SetAxis(ref Vector3 value, int axis, float component)
    {
        if (axis == 0) value.x = component;
        else if (axis == 1) value.y = component;
        else value.z = component;
    }

    private static Vector3 GetAxisVector(int axis)
    {
        return axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
    }

    private static bool IsRuntimeOutline(Transform candidate)
    {
        return (candidate.gameObject.hideFlags & HideFlags.DontSave) != 0 ||
            candidate.name.IndexOf("Hover Outline", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private float GetRandomRotationAngle()
    {
        if (rotationAngles == null || rotationAngles.Length == 0)
        {
            return 0f;
        }

        return rotationAngles[UnityEngine.Random.Range(0, rotationAngles.Length)];
    }
}
