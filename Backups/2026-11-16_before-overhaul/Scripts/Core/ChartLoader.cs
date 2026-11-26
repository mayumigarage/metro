using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stick direction a lane note asks for. None (the JSON default when the field
/// is omitted) is a plain tap note hit with the lane button.
/// </summary>
public enum NoteDirection
{
    None,
    Up,
    Down,
    Left,
    Right
}

[Serializable]
public sealed class TapNoteData
{
    [Range(0, 3)] public int lane;
    public double time;
    public NoteDirection direction;
}

/// <summary>One control point of a laser: where it is in the stick's -1..1 range at a time.</summary>
[Serializable]
public sealed class LaserPointData
{
    public double time;
    [Range(-1f, 1f)] public float x;
    [Range(-1f, 1f)] public float y;
}

/// <summary>
/// A laser drawn in the space between the upper and lower lanes. It moves in a
/// straight line between consecutive points and is followed with the side's stick.
/// </summary>
[Serializable]
public sealed class LaserNoteData
{
    [Range(0, 1)] public int side;
    public LaserPointData[] points = Array.Empty<LaserPointData>();

    public double StartTime => points[0].time;
    public double EndTime => points[points.Length - 1].time;
}

[Serializable]
public sealed class SongChartData
{
    public string title;
    public float bpm = 120f;
    public TapNoteData[] notes = Array.Empty<TapNoteData>();
    public LaserNoteData[] lasers = Array.Empty<LaserNoteData>();
}

/// <summary>
/// Loads the small, JsonUtility-compatible chart format used by this prototype.
/// Time is measured in seconds from the DSP song start and lane is 0..3.
/// </summary>
public sealed class ChartLoader : MonoBehaviour
{
    [SerializeField] private TextAsset chartJson;

    public SongChartData Chart { get; private set; }

    private void Awake()
    {
        Load();
    }

    public SongChartData Load()
    {
        if (chartJson == null || string.IsNullOrWhiteSpace(chartJson.text))
        {
            Chart = new SongChartData();
            return Chart;
        }

        try
        {
            Chart = JsonUtility.FromJson<SongChartData>(chartJson.text) ?? new SongChartData();
        }
        catch (Exception exception)
        {
            Debug.LogError($"Chart JSON could not be loaded: {exception.Message}", this);
            Chart = new SongChartData();
        }

        if (Chart.notes == null)
        {
            Chart.notes = Array.Empty<TapNoteData>();
        }

        Chart.lasers = GetPlayableLasers(Chart.lasers);

        Array.Sort(Chart.notes, (left, right) => left.time.CompareTo(right.time));
        Array.Sort(Chart.lasers, (left, right) => left.StartTime.CompareTo(right.StartTime));
        return Chart;
    }

    /// <summary>
    /// Sorts each laser's points by time and drops lasers that cannot be drawn
    /// (fewer than two points, e.g. entries still in the old direction-based format).
    /// </summary>
    private LaserNoteData[] GetPlayableLasers(LaserNoteData[] lasers)
    {
        List<LaserNoteData> playable = new List<LaserNoteData>();
        if (lasers == null)
        {
            return playable.ToArray();
        }

        foreach (LaserNoteData laser in lasers)
        {
            if (laser == null || laser.points == null || laser.points.Length < 2)
            {
                Debug.LogWarning("Chart laser skipped: it needs at least two points.", this);
                continue;
            }

            Array.Sort(laser.points, (left, right) => left.time.CompareTo(right.time));
            playable.Add(laser);
        }

        return playable.ToArray();
    }
}
