using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Opens the bed close-up after the player has climbed to the top of the
/// ladder and clicks a collider belonging to a named pile.
/// </summary>
public sealed class BedPileViewController : MonoBehaviour
{
    private const float TransitionDuration = 0.35f;
    private const float FramingPadding = 0.25f;
    private const float HangerDropRadiusPixels = 70f;

    private readonly List<Transform> piles = new List<Transform>();
    private readonly HashSet<Transform> placedPiles = new HashSet<Transform>();

    private LadderClimbEntranceController ladderClimb;
    private Transform bedHead;
    private Camera bedViewCamera;
    private Camera targetCamera;
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private bool previousOrthographic;
    private float previousFieldOfView;
    private float previousOrthographicSize;
    private bool isFocused;
    private bool isTransitioning;
    private Transform draggedPile;
    private Plane dragPlane;
    private Vector3 dragOffset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToLadder()
    {
        Transform ladder = FindSceneTransform("ladder");
        if (ladder != null && ladder.GetComponent<BedPileViewController>() == null)
        {
            ladder.gameObject.AddComponent<BedPileViewController>();
        }
    }

    private void Awake()
    {
        ladderClimb = GetComponent<LadderClimbEntranceController>();
        bedHead = FindSceneTransform("床头");
        bedViewCamera = FindSceneCamera("BedViewCamera");
        DisableTemplateCamera();
        CollectPiles();
        HideClothsAtStart();
    }

    private void Update()
    {
        if (ladderClimb == null)
        {
            ladderClimb = GetComponent<LadderClimbEntranceController>();
        }

        if (targetCamera == null && ladderClimb != null)
        {
            targetCamera = ladderClimb.TargetCamera;
        }

        if (bedViewCamera == null)
        {
            bedViewCamera = FindSceneCamera("BedViewCamera");
            DisableTemplateCamera();
        }

        if (targetCamera == null || isTransitioning)
        {
            return;
        }

        if (isFocused)
        {
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                draggedPile = null;
                RestorePreviousProjection();
                StartCoroutine(MoveCamera(previousCameraPosition, previousCameraRotation, false));
            }

            UpdatePileDrag();

            return;
        }

        if (ladderClimb == null || !ladderClimb.IsAtTop || !Input.GetMouseButtonDown(0) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        if (IsPointerOverPile())
        {
            EnterBedView();
        }
    }

    private bool IsPointerOverPile()
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            Transform hitTransform = hit.collider.transform;
            foreach (Transform pile in piles)
            {
                if (pile != null && (hitTransform == pile || hitTransform.IsChildOf(pile)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void UpdatePileDrag()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            float closestDistance = float.PositiveInfinity;
            Transform selectedPile = null;
            RaycastHit selectedHit = default;

            foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                Transform pile = FindPileRoot(hit.collider.transform);
                if (pile != null && hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    selectedPile = pile;
                    selectedHit = hit;
                }
            }

            if (selectedPile != null && !placedPiles.Contains(selectedPile))
            {
                // A plane perpendicular to the bed camera keeps the pile on
                // the visible bed-view plane instead of moving it in depth.
                dragPlane = new Plane(targetCamera.transform.forward, selectedHit.point);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    draggedPile = selectedPile;
                    dragOffset = draggedPile.position - ray.GetPoint(enter);
                }
            }
        }

        if (draggedPile == null)
        {
            return;
        }

        if (Input.GetMouseButtonUp(0))
        {
            TryPlaceOnMatchingHanger(draggedPile);
            draggedPile = null;
            return;
        }

