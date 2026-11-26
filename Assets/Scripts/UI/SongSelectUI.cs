using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Song select drawn as a route map of the chosen genre's line: the songs are its
/// stations down a vertical line on the left, which bends off to the top left. The
/// selected station is a large badge with the song's title and artist beside it; the
/// others shrink with their distance along the line. Up / down move along the line,
/// left / right pick the song's difficulty (charts with the same title and artist, see
/// <see cref="ChartLoader.GetSongGroups"/>), confirm opens the selected-song sign.
/// </summary>
public sealed class SongSelectUI : MonoBehaviour
{
    private const string Footer = "↑ ↓  SELECT        ← →  DIFFICULTY        ENTER / A  DECIDE        ESC / B  GENRE        TAB / Y  OPTIONS";

    // Route map layout, in canvas units from the top left of the screen.
    private const float LineX = 330f;
    private const float LineWidth = 30f;
    private const float BendY = 200f;
    private const float BendRadius = 110f;
    private const float SelectedY = 700f;
    private const float FirstGap = 280f;
    private const float StationGap = 190f;
    private const float SelectedDiameter = 230f;
    private const float NeighbourDiameter = 100f;
    private const float FarDiameter = 64f;
    private const float ShrinkPerStation = 16f;
    private const float TextX = 545f;
    private const float ScrollSmoothSeconds = 0.08f;
    private const float ColorSmoothRate = 14f;
    private const float DetailSlideSeconds = 0.22f;
    private const float SongSlideDistance = 40f;
    private const float Diagonal = 0.70710678f;

    // The difficulty row above the title: one tab per grade, a filled cursor on the chosen one.
    private const float GradeRowLift = 172f;
    private const float TabHeight = 52f;
    private const float TabGap = 12f;
    private const float TabPad = 22f;
    private const float TabLevelWidth = 34f;
    private const int TabFontSize = 24;
    private const float ArrowSpace = 38f;
    private const float CursorSmoothRate = 18f;
    private const float TabFadeRate = 14f;
    private const float GradeSlideDistance = 80f;
    private const float GradePunchSeconds = 0.28f;
    private const float BadgeFlipSeconds = 0.34f;
    private const float BumpSeconds = 0.36f;
    private const float BumpDistance = 12f;

    private sealed class Station
    {
        /// <summary>Library indices of the song's charts, lowest grade first.</summary>
        public List<int> Charts;

        /// <summary>Position in <see cref="Charts"/> of the chosen difficulty.</summary>
        public int Chart;

        public LineBadge Badge;
        public Text Label;

        public int SongIndex => Charts[Chart];
    }

    private sealed class GradeTab
    {
        public SongDifficulty Grade;
        public RoundedRectGraphic Frame;
        public Text Name;
        public Text Level;
        public float X;
        public float Width;
        public float Alpha = 1f;
    }

    private readonly List<Station> stations = new List<Station>();
    private readonly List<GradeTab> tabs = new List<GradeTab>();
    private GameFlow flow;
    private ChartLoader charts;
    private SongGenre genre;
    private RectTransform stationLayer;
    private AngularGraphic lineVertical;
    private AngularGraphic lineDiagonal;
    private CircleGraphic lineBend;
    private LineBadge headerBadge;
    private Text headerName;
    private Text headerEnglish;
    private RectTransform detail;
    private CanvasGroup detailGroup;
    private Text titleText;
    private Text artistText;
    private Text infoText;
    private Text watermark;
    private RectTransform gradeRow;
    private RoundedRectGraphic gradeCursor;
    private ChevronGraphic gradeArrowLeft;
    private ChevronGraphic gradeArrowRight;
    private Color lineColor = SignKit.MetroRed;
    private int selected;
    private float scroll;
    private float scrollVelocity;
    private float detailChangedAt;
    private float detailSlideFrom = SongSlideDistance;
    private bool selectedStationHidden;

    // Difficulty: the grade kept while moving along the line, and the animation state.
    private int preferredGrade;
    private float cursorX;
    private float cursorWidth;
    private Color cursorColor = SignKit.MetroRed;
    private float gradePunchAt = -10f;
    private float bumpAt = -10f;
    private int bumpDirection;
    private float flipAt = -10f;
    private int flipStation = -1;
    private Color flipFromColor;

