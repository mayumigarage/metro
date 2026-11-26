using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MetroEd;
using UnityEngine;

/// <summary>
/// The chart editor screen of the game (title screen > CHART EDITOR): edits JSON charts on a vertical
/// timeline (time runs upward, like the notes flying toward the player).
///
/// Columns, left hand to right hand: left stick laser (X, Y), lanes L / R / ZL / ZR,
/// right stick laser (X, Y). Notes snap to the beat grid built from the chart's bpm and
/// offset. Space plays the music from the cursor with a clap on every note; P plays the
/// chart from the cursor in the real game (Game view). Edits go through Unity's Undo.
/// </summary>
public sealed partial class ChartEditorScreen : MonoBehaviour
{
    private const float HeaderHeight = 20f;
    private const float StatusHeight = 20f;
    private const float RulerWidth = 58f;
    private const float WaveWidth = 40f;
    private const float LaneWidth = 58f;
    private const float LaserColumnWidth = 62f;
    private const float MinPixelsPerSecond = 40f;
    private const float MaxPixelsPerSecond = 2400f;
    private static readonly float[] LaserGridSteps = { 1f, 0.5f, 0.25f, 0.2f, 0.125f, 0.1f, 0.05f };
    private int laserGridIndex = 2;
    private float timelineZoomStart;
    private const int WaveformRate = 200;

    // Grid fineness: how many steps one beat (a quarter note at the chart's bpm) is cut into.
    private static readonly int[] DivisionPresets = { 1, 2, 3, 4, 6, 8, 12, 16 };
    private const int MaxDivisions = 64;
    private const float DirectionDragThreshold = 14f;
    private static readonly string[] LaneNames = { "L", "R", "ZL", "ZR" };
    // Columns left to right: L, R, ZL, ZR.
    private static readonly int[] LaneOrder = { 0, 1, 2, 3 };
    private static readonly string[] StickChoices = { "レーン準拠", "左スティック", "右スティック" };
    private static readonly NoteDirection[] PadLayout =
    {
        NoteDirection.UpLeft, NoteDirection.Up, NoteDirection.UpRight,
        NoteDirection.Left, NoteDirection.None, NoteDirection.Right,
        NoteDirection.DownLeft, NoteDirection.Down, NoteDirection.DownRight
    };

    private static readonly Color Background = new Color(0.12f, 0.12f, 0.13f);
    private static readonly Color LaneColorA = new Color(0.17f, 0.17f, 0.19f);
    private static readonly Color LaneColorB = new Color(0.15f, 0.15f, 0.17f);
    private static readonly Color TapColor = new Color(0.95f, 0.95f, 0.95f);
    private static readonly Color HoldColor = new Color(1f, 0.82f, 0.2f);
    private static readonly Color SelectColor = new Color(1f, 0.55f, 0.1f);
    private static readonly Color CursorColor = new Color(1f, 0.3f, 0.3f);

    private enum ColumnKind
    {
        Ruler,
        Wave,
        Lane,
        LaserX,
        LaserY
    }

    private struct Column
    {
        public ColumnKind Kind;
        public int Index; // lane for Lane, stick side for LaserX / LaserY
        public Rect Rect;
        public string Label;
    }

    private enum DragMode
    {
        None,
        Scrub,
        Pan,
        MoveAudio,
        MoveNotes,
        ResizeHold,
        MovePoint,
        BoxSelect,
        PadPoint,
        SetDirection,
        Erase,
        Orbit,
        PanView,
        Dolly
    }

    private ChartEditorDocument document;
    private string chartPath;
    private string savedJson;
    private float pixelsPerSecond = 240f;
    private double viewTime = -0.5d;
    private double cursorTime;
    private int gridDivisions = 4;
    private bool snapEnabled = true;
    private NoteDirection placeDirection;
    private int placeStick;
    private int placeSpin = TapNoteData.SpinCounterClockwise;
    private bool clapEnabled = true;
    private float musicVolume = 0.8f;
    private float clapVolume = 0.6f;
    private bool freeLaserValues;
    private float previewLookAhead = 1.2f;
    private bool returnOnStop = true;
    private bool musicEnabled = true;
    private double playStartTime;
    private int jumpMeasure = 1;
    private int scrubResume;
    private bool previewWasRunning;

    // Not serialized: a domain reload would otherwise bring the selection back as copies
    // of the notes (no longer the chart's own objects) and nulls back as empty values.
    [NonSerialized] private readonly List<TapNoteData> selectedNotes = new List<TapNoteData>();
    [NonSerialized] private LaserNoteData selectedLaser;
    [NonSerialized] private int selectedPoint = -1;
    // Whole lasers taken by a box selection (or Ctrl+click) together with notes; selectedLaser is the one being shaped.
    [NonSerialized] private readonly List<LaserNoteData> selectedLasers = new List<LaserNoteData>();
    // Several laser points at once (Ctrl+click or a box that takes only part of a laser); they move together.
    [NonSerialized] private readonly HashSet<LaserPointData> selectedPoints = new HashSet<LaserPointData>();
    [NonSerialized] private readonly Dictionary<LaserPointData, (double time, float x, float y)> pointOrigins = new Dictionary<LaserPointData, (double time, float x, float y)>();
    [NonSerialized] private double pointDragStartTime;
    [NonSerialized] private float pointDragStartValue;

    [NonSerialized] private ChartEditorPlayback playback;
    [NonSerialized] private AudioClip audioClip;
    [NonSerialized] private string loadedAudioPath;
    [NonSerialized] private float[] waveform;
    [NonSerialized] private string waveformNote;
    [NonSerialized] private bool dirty;

    private static readonly List<TapNoteData> clipboardNotes = new List<TapNoteData>();
    private static readonly List<LaserNoteData> clipboardLasers = new List<LaserNoteData>();
    private static double clipboardOrigin;

    [NonSerialized] private string[] chartPaths = new string[0];
    [NonSerialized] private string[] chartNames = new string[0];
    [NonSerialized] private readonly List<Column> columns = new List<Column>();
    [NonSerialized] private float timelineHeight;
    [NonSerialized] private float gridLeft;
    [NonSerialized] private float gridRight;

    // Drag state
    [NonSerialized] private DragMode drag;
    [NonSerialized] private double audioDragStartTime;
    [NonSerialized] private double audioDragStartOffset;
    [NonSerialized] private int dragUndoGroup;
    [NonSerialized] private double dragStartMouseTime;
    [NonSerialized] private float dragStartMouseY;
    [NonSerialized] private double dragStartViewTime;
    [NonSerialized] private int dragStartSlot;
    [NonSerialized] private TapNoteData dragAnchor;
    [NonSerialized] private readonly Dictionary<TapNoteData, (double time, int slot)> dragOrigins = new Dictionary<TapNoteData, (double, int)>();
    [NonSerialized] private int dragAxis; // 0 = x, 1 = y (laser columns)
    [NonSerialized] private Vector2 boxStart;
    [NonSerialized] private Rect boxRect;
    [NonSerialized] private string hoverHint = string.Empty;

    // Direction drag (drag out of a note to give it a direction; right button = flick-only).
    [NonSerialized] private Vector2 dirOrigin;
    [NonSerialized] private Vector2 dirMouse;
    [NonSerialized] private int dirLane;
    [NonSerialized] private double dirTime;
    [NonSerialized] private bool dirForceFlick;
    [NonSerialized] private bool dirBaseFlick;
    [NonSerialized] private NoteDirection dirBaseDirection;
    [NonSerialized] private bool dirPendingDelete;
    [NonSerialized] private bool dirDragged;

    private SongChartData Chart => document.chart;

    private double BeatSeconds => 60d / Math.Max(1f, Chart.bpm);

    private int Division => Mathf.Clamp(gridDivisions, 1, MaxDivisions);

    /// <summary>Length of one grid step in seconds: one beat of the chart's bpm cut into <see cref="Division"/> parts.</summary>
    private double GridStep => BeatSeconds / Division;

    /// <summary>Steps the grid one preset coarser (-1) or finer (+1).</summary>
    private void StepDivision(int direction)
    {
        int current = Division;
        if (direction > 0)
        {
            gridDivisions = DivisionPresets.FirstOrDefault(value => value > current);
            if (gridDivisions == 0)
            {
                gridDivisions = current;
            }
        }
        else
        {
            gridDivisions = DivisionPresets.LastOrDefault(value => value < current);
            if (gridDivisions == 0)
            {
                gridDivisions = current;
            }
        }
    }

    private static string DivisionLabel(int division)
    {
        return division == 1 ? "1拍" : "1/" + division + "拍";
    }

    private string ChartName => string.IsNullOrEmpty(chartPath) ? "(未保存)" : Path.GetFileNameWithoutExtension(chartPath);

    private GameFlow flow;
    private string notice = string.Empty;
    private double noticeUntil;
    private bool showChartList;
    private Vector2 chartListScroll;

