using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates lane notes and lasers early enough for them to travel to the judgement
/// line, and owns them until a TryConsume* / ConsumeHeld call takes them out of play.
/// Idle until <see cref="Begin"/> is given a chart.
/// </summary>
public sealed class NoteSpawner : MonoBehaviour
{
    [SerializeField] private SongClock songClock;
    [SerializeField] private ChartLoader chartLoader;
    [SerializeField] private NoteView notePrefab;
    [SerializeField] private LaserNoteView laserNotePrefab;
    [SerializeField] private Transform noteParent;
    [Tooltip("Travel time at HI-SPEED x1.0. The option divides it.")]
    [SerializeField] private float travelTimeSeconds = 1.5f;

    private readonly List<NoteView> activeNotes = new List<NoteView>();
    private readonly List<NoteView> heldNotes = new List<NoteView>();
    private readonly List<LaserNoteView> activeLasers = new List<LaserNoteView>();
    private SongChartData chart;
    private int nextNoteIndex;
    private int nextLaserIndex;

    /// <summary>Notes on their way to the judgement line.</summary>
    public IReadOnlyList<NoteView> ActiveNotes => activeNotes;

    /// <summary>Hold notes whose head was hit and whose button is being kept down.</summary>
    public IReadOnlyList<NoteView> HeldNotes => heldNotes;

    public IReadOnlyList<LaserNoteView> ActiveLasers => activeLasers;

    /// <summary>The chart being played (null when idle). Also a chart editor preview, unlike ChartLoader.Chart.</summary>
    public SongChartData Chart => chart;

    /// <summary>
    /// Seconds a note takes from the far end to the judgement line, at the current HI-SPEED.
    /// With APPROACH = ACCEL this is longer (<see cref="RhythmStageView.ApproachTimeScale"/>):
    /// the notes crawl in from far away and reach the line at the LINEAR option's speed.
    /// </summary>
    public float TravelTimeSeconds => Mathf.Max(0.05f, travelTimeSeconds / GameSettings.HiSpeed) * RhythmStageView.ApproachTimeScale;

    /// <summary>True once the whole chart was spawned and nothing is left in play.</summary>
    public bool IsFinished => chart != null
        && nextNoteIndex >= chart.notes.Length
        && nextLaserIndex >= chart.lasers.Length
        && activeNotes.Count == 0
        && heldNotes.Count == 0
        && activeLasers.Count == 0;

    private void Start()
    {
        if (songClock == null)
        {
            songClock = FindFirstObjectByType<SongClock>();
        }

        if (chartLoader == null)
        {
            chartLoader = FindFirstObjectByType<ChartLoader>();
        }

        if (noteParent == null)
        {
            noteParent = transform;
        }
    }

    /// <summary>Removes everything in play and starts spawning <paramref name="newChart"/> from its beginning.</summary>
    public void Begin(SongChartData newChart)
    {
        Clear();
        chart = newChart ?? new SongChartData();
    }

    /// <summary>Removes everything in play and stops spawning.</summary>
    public void Clear()
    {
        foreach (NoteView note in activeNotes)
        {
            Destroy(note.gameObject);
        }

        foreach (NoteView note in heldNotes)
        {
            Destroy(note.gameObject);
        }

        foreach (LaserNoteView laser in activeLasers)
        {
            Destroy(laser.gameObject);
        }

        activeNotes.Clear();
        heldNotes.Clear();
        activeLasers.Clear();
        chart = null;
        nextNoteIndex = 0;
        nextLaserIndex = 0;
        StickInput.SetAutoValue(0, null);
        StickInput.SetAutoValue(1, null);
    }