    public CanvasGroup Group { get; private set; }

    /// <summary>Library index of the selected song's chosen chart, -1 when the line has none.</summary>
    public int SelectedSongIndex => stations.Count > 0 ? stations[selected].SongIndex : -1;

    /// <summary>The selected song's station number on its line, "01"...</summary>
    public string SelectedStationNumber => SongGenre.FormatStation(selected + 1);

    public LineBadge SelectedBadge => stations.Count > 0 ? stations[selected].Badge : null;

    /// <summary>The chosen chart's difficulty colour: what the line takes (the genre's colour when the line has no songs).</summary>
    public Color SelectedColor => stations.Count > 0 ? SongOf(stations[selected]).Difficulty.Color : (genre ?? SongGenre.Default).Color;

    public void Build(GameFlow gameFlow, ChartLoader chartLoader)
    {
        flow = gameFlow;
        charts = chartLoader;
        RectTransform root = (RectTransform)transform;
        Group = gameObject.AddComponent<CanvasGroup>();
        Vector2 topLeft = new Vector2(0f, 1f);

        RoundedRectGraphic face = SignKit.CreateBoard(root, "Board", 26f, 10f);
        face.raycastTarget = true;

        // The selected station's number, huge and faint behind everything.
        watermark = SignKit.CreateText(root, "Watermark", string.Empty, 620, TextAnchor.MiddleRight, SignKit.Watermark, true);
        watermark.rectTransform.anchorMin = new Vector2(1f, 1f);
        watermark.rectTransform.anchorMax = new Vector2(1f, 1f);
        watermark.rectTransform.pivot = new Vector2(1f, 0.5f);
        watermark.rectTransform.sizeDelta = new Vector2(1100f, 700f);
        watermark.rectTransform.anchoredPosition = new Vector2(-40f, -(SelectedY - 60f));

        // The line: straight down the left, curving off to the top left above the stations.
        lineVertical = UiKit.CreatePanel(root, "LineVertical", SignKit.MetroRed);
        lineVertical.rectTransform.anchorMin = topLeft;
        lineVertical.rectTransform.anchorMax = topLeft;
        lineVertical.rectTransform.pivot = new Vector2(0.5f, 1f);
        lineVertical.rectTransform.sizeDelta = new Vector2(LineWidth, 1400f);
        lineVertical.rectTransform.anchoredPosition = new Vector2(LineX, -BendY);

        lineDiagonal = UiKit.CreatePanel(root, "LineDiagonal", SignKit.MetroRed);
        lineDiagonal.rectTransform.anchorMin = topLeft;
        lineDiagonal.rectTransform.anchorMax = topLeft;
        lineDiagonal.rectTransform.pivot = new Vector2(0.5f, 0f);
        lineDiagonal.rectTransform.sizeDelta = new Vector2(LineWidth, 900f);
        lineDiagonal.rectTransform.anchoredPosition = GetPathPoint(SelectedY - BendY + BendLength);
        lineDiagonal.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        // An eighth of a circle joins the two straight parts.
        float bendSize = (BendRadius + LineWidth * 0.5f) * 2f;
        lineBend = SignKit.CreateCircle(root, "LineBend", SignKit.MetroRed, LineWidth);
        lineBend.SetArc(0f, 45f);
        lineBend.rectTransform.anchorMin = topLeft;
        lineBend.rectTransform.anchorMax = topLeft;
        lineBend.rectTransform.sizeDelta = new Vector2(bendSize, bendSize);
        lineBend.rectTransform.anchoredPosition = new Vector2(LineX - BendRadius, -BendY);

        stationLayer = UiKit.CreateRect(root, "Stations");
        UiKit.Stretch(stationLayer);

        BuildDetail(root);
        BuildGradeRow(root);
        BuildHeader(root);

        Text footer = SignKit.CreateText(root, "Footer", Footer, 24, TextAnchor.MiddleRight, SignKit.InkDim, true);
        UiKit.Place(footer, new Vector2(1f, 0f), new Vector2(-66f, 54f), new Vector2(1700f, 34f));
    }