        if (Input.GetMouseButton(0))
        {
            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            if (dragPlane.Raycast(ray, out float enter))
            {
                draggedPile.position = ray.GetPoint(enter) + dragOffset;
            }
        }
    }

    private Transform FindPileRoot(Transform candidate)
    {
        foreach (Transform pile in piles)
        {
            if (pile != null && (candidate == pile || candidate.IsChildOf(pile)))
            {
                return pile;
            }
        }

        return null;
    }

    private bool TryPlaceOnMatchingHanger(Transform pile)
    {
        string colour = GetColour(pile.name);
        if (colour == null)
        {
            return false;
        }

        Transform hanger = FindSceneTransform(colour + " hanger");
        Transform cloth = FindSceneTransform(colour + " cloth");
        if (hanger == null || cloth == null)
        {
            Debug.LogWarning($"[BedPileView] Missing {colour} hanger or cloth.", this);
            return false;
        }

        Vector2 pileScreenPosition = targetCamera.WorldToScreenPoint(GetVisualCenter(pile));
        Vector2 hangerScreenPosition = targetCamera.WorldToScreenPoint(GetVisualCenter(hanger));
        if (Vector2.Distance(pileScreenPosition, hangerScreenPosition) > HangerDropRadiusPixels)
        {
            return false;
        }

        // The hanger can sit at a different world depth from the draggable
        // pile. Project its screen position back onto the pile's fixed drag
        // plane so snapping looks correct without changing depth.
        Ray ray = targetCamera.ScreenPointToRay(hangerScreenPosition);
        if (dragPlane.Raycast(ray, out float enter))
        {
            pile.position = ray.GetPoint(enter);
        }

        placedPiles.Add(pile);
        cloth.gameObject.SetActive(true);
        pile.gameObject.SetActive(false);
        Debug.Log($"[BedPileView] {colour} pile placed correctly; {colour} cloth revealed.", this);
        return true;
    }

    private static string GetColour(string objectName)
    {
        if (objectName.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "red";
        }

        if (objectName.IndexOf("yellow", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "yellow";
        }

        if (objectName.IndexOf("blue", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "blue";
        }

        return null;
    }

    private static Vector3 GetVisualCenter(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return root.position;
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds.center;
    }

    private void EnterBedView()
    {
        previousCameraPosition = targetCamera.transform.position;
        previousCameraRotation = targetCamera.transform.rotation;
        previousOrthographic = targetCamera.orthographic;
        previousFieldOfView = targetCamera.fieldOfView;
        previousOrthographicSize = targetCamera.orthographicSize;

        // BedViewCamera is an editor-authored view marker.  Its transform and
        // projection are copied to Main Camera, rather than enabling a second
        // live camera, so the rest of the game keeps using one camera.
        if (bedViewCamera != null)
        {
            CopyProjection(bedViewCamera, targetCamera);
            ladderClimb.SetExternalViewActive(true);
            StartCoroutine(MoveCamera(bedViewCamera.transform.position, bedViewCamera.transform.rotation, true));
            Debug.Log("[BedPileView] Entered the authored BedViewCamera view.", this);
            return;
        }

        if (!TryGetBedViewBounds(out Bounds bounds, out Vector3 outwardNormal))
        {
            Debug.LogWarning("[BedPileView] No renderers were found for the bed view.", this);
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(-outwardNormal, Vector3.up);
        float distance = CalculatePerspectiveDistance(bounds, targetRotation);
        Vector3 targetPosition = bounds.center + outwardNormal * distance;

        StartCoroutine(MoveCamera(targetPosition, targetRotation, true));
        Debug.Log("[BedPileView] Entered bed view through pile click.", this);
    }

    private IEnumerator MoveCamera(Vector3 destinationPosition, Quaternion destinationRotation, bool entering)
    {
        isTransitioning = true;
        Vector3 startPosition = targetCamera.transform.position;
        Quaternion startRotation = targetCamera.transform.rotation;
        float elapsed = 0f;

        while (elapsed < TransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / TransitionDuration);
            targetCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, destinationPosition, progress),
                Quaternion.Slerp(startRotation, destinationRotation, progress));
            yield return null;
        }

        targetCamera.transform.SetPositionAndRotation(destinationPosition, destinationRotation);
        isFocused = entering;
        isTransitioning = false;

        if (!entering && ladderClimb != null)
        {
            ladderClimb.SetExternalViewActive(false);
        }
    }

    private void CollectPiles()
    {
        piles.Clear();
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate != null && candidate.gameObject.scene.IsValid() &&
                candidate.name.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                piles.Add(candidate);
            }
        }
    }

    private static void HideClothsAtStart()
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate != null && candidate.gameObject.scene.IsValid() &&
                candidate.name.IndexOf("cloth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidate.gameObject.SetActive(false);
            }
        }
    }

    private bool TryGetBedViewBounds(out Bounds bounds, out Vector3 outwardNormal)
    {
        bool hasRenderer = false;
        bounds = default;

        // The headboard establishes the view plane; piles are included only so
        // the player can still see the objects they clicked.
        EncapsulateRenderers(bedHead, ref bounds, ref hasRenderer);

        foreach (Transform pile in piles)
        {
            EncapsulateRenderers(pile, ref bounds, ref hasRenderer);
        }

        if (!hasRenderer)
        {
            outwardNormal = Vector3.zero;
            return false;
        }

        // A bedhead is thin in one horizontal world axis.  That thin axis is
        // its front/back direction even when the imported mesh has unusual
        // local rotation.  Pick the side nearest the current camera.
        outwardNormal = bounds.extents.x <= bounds.extents.z ? Vector3.right : Vector3.forward;
        Vector3 cameraSide = Vector3.ProjectOnPlane(previousCameraPosition - bounds.center, Vector3.up);
        if (Vector3.Dot(outwardNormal, cameraSide) < 0f)
        {
            outwardNormal = -outwardNormal;
        }

        return true;
    }

    private static void EncapsulateRenderers(Transform root, ref Bounds bounds, ref bool hasRenderer)
    {
        if (root == null)
        {
            return;
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!hasRenderer)
            {
                bounds = renderer.bounds;
                hasRenderer = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
    }

    private float CalculatePerspectiveDistance(Bounds bounds, Quaternion viewRotation)
    {
        float halfWidth = ProjectExtents(bounds.extents, viewRotation * Vector3.right);
        float halfHeight = ProjectExtents(bounds.extents, viewRotation * Vector3.up);
        float halfDepth = ProjectExtents(bounds.extents, viewRotation * Vector3.forward);
        float verticalHalfFov = targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * targetCamera.aspect);
        float verticalDistance = halfHeight / Mathf.Max(0.001f, Mathf.Tan(verticalHalfFov));
        float horizontalDistance = halfWidth / Mathf.Max(0.001f, Mathf.Tan(horizontalHalfFov));
        return Mathf.Max(0.5f, verticalDistance, horizontalDistance) + halfDepth + FramingPadding;
    }

    private static float ProjectExtents(Vector3 extents, Vector3 axis)
    {
        axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        return Vector3.Dot(extents, axis);
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

    private static Camera FindSceneCamera(string objectName)
    {
        Transform transform = FindSceneTransform(objectName);
        return transform != null ? transform.GetComponent<Camera>() : null;
    }

    private static void CopyProjection(Camera source, Camera destination)
    {
        destination.orthographic = source.orthographic;
        destination.fieldOfView = source.fieldOfView;
        destination.orthographicSize = source.orthographicSize;
    }

    private void RestorePreviousProjection()
    {
        targetCamera.orthographic = previousOrthographic;
        targetCamera.fieldOfView = previousFieldOfView;
        targetCamera.orthographicSize = previousOrthographicSize;
    }

    private void DisableTemplateCamera()
    {
        if (bedViewCamera != null && bedViewCamera != targetCamera)
        {
            bedViewCamera.enabled = false;
        }
    }
}
