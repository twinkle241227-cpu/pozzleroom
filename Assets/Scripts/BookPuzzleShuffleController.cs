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
    private float effectiveSpacing;
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
        layoutLeftEdge = float.PositiveInfinity;
        float originalRightEdge = float.NegativeInfinity;
        float totalBookWidth = 0f;

        for (int index = 0; index < books.Count; index++)
        {
            if (!TryGetBookBounds(books[index], out Bounds bounds))
            {
                Debug.LogError("Book" + (index + 1) + " has no Renderer and cannot be shuffled.", books[index]);
                return false;
            }

            float width = bounds.size.x;
            bookWidths.Add(width);
            totalBookWidth += width;
            layoutLeftEdge = Mathf.Min(layoutLeftEdge, bounds.min.x);
            originalRightEdge = Mathf.Max(originalRightEdge, bounds.max.x);
        }

        float originalSpan = originalRightEdge - layoutLeftEdge;
        float originalAverageSpacing = books.Count > 1
            ? (originalSpan - totalBookWidth) / (books.Count - 1)
            : 0f;
        effectiveSpacing = Mathf.Max(minimumBookSpacing, originalAverageSpacing);

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
            float targetCenterX = cursor + bookWidths[bookIndex] * 0.5f;
            MoveBookCenterToX(books[bookIndex], targetCenterX);
            cursor += bookWidths[bookIndex] + effectiveSpacing;
        }
    }

    private void UpdateSolvedSlotPositions()
    {
        float cursor = layoutLeftEdge;

        for (int index = 0; index < slots.Count; index++)
        {
            float targetCenterX = cursor + bookWidths[index] * 0.5f;
            Vector3 slotPosition = slots[index].position;
            slotPosition.x = targetCenterX;
            slots[index].position = slotPosition;
            cursor += bookWidths[index] + effectiveSpacing;
        }
    }

    private static void MoveBookCenterToX(Transform book, float targetCenterX)
    {
        if (!TryGetBookBounds(book, out Bounds bounds))
        {
            return;
        }

        Vector3 position = book.position;
        position.x += targetCenterX - bounds.center.x;
        book.position = position;
    }

    private static bool TryGetBookBounds(Transform book, out Bounds combinedBounds)
    {
        Renderer[] renderers = book.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            combinedBounds = default;
            return false;
        }

        combinedBounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            combinedBounds.Encapsulate(renderers[index].bounds);
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