    /// <summary>Shows the line of <paramref name="lineGenre"/> with <paramref name="songIndex"/> (a library index) selected if it is on it.</summary>
    public void Show(SongGenre lineGenre, int songIndex)
    {
        gameObject.SetActive(true);
        Group.alpha = 1f;
        genre = lineGenre ?? SongGenre.Default;
        BuildStations();

        selected = 0;
        bool found = false;
        for (int index = 0; index < stations.Count && !found; index++)
        {
            int chart = stations[index].Charts.IndexOf(songIndex);
            if (chart >= 0)
            {
                selected = index;
                stations[index].Chart = chart;
                preferredGrade = SongOf(stations[index]).Difficulty.Index;
                found = true;
            }
        }

        if (!found && stations.Count > 0)
        {
            stations[selected].Chart = GetClosestChart(stations[selected], preferredGrade);
        }

        scroll = selected;
        scrollVelocity = 0f;
        selectedStationHidden = false;
        flipStation = -1;
        gradePunchAt = -10f;
        bumpAt = -10f;

        // The line is drawn in the chosen chart's difficulty colour (stations carry their own).
        lineColor = SelectedColor;
        ApplyLineColor();
        headerBadge.SetCode(genre.Letter, SongGenre.FormatStation(stations.Count));
        headerName.text = genre.Name;
        headerEnglish.text = $"{genre.EnglishName}   ·   {stations.Count} {(stations.Count == 1 ? "song" : "songs")}";

        RefreshDetail(SongSlideDistance);
        SnapGradeCursor();
        LayoutStations();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>Hides the selected station's badge while another badge flies into its place.</summary>
    public void SetSelectedStationHidden(bool hidden)
    {
        selectedStationHidden = hidden;
        LayoutStations();
    }

    private void Update()
    {
        scroll = Mathf.SmoothDamp(scroll, selected, ref scrollVelocity, ScrollSmoothSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
        lineColor = Color.Lerp(lineColor, SelectedColor, 1f - Mathf.Exp(-Time.unscaledDeltaTime * ColorSmoothRate));
        ApplyLineColor();
        LayoutStations();
        AnimateDetail();
        AnimateGradeRow();

        if (flow.IsBusy || flow.CurrentState != GameFlow.State.SongSelect || stations.Count == 0)
        {
            return;
        }

        float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
        if (MenuInput.Up || wheel > 0.01f)
        {
            Select(selected - 1);
        }

        if (MenuInput.Down || wheel < -0.01f)
        {
            Select(selected + 1);
        }

        if (MenuInput.Left)
        {
            StepDifficulty(-1);
        }

        if (MenuInput.Right)
        {
            StepDifficulty(1);
        }

        if (MenuInput.Confirm)
        {
            MenuInput.Consume();
            Decide();
        }
        else if (MenuInput.Cancel)
        {
            MenuInput.Consume();
            flow.BackToGenreSelect();
        }
        else if (MenuInput.Option)
        {
            MenuInput.Consume();
            flow.OpenOptions();
        }
    }

    private SongChartData SongOf(Station station)
    {
        return charts.Songs[station.SongIndex];
    }

    private void ApplyLineColor()
    {
        lineVertical.color = lineColor;
        lineDiagonal.color = lineColor;
        lineBend.color = lineColor;
        headerBadge.SetLineColor(lineColor);
    }

    private void Select(int index)
    {
        index = Mathf.Clamp(index, 0, stations.Count - 1);
        if (index == selected)
        {
            return;
        }

        selected = index;
        // Moving along the line keeps the difficulty, or the nearest one the song has.
        stations[selected].Chart = GetClosestChart(stations[selected], preferredGrade);
        RefreshDetail(SongSlideDistance);
    }

    /// <summary>Left / right: the next lower or higher difficulty of the selected song; a bump at either end.</summary>
    private void StepDifficulty(int direction)
    {
        Station station = stations[selected];
        int chart = station.Chart + direction;
        if (chart < 0 || chart >= station.Charts.Count)
        {
            bumpAt = Time.unscaledTime;
            bumpDirection = direction;
            return;
        }

        SetChart(chart);
    }

    private void SetChart(int chart)
    {
        Station station = stations[selected];
        if (chart == station.Chart || chart < 0 || chart >= station.Charts.Count)
        {
            return;
        }

        int direction = chart > station.Chart ? 1 : -1;
        flipFromColor = station.Badge.Ring.color;
        flipAt = Time.unscaledTime;
        flipStation = selected;
        gradePunchAt = Time.unscaledTime;

        station.Chart = chart;
        preferredGrade = SongOf(station).Difficulty.Index;
        RefreshDetail(GradeSlideDistance * direction);
    }

    /// <summary>The station's chart whose grade is nearest to <paramref name="grade"/> (the lower one on a tie).</summary>
    private int GetClosestChart(Station station, int grade)
    {
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int index = 0; index < station.Charts.Count; index++)
        {
            int distance = Mathf.Abs(charts.Songs[station.Charts[index]].Difficulty.Index - grade);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The station's chart of <paramref name="grade"/>, -1 if the song has none.</summary>
    private int FindChart(Station station, int grade)
    {
        for (int index = 0; index < station.Charts.Count; index++)
        {
            if (charts.Songs[station.Charts[index]].Difficulty.Index == grade)
            {
                return index;
            }
        }

        return -1;
    }

    private void Decide()
    {
        if (stations.Count > 0)
        {
            flow.ConfirmSong(stations[selected].SongIndex, SelectedStationNumber);
        }
    }

    private void BuildDetail(RectTransform root)
    {
        detail = UiKit.CreateRect(root, "Detail");
        detail.anchorMin = new Vector2(0f, 1f);
        detail.anchorMax = new Vector2(0f, 1f);
        detail.pivot = new Vector2(0f, 0.5f);
        detail.sizeDelta = new Vector2(1300f, 300f);
        detail.anchoredPosition = new Vector2(TextX, -SelectedY);
        detailGroup = detail.gameObject.AddComponent<CanvasGroup>();
        detailGroup.blocksRaycasts = true;

        Vector2 left = new Vector2(0f, 0.5f);
        titleText = SignKit.CreateFittedText(detail, "SongTitle", 104, 48, TextAnchor.MiddleLeft, SignKit.Ink, true);
        UiKit.Place(titleText, left, new Vector2(0f, 58f), new Vector2(1300f, 136f));
        SignKit.AddRelay(titleText).Clicked = eventData =>
        {
            if (!flow.IsBusy && flow.CurrentState == GameFlow.State.SongSelect)
            {
                Decide();
            }
        };

        artistText = SignKit.CreateFittedText(detail, "Artist", 44, 28, TextAnchor.MiddleLeft, SignKit.InkSoft, false);
        UiKit.Place(artistText, left, new Vector2(4f, -36f), new Vector2(1300f, 60f));

        infoText = SignKit.CreateText(detail, "Info", string.Empty, 30, TextAnchor.MiddleLeft, SignKit.InkDim, true);
        UiKit.Place(infoText, left, new Vector2(6f, -100f), new Vector2(1300f, 44f));
    }

    /// <summary>
    /// The difficulty row: a pill per grade (LOCAL ... LIMITED EXPRESS) with the song's
    /// level in it, a filled cursor in the chosen grade's colour that slides between them,
    /// and a chevron at each end that is lit while there is a difficulty that way.
    /// </summary>
    private void BuildGradeRow(RectTransform root)
    {
        gradeRow = UiKit.CreateRect(root, "Difficulty");
        gradeRow.anchorMin = new Vector2(0f, 1f);
        gradeRow.anchorMax = new Vector2(0f, 1f);
        gradeRow.pivot = new Vector2(0f, 0.5f);
        gradeRow.sizeDelta = new Vector2(1300f, TabHeight + 20f);
        gradeRow.anchoredPosition = new Vector2(TextX, -(SelectedY - GradeRowLift));

        Vector2 left = new Vector2(0f, 0.5f);
        gradeArrowLeft = SignKit.CreateChevron(gradeRow, "ArrowLeft", SignKit.Ink, 6f);
        PlaceCentred(gradeArrowLeft.rectTransform, 9f, new Vector2(18f, 30f));
        gradeArrowLeft.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);

        // Frames first, then the cursor over them, then the texts over the cursor.
        float x = ArrowSpace;
        foreach (SongDifficulty grade in SongDifficulty.All)
        {
            GradeTab tab = new GradeTab { Grade = grade };
            tab.Name = SignKit.CreateText(gradeRow, $"Name_{grade.Index}", grade.Name.ToUpperInvariant(), TabFontSize, TextAnchor.MiddleLeft, SignKit.Ink, true);
            float nameWidth = Mathf.Ceil(tab.Name.preferredWidth);
            tab.Width = TabPad + nameWidth + 14f + TabLevelWidth + TabPad;
            tab.X = x + tab.Width * 0.5f;
            x += tab.Width + TabGap;

            tab.Frame = SignKit.CreateRoundedRect(gradeRow, $"Tab_{grade.Index}", grade.Color, TabHeight * 0.5f, 3f);
            PlaceCentred(tab.Frame.rectTransform, tab.X, new Vector2(tab.Width, TabHeight));
            int gradeIndex = grade.Index;
            SignKit.AddRelay(tab.Frame).Clicked = eventData => OnTabClicked(gradeIndex);

            UiKit.Place(tab.Name, left, new Vector2(tab.X - tab.Width * 0.5f + TabPad, TabFontSize * SignKit.CapRaise), new Vector2(nameWidth + 10f, TabHeight));
            tab.Level = SignKit.CreateText(gradeRow, $"Level_{grade.Index}", string.Empty, TabFontSize, TextAnchor.MiddleRight, SignKit.InkSoft, true);
            UiKit.Place(tab.Level, left, new Vector2(tab.X + tab.Width * 0.5f - TabPad - TabLevelWidth, TabFontSize * SignKit.CapRaise), new Vector2(TabLevelWidth, TabHeight));
            tabs.Add(tab);
        }

        gradeCursor = SignKit.CreateRoundedRect(gradeRow, "Cursor", SignKit.MetroRed, TabHeight * 0.5f);
        PlaceCentred(gradeCursor.rectTransform, tabs[0].X, new Vector2(tabs[0].Width, TabHeight));
        int firstText = tabs[0].Name.rectTransform.GetSiblingIndex();
        gradeCursor.rectTransform.SetSiblingIndex(firstText);
        foreach (GradeTab tab in tabs)
        {
            // Every frame below the cursor, every text above it.
            tab.Frame.rectTransform.SetSiblingIndex(0);
        }

        gradeArrowRight = SignKit.CreateChevron(gradeRow, "ArrowRight", SignKit.Ink, 6f);
        PlaceCentred(gradeArrowRight.rectTransform, x - TabGap + ArrowSpace - 9f, new Vector2(18f, 30f));
    }

    private static void PlaceCentred(RectTransform rect, float x, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(x, 0f);
    }

    private void OnTabClicked(int grade)
    {
        if (flow.IsBusy || flow.CurrentState != GameFlow.State.SongSelect || stations.Count == 0)
        {
            return;
        }

        int chart = FindChart(stations[selected], grade);
        if (chart < 0)
        {
            bumpAt = Time.unscaledTime;
            bumpDirection = grade > SongOf(stations[selected]).Difficulty.Index ? 1 : -1;
            return;
        }

        SetChart(chart);
    }

    private void BuildHeader(RectTransform root)
    {
        Vector2 topRight = new Vector2(1f, 1f);
        headerBadge = LineBadge.Create(root, "LineBadge", SignKit.MetroRed, 124f);
        headerBadge.Rect.anchorMin = topRight;
        headerBadge.Rect.anchorMax = topRight;
        headerBadge.Rect.anchoredPosition = new Vector2(-132f, -130f);

        headerName = SignKit.CreateText(root, "LineName", string.Empty, 62, TextAnchor.MiddleRight, SignKit.Ink, true);
        headerName.rectTransform.anchorMin = topRight;
        headerName.rectTransform.anchorMax = topRight;
        headerName.rectTransform.pivot = new Vector2(1f, 0.5f);
        headerName.rectTransform.sizeDelta = new Vector2(900f, 80f);
        headerName.rectTransform.anchoredPosition = new Vector2(-222f, -108f);

        headerEnglish = SignKit.CreateText(root, "LineEnglish", string.Empty, 30, TextAnchor.MiddleRight, SignKit.InkSoft);
        headerEnglish.rectTransform.anchorMin = topRight;
        headerEnglish.rectTransform.anchorMax = topRight;
        headerEnglish.rectTransform.pivot = new Vector2(1f, 0.5f);
        headerEnglish.rectTransform.sizeDelta = new Vector2(900f, 40f);
        headerEnglish.rectTransform.anchoredPosition = new Vector2(-224f, -164f);
    }

    private void BuildStations()
    {
        foreach (Station station in stations)
        {
            Destroy(station.Badge.gameObject);
            Destroy(station.Label.gameObject);
        }

        stations.Clear();
        List<List<int>> songs = charts.GetSongGroups(genre);
        Vector2 topLeft = new Vector2(0f, 1f);
        for (int index = 0; index < songs.Count; index++)
        {
            Station station = new Station { Charts = songs[index] };
            SongChartData song = SongOf(station);

            // Labels first, so every badge is drawn over the line and the labels.
            station.Label = SignKit.CreateText(stationLayer, $"Label_{index + 1:00}", song.title, 34, TextAnchor.MiddleLeft, SignKit.InkSoft, true);
            station.Label.rectTransform.anchorMin = topLeft;
            station.Label.rectTransform.anchorMax = topLeft;
            station.Label.rectTransform.pivot = new Vector2(0f, 0.5f);
            station.Label.rectTransform.sizeDelta = new Vector2(1200f, 50f);

            station.Badge = LineBadge.Create(stationLayer, $"Station_{index + 1:00}", song.Difficulty.Color, NeighbourDiameter);
            station.Badge.Rect.anchorMin = topLeft;
            station.Badge.Rect.anchorMax = topLeft;
            station.Badge.SetCode(genre.Letter, SongGenre.FormatStation(index + 1));

            int stationIndex = index;
            SignKit.AddRelay(station.Badge.Face).Clicked = eventData =>
            {
                if (flow.IsBusy || flow.CurrentState != GameFlow.State.SongSelect)
                {
                    return;
                }

                if (stationIndex == selected)
                {
                    Decide();
                }
                else
                {
                    Select(stationIndex);
                }
            };

            stations.Add(station);
        }

        // Badges above labels: move every label to the back of the layer.
        for (int index = stations.Count - 1; index >= 0; index--)
        {
            stations[index].Label.rectTransform.SetAsFirstSibling();
        }
    }

    private void LayoutStations()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.5f);
        for (int index = 0; index < stations.Count; index++)
        {
            Station station = stations[index];
            float offset = index - scroll;
            float distance = Mathf.Abs(offset);
            float along = distance <= 1f ? distance * FirstGap : FirstGap + (distance - 1f) * StationGap;

            // Earlier songs go up the line (and round the bend), later ones down.
            float up = offset < 0f ? along : -along;
            Vector2 position = GetPathPoint(up);
            float diameter = distance <= 1f
                ? Mathf.Lerp(SelectedDiameter, NeighbourDiameter, distance)
                : Mathf.Max(FarDiameter, NeighbourDiameter - (distance - 1f) * ShrinkPerStation);

            // A difficulty change flips the badge over like a sign plate, turning to the new colour edge-on.
            Color ring = SongOf(station).Difficulty.Color;
            float flip = 1f;
            float flipT = (Time.unscaledTime - flipAt) / BadgeFlipSeconds;
            if (index == flipStation && flipT < 1f)
            {
                flip = Mathf.Max(0.02f, Mathf.Abs(Mathf.Cos(Ease.InOutCubic(flipT) * Mathf.PI)));
                if (flipT < 0.5f)
                {
                    ring = flipFromColor;
                }
            }

            station.Badge.Rect.anchoredPosition = position;
            station.Badge.Rect.localScale = new Vector3(flip, 1f, 1f);
            station.Badge.SetDiameter(diameter);
            station.Badge.SetLineColor(ring);
            float alpha = Mathf.Clamp01(5f - distance);
            station.Badge.Group.alpha = index == selected && selectedStationHidden ? 0f : alpha;
            station.Badge.SetHalo(index == selected ? (1f - Mathf.Clamp01(distance * 2f)) * (0.35f + 0.3f * pulse) : 0f);

            // Neighbours carry a small title; the selected one has the big text instead.
            bool onStraight = up < SelectedY - BendY - 20f;
            float labelAlpha = onStraight ? Mathf.Clamp01((distance - 0.45f) * 2.5f) * Mathf.Clamp01((3.2f - distance) / 2.2f) : 0f;
            station.Label.enabled = labelAlpha > 0.01f;
            station.Label.color = UiKit.WithAlpha(SignKit.InkSoft, labelAlpha);
            station.Label.rectTransform.anchoredPosition = new Vector2(position.x + diameter * 0.5f + 34f, position.y);
        }
    }

