using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The options as a modal sign board over the song select or the selected-song sign
/// (Tab / Y). Same rows as in the pause menu plus the player name (entered on
/// NameEntryUI, on top of this board), and how to play. The band and the
/// selection marker take the browsed song's line colour.
/// </summary>
public sealed class OptionsOverlayUI : MonoBehaviour
{
    private const string Footer = "↑ ↓  SELECT        ← →  CHANGE        TAB / ESC / B  CLOSE";
    private const string HowToPlay =
        "L / R / ZL / ZR  =  LB / RB / LT / RT   ( KEYS  D / K / F / J )\n" +
        "ARROW NOTE  =  STICK FLICK      HOLD NOTE  =  KEEP THE BUTTON DOWN\n" +
        "LASER  =  FOLLOW WITH THE STICK   ( KEYS  ↑ ↓ ← → )";

    private MenuList list;
    private AngularGraphic band;
    private Text description;
    private NameEntryUI nameEntry;
    private Color lineColor = SignKit.MetroRed;
    private float openedAt;

    public bool IsOpen => gameObject.activeSelf;

    /// <summary>The highlighted row, kept when the menus are rebuilt for a theme change.</summary>
    public int SelectedRow
    {
        get => list.SelectedIndex;
        set => list.Select(value);
    }

    public void Build()
    {
        RectTransform root = (RectTransform)transform;
        Vector2 topLeft = new Vector2(0f, 1f);

        SignKit.CreateDim(root);

        RectTransform board = UiKit.CreateRect(root, "Sign");
        UiKit.Place(board, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 1000f));
        Transform face = SignKit.CreateBoard(board, "Board", 16f, 14f).transform;

        SignKit.CreateHeading(face, "オプション", "Options", new Vector2(44f, -26f), 48);
        band = SignKit.CreateBand(face, "LineBand", SignKit.MetroRed, 148f, 14f);

        RectTransform listRect = UiKit.CreateRect(face, "OptionList");
        UiKit.Place(listRect, topLeft, new Vector2(40f, -190f), new Vector2(868f, 546f));
        list = new MenuList(listRect, 48f, 6f, 23, MenuList.Look.Sign);
        list.AddAction(
            "PLAYER NAME",
            "曲中の画面上部に表示する名前。キーボードで入力するか、パッドで文字を選ぶ",
            () => nameEntry.Open(lineColor),
            () => GameSettings.PlayerName);
        OptionsMenu.AddItems(list);
        list.SelectionChanged += RefreshDescription;

        description = SignKit.CreateText(face, "Description", string.Empty, 21, TextAnchor.UpperLeft, SignKit.Ink);
        description.horizontalOverflow = HorizontalWrapMode.Wrap;
        UiKit.Place(description, topLeft, new Vector2(44f, -754f), new Vector2(860f, 60f));

        AngularGraphic rule = UiKit.CreatePanel(face, "Rule", UiKit.WithAlpha(SignKit.Ink, 0.18f));
        rule.rectTransform.anchorMin = new Vector2(0f, 0f);
        rule.rectTransform.anchorMax = new Vector2(1f, 0f);
        rule.rectTransform.pivot = new Vector2(0.5f, 0f);
        rule.rectTransform.offsetMin = new Vector2(40f, 146f);
        rule.rectTransform.offsetMax = new Vector2(-40f, 148f);

        Text help = SignKit.CreateText(face, "HowToPlay", HowToPlay, 17, TextAnchor.LowerLeft, SignKit.InkSoft);
        help.lineSpacing = 1.2f;
        UiKit.Place(help, new Vector2(0f, 0f), new Vector2(44f, 58f), new Vector2(860f, 80f));

        Text footer = SignKit.CreateText(face, "Footer", Footer, 16, TextAnchor.MiddleLeft, SignKit.InkDim, true);
        UiKit.Place(footer, new Vector2(0f, 0f), new Vector2(44f, 22f), new Vector2(860f, 26f));

        RectTransform entryRect = UiKit.CreateRect(root, "NameEntry");
        UiKit.Stretch(entryRect);
        nameEntry = entryRect.gameObject.AddComponent<NameEntryUI>();
        nameEntry.Build();
        nameEntry.Closed += () =>
        {
            openedAt = Time.unscaledTime;
            list.Refresh();
        };

        gameObject.SetActive(false);
    }

    /// <param name="lineColor">Colour of the band and the selection marker: the browsed song's line.</param>
    public void Open(Color lineColor)
    {
        gameObject.SetActive(true);
        openedAt = Time.unscaledTime;
        this.lineColor = lineColor;
        band.color = lineColor;
        list.LineColor = lineColor;
        list.Refresh();
        RefreshDescription();
    }

    public void Close()
    {
        if (nameEntry.IsOpen)
        {
            nameEntry.Close();
        }

        gameObject.SetActive(false);
    }

    private void Update()
    {
        // The name entry on top has the input while it is open.
        if (nameEntry.IsOpen || Time.unscaledTime - openedAt < 0.1f)
        {
            return;
        }

        list.Tick();
        if (MenuInput.Cancel || MenuInput.Option)
        {
            MenuInput.Consume();
            Close();
        }
    }

    private void RefreshDescription()
    {
        MenuList.Item item = list.Selected;
        description.text = item != null ? item.Description : string.Empty;
    }
}
