using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Stick direction a lane note asks for. None (the JSON default when the field
/// is omitted) is a plain note hit with the lane button. The numbers are the
/// chart's integers, so the four diagonals come after the original four.
/// </summary>
public enum NoteDirection
{
    None,
    Up,
    Down,
    Left,
    Right,
    UpLeft,
    UpRight,
    DownLeft,
    DownRight
}

[Serializable]
public sealed class TapNoteData
{
    [Range(0, 3)] public int lane;
    public double time;
    public NoteDirection direction;

    /// <summary>Seconds the lane button has to stay down after the hit. 0 (or omitted) = not a hold.</summary>
    public double duration;

    /// <summary>
    /// Stick that plays the direction: "L" or "R". Omitted = the lane's own side
    /// (L / ZL = left, R / ZR = right). The note's arrow takes that stick's colour.
    /// </summary>
    public string stick;

    /// <summary>A direction with no button: only the flick of the stick is judged.</summary>
    public bool flickOnly;

    /// <summary>
    /// A spin note: the stick has to be turned around. 0 / omitted = no, 1 = counter-clockwise,
    /// 2 = clockwise. Like a flick-only note it has no button; a spin note has no direction
    /// and cannot be held.
    /// </summary>
    public int spin;

    public bool IsSpin => spin == SpinCounterClockwise || spin == SpinClockwise;

    public const int SpinCounterClockwise = 1;
    public const int SpinClockwise = 2;

    public bool HasDirection => !IsSpin && direction > NoteDirection.None && direction <= NoteDirection.DownRight;

    /// <summary>Flick-only only means something on a note that has a direction.</summary>
    public bool IsFlickOnly => flickOnly && HasDirection;

    /// <summary>A flick-only note cannot be held: there is no button to keep down.</summary>
    public bool IsHold => duration > 0d && !IsFlickOnly && !IsSpin;
    public double EndTime => IsHold ? time + duration : time;

    /// <summary>The stick that plays this note's direction: 0 = left, 1 = right.</summary>
    public int GetStickSide(int lane)
    {
        if (!string.IsNullOrEmpty(stick))
        {
            char first = char.ToUpperInvariant(stick.Trim()[0]);
            if (first == 'L')
            {
                return 0;
            }

            if (first == 'R')
            {
                return 1;
            }
        }

        return RhythmStageView.GetStickSide(lane);
    }
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
    public string artist;
    public float bpm = 120f;

    /// <summary>Song time (seconds) of the first beat: where the tunnel frames and the chart editor's grid start.</summary>
    public double offset;

    public int level = 1;

    /// <summary>Difficulty grade, 0-3: Local, Rapid, Express, Limited Express (see <see cref="SongDifficulty"/>). Omitted = Local.</summary>
    [Range(0, 3)] public int difficulty;

    /// <summary>
    /// Genre folder of the song select: a <see cref="SongGenre"/> id ("pops", "vocaloid",
    /// "game", "original") or its letter. Omitted or unknown = original.
    /// </summary>
    public string genre;

    /// <summary>Optional music: path of an AudioClip under a Resources folder, without extension.</summary>
    public string audio;

    /// <summary>Seconds the music is shifted later on the song clock (negative = earlier): the clip's own time 0 sits at this song time.</summary>
    public double audioOffset;

    /// <summary>Optional cover art (表紙): path of a Texture2D under a Resources folder, without extension.</summary>
    public string jacket;

    public TapNoteData[] notes = Array.Empty<TapNoteData>();
    public LaserNoteData[] lasers = Array.Empty<LaserNoteData>();

    /// <summary>
    /// Guide beats: song times to tap along to, a charting aid made by the chart editor from the song
    /// (<c>ChartGuideBeats</c>). Shown and sounded in editor previews only; never judged.
    /// </summary>
    public double[] guides = Array.Empty<double>();

    /// <summary>Name of the chart asset; identifies the song in the saved best scores.</summary>
    [NonSerialized] public string id;

    public SongGenre Genre => SongGenre.Find(genre);

    public SongDifficulty Difficulty => SongDifficulty.Get(difficulty);

    public int HoldCount
    {
        get
        {
            int count = 0;
            foreach (TapNoteData note in notes)
            {
                if (note.IsHold)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Directions placed on a bar: the chevron is judged apart from the bar.</summary>
    public int AttachedFlickCount
    {
        get
        {
            int count = 0;
            foreach (TapNoteData note in notes)
            {
                if (note.HasDirection && !note.IsFlickOnly)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// How many judgements the chart produces: a hold counts twice (start and end), and
    /// so does a bar with a direction (button and flick).
    /// </summary>
    public int JudgementCount => notes.Length + HoldCount + AttachedFlickCount + lasers.Length;

    /// <summary>Song time at which the last note or laser ends.</summary>
    public double EndTime
    {
        get
        {
            double end = 0d;
            foreach (TapNoteData note in notes)
            {
                end = Math.Max(end, note.EndTime);
            }

            foreach (LaserNoteData laser in lasers)
            {
                end = Math.Max(end, laser.EndTime);
            }

            return end;
        }
    }
}

/// <summary>
/// The song library. Every JSON TextAsset in a Resources/Charts folder is a song, and in a
/// build every JSON file in <see cref="UserChartsDirectory"/> too (the player's own charts),
/// in the small JsonUtility-compatible chart format of this prototype: time is in
/// seconds from the DSP song start and lane is 0..3.
/// </summary>
public sealed class ChartLoader : MonoBehaviour
{
    public const string ResourcesFolder = "Charts";

    /// <summary>
    /// The folder the chart editor saves into: the project's Resources/Charts in the Editor,
    /// otherwise a folder next to the saved data.
    /// </summary>
    public static string UserChartsDirectory
    {
        get
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "Resources", ResourcesFolder);
#else
            return Path.Combine(Application.persistentDataPath, ResourcesFolder);
#endif
        }
    }

    private readonly List<SongChartData> songs = new List<SongChartData>();

    /// <summary>All songs, easiest first.</summary>
    public IReadOnlyList<SongChartData> Songs => songs;

    public int SelectedIndex { get; private set; }

    /// <summary>The chart being played: the selected song, mirrored if that was asked for.</summary>
    public SongChartData Chart { get; private set; } = new SongChartData();

    private void Awake()
    {
        LoadLibrary();
        Select(0, false);
    }

    public void LoadLibrary()
    {
        songs.Clear();
        foreach (TextAsset asset in Resources.LoadAll<TextAsset>(ResourcesFolder))
        {
            SongChartData chart = Parse(asset.text, asset.name);
            if (chart != null)
            {
                songs.Add(chart);
            }
        }

#if !UNITY_EDITOR
        // In the Editor the user's folder is the Resources folder read above.
        LoadUserCharts();
#endif

        songs.Sort((left, right) => left.level != right.level
            ? left.level.CompareTo(right.level)
            : string.CompareOrdinal(left.title, right.title));

        if (songs.Count == 0)
        {
            Debug.LogWarning($"No charts found in Resources/{ResourcesFolder}.", this);
        }
    }

#if !UNITY_EDITOR
    private void LoadUserCharts()
    {
        if (!Directory.Exists(UserChartsDirectory))
        {
            return;
        }

        foreach (string path in Directory.GetFiles(UserChartsDirectory, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            SongChartData chart;
            try
            {
                chart = Parse(File.ReadAllText(path), id);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Chart '{id}' could not be read: {exception.Message}", this);
                continue;
            }

            if (chart != null)
            {
                // A user chart with a bundled chart's name replaces it.
                songs.RemoveAll(song => string.Equals(song.id, id, StringComparison.OrdinalIgnoreCase));
                songs.Add(chart);
            }
        }
    }
#endif

    /// <summary>Library indices of the songs in <paramref name="genre"/>, in library order (easiest first).</summary>
    public List<int> GetSongIndices(SongGenre genre)
    {
        List<int> indices = new List<int>();
        for (int index = 0; index < songs.Count; index++)
        {
            if (songs[index].Genre == genre)
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    /// <summary>
    /// The songs of <paramref name="genre"/> as the song select shows them: charts with the
    /// same title and artist (ignoring case) are the difficulties of one song. Each entry
    /// holds the library indices of one song's charts, lowest difficulty grade first; the
    /// songs come in the library order of their first chart.
    /// </summary>
    public List<List<int>> GetSongGroups(SongGenre genre)
    {
        List<List<int>> groups = new List<List<int>>();
        Dictionary<string, List<int>> byKey = new Dictionary<string, List<int>>();
        foreach (int index in GetSongIndices(genre))
        {
            string key = GetSongKey(songs[index]);
            if (!byKey.TryGetValue(key, out List<int> group))
            {
                group = new List<int>();
                byKey.Add(key, group);
                groups.Add(group);
            }

            group.Add(index);
        }

        foreach (List<int> group in groups)
        {
            group.Sort((left, right) => songs[left].Difficulty.Index != songs[right].Difficulty.Index
                ? songs[left].Difficulty.Index.CompareTo(songs[right].Difficulty.Index)
                : left.CompareTo(right));
        }

        return groups;
    }

    /// <summary>How many songs (not charts) <paramref name="genre"/> has: its station count.</summary>
    public int GetSongCount(SongGenre genre)
    {
        return GetSongGroups(genre).Count;
    }

    private static string GetSongKey(SongChartData chart)
    {
        string title = (chart.title ?? string.Empty).Trim().ToUpperInvariant();
        string artist = (chart.artist ?? string.Empty).Trim().ToUpperInvariant();
        return title + "\n" + artist;
    }

    public SongChartData Select(int index, bool mirror)
    {
        if (songs.Count == 0)
        {
            SelectedIndex = 0;
            Chart = new SongChartData();
            return Chart;
        }

        SelectedIndex = Mathf.Clamp(index, 0, songs.Count - 1);
        Chart = mirror ? CreateMirrored(songs[SelectedIndex]) : songs[SelectedIndex];
        return Chart;
    }

    /// <summary>
    /// Drops the angle brackets from typed text: the Text / TextMeshPro that show it would
    /// read them as rich-text tags (&lt;br&gt; breaks the line, &lt;size&gt; resizes it).
    /// </summary>
    public static string RemoveTagBrackets(string text)
    {
        return text?.Replace("<", string.Empty).Replace(">", string.Empty);
    }

    /// <summary>Parses and normalises one chart. Null if the JSON cannot be read.</summary>
    public static SongChartData Parse(string json, string id)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        SongChartData chart;
        try
        {
            chart = JsonUtility.FromJson<SongChartData>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Chart '{id}' could not be loaded: {exception.Message}");
            return null;
        }

        if (chart == null)
        {
            return null;
        }

        chart.id = id;
        chart.title = RemoveTagBrackets(chart.title);
        chart.artist = RemoveTagBrackets(chart.artist);
        if (string.IsNullOrWhiteSpace(chart.title))
        {
            chart.title = id;
        }

        if (chart.notes == null)
        {
            chart.notes = Array.Empty<TapNoteData>();
        }

        chart.lasers = GetPlayableLasers(chart.lasers, id);
        chart.guides ??= Array.Empty<double>();
        Array.Sort(chart.guides);
        Array.Sort(chart.notes, (left, right) => left.time.CompareTo(right.time));
        Array.Sort(chart.lasers, (left, right) => left.StartTime.CompareTo(right.StartTime));
        return chart;
    }

    /// <summary>
    /// Sorts each laser's points by time and drops lasers that cannot be drawn
    /// (fewer than two points, e.g. entries still in the old direction-based format).
    /// </summary>
    private static LaserNoteData[] GetPlayableLasers(LaserNoteData[] lasers, string id)
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
                Debug.LogWarning($"Chart '{id}': laser skipped, it needs at least two points.");
                continue;
            }

            Array.Sort(laser.points, (left, right) => left.time.CompareTo(right.time));
            playable.Add(laser);
        }

        return playable.ToArray();
    }

    /// <summary>A copy with left and right swapped: lanes, flick directions, sticks, laser sides and laser x.</summary>
    private static SongChartData CreateMirrored(SongChartData source)
    {
        SongChartData mirrored = new SongChartData
        {
            id = source.id,
            title = source.title,
            artist = source.artist,
            bpm = source.bpm,
            offset = source.offset,
            level = source.level,
            difficulty = source.difficulty,
            genre = source.genre,
            audio = source.audio,
            audioOffset = source.audioOffset,
            jacket = source.jacket,
            guides = source.guides,
            notes = new TapNoteData[source.notes.Length],
            lasers = new LaserNoteData[source.lasers.Length]
        };

        for (int index = 0; index < source.notes.Length; index++)
        {
            TapNoteData note = source.notes[index];
            mirrored.notes[index] = new TapNoteData
            {
                // 0 <-> 1 and 2 <-> 3: the other button of the same row.
                lane = (Mathf.Abs(note.lane) % RhythmStageView.LaneCount) ^ 1,
                time = note.time,
                duration = note.duration,
                direction = StickInput.Mirror(note.direction),
                flickOnly = note.flickOnly,
                spin = note.spin == TapNoteData.SpinCounterClockwise ? TapNoteData.SpinClockwise :
                    note.spin == TapNoteData.SpinClockwise ? TapNoteData.SpinCounterClockwise : 0,
                // An explicit stick swaps too; an omitted one follows the mirrored lane by itself.
                stick = string.IsNullOrEmpty(note.stick) ? note.stick : note.GetStickSide(note.lane) == 0 ? "R" : "L"
            };
        }

        for (int index = 0; index < source.lasers.Length; index++)
        {
            LaserNoteData laser = source.lasers[index];
            LaserNoteData copy = new LaserNoteData
            {
                side = 1 - Mathf.Clamp(laser.side, 0, 1),
                points = new LaserPointData[laser.points.Length]
            };

            for (int point = 0; point < laser.points.Length; point++)
            {
                copy.points[point] = new LaserPointData
                {
                    time = laser.points[point].time,
                    x = -laser.points[point].x,
                    y = laser.points[point].y
                };
            }

            mirrored.lasers[index] = copy;
        }

        return mirrored;
    }
}
