using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Read-only diagnostics for the book drag-and-reorder interaction. It does
/// not move books or change puzzle state; it observes the existing controller
/// and reports selection, drag plane, shelf axis, slot coordinates and the
/// insertion index whenever they change.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class BookDragReorderDebug : MonoBehaviour
{
    [SerializeField] private KeyCode dumpStateKey = KeyCode.F8;
    [SerializeField] private bool logPointerRay = true;
    [SerializeField] private bool logInsertionChanges = true;
    [SerializeField, Min(1)] private int maximumRayHitsToLog = 8;

    private BookPuzzleInteractionController interaction;
    private BookPuzzleViewController view;
    private Camera targetCamera;
    private Transform booksRoot;
    private Transform slotsRoot;
    private Transform previousDraggedBook;
    private int previousInsertionIndex = int.MinValue;
    private bool isReady;

    private FieldInfo draggedBookField;
    private FieldInfo insertionIndexField;
    private FieldInfo shelfAxisField;
    private FieldInfo dragPlaneField;
    private FieldInfo pointerOffsetField;
    private FieldInfo remainingBooksField;
    private FieldInfo currentOrderField;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        Transform root = FindSceneTransform("BookPuzzleRoot");
        if (root != null && root.GetComponent<BookDragReorderDebug>() == null)
        {
            root.gameObject.AddComponent<BookDragReorderDebug>();
        }
    }

    private IEnumerator Start()
    {
        interaction = GetComponent<BookPuzzleInteractionController>();
        view = GetComponent<BookPuzzleViewController>();
        targetCamera = Camera.main;
        booksRoot = transform.Find("Book");
        slotsRoot = transform.Find("Slots");

        CacheDiagnosticFields();

        // Wait until BookPuzzleShuffleController has completed its Start call.
        yield return null;
        isReady = true;
        DumpState("INITIAL_AFTER_SHUFFLE");
    }

    private void LateUpdate()
    {
        if (!isReady)
        {
            return;
        }

        if (Input.GetKeyDown(dumpStateKey))
        {
            DumpState("MANUAL_F8");
        }

        if (Input.GetMouseButtonDown(0) && logPointerRay)
        {
            LogPointerRay("LEFT_DOWN");
        }

        Transform draggedBook = ReadField<Transform>(draggedBookField);
        int insertionIndex = ReadField(insertionIndexField, -1);

        if (draggedBook != previousDraggedBook)
        {
            if (previousDraggedBook != null)
            {
                DumpState("DRAG_ENDED:" + previousDraggedBook.name);
            }

            if (draggedBook != null)
            {
                DumpState("DRAG_STARTED:" + draggedBook.name);
            }
        }

        if (draggedBook != null && logInsertionChanges && insertionIndex != previousInsertionIndex)
        {
            DumpState($"INSERTION_CHANGED:{previousInsertionIndex}->{insertionIndex}");
        }

        if (Input.GetMouseButtonUp(0) && logPointerRay)
        {
            LogPointerRay("LEFT_UP");
        }

        previousDraggedBook = draggedBook;
        previousInsertionIndex = insertionIndex;
    }

    private void CacheDiagnosticFields()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(BookPuzzleInteractionController);
        draggedBookField = type.GetField("draggedBook", flags);
        insertionIndexField = type.GetField("insertionIndex", flags);
        shelfAxisField = type.GetField("shelfAxis", flags);
        dragPlaneField = type.GetField("dragPlane", flags);
        pointerOffsetField = type.GetField("pointerToBookOffset", flags);
        remainingBooksField = type.GetField("remainingBooks", flags);
        currentOrderField = type.GetField("currentOrder", flags);
    }

    private void LogPointerRay(string phase)
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            Debug.LogWarning($"[BookDragDebug][{phase}] Camera.main is missing.", this);
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            Mathf.Infinity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        StringBuilder message = new StringBuilder(768);
        message.Append($"[BookDragDebug][{phase}] frame={Time.frameCount}; pointer={Input.mousePosition}; ");
        message.Append($"viewFocused={(view != null && view.IsFocused)}; viewTransitioning={(view != null && view.IsTransitioning)}; ");
        message.Append($"rayOrigin={Format(ray.origin)}; rayDirection={Format(ray.direction)}; hits={hits.Length}");

        int count = Mathf.Min(hits.Length, maximumRayHitsToLog);
        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = hits[index];
            Transform directBook = FindDirectBook(hit.transform);
            message.Append($" || #{index} collider={Path(hit.transform)}; distance={hit.distance:F4}; ");
            message.Append($"directBook={(directBook == null ? "<none>" : directBook.name)}; layer={hit.collider.gameObject.layer}; trigger={hit.collider.isTrigger}");
        }

        Debug.Log(message.ToString(), this);
    }

    private void DumpState(string reason)
    {
        Vector3 shelfAxis = ReadField(shelfAxisField, Vector3.zero);
        Plane dragPlane = ReadField(dragPlaneField, new Plane(Vector3.up, 0f));
        float pointerOffset = ReadField(pointerOffsetField, 0f);
        int insertionIndex = ReadField(insertionIndexField, -1);
        Transform draggedBook = ReadField<Transform>(draggedBookField);
        List<Transform> remaining = ReadField<List<Transform>>(remainingBooksField);
        List<Transform> currentOrder = ReadField<List<Transform>>(currentOrderField);

        StringBuilder message = new StringBuilder(2048);
        message.Append($"[BookDragDebug][{reason}] frame={Time.frameCount}; ");
        message.Append($"focused={(view != null && view.IsFocused)}; transitioning={(view != null && view.IsTransitioning)}; ");
        message.Append($"dragged={(draggedBook == null ? "<none>" : draggedBook.name)}; insertionIndex={insertionIndex}; ");
        message.Append($"shelfAxis={Format(shelfAxis)} magnitude={shelfAxis.magnitude:F4}; ");
        message.Append($"dragPlaneNormal={Format(dragPlane.normal)} distance={dragPlane.distance:F4}; pointerOffset={pointerOffset:F4}; ");
        message.Append($"puzzleRoot={TransformDescription(transform)}; booksRoot={TransformDescription(booksRoot)}; slotsRoot={TransformDescription(slotsRoot)}; ");
        message.Append($"remaining={Names(remaining)}; currentOrder={Names(currentOrder)}");

        if (booksRoot != null)
        {
            for (int index = 1; index <= 9; index++)
            {
                Transform book = booksRoot.Find("Book" + index);
                if (book == null)
                {
                    message.Append($" || Book{index}=MISSING");
                    continue;
                }

                Bounds bounds = GetBounds(book);
                message.Append($" || {book.name}: worldCenter={Format(bounds.center)}, axisCoordinate={Vector3.Dot(bounds.center, shelfAxis):F4}, localPos={Format(book.localPosition)}");
            }
        }

        if (slotsRoot != null)
        {
            float previousCoordinate = float.NaN;
            for (int index = 1; index <= 9; index++)
            {
                Transform slot = slotsRoot.Find("Slot" + index.ToString("00"));
                if (slot == null)
                {
                    message.Append($" || Slot{index:00}=MISSING");
                    continue;
                }

                float coordinate = Vector3.Dot(slot.position, shelfAxis);
                string monotonic = float.IsNaN(previousCoordinate)
                    ? "first"
                    : $"delta={coordinate - previousCoordinate:F4}";
                message.Append($" || {slot.name}: world={Format(slot.position)}, axisCoordinate={coordinate:F4}, {monotonic}");
                previousCoordinate = coordinate;
            }
        }

        Debug.Log(message.ToString(), this);
    }

    private Transform FindDirectBook(Transform candidate)
    {
        while (candidate != null && candidate.parent != booksRoot)
        {
            candidate = candidate.parent;
        }

        return candidate != null && candidate.parent == booksRoot ? candidate : null;
    }

    private T ReadField<T>(FieldInfo field, T fallback = default)
    {
        if (interaction == null || field == null)
        {
            return fallback;
        }

        object value = field.GetValue(interaction);
        return value is T typed ? typed : fallback;
    }

    private static Bounds GetBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static string TransformDescription(Transform value)
    {
        return value == null
            ? "<missing>"
            : $"{Path(value)} pos={Format(value.position)} rot={Format(value.eulerAngles)} scale={Format(value.lossyScale)}";
    }

    private static string Names(List<Transform> values)
    {
        if (values == null)
        {
            return "<unavailable>";
        }

        List<string> names = new List<string>();
        foreach (Transform value in values)
        {
            names.Add(value == null ? "<null>" : value.name);
        }

        return "[" + string.Join(",", names) + "]";
    }

    private static string Path(Transform value)
    {
        if (value == null)
        {
            return "<none>";
        }

        string result = value.name;
        while (value.parent != null)
        {
            value = value.parent;
            result = value.name + "/" + result;
        }

        return result;
    }

    private static string Format(Vector3 value)
    {
        return $"({value.x:F4},{value.y:F4},{value.z:F4})";
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
}
