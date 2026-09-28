using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporary drag diagnostics. Records world and wall-local movement so any
/// unwanted displacement along the wall normal is visible in the Console.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class PhotoWallDragMovementDebug : MonoBehaviour
{
    [SerializeField, Min(1)] private int logEveryFrames = 6;
    [SerializeField, Min(0.000001f)] private float normalMovementTolerance = 0.0001f;

    private readonly List<Transform> photos = new List<Transform>();
    private readonly List<Transform> pins = new List<Transform>();
    private readonly Dictionary<Transform, Vector3> startWorldPositions = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Vector3> previousWorldPositions = new Dictionary<Transform, Vector3>();

    private PhotoWallPuzzleController puzzleController;
    private Transform wallRoot;
    private Vector3 wallNormal;
    private bool wasDragging;
    private int frameCount;
    private float largestNormalDrift;

    private void Awake()
    {
        puzzleController = GetComponent<PhotoWallPuzzleController>();
        wallRoot = GetComponent<PhotoWallShuffleController>() != null ? transform : transform;
        DiscoverWallObjects();
    }

    private void Update()
    {
        if (puzzleController == null)
        {
            puzzleController = GetComponent<PhotoWallPuzzleController>();
        }

        bool isDragging = puzzleController != null && puzzleController.IsDragging;
        if (isDragging && !wasDragging)
        {
            BeginCapture();
        }
        else if (isDragging)
        {
            LogMovementSample();
        }
        else if (!isDragging && wasDragging)
        {
            EndCapture();
        }

        wasDragging = isDragging;
    }

    private void BeginCapture()
    {
        DiscoverWallObjects();
        startWorldPositions.Clear();
        previousWorldPositions.Clear();
        foreach (Transform photo in photos)
        {
            startWorldPositions[photo] = photo.position;
            previousWorldPositions[photo] = photo.position;
        }

        frameCount = 0;
        largestNormalDrift = 0f;
        Debug.Log($"[PhotoDragDebug] BEGIN photos={photos.Count}; wallRoot={wallRoot.name}; wallNormal={wallNormal:F5}; mouse={Input.mousePosition}.", this);
    }

    private void LogMovementSample()
    {
        frameCount++;
        Transform movedPhoto = null;
        float largestStep = 0f;
        foreach (Transform photo in photos)
        {
            if (!previousWorldPositions.TryGetValue(photo, out Vector3 previous))
            {
                previousWorldPositions[photo] = photo.position;
                continue;
            }

            float step = (photo.position - previous).sqrMagnitude;
            if (step > largestStep)
            {
                largestStep = step;
                movedPhoto = photo;
            }

            previousWorldPositions[photo] = photo.position;
        }

        if (movedPhoto == null)
        {
            return;
        }

        Vector3 worldDelta = movedPhoto.position - startWorldPositions[movedPhoto];
        Vector3 localDelta = wallRoot.InverseTransformVector(worldDelta);
        float normalDelta = Vector3.Dot(worldDelta, wallNormal);
        largestNormalDrift = Mathf.Max(largestNormalDrift, Mathf.Abs(normalDelta));
        if (frameCount % logEveryFrames != 0 && Mathf.Abs(normalDelta) <= normalMovementTolerance)
        {
            return;
        }

        string severity = Mathf.Abs(normalDelta) > normalMovementTolerance ? "WARNING off-plane" : "OK planar";
        Debug.Log($"[PhotoDragDebug] {severity}; photo={movedPhoto.name}; frame={frameCount}; worldDelta={worldDelta:F6}; localDelta={localDelta:F6}; normalDelta={normalDelta:F6}; mouse={Input.mousePosition}.", this);
    }

    private void EndCapture()
    {
        Transform movedPhoto = null;
        float largestDistance = 0f;
        foreach (Transform photo in photos)
        {
            if (!startWorldPositions.TryGetValue(photo, out Vector3 start)) continue;
            float distance = (photo.position - start).sqrMagnitude;
            if (distance > largestDistance)
            {
                largestDistance = distance;
                movedPhoto = photo;
            }
        }

        if (movedPhoto == null)
        {
            Debug.Log("[PhotoDragDebug] END: no photo transform changed.", this);
            return;
        }

        Vector3 worldDelta = movedPhoto.position - startWorldPositions[movedPhoto];
        Vector3 localDelta = wallRoot.InverseTransformVector(worldDelta);
        float normalDelta = Vector3.Dot(worldDelta, wallNormal);
        Debug.Log($"[PhotoDragDebug] END; photo={movedPhoto.name}; worldDelta={worldDelta:F6}; localDelta={localDelta:F6}; normalDelta={normalDelta:F6}; largestNormalDrift={largestNormalDrift:F6}.", this);
    }

    private void DiscoverWallObjects()
    {
        photos.Clear();
        pins.Clear();
        foreach (Transform candidate in wallRoot.GetComponentsInChildren<Transform>(true))
        {
            string name = candidate.name;
            if (name.IndexOf("Hover Outline", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (name.IndexOf("photo", System.StringComparison.OrdinalIgnoreCase) >= 0 && candidate.GetComponent<Collider>() != null)
            {
                photos.Add(candidate);
            }
            else if (name.IndexOf("pin", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                pins.Add(candidate);
            }
        }

        wallNormal = CalculateWallNormal();
    }

    private Vector3 CalculateWallNormal()
    {
        if (pins.Count < 2)
        {
            return -Camera.main.transform.forward;
        }

        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Transform pin in pins)
        {
            Vector3 local = wallRoot.InverseTransformPoint(pin.position);
            minimum = Vector3.Min(minimum, local);
            maximum = Vector3.Max(maximum, local);
        }

        Vector3 spread = maximum - minimum;
        Vector3 localNormal = spread.x <= spread.y && spread.x <= spread.z ? Vector3.right :
            spread.y <= spread.z ? Vector3.up : Vector3.forward;
        Vector3 normal = wallRoot.TransformDirection(localNormal).normalized;
        Camera camera = Camera.main;
        return camera != null && Vector3.Dot(normal, camera.transform.position - wallRoot.position) < 0f ? -normal : normal;
    }
}
