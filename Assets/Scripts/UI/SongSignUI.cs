using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The selected song as a platform station sign, over the dimmed route map: title and
/// artist where the station name goes, then the line band carrying the level badge,
/// two chevrons and a spinning start circle (the next stop: playing the song). The
/// best score sits where the previous station's name would. Confirm departs.
/// </summary>
public sealed class SongSignUI : MonoBehaviour
{
    private const string Footer = "ENTER / A  START          ESC / B  BACK          TAB / Y  OPTIONS";

    // Board layout (1440 x 760), in canvas units from the board's top left.
    private const float BoardWidth = 1440f;
    private const float BoardHeight = 760f;
    private const float Frame = 20f;
    private const float BandY = 470f;
    private const float BandHeight = 64f;
    private const float BadgeDiameter = 172f;
    private const float LevelX = 720f;
    private const float StartX = 1236f;
    private const float IntroSeconds = 0.5f;

    private GameFlow flow;
    private CanvasGroup group;
    private RectTransform board;
    private Text codeText;
    private Text titleText;
    private Text artistText;
    private Text statsText;
    private Text bestText;
    private RectTransform band;
    private AngularGraphic bandFill;
    private LineBadge levelBadge;
    private RectTransform startRoot;
    private CircleGraphic startRing;
    private CircleGraphic startInnerRing;
    private ChevronGraphic startArrow;
    private readonly ChevronGraphic[] chevrons = new ChevronGraphic[2];
    private Color lineColor;
    private float shownAt;

