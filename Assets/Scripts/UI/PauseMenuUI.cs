using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pause menu as a station sign over the dimmed song: the song's station badge and
/// "一時停止 / Paused" as the station name, the song's line band, then resume / retry /
/// back to song select / settings (the full-screen SettingsScreenUI over this board, so
/// options can be changed without leaving the song).
/// </summary>
public sealed class PauseMenuUI : MonoBehaviour
{
    private const string Footer = "↑ ↓  SELECT        ENTER / A  OK        TAB / Y  SETTINGS        ESC / B  RESUME";

    private GameFlow flow;
    private MenuList list;
    private LineBadge badge;
    private AngularGraphic band;
    private Text songText;
    private Text serviceText;
    private Text description;

    /// <summary>The highlighted row, kept when the menus are rebuilt for a theme change.</summary>
    public int SelectedRow
    {
        get => list.SelectedIndex;
        set => list.Select(value);
    }

    public void Build(GameFlow gameFlow)
    {
        flow = gameFlow;
        RectTransform root = (RectTransform)transform;
        Vector2 topLeft = new Vector2(0f, 1f);
        Vector2 topRight = new Vector2(1f, 1f);

        SignKit.CreateDim(root);

        RectTransform board = UiKit.CreateRect(root, "Sign");
        UiKit.Place(board, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 560f));
        Transform face = SignKit.CreateBoard(board, "Board", 16f, 14f).transform;

        badge = LineBadge.Create(face, "SongBadge", SignKit.MetroRed, 104f);
        badge.Rect.anchorMin = topLeft;
        badge.Rect.anchorMax = topLeft;
        badge.Rect.anchoredPosition = new Vector2(82f, -78f);
        SignKit.CreateHeading(face, "一時停止", "Paused", new Vector2(156f, -26f), 48);

        songText = SignKit.CreateFittedText(face, "Song", 28, 18, TextAnchor.MiddleRight, SignKit.Ink, true);
        UiKit.Place(songText, topRight, new Vector2(-40f, -36f), new Vector2(400f, 40f));
        serviceText = SignKit.CreateText(face, "Service", string.Empty, 18, TextAnchor.MiddleRight, SignKit.MetroRed, true);
        UiKit.Place(serviceText, topRight, new Vector2(-40f, -80f), new Vector2(400f, 26f));

        band = SignKit.CreateBand(face, "LineBand", SignKit.MetroRed, 148f, 14f);

        RectTransform listRect = UiKit.CreateRect(face, "List");
        UiKit.Place(listRect, topLeft, new Vector2(40f, -190f), new Vector2(868f, 232f));
        list = new MenuList(listRect, 46f, 6f, 23, MenuList.Look.Sign);
        list.AddAction("RESUME", "カウントダウンの後、続きから再開", flow.Resume);
        list.AddAction("RETRY", "この曲を最初からやり直す", flow.Retry);
        list.AddAction("SONG SELECT", "曲選択に戻る", flow.QuitToSongSelect);
        list.AddAction("SETTINGS", "設定（速度・音量・表示など）を開く", flow.OpenOptions);
        list.SelectionChanged += RefreshDescription;

        AngularGraphic rule = UiKit.CreatePanel(face, "Rule", UiKit.WithAlpha(SignKit.Ink, 0.18f));
        rule.rectTransform.anchorMin = new Vector2(0f, 0f);
        rule.rectTransform.anchorMax = new Vector2(1f, 0f);
        rule.rectTransform.pivot = new Vector2(0.5f, 0f);
        rule.rectTransform.offsetMin = new Vector2(40f, 104f);
        rule.rectTransform.offsetMax = new Vector2(-40f, 106f);

        description = SignKit.CreateText(face, "Description", string.Empty, 21, TextAnchor.MiddleLeft, SignKit.Ink);
        UiKit.Place(description, new Vector2(0f, 0f), new Vector2(44f, 60f), new Vector2(860f, 32f));

        Text footer = SignKit.CreateText(face, "Footer", Footer, 16, TextAnchor.MiddleLeft, SignKit.InkDim, true);
        UiKit.Place(footer, new Vector2(0f, 0f), new Vector2(44f, 22f), new Vector2(860f, 26f));
    }

    /// <param name="genreLetter">Letter and <paramref name="stationNumber"/> of the song's station on its line.</param>
    public void Show(SongChartData chart, string genreLetter, string stationNumber)
    {
        gameObject.SetActive(true);
        Color lineColor = chart != null ? chart.Difficulty.Color : SignKit.MetroRed;
        songText.text = chart != null ? chart.title : string.Empty;
        serviceText.text = chart != null ? $"{chart.Difficulty.Name.ToUpperInvariant()}   LV {chart.level}" : string.Empty;
        serviceText.color = lineColor;
        badge.SetLineColor(lineColor);
        badge.SetCode(genreLetter, stationNumber);
        band.color = lineColor;
        list.LineColor = lineColor;
        list.Select(0);
        list.Refresh();
        RefreshDescription();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (flow.IsBusy)
        {
            return;
        }

        list.Tick();
        if (MenuInput.Cancel || MenuInput.Pause)
        {
            MenuInput.Consume();
            flow.Resume();
        }
    }

    private void RefreshDescription()
    {
        MenuList.Item item = list.Selected;
        description.text = item != null ? item.Description : string.Empty;
    }
}
