using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Randomly assigns Book1..Book9 to Slot01..Slot09 when a game starts.
/// The numbered slots represent the solved order from tallest to shortest.
/// </summary>
public sealed class BookPuzzleShuffleController : MonoBehaviour
{
    [SerializeField] private Transform booksRoot;
    [SerializeField] private Transform slotsRoot;
    [SerializeField] private bool shuffleOnStart = true;
    [SerializeField] private bool avoidSolvedStart = true;
    [SerializeField, Min(0f)] private float minimumBookSpacing = 0.005f;

    private readonly List<Transform> books = new List<Transform>();
    private readonly List<Transform> slots = new List<Transform>();
    private readonly List<float> bookWidths = new List<float>();
    private float layoutLeftEdge;
    private float layoutRightEdge;
    private float effectiveSpacing;
    private Vector3 shelfAxis;
    private bool isInitialized;

    private void Start()
    {
        if (!InitializePuzzleData())
        {
            return;
        }

        if (shuffleOnStart)
        {
            ShuffleBooks();
        }
    }

    public void ShuffleBooks()
    {
        if (!EnsureInitialized())
        {
            return;
        }

        List<int> permutation = new List<int>(books.Count);
        for (int index = 0; index < books.Count; index++)
        {
            permutation.Add(index);
        }

        for (int index = permutation.Count - 1; index > 0; index--)
        {
            int swapIndex = Random.Range(0, index + 1);
            int temporary = permutation[index];
            permutation[index] = permutation[swapIndex];
            permutation[swapIndex] = temporary;
        }

        if (avoidSolvedStart && IsSolvedPermutation(permutation) && permutation.Count > 1)
        {
            int first = permutation[0];
            permutation[0] = permutation[1];
            permutation[1] = first;
        }

        LayoutBooks(permutation);
    }

    public void PlaceBooksInCorrectOrder()
    {
        if (!EnsureInitialized())
        {
            return;
        }

        List<int> solvedOrder = new List<int>(books.Count);
        for (int index = 0; index < books.Count; index++)
        {
            solvedOrder.Add(index);
        }

        LayoutBooks(solvedOrder);
    }

    private bool InitializePuzzleData()
    {
        if (!CollectAndValidateObjects())
        {
            return false;
        }

        bookWidths.Clear();
        shelfAxis = ResolveShelfAxis();
        layoutLeftEdge = float.PositiveInfinity;
        layoutRightEdge = float.NegativeInfinity;
        float totalBookWidth = 0f;

        for (int index = 0; index < books.Count; index++)
        {
            if (!TryGetProjectedBounds(books[index], shelfAxis, out float left, out float right))
            {
                Debug.LogError("Book" + (index + 1) + " has no Renderer and cannot be shuffled.", books[index]);
                return false;
            }

            float width = right - left;
            bookWidths.Add(width);
            totalBookWidth += width;
            layoutLeftEdge = Mathf.Min(layoutLeftEdge, left);
            layoutRightEdge = Mathf.Max(layoutRightEdge, right);
        }

        float originalSpan = layoutRightEdge - layoutLeftEdge;
        float originalAverageSpacing = books.Count > 1
            ? (originalSpan - totalBookWidth) / (books.Count - 1)
            : 0f;
        // The authored book span is the shelf's usable width. Prefer its original
        // gap, but never expand the layout beyond those two shelf edges.
        float maximumSpacing = books.Count > 1
            ? Mathf.Max(0f, (originalSpan - totalBookWidth) / (books.Count - 1))
            : 0f;
        effectiveSpacing = Mathf.Min(Mathf.Max(0f, Mathf.Max(minimumBookSpacing, originalAverageSpacing)), maximumSpacing);

        UpdateSolvedSlotPositions();

        isInitialized = true;
        return true;
    }

    private bool EnsureInitialized()
    {
        return isInitialized || InitializePuzzleData();
    }

