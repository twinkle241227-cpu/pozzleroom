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

    private bool isDragging;
    private Vector3 previousMousePosition;

    private void Awake()
    {
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

        Bounds combinedBounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            combinedBounds.Encapsulate(renderers[index].bounds);
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
}
