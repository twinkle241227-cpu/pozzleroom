using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Temporary diagnostics for photo hover and click-ray behaviour.</summary>
public sealed class PhotoWallHoverDebug : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask raycastLayers = ~0;
    [SerializeField] private PhotoWallViewController viewController;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
        if (viewController == null)
        {
            viewController = GetComponentInChildren<PhotoWallViewController>(true);
        }
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            return;
        }

        if (viewController != null && !viewController.IsFocused)
        {
            PhotoHoverOutline.SetHighlighted(null);
            PhotoHoverOutline.SetGameplayOutlineVisibility(false);
            return;
        }

        PhotoHoverOutline.SetGameplayOutlineVisibility(true);

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, raycastLayers, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        PhotoHoverOutline selectedPhoto = PhotoWallPuzzleController.ActiveInstance != null &&
            PhotoWallPuzzleController.ActiveInstance.IsDragging
            ? null
            : GetSingleHoverTarget(hits);
        PhotoHoverOutline.SetHighlighted(selectedPhoto);

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        StringBuilder hitReport = new StringBuilder();
        for (int index = 0; index < hits.Length; index++)
        {
            RaycastHit hit = hits[index];
            if (index > 0) hitReport.Append(" | ");
            PhotoHoverOutline photo = hit.collider.GetComponentInParent<PhotoHoverOutline>();
            hitReport.Append($"#{index} {GetPath(hit.collider.transform)} distance={hit.distance:F4} point={hit.point}");
            if (photo != null) hitReport.Append($" photo={photo.GetDebugDescription()}");
        }

        Debug.Log(
            $"[PhotoHoverDebug] Click screen={Input.mousePosition}; ray origin={ray.origin}; direction={ray.direction}; " +
            $"highlighted={PhotoHoverOutline.GetHighlightedDebugDescriptions()}; hits={hitReport}");
    }

    private static PhotoHoverOutline GetSingleHoverTarget(RaycastHit[] hits)
    {
        PhotoHoverOutline selected = null;
        float nearestPhotoDistance = float.PositiveInfinity;
        const float DistanceTieTolerance = 0.0001f;

        foreach (RaycastHit hit in hits)
        {
            PhotoHoverOutline candidate = hit.collider.GetComponentInParent<PhotoHoverOutline>();
            if (candidate == null || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            if (hit.distance < nearestPhotoDistance - DistanceTieTolerance)
            {
                selected = candidate;
                nearestPhotoDistance = hit.distance;
                continue;
            }

            if (Mathf.Abs(hit.distance - nearestPhotoDistance) <= DistanceTieTolerance &&
                (selected == null || candidate.GetStableHoverPriority() > selected.GetStableHoverPriority()))
            {
                selected = candidate;
            }
        }

        return selected;
    }

    private static string GetPath(Transform target)
    {
        List<string> parts = new List<string>();
        while (target != null)
        {
            parts.Add(target.name);
            target = target.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }
}
