using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Provides an insert-and-reorder book interaction inspired by A Little to the Left.
/// Books move only along the shelf axis, while neighbouring books make room dynamically.
/// </summary>
public sealed class BookPuzzleInteractionController : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform booksRoot;
    [SerializeField] private BookPuzzleViewController viewController;

    [Header("Drag Feel")]
    [SerializeField, Min(0f)] private float dragDepthOffset = 0.03f;
    [SerializeField, Min(0.001f)] private float neighbourMoveDuration = 0.12f;
    [SerializeField, Min(0f)] private float snapDuration = 0.15f;
    [SerializeField, Min(0f)] private float insertionHysteresis = 0.01f;
    [SerializeField, Min(0f)] private float selectionPaddingPixels = 20f;
    [SerializeField] private LayerMask selectableLayers = ~0;

    [Header("Layout")]
    [SerializeField, Min(0f)] private float minimumBookSpacing = 0.005f;

    [Header("Completion")]
    [SerializeField] private bool lockBooksWhenSolved = true;
    [SerializeField] private UnityEvent onPuzzleSolved;

    private readonly List<Transform> numberedBooks = new List<Transform>();
    private readonly List<Transform> currentOrder = new List<Transform>();
    private readonly List<Transform> orderBeforeDrag = new List<Transform>();
    private readonly List<Transform> remainingBooks = new List<Transform>();
    private readonly Dictionary<Transform, float> previewTargets = new Dictionary<Transform, float>();

    private Transform draggedBook;
    private Plane dragPlane;
    private Vector3 shelfAxis;
    private float pointerToBookOffset;
    private float layoutLeftEdge;
    private float effectiveSpacing;
    private int insertionIndex;
    private bool isSettling;
    private bool isSolved;

    public bool IsBusy => draggedBook != null || isSettling;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        CollectBooks();
    }

    private void Update()
    {
        if (targetCamera == null || booksRoot == null || viewController == null || isSolved || isSettling)
        {
            return;
        }

        if (draggedBook == null)
        {
            if (viewController.IsFocused && !viewController.IsTransitioning && Input.GetMouseButton(0))
            {
                if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                {
                    TryBeginDrag(Input.mousePosition);
                }
            }

            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            FinishDrag(true);
            return;
        }

        UpdateDraggedBook(Input.mousePosition);
        UpdateNeighbourPreview();

        if (Input.GetMouseButtonUp(0))
        {
            FinishDrag(false);
        }
    }

    private void TryBeginDrag(Vector3 pointerPosition)
    {
        Transform selectedBook = FindBookUnderPointer(pointerPosition);
        if (selectedBook == null)
        {
            return;
        }

        RefreshCurrentOrder();
        if (!TryGetProjectedBounds(selectedBook, GetShelfAxis(), out float minimum, out float maximum))
        {
            return;
        }

        draggedBook = selectedBook;
        orderBeforeDrag.Clear();
        orderBeforeDrag.AddRange(currentOrder);
        remainingBooks.Clear();
        remainingBooks.AddRange(currentOrder);
        insertionIndex = remainingBooks.IndexOf(draggedBook);
        remainingBooks.Remove(draggedBook);

        CalculateLayoutMetrics(currentOrder);
        dragPlane = new Plane(targetCamera.transform.forward, GetBookBounds(draggedBook).center);
        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        if (dragPlane.Raycast(ray, out float enter))
        {
            float pointerCoordinate = Vector3.Dot(ray.GetPoint(enter), shelfAxis);
            float bookCenter = (minimum + maximum) * 0.5f;
            pointerToBookOffset = bookCenter - pointerCoordinate;
        }
        else
        {
            pointerToBookOffset = 0f;
        }

        draggedBook.position -= targetCamera.transform.forward * dragDepthOffset;
        BuildPreviewTargets();
    }

    private void UpdateDraggedBook(Vector3 pointerPosition)
    {
        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        if (!dragPlane.Raycast(ray, out float enter))
        {
            return;
        }

        float targetCenter = Vector3.Dot(ray.GetPoint(enter), shelfAxis) + pointerToBookOffset;
        MoveBookCenterToCoordinate(draggedBook, targetCenter, 1f);
        UpdateInsertionIndex(targetCenter);
    }

    private void UpdateInsertionIndex(float draggedCenter)
    {
        int previousIndex = insertionIndex;

        while (insertionIndex < remainingBooks.Count &&
               draggedCenter > GetBookCenterCoordinate(remainingBooks[insertionIndex]) + insertionHysteresis)
        {
            insertionIndex++;
        }

        while (insertionIndex > 0 &&
               draggedCenter < GetBookCenterCoordinate(remainingBooks[insertionIndex - 1]) - insertionHysteresis)
        {
            insertionIndex--;
        }

        if (insertionIndex != previousIndex)
        {
            BuildPreviewTargets();
        }
    }

    private void BuildPreviewTargets()
    {
        previewTargets.Clear();
        float cursor = layoutLeftEdge;
        float draggedWidth = GetBookWidth(draggedBook);

        for (int position = 0; position <= remainingBooks.Count; position++)
        {
            if (position == insertionIndex)
            {
                cursor += draggedWidth + effectiveSpacing;
            }

            if (position >= remainingBooks.Count)
            {
                continue;
            }

            Transform book = remainingBooks[position];
            float width = GetBookWidth(book);
            previewTargets[book] = cursor + width * 0.5f;
            cursor += width + effectiveSpacing;
        }
    }

    private void UpdateNeighbourPreview()
    {
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / neighbourMoveDuration);
        foreach (KeyValuePair<Transform, float> entry in previewTargets)
        {
            MoveBookCenterToCoordinate(entry.Key, entry.Value, blend);
        }
    }

    private void FinishDrag(bool cancel)
    {
        List<Transform> finalOrder = new List<Transform>();
        if (cancel)
        {
            finalOrder.AddRange(orderBeforeDrag);
        }
        else
        {
            finalOrder.AddRange(remainingBooks);
            finalOrder.Insert(Mathf.Clamp(insertionIndex, 0, finalOrder.Count), draggedBook);
        }

        Transform releasedBook = draggedBook;
        draggedBook = null;
        StartCoroutine(SnapToOrder(finalOrder, releasedBook));
    }

    private IEnumerator SnapToOrder(List<Transform> finalOrder, Transform releasedBook)
    {
        isSettling = true;
        Dictionary<Transform, Vector3> starts = new Dictionary<Transform, Vector3>();
        Dictionary<Transform, Vector3> destinations = BuildDestinationPositions(finalOrder);

        if (releasedBook != null)
        {
            destinations[releasedBook] += targetCamera.transform.forward * dragDepthOffset;
        }

        foreach (Transform book in finalOrder)
        {
            starts[book] = book.position;
        }

        if (snapDuration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < snapDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / snapDuration));
                foreach (Transform book in finalOrder)
                {
                    book.position = Vector3.Lerp(starts[book], destinations[book], t);
                }

                yield return null;
            }
        }

        foreach (Transform book in finalOrder)
        {
            book.position = destinations[book];
        }

        currentOrder.Clear();
        currentOrder.AddRange(finalOrder);
        isSettling = false;

        if (IsCorrectOrder(currentOrder))
        {
            isSolved = lockBooksWhenSolved;
            onPuzzleSolved?.Invoke();
        }
    }

    private Dictionary<Transform, Vector3> BuildDestinationPositions(IReadOnlyList<Transform> order)
    {
        Dictionary<Transform, Vector3> result = new Dictionary<Transform, Vector3>();
        float cursor = layoutLeftEdge;

        foreach (Transform book in order)
        {
            float width = GetBookWidth(book);
            float targetCenter = cursor + width * 0.5f;
            float currentCenter = GetBookCenterCoordinate(book);
            result[book] = book.position + shelfAxis * (targetCenter - currentCenter);
            cursor += width + effectiveSpacing;
        }

        return result;
    }

    private void RefreshCurrentOrder()
    {
        currentOrder.Clear();
        currentOrder.AddRange(numberedBooks);
        shelfAxis = GetShelfAxis();
        currentOrder.Sort((left, right) => GetBookCenterCoordinate(left).CompareTo(GetBookCenterCoordinate(right)));
    }

    private void CalculateLayoutMetrics(IReadOnlyList<Transform> order)
    {
        layoutLeftEdge = float.PositiveInfinity;
        float rightEdge = float.NegativeInfinity;
        float totalWidth = 0f;

        foreach (Transform book in order)
        {
            TryGetProjectedBounds(book, shelfAxis, out float minimum, out float maximum);
            layoutLeftEdge = Mathf.Min(layoutLeftEdge, minimum);
            rightEdge = Mathf.Max(rightEdge, maximum);
            totalWidth += maximum - minimum;
        }

        float averageSpacing = order.Count > 1
            ? (rightEdge - layoutLeftEdge - totalWidth) / (order.Count - 1)
            : 0f;
        effectiveSpacing = Mathf.Max(minimumBookSpacing, averageSpacing);
    }

    private Transform FindBookUnderPointer(Vector3 pointerPosition)
    {
        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, selectableLayers, QueryTriggerInteraction.Collide))
        {
            Transform candidate = hit.transform;
            while (candidate != null && candidate.parent != booksRoot)
            {
                candidate = candidate.parent;
            }

            if (candidate != null && candidate.parent == booksRoot && numberedBooks.Contains(candidate))
            {
                return candidate;
            }
        }

        Transform bestBook = null;
        float bestDistance = float.PositiveInfinity;

        foreach (Transform book in numberedBooks)
        {
            Bounds bounds = GetBookBounds(book);
            if (!TryGetScreenRect(bounds, out Rect rect))
            {
                continue;
            }

            rect.xMin -= selectionPaddingPixels;
            rect.xMax += selectionPaddingPixels;
            rect.yMin -= selectionPaddingPixels;
            rect.yMax += selectionPaddingPixels;
            if (!rect.Contains(pointerPosition))
            {
                continue;
            }

            Vector3 screenCenter = targetCamera.WorldToScreenPoint(bounds.center);
            float distance = ((Vector2)screenCenter - (Vector2)pointerPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestBook = book;
            }
        }

        return bestBook;
    }

    private bool TryGetScreenRect(Bounds bounds, out Rect rect)
    {
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        bool hasPoint = false;

        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            Vector3 screenPoint = targetCamera.WorldToScreenPoint(corner);
            if (screenPoint.z <= 0f)
            {
                continue;
            }

            hasPoint = true;
            minimum = Vector2.Min(minimum, screenPoint);
            maximum = Vector2.Max(maximum, screenPoint);
        }

        rect = hasPoint ? Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y) : default;
        return hasPoint;
    }

    private void CollectBooks()
    {
        numberedBooks.Clear();
        if (booksRoot == null)
        {
            return;
        }

        for (int index = 1; index <= 9; index++)
        {
            Transform book = booksRoot.Find("Book" + index);
            if (book != null)
            {
                numberedBooks.Add(book);
            }
        }
    }

    private Vector3 GetShelfAxis()
    {
        return booksRoot.right.normalized;
    }

    private float GetBookCenterCoordinate(Transform book)
    {
        return Vector3.Dot(GetBookBounds(book).center, shelfAxis);
    }

    private float GetBookWidth(Transform book)
    {
        TryGetProjectedBounds(book, shelfAxis, out float minimum, out float maximum);
        return maximum - minimum;
    }

    private void MoveBookCenterToCoordinate(Transform book, float targetCoordinate, float blend)
    {
        float currentCoordinate = GetBookCenterCoordinate(book);
        book.position += shelfAxis * ((targetCoordinate - currentCoordinate) * blend);
    }

    private static Bounds GetBookBounds(Transform book)
    {
        Renderer[] renderers = book.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static bool TryGetProjectedBounds(Transform book, Vector3 axis, out float minimum, out float maximum)
    {
        Renderer[] renderers = book.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            minimum = maximum = 0f;
            return false;
        }

        minimum = float.PositiveInfinity;
        maximum = float.NegativeInfinity;
        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                float projection = Vector3.Dot(corner, axis);
                minimum = Mathf.Min(minimum, projection);
                maximum = Mathf.Max(maximum, projection);
            }
        }

        return true;
    }

    private bool IsCorrectOrder(IReadOnlyList<Transform> order)
    {
        if (order.Count != numberedBooks.Count)
        {
            return false;
        }

        for (int index = 0; index < order.Count; index++)
        {
            if (order[index] != numberedBooks[index])
            {
                return false;
            }
        }

        return true;
    }
}
