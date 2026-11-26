using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The one place the analog sticks are read. Side 0 = left stick, 1 = right stick.
/// Without a gamepad the arrow keys drive both sides at once.
/// </summary>
public static class StickInput
{
    /// <summary>Stick position, clamped to the unit circle.</summary>
    public static Vector2 Read(int side)
    {
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

    /// <summary>The direction the stick leans in, or None while it is inside <paramref name="threshold"/>.</summary>
    public static NoteDirection GetDirection(Vector2 stick, float threshold)
    {
        if (stick.magnitude < threshold)
        {
            return NoteDirection.None;
        }

        if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y))
        {
            return stick.x < 0f ? NoteDirection.Left : NoteDirection.Right;
        }

        return stick.y < 0f ? NoteDirection.Down : NoteDirection.Up;
    }

    public static Vector2 GetDirectionVector(NoteDirection direction)
    {
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
            default:
                return Vector2.zero;
        }
    }
}
