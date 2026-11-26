using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The one place the analog sticks are read. Side 0 = left stick, 1 = right stick.
/// Without a gamepad the arrow keys drive both sides at once.
/// </summary>
public static class StickInput
{
    private static readonly Vector2?[] autoValues = new Vector2?[2];

    /// <summary>
    /// Auto play: makes <see cref="Read"/> return <paramref name="value"/> for a side
    /// (null hands the stick back to the player). Set by NoteSpawner each frame.
    /// </summary>
    public static void SetAutoValue(int side, Vector2? value)
    {
        autoValues[side <= 0 ? 0 : 1] = value;
    }

    /// <summary>
    /// Keeps a laser point inside the square -1..1 on both axes. A real stick only reaches the
    /// unit circle, but a laser is followed by direction, so charts may use the corners too.
    /// </summary>
    public static Vector2 ClampToSquare(Vector2 value)
    {
        return new Vector2(Mathf.Clamp(value.x, -1f, 1f), Mathf.Clamp(value.y, -1f, 1f));
    }

    /// <summary>Stick position, clamped to the unit circle.</summary>
    public static Vector2 Read(int side)
    {
        Vector2? autoValue = autoValues[side <= 0 ? 0 : 1];
        if (autoValue.HasValue)
        {
            return Vector2.ClampMagnitude(autoValue.Value, 1f);
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = side <= 0 ? gamepad.leftStick.ReadValue() : gamepad.rightStick.ReadValue();
            return Vector2.ClampMagnitude(stick, 1f);
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float horizontal = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float vertical = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
        return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
    }

    /// <summary>
    /// How far past its own 45° sector the stick may drift and still count as the
    /// direction it already points in, so it does not flicker between neighbours.
    /// </summary>
    private const float SectorHysteresisDegrees = 10f;

    /// <summary>
    /// The direction (one of eight) the stick leans in, or None while it is inside
    /// <paramref name="threshold"/>. <paramref name="current"/> is the direction it
    /// pointed in so far: it is kept until the stick clearly leaves its sector.
    /// </summary>
    public static NoteDirection GetDirection(Vector2 stick, float threshold, NoteDirection current = NoteDirection.None)
    {
        if (stick.magnitude < threshold)
        {
            return NoteDirection.None;
        }

        float angle = GetAngle(stick);
        if (current != NoteDirection.None
            && Mathf.Abs(Mathf.DeltaAngle(angle, GetAngle(GetDirectionVector(current)))) <= 22.5f + SectorHysteresisDegrees)
        {
            return current;
        }

        // Sector 0 = Right, counting counter-clockwise in 45° steps.
        int sector = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 45f) % 8;
        return SectorDirections[sector];
    }

    private static readonly NoteDirection[] SectorDirections =
    {
        NoteDirection.Right,
        NoteDirection.UpRight,
        NoteDirection.Up,
        NoteDirection.UpLeft,
        NoteDirection.Left,
        NoteDirection.DownLeft,
        NoteDirection.Down,
        NoteDirection.DownRight
    };

    /// <summary>Unit vector of a direction (zero for None).</summary>
    public static Vector2 GetDirectionVector(NoteDirection direction)
    {
        const float diagonal = 0.70710678f;
        switch (direction)
        {
            case NoteDirection.Up:
                return Vector2.up;
            case NoteDirection.Down:
                return Vector2.down;
            case NoteDirection.Left:
                return Vector2.left;
            case NoteDirection.Right:
                return Vector2.right;
            case NoteDirection.UpLeft:
                return new Vector2(-diagonal, diagonal);
            case NoteDirection.UpRight:
                return new Vector2(diagonal, diagonal);
            case NoteDirection.DownLeft:
                return new Vector2(-diagonal, -diagonal);
            case NoteDirection.DownRight:
                return new Vector2(diagonal, -diagonal);
            default:
                return Vector2.zero;
        }
    }

    /// <summary>How far the stick has to lean before it counts as pointing anywhere.</summary>
    public const float TiltThreshold = 0.5f;

    /// <summary>
    /// True when <paramref name="stick"/> leans past <see cref="TiltThreshold"/> within
    /// the STICK ANGLE option of <paramref name="direction"/> (any non-zero vector).
    /// </summary>
    public static bool PointsToward(Vector2 stick, Vector2 direction)
    {
        return stick.magnitude >= TiltThreshold
            && direction.sqrMagnitude > 1e-8f
            && Vector2.Angle(stick, direction) <= GameSettings.StickAngleDegrees;
    }

    public static bool PointsToward(Vector2 stick, NoteDirection direction)
    {
        return direction != NoteDirection.None && PointsToward(stick, GetDirectionVector(direction));
    }

    /// <summary>The same direction with left and right swapped.</summary>
    public static NoteDirection Mirror(NoteDirection direction)
    {
        switch (direction)
        {
            case NoteDirection.Left:
                return NoteDirection.Right;
            case NoteDirection.Right:
                return NoteDirection.Left;
            case NoteDirection.UpLeft:
                return NoteDirection.UpRight;
            case NoteDirection.UpRight:
                return NoteDirection.UpLeft;
            case NoteDirection.DownLeft:
                return NoteDirection.DownRight;
            case NoteDirection.DownRight:
                return NoteDirection.DownLeft;
            default:
                return direction;
        }
    }

    private static float GetAngle(Vector2 vector)
    {
        return Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
    }
}