    private static float BendLength => BendRadius * Mathf.PI * 0.25f;

    /// <summary>
    /// A point on the line (anchored to the screen's top left), <paramref name="up"/> units
    /// along it above the selected station's place (below if negative): up the straight
    /// part, round the bend, then up the diagonal.
    /// </summary>
    private static Vector2 GetPathPoint(float up)
    {
        float straight = SelectedY - BendY;
        if (up <= straight)
        {
            return new Vector2(LineX, -(SelectedY - up));
        }

        if (up <= straight + BendLength)
        {
            float angle = (up - straight) / BendRadius;
            return new Vector2(LineX - BendRadius + Mathf.Cos(angle) * BendRadius, -BendY + Mathf.Sin(angle) * BendRadius);
        }

        Vector2 bendEnd = new Vector2(LineX - BendRadius + Diagonal * BendRadius, -BendY + Diagonal * BendRadius);
        float extra = (up - straight - BendLength) * Diagonal;
        return bendEnd + new Vector2(-extra, extra);
    }

    /// <summary>Fills the text for the chosen chart; it slides in from <paramref name="slideFrom"/> units to the right (left if negative).</summary>
    private void RefreshDetail(float slideFrom)
    {
        detailChangedAt = Time.unscaledTime;
        detailSlideFrom = slideFrom;
        gradeRow.gameObject.SetActive(stations.Count > 0);
        if (stations.Count == 0)
        {
            titleText.text = "NO SONGS";
            artistText.text = string.Empty;
            infoText.text = string.Empty;
            watermark.text = string.Empty;
            return;
        }

        Station station = stations[selected];
        SongChartData song = SongOf(station);
        titleText.text = song.title;
        artistText.text = string.IsNullOrWhiteSpace(song.artist) ? "-" : song.artist;

        int seconds = Mathf.CeilToInt((float)song.EndTime);
        float best = ScoreTracker.GetBest(song.id);
        string bestText = best < 0f ? "--.--%" : $"{best:0.00}%  {ScoreTracker.GetRank(best)}";
        infoText.text = $"BPM {song.bpm:0.#}        {seconds / 60}:{seconds % 60:00}        BEST  {bestText}";
        watermark.text = SelectedStationNumber;

        foreach (GradeTab tab in tabs)
        {
            int chart = FindChart(station, tab.Grade.Index);
            tab.Level.text = chart >= 0 ? charts.Songs[station.Charts[chart]].level.ToString() : "-";
        }
    }