    private void LayoutBooks(IReadOnlyList<int> orderedBookIndices)
    {
        float cursor = layoutLeftEdge;

        for (int orderIndex = 0; orderIndex < orderedBookIndices.Count; orderIndex++)
        {
            int bookIndex = orderedBookIndices[orderIndex];
            float targetCenter = cursor + bookWidths[bookIndex] * 0.5f;
            MoveBookCenterToCoordinate(books[bookIndex], targetCenter);
            cursor += bookWidths[bookIndex] + effectiveSpacing;
        }
    }

    private void UpdateSolvedSlotPositions()
    {
        float cursor = layoutLeftEdge;

        for (int index = 0; index < slots.Count; index++)
        {
            float targetCenter = cursor + bookWidths[index] * 0.5f;
            Vector3 slotPosition = slots[index].position;
            float currentCenter = Vector3.Dot(slotPosition, shelfAxis);
            slotPosition += shelfAxis * (targetCenter - currentCenter);
            slots[index].position = slotPosition;
            cursor += bookWidths[index] + effectiveSpacing;
        }
    }

    private void MoveBookCenterToCoordinate(Transform book, float targetCenter)
    {
        if (!TryGetProjectedBounds(book, shelfAxis, out float minimum, out float maximum))
        {
            return;
        }

        float currentCenter = (minimum + maximum) * 0.5f;
        book.position += shelfAxis * (targetCenter - currentCenter);
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

    private bool CollectAndValidateObjects()
    {
        books.Clear();
        slots.Clear();

        if (booksRoot == null || slotsRoot == null)
        {
            Debug.LogError("Book puzzle requires both Books Root and Slots Root references.", this);
            return false;
        }

        for (int index = 1; index <= 9; index++)
        {
            Transform book = booksRoot.Find("Book" + index);
            Transform slot = slotsRoot.Find("Slot" + index.ToString("00"));

            if (book == null || slot == null)
            {
                Debug.LogError("Book puzzle is missing Book" + index + " or Slot" + index.ToString("00") + ".", this);
                return false;
            }

            books.Add(book);
            slots.Add(slot);
        }

        return true;
    }

    private Vector3 ResolveShelfAxis()
    {
        // Measure the row in Book LOCAL space so moving or rotating the whole
        // shelf cannot change which local axis owns the layout.
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        foreach (Transform book in books)
        {
            Bounds bounds = GetCombinedBounds(book);
            Vector3 localCenter = booksRoot.InverseTransformPoint(bounds.center);
            minX = Mathf.Min(minX, localCenter.x);
            maxX = Mathf.Max(maxX, localCenter.x);
            minZ = Mathf.Min(minZ, localCenter.z);
            maxZ = Mathf.Max(maxZ, localCenter.z);
        }

        Vector3 localShelfAxis = maxX - minX >= maxZ - minZ ? Vector3.right : Vector3.forward;
        Vector3 worldShelfAxis = booksRoot.TransformDirection(localShelfAxis).normalized;

        // Before shuffling, Book1..Book9 are the authored solved order. Use
        // that order to give the otherwise unsigned measured axis a stable
        // direction, then write Slot01..Slot09 along the same direction.
        if (books.Count >= 2)
        {
            Vector3 firstCenter = GetCombinedBounds(books[0]).center;
            Vector3 lastCenter = GetCombinedBounds(books[books.Count - 1]).center;
            if (Vector3.Dot(lastCenter - firstCenter, worldShelfAxis) < 0f)
            {
                worldShelfAxis = -worldShelfAxis;
            }
        }

        return worldShelfAxis;
    }

    private static Bounds GetCombinedBounds(Transform book)
    {
        Renderer[] renderers = book.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(book.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static bool IsSolvedPermutation(IReadOnlyList<int> permutation)
    {
        for (int index = 0; index < permutation.Count; index++)
        {
            if (permutation[index] != index)
            {
                return false;
            }
        }

        return true;
    }
}
