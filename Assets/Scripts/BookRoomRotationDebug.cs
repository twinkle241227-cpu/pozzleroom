using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Read-only diagnostics for the book shelf after RoomPivot rotation.
/// It never changes transforms; copy Console entries beginning with
/// [BookRoomDebug] after reproducing a problem.
/// </summary>
public sealed class BookRoomRotationDebug : MonoBehaviour
{
    private const float RotationChangeThreshold = 0.1f;

    private readonly Dictionary<Transform, Vector3> initialBookLocalPositions = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Quaternion> initialBookLocalRotations = new Dictionary<Transform, Quaternion>();

    private Transform roomPivot;
    private Transform booksRoot;
    private Camera targetCamera;
    private BookPuzzleViewController bookView;
    private Quaternion lastRoomRotation;
    private bool lastFocused;
    private bool isReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        Transform root = FindSceneTransform("BookPuzzleRoot");
        if (root == null || root.GetComponent<BookRoomRotationDebug>() != null)
        {
            return;
        }

        root.gameObject.AddComponent<BookRoomRotationDebug>();
    }

    private void Start()
    {
        booksRoot = transform;
        roomPivot = FindRoomPivot();
        targetCamera = Camera.main;
        bookView = GetComponent<BookPuzzleViewController>();

        foreach (Transform child in booksRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!IsNumberedBook(child.name))
            {
                continue;
            }

            initialBookLocalPositions[child] = child.localPosition;
            initialBookLocalRotations[child] = child.localRotation;
        }

        lastRoomRotation = roomPivot != null ? roomPivot.rotation : Quaternion.identity;
        lastFocused = bookView != null && bookView.IsFocused;
        isReady = true;
        LogSnapshot("Initial capture");
    }

    private void LateUpdate()
    {
        if (!isReady)
        {
            return;
        }

        if (roomPivot != null && Quaternion.Angle(lastRoomRotation, roomPivot.rotation) >= RotationChangeThreshold)
        {
            lastRoomRotation = roomPivot.rotation;
            LogSnapshot("Room rotation changed");
        }

        bool focused = bookView != null && bookView.IsFocused;
        if (focused != lastFocused)
        {
            lastFocused = focused;
            LogSnapshot(focused ? "Entered book view" : "Exited book view");
        }

        if (Input.GetMouseButtonDown(0))
        {
            LogSnapshot("Left mouse down");
        }
        else if (Input.GetMouseButtonUp(0))
        {
            LogSnapshot("Left mouse up");
        }
    }

    private void LogSnapshot(string reason)
    {
        StringBuilder output = new StringBuilder();
        output.Append($"[BookRoomDebug] {reason}; ");
        AppendTransform(output, "roomPivot", roomPivot);
        output.Append("; ");
        AppendTransform(output, "booksRoot", booksRoot);
        output.Append("; ");
        AppendTransform(output, "camera", targetCamera == null ? null : targetCamera.transform);
        output.Append($"; focused={(bookView != null && bookView.IsFocused)}");

        foreach (KeyValuePair<Transform, Vector3> entry in initialBookLocalPositions)
        {
            Transform book = entry.Key;
            if (book == null)
            {
                continue;
            }

            Vector3 localDelta = book.localPosition - entry.Value;
            float localAngleDelta = Quaternion.Angle(initialBookLocalRotations[book], book.localRotation);
            output.Append($" || {book.name}: active={book.gameObject.activeInHierarchy}; ");
            output.Append($"localPos={book.localPosition:F4}; localDelta={localDelta:F4}; ");
            output.Append($"localRotDelta={localAngleDelta:F2}°; worldPos={book.position:F4}; worldRot={book.eulerAngles:F2}");
        }

        Debug.Log(output.ToString(), this);
    }

    private static void AppendTransform(StringBuilder output, string label, Transform value)
    {
        if (value == null)
        {
            output.Append(label + "=<missing>");
            return;
        }

        output.Append($"{label}=name:{value.name}, pos:{value.position:F4}, rot:{value.eulerAngles:F2}");
    }

    private static Transform FindRoomPivot()
    {
        RoomPivotDragController controller = FindObjectOfType<RoomPivotDragController>();
        return controller == null ? null : controller.transform;
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

    private static bool IsNumberedBook(string objectName)
    {
        if (string.IsNullOrEmpty(objectName) || !objectName.StartsWith("Book", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(objectName.Substring("Book".Length), out int number) && number >= 1 && number <= 9;
    }
}