    /// <summary>Where the editor looks for charts: the project's chart folder in the Editor, otherwise a folder next to the saved data.</summary>
    private static string ChartsDirectory
    {
        get
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "Resources", "Charts");
#else
            return Path.Combine(Application.persistentDataPath, "Charts");
#endif
        }
    }

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Shows the editor (the first call also builds it).</summary>
    public void Open(GameFlow gameFlow)
    {
        flow = gameFlow;
        if (document == null)
        {
            document = ScriptableObject.CreateInstance<ChartEditorDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            playback = new ChartEditorPlayback();
            LoadLayout();
            Undo.undoRedoPerformed += OnUndoRedo;
            Directory.CreateDirectory(ChartsDirectory);
            RefreshChartList();
            if (chartPaths.Length > 0)
            {
                Load(chartPaths[0]);
            }
            else
            {
                NewChart(false);
            }
        }

        openedFrame = Time.frameCount;
        enabled = true;
    }

    // The key that opened the editor (E on the title, Enter on a menu) must not act in it too.
    [NonSerialized] private int openedFrame = -1;

    private void OnDestroy()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        playback?.Dispose();
        Release3DView();
        if (document != null)
        {
            Destroy(document);
        }
    }

    private void OnDisable()
    {
        playback?.Stop();
    }

    /// <summary>Leaves the editor for the title screen, after asking about unsaved changes.</summary>
    private void ExitToTitle()
    {
        playback.Stop();
        if (ConfirmDiscard())
        {
            flow.CloseChartEditor();
        }
    }

    private void OnUndoRedo()
    {
        // The undo system rebuilt the chart's objects, so references held here are stale.
        ClearSelection();
        UpdateDirty();
    }

    private void Repaint()
    {
    }

    private void Notify(string text)
    {
        notice = text;
        noticeUntil = Time.realtimeSinceStartupAsDouble + 2.5d;
    }

    private void Update()
    {
        if (flow != null)
        {
            flow.SetGameViewport(show3D ? gameViewport : new Rect(0f, 0f, 1f, 1f));
            if (flow.IsEmbeddedPreview)
            {
                // The game is playing: the playhead follows its clock (held at the start during the run-up).
                previewWasRunning = true;
                cursorTime = Math.Max(playStartTime, flow.PreviewSongTime);
                FollowCursor();
                return;
            }

            if (previewWasRunning)
            {
                // The preview ended by itself.
                previewWasRunning = false;
                if (returnOnStop)
                {
                    cursorTime = playStartTime;
                    RevealCursor();
                }
            }
        }

        if (playback == null || !playback.IsPlaying)
        {
            return;
        }

        playback.MusicEnabled = musicEnabled;
        playback.ClapEnabled = clapEnabled;
        playback.ClapVolume = clapVolume;
        playback.Update();
        cursorTime = playback.SongTime;
        // Keep the playhead in the lower part of the view.
        FollowCursor();

        double end = Math.Max(Chart.EndTime, audioClip != null ? audioClip.length : 0d) + 2d;
        if (cursorTime > end)
        {
            StopPlayback();
        }
    }

    // ------------------------------------------------------------------ files

    private void RefreshChartList()
    {
        chartPaths = Directory.Exists(ChartsDirectory)
            ? Directory.GetFiles(ChartsDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : new string[0];
        chartNames = chartPaths.Select(Path.GetFileNameWithoutExtension).ToArray();
    }

    private void Load(string path)
    {
        playback?.Stop();
        string json = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        SongChartData chart = ChartLoader.Parse(json, Path.GetFileNameWithoutExtension(path));
        if (chart == null)
        {
            chart = new SongChartData { title = Path.GetFileNameWithoutExtension(path) };
        }

        Undo.ClearUndo(document);
        document.chart = chart;
        chartPath = path;
        Normalize();
        savedJson = ChartJsonWriter.Write(Chart);
        ClearSelection();
        cursorTime = 0d;
        viewTime = -0.5d;
        followOnLayout = true;
        UpdateDirty();
    }

    private void Save()
    {
        if (string.IsNullOrEmpty(chartPath))
        {
            string path = NativeDialogs.SaveFile("譜面を保存", NativeDialogs.JsonFilter, ChartsDirectory, "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            chartPath = path;
        }

        Normalize();
        string json = ChartJsonWriter.Write(Chart);
        File.WriteAllText(chartPath, json, new UTF8Encoding(false));
        savedJson = json;
        RefreshChartList();
        UpdateDirty();
        Notify($"{ChartName} を保存しました");
    }

    /// <summary>Renames the chart's file (and its .meta) in place; the open document and its edits stay as they are.</summary>
    private bool RenameChart(string newName)
    {
        newName = (newName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(chartPath) || newName.Length == 0)
        {
            Notify("名前を入力してください");
            return false;
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Notify("ファイル名に使えない文字が含まれています");
            return false;
        }

        if (newName == ChartName)
        {
            return true;
        }

        string target = Path.Combine(Path.GetDirectoryName(chartPath), newName + ".json");
        if (File.Exists(target))
        {
            Notify($"{newName} は既に存在します");
            return false;
        }

        File.Move(chartPath, target);
        if (File.Exists(chartPath + ".meta"))
        {
            File.Move(chartPath + ".meta", target + ".meta");
        }

        chartPath = target;
        RefreshChartList();
        Notify($"{newName} に名前を変更しました");
        return true;
    }

    /// <summary>Writes the chart to a file of the user's choosing; the open document stays as it is.</summary>
    private void ExportChart()
    {
        Normalize();
        string suggested = string.IsNullOrWhiteSpace(Chart.title) ? "new_chart" : Chart.title.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            suggested = suggested.Replace(invalid, '_');
        }

        string start = string.IsNullOrEmpty(chartPath) ? ChartsDirectory : Path.GetDirectoryName(chartPath);
        string path = NativeDialogs.SaveFile("譜面を書き出し", NativeDialogs.JsonFilter, Path.Combine(start, suggested + ".json"), "json");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        File.WriteAllText(path, ChartJsonWriter.Write(Chart), new UTF8Encoding(false));
        Notify($"{Path.GetFileName(path)} に書き出しました");
    }

    private void NewChart()
    {
        NewChart(true);
    }

    private void NewChart(bool confirm)
    {
        if (confirm && !ConfirmDiscard())
        {
            return;
        }

        playback?.Stop();
        Undo.ClearUndo(document);
        document.chart = new SongChartData { title = "New Chart", bpm = 120f, level = 1 };
        chartPath = null;
        Normalize();
        savedJson = ChartJsonWriter.Write(Chart);
        ClearSelection();
        cursorTime = 0d;
        viewTime = -0.5d;
        followOnLayout = true;
        UpdateDirty();
    }

    /// <summary>True when it is fine to throw the current edits away (saved, or the user said so).</summary>
    private bool ConfirmDiscard()
    {
        if (!dirty)
        {
            return true;
        }

        switch (NativeDialogs.AskSave("未保存の変更", $"{ChartName} に保存していない変更があります。保存しますか？"))
        {
            case NativeDialogs.Answer.Yes:
                Save();
                return !dirty;
            case NativeDialogs.Answer.No:
                return true;
            default:
                return false;
        }
    }

    private void UpdateDirty()
    {
        dirty = savedJson == null || ChartJsonWriter.Write(Chart) != savedJson;
    }

    // ------------------------------------------------------------------ editing helpers

    /// <summary>One undoable edit: records, changes, re-sorts.</summary>
    private void Modify(string undoName, Action change)
    {
        Undo.RecordObject(document, undoName);
        change();
        Normalize();
        UpdateDirty();
        Repaint();
    }

    /// <summary>One step of a drag; the whole drag is collapsed into one undo entry on release.</summary>
    private void DragModify(string undoName, Action change)
    {
        Undo.RecordObject(document, undoName);
        change();
        UpdateDirty();
    }

    private void BeginDrag(DragMode mode, string undoName)
    {
        drag = mode;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(undoName);
        dragUndoGroup = Undo.GetCurrentGroup();
    }

    private void EndDrag()
    {
        if (drag != DragMode.None && drag != DragMode.Scrub && drag != DragMode.Pan && drag != DragMode.BoxSelect
            && drag != DragMode.Orbit && drag != DragMode.PanView && drag != DragMode.Dolly)
        {
            Undo.RecordObject(document, "Edit chart");
            Normalize();
            Undo.CollapseUndoOperations(dragUndoGroup, document);
            UpdateDirty();
        }

        if (drag == DragMode.Scrub && scrubResume != 0)
        {
            // The timeline was clicked or dragged during playback: carry on from the new spot.
            int resume = scrubResume;
            scrubResume = 0;
            drag = DragMode.None;
            if (resume == 1)
            {
                PlayFrom(cursorTime);
            }
            else if (show3D)
            {
                StartSplitPreview();
            }
        }

        drag = DragMode.None;
        dragOrigins.Clear();
        pointOrigins.Clear();
    }

    /// <summary>Sorts notes, lasers and laser points by time, keeping the selected point selected.</summary>
    private void Normalize()
    {
        SongChartData chart = Chart;
        chart.notes ??= new TapNoteData[0];
        chart.lasers ??= new LaserNoteData[0];
        Array.Sort(chart.notes, (left, right) => left.time != right.time ? left.time.CompareTo(right.time) : left.lane.CompareTo(right.lane));

        LaserPointData selected = selectedLaser != null && selectedPoint >= 0 && selectedPoint < selectedLaser.points.Length
            ? selectedLaser.points[selectedPoint]
            : null;
        foreach (LaserNoteData laser in chart.lasers)
        {
            laser.points ??= new LaserPointData[0];
            Array.Sort(laser.points, (left, right) => left.time.CompareTo(right.time));
        }

        Array.Sort(chart.lasers, (left, right) => LaserStart(left).CompareTo(LaserStart(right)));
        if (selected != null)
        {
            selectedPoint = Array.IndexOf(selectedLaser.points, selected);
        }
    }

    private static double LaserStart(LaserNoteData laser)
    {
        return laser.points.Length > 0 ? laser.points[0].time : 0d;
    }

    private static double LaserEnd(LaserNoteData laser)
    {
        return laser.points.Length > 0 ? laser.points[laser.points.Length - 1].time : 0d;
    }

    private double Snap(double time)
    {
        bool free = !snapEnabled || Event.current != null && Event.current.alt;
        double snapped;
        if (free)
        {
            snapped = Math.Round(time * 1000d) / 1000d;
        }
        else
        {
            double step = GridStep;
            snapped = Chart.offset + Math.Round((time - Chart.offset) / step) * step;
        }

        return Math.Max(0d, snapped);
    }

    private float SnapLaserValue(float value)
    {
        bool free = freeLaserValues || Event.current != null && Event.current.alt;
        value = Mathf.Clamp(value, -1f, 1f);
        if (free)
        {
            return Mathf.Round(value * 100f) / 100f;
        }

        float step = LaserGridSteps[Mathf.Clamp(laserGridIndex, 0, LaserGridSteps.Length - 1)];
        return Mathf.Clamp(Mathf.Round(Mathf.Round(value / step) * step * 10000f) / 10000f, -1f, 1f);
    }

    private static string LaserGridLabel(float step)
    {
        return step.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Laser point grid: on/off toggle plus ◀ ▶ to pick how coarse the X / Y grid is.</summary>
    private void LaserGridControls()
    {
        int last = LaserGridSteps.Length - 1;
        laserGridIndex = Mathf.Clamp(laserGridIndex, 0, last);
        freeLaserValues = !GUILayout.Toggle(!freeLaserValues, new GUIContent("XY吸着", "レーザー点の X / Y をグリッドに合わせる（Alt で一時的に細かく）"), EditorStyles.miniButton, GUILayout.Width(56f));
        using (new EditorGUI.DisabledScope(freeLaserValues))
        {
            if (GUILayout.Button(new GUIContent("◀", "粗くする"), EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                laserGridIndex = Mathf.Max(0, laserGridIndex - 1);
            }

            GUILayout.Label(new GUIContent(LaserGridLabel(LaserGridSteps[laserGridIndex]), "XY グリッドの刻み幅（-1〜1 の範囲）"), GUILayout.Width(40f));
            if (GUILayout.Button(new GUIContent("▶", "細かくする"), EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                laserGridIndex = Mathf.Min(last, laserGridIndex + 1);
            }
        }
    }

    private void ClearSelection()
    {
        selectedNotes.Clear();
        selectedLasers.Clear();
        selectedPoints.Clear();
        selectedLaser = null;
        selectedPoint = -1;
    }

    private void SelectNote(TapNoteData note, bool additive)
    {
        selectedLaser = null;
        selectedPoint = -1;
        selectedPoints.Clear();
        if (!additive)
        {
            selectedNotes.Clear();
            selectedLasers.Clear();
        }

        if (!selectedNotes.Contains(note))
        {
            selectedNotes.Add(note);
        }
    }

    private void SelectLaser(LaserNoteData laser, int point)
    {
        selectedNotes.Clear();
        selectedLasers.Clear();
        selectedPoints.Clear();
        selectedLaser = laser;
        selectedPoint = point;
    }

    /// <summary>Ctrl+click on a laser point: adds it to (or takes it out of) the set of selected points.</summary>
    private void TogglePointInSelection(LaserNoteData laser, int index)
    {
        if (selectedLaser != null && selectedPoint >= 0 && selectedPoint < selectedLaser.points.Length)
        {
            selectedPoints.Add(selectedLaser.points[selectedPoint]);
        }

        selectedLaser = null;
        selectedPoint = -1;
        LaserPointData point = laser.points[index];
        if (!selectedPoints.Remove(point))
        {
            selectedPoints.Add(point);
        }

        FinishSelectionOfPoints();
    }

    /// <summary>A selection that is one point alone becomes the usual point selection.</summary>
    private void FinishSelectionOfPoints()
    {
        if (selectedNotes.Count == 0 && selectedLasers.Count == 0 && selectedPoints.Count == 1)
        {
            LaserPointData only = selectedPoints.First();
            LaserNoteData owner = Chart.lasers.FirstOrDefault(laser => Array.IndexOf(laser.points, only) >= 0);
            if (owner != null)
            {
                SelectLaser(owner, Array.IndexOf(owner.points, only));
            }
        }
    }

    /// <summary>Ctrl+click on a laser: adds it to (or takes it out of) a selection of whole lasers and notes.</summary>
    private void ToggleLaserInSelection(LaserNoteData laser)
    {
        if (selectedLaser != null && !selectedLasers.Contains(selectedLaser))
        {
            selectedLasers.Add(selectedLaser);
        }

        selectedLaser = null;
        selectedPoint = -1;
        selectedPoints.Clear();
        if (!selectedLasers.Remove(laser))
        {
            selectedLasers.Add(laser);
        }
    }

    /// <summary>Every laser in the selection: the box-selected ones and the one being shaped.</summary>
    private List<LaserNoteData> SelectedLaserList()
    {
        List<LaserNoteData> lasers = new List<LaserNoteData>(selectedLasers);
        if (selectedLaser != null && !lasers.Contains(selectedLaser))
        {
            lasers.Add(selectedLaser);
        }

        return lasers;
    }

    private bool IsLaserSelected(LaserNoteData laser)
    {
        return laser == selectedLaser || selectedLasers.Contains(laser);
    }

    private static int SlotOfLane(int lane)
    {
        return Array.IndexOf(LaneOrder, Mathf.Clamp(lane, 0, 3));
    }

    private static TapNoteData Clone(TapNoteData note)
    {
        return new TapNoteData
        {
            lane = note.lane,
            time = note.time,
            direction = note.direction,
            duration = note.duration,
            stick = note.stick,
            flickOnly = note.flickOnly,
            spin = note.spin
        };
    }

    private static LaserNoteData Clone(LaserNoteData laser)
    {
        return new LaserNoteData
        {
            side = laser.side,
            points = laser.points.Select(point => new LaserPointData { time = point.time, x = point.x, y = point.y }).ToArray()
        };
    }

    private static Vector2 Evaluate(LaserNoteData laser, double time)
    {
        LaserPointData[] points = laser.points;
        if (points.Length == 0)
        {
            return Vector2.zero;
        }

        if (time <= points[0].time)
        {
            return new Vector2(points[0].x, points[0].y);
        }

        for (int index = 0; index < points.Length - 1; index++)
        {
            LaserPointData from = points[index];
            LaserPointData to = points[index + 1];
            if (time <= to.time)
            {
                double span = to.time - from.time;
                float t = span <= 0d ? 1f : (float)((time - from.time) / span);
                return Vector2.Lerp(new Vector2(from.x, from.y), new Vector2(to.x, to.y), t);
            }
        }

        LaserPointData last = points[points.Length - 1];
        return new Vector2(last.x, last.y);
    }

    private void DeleteSelection()
    {
        if (selectedNotes.Count > 0 || selectedLasers.Count > 0 || selectedPoints.Count > 0)
        {
            HashSet<LaserPointData> doomedPoints = new HashSet<LaserPointData>(selectedPoints);
            HashSet<TapNoteData> doomed = new HashSet<TapNoteData>(selectedNotes);
            HashSet<LaserNoteData> doomedLasers = new HashSet<LaserNoteData>(SelectedLaserList());
            Modify("Delete selection", () =>
            {
                Chart.notes = Chart.notes.Where(note => !doomed.Contains(note)).ToArray();
                Chart.lasers = Chart.lasers.Where(laser => !doomedLasers.Contains(laser)).ToArray();
                foreach (LaserNoteData laser in Chart.lasers)
                {
                    laser.points = laser.points.Where(point => !doomedPoints.Contains(point)).ToArray();
                }

                // A laser needs two points; what is left of one with fewer goes too.
                Chart.lasers = Chart.lasers.Where(laser => laser.points.Length >= 2).ToArray();
            });
            ClearSelection();
            return;
        }

        if (selectedLaser != null)
        {
            LaserNoteData laser = selectedLaser;
            if (selectedPoint >= 0 && laser.points.Length > 2)
            {
                int point = selectedPoint;
                Modify("Delete laser point", () => laser.points = laser.points.Where((_, index) => index != point).ToArray());
                selectedPoint = -1;
            }
            else
            {
                Modify("Delete laser", () => Chart.lasers = Chart.lasers.Where(item => item != laser).ToArray());
                ClearSelection();
            }
        }
    }

    private void CopySelection()
    {
        List<LaserNoteData> lasers = SelectedLaserList();
        if (selectedNotes.Count == 0 && lasers.Count == 0)
        {
            return;
        }

        clipboardNotes.Clear();
        clipboardLasers.Clear();
        clipboardNotes.AddRange(selectedNotes.Select(Clone));
        clipboardLasers.AddRange(lasers.Select(Clone));
        clipboardOrigin = selectedNotes.Select(note => note.time).Concat(lasers.Select(LaserStart)).Min();
    }

    /// <summary>Pastes the clipboard so that its earliest item lands on the cursor.</summary>
    private void Paste()
    {
        if (clipboardNotes.Count == 0 && clipboardLasers.Count == 0)
        {
            return;
        }

        double shift = Snap(cursorTime) - clipboardOrigin;
        List<TapNoteData> pastedNotes = clipboardNotes.Select(Clone).ToList();
        List<LaserNoteData> pastedLasers = clipboardLasers.Select(Clone).ToList();
        foreach (TapNoteData note in pastedNotes)
        {
            note.time = Math.Max(0d, note.time + shift);
        }

        foreach (LaserNoteData laser in pastedLasers)
        {
            foreach (LaserPointData point in laser.points)
            {
                point.time = Math.Max(0d, point.time + shift);
            }
        }

        Modify("Paste", () =>
        {
            Chart.notes = Chart.notes.Concat(pastedNotes).ToArray();
            Chart.lasers = Chart.lasers.Concat(pastedLasers).ToArray();
        });

        ClearSelection();
        if (pastedNotes.Count == 0 && pastedLasers.Count == 1)
        {
            SelectLaser(pastedLasers[0], -1);
        }
        else
        {
            selectedNotes.AddRange(pastedNotes);
            selectedLasers.AddRange(pastedLasers);
        }
    }

    private bool CanMirror() => selectedNotes.Count > 0 || selectedLaser != null || selectedLasers.Count > 0;

    /// <summary>
    /// Copies the selection mirrored onto the opposite side and deletes whatever already sat there.
    /// Left-right: L → R, ZL → ZR, the left stick's lasers become the right stick's (and the other way round) with X reversed.
    /// Up-down: L → ZL, R → ZR, laser Y reversed (a laser stays on its stick, so it is flipped in place).
    /// Both: L → ZR, R → ZL. Directions, explicit sticks and spins follow, so the copy plays the same way mirrored.
    /// The copies end up selected.
    /// </summary>
    private void MirrorSelection(bool horizontal, bool vertical)
    {
        if (!horizontal && !vertical || !CanMirror())
        {
            return;
        }

        const double Eps = 1e-4;
        List<TapNoteData> copies = selectedNotes.Select(Clone).ToList();
        List<LaserNoteData> sources = SelectedLaserList();
        List<LaserNoteData> inPlace = horizontal ? new List<LaserNoteData>() : sources;
        List<LaserNoteData> laserCopies = horizontal ? sources.Select(Clone).ToList() : new List<LaserNoteData>();

        foreach (TapNoteData note in copies)
        {
            int side = note.GetStickSide(note.lane);
            if (horizontal)
            {
                note.lane ^= 1;
                note.direction = StickInput.Mirror(note.direction);
                if (!string.IsNullOrEmpty(note.stick))
                {
                    note.stick = side == 0 ? "R" : "L";
                }
            }

            if (vertical)
            {
                note.lane ^= 2;
                note.direction = FlipVertical(note.direction);
            }

            // One reflection reverses the turn; two (a half turn) keep it.
            if (note.IsSpin && horizontal != vertical)
            {
                note.spin = note.spin == TapNoteData.SpinClockwise ? TapNoteData.SpinCounterClockwise : TapNoteData.SpinClockwise;
            }
        }

        foreach (LaserNoteData laser in laserCopies)
        {
            laser.side = laser.side == 0 ? 1 : 0;
            foreach (LaserPointData point in laser.points)
            {
                point.x = -point.x;
                if (vertical)
                {
                    point.y = -point.y;
                }
            }
        }

        string name = horizontal && vertical ? "Mirror (both)" : horizontal ? "Mirror left-right" : "Mirror up-down";
        Modify(name, () =>
        {
            foreach (LaserNoteData laser in inPlace)
            {
                foreach (LaserPointData point in laser.points)
                {
                    point.y = -point.y;
                }
            }

            Chart.notes = Chart.notes
                .Where(old => !copies.Any(copy => copy.lane == old.lane && copy.time <= old.EndTime + Eps && old.time <= copy.EndTime + Eps))
                .Concat(copies).ToArray();
            Chart.lasers = Chart.lasers
                .Where(old => !laserCopies.Any(copy => copy.side == old.side && LaserStart(copy) <= LaserEnd(old) + Eps && LaserStart(old) <= LaserEnd(copy) + Eps))
                .Concat(laserCopies).ToArray();
        });

        ClearSelection();
        if (copies.Count == 0 && laserCopies.Count + inPlace.Count == 1)
        {
            SelectLaser(laserCopies.Count == 1 ? laserCopies[0] : inPlace[0], -1);
        }
        else
        {
            selectedNotes.AddRange(copies);
            selectedLasers.AddRange(laserCopies);
            selectedLasers.AddRange(inPlace);
        }

        Notify(horizontal && vertical ? "上下左右に反転コピーしました（L→ZR, R→ZL。重なる既存ノーツは削除）"
            : horizontal ? "左右に反転コピーしました（L→R, ZL→ZR, 左レーザー→右レーザー。重なる既存ノーツは削除）"
            : "上下に反転コピーしました（L→ZL, R→ZR。重なる既存ノーツは削除）");
    }

    private static NoteDirection FlipVertical(NoteDirection direction)
    {
        switch (direction)
        {
            case NoteDirection.Up: return NoteDirection.Down;
            case NoteDirection.Down: return NoteDirection.Up;
            case NoteDirection.UpLeft: return NoteDirection.DownLeft;
            case NoteDirection.DownLeft: return NoteDirection.UpLeft;
            case NoteDirection.UpRight: return NoteDirection.DownRight;
            case NoteDirection.DownRight: return NoteDirection.UpRight;
            default: return direction;
        }
    }

    /// <summary>Plays the selection backwards in time: its first and last moments trade places (holds and lasers turn round too).</summary>
    private void ReverseSelectionInTime()
    {
        List<TapNoteData> notes = selectedNotes.ToList();
        List<LaserNoteData> lasers = SelectedLaserList();
        if (notes.Count == 0 && lasers.Count == 0)
        {
            return;
        }

        double start = notes.Select(note => note.time).Concat(lasers.Select(LaserStart)).Min();
        double end = notes.Select(note => note.EndTime).Concat(lasers.Select(LaserEnd)).Max();
        Modify("Reverse in time", () =>
        {
            foreach (TapNoteData note in notes)
            {
                note.time = Math.Max(0d, Math.Round(start + end - note.EndTime, 6));
            }

            foreach (LaserNoteData laser in lasers)
            {
                foreach (LaserPointData point in laser.points)
                {
                    point.time = Math.Max(0d, Math.Round(start + end - point.time, 6));
                }
            }
        });

        Notify("選択範囲を前後反転しました");
    }

    /// <summary>選択範囲を反転: everything that was not selected is selected, and the other way round.</summary>
    private void InvertSelection()
    {
        HashSet<TapNoteData> notes = new HashSet<TapNoteData>(selectedNotes);
        HashSet<LaserNoteData> lasers = new HashSet<LaserNoteData>(SelectedLaserList());
        ClearSelection();
        selectedNotes.AddRange(Chart.notes.Where(note => !notes.Contains(note)));
        selectedLasers.AddRange(Chart.lasers.Where(laser => !lasers.Contains(laser)));
        Notify($"選択範囲を反転しました（ノーツ {selectedNotes.Count} / レーザー {selectedLasers.Count}）");
    }

    /// <summary>Sets the placement direction and, when notes are selected, their direction too.</summary>
    private void ApplyDirection(NoteDirection direction)
    {
        placeDirection = direction;
        if (selectedNotes.Count > 0)
        {
            List<TapNoteData> notes = selectedNotes.ToList();
            Modify("Set direction", () =>
            {
                foreach (TapNoteData note in notes)
                {
                    note.direction = direction;
                    if (direction != NoteDirection.None)
                    {
                        // Giving a direction makes it an ordinary note again.
                        note.spin = 0;
                    }
                }
            });
        }

        Repaint();
    }

    /// <summary>Sets the placement spin (0 none, 1 counter-clockwise, 2 clockwise) and, when notes are selected, theirs too.</summary>
    private void ApplySpin(int spin)
    {
        placeSpin = spin == 0 ? TapNoteData.SpinCounterClockwise : spin;
        if (selectedNotes.Count > 0)
        {
            List<TapNoteData> notes = selectedNotes.ToList();
            Modify("Set spin", () =>
            {
                foreach (TapNoteData note in notes)
                {
                    note.spin = spin;
                    if (spin != 0)
                    {
                        note.direction = NoteDirection.None;
                        note.flickOnly = false;
                        note.duration = 0d;
                    }
                }
            });
        }

        Repaint();
    }

    private void ToggleFlickOnly()
    {
        if (selectedNotes.Count > 0)
        {
            bool value = !selectedNotes.All(note => note.flickOnly);
            List<TapNoteData> notes = selectedNotes.ToList();
            Modify("Toggle flick only", () =>
            {
                foreach (TapNoteData note in notes)
                {
                    note.flickOnly = value;
                }
            });
        }
        else
        {
            tool = tool == Tool.Flick ? Tool.Tap : Tool.Flick;
        }

        Repaint();
    }

    // ------------------------------------------------------------------ playback

    private AudioClip GetAudioClip()
    {
        string path = Chart.audio ?? string.Empty;
        if (path != loadedAudioPath)
        {
            loadedAudioPath = path;
            audioClip = string.IsNullOrWhiteSpace(path) ? null : Resources.Load<AudioClip>(path);
            BuildWaveform();
        }

        return audioClip;
    }

    /// <summary>
    /// Finds where the music first becomes audible in the waveform and shifts the music so
    /// that spot falls on the chart's first beat (<c>offset</c>).
    /// </summary>
    private void AutoAlignAudio()
    {
        if (waveform == null)
        {
            Notify("波形が読み込まれていないため調整できません");
            return;
        }

        float max = 0f;
        foreach (float peak in waveform)
        {
            max = Mathf.Max(max, peak);
        }

        if (max <= 0.0001f)
        {
            Notify("無音の音源です");
            return;
        }

        float threshold = Mathf.Max(0.02f, max * 0.12f);
        int onset = -1;
        for (int i = 0; i < waveform.Length; i++)
        {
            if (waveform[i] >= threshold)
            {
                onset = i;
                break;
            }
        }

        if (onset < 0)
        {
            Notify("音の開始位置が見つかりません");
            return;
        }

        double onsetTime = onset / (double)WaveformRate;
        double value = Math.Round((Chart.offset - onsetTime) * 1000d) / 1000d;
        Modify("Auto align audio", () => Chart.audioOffset = value);
        Notify($"音の開始 {onsetTime:0.000}s を最初の拍 {Chart.offset:0.000}s に合わせました（ずれ {value:0.000}s）");
    }

    private void BuildWaveform()
    {
        waveform = null;
        waveformNote = null;
        if (audioClip == null)
        {
            return;
        }

        if (audioClip.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            waveformNote = "波形を表示するには、音源の Load Type を Decompress On Load にしてください。";
            return;
        }

        if (audioClip.loadState != AudioDataLoadState.Loaded)
        {
            audioClip.LoadAudioData();
        }

        int channels = Mathf.Max(1, audioClip.channels);
        int frequency = Mathf.Max(1, audioClip.frequency);
        int bins = Mathf.CeilToInt(audioClip.length * WaveformRate) + 1;
        float[] peaks = new float[bins];
        float[] buffer = new float[frequency * channels];
        for (int offset = 0; offset < audioClip.samples; offset += frequency)
        {
            int frames = Mathf.Min(frequency, audioClip.samples - offset);
            if (frames < frequency)
            {
                buffer = new float[frames * channels];
            }

            if (!audioClip.GetData(buffer, offset))
            {
                waveformNote = "音源の波形を読み込めませんでした。";
                return;
            }

            for (int frame = 0; frame < frames; frame++)
            {
                int bin = (int)((long)(offset + frame) * WaveformRate / frequency);
                float peak = peaks[bin];
                for (int channel = 0; channel < channels; channel++)
                {
                    float value = Mathf.Abs(buffer[frame * channels + channel]);
                    if (value > peak)
                    {
                        peak = value;
                    }
                }

                peaks[bin] = peak;
            }
        }

        waveform = peaks;
    }

    private void TogglePlayback()
    {
        if (playback.IsPlaying)
        {
            StopPlayback();
        }
        else
        {
            if (flow.IsEmbeddedPreview)
            {
                flow.StopEmbeddedPreview();
            }

            playStartTime = cursorTime;
            playback.MusicEnabled = musicEnabled;
            playback.MusicVolume = musicVolume;
            playback.ClapVolume = clapVolume;
            playback.ClapEnabled = clapEnabled;
            playback.Play(GetAudioClip(), cursorTime, Chart);
        }

        Repaint();
    }

    /// <summary>Stops; with "return on stop" the cursor goes back to where the playback began.</summary>
    private void StopPlayback()
    {
        bool wasPlaying = playback.IsPlaying;
        playback.Stop();
        if (wasPlaying && returnOnStop)
        {
            cursorTime = playStartTime;
            RevealCursor();
        }
        else
        {
            cursorTime = Snap(cursorTime);
        }
    }

    private void PlayFrom(double time)
    {
        if (playback.IsPlaying)
        {
            playback.Stop();
        }

        cursorTime = Math.Max(0d, time);
        FollowCursor();
        TogglePlayback();
    }

    private double MeasureStart(int measure)
    {
        return Chart.offset + (measure - 1) * 4d * BeatSeconds;
    }

    /// <summary>
    /// The game's own preview from the playhead. With the game preview sidebar shown it plays there
    /// (and a second press stops it); otherwise it takes the whole screen and the pause menu or result leads back.
    /// </summary>
    private void TogglePreview()
    {
        if (!show3D)
        {
            PreviewInGame();
            return;
        }

        if (flow.IsEmbeddedPreview)
        {
            flow.StopEmbeddedPreview();
            previewWasRunning = false;
            if (returnOnStop)
            {
                cursorTime = playStartTime;
                RevealCursor();
            }
        }
        else
        {
            StartSplitPreview();
        }
    }

    private void StartSplitPreview()
    {
        playback.Stop();
        Normalize();
        playStartTime = cursorTime;
        flow.StartEmbeddedPreview(ChartLoader.Parse(ChartJsonWriter.Write(Chart), ChartName), cursorTime);
    }

    /// <summary>Plays the chart from the cursor in the real game; the pause menu or result leads back here.</summary>
    private void PreviewInGame()
    {
        playback.Stop();
        Normalize();
        flow.PreviewFromEditor(ChartLoader.Parse(ChartJsonWriter.Write(Chart), ChartName), cursorTime);
    }

    // ------------------------------------------------------------------ shortcuts


    /// <summary>Number keys laid out like a numpad: 8 = Up, 2 = Down, 7 = UpLeft ... 5 / 0 = none.</summary>
    private bool TryDirectionKey(KeyCode key)
    {
        int digit;
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
        {
            digit = key - KeyCode.Alpha0;
        }
        else if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
        {
            digit = key - KeyCode.Keypad0;
        }
        else
        {
            return false;
        }

        NoteDirection direction;
        switch (digit)
        {
            case 8: direction = NoteDirection.Up; break;
            case 2: direction = NoteDirection.Down; break;
            case 4: direction = NoteDirection.Left; break;
            case 6: direction = NoteDirection.Right; break;
            case 7: direction = NoteDirection.UpLeft; break;
            case 9: direction = NoteDirection.UpRight; break;
            case 1: direction = NoteDirection.DownLeft; break;
            case 3: direction = NoteDirection.DownRight; break;
            default: direction = NoteDirection.None; break;
        }

        ApplyDirection(direction);
        return true;
    }

    private void MoveCursor(double delta)
    {
        playback.Stop();
        cursorTime = Math.Max(0d, Snap(cursorTime + delta));
        // Scroll along when the cursor leaves the view.
        double visible = (timelineHeight - HeaderHeight) / pixelsPerSecond;
        if (cursorTime < viewTime + visible * 0.1d || cursorTime > viewTime + visible * 0.9d)
        {
            viewTime = cursorTime - visible * 0.3d;
        }
    }

    // ------------------------------------------------------------------ GUI








    private string FormatTime(double time)
    {
        double beats = (time - Chart.offset) / BeatSeconds;
        int measure = (int)Math.Floor(beats / 4d) + 1;
        double beatInMeasure = beats - Math.Floor(beats / 4d) * 4d + 1d;
        return $"{time:0.000}s  {measure}:{beatInMeasure:0.00}";
    }

    // ------------------------------------------------------------------ timeline layout & mapping

    private void LayoutColumns(float width)
    {
        // Lanes and laser columns grow with the window (within limits); the rest is fixed.
        const float fixedWidth = RulerWidth + WaveWidth + 6f + 8f * 3f + 12f;
        float scale = Mathf.Clamp((width - fixedWidth) / (LaneWidth * 4f + LaserColumnWidth * 4f), 0.75f, 2.2f);
        float lane = Mathf.Round(LaneWidth * scale);
        float laser = Mathf.Round(LaserColumnWidth * scale);

        columns.Clear();
        float x = 0f;
        Add(ColumnKind.Ruler, 0, RulerWidth, string.Empty);
        Add(ColumnKind.Wave, 0, WaveWidth, "波形");
        x += 6f;
        gridLeft = x;
        Add(ColumnKind.LaserX, 0, laser, "左 X");
        Add(ColumnKind.LaserY, 0, laser, "左 Y");
        x += 8f;
        Add(ColumnKind.Lane, 0, lane, "L");
        Add(ColumnKind.Lane, 1, lane, "R");
        Add(ColumnKind.Lane, 2, lane, "ZL");
        Add(ColumnKind.Lane, 3, lane, "ZR");
        x += 8f;
        Add(ColumnKind.LaserX, 1, laser, "右 X");
        Add(ColumnKind.LaserY, 1, laser, "右 Y");
        gridRight = x;

        void Add(ColumnKind kind, int index, float width, string label)
        {
            columns.Add(new Column { Kind = kind, Index = index, Rect = new Rect(x, 0f, width, timelineHeight), Label = label });
            x += width;
        }
    }

    private float TimeToY(double time)
    {
        return timelineHeight - (float)((time - viewTime) * pixelsPerSecond);
    }

    private double YToTime(float y)
    {
        return viewTime + (timelineHeight - y) / pixelsPerSecond;
    }

    private static float ValueToX(Rect column, float value)
    {
        return column.x + 8f + (Mathf.Clamp(value, -1f, 1f) + 1f) * 0.5f * (column.width - 16f);
    }

    private static float XToValue(Rect column, float x)
    {
        return Mathf.Clamp((x - column.x - 8f) / (column.width - 16f) * 2f - 1f, -1f, 1f);
    }

    private bool TryGetColumn(float x, out Column column)
    {
        foreach (Column candidate in columns)
        {
            if (x >= candidate.Rect.x && x < candidate.Rect.xMax)
            {
                column = candidate;
                return true;
            }
        }

        column = default;
        return false;
    }

    private Column LaneColumn(int lane)
    {
        return columns.First(column => column.Kind == ColumnKind.Lane && column.Index == lane);
    }

    private void SetZoom(float newPixelsPerSecond, float anchorY)
    {
        newPixelsPerSecond = Mathf.Clamp(newPixelsPerSecond, MinPixelsPerSecond, MaxPixelsPerSecond);
        if (Mathf.Abs(newPixelsPerSecond - pixelsPerSecond) < 0.01f)
        {
            return;
        }

        double anchorTime = YToTime(anchorY);
        pixelsPerSecond = newPixelsPerSecond;
        viewTime = anchorTime - (timelineHeight - anchorY) / pixelsPerSecond;
    }

    // Note shapes, in timeline coordinates.
    private Rect NoteHeadRect(TapNoteData note)
    {
        Rect column = LaneColumn(Mathf.Clamp(note.lane, 0, 3)).Rect;
        float y = TimeToY(note.time);
        return new Rect(column.x + 4f, y - 5f, column.width - 8f, 10f);
    }

    private Rect HoldBodyRect(TapNoteData note)
    {
        Rect column = LaneColumn(Mathf.Clamp(note.lane, 0, 3)).Rect;
        float head = TimeToY(note.time);
        float tail = TimeToY(note.EndTime);
        return new Rect(column.x + 13f, tail, column.width - 26f, head - tail);
    }

    private Rect HoldTailRect(TapNoteData note)
    {
        Rect column = LaneColumn(Mathf.Clamp(note.lane, 0, 3)).Rect;
        float tail = TimeToY(note.EndTime);
        return new Rect(column.x + 8f, tail - 4f, column.width - 16f, 8f);
    }

    // ------------------------------------------------------------------ timeline input

    private void HandleTimelineInput(Event current, Vector2 size)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Vector2 mouse = current.mousePosition;
        bool inside = mouse.x >= 0f && mouse.y >= HeaderHeight && mouse.x < size.x && mouse.y < size.y && !IsTimelineBlocked(mouse);

        if (current.type == EventType.Repaint && inside && drag == DragMode.None)
        {
            UpdateHoverHint(mouse);
        }

        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseMove:
                if (inside)
                {
                    UpdateHoverHint(mouse);
                    Repaint();
                }

                break;

            case EventType.ScrollWheel:
                if (!inside)
                {
                    break;
                }

                if (current.control || current.command)
                {
                    SetZoom(pixelsPerSecond * Mathf.Pow(1.12f, -Mathf.Clamp(current.delta.y, -1f, 1f)), mouse.y);
                }
                else
                {
                    viewTime -= Mathf.Clamp(current.delta.y, -1f, 1f) * 54f / pixelsPerSecond;
                }

                viewTime = Math.Max(-2d, viewTime);
                current.Use();
                Repaint();
                break;

            case EventType.MouseDown:
                if (!inside || !TryGetColumn(mouse.x, out Column column))
                {
                    break;
                }

                GUIUtility.keyboardControl = 0;
                if (OnMouseDown(current, column, mouse))
                {
                    GUIUtility.hotControl = controlId;
                }

                current.Use();
                Repaint();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != controlId)
                {
                    break;
                }

                OnMouseDrag(current, mouse);
                current.Use();
                Repaint();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != controlId)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                if (drag == DragMode.BoxSelect)
                {
                    FinishBoxSelect();
                }
                else if (drag == DragMode.SetDirection)
                {
                    FinishDirectionDrag();
                }

                EndDrag();
                current.Use();
                Repaint();
                break;
        }
    }

    private void UpdateHoverHint(Vector2 mouse)
    {
        if (!TryGetColumn(mouse.x, out Column column))
        {
            hoverHint = string.Empty;
            return;
        }

        switch (column.Kind)
        {
            case ColumnKind.Lane:
                hoverHint = LaneHint();
                break;
            case ColumnKind.LaserX:
            case ColumnKind.LaserY:
                hoverHint = LaserColumnHint();
                break;
            default:
                hoverHint = "クリック / ドラッグ: カーソル移動 ／ ホイール: スクロール ／ Ctrl+ホイール: ズーム ／ 中ボタンドラッグ: スクロール";
                break;
        }
    }

    /// <returns>True when a drag starts (the control takes the mouse until release).</returns>
    private bool OnMouseDown(Event current, Column column, Vector2 mouse)
    {
        double time = YToTime(mouse.y);

        if (current.button == 2)
        {
            // Ctrl + middle drag zooms (up = in, down = out) around the spot where it started; plain middle drag pans.
            drag = current.control || current.command ? DragMode.Dolly : DragMode.Pan;
            dragStartMouseY = mouse.y;
            dragStartViewTime = viewTime;
            timelineZoomStart = pixelsPerSecond;
            return true;
        }

        switch (column.Kind)
        {
            case ColumnKind.Wave:
                if (current.button == 0 && audioClip != null)
                {
                    // Dragging the waveform slides the music against the chart.
                    playback.Stop();
                    flow.StopEmbeddedPreview();
                    audioDragStartTime = time;
                    audioDragStartOffset = Chart.audioOffset;
                    BeginDrag(DragMode.MoveAudio, "Move audio");
                    return true;
                }

                goto case ColumnKind.Ruler;

            case ColumnKind.Ruler:
                if (current.button != 0)
                {
                    return false;
                }

                scrubResume = flow.IsEmbeddedPreview ? 2 : playback.IsPlaying ? 1 : 0;
                playback.Stop();
                flow.StopEmbeddedPreview();
                cursorTime = Snap(time);
                drag = DragMode.Scrub;
                return true;

            case ColumnKind.Lane:
                return OnLaneMouseDown(current, column, mouse, time);

            default:
                return OnLaserMouseDown(current, column, mouse, time);
        }
    }

    private bool OnLaneMouseDown(Event current, Column column, Vector2 mouse, double time)
    {
        TapNoteData hit = null;
        bool tailHit = false;
        // Last drawn first: the note on top wins.
        for (int index = Chart.notes.Length - 1; index >= 0 && hit == null; index--)
        {
            TapNoteData note = Chart.notes[index];
            if (note.lane != column.Index)
            {
                continue;
            }

            if (note.IsHold && HoldTailRect(note).Contains(mouse))
            {
                hit = note;
                tailHit = true;
            }
            else if (NoteHeadRect(note).Contains(mouse) || note.IsHold && HoldBodyRect(note).Contains(mouse))
            {
                hit = note;
            }
        }

        return OnLaneMouseDown(current, column.Index, mouse, time, hit, tailHit);
    }

    /// <summary>
    /// A press on a lane (the timeline's lane column or a lane of the 3D view): <paramref name="hit"/> is the
    /// note under the pointer, if any, and <paramref name="time"/> the unsnapped time at the pointer.
    /// </summary>
    private bool OnLaneMouseDown(Event current, int lane, Vector2 mouse, double time, TapNoteData hit, bool tailHit)
    {
        if (current.button == 1)
        {
            // Right button: a drag gives a flick-only direction (a plain click on a note deletes it).
            if (hit != null)
            {
                SelectNote(hit, false);
                BeginDirectionDrag(hit, hit.lane, hit.time, current.mousePosition, true);
                dirPendingDelete = true;
                return true;
            }

            BeginDirectionDrag(null, lane, Snap(time), current.mousePosition, true);
            return true;
        }

        if (current.button != 0)
        {
            return false;
        }

        if (tool == Tool.Erase)
        {
            // The eraser: everything the drag passes over goes.
            BeginDrag(DragMode.Erase, "Erase");
            EraseAtPointer(mouse);
            return true;
        }

        if (tool == Tool.Box || hit == null && (current.shift || tool == Tool.Select || tool == Tool.Laser))
        {
            if (!(current.control || current.command))
            {
                ClearSelection();
            }

            drag = DragMode.BoxSelect;
            boxStart = mouse;
            boxRect = new Rect(mouse, Vector2.zero);
            return true;
        }

        if (hit != null)
        {
            if (current.shift && !tailHit)
            {
                // Shift + drag on a note: give it a direction.
                SelectNote(hit, false);
                BeginDirectionDrag(hit, hit.lane, hit.time, current.mousePosition, false);
                return true;
            }

            if (current.control || current.command)
            {
                if (!selectedNotes.Remove(hit))
                {
                    SelectNote(hit, true);
                }

                return false;
            }

            if (tailHit || tool == Tool.Hold && !hit.IsFlickOnly)
            {
                // The hold tool stretches the note it grabs (a tap becomes a hold).
                SelectNote(hit, false);
                dragAnchor = hit;
                BeginDrag(DragMode.ResizeHold, "Resize hold");
                return true;
            }

            if (tool == Tool.Flick)
            {
                // The flick tool turns the note it drags into a flick-only one (a click only selects).
                SelectNote(hit, false);
                BeginDirectionDrag(hit, hit.lane, hit.time, current.mousePosition, true);
                return true;
            }

            if (tool == Tool.Spin)
            {
                // The spin tool turns the note it clicks into a spin note (dragging still moves it).
                SelectNote(hit, false);
                if (hit.spin != placeSpin)
                {
                    ApplySpin(placeSpin);
                }

                BeginMoveNotes(hit, hit.lane, time);
                return true;
            }

            if (!selectedNotes.Contains(hit))
            {
                SelectNote(hit, false);
            }

            BeginMoveNotes(hit, hit.lane, time);
            return true;
        }

        double placeTime = Snap(time);
        if (tool == Tool.Spin)
        {
            TapNoteData spinNote = new TapNoteData
            {
                lane = lane,
                time = placeTime,
                spin = placeSpin,
                stick = placeStick == 1 ? "L" : placeStick == 2 ? "R" : string.Empty
            };

            SelectNote(spinNote, false);
            BeginMoveNotes(spinNote, lane, time);
            DragModify("Place note", () => Chart.notes = Chart.notes.Append(spinNote).ToArray());
            return true;
        }

        if (tool == Tool.Flick)
        {
            // The note appears once the drag has a direction (or, on a plain click, with the set direction).
            ClearSelection();
            BeginDirectionDrag(null, lane, placeTime, current.mousePosition, true);
            return true;
        }

        // Place a new note; dragging out of it gives a direction, the hold tool (or Ctrl) drags out a hold.
        TapNoteData existing = Chart.notes.FirstOrDefault(note => note.lane == lane && Math.Abs(note.time - placeTime) < 0.0005d);
        if (existing != null)
        {
            SelectNote(existing, false);
            BeginMoveNotes(existing, lane, time);
            return true;
        }

        TapNoteData created = new TapNoteData
        {
            lane = lane,
            time = placeTime,
            direction = placeDirection,
            stick = placeStick == 1 ? "L" : placeStick == 2 ? "R" : string.Empty
        };

        bool holdDrag = tool == Tool.Hold || current.control || current.command;
        BeginDrag(holdDrag ? DragMode.ResizeHold : DragMode.SetDirection, "Place note");
        DragModify("Place note", () => Chart.notes = Chart.notes.Append(created).ToArray());
        SelectNote(created, false);
        dragAnchor = created;
        if (!holdDrag)
        {
            // Dragging out of the new note gives it a direction.
            dirOrigin = current.mousePosition;
            dirMouse = dirOrigin;
            dirLane = lane;
            dirTime = placeTime;
            dirForceFlick = false;
            dirBaseFlick = created.flickOnly;
            dirBaseDirection = created.direction;
            dirPendingDelete = false;
            dirDragged = false;
        }

        return true;
    }

    private void EraseAtPointer(Vector2 mouse)
    {
        if (editView3D)
        {
            EraseAt3D(mouse);
        }
        else
        {
            EraseAt(mouse);
        }
    }

    /// <summary>Eraser: deletes the note, hold or laser point under <paramref name="mouse"/> (timeline coordinates).</summary>
    private void EraseAt(Vector2 mouse)
    {
        if (!TryGetColumn(mouse.x, out Column column))
        {
            return;
        }

        if (column.Kind == ColumnKind.Lane)
        {
            TapNoteData note = Chart.notes.LastOrDefault(item => item.lane == column.Index
                && (NoteHeadRect(item).Contains(mouse) || item.IsHold && (HoldBodyRect(item).Contains(mouse) || HoldTailRect(item).Contains(mouse))
                    || (item.IsFlickOnly || item.IsSpin) && new Rect(NoteHeadRect(item).center.x - 11f, NoteHeadRect(item).center.y - 9f, 22f, 18f).Contains(mouse)));
            if (note != null)
            {
                DragModify("Erase", () => Chart.notes = Chart.notes.Where(item => item != note).ToArray());
                selectedNotes.Remove(note);
            }

            return;
        }

        if (column.Kind != ColumnKind.LaserX && column.Kind != ColumnKind.LaserY)
        {
            return;
        }

        int axis = column.Kind == ColumnKind.LaserX ? 0 : 1;
        foreach (LaserNoteData laser in Chart.lasers.Reverse())
        {
            if (laser.side != column.Index)
            {
                continue;
            }

            int point = Array.FindIndex(laser.points, item => Vector2.Distance(mouse, PointPosition(column.Rect, item, axis)) <= 7f);
            if (point >= 0 && laser.points.Length > 2)
            {
                DragModify("Erase", () => laser.points = laser.points.Where((_, index) => index != point).ToArray());
                if (laser == selectedLaser)
                {
                    selectedPoint = -1;
                }

                return;
            }

            if (point >= 0 || DistanceToLaser(column.Rect, laser, axis, mouse) <= 5f)
            {
                DragModify("Erase", () => Chart.lasers = Chart.lasers.Where(item => item != laser).ToArray());
                if (laser == selectedLaser)
                {
                    ClearSelection();
                }

                return;
            }
        }
    }

    private void BeginDirectionDrag(TapNoteData note, int lane, double time, Vector2 mouse, bool forceFlick)
    {
        BeginDrag(DragMode.SetDirection, "Set direction");
        dragAnchor = note;
        dirOrigin = mouse;
        dirMouse = mouse;
        dirLane = lane;
        dirTime = time;
        dirForceFlick = forceFlick;
        dirBaseFlick = note != null && note.flickOnly;
        dirBaseDirection = note != null ? note.direction : NoteDirection.None;
        dirPendingDelete = false;
        dirDragged = false;
    }

    /// <summary>The eight-way direction a drag on the timeline points to (screen up = Up).</summary>
    private static NoteDirection DirectionFromDrag(Vector2 delta)
    {
        float angle = Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg;
        int sector = Mathf.RoundToInt(angle / 45f);
        switch (((sector % 8) + 8) % 8)
        {
            case 0: return NoteDirection.Right;
            case 1: return NoteDirection.UpRight;
            case 2: return NoteDirection.Up;
            case 3: return NoteDirection.UpLeft;
            case 4: return NoteDirection.Left;
            case 5: return NoteDirection.DownLeft;
            case 6: return NoteDirection.Down;
            default: return NoteDirection.DownRight;
        }
    }

    private void DragDirection(Vector2 mouse)
    {
        dirMouse = mouse;
        Vector2 delta = mouse - dirOrigin;
        bool far = delta.magnitude >= DirectionDragThreshold;
        if (far)
        {
            dirDragged = true;
        }

        if (dragAnchor == null)
        {
            if (!far)
            {
                return;
            }

            // Right-drag on an empty spot: the note appears once the drag has a direction.
            TapNoteData created = new TapNoteData
            {
                lane = dirLane,
                time = dirTime,
                stick = placeStick == 1 ? "L" : placeStick == 2 ? "R" : string.Empty
            };

            DragModify("Place note", () => Chart.notes = Chart.notes.Append(created).ToArray());
            SelectNote(created, false);
            dragAnchor = created;
            dirBaseDirection = NoteDirection.None;
            dirBaseFlick = false;
        }

        TapNoteData note = dragAnchor;
        NoteDirection direction = far ? DirectionFromDrag(delta) : dirBaseDirection;
        bool flick = far ? dirForceFlick || dirBaseFlick : dirBaseFlick;
        if (note.direction != direction || note.flickOnly != flick)
        {
            DragModify("Set direction", () =>
            {
                note.direction = direction;
                note.flickOnly = flick;
            });
        }
    }

    private void FinishDirectionDrag()
    {
        if (dirPendingDelete && !dirDragged && dragAnchor != null)
        {
            TapNoteData note = dragAnchor;
            Modify("Delete note", () => Chart.notes = Chart.notes.Where(item => item != note).ToArray());
            selectedNotes.Remove(note);
        }
        else if (!dirDragged && dragAnchor == null && tool == Tool.Flick && Event.current.button == 0 && placeDirection != NoteDirection.None)
        {
            // A plain click with the flick tool: a flick-only note in the set direction.
            int lane = dirLane;
            double time = dirTime;
            if (!Chart.notes.Any(note => note.lane == lane && Math.Abs(note.time - time) < 0.0005d))
            {
                TapNoteData created = new TapNoteData
                {
                    lane = lane,
                    time = time,
                    direction = placeDirection,
                    flickOnly = true,
                    stick = placeStick == 1 ? "L" : placeStick == 2 ? "R" : string.Empty
                };

                DragModify("Place note", () => Chart.notes = Chart.notes.Append(created).ToArray());
                SelectNote(created, false);
            }
        }
    }

    private void BeginMoveNotes(TapNoteData anchor, int lane, double time)
    {
        BeginDrag(DragMode.MoveNotes, "Move notes");
        dragAnchor = anchor;
        dragStartMouseTime = time;
        dragStartSlot = SlotOfLane(lane);
        dragOrigins.Clear();
        foreach (TapNoteData note in selectedNotes)
        {
            dragOrigins[note] = (note.time, SlotOfLane(note.lane));
        }
    }

    private bool BeginBoxSelect(Event current, Vector2 mouse)
    {
        if (!(current.control || current.command))
        {
            ClearSelection();
        }

        drag = DragMode.BoxSelect;
        boxStart = mouse;
        boxRect = new Rect(mouse, Vector2.zero);
        return true;
    }

    private bool OnLaserMouseDown(Event current, Column column, Vector2 mouse, double time)
    {
        int side = column.Index;
        int axis = column.Kind == ColumnKind.LaserX ? 0 : 1;
        if (current.button == 0 && tool == Tool.Erase)
        {
            BeginDrag(DragMode.Erase, "Erase");
            EraseAt(mouse);
            return true;
        }

        if (current.button == 0 && tool == Tool.Box)
        {
            return BeginBoxSelect(current, mouse);
        }

        // Hit test: points first, then segments.
        LaserNoteData hitLaser = null;
        int hitPoint = -1;
        foreach (LaserNoteData laser in Chart.lasers.Reverse())
        {
            if (laser.side != side)
            {
                continue;
            }

            for (int index = 0; index < laser.points.Length && hitPoint < 0; index++)
            {
                if (Vector2.Distance(mouse, PointPosition(column.Rect, laser.points[index], axis)) <= 7f)
                {
                    hitLaser = laser;
                    hitPoint = index;
                }
            }

            if (hitPoint >= 0)
            {
                break;
            }
        }

        if (hitLaser == null)
        {
            foreach (LaserNoteData laser in Chart.lasers.Reverse())
            {
                if (laser.side == side && DistanceToLaser(column.Rect, laser, axis, mouse) <= 5f)
                {
                    hitLaser = laser;
                    break;
                }
            }
        }

        if (current.button == 1)
        {
            if (hitLaser == null)
            {
                return false;
            }

            if (hitPoint >= 0 && hitLaser.points.Length > 2)
            {
                LaserNoteData laser = hitLaser;
                int point = hitPoint;
                Modify("Delete laser point", () => laser.points = laser.points.Where((_, index) => index != point).ToArray());
                SelectLaser(laser, -1);
            }
            else
            {
                LaserNoteData laser = hitLaser;
                Modify("Delete laser", () => Chart.lasers = Chart.lasers.Where(item => item != laser).ToArray());
                ClearSelection();
            }

            return false;
        }

        if (current.button != 0)
        {
            return false;
        }

        if (hitLaser != null && hitPoint >= 0)
        {
            LaserPointData pressed = hitLaser.points[hitPoint];
            if (current.control || current.command)
            {
                TogglePointInSelection(hitLaser, hitPoint);
                return false;
            }

            dragAxis = axis;
            if (selectedPoints.Contains(pressed))
            {
                // Pressing one of several selected points moves them all.
                pointOrigins.Clear();
                foreach (LaserPointData point in selectedPoints)
                {
                    pointOrigins[point] = (point.time, point.x, point.y);
                }

                pointDragStartTime = time;
                pointDragStartValue = XToValue(column.Rect, mouse.x);
                BeginDrag(DragMode.MovePoint, "Move laser points");
                return true;
            }

            SelectLaser(hitLaser, hitPoint);
            BeginDrag(DragMode.MovePoint, "Move laser point");
            return true;
        }

        if (hitLaser != null)
        {
            if (current.control || current.command)
            {
                ToggleLaserInSelection(hitLaser);
                FinishSelectionOfLasers();
            }
            else
            {
                SelectLaser(hitLaser, -1);
            }

            return false;
        }

        if (tool == Tool.Select)
        {
            // An empty spot with the select tool: drag a box.
            return BeginBoxSelect(current, mouse);
        }

        if (tool != Tool.Laser)
        {
            ClearSelection();
            Notify("レーザーはレーザーツール (L) で配置します");
            return false;
        }

        double placeTime = Snap(time);
        float value = SnapLaserValue(XToValue(column.Rect, mouse.x));

        if (!current.shift && selectedLaser != null && selectedLaser.side == side)
        {
            // Add a point to the selected laser; the other axis follows the laser's path.
            LaserNoteData laser = selectedLaser;
            int same = Array.FindIndex(laser.points, point => Math.Abs(point.time - placeTime) < 0.0005d);
            if (same >= 0)
            {
                SelectLaser(laser, same);
                dragAxis = axis;
                BeginDrag(DragMode.MovePoint, "Move laser point");
                return true;
            }

            Vector2 along = Evaluate(laser, placeTime);
            LaserPointData added = new LaserPointData
            {
                time = placeTime,
                x = axis == 0 ? value : along.x,
                y = axis == 1 ? value : along.y
            };

            BeginDrag(DragMode.MovePoint, "Add laser point");
            DragModify("Add laser point", () =>
            {
                laser.points = laser.points.Append(added).OrderBy(point => point.time).ToArray();
            });
            SelectLaser(laser, Array.IndexOf(laser.points, added));
            dragAxis = axis;
            return true;
        }

        // A new laser: two points one beat apart, the second one selected to be shaped next.
        LaserNoteData createdLaser = new LaserNoteData
        {
            side = side,
            points = new[]
            {
                new LaserPointData { time = placeTime, x = axis == 0 ? value : 0f, y = axis == 1 ? value : 0f },
                new LaserPointData { time = placeTime + BeatSeconds, x = axis == 0 ? value : 0f, y = axis == 1 ? value : 0f }
            }
        };

        Modify("Add laser", () => Chart.lasers = Chart.lasers.Append(createdLaser).ToArray());
        SelectLaser(createdLaser, 1);
        return false;
    }

    private void OnMouseDrag(Event current, Vector2 mouse)
    {
        double time = YToTime(mouse.y);
        switch (drag)
        {
            case DragMode.Pan:
                viewTime = Math.Max(-2d, dragStartViewTime + (mouse.y - dragStartMouseY) / pixelsPerSecond);
                break;

            case DragMode.Dolly:
                SetZoom(timelineZoomStart * Mathf.Exp((dragStartMouseY - mouse.y) * 0.01f), dragStartMouseY);
                break;

            case DragMode.Scrub:
                cursorTime = Snap(time);
                break;

            case DragMode.MoveAudio:
                {
                    double value = Math.Round((audioDragStartOffset + time - audioDragStartTime) * 1000d) / 1000d;
                    DragModify("Move audio", () => Chart.audioOffset = value);
                    break;
                }

            case DragMode.BoxSelect:
                boxRect = Rect.MinMaxRect(
                    Mathf.Min(boxStart.x, mouse.x),
                    Mathf.Min(boxStart.y, mouse.y),
                    Mathf.Max(boxStart.x, mouse.x),
                    Mathf.Max(boxStart.y, mouse.y));
                break;

            case DragMode.ResizeHold:
                if (dragAnchor != null)
                {
                    TapNoteData note = dragAnchor;
                    double duration = Snap(time) - note.time;
                    duration = duration < 0.0005d ? 0d : duration;
                    if (Math.Abs(duration - note.duration) > 0.00001d)
                    {
                        DragModify("Resize hold", () => note.duration = duration);
                    }
                }

                break;

            case DragMode.MoveNotes:
                DragNotes(time, mouse);
                break;

            case DragMode.MovePoint:
                DragPoint(time, mouse);
                break;

            case DragMode.SetDirection:
                DragDirection(mouse);
                break;

            case DragMode.Erase:
                EraseAt(mouse);
                break;
        }
    }

    private void DragNotes(double time, Vector2 mouse)
    {
        int slot = TryGetColumn(mouse.x, out Column column) && column.Kind == ColumnKind.Lane ? SlotOfLane(column.Index) : -1;
        DragNotesTo(time, slot);
    }

    /// <summary>Moves the dragged notes so the anchor follows <paramref name="time"/>; <paramref name="slot"/> &lt; 0 keeps their lanes.</summary>
    private void DragNotesTo(double time, int slot)
    {
        if (dragAnchor == null || !dragOrigins.TryGetValue(dragAnchor, out (double time, int slot) anchor))
        {
            return;
        }

        double delta = Snap(anchor.time + (time - dragStartMouseTime)) - anchor.time;
        double earliest = dragOrigins.Values.Min(origin => origin.time);
        delta = Math.Max(delta, -earliest);

        int slotDelta = slot >= 0 ? slot - dragStartSlot : 0;

        int minSlot = dragOrigins.Values.Min(origin => origin.slot);
        int maxSlot = dragOrigins.Values.Max(origin => origin.slot);
        slotDelta = Mathf.Clamp(slotDelta, -minSlot, 3 - maxSlot);

        DragModify("Move notes", () =>
        {
            foreach (KeyValuePair<TapNoteData, (double time, int slot)> pair in dragOrigins)
            {
                pair.Key.time = pair.Value.time + delta;
                pair.Key.lane = LaneOrder[pair.Value.slot + slotDelta];
            }
        });
    }

    /// <summary>Moves every selected point by the pointer's travel since the press (time snapped, value snapped per point).</summary>
    private void DragPoints(double time, Vector2 mouse)
    {
        ColumnKind kind = dragAxis == 0 ? ColumnKind.LaserX : ColumnKind.LaserY;
        int found = columns.FindIndex(item => item.Kind == kind && item.Rect.xMin <= mouse.x && mouse.x <= item.Rect.xMax);
        Column column = found >= 0 ? columns[found] : columns.First(item => item.Kind == kind);

        double earliest = pointOrigins.Values.Min(origin => origin.time);
        double delta = Snap(earliest + (time - pointDragStartTime)) - earliest;
        float valueDelta = XToValue(column.Rect, mouse.x) - pointDragStartValue;
        DragModify("Move laser points", () =>
        {
            foreach (KeyValuePair<LaserPointData, (double time, float x, float y)> pair in pointOrigins)
            {
                pair.Key.time = Math.Max(0d, Math.Round(pair.Value.time + delta, 6));
                if (dragAxis == 0)
                {
                    pair.Key.x = SnapLaserValue(pair.Value.x + valueDelta);
                }
                else
                {
                    pair.Key.y = SnapLaserValue(pair.Value.y + valueDelta);
                }
            }
        });
    }

    private void DragPoint(double time, Vector2 mouse)
    {
        if (pointOrigins.Count > 0)
        {
            DragPoints(time, mouse);
            return;
        }

        LaserNoteData laser = selectedLaser;
        if (laser == null || selectedPoint < 0 || selectedPoint >= laser.points.Length)
        {
            return;
        }

        int index = selectedPoint;
        double newTime = Snap(time);
        // A point stays between its neighbours, so the laser's order never flips mid-drag.
        if (index > 0)
        {
            newTime = Math.Max(newTime, laser.points[index - 1].time + 0.001d);
        }

        if (index < laser.points.Length - 1)
        {
            newTime = Math.Min(newTime, laser.points[index + 1].time - 0.001d);
        }

        Column column = columns.First(item => item.Index == laser.side && item.Kind == (dragAxis == 0 ? ColumnKind.LaserX : ColumnKind.LaserY));
        float value = SnapLaserValue(XToValue(column.Rect, mouse.x));
        LaserPointData point = laser.points[index];
        DragModify("Move laser point", () =>
        {
            point.time = newTime;
            if (dragAxis == 0)
            {
                point.x = value;
            }
            else
            {
                point.y = value;
            }
        });
    }

    private void FinishBoxSelect()
    {
        bool additive = Event.current.control || Event.current.command;
        if (!additive)
        {
            selectedNotes.Clear();
        }

        if (selectedLaser != null && additive && !selectedLasers.Contains(selectedLaser))
        {
            selectedLasers.Add(selectedLaser);
        }

        if (!additive)
        {
            selectedLasers.Clear();
        }

        selectedLaser = null;
        selectedPoint = -1;
        if (!additive)
        {
            selectedPoints.Clear();
        }

        foreach (TapNoteData note in Chart.notes)
        {
            if (boxRect.Contains(NoteHeadRect(note).center) && !selectedNotes.Contains(note))
            {
                selectedNotes.Add(note);
            }
        }

        // A laser comes along when the box takes any of its points in its X or Y column.
        foreach (LaserNoteData laser in Chart.lasers)
        {
            if (selectedLasers.Contains(laser))
            {
                continue;
            }

            // Every point in the box: the whole laser comes along; only some: just those points.
            List<LaserPointData> inside = laser.points.Where(point => columns.Any(column => column.Index == laser.side
                && (column.Kind == ColumnKind.LaserX || column.Kind == ColumnKind.LaserY)
                && boxRect.Contains(PointPosition(column.Rect, point, column.Kind == ColumnKind.LaserX ? 0 : 1)))).ToList();
            if (inside.Count == 0)
            {
                continue;
            }

            if (inside.Count == laser.points.Length)
            {
                selectedLasers.Add(laser);
            }
            else
            {
                foreach (LaserPointData point in inside)
                {
                    selectedPoints.Add(point);
                }
            }
        }

        selectedPoints.RemoveWhere(point => selectedLasers.Any(laser => Array.IndexOf(laser.points, point) >= 0));
        FinishSelectionOfLasers();
        FinishSelectionOfPoints();
    }

    /// <summary>A selection that is one laser alone becomes the usual laser selection, so its points can be edited.</summary>
    private void FinishSelectionOfLasers()
    {
        if (selectedNotes.Count == 0 && selectedLasers.Count == 1 && selectedPoints.Count == 0)
        {
            SelectLaser(selectedLasers[0], -1);
        }
    }

    private Vector2 PointPosition(Rect column, LaserPointData point, int axis)
    {
        return new Vector2(ValueToX(column, axis == 0 ? point.x : point.y), TimeToY(point.time));
    }

    private float DistanceToLaser(Rect column, LaserNoteData laser, int axis, Vector2 mouse)
    {
        float best = float.MaxValue;
        for (int index = 0; index < laser.points.Length - 1; index++)
        {
            Vector2 from = PointPosition(column, laser.points[index], axis);
            Vector2 to = PointPosition(column, laser.points[index + 1], axis);
            best = Mathf.Min(best, DistanceToSegment(mouse, from, to));
        }

        return best;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSquared = segment.sqrMagnitude;
        float t = lengthSquared <= 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSquared);
        return Vector2.Distance(point, from + segment * t);
    }

    // ------------------------------------------------------------------ timeline drawing

    private void DrawTimeline(Vector2 size)
    {
        EditorGUI.DrawRect(new Rect(0f, 0f, size.x, size.y), Background);
        // Lines stop at the column header (they used to run up into the toolbar).
        Handles.SetClip(new Rect(0f, HeaderHeight, size.x, size.y - HeaderHeight));

        double bottom = YToTime(size.y);
        double top = YToTime(HeaderHeight);

        // Column backgrounds
        int laneIndex = 0;
        foreach (Column column in columns)
        {
            switch (column.Kind)
            {
                case ColumnKind.Lane:
                    EditorGUI.DrawRect(column.Rect, laneIndex++ % 2 == 0 ? LaneColorA : LaneColorB);
                    break;
                case ColumnKind.LaserX:
                case ColumnKind.LaserY:
                    Color tint = RhythmStageView.GetStickColor(column.Index);
                    EditorGUI.DrawRect(column.Rect, new Color(tint.r * 0.12f + 0.08f, tint.g * 0.12f + 0.08f, tint.b * 0.12f + 0.08f));
                    // -1 / 0 / +1 guides
                    for (int guide = -1; guide <= 1; guide++)
                    {
                        float x = ValueToX(column.Rect, guide);
                        EditorGUI.DrawRect(new Rect(x, 0f, 1f, size.y), new Color(1f, 1f, 1f, guide == 0 ? 0.12f : 0.05f));
                    }

                    break;
                case ColumnKind.Wave:
                    EditorGUI.DrawRect(column.Rect, new Color(0.1f, 0.1f, 0.11f));
                    break;
            }
        }

        DrawWaveform(size, bottom, top);
        DrawGrid(bottom, top);
        DrawSecondsRuler(bottom, top);

        // Song end / audio end
        if (audioClip != null)
        {
            float y = TimeToY(audioClip.length + Chart.audioOffset);
            EditorGUI.DrawRect(new Rect(gridLeft, y, gridRight - gridLeft, 2f), new Color(0.4f, 0.6f, 1f, 0.6f));
        }

        foreach (LaserNoteData laser in Chart.lasers)
        {
            if (LaserEnd(laser) >= bottom - 1d && LaserStart(laser) <= top + 1d && !IsLaserSelected(laser))
            {
                DrawLaser(laser);
            }
        }

        foreach (LaserNoteData laser in SelectedLaserList())
        {
            DrawLaser(laser);
        }

        foreach (TapNoteData note in Chart.notes)
        {
            if (note.EndTime >= bottom - 0.5d && note.time <= top + 0.5d)
            {
                DrawNote(note, selectedNotes.Contains(note));
            }
        }

        // Cursor / playhead
        float cursorY = TimeToY(cursorTime);
        EditorGUI.DrawRect(new Rect(0f, cursorY - 1f, gridRight, 2f), playback.IsPlaying ? new Color(0.3f, 1f, 0.5f) : CursorColor);

        if (drag == DragMode.BoxSelect)
        {
            EditorGUI.DrawRect(boxRect, new Color(SelectColor.r, SelectColor.g, SelectColor.b, 0.12f));
            DrawOutline(boxRect, SelectColor, 1f);
        }
        else if (drag == DragMode.SetDirection && (dirMouse - dirOrigin).magnitude >= DirectionDragThreshold)
        {
            // The pull line of a direction drag, with the direction it will take.
            Handles.color = dirForceFlick ? RhythmStageView.GetStickColor(dragAnchor != null ? dragAnchor.GetStickSide(dirLane) : 0) : Color.white;
            Handles.DrawAAPolyLine(2f, new Vector3(dirOrigin.x, dirOrigin.y, 0f), new Vector3(dirMouse.x, dirMouse.y, 0f));
        }

        Handles.ClearClip();

        // Header
        EditorGUI.DrawRect(new Rect(0f, 0f, size.x, HeaderHeight), new Color(0.2f, 0.2f, 0.22f));
        GUIStyle header = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
        foreach (Column column in columns)
        {
            if (string.IsNullOrEmpty(column.Label))
            {
                continue;
            }

            header.normal.textColor = column.Kind == ColumnKind.LaserX || column.Kind == ColumnKind.LaserY
                ? RhythmStageView.GetStickColor(column.Index)
                : new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(column.Rect.x, 0f, column.Rect.width, HeaderHeight), column.Label, header);
        }

        if (Chart.notes.Length == 0 && Chart.lasers.Length == 0)
        {
            GUIStyle hint = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { alignment = TextAnchor.MiddleCenter };
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(gridLeft, size.y * 0.4f, gridRight - gridLeft, 60f), "レーンをクリックしてノーツを置く\nレーザーの列をクリックしてレーザーを置く", hint);
        }
    }

    private void DrawGrid(double bottom, double top)
    {
        double beat = BeatSeconds;
        double step = GridStep;
        if (step * pixelsPerSecond < 5d)
        {
            step = beat;
        }

        if (step * pixelsPerSecond < 5d)
        {
            step = beat * 4d;
        }

        long first = (long)Math.Ceiling((bottom - Chart.offset) / step);
        long last = (long)Math.Floor((top - Chart.offset) / step);
        if (last - first > 4000)
        {
            return;
        }

        GUIStyle measureStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
        measureStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
        GUIStyle beatStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        beatStyle.normal.textColor = new Color(1f, 1f, 1f, 0.4f);
        for (long index = first; index <= last; index++)
        {
            double time = Chart.offset + index * step;
            double beats = index * step / beat;
            bool onBeat = Math.Abs(beats - Math.Round(beats)) < 0.0001d;
            bool onMeasure = onBeat && (long)Math.Round(beats) % 4 == 0;
            float y = TimeToY(time);
            Color color = onMeasure ? new Color(1f, 1f, 1f, 0.32f) : onBeat ? new Color(1f, 1f, 1f, 0.14f) : new Color(1f, 1f, 1f, 0.05f);
            EditorGUI.DrawRect(new Rect(gridLeft, y, gridRight - gridLeft, 1f), color);
            if (onMeasure)
            {
                long measure = (long)Math.Round(beats) / 4 + 1;
                EditorGUI.DrawRect(new Rect(RulerWidth - 14f, y, 14f, 1f), new Color(1f, 1f, 1f, 0.5f));
                GUI.Label(new Rect(4f, y - 14f, RulerWidth - 6f, 14f), "#" + measure, measureStyle);
            }
            else if (onBeat && beat * pixelsPerSecond >= 26d)
            {
                // Beats inside the measure: the beat number (2, 3, 4) at the ruler's edge.
                long beatIndex = (long)Math.Round(beats);
                long inMeasure = ((beatIndex % 4) + 4) % 4 + 1;
                EditorGUI.DrawRect(new Rect(RulerWidth - 8f, y, 8f, 1f), new Color(1f, 1f, 1f, 0.3f));
                GUI.Label(new Rect(RulerWidth - 24f, y - 13f, 16f, 13f), inMeasure.ToString(), beatStyle);
            }
        }
    }

    private void DrawSecondsRuler(double bottom, double top)
    {
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        style.normal.textColor = new Color(1f, 1f, 1f, 0.4f);
        double stepSeconds = pixelsPerSecond >= 60f ? 1d : 5d;
        for (double second = Math.Ceiling(Math.Max(0d, bottom) / stepSeconds) * stepSeconds; second <= top; second += stepSeconds)
        {
            float y = TimeToY(second);
            EditorGUI.DrawRect(new Rect(RulerWidth - 5f, y, 5f, 1f), new Color(1f, 1f, 1f, 0.3f));
            GUI.Label(new Rect(0f, y - 1f, RulerWidth - 6f, 13f), second.ToString("0") + "s", style);
        }
    }

    private void DrawWaveform(Vector2 size, double bottom, double top)
    {
        if (waveform == null)
        {
            return;
        }

        Column column = columns.First(item => item.Kind == ColumnKind.Wave);
        Rect rect = column.Rect;
        Color color = new Color(0.45f, 0.75f, 1f, 0.7f);
        const float rowHeight = 2f;
        for (float y = HeaderHeight; y < size.y; y += rowHeight)
        {
            double to = YToTime(y) - Chart.audioOffset;
            double from = YToTime(y + rowHeight) - Chart.audioOffset;
            if (to < 0d)
            {
                continue;
            }

            int start = Mathf.Max(0, (int)(from * WaveformRate));
            int end = Mathf.Min(waveform.Length - 1, (int)(to * WaveformRate));
            if (end < 0 || start >= waveform.Length || start > end)
            {
                continue;
            }

            float peak = 0f;
            for (int bin = start; bin <= end; bin++)
            {
                peak = Mathf.Max(peak, waveform[bin]);
            }

            float width = Mathf.Max(1f, peak * (rect.width - 6f));
            EditorGUI.DrawRect(new Rect(rect.center.x - width * 0.5f, y, width, rowHeight), color);
        }
    }

    private void DrawNote(TapNoteData note, bool selected)
    {
        Rect head = NoteHeadRect(note);
        int side = note.GetStickSide(note.lane);
        Color stickColor = RhythmStageView.GetStickColor(side);

        if (note.IsHold)
        {
            Rect body = HoldBodyRect(note);
            EditorGUI.DrawRect(body, new Color(HoldColor.r, HoldColor.g, HoldColor.b, 0.35f));
            Rect tail = HoldTailRect(note);
            EditorGUI.DrawRect(new Rect(tail.x, tail.center.y - 1.5f, tail.width, 3f), HoldColor);
            if (selected)
            {
                DrawOutline(body, SelectColor, 1f);
            }
        }

        if (note.IsSpin)
        {
            // Spin: the turning arrow alone, in a dark box outlined with the stick colour.
            Rect box = new Rect(head.center.x - 11f, head.center.y - 9f, 22f, 18f);
            EditorGUI.DrawRect(box, new Color(0.08f, 0.08f, 0.1f));
            DrawOutline(box, stickColor, 1f);
            DrawSpinIcon(box.center, note.spin, 7f, stickColor);
            if (selected)
            {
                DrawOutline(new Rect(box.x - 2f, box.y - 2f, box.width + 4f, box.height + 4f), SelectColor, 2f);
            }

            return;
        }

        if (note.IsFlickOnly)
        {
            // Flick-only: the chevron alone, in a dark box outlined with the stick colour.
            Rect box = new Rect(head.center.x - 11f, head.center.y - 9f, 22f, 18f);
            EditorGUI.DrawRect(box, new Color(0.08f, 0.08f, 0.1f));
            DrawOutline(box, stickColor, 1f);
            DrawArrow(box.center, note.direction, 7f, stickColor);
            if (selected)
            {
                DrawOutline(new Rect(box.x - 2f, box.y - 2f, box.width + 4f, box.height + 4f), SelectColor, 2f);
            }

            return;
        }

        EditorGUI.DrawRect(head, note.IsHold ? HoldColor : TapColor);
        if (note.HasDirection)
        {
            Rect badge = new Rect(head.center.x - 9f, head.center.y - 9f, 18f, 18f);
            EditorGUI.DrawRect(badge, new Color(0.08f, 0.08f, 0.1f));
            DrawArrow(badge.center, note.direction, 6.5f, stickColor);
        }

        if (selected)
        {
            DrawOutline(new Rect(head.x - 2f, head.y - 2f, head.width + 4f, head.height + 4f), SelectColor, 2f);
        }
    }

    private void DrawLaser(LaserNoteData laser)
    {
        bool selected = IsLaserSelected(laser);
        Color color = RhythmStageView.GetStickColor(laser.side);
        if (!selected)
        {
            color.a = 0.65f;
        }

        foreach (Column column in columns)
        {
            if (column.Index != laser.side || column.Kind != ColumnKind.LaserX && column.Kind != ColumnKind.LaserY)
            {
                continue;
            }

            int axis = column.Kind == ColumnKind.LaserX ? 0 : 1;
            Vector3[] line = laser.points.Select(point => (Vector3)PointPosition(column.Rect, point, axis)).ToArray();
            if (line.Length >= 2)
            {
                Handles.color = color;
                Handles.DrawAAPolyLine(selected ? 4f : 2.5f, line);
            }

            for (int index = 0; index < line.Length; index++)
            {
                bool pointSelected = laser == selectedLaser && index == selectedPoint || selectedPoints.Contains(laser.points[index]);
                float half = pointSelected ? 5f : 3.5f;
                Rect rect = new Rect(line[index].x - half, line[index].y - half, half * 2f, half * 2f);
                EditorGUI.DrawRect(rect, pointSelected ? Color.white : color);
                if (pointSelected)
                {
                    DrawOutline(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), SelectColor, 2f);
                }
            }
        }
    }

    /// <summary>The spin mark: an arc over the top with an arrowhead (counter-clockwise ends on the left, clockwise on the right).</summary>
    private static void DrawSpinIcon(Vector2 center, int spin, float size, Color color)
    {
        float mirror = spin == TapNoteData.SpinClockwise ? -1f : 1f;
        const float startDegrees = -15f;
        const float endDegrees = 195f;
        const int segments = 14;
        Vector2 Point(float degrees, float radius)
        {
            float radians = degrees * Mathf.Deg2Rad;
            // GUI space: y grows downward; the arc's circle sits a little lower so the mark is centred in the box.
            return center + new Vector2(0f, size * 0.45f) + new Vector2(Mathf.Cos(radians) * mirror, -Mathf.Sin(radians)) * (radius * size * 0.75f);
        }

        Vector3[] line = new Vector3[segments + 1];
        for (int index = 0; index <= segments; index++)
        {
            line[index] = Point(Mathf.Lerp(startDegrees, endDegrees, index / (float)segments), 1f);
        }

        Handles.color = color;
        Handles.DrawAAPolyLine(2f, line);

        float endRadians = endDegrees * Mathf.Deg2Rad;
        Vector2 end = Point(endDegrees, 1f);
        Vector2 radial = new Vector2(Mathf.Cos(endRadians) * mirror, -Mathf.Sin(endRadians)).normalized;
        Vector2 tangent = new Vector2(-Mathf.Sin(endRadians) * mirror, -Mathf.Cos(endRadians)).normalized;
        float head = size * 0.5f;
        Handles.DrawAAConvexPolygon(end + radial * head, end - radial * head, end + tangent * head * 1.3f);
    }

    private static void DrawArrow(Vector2 center, NoteDirection direction, float size, Color color)
    {
        if (direction == NoteDirection.None)
        {
            EditorGUI.DrawRect(new Rect(center.x - 2f, center.y - 2f, 4f, 4f), color);
            return;
        }

        Vector2 vector = StickInput.GetDirectionVector(direction);
        // GUI space: y grows downward.
        Vector2 forward = new Vector2(vector.x, -vector.y).normalized;
        Vector2 across = new Vector2(-forward.y, forward.x);
        Vector2 tip = center + forward * size;
        Vector2 baseCenter = center - forward * size * 0.55f;
        Vector2 notch = center - forward * size * 0.05f;
        Handles.color = color;
        Handles.DrawAAConvexPolygon(tip, baseCenter + across * size * 0.9f, notch);
        Handles.DrawAAConvexPolygon(tip, notch, baseCenter - across * size * 0.9f);
    }

    private static void DrawOutline(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    // ------------------------------------------------------------------ chart preview sidebar


    private void DrawPreviewNote(TapNoteData note, Rect lanes, float laneWidth, float judgeY, float span, double lookAhead)
    {
        int slot = SlotOfLane(note.lane);
        float x = lanes.x + slot * laneWidth;
        float headY = judgeY - (float)(Math.Max(0d, note.time - cursorTime) / lookAhead) * span;
        Color stickColor = RhythmStageView.GetStickColor(note.GetStickSide(note.lane));

        if (note.IsHold && !note.IsFlickOnly)
        {
            float tailY = judgeY - (float)(Math.Min(lookAhead, note.EndTime - cursorTime) / lookAhead) * span;
            EditorGUI.DrawRect(new Rect(x + laneWidth * 0.3f, tailY, laneWidth * 0.4f, headY - tailY), new Color(HoldColor.r, HoldColor.g, HoldColor.b, 0.45f));
        }

        if (note.time < cursorTime && !note.IsHold)
        {
            return;
        }

        if (note.IsSpin)
        {
            Rect spinBox = new Rect(x + laneWidth * 0.5f - 11f, headY - 9f, 22f, 18f);
            EditorGUI.DrawRect(spinBox, new Color(0.08f, 0.08f, 0.1f));
            DrawOutline(spinBox, stickColor, 1f);
            DrawSpinIcon(spinBox.center, note.spin, 7f, stickColor);
            return;
        }

        if (note.IsFlickOnly)
        {
            Rect box = new Rect(x + laneWidth * 0.5f - 11f, headY - 9f, 22f, 18f);
            EditorGUI.DrawRect(box, new Color(0.08f, 0.08f, 0.1f));
            DrawOutline(box, stickColor, 1f);
            DrawArrow(box.center, note.direction, 7f, stickColor);
            return;
        }

        Rect bar = new Rect(x + 3f, headY - 4f, laneWidth - 6f, 8f);
        EditorGUI.DrawRect(bar, note.IsHold ? HoldColor : TapColor);
        if (note.HasDirection)
        {
            Rect badge = new Rect(bar.center.x - 9f, bar.center.y - 9f, 18f, 18f);
            EditorGUI.DrawRect(badge, new Color(0.08f, 0.08f, 0.1f));
            DrawArrow(badge.center, note.direction, 6.5f, stickColor);
        }
    }

    private void DrawPreviewStick(Rect pad, int side, double lookAhead)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color color = RhythmStageView.GetStickColor(side);
        Vector2 center = pad.center;
        float radius = pad.width * 0.5f - 8f;
        EditorGUI.DrawRect(pad, new Color(0.1f, 0.1f, 0.12f));
        Handles.SetClip(pad);
        Handles.color = new Color(1f, 1f, 1f, 0.25f);
        Handles.DrawWireDisc(center, Vector3.forward, radius);
        Handles.DrawLine(center + Vector2.left * radius, center + Vector2.right * radius);
        Handles.DrawLine(center + Vector2.up * radius, center + Vector2.down * radius);
        DrawPadSquare(center, radius);
        GUI.Label(new Rect(pad.x + 2f, pad.y, 80f, 14f), side == 0 ? "左スティック" : "右スティック", EditorStyles.miniLabel);

        Vector2 ToPad(Vector2 value)
        {
            value = StickInput.ClampToSquare(value);
            return center + new Vector2(value.x, -value.y) * radius;
        }

        foreach (LaserNoteData laser in Chart.lasers)
        {
            if (laser.side != side || laser.points.Length < 2 || LaserEnd(laser) < cursorTime || LaserStart(laser) > cursorTime + lookAhead)
            {
                continue;
            }

            bool active = LaserStart(laser) <= cursorTime;
            double from = Math.Max(cursorTime, LaserStart(laser));
            double to = Math.Min(cursorTime + lookAhead, LaserEnd(laser));
            const int steps = 24;
            Vector2 previous = ToPad(Evaluate(laser, from));
            for (int step = 1; step <= steps; step++)
            {
                Vector2 next = ToPad(Evaluate(laser, from + (to - from) * step / steps));
                Color fade = color;
                fade.a = (active ? 0.9f : 0.5f) * (1f - step / (float)steps * 0.6f);
                Handles.color = fade;
                Handles.DrawAAPolyLine(3f, previous, next);
                previous = next;
            }

            Vector2 head = ToPad(Evaluate(laser, from));
            float half = active ? 6f : 4f;
            EditorGUI.DrawRect(new Rect(head.x - half, head.y - half, half * 2f, half * 2f), active ? Color.white : color);
        }

        Handles.ClearClip();
    }

    // ------------------------------------------------------------------ side panel


    private static readonly string[] GenreLabels = Array.ConvertAll(SongGenre.All, genre => $"{genre.Letter}  {genre.Name} ({genre.Id})");

    private static readonly string[] DifficultyLabels = Array.ConvertAll(SongDifficulty.All, difficulty => difficulty.Name);

    private void DrawChartSection()
    {
        EditorGUILayout.LabelField("譜面", EditorStyles.boldLabel);
        SongChartData chart = Chart;

        EditorGUI.BeginChangeCheck();
        string title = EditorGUILayout.TextField("曲名", chart.title);
        string artist = EditorGUILayout.TextField("アーティスト", chart.artist);
        int level = EditorGUILayout.IntSlider("レベル", chart.level, 1, 20);
        int difficulty = EditorGUILayout.Popup(new GUIContent("難易度", "曲選択の路線の色。省略はLocal。"), chart.Difficulty.Index, DifficultyLabels);
        int genreIndex = EditorGUILayout.Popup(new GUIContent("ジャンル", "曲選択のフォルダ。未指定はオリジナル。"), chart.Genre.Index, GenreLabels);
        float bpm = EditorGUILayout.FloatField("BPM", chart.bpm);
        double offset = EditorGUILayout.DoubleField(new GUIContent("オフセット", "最初の拍の時刻（秒）。グリッドの基準。"), chart.offset);
        if (EditorGUI.EndChangeCheck())
        {
            Modify("Edit chart info", () =>
            {
                chart.title = title;
                chart.artist = artist;
                chart.level = level;
                chart.difficulty = difficulty;
                // Only a changed popup writes the genre, so an omitted (or unknown) value is kept.
                if (genreIndex != chart.Genre.Index)
                {
                    chart.genre = SongGenre.All[genreIndex].Id;
                }

                chart.bpm = Mathf.Max(1f, bpm);
                chart.offset = offset;
            });
        }

        if (GUILayout.Button("オフセットを再生位置にする", EditorStyles.miniButton))
        {
            double cursor = cursorTime;
            Modify("Set offset", () => chart.offset = Math.Round(cursor * 10000d) / 10000d);
        }

        AudioClip clip = GetAudioClip();
        EditorGUI.BeginChangeCheck();
        string audioPath = EditorGUILayout.TextField("音源", chart.audio ?? string.Empty);
        if (EditorGUI.EndChangeCheck())
        {
            Modify("Set audio", () => chart.audio = audioPath.Trim());
        }

        EditorGUILayout.LabelField("Resources 内のパス（拡張子なし）例: Audio/elevate", EditorStyles.wordWrappedMiniLabel);

#if UNITY_EDITOR
        if (GUILayout.Button("音源ファイルを取り込む…", EditorStyles.miniButton))
        {
            Defer(ImportAudio);
        }
#endif

        if (clip != null)
        {
            EditorGUI.BeginChangeCheck();
            double audioOffset = EditorGUILayout.DoubleField(new GUIContent("音源のずれ(秒)", "音源を譜面に対して何秒遅らせるか（負で早める）。波形列をドラッグしても調整できる。"), chart.audioOffset);
            if (EditorGUI.EndChangeCheck())
            {
                Modify("Set audio offset", () => chart.audioOffset = audioOffset);
            }

            if (GUILayout.Button("波形から開始位置を自動調整", EditorStyles.miniButton))
            {
                AutoAlignAudio();
            }

            EditorGUILayout.LabelField("波形列をドラッグ: 音源の位置を調整", EditorStyles.wordWrappedMiniLabel);
        }

        if (!string.IsNullOrWhiteSpace(chart.audio) && clip == null)
        {
            EditorGUILayout.HelpBox($"Resources/{chart.audio} が見つかりません。", MessageType.Warning);
        }
        else if (!string.IsNullOrEmpty(waveformNote))
        {
            EditorGUILayout.HelpBox(waveformNote, MessageType.Info);
        }

        musicVolume = EditorGUILayout.Slider("音量", musicVolume, 0f, 1f);
        clapVolume = EditorGUILayout.Slider("クラップ音量", clapVolume, 0f, 1f);
    }







    /// <summary>A faint square at the edge of the area a laser point may use (the circle is the stick's own reach).</summary>
    private static void DrawPadSquare(Vector2 center, float radius)
    {
        Color previous = Handles.color;
        Handles.color = new Color(1f, 1f, 1f, 0.12f);
        Vector2 topLeft = center + new Vector2(-radius, -radius);
        Vector2 topRight = center + new Vector2(radius, -radius);
        Vector2 bottomRight = center + new Vector2(radius, radius);
        Vector2 bottomLeft = center + new Vector2(-radius, radius);
        Handles.DrawLine(topLeft, topRight);
        Handles.DrawLine(topRight, bottomRight);
        Handles.DrawLine(bottomRight, bottomLeft);
        Handles.DrawLine(bottomLeft, topLeft);
        Handles.color = previous;
    }


    private void MovePadPoint(LaserNoteData laser, Vector2 mouse, Vector2 center, float radius)
    {
        if (selectedPoint < 0 || selectedPoint >= laser.points.Length)
        {
            return;
        }

        Vector2 value = (mouse - center) / radius;
        value.y = -value.y;
        value = StickInput.ClampToSquare(value);
        float x = SnapLaserValue(value.x);
        float y = SnapLaserValue(value.y);
        LaserPointData point = laser.points[selectedPoint];
        DragModify("Move laser point", () =>
        {
            point.x = x;
            point.y = y;
        });
    }

    private void DrawStatsSection()
    {
        SongChartData chart = Chart;
        EditorGUILayout.LabelField("統計", EditorStyles.boldLabel);
        int flickOnly = chart.notes.Count(note => note.IsFlickOnly);
        int spins = chart.notes.Count(note => note.IsSpin);
        EditorGUILayout.LabelField(
            $"ノーツ {chart.notes.Length}（ロング {chart.HoldCount} / 方向付き {chart.AttachedFlickCount} / フリックのみ {flickOnly} / 回転 {spins}）",
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField($"レーザー {chart.lasers.Length}　判定数 {chart.JudgementCount}", EditorStyles.miniLabel);
        TimeSpan length = TimeSpan.FromSeconds(Math.Max(0d, chart.EndTime));
        EditorGUILayout.LabelField($"長さ {(int)length.TotalMinutes}:{length.Seconds:00}", EditorStyles.miniLabel);
    }

}
