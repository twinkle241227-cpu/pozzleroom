using UnityEngine;

/// <summary>
/// Keeps a focused puzzle view exclusive. While one controller owns the lock,
/// all other puzzle entrances ignore pointer input and cannot move the camera.
/// </summary>
public static class PuzzleViewLock
{
    private static Object activeOwner;

    public static bool IsLockedByOther(Object owner)
    {
        return activeOwner != null && activeOwner != owner;
    }

    public static bool TryAcquire(Object owner)
    {
        if (owner == null)
        {
            return false;
        }

        if (activeOwner == null || activeOwner == owner)
        {
            activeOwner = owner;
            return true;
        }

        return false;
    }

    public static void Release(Object owner)
    {
        if (activeOwner == owner)
        {
            activeOwner = null;
        }
    }
}