    private void Update()
    {
        if (songClock == null || chart == null || !songClock.IsRunning)
        {
            return;
        }

        double songTime = songClock.SongTime;
        // What is drawn follows the DISPLAY OFFSET option; the sticks and judging keep the song time.
        double viewTime = songTime - GameSettings.VisualOffsetSeconds;
        float travelTime = TravelTimeSeconds;
        while (nextNoteIndex < chart.notes.Length && chart.notes[nextNoteIndex].time - viewTime <= travelTime)
        {
            Spawn(chart.notes[nextNoteIndex], IsSimultaneous(nextNoteIndex));
            nextNoteIndex++;
        }

        while (nextLaserIndex < chart.lasers.Length && chart.lasers[nextLaserIndex].StartTime - viewTime <= travelTime)
        {
            SpawnLaser(chart.lasers[nextLaserIndex], viewTime, travelTime);
            nextLaserIndex++;
        }

        foreach (NoteView note in activeNotes)
        {
            note.UpdateView(GetProgress(note.TargetTime, viewTime, travelTime), GetProgress(note.EndTime, viewTime, travelTime));
        }

        foreach (NoteView note in heldNotes)
        {
            // The head waits on the judgement line while the rest of the body arrives.
            note.UpdateView(1f, GetProgress(note.EndTime, viewTime, travelTime));
        }

        // Auto play steers the sticks along the lasers; otherwise they belong to the player.
        bool autoPlay = GameSettings.AutoPlay;
        StickInput.SetAutoValue(0, null);
        StickInput.SetAutoValue(1, null);
        if (autoPlay)
        {
            // Later lasers first, so the one already being crossed (earliest) wins its side.
            for (int index = activeLasers.Count - 1; index >= 0; index--)
            {
                LaserNoteView laser = activeLasers[index];
                StickInput.SetAutoValue(laser.Side, laser.GetMotion(songTime));
            }
        }

        for (int index = activeLasers.Count - 1; index >= 0; index--)
        {
            LaserNoteView laser = activeLasers[index];
            if (laser.IsExpired(songTime))
            {
                activeLasers.RemoveAt(index);
                Destroy(laser.gameObject);
                continue;
            }

            laser.SampleInput(StickInput.Read(laser.Side), songTime);
            laser.UpdateView(viewTime, travelTime);
        }
    }

    /// <summary>
    /// Consumes the bar closest to <paramref name="songTime"/> in a lane (a button press).
    /// Chevrons are never hit by a button.
    /// A hold note is not destroyed: it moves to <see cref="HeldNotes"/>.
    /// </summary>
    public bool TryConsumeClosest(int lane, double songTime, double windowSeconds, out NoteView consumed, out double deltaSeconds)
    {
        return TryConsumeClosest(
            note => note.Lane == lane && !note.FlickOnly && !note.IsSpin,
            songTime,
            windowSeconds,
            out consumed,
            out deltaSeconds);
    }

    /// <summary>
    /// Consumes the closest chevron (flick-only note) in a lane that a flick of the stick on
    /// <paramref name="side"/> to <paramref name="stick"/> plays. The direction the stick
    /// pointed in before the flick must not already have matched the note, so a stick
    /// held that way (and only wobbling) does not take it.
    /// </summary>
    public bool TryConsumeClosestFlick(int lane, int side, Vector2 stick, NoteDirection previous, double songTime, double windowSeconds, out NoteView consumed, out double deltaSeconds)
    {
        return TryConsumeClosest(
            note => note.Lane == lane && note.FlickOnly && note.Side == side
                && StickInput.PointsToward(stick, note.Direction)
                && (previous == NoteDirection.None || !StickInput.PointsToward(StickInput.GetDirectionVector(previous), note.Direction)),
            songTime,
            windowSeconds,
            out consumed,
            out deltaSeconds);
    }

