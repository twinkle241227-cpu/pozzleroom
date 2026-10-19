using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Rotates this pivot around the world Y axis while the left mouse button is dragged.
/// Place the room and any room decorations beneath this object.
/// </summary>
public sealed class RoomPivotDragController : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float degreesPerPixel = 0.2f;
    [SerializeField] private bool ignorePointerOverUi = true;
    [SerializeField] private bool centerPivotFromChildRenderers = true;
    [Header("Pivot Bounds")]
    [Tooltip("Renderers below these roots are ignored when the room pivot is automatically centred. They still rotate with NewPivot.")]
    [SerializeField] private Transform[] excludedFromPivotBounds;
    [Tooltip("When enabled, a child named CorridorRoot is ignored even if it has not been added to the list above yet.")]
    [SerializeField] private bool automaticallyExcludeCorridorRoot = true;

    private bool isDragging;
    private Vector3 previousMousePosition;
    private Transform automaticCorridorRoot;

    private void Awake()
    {
        if (automaticallyExcludeCorridorRoot)
        {
            automaticCorridorRoot = FindDescendantByName(transform, "CorridorRoot");
        }

        if (centerPivotFromChildRenderers)
        {
            CenterPivotWithoutMovingRoom();
        }
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (ignorePointerOverUi && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            isDragging = true;
            previousMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
        }

        if (!isDragging || !Input.GetMouseButton(0))
        {
            return;
        }

        Vector3 currentMousePosition = Input.mousePosition;
        float horizontalDelta = currentMousePosition.x - previousMousePosition.x;
        transform.Rotate(Vector3.up, -horizontalDelta * degreesPerPixel, Space.World);
        previousMousePosition = currentMousePosition;
    }

    private void CenterPivotWithoutMovingRoom()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return;
        }

        bool hasBounds = false;
        Bounds combinedBounds = default;
        foreach (Renderer renderer in renderers)
        {
            if (IsExcludedFromPivotBounds(renderer.transform))
            {
                continue;
            }

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

        if (!hasBounds)
        {
            return;
        }

        Vector3 offset = combinedBounds.center - transform.position;
        if (offset.sqrMagnitude < 0.000001f)
        {
            return;
        }

        transform.position += offset;
        foreach (Transform child in transform)
        {
            child.position -= offset;
        }
    }

    private bool IsExcludedFromPivotBounds(Transform candidate)
    {
        if (excludedFromPivotBounds != null)
        {
            foreach (Transform excludedRoot in excludedFromPivotBounds)
            {
                if (IsBelowRoot(candidate, excludedRoot))
                {
                    return true;
                }
            }
        }

        if (automaticallyExcludeCorridorRoot)
        {
            if (IsBelowRoot(candidate, automaticCorridorRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBelowRoot(Transform candidate, Transform root)
    {
        return root != null && (candidate == root || candidate.IsChildOf(root));
    }

    private static Transform FindDescendantByName(Transform root, string objectName)
    {
        foreach (Transform descendant in root.GetComponentsInChildren<Transform>(true))
        {
            if (descendant.name == objectName)
            {
                return descendant;
            }
        }

        return null;
    }
}
