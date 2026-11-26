using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keyboard / gamepad input for the menus, polled like the gameplay input.
/// Directions repeat while held. <see cref="Consume"/> swallows the rest of the
/// frame, so the press that opened a screen does not also act inside it.
/// </summary>
public static class MenuInput
{
    private const float RepeatDelaySeconds = 0.38f;
    private const float RepeatIntervalSeconds = 0.09f;
    private const float StickThreshold = 0.55f;
    private const int DirectionCount = 4;

    private static readonly bool[] held = new bool[DirectionCount];
    private static readonly bool[] fired = new bool[DirectionCount];
    private static readonly float[] nextRepeat = new float[DirectionCount];

    private static int polledFrame = -1;
    private static int consumedFrame = -1;
    private static bool confirm;
    private static bool cancel;
    private static bool pause;
    private static bool option;

    public static bool Up => Get(fired[0]);
    public static bool Down => Get(fired[1]);
    public static bool Left => Get(fired[2]);
    public static bool Right => Get(fired[3]);

    /// <summary>Enter / Space, or the gamepad's south button.</summary>
    public static bool Confirm => Get(confirm);

    /// <summary>Esc / Backspace, or the gamepad's east button.</summary>
    public static bool Cancel => Get(cancel);

    /// <summary>Esc, or the gamepad's Start button.</summary>
    public static bool Pause => Get(pause);

    /// <summary>Tab, or the gamepad's north button.</summary>
    public static bool Option => Get(option);

    /// <summary>Drops every press of this frame for whoever asks next.</summary>
    public static void Consume()
    {
        consumedFrame = Time.frameCount;
    }

    /// <summary>
    /// Reads the devices once per frame. GameFlow calls it every frame so the
    /// held state never goes stale while no menu is asking.
    /// </summary>
    public static void Poll()
    {
        if (polledFrame == Time.frameCount)
        {
            return;
        }

        polledFrame = Time.frameCount;
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        Vector2 stick = gamepad != null ? gamepad.leftStick.ReadValue() : Vector2.zero;

        UpdateDirection(0, (keyboard != null && (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed))
            || (gamepad != null && gamepad.dpad.up.isPressed) || stick.y > StickThreshold);
        UpdateDirection(1, (keyboard != null && (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed))
            || (gamepad != null && gamepad.dpad.down.isPressed) || stick.y < -StickThreshold);
        UpdateDirection(2, (keyboard != null && (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed))
            || (gamepad != null && gamepad.dpad.left.isPressed) || stick.x < -StickThreshold);
        UpdateDirection(3, (keyboard != null && (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed))
            || (gamepad != null && gamepad.dpad.right.isPressed) || stick.x > StickThreshold);

        confirm = (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
        cancel = (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame))
            || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
        pause = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            || (gamepad != null && gamepad.startButton.wasPressedThisFrame);
        option = (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame);
    }

    private static bool Get(bool value)
    {
        Poll();
        return value && consumedFrame != Time.frameCount;
    }

    private static void UpdateDirection(int index, bool isHeld)
    {
        float now = Time.unscaledTime;
        if (!isHeld)
        {
            fired[index] = false;
        }
        else if (!held[index])
        {
            fired[index] = true;
            nextRepeat[index] = now + RepeatDelaySeconds;
        }
        else if (now >= nextRepeat[index])
        {
            fired[index] = true;
            nextRepeat[index] = now + RepeatIntervalSeconds;
        }
        else
        {
            fired[index] = false;
        }

        held[index] = isHeld;
    }
}