    /// <summary>Consumes a note the player let pass: one further behind than its miss window.</summary>
    public bool TryConsumeNextMissed(double songTime, Func<NoteView, double> missWindowSeconds, out NoteView missed)
    {
        missed = null;
        for (int index = 0; index < activeNotes.Count; index++)
        {
            NoteView candidate = activeNotes[index];
            if (songTime - candidate.TargetTime <= missWindowSeconds(candidate))
            {
                continue;
            }

            missed = candidate;
            activeNotes.RemoveAt(index);
            Destroy(candidate.gameObject);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Auto play: consumes a note that has reached the judgement line. Like a hit,
    /// a hold note moves to <see cref="HeldNotes"/> instead of being destroyed.
    /// </summary>
    public bool TryConsumeNextDue(double songTime, out NoteView due)
    {
        due = null;
        for (int index = 0; index < activeNotes.Count; index++)
        {
            NoteView candidate = activeNotes[index];
            if (candidate.TargetTime > songTime)
            {
                continue;
            }

            due = candidate;
            activeNotes.RemoveAt(index);
            Release(candidate);
            return true;
        }

        return false;
    }

    /// <summary>Takes a note out of the approach list without judging it (a spin note the judge has resolved).</summary>
    public void ConsumeActive(NoteView note)
    {
        if (activeNotes.Remove(note))
        {
            Destroy(note.gameObject);
        }
    }

    /// <summary>Takes a hold out of play: its end was reached, or its button was let go.</summary>
    public void ConsumeHeld(NoteView note)
    {
        if (heldNotes.Remove(note))
        {
            Destroy(note.gameObject);
        }
    }

    public bool TryConsumeCompletedLaser(double songTime, out LaserNoteView completed)
    {
        completed = null;
        for (int index = 0; index < activeLasers.Count; index++)
        {
            LaserNoteView candidate = activeLasers[index];
            if (!candidate.IsReadyToJudge(songTime))
            {
                continue;
            }

            completed = candidate;
            activeLasers.RemoveAt(index);
            Destroy(candidate.gameObject);
            return true;
        }

        return false;
    }

    private bool TryConsumeClosest(Predicate<NoteView> matches, double songTime, double windowSeconds, out NoteView consumed, out double deltaSeconds)
    {
        consumed = null;
        deltaSeconds = 0d;
        double closest = double.MaxValue;

        for (int index = 0; index < activeNotes.Count; index++)
        {
            NoteView candidate = activeNotes[index];
            if (!matches(candidate))
            {
                continue;
            }

            double delta = songTime - candidate.TargetTime;
            double distance = Math.Abs(delta);
            if (distance <= windowSeconds && distance < closest)
            {
                closest = distance;
                consumed = candidate;
                deltaSeconds = delta;
            }
        }

        if (consumed == null)
        {
            return false;
        }

        activeNotes.Remove(consumed);
        Release(consumed);
        return true;
    }

    /// <summary>A hit note leaves the approach list: a hold starts being held, anything else is gone.</summary>
    private void Release(NoteView note)
    {
        if (note.IsHold)
        {
            note.BeginHold();
            heldNotes.Add(note);
        }
        else
        {
            Destroy(note.gameObject);
        }
    }

    private static float GetProgress(double targetTime, double songTime, float travelTime)
    {
        return RhythmStageView.ApplyApproachCurve(Mathf.Clamp01(1f - (float)((targetTime - songTime) / travelTime)));
    }

    // Button notes this close in time (seconds) count as hit together.
    private const double SimultaneousSeconds = 0.002d;

    /// <summary>
    /// Whether chart note <paramref name="index"/> is a button note with another button note
    /// in a different lane at the same time (notes are sorted by time). Flick-only and spin
    /// notes have no button, so they neither count nor turn yellow.
    /// </summary>
    private bool IsSimultaneous(int index)
    {
        TapNoteData data = chart.notes[index];
        if (data.IsFlickOnly || data.IsSpin)
        {
            return false;
        }

        int lane = Mathf.Abs(data.lane) % RhythmStageView.LaneCount;
        for (int step = -1; step <= 1; step += 2)
        {
            for (int other = index + step; other >= 0 && other < chart.notes.Length; other += step)
            {
                TapNoteData candidate = chart.notes[other];
                if (Math.Abs(candidate.time - data.time) > SimultaneousSeconds)
                {
                    break;
                }

                if (!candidate.IsFlickOnly && !candidate.IsSpin && Mathf.Abs(candidate.lane) % RhythmStageView.LaneCount != lane)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Spawn(TapNoteData data, bool simultaneous)
    {
        // Chart lanes: 0 = L, 1 = R (upper row), 2 = ZL, 3 = ZR (lower row).
        int lane = Mathf.Abs(data.lane) % RhythmStageView.LaneCount;
        if (data.IsSpin)
        {
            CreateNote().Initialize(lane, data.time, NoteDirection.None, 0d, data.GetStickSide(lane), false, data.spin);
            return;
        }

        // A direction on a bar is its own chevron, judged by the flick alone: the
        // bar (button) and the chevron (stick) are two notes at the same time.
        if (!data.IsFlickOnly)
        {
            CreateNote().Initialize(lane, data.time, NoteDirection.None, data.IsHold ? data.duration : 0d, 0, false, 0, simultaneous);
        }

        if (data.HasDirection)
        {
            CreateNote().Initialize(lane, data.time, data.direction, 0d, data.GetStickSide(lane), true);
        }
    }

    private NoteView CreateNote()
    {
        NoteView note;
        if (notePrefab != null)
        {
            note = Instantiate(notePrefab, noteParent);
        }
        else
        {
            GameObject noteObject = new GameObject("Note");
            noteObject.transform.SetParent(noteParent, false);
            note = noteObject.AddComponent<NoteView>();
        }

        activeNotes.Add(note);
        return note;
    }

    private void SpawnLaser(LaserNoteData data, double songTime, float travelTime)
    {
        LaserNoteView laser;
        if (laserNotePrefab != null)
        {
            laser = Instantiate(laserNotePrefab, noteParent);
        }
        else
        {
            GameObject laserObject = new GameObject("LaserNote");
            laserObject.transform.SetParent(noteParent, false);
            laser = laserObject.AddComponent<LaserNoteView>();
        }

        laser.Initialize(data);
        laser.UpdateView(songTime, travelTime);
        activeLasers.Add(laser);
    }
}
