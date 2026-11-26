using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates lane notes and lasers early enough for them to travel to the judgement line.
/// </summary>
public sealed class NoteSpawner : MonoBehaviour
{
    [SerializeField] private SongClock songClock;
    [SerializeField] private ChartLoader chartLoader;
    [SerializeField] private NoteView notePrefab;
    [SerializeField] private LaserNoteView laserNotePrefab;
    [SerializeField] private Transform noteParent;
    [SerializeField] private float travelTimeSeconds = 1.5f;

    private readonly List<NoteView> activeNotes = new List<NoteView>();
    private readonly List<LaserNoteView> activeLasers = new List<LaserNoteView>();
    private SongChartData chart;
    private int nextNoteIndex;
    private int nextLaserIndex;

    public IReadOnlyList<NoteView> ActiveNotes => activeNotes;
    public IReadOnlyList<LaserNoteView> ActiveLasers => activeLasers;

    private void Start()
    {
        songClock ??= FindFirstObjectByType<SongClock>();
        chartLoader ??= FindFirstObjectByType<ChartLoader>();
        noteParent ??= transform;
        chart = chartLoader != null ? chartLoader.Chart : new SongChartData();

        if (chart == null)
        {
            chart = new SongChartData();
        }
    }

    private void Update()
    {
        if (songClock == null || chart == null)
        {
            return;
        }

        double songTime = songClock.SongTime;
        while (nextNoteIndex < chart.notes.Length && chart.notes[nextNoteIndex].time - songTime <= travelTimeSeconds)
        {
            Spawn(chart.notes[nextNoteIndex]);
            nextNoteIndex++;
        }

        while (nextLaserIndex < chart.lasers.Length && chart.lasers[nextLaserIndex].StartTime - songTime <= travelTimeSeconds)
        {
            SpawnLaser(chart.lasers[nextLaserIndex]);
            nextLaserIndex++;
        }

        for (int index = activeNotes.Count - 1; index >= 0; index--)
        {
            NoteView note = activeNotes[index];
            float progress = Mathf.InverseLerp(
                (float)(note.TargetTime - travelTimeSeconds),
                (float)note.TargetTime,
                (float)songTime);
            note.UpdateView(progress);
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
            laser.UpdateView(songTime);
        }
    }

    /// <summary>
    /// Consumes the note closest to <paramref name="songTime"/> in a lane that a button
    /// press can hit: a plain note, or a directional note asking for the direction the
    /// stick already points in (<paramref name="stickDirection"/>, None if it is centred).
    /// </summary>
    public bool TryConsumeClosest(int lane, NoteDirection stickDirection, double songTime, double windowSeconds, out NoteView consumed, out double deltaSeconds)
    {
        return TryConsumeClosest(
            note => note.Lane == lane && (!note.IsDirectional || note.Direction == stickDirection),
            songTime,
            windowSeconds,
            out consumed,
            out deltaSeconds);
    }

    /// <summary>
    /// Consumes the directional note closest to <paramref name="songTime"/> in a lane
    /// that asks for <paramref name="direction"/>.
    /// </summary>
    public bool TryConsumeClosestDirectional(int lane, NoteDirection direction, double songTime, double windowSeconds, out NoteView consumed, out double deltaSeconds)
    {
        return TryConsumeClosest(
            note => note.IsDirectional && note.Direction == direction && note.Lane == lane,
            songTime,
            windowSeconds,
            out consumed,
            out deltaSeconds);
    }

    public bool TryConsumeNextMissed(double songTime, double missWindowSeconds, out NoteView missed)
    {
        missed = null;
        for (int index = 0; index < activeNotes.Count; index++)
        {
            NoteView candidate = activeNotes[index];
            if (songTime - candidate.TargetTime <= missWindowSeconds)
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
        Destroy(consumed.gameObject);
        return true;
    }

    private void Spawn(TapNoteData data)
    {
        // Chart lanes: 0 = L, 1 = R (upper row), 2 = ZL, 3 = ZR (lower row).
        int lane = Mathf.Abs(data.lane) % RhythmStageView.LaneCount;
        NoteDirection direction = Enum.IsDefined(typeof(NoteDirection), data.direction) ? data.direction : NoteDirection.None;
        NoteView note;
        if (notePrefab != null)
        {
            note = Instantiate(notePrefab, noteParent);
        }
        else
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            primitive.transform.SetParent(noteParent);
            note = primitive.AddComponent<NoteView>();
        }

        note.Initialize(lane, data.time, direction);
        activeNotes.Add(note);
    }

    private void SpawnLaser(LaserNoteData data)
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

        laser.Initialize(data, travelTimeSeconds);
        laser.UpdateView(songClock.SongTime);
        activeLasers.Add(laser);
    }
}
