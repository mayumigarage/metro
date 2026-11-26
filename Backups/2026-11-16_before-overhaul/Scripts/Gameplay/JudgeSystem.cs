using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public enum Judgement
{
    Perfect,
    Great,
    Good,
    Miss
}

public readonly struct JudgementResult
{
    public JudgementResult(Judgement judgement, int lane, NoteDirection direction, double deltaSeconds)
    {
        this.judgement = judgement;
        this.lane = lane;
        this.direction = direction;
        this.deltaSeconds = deltaSeconds;
    }

    public readonly Judgement judgement;
    public readonly int lane;

    /// <summary>None for a plain button note, otherwise the stick direction the note asked for.</summary>
    public readonly NoteDirection direction;
    public readonly double deltaSeconds;
}

public readonly struct LaserJudgementResult
{
    public LaserJudgementResult(Judgement judgement, int side, float trackingAccuracy)
    {
        this.judgement = judgement;
        this.side = side;
        this.trackingAccuracy = trackingAccuracy;
    }

    public readonly Judgement judgement;
    public readonly int side;
    public readonly float trackingAccuracy;
}

/// <summary>
/// Maps the four lanes (0 = L, 1 = R, 2 = ZL, 3 = ZR) to controller inputs and
/// judges against DSP song time. Plain notes are hit with the lane button,
/// directional notes with the lane button plus a flick of the side's stick, and
/// lasers by following them with the stick. Keyboard D/K/F/J and the arrow keys
/// are a development fallback.
/// </summary>
public sealed class JudgeSystem : MonoBehaviour
{
    public const double PerfectWindowSeconds = 0.035d;
    public const double GreatWindowSeconds = 0.070d;
    public const double GoodWindowSeconds = 0.110d;
    public const double MissWindowSeconds = 0.160d;

    [SerializeField] private SongClock songClock;
    [SerializeField] private NoteSpawner noteSpawner;

    public event Action<JudgementResult> JudgementResolved;
    public event Action<LaserJudgementResult> LaserJudgementResolved;

    // A flick registers when the stick leaves the centre past FlickPressThreshold, or
    // swings to another direction; it has to drop below FlickReleaseThreshold to re-arm.
    private const float FlickPressThreshold = 0.6f;
    private const float FlickReleaseThreshold = 0.35f;
    private const int StickSideCount = 2;

    private static readonly Key[] LaneKeys = { Key.D, Key.K, Key.F, Key.J };

    private readonly NoteDirection[] heldDirections = new NoteDirection[StickSideCount];
    private readonly bool[] flickedThisFrame = new bool[StickSideCount];
    private readonly bool[] pressedThisFrame = new bool[RhythmStageView.LaneCount];

    private void Start()
    {
        songClock ??= FindFirstObjectByType<SongClock>();
        noteSpawner ??= FindFirstObjectByType<NoteSpawner>();
    }

    private void Update()
    {
        if (songClock == null || noteSpawner == null)
        {
            return;
        }

        double songTime = songClock.SongTime;

        // Sticks first, so a button press this frame already sees where its stick points.
        for (int side = 0; side < StickSideCount; side++)
        {
            flickedThisFrame[side] = UpdateStick(side);
        }

        for (int lane = 0; lane < RhythmStageView.LaneCount; lane++)
        {
            pressedThisFrame[lane] = WasLanePressed(lane);
            if (pressedThisFrame[lane])
            {
                JudgePress(lane, songTime);
            }
        }

        for (int lane = 0; lane < RhythmStageView.LaneCount; lane++)
        {
            // A flick completes a directional note only while the lane button is down.
            // A press made this frame has already been judged with the new direction.
            if (flickedThisFrame[RhythmStageView.GetStickSide(lane)] && !pressedThisFrame[lane] && IsLaneHeld(lane))
            {
                JudgeFlick(lane, songTime);
            }
        }

        while (noteSpawner.TryConsumeNextMissed(songTime, MissWindowSeconds, out NoteView missed))
        {
            Resolve(new JudgementResult(Judgement.Miss, missed.Lane, missed.Direction, songTime - missed.TargetTime));
        }

        while (noteSpawner.TryConsumeCompletedLaser(songTime, out LaserNoteView laser))
        {
            Judgement judgement = EvaluateLaser(laser.TrackingAccuracy);
            ResolveLaser(new LaserJudgementResult(judgement, laser.Side, laser.TrackingAccuracy));
        }
    }

