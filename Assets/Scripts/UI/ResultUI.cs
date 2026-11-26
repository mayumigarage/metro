using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Result screen shown when a song ends, as a station sign like the selected-song sign:
/// the song's station badge, title and artist as the station name, the line band in the
/// song's difficulty colour carrying the rank in a large badge, the achievement rate,
/// the judgement breakdown, and retry / back to song select.
/// </summary>
public sealed class ResultUI : MonoBehaviour
{
    private static readonly Judgement[] Judgements = { Judgement.Perfect, Judgement.Great, Judgement.Good, Judgement.Miss };

    private const string Footer = "↑ ↓  SELECT          ENTER / A  DECIDE          ESC / B  SONG SELECT";
    private const float BoardWidth = 1640f;
    private const float BoardHeight = 860f;
    private const float Frame = 18f;
    private const float BandTop = 236f;
    private const float BandHeight = 56f;
    private const float RankDiameter = 210f;
    private const float RankX = 300f;

    private GameFlow flow;
    private MenuList list;
    private CanvasGroup group;
    private RectTransform board;
    private LineBadge songBadge;
    private LineBadge rankBadge;
    private AngularGraphic band;
    private RoundedRectGraphic recordBox;
    private Text recordText;
    private Text titleText;
    private Text artistText;
    private Text serviceText;
    private Text percentText;
    private Text comboText;
    private Text timingText;
    private readonly Text[] countTexts = new Text[4];
    private float shownAt;

    public void Build(GameFlow gameFlow)
    {
        flow = gameFlow;
        RectTransform root = (RectTransform)transform;
        group = gameObject.AddComponent<CanvasGroup>();
        Vector2 topLeft = new Vector2(0f, 1f);
        Vector2 topRight = new Vector2(1f, 1f);

        SignKit.CreateDim(root);

        board = UiKit.CreateRect(root, "Sign");
        UiKit.Place(board, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BoardWidth, BoardHeight));
        Transform face = SignKit.CreateBoard(board, "Board", Frame, 16f).transform;

        // Station name: badge, title, artist; the record and the service on the right.
        songBadge = LineBadge.Create(face, "SongBadge", SignKit.MetroRed, 128f);
        songBadge.Rect.anchorMin = topLeft;
        songBadge.Rect.anchorMax = topLeft;
        songBadge.Rect.anchoredPosition = new Vector2(100f, -98f);

        titleText = SignKit.CreateFittedText(face, "SongTitle", 68, 36, TextAnchor.MiddleLeft, SignKit.Ink, true);
        UiKit.Place(titleText, topLeft, new Vector2(190f, -28f), new Vector2(980f, 88f));
        artistText = SignKit.CreateFittedText(face, "Artist", 32, 22, TextAnchor.MiddleLeft, SignKit.InkSoft, false);
        UiKit.Place(artistText, topLeft, new Vector2(192f, -112f), new Vector2(980f, 44f));

        recordText = SignKit.CreateCodeBox(face, "Record", SignKit.MetroRed, 30, 4f, topRight, new Vector2(-44f, -36f), new Vector2(290f, 64f));
        recordBox = recordText.transform.parent.GetComponent<RoundedRectGraphic>();
        serviceText = SignKit.CreateText(face, "Service", string.Empty, 24, TextAnchor.MiddleRight, SignKit.MetroRed, true);
        UiKit.Place(serviceText, topRight, new Vector2(-46f, -116f), new Vector2(420f, 34f));

        // The line band, with the rank where the selected-song sign has the level.
        band = SignKit.CreateBand(face, "LineBand", SignKit.MetroRed, BandTop, BandHeight);
        band.rectTransform.pivot = topLeft;

