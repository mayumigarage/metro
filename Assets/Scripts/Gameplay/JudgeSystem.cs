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

/// <summary>Which part of a lane note a judgement is for. A hold note is judged twice.</summary>
public enum NotePart
{
    Tap,
    HoldStart,
    HoldEnd
}

public readonly struct JudgementResult
{
    public JudgementResult(Judgement judgement, int lane, NoteDirection direction, double deltaSeconds, NotePart part = NotePart.Tap)
    {
        this.judgement = judgement;
        this.lane = lane;
        this.direction = direction;
        this.deltaSeconds = deltaSeconds;
        this.part = part;
    }

    public readonly Judgement judgement;
    public readonly int lane;

    /// <summary>None for a plain button note, otherwise the stick direction the note asked for.</summary>
    public readonly NoteDirection direction;

    /// <summary>Input time minus note time: negative = early. For a hold's end, how early the button was let go.</summary>
    public readonly double deltaSeconds;
    public readonly NotePart part;
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
/// judges against DSP song time. Bars are hit with the lane button; chevrons
/// (direction notes, with or without a bar) by flicking the note's stick (either
/// side, whatever the lane) its way, with looser timing windows; hold
/// notes by keeping the button down until their end, and lasers by following them
/// with the stick. Keyboard D/K/F/J and the arrow keys are a development fallback.
/// </summary>
public sealed class JudgeSystem : MonoBehaviour
{
    public const double PerfectWindowSeconds = 0.035d;
    public const double GreatWindowSeconds = 0.070d;
    public const double GoodWindowSeconds = 0.110d;
    public const double MissWindowSeconds = 0.160d;

    // Flicks (stick) are judged more loosely than buttons.
    public const double FlickPerfectWindowSeconds = 0.060d;
    public const double FlickGreatWindowSeconds = 0.100d;
    public const double FlickGoodWindowSeconds = 0.150d;
    public const double FlickMissWindowSeconds = 0.200d;

    // Spin notes: the stick has to turn SpinRequiredDegrees the note's way while the note is
    // between SpinWindowBefore and SpinWindowAfter of its time. Done close to the time = Perfect,
    // done early or late in the window = Great; at least half the turn by the end = Good.
    public const double SpinWindowBeforeSeconds = 0.35d;
    public const double SpinWindowAfterSeconds = 0.25d;
    public const double SpinPerfectWindowSeconds = 0.20d;
    public const float SpinRequiredDegrees = 240f;
    public const float SpinPartialShare = 0.5f;

    /// <summary>A hold let go this close to its end still counts as kept to the end.</summary>
    public const double HoldReleaseWindowSeconds = GoodWindowSeconds;

    /// <summary>Share of a laser's time that has to be followed for it to be Perfect.</summary>
    public const float LaserPerfectAccuracy = 0.65f;

    [SerializeField] private SongClock songClock;
    [SerializeField] private NoteSpawner noteSpawner;

    public event Action<JudgementResult> JudgementResolved;
    public event Action<LaserJudgementResult> LaserJudgementResolved;

    /// <summary>One combo step while a laser is followed (4 per second) or a hold is kept down (3 per second).</summary>
    public event Action ComboTicked;

    // A flick registers when the stick leaves the centre past FlickPressThreshold, or
    // swings to another direction; it has to drop below FlickReleaseThreshold to re-arm.
    private const float FlickPressThreshold = 0.6f;
    private const float FlickReleaseThreshold = 0.35f;
    private const int StickSideCount = 2;

    private static readonly Key[] LaneKeys = { Key.D, Key.K, Key.F, Key.J };

    private readonly NoteDirection[] heldDirections = new NoteDirection[StickSideCount];
    private readonly NoteDirection[] previousDirections = new NoteDirection[StickSideCount];
    private readonly Vector2[] sticks = new Vector2[StickSideCount];
    private readonly bool[] flickedThisFrame = new bool[StickSideCount];
    private readonly float[] spinDeltas = new float[StickSideCount];

    private void Start()
    {
        if (songClock == null)
        {
            songClock = FindFirstObjectByType<SongClock>();
        }

        if (noteSpawner == null)
        {
            noteSpawner = FindFirstObjectByType<NoteSpawner>();
        }
    }

    private void Update()
    {
        if (songClock == null || noteSpawner == null || !songClock.IsRunning)
        {
            return;
        }

        double songTime = songClock.SongTime;
        if (GameSettings.AutoPlay)
        {
            AutoPlay(songTime);
        }
        else
        {
            // The judge offset moves the moment an input is taken to have happened.
            double judgeTime = songTime - GameSettings.JudgeOffsetSeconds;
            JudgeInputs(judgeTime);
            JudgeHolds(judgeTime);
            JudgeSpins(judgeTime);

            while (noteSpawner.TryConsumeNextMissed(judgeTime, GetMissWindow, out NoteView missed))
            {
                ResolveMissed(missed, judgeTime - missed.TargetTime);
            }
        }

        foreach (LaserNoteView activeLaser in noteSpawner.ActiveLasers)
        {
            EmitComboTicks(activeLaser.TakeComboTicks(songTime));
        }

        while (noteSpawner.TryConsumeCompletedLaser(songTime, out LaserNoteView laser))
        {
            Judgement judgement = EvaluateLaser(laser.TrackingAccuracy);
            ResolveLaser(new LaserJudgementResult(judgement, laser.Side, laser.TrackingAccuracy));
        }
    }

    private void JudgeInputs(double judgeTime)
    {
        for (int side = 0; side < StickSideCount; side++)
        {
            flickedThisFrame[side] = UpdateStick(side);
        }

        for (int lane = 0; lane < RhythmStageView.LaneCount; lane++)
        {
            if (WasLanePressed(lane))
            {
                JudgePress(lane, judgeTime);
            }
        }

        for (int lane = 0; lane < RhythmStageView.LaneCount; lane++)
        {
            for (int side = 0; side < StickSideCount; side++)
            {
                if (flickedThisFrame[side])
                {
                    JudgeFlick(lane, side, judgeTime);
                }
            }
        }
    }

    /// <summary>
    /// Feeds each spin note in its window the turn its stick made this frame, and resolves it
    /// once the turn is complete or the window has run out.
    /// </summary>
    private void JudgeSpins(double judgeTime)
    {
        for (int index = noteSpawner.ActiveNotes.Count - 1; index >= 0; index--)
        {
            NoteView note = noteSpawner.ActiveNotes[index];
            if (!note.IsSpin)
            {
                continue;
            }

            double delta = judgeTime - note.TargetTime;
            if (delta < -SpinWindowBeforeSeconds)
            {
                continue;
            }

            if (delta <= SpinWindowAfterSeconds)
            {
                note.AddSpin(spinDeltas[note.Side]);
                if (note.SpinProgress >= SpinRequiredDegrees)
                {
                    noteSpawner.ConsumeActive(note);
                    Resolve(new JudgementResult(Math.Abs(delta) <= SpinPerfectWindowSeconds ? Judgement.Perfect : Judgement.Great, note.Lane, NoteDirection.None, delta));
                }

                continue;
            }

            // The window is over.
            noteSpawner.ConsumeActive(note);
            Judgement result = note.SpinProgress >= SpinRequiredDegrees * SpinPartialShare ? Judgement.Good : Judgement.Miss;
            Resolve(new JudgementResult(result, note.Lane, NoteDirection.None, delta));
        }
    }

    /// <summary>The lane button went down: hits the closest bar in the lane.</summary>
    private void JudgePress(int lane, double judgeTime)
    {
        if (noteSpawner.TryConsumeClosest(lane, judgeTime, MissWindowSeconds, out NoteView note, out double deltaSeconds))
        {
            ResolveHit(note, deltaSeconds);
        }
    }

    /// <summary>A stick flicked: hits the closest chevron in the lane asking for that stick and direction.</summary>
    private void JudgeFlick(int lane, int side, double judgeTime)
    {
        if (noteSpawner.TryConsumeClosestFlick(lane, side, sticks[side], previousDirections[side], judgeTime, FlickMissWindowSeconds, out NoteView note, out double deltaSeconds))
        {
            ResolveHit(note, deltaSeconds);
        }
    }

    /// <summary>
    /// Ends the holds that reached their end (Perfect) or whose button was let go:
    /// letting go within the release window still counts, earlier is a Miss.
    /// </summary>
    private void JudgeHolds(double judgeTime)
    {
        for (int index = noteSpawner.HeldNotes.Count - 1; index >= 0; index--)
        {
            NoteView note = noteSpawner.HeldNotes[index];
            double remaining = note.EndTime - judgeTime;
            if (note.IsReleased)
            {
                // Already judged when the button was let go; it just runs out on screen.
                if (remaining <= 0d)
                {
                    noteSpawner.ConsumeHeld(note);
                }

                continue;
            }

            bool held = IsLaneHeld(note.Lane);
            EmitComboTicks(note.TakeComboTicks(judgeTime, held));
            if (remaining <= 0d)
            {
                // Kept down to the end: Perfect, no need to let go on time.
                noteSpawner.ConsumeHeld(note);
                Resolve(new JudgementResult(Judgement.Perfect, note.Lane, note.Direction, 0d, NotePart.HoldEnd));
            }
            else if (!held)
            {
                // Let go early: judged now, but the hold stays on screen until its end.
                note.Release();
                Judgement judgement = remaining <= HoldReleaseWindowSeconds ? Judgement.Perfect : Judgement.Miss;
                Resolve(new JudgementResult(judgement, note.Lane, note.Direction, -remaining, NotePart.HoldEnd));
            }
        }
    }

    private void EmitComboTicks(int count)
    {
        for (int tick = 0; tick < count; tick++)
        {
            ComboTicked?.Invoke();
        }
    }

    /// <summary>Auto play: every note is hit the moment it arrives and every hold is kept to its end.</summary>
    private void AutoPlay(double songTime)
    {
        while (noteSpawner.TryConsumeNextDue(songTime, out NoteView note))
        {
            Resolve(new JudgementResult(Judgement.Perfect, note.Lane, note.Direction, 0d, note.IsHold ? NotePart.HoldStart : NotePart.Tap));
        }

        for (int index = noteSpawner.HeldNotes.Count - 1; index >= 0; index--)
        {
            NoteView note = noteSpawner.HeldNotes[index];
            EmitComboTicks(note.TakeComboTicks(songTime, true));
            if (songTime >= note.EndTime)
            {
                noteSpawner.ConsumeHeld(note);
                Resolve(new JudgementResult(Judgement.Perfect, note.Lane, note.Direction, 0d, NotePart.HoldEnd));
            }
        }
    }

    /// <summary>A note was consumed by an input. A hold whose start is a Miss is lost as a whole.</summary>
    private void ResolveHit(NoteView note, double deltaSeconds)
    {
        Judgement judgement = note.FlickOnly ? EvaluateFlick(Math.Abs(deltaSeconds)) : Evaluate(Math.Abs(deltaSeconds));
        if (!note.IsHold)
        {
            Resolve(new JudgementResult(judgement, note.Lane, note.Direction, deltaSeconds));
            return;
        }

        if (judgement == Judgement.Miss)
        {
            noteSpawner.ConsumeHeld(note);
            ResolveMissed(note, deltaSeconds);
            return;
        }

        Resolve(new JudgementResult(judgement, note.Lane, note.Direction, deltaSeconds, NotePart.HoldStart));
    }

    private void ResolveMissed(NoteView note, double deltaSeconds)
    {
        if (!note.IsHold)
        {
            Resolve(new JudgementResult(Judgement.Miss, note.Lane, note.Direction, deltaSeconds));
            return;
        }

        Resolve(new JudgementResult(Judgement.Miss, note.Lane, note.Direction, deltaSeconds, NotePart.HoldStart));
        Resolve(new JudgementResult(Judgement.Miss, note.Lane, note.Direction, 0d, NotePart.HoldEnd));
    }

    /// <summary>Refreshes where a stick leans and the direction it points in. True on the frame it newly points in one.</summary>
    private bool UpdateStick(int side)
    {
        Vector2 stick = StickInput.Read(side);
        Vector2 before = sticks[side];
        // Turn since last frame, only while the stick is out of the centre both times.
        spinDeltas[side] = before.magnitude >= StickInput.TiltThreshold && stick.magnitude >= StickInput.TiltThreshold
            ? Vector2.SignedAngle(before, stick)
            : 0f;
        sticks[side] = stick;

        NoteDirection previous = heldDirections[side];
        previousDirections[side] = previous;
        float threshold = previous == NoteDirection.None ? FlickPressThreshold : FlickReleaseThreshold;
        NoteDirection direction = StickInput.GetDirection(stick, threshold, previous);
        heldDirections[side] = direction;
        return direction != NoteDirection.None && direction != previous;
    }

    /// <summary>How late a note may be before it is swept as a Miss: flicks get longer.</summary>
    private static double GetMissWindow(NoteView note)
    {
        // Spin notes are resolved by JudgeSpins; this only backs it up.
        return note.IsSpin ? SpinWindowAfterSeconds + 0.1d : note.FlickOnly ? FlickMissWindowSeconds : MissWindowSeconds;
    }

    public static Judgement EvaluateFlick(double absoluteDeltaSeconds)
    {
        return absoluteDeltaSeconds <= FlickPerfectWindowSeconds ? Judgement.Perfect :
            absoluteDeltaSeconds <= FlickGreatWindowSeconds ? Judgement.Great :
            absoluteDeltaSeconds <= FlickGoodWindowSeconds ? Judgement.Good :
            Judgement.Miss;
    }

    public static Judgement Evaluate(double absoluteDeltaSeconds)
    {
        return absoluteDeltaSeconds <= PerfectWindowSeconds ? Judgement.Perfect :
            absoluteDeltaSeconds <= GreatWindowSeconds ? Judgement.Great :
            absoluteDeltaSeconds <= GoodWindowSeconds ? Judgement.Good :
            Judgement.Miss;
    }

    public static Judgement EvaluateLaser(float trackingAccuracy)
    {
        // Lasers are all-or-nothing: Perfect or Miss.
        return trackingAccuracy >= LaserPerfectAccuracy ? Judgement.Perfect : Judgement.Miss;
    }

    /// <summary>True while a lane's button (or its keyboard key) is down.</summary>
    public static bool IsLaneHeld(int lane)
    {
        ButtonControl button = GetGamepadButton(lane);
        ButtonControl key = GetKeyboardKey(lane);
        return (button != null && button.isPressed) || (key != null && key.isPressed);
    }

    private static bool WasLanePressed(int lane)
    {
        ButtonControl button = GetGamepadButton(lane);
        ButtonControl key = GetKeyboardKey(lane);
        return (button != null && button.wasPressedThisFrame) || (key != null && key.wasPressedThisFrame);
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
        return keyboard != null && lane >= 0 && lane < LaneKeys.Length ? keyboard[LaneKeys[lane]] : null;
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