    private void AnimateDetail()
    {
        float t = Ease.OutCubic((Time.unscaledTime - detailChangedAt) / DetailSlideSeconds);
        detail.anchoredPosition = new Vector2(TextX + (1f - t) * detailSlideFrom, -SelectedY);
        detailGroup.alpha = t;
    }

    /// <summary>Puts the cursor straight onto the chosen grade (no slide), e.g. when the screen opens.</summary>
    private void SnapGradeCursor()
    {
        if (stations.Count == 0)
        {
            return;
        }

        GradeTab target = tabs[SongOf(stations[selected]).Difficulty.Index];
        cursorX = target.X;
        cursorWidth = target.Width;
        cursorColor = target.Grade.Color;
        foreach (GradeTab tab in tabs)
        {
            tab.Alpha = FindChart(stations[selected], tab.Grade.Index) >= 0 ? 1f : 0f;
        }

        AnimateGradeRow();
    }

    private void AnimateGradeRow()
    {
        if (stations.Count == 0)
        {
            return;
        }

        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        Station station = stations[selected];
        GradeTab target = tabs[SongOf(station).Difficulty.Index];

        // The cursor glides to the chosen grade, changing colour on the way.
        float follow = 1f - Mathf.Exp(-dt * CursorSmoothRate);
        cursorX = Mathf.Lerp(cursorX, target.X, follow);
        cursorWidth = Mathf.Lerp(cursorWidth, target.Width, follow);
        cursorColor = Color.Lerp(cursorColor, target.Grade.Color, follow);
        gradeCursor.rectTransform.anchoredPosition = new Vector2(cursorX, 0f);
        gradeCursor.rectTransform.sizeDelta = new Vector2(cursorWidth, TabHeight);
        gradeCursor.color = cursorColor;

        // A short squash-and-stretch as it sets off.
        float punch = Mathf.Sin(Mathf.Clamp01((now - gradePunchAt) / GradePunchSeconds) * Mathf.PI);
        gradeCursor.rectTransform.localScale = new Vector3(1f + 0.08f * punch, 1f - 0.12f * punch, 1f);

        // Pressing past the easiest or hardest difficulty shakes the row towards that side.
        float bumpT = (now - bumpAt) / BumpSeconds;
        float shake = bumpT < 1f ? bumpDirection * BumpDistance * Mathf.Sin(bumpT * Mathf.PI * 4f) * (1f - bumpT) : 0f;
        gradeRow.anchoredPosition = new Vector2(TextX + shake, -(SelectedY - GradeRowLift));

        Color onCursor = GetReadableInk(cursorColor);
        float fade = 1f - Mathf.Exp(-dt * TabFadeRate);
        foreach (GradeTab tab in tabs)
        {
            bool available = FindChart(station, tab.Grade.Index) >= 0;
            tab.Alpha = Mathf.Lerp(tab.Alpha, available ? 1f : 0f, fade);

            // How much of the cursor sits on this tab: its text turns to the cursor's ink.
            float cover = Mathf.Clamp01(1f - Mathf.Abs(cursorX - tab.X) / (tab.Width * 0.6f));
            Color frame = Color.Lerp(SignKit.Disabled, tab.Grade.Color, tab.Alpha);
            Color name = Color.Lerp(SignKit.Disabled, Color.Lerp(tab.Grade.Color, SignKit.Ink, 0.3f), tab.Alpha);
            Color level = Color.Lerp(SignKit.Disabled, SignKit.InkSoft, tab.Alpha);
            tab.Frame.color = frame;
            tab.Name.color = Color.Lerp(name, onCursor, cover);
            tab.Level.color = Color.Lerp(level, onCursor, cover);
        }

        float flash = bumpT < 1f ? 1f - bumpT : 0f;
        gradeArrowLeft.color = GetArrowColor(station.Chart > 0, bumpDirection < 0 ? flash : 0f);
        gradeArrowRight.color = GetArrowColor(station.Chart < station.Charts.Count - 1, bumpDirection > 0 ? flash : 0f);
    }

    private static Color GetArrowColor(bool active, float flash)
    {
        return Color.Lerp(active ? SignKit.Ink : SignKit.Disabled, SignKit.MetroRed, flash);
    }

    /// <summary>Black ink on light colours (Rapid's yellow), white on the rest.</summary>
    private static Color GetReadableInk(Color background)
    {
        float luminance = 0.299f * background.r + 0.587f * background.g + 0.114f * background.b;
        return luminance > 0.6f ? SignKit.DarkInk : Color.white;
    }
}