        rankBadge = LineBadge.Create(face, "RankBadge", SignKit.MetroRed, RankDiameter);
        rankBadge.Rect.anchorMin = topLeft;
        rankBadge.Rect.anchorMax = topLeft;
        rankBadge.Rect.anchoredPosition = new Vector2(RankX, -(BandTop + BandHeight * 0.5f));
        AddCentredLabel(face, "RankName", "ランク", 36, true, RankX, BandTop + BandHeight * 0.5f + RankDiameter * 0.5f + 34f);
        AddCentredLabel(face, "RankEnglish", "Rank", 22, false, RankX, BandTop + BandHeight * 0.5f + RankDiameter * 0.5f + 72f);

        // Achievement rate right of the rank, under the band.
        Text percentLabel = SignKit.CreateText(face, "PercentLabel", SignKit.Bilingual("達成率", "Achievement", 20), 30, TextAnchor.MiddleLeft, SignKit.InkSoft, true);
        UiKit.Place(percentLabel, topLeft, new Vector2(478f, -322f), new Vector2(560f, 44f));
        percentText = SignKit.CreateText(face, "Percent", string.Empty, 124, TextAnchor.MiddleLeft, SignKit.Ink, true);
        UiKit.Place(percentText, topLeft, new Vector2(470f, -362f), new Vector2(600f, 150f));

        BuildCounts(face);

        RectTransform listRect = UiKit.CreateRect(face, "Actions");
        UiKit.Place(listRect, topLeft, new Vector2(60f, -606f), new Vector2(640f, 122f));
        list = new MenuList(listRect, 56f, 10f, 28, MenuList.Look.Sign);
        list.AddAction("RETRY", null, flow.Retry);
        list.AddAction("SONG SELECT", null, flow.QuitToSongSelect);