    public void Build(GameFlow gameFlow)
    {
        flow = gameFlow;
        RectTransform root = (RectTransform)transform;
        group = gameObject.AddComponent<CanvasGroup>();

        AngularGraphic dim = UiKit.CreatePanel(root, "Dim", new Color(0.02f, 0.02f, 0.03f, 0.93f));
        UiKit.Stretch(dim);
        SignKit.AddRelay(dim).Clicked = eventData =>
        {
            if (!flow.IsBusy)
            {
                flow.CancelSong();
            }
        };

        board = UiKit.CreateRect(root, "Sign");
        UiKit.Place(board, new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(BoardWidth, BoardHeight));
        RoundedRectGraphic face = SignKit.CreateBoard(board, "Board", Frame, 14f);
        face.raycastTarget = true;

        // Station code box in the corner (P04...), as on the reference sign.
        RoundedRectGraphic codeBox = SignKit.CreateRoundedRect(board, "CodeBox", SignKit.Ink, 10f, 5f);
        UiKit.Place(codeBox, new Vector2(1f, 1f), new Vector2(-50f, -46f), new Vector2(150f, 82f));
        codeText = SignKit.CreateText(codeBox.rectTransform, "Code", string.Empty, 46, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Stretch(codeText);
        codeText.rectTransform.anchoredPosition = new Vector2(0f, 46 * SignKit.CapRaise);

        titleText = SignKit.CreateFittedText(board, "SongTitle", 118, 52, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Place(titleText, new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(1080f, 150f));
        artistText = SignKit.CreateFittedText(board, "Artist", 48, 28, TextAnchor.MiddleCenter, SignKit.Ink, false);
        UiKit.Place(artistText, new Vector2(0.5f, 1f), new Vector2(0f, -236f), new Vector2(1200f, 64f));
        statsText = SignKit.CreateText(board, "Stats", string.Empty, 28, TextAnchor.MiddleCenter, SignKit.InkDim, true);
        UiKit.Place(statsText, new Vector2(0.5f, 1f), new Vector2(0f, -312f), new Vector2(1300f, 40f));

        BuildBand();
        BuildLabels();

        Text footer = SignKit.CreateText(root, "Footer", Footer, 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f), true);
        UiKit.Place(footer, new Vector2(0.5f, 0f), new Vector2(0f, 122f), new Vector2(1600f, 34f));
    }

    public void Show(SongChartData song, SongGenre genre, string stationNumber)
    {
        gameObject.SetActive(true);
        shownAt = Time.unscaledTime;
        lineColor = song.Difficulty.Color;

        codeText.text = genre.Letter + stationNumber;
        titleText.text = song.title;
        artistText.text = string.IsNullOrWhiteSpace(song.artist) ? "-" : song.artist;

        int seconds = Mathf.CeilToInt((float)song.EndTime);
        statsText.text = $"BPM {song.bpm:0.#}     ·     {seconds / 60}:{seconds % 60:00}     ·     NOTES {song.notes.Length}     ·     HOLDS {song.HoldCount}     ·     LASERS {song.lasers.Length}";

        float best = ScoreTracker.GetBest(song.id);
        bestText.text = best < 0f ? "--.--<size=40>%</size>" : $"{best:0.00}<size=40>%</size>  {ScoreTracker.GetRank(best)}";

        bandFill.color = lineColor;
        levelBadge.SetLineColor(lineColor);
        levelBadge.SetCode("LV", SongGenre.FormatStation(song.level));
        startRing.color = lineColor;
        startInnerRing.color = lineColor;
        startArrow.color = lineColor;
        Animate();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        Animate();
        if (flow.IsBusy || flow.CurrentState != GameFlow.State.SongConfirm || Time.unscaledTime - shownAt < 0.15f)
        {
            return;
        }

        if (MenuInput.Confirm)
        {
            MenuInput.Consume();
            flow.DepartSong();
        }
        else if (MenuInput.Cancel)
        {
            MenuInput.Consume();
            flow.CancelSong();
        }
        else if (MenuInput.Option)
        {
            MenuInput.Consume();
            flow.OpenOptions();
        }
    }

    private void BuildBand()
    {
        // The band runs the full width of the face.
        band = UiKit.CreateRect(board, "Band");
        band.anchorMin = new Vector2(0f, 1f);
        band.anchorMax = new Vector2(0f, 1f);
        band.pivot = new Vector2(0f, 0.5f);
        band.sizeDelta = new Vector2(BoardWidth - Frame * 2f, BandHeight);
        band.anchoredPosition = new Vector2(Frame, -BandY);
        bandFill = UiKit.CreatePanel(band, "Fill", SignKit.MetroRed);
        bandFill.rectTransform.anchorMin = Vector2.zero;
        bandFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        bandFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        bandFill.rectTransform.offsetMin = Vector2.zero;
        bandFill.rectTransform.offsetMax = Vector2.zero;

        Vector2 topLeft = new Vector2(0f, 1f);
        levelBadge = LineBadge.Create(board, "LevelBadge", SignKit.MetroRed, BadgeDiameter);
        levelBadge.Rect.anchorMin = topLeft;
        levelBadge.Rect.anchorMax = topLeft;
        levelBadge.Rect.anchoredPosition = new Vector2(LevelX, -BandY);

        for (int index = 0; index < chevrons.Length; index++)
        {
            chevrons[index] = SignKit.CreateChevron(board, $"Chevron{index + 1}", SignKit.MetroRed, 36f);
            RectTransform rect = chevrons[index].rectTransform;
            rect.anchorMin = topLeft;
            rect.anchorMax = topLeft;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(84f, 150f);
            rect.anchoredPosition = new Vector2(948f + index * 92f, -BandY);
        }

        // Start circle: white disc, a ring of arcs that keeps turning, and a play mark.
        startRoot = UiKit.CreateRect(board, "Start");
        startRoot.anchorMin = topLeft;
        startRoot.anchorMax = topLeft;
        startRoot.pivot = new Vector2(0.5f, 0.5f);
        startRoot.sizeDelta = new Vector2(BadgeDiameter, BadgeDiameter);
        startRoot.anchoredPosition = new Vector2(StartX, -BandY);

        CircleGraphic disc = SignKit.CreateCircle(startRoot, "Face", SignKit.Plate);
        UiKit.Stretch(disc);
        SignKit.AddRelay(disc).Clicked = eventData =>
        {
            if (!flow.IsBusy && flow.CurrentState == GameFlow.State.SongConfirm)
            {
                flow.DepartSong();
            }
        };

        startRing = SignKit.CreateCircle(startRoot, "TurningRing", SignKit.MetroRed, BadgeDiameter * 0.1f);
        startRing.SetSegments(3, 34f);
        UiKit.Stretch(startRing);
        startInnerRing = SignKit.CreateCircle(startRoot, "InnerRing", SignKit.MetroRed, 6f);
        UiKit.Stretch(startInnerRing, 30f, 30f, 30f, 30f);
        startArrow = SignKit.CreateChevron(startRoot, "Play", SignKit.MetroRed, 0f);
        UiKit.Place(startArrow, new Vector2(0.5f, 0.5f), new Vector2(7f, 0f), new Vector2(50f, 58f));
    }

    private void BuildLabels()
    {
        Vector2 topLeft = new Vector2(0f, 1f);
        AddLabel("LevelName", "レベル", 40, true, LevelX, BandY + 140f);
        AddLabel("LevelEnglish", "Level", 26, false, LevelX, BandY + 186f);
        AddLabel("StartName", "スタート", 40, true, StartX, BandY + 140f);
        AddLabel("StartEnglish", "Start", 26, false, StartX, BandY + 186f);

        // Best score where the sign names the previous station.
        Text bestLabel = SignKit.CreateText(board, "BestLabel", "ベスト  <size=24>Best</size>", 34, TextAnchor.MiddleLeft, SignKit.InkSoft, true);
        UiKit.Place(bestLabel, topLeft, new Vector2(72f, -(BandY + 98f)), new Vector2(500f, 46f));
        bestText = SignKit.CreateText(board, "Best", string.Empty, 64, TextAnchor.MiddleLeft, SignKit.Ink, true);
        UiKit.Place(bestText, topLeft, new Vector2(68f, -(BandY + 152f)), new Vector2(520f, 80f));
    }

    private void AddLabel(string name, string content, int size, bool bold, float x, float y)
    {
        Text text = SignKit.CreateText(board, name, content, size, TextAnchor.MiddleCenter, SignKit.Ink, bold);
        text.rectTransform.anchorMin = new Vector2(0f, 1f);
        text.rectTransform.anchorMax = new Vector2(0f, 1f);
        text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        text.rectTransform.sizeDelta = new Vector2(320f, size * 1.5f);
        text.rectTransform.anchoredPosition = new Vector2(x, -y);
    }

    private void Animate()
    {
        float now = Time.unscaledTime;
        float age = now - shownAt;

        // The board rises in, the band runs out from the left, then the badges pop.
        float rise = Ease.OutCubic(age / 0.25f);
        group.alpha = rise;
        board.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, rise);

        float bandWidth = BoardWidth - Frame * 2f;
        bandFill.rectTransform.sizeDelta = new Vector2(bandWidth * Ease.OutCubic((age - 0.05f) / 0.4f), 0f);
        levelBadge.Rect.localScale = Vector3.one * Ease.OutBack((age - 0.15f) / 0.3f);
        startRoot.localScale = Vector3.one * Ease.OutBack((age - 0.3f) / 0.3f);

        // Solid like the sign's chevrons; a light runs through them toward the start circle.
        float phase = Mathf.Repeat(now / 1.6f, 1f);
        for (int index = 0; index < chevrons.Length; index++)
        {
            float appear = Mathf.Clamp01((age - 0.25f - index * 0.06f) / 0.2f);
            float light = age < IntroSeconds ? 0f : Mathf.Exp(-Mathf.Pow((phase - 0.2f - index * 0.16f) / 0.07f, 2f));
            chevrons[index].color = UiKit.WithAlpha(Color.Lerp(lineColor, Color.white, light * 0.5f), appear);
        }

        startRing.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -now * 120f);
    }
}