    /// <summary>
    /// The lane button went down: hits the closest plain note, or the closest
    /// directional note whose direction the side's stick is already pointing in.
    /// </summary>
    private void JudgePress(int lane, double songTime)
    {
        NoteDirection stickDirection = heldDirections[RhythmStageView.GetStickSide(lane)];
        if (!noteSpawner.TryConsumeClosest(lane, stickDirection, songTime, MissWindowSeconds, out NoteView note, out double deltaSeconds))
        {
            return;
        }

        Resolve(new JudgementResult(Evaluate(Math.Abs(deltaSeconds)), lane, note.Direction, deltaSeconds));
    }

    /// <summary>
    /// The stick flicked while the lane button was already held: hits the closest
    /// directional note in the lane asking for that direction.
    /// </summary>
    private void JudgeFlick(int lane, double songTime)
    {
        NoteDirection direction = heldDirections[RhythmStageView.GetStickSide(lane)];
        if (!noteSpawner.TryConsumeClosestDirectional(lane, direction, songTime, MissWindowSeconds, out NoteView note, out double deltaSeconds))
        {
            return;
        }

        Resolve(new JudgementResult(Evaluate(Math.Abs(deltaSeconds)), lane, direction, deltaSeconds));
    }

    /// <summary>Refreshes the direction a stick points in. True on the frame it newly points in one.</summary>
    private bool UpdateStick(int side)
    {
        NoteDirection previous = heldDirections[side];
        float threshold = previous == NoteDirection.None ? FlickPressThreshold : FlickReleaseThreshold;
        NoteDirection direction = StickInput.GetDirection(StickInput.Read(side), threshold);
        heldDirections[side] = direction;
        return direction != NoteDirection.None && direction != previous;
    }

    public static Judgement Evaluate(double absoluteDeltaSeconds)
    {
        return absoluteDeltaSeconds <= PerfectWindowSeconds ? Judgement.Perfect :
            absoluteDeltaSeconds <= GreatWindowSeconds ? Judgement.Great :
            absoluteDeltaSeconds <= GoodWindowSeconds ? Judgement.Good :
            absoluteDeltaSeconds <= MissWindowSeconds ? Judgement.Miss :
            Judgement.Miss;
    }

    public static Judgement EvaluateLaser(float trackingAccuracy)
    {
        return trackingAccuracy >= 0.90f ? Judgement.Perfect :
            trackingAccuracy >= 0.70f ? Judgement.Great :
            trackingAccuracy >= 0.45f ? Judgement.Good :
            Judgement.Miss;
    }

    private static bool WasLanePressed(int lane)
    {
        ButtonControl button = GetGamepadButton(lane);
        ButtonControl key = GetKeyboardKey(lane);
        return (button != null && button.wasPressedThisFrame) || (key != null && key.wasPressedThisFrame);
    }

    private static bool IsLaneHeld(int lane)
    {
        ButtonControl button = GetGamepadButton(lane);
        ButtonControl key = GetKeyboardKey(lane);
        return (button != null && button.isPressed) || (key != null && key.isPressed);
    }

    private static ButtonControl GetGamepadButton(int lane)
    {
        Gamepad gamepad = Gamepad.current;
        if (gamepad == null)
        {
            return null;
        }

        switch (lane)
        {
            case 0:
                return gamepad.leftShoulder;
            case 1:
                return gamepad.rightShoulder;
            case 2:
                return gamepad.leftTrigger;
            case 3:
                return gamepad.rightTrigger;
            default:
                return null;
        }
    }

    private static ButtonControl GetKeyboardKey(int lane)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null ? keyboard[LaneKeys[lane]] : null;
    }

    private void Resolve(JudgementResult result)
    {
        JudgementResolved?.Invoke(result);
    }

    private void ResolveLaser(LaserJudgementResult result)
    {
        LaserJudgementResolved?.Invoke(result);
    }
}