        Text footer = SignKit.CreateText(face, "Footer", Footer, 18, TextAnchor.MiddleLeft, SignKit.InkDim, true);
        UiKit.Place(footer, new Vector2(0f, 0f), new Vector2(62f, 24f), new Vector2(1200f, 28f));
    }

    /// <param name="newRecord">The play beat the song's saved best.</param>
    /// <param name="autoPlayed">Auto play was on at some point, so nothing was saved.</param>
    /// <param name="genreLetter">Letter and <paramref name="stationNumber"/> of the song's station on its line.</param>
    public void Show(SongChartData chart, ScoreTracker score, bool newRecord, bool autoPlayed, string genreLetter, string stationNumber)
    {
        gameObject.SetActive(true);
        shownAt = Time.unscaledTime;
        Color lineColor = chart.Difficulty.Color;

        titleText.text = chart.title;
        artistText.text = string.IsNullOrWhiteSpace(chart.artist) ? "-" : chart.artist;
        serviceText.text = $"{chart.Difficulty.Name.ToUpperInvariant()}   LV {chart.level}";
        serviceText.color = lineColor;
        songBadge.SetLineColor(lineColor);
        songBadge.SetCode(genreLetter, stationNumber);
        band.color = lineColor;
        rankBadge.SetLineColor(lineColor);
        rankBadge.SetCode(score.Rank, null);

        recordBox.gameObject.SetActive(autoPlayed || newRecord);
        recordText.text = autoPlayed ? "AUTO PLAY" : "NEW RECORD";
        Color recordColor = autoPlayed ? SignKit.InkDim : SignKit.MetroRed;
        recordText.color = recordColor;
        recordBox.color = recordColor;

        percentText.text = UiKit.FormatPercent(score.Percent, 60);
        for (int index = 0; index < Judgements.Length; index++)
        {
            countTexts[index].text = score.GetCount(Judgements[index]).ToString();
        }

        bool fullCombo = score.JudgedCount > 0 && score.GetCount(Judgement.Miss) == 0;
        string fullComboMark = $"<color=#{ColorUtility.ToHtmlStringRGB(SignKit.GetJudgementColor(Judgement.Perfect))}>FULL COMBO</color>   ";
        comboText.text = fullCombo ? fullComboMark + score.MaxCombo : score.MaxCombo.ToString();
        timingText.text = $"{score.FastCount} / {score.SlowCount}";

        list.LineColor = lineColor;
        list.Select(0);
        list.Refresh();
        Animate();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        Animate();
        if (flow.IsBusy)
        {
            return;
        }

        list.Tick();
        if (MenuInput.Cancel)
        {
            MenuInput.Consume();
            flow.QuitToSongSelect();
        }
    }

    /// <summary>Right column: the judgement breakdown, max combo and fast / slow.</summary>
    private void BuildCounts(Transform face)
    {
        Vector2 topLeft = new Vector2(0f, 1f);
        Vector2 topRight = new Vector2(1f, 1f);
        const float left = 1092f;

        for (int index = 0; index < Judgements.Length; index++)
        {
            float y = -322f - index * 54f;
            Judgement judgement = Judgements[index];
            CircleGraphic chip = SignKit.CreateCircle(face, $"{judgement}_Chip", SignKit.GetJudgementColor(judgement));
            UiKit.Place(chip, topLeft, new Vector2(left, y - 12f), new Vector2(24f, 24f));
            Text label = SignKit.CreateText(face, $"{judgement}_Label", judgement.ToString().ToUpperInvariant(), 34, TextAnchor.MiddleLeft, SignKit.Ink, true);
            UiKit.Place(label, topLeft, new Vector2(left + 40f, y), new Vector2(300f, 48f));
            countTexts[index] = SignKit.CreateText(face, $"{judgement}_Count", "0", 36, TextAnchor.MiddleRight, SignKit.Ink, true);
            UiKit.Place(countTexts[index], topRight, new Vector2(-62f, y), new Vector2(240f, 48f));
        }

        AngularGraphic rule = UiKit.CreatePanel(face, "Rule", UiKit.WithAlpha(SignKit.Ink, 0.2f));
        UiKit.Place(rule, topLeft, new Vector2(left, -548f), new Vector2(BoardWidth - Frame * 2f - left - 62f, 2f));

        Text comboLabel = SignKit.CreateText(face, "ComboLabel", "MAX COMBO", 26, TextAnchor.MiddleLeft, SignKit.InkSoft, true);
        UiKit.Place(comboLabel, topLeft, new Vector2(left, -566f), new Vector2(240f, 40f));
        comboText = SignKit.CreateText(face, "Combo", "0", 28, TextAnchor.MiddleRight, SignKit.Ink, true);
        UiKit.Place(comboText, topRight, new Vector2(-62f, -566f), new Vector2(300f, 40f));

        Text timingLabel = SignKit.CreateText(face, "TimingLabel", "FAST / SLOW", 26, TextAnchor.MiddleLeft, SignKit.InkSoft, true);
        UiKit.Place(timingLabel, topLeft, new Vector2(left, -614f), new Vector2(240f, 40f));
        timingText = SignKit.CreateText(face, "Timing", string.Empty, 28, TextAnchor.MiddleRight, SignKit.Ink, true);
        UiKit.Place(timingText, topRight, new Vector2(-62f, -614f), new Vector2(300f, 40f));
    }

    private static void AddCentredLabel(Transform face, string name, string content, int size, bool bold, float x, float y)
    {
        Text text = SignKit.CreateText(face, name, content, size, TextAnchor.MiddleCenter, SignKit.Ink, bold);
        text.rectTransform.anchorMin = new Vector2(0f, 1f);
        text.rectTransform.anchorMax = new Vector2(0f, 1f);
        text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        text.rectTransform.sizeDelta = new Vector2(320f, size * 1.5f);
        text.rectTransform.anchoredPosition = new Vector2(x, -y);
    }

    /// <summary>As the selected-song sign: the board rises in, the band runs out from the left, then the rank pops.</summary>
    private void Animate()
    {
        float age = Time.unscaledTime - shownAt;
        float rise = Ease.OutCubic(age / 0.25f);
        group.alpha = rise;
        board.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, rise);
        band.rectTransform.anchorMax = new Vector2(Ease.OutCubic((age - 0.05f) / 0.4f), 1f);
        rankBadge.Rect.localScale = Vector3.one * Ease.OutBack((age - 0.3f) / 0.35f);
    }
}
