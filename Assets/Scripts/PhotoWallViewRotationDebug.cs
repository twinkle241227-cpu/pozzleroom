using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Read-only diagnostics for unexpected photo rotation while entering the
/// photo-wall view. It records the click, camera/wall pose, and every photo
/// rotation change without modifying gameplay state.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class PhotoWallViewRotationDebug : MonoBehaviour
{
    private const float RotationEpsilon = 0.01f;

    [SerializeField] private bool logEntranceClicks = true;
    [SerializeField] private bool logPhotoRotationChanges = true;

    private readonly List<Transform> photos = new List<Transform>();
    private readonly List<Transform> pins = new List<Transform>();
    private readonly Dictionary<Transform, Quaternion> previousLocalRotations =
        new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, Quaternion> previousWorldRotations =
        new Dictionary<Transform, Quaternion>();

    private PhotoWallViewController viewController;
    private PhotoWallShuffleController shuffleController;
    private Camera targetCamera;
    private bool previousInteractionActive;
    private bool previousFocused;
    private bool previousTransitioning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPhotoWall()
    {
        PhotoWallViewController controller = FindObjectOfType<PhotoWallViewController>();
        if (controller != null && controller.GetComponent<PhotoWallViewRotationDebug>() == null)
        {
            controller.gameObject.AddComponent<PhotoWallViewRotationDebug>();
        }
    }

    private void Awake()
    {
        viewController = GetComponent<PhotoWallViewController>();
        if (viewController == null)
        {
            viewController = FindObjectOfType<PhotoWallViewController>();
        }

        shuffleController = FindObjectOfType<PhotoWallShuffleController>();
        targetCamera = Camera.main;
        RefreshPhotoList();
        previousInteractionActive = PhotoWallViewController.IsPhotoWallInteractionActive;
        previousFocused = viewController != null && viewController.IsFocused;
        previousTransitioning = viewController != null && viewController.IsTransitioning;

        Debug.Log($"[PhotoViewRotationDebug] Tracking {photos.Count} photos. " +
                  "Logs are read-only; no photo transform will be changed.", this);
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (logEntranceClicks && Input.GetMouseButtonDown(0) &&
            !PhotoWallViewController.IsPhotoWallInteractionActive)
        {
            LogClickReport();
        }
    }

    private void LateUpdate()
    {
        bool active = PhotoWallViewController.IsPhotoWallInteractionActive;
        bool focused = viewController != null && viewController.IsFocused;
        bool transitioning = viewController != null && viewController.IsTransitioning;

        if (active != previousInteractionActive || focused != previousFocused ||
            transitioning != previousTransitioning)
        {
            Debug.Log(BuildViewStateReport(active, focused, transitioning), this);
        }

        RefreshPhotoListIfNeeded();
        for (int index = 0; index < photos.Count; index++)
        {
            Transform photo = photos[index];
            if (photo == null)
            {
                continue;
            }

            if (!previousLocalRotations.TryGetValue(photo, out Quaternion previousLocal) ||
                !previousWorldRotations.TryGetValue(photo, out Quaternion previousWorld))
            {
                RecordPhoto(photo);
                continue;
            }

            float changeAngle = Quaternion.Angle(previousWorld, photo.rotation);
            if (logPhotoRotationChanges && changeAngle > RotationEpsilon)
            {
                Debug.Log(BuildPhotoRotationReport(
                    photo,
                    previousLocal,
                    previousWorld,
                    active,
                    focused,
                    transitioning), photo);
            }

            previousLocalRotations[photo] = photo.localRotation;
            previousWorldRotations[photo] = photo.rotation;
        }

        previousInteractionActive = active;
        previousFocused = focused;
        previousTransitioning = transitioning;
    }

    private void RefreshPhotoList()
    {
        photos.Clear();
        pins.Clear();

        if (shuffleController != null)
        {
            shuffleController.CopyPhotoWallObjects(photos, pins);
        }

        previousLocalRotations.Clear();
        previousWorldRotations.Clear();
        for (int index = 0; index < photos.Count; index++)
        {
            if (photos[index] != null)
            {
                RecordPhoto(photos[index]);
            }
        }
    }

    private void RefreshPhotoListIfNeeded()
    {
        if (shuffleController == null)
        {
            shuffleController = FindObjectOfType<PhotoWallShuffleController>();
        }

        if (shuffleController == null)
        {
            return;
        }

        List<Transform> currentPhotos = new List<Transform>();
        List<Transform> currentPins = new List<Transform>();
        shuffleController.CopyPhotoWallObjects(currentPhotos, currentPins);
        if (currentPhotos.Count == photos.Count)
        {
            return;
        }

        RefreshPhotoList();
        Debug.Log($"[PhotoViewRotationDebug] Refreshed tracked photo list: {photos.Count} photos.", this);
    }

    private void RecordPhoto(Transform photo)
    {
        previousLocalRotations[photo] = photo.localRotation;
        previousWorldRotations[photo] = photo.rotation;
    }

    private void LogClickReport()
    {
        if (targetCamera == null)
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            Mathf.Infinity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        StringBuilder report = new StringBuilder("[PhotoViewRotationDebug][CLICK] ");
        report.Append($"screen={Input.mousePosition}; ");
        report.Append($"cameraPos={targetCamera.transform.position:F4}; ");
        report.Append($"cameraEuler={targetCamera.transform.eulerAngles:F2}; ");
        report.Append($"rayOrigin={ray.origin:F4}; rayDirection={ray.direction:F4}; ");

        int reportedHits = 0;
        for (int index = 0; index < hits.Length && reportedHits < 8; index++)
        {
            Transform hitTransform = hits[index].collider.transform;
            report.Append($"hit[{reportedHits}]='{GetPath(hitTransform)}', point={hits[index].point:F4}, " +
                          $"normal={hits[index].normal:F4}, distance={hits[index].distance:F4}; ");
            reportedHits++;
        }

        Debug.Log(report.ToString(), this);
    }

    private string BuildViewStateReport(bool active, bool focused, bool transitioning)
    {
        Transform wall = shuffleController != null ? shuffleController.transform : transform;
        return "[PhotoViewRotationDebug][VIEW] " +
               $"active={active}; focused={focused}; transitioning={transitioning}; " +
               $"cameraPos={(targetCamera != null ? targetCamera.transform.position : Vector3.zero):F4}; " +
               $"cameraEuler={(targetCamera != null ? targetCamera.transform.eulerAngles : Vector3.zero):F2}; " +
               $"wallPath='{GetPath(wall)}'; wallPos={wall.position:F4}; " +
               $"wallLocalEuler={wall.localEulerAngles:F2}; wallWorldEuler={wall.eulerAngles:F2}; " +
               $"wallAxesWorld: X={wall.right:F3}, Y={wall.up:F3}, Z={wall.forward:F3}";
    }

    private string BuildPhotoRotationReport(
        Transform photo,
        Quaternion previousLocal,
        Quaternion previousWorld,
        bool active,
        bool focused,
        bool transitioning)
    {
        Quaternion localDelta = Quaternion.Inverse(previousLocal) * photo.localRotation;
        localDelta.ToAngleAxis(out float localAngle, out Vector3 localAxis);
        NormalizeAngleAndAxis(ref localAngle, ref localAxis);

        Quaternion worldDelta = photo.rotation * Quaternion.Inverse(previousWorld);
        worldDelta.ToAngleAxis(out float worldAngle, out Vector3 worldAxis);
        NormalizeAngleAndAxis(ref worldAngle, ref worldAxis);

        Vector3 previousLocalEuler = previousLocal.eulerAngles;
        Vector3 previousWorldEuler = previousWorld.eulerAngles;
        string phase = transitioning ? "camera-transition" : focused ? "focused" : active ? "entry-start" : "room-view";

        return "[PhotoViewRotationDebug][PHOTO_ROTATION] " +
               $"phase={phase}; photo='{GetPath(photo)}'; " +
               $"localEulerBefore={previousLocalEuler:F2}; localEulerAfter={photo.localEulerAngles:F2}; " +
               $"worldEulerBefore={previousWorldEuler:F2}; worldEulerAfter={photo.eulerAngles:F2}; " +
               $"deltaLocal: angle={localAngle:F2}, axis={localAxis:F3}; " +
               $"deltaWorld: angle={worldAngle:F2}, axis={worldAxis:F3}; " +
               $"parent='{GetPath(photo.parent)}'; " +
               $"parentWorldEuler={(photo.parent != null ? photo.parent.eulerAngles : Vector3.zero):F2}; " +
               $"cameraEuler={(targetCamera != null ? targetCamera.transform.eulerAngles : Vector3.zero):F2}";
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
}
