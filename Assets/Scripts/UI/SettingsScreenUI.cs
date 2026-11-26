using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The settings as a full-screen sign board, after the hand-drawn sketch: five tabs drawn as
/// boxes standing on a line (ゲームプレイ / オーディオ / グラフィック / 入力 / その他, the chosen
/// one larger and in the line colour), and under them the tab's settings as cards — a name
/// plate over a value box with ◁◁ ◁ … ▷ ▷▷ beside it (small and large steps, the amounts
/// written under the arrows). Opened from the settings button at the top right of every
/// menu screen (SettingsButton), or Tab / Y. Every change goes straight to GameSettings.
/// </summary>
public sealed class SettingsScreenUI : MonoBehaviour
{
    private const string Footer =
        "Q / E · LB / RB  タブ        ↑ ↓  選択        ← →  変更        SHIFT + ← → · LT / RT  大きく変更        ESC / B  閉じる";

    private const string ControlsText =
        "<b>レーン</b>\n" +
        "L / R / ZL / ZR   =   LB / RB / LT / RT      キーボード  D / K / F / J\n\n" +
        "<b>矢印ノーツ</b>\n" +
        "矢印の色のスティックを、その向きへはじく\n\n" +
        "<b>ロングノーツ</b>\n" +
        "ボタンを押したまま、終わりまで離さない\n\n" +
        "<b>レーザー</b>\n" +
        "スティックをレーザーの進む向きへ倒し続ける      キーボード  ↑ ↓ ← →\n\n" +
        "<b>一時停止</b>\n" +
        "ESC / START";

    // Board layout, 1920 x 1080 reference.
    private const float Frame = 24f;
    private const float RailY = -262f;
    private const float TabLeft = 96f;
    private const float TabGap = 30f;
    private const float ContentTop = -318f;
    private const float CardWidth = 540f;
    private const float CardHeight = 180f;
    private const float CardGapX = 36f;
    private const float CardGapY = 14f;
    private const int RowsPerColumn = 3;
    private const int Columns = 3;

    private sealed class Setting
    {
        public string Name;
        public string English;
        public string Description;
        public Func<string> GetValue;

        /// <summary>Called with the number of small steps (negative = down).</summary>
        public Action<int> Adjust;

        /// <summary>Small steps in one large step; 0 = only the small arrows.</summary>
        public int BigStep;
        public string SmallAmount;
        public string BigAmount;

        /// <summary>Set for a setting that opens something instead of stepping a value.</summary>
        public Action Confirm;

        /// <summary>The line under an opening setting's box.</summary>
        public string Hint = "ENTER / A / クリックで入力";
    }

    private sealed class Tab
    {
        public string Name;
        public string English;
        public readonly List<Setting> Settings = new List<Setting>();

        /// <summary>Text shown beside the cards (the controls on the input tab).</summary>
        public string Info;

        public RectTransform Root;
        public AngularGraphic Front;
        public AngularGraphic FrontBorder;
        public PolygonGraphic Top;
        public PolygonGraphic Side;
        public Text Label;
        public Text EnglishLabel;
    }

    private sealed class Card
    {
        public Setting Setting;
        public RectTransform Rect;
        public RoundedRectGraphic Back;
        public RoundedRectGraphic Plate;
        public Text PlateText;
        public RoundedRectGraphic ValueBox;
        public Text Value;
        public readonly List<Graphic> Marks = new List<Graphic>();
    }

    private GameFlow flow;
    private RectTransform face;
    private AngularGraphic rail;
    private RectTransform content;
    private Text description;
    private NameEntryUI nameEntry;
    private TimingCalibrationUI calibration;
    private readonly List<Tab> tabs = new List<Tab>();
    private readonly List<Card> cards = new List<Card>();
    private int tabIndex;
    private int selected;
    private bool tabFocus;
    private Color lineColor = SignKit.MetroRed;
    private float openedAt;

    public bool IsOpen => gameObject.activeSelf;

    /// <summary>The tab and the card in view, kept when the menus are rebuilt for a theme change.</summary>
    public int CurrentTab => tabIndex;
    public int SelectedCard => selected;

    public void Build(GameFlow gameFlow)
    {
        flow = gameFlow;
        RectTransform root = (RectTransform)transform;

        // Takes every click so nothing behind the board reacts.
        AngularGraphic blocker = UiKit.CreatePanel(root, "Blocker", SignKit.Night);
        blocker.raycastTarget = true;
        UiKit.Stretch(blocker);

        face = SignKit.CreateBoard(root, "Board", Frame, 26f).rectTransform;

        SignKit.CreateHeading(face, "設定", "Settings", new Vector2(56f, -22f), 44);

        RectTransform close = SettingsButton.CreateClose(face, "Close");
        close.anchoredPosition = new Vector2(-40f, -32f);
        close.GetComponent<UiPointerRelay>().Clicked = eventData =>
        {
            if (eventData.button == PointerEventData.InputButton.Left && !HasDialog)
            {
                Close();
            }
        };

        // The line the tabs stand on, like the sketch's double rule: drawn under them.
        rail = UiKit.CreatePanel(face, "Rail", lineColor);
        rail.rectTransform.anchorMin = new Vector2(0f, 1f);
        rail.rectTransform.anchorMax = new Vector2(1f, 1f);
        rail.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rail.rectTransform.sizeDelta = new Vector2(0f, 12f);
        rail.rectTransform.anchoredPosition = new Vector2(0f, RailY + 26f);

        CreateTabs();
        foreach (Tab tab in tabs)
        {
            BuildTab(tab);
        }

        content = UiKit.CreateRect(face, "Content");
        content.anchorMin = new Vector2(0.5f, 1f);
        content.anchorMax = new Vector2(0.5f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(Columns * CardWidth + (Columns - 1) * CardGapX, RowsPerColumn * CardHeight + (RowsPerColumn - 1) * CardGapY);
        content.anchoredPosition = new Vector2(0f, ContentTop);

        AngularGraphic rule = UiKit.CreatePanel(face, "Rule", UiKit.WithAlpha(SignKit.Ink, 0.18f));
        rule.rectTransform.anchorMin = new Vector2(0f, 0f);
        rule.rectTransform.anchorMax = new Vector2(1f, 0f);
        rule.rectTransform.pivot = new Vector2(0.5f, 0f);
        rule.rectTransform.offsetMin = new Vector2(56f, 128f);
        rule.rectTransform.offsetMax = new Vector2(-56f, 130f);

        description = SignKit.CreateText(face, "Description", string.Empty, 24, TextAnchor.MiddleLeft, SignKit.Ink);
        description.rectTransform.anchorMin = new Vector2(0f, 0f);
        description.rectTransform.anchorMax = new Vector2(1f, 0f);
        description.rectTransform.pivot = new Vector2(0.5f, 0f);
        description.rectTransform.offsetMin = new Vector2(60f, 74f);
        description.rectTransform.offsetMax = new Vector2(-60f, 118f);

        Text footer = SignKit.CreateText(face, "Footer", Footer, 17, TextAnchor.MiddleLeft, SignKit.InkDim, true);
        footer.rectTransform.anchorMin = new Vector2(0f, 0f);
        footer.rectTransform.anchorMax = new Vector2(1f, 0f);
        footer.rectTransform.pivot = new Vector2(0.5f, 0f);
        footer.rectTransform.offsetMin = new Vector2(60f, 26f);
        footer.rectTransform.offsetMax = new Vector2(-60f, 56f);

        RectTransform entryRect = UiKit.CreateRect(root, "NameEntry");
        UiKit.Stretch(entryRect);
        nameEntry = entryRect.gameObject.AddComponent<NameEntryUI>();
        nameEntry.Build();
        nameEntry.Closed += () =>
        {
            openedAt = Time.unscaledTime;
            RefreshCards();
        };

        RectTransform calibrationRect = UiKit.CreateRect(root, "Calibration");
        UiKit.Stretch(calibrationRect);
        calibration = calibrationRect.gameObject.AddComponent<TimingCalibrationUI>();
        calibration.Build();
        calibration.Closed += () =>
        {
            openedAt = Time.unscaledTime;
            RefreshCards();
        };

        gameObject.SetActive(false);
    }

    /// <param name="color">Colour of the line, the chosen tab and the selection: the song in view's line.</param>
    public void Open(Color color, int tab = 0, int card = 0)
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        openedAt = Time.unscaledTime;
        lineColor = color;
        rail.color = color;
        tabFocus = false;
        ShowTab(tab, card);
    }

    public void Close()
    {
        if (nameEntry.IsOpen)
        {
            nameEntry.Close();
        }

        if (calibration.IsOpen)
        {
            calibration.Close();
        }

        gameObject.SetActive(false);
    }

    // ---- The settings ---------------------------------------------------------------

    private void CreateTabs()
    {
        Tab gameplay = AddTab("ゲームプレイ", "Gameplay");
        Add(gameplay, "速度", "Hi-Speed", "ノーツとレーザーが流れてくる速さ",
            () => $"{GameSettings.HiSpeed:0.0}", steps => GameSettings.HiSpeed += steps * 0.1f, 5, "0.1", "0.5");
        Add(gameplay, "流れ方", "Approach", "加速：奥ではゆっくり、判定ラインに近づくほど速くなる（判定ラインでの速さとタイミングは一定と同じ）",
            () => GameSettings.ApproachAccelerate ? "加速" : "一定", steps => GameSettings.ApproachAccelerate = !GameSettings.ApproachAccelerate);
        Add(gameplay, "加速の強さ", "Accel Level", "流れ方が「加速」のときの奥での遅さ。大きいほど奥でゆっくりになり、その分ノーツが早くから見える",
            () => $"{GameSettings.ApproachStrength}", steps => GameSettings.ApproachStrength += steps, 3, "1", "3");
        Add(gameplay, "判定タイミング", "Judge Offset", "判定タイミングの補正。遅れて押しがち（SLOW が多い）なら +、早く押しがち（FAST が多い）なら −",
            () => $"{GameSettings.JudgeOffsetMs:+0;-0;0} ms", steps => GameSettings.JudgeOffsetMs += steps, 10, "1", "10");
        Add(gameplay, "表示タイミング", "Display Offset", "ノーツを描くタイミングの補正（判定は変わらない）。音より早く見えるなら +、遅く見えるなら −",
            () => $"{GameSettings.VisualOffsetMs:+0;-0;0} ms", steps => GameSettings.VisualOffsetMs += steps, 10, "1", "10");
        gameplay.Settings.Add(new Setting
        {
            Name = "タイミング調整",
            English = "Calibration",
            Description = "リズムに合わせてボタンを押し、判定タイミングと表示タイミングを自動で合わせる",
            GetValue = () => "測定する",
            Confirm = () => calibration.Open(lineColor),
            Hint = "ENTER / A / クリックで開始"
        });
        Add(gameplay, "ミラー", "Mirror", "譜面の左右を反転する（次に曲を始めたときから）",
            () => OnOff(GameSettings.Mirror), steps => GameSettings.Mirror = !GameSettings.Mirror);
        Add(gameplay, "オートプレイ", "Auto Play", "自動でプレイする（ベストスコアには記録されない）",
            () => OnOff(GameSettings.AutoPlay), steps => GameSettings.AutoPlay = !GameSettings.AutoPlay);

        Tab audio = AddTab("オーディオ", "Audio");
        Add(audio, "全体の音量", "Master Volume", "ゲーム全体の音量",
            () => $"{GameSettings.MasterVolumePercent}", steps => GameSettings.MasterVolumePercent += steps, 10, "1", "10");
        Add(audio, "曲の音量", "Music Volume", "曲の音楽の音量",
            () => $"{GameSettings.MusicVolumePercent}", steps => GameSettings.MusicVolumePercent += steps, 10, "1", "10");
        Add(audio, "音声オフセット", "Audio Offset", "曲の音を鳴らすタイミングの補正。音がノーツより遅れて聞こえるなら −（次に曲を始めたときから）",
            () => $"{GameSettings.AudioOffsetMs:+0;-0;0} ms", steps => GameSettings.AudioOffsetMs += steps, 10, "1", "10");
        Add(audio, "ノーツ音", "Note Sound", "ノーツを取ったときに音を鳴らす",
            () => OnOff(GameSettings.NoteSound), steps => GameSettings.NoteSound = !GameSettings.NoteSound);
        Add(audio, "ノーツ音の音量", "Note Sound Volume", "ノーツ音の大きさ",
            () => $"{GameSettings.NoteSoundVolumePercent}", steps => GameSettings.NoteSoundVolumePercent += steps, 10, "1", "10");

        Tab graphics = AddTab("グラフィック", "Graphics");
        Add(graphics, "発光", "Bloom", "ノーツとレーザーの光のにじみの強さ。0 で消える",
            () => $"{GameSettings.BloomPercent}%", steps => GameSettings.BloomPercent += steps * 10, 5, "10", "50");
        Add(graphics, "ステージの明るさ", "Stage Brightness", "レーンと奥の壁の明るさ",
            () => $"{GameSettings.StageBrightnessPercent}%", steps => GameSettings.StageBrightnessPercent += steps * 5, 4, "5", "20");
        Add(graphics, "グリッド", "Grid", "ノーツと一緒に流れてくるトンネルの枠の間隔（拍）。オフで表示しない",
            () => FormatGrid(GameSettings.GridBeats), steps => GameSettings.GridIntervalIndex += steps);
        Add(graphics, "左レーザーの色", "Left Laser Color", "左スティックのレーザー・矢印ノーツ・画面左の予告の色",
            () => GameSettings.LaserColorNames[GameSettings.LeftLaserColorIndex], steps => GameSettings.LeftLaserColorIndex += steps);
        Add(graphics, "右レーザーの色", "Right Laser Color", "右スティックのレーザー・矢印ノーツ・画面右の予告の色",
            () => GameSettings.LaserColorNames[GameSettings.RightLaserColorIndex], steps => GameSettings.RightLaserColorIndex += steps);
        Add(graphics, "FAST / SLOW", "Fast / Slow", "判定の下に、早い・遅いを表示する",
            () => OnOff(GameSettings.ShowFastSlow), steps => GameSettings.ShowFastSlow = !GameSettings.ShowFastSlow);
        Add(graphics, "判定のズレ", "Timing Detail", "判定の下に、ズレを ms で表示する",
            () => OnOff(GameSettings.ShowTimingDetail), steps => GameSettings.ShowTimingDetail = !GameSettings.ShowTimingDetail);
        Add(graphics, "画面モード", "Screen Mode", "フルスクリーンとウィンドウを切り替える（エディター上では変わらない）",
            () => Screen.fullScreen ? "フルスクリーン" : "ウィンドウ", steps => Screen.fullScreen = !Screen.fullScreen);

        Tab input = AddTab("入力", "Input");
        Add(input, "スティックの角度", "Stick Angle", "スティックの向きの許容角度（±）。レーザーと矢印ノーツに使う。大きいほど斜めがずれていても取れる",
            () => $"±{GameSettings.StickAngleDegrees}°", steps => GameSettings.StickAngleDegrees += steps, 5, "1", "5");
        input.Info = ControlsText;

        Tab other = AddTab("その他", "Other");
        tabs[tabs.Count - 1].Settings.Add(new Setting
        {
            Name = "プレイヤー名",
            English = "Player Name",
            Description = "曲中の画面上部に表示する名前。キーボードで入力するか、パッドで文字を選ぶ",
            GetValue = () => GameSettings.PlayerName,
            Confirm = () => nameEntry.Open(lineColor)
        });
        Add(other, "テーマ", "Theme", "メニュー画面の配色。ダークは背景が黒になる",
            () => GameSettings.DarkMode ? "ダーク" : "ライト", steps => GameSettings.DarkMode = !GameSettings.DarkMode);
    }

    private Tab AddTab(string name, string english)
    {
        Tab tab = new Tab { Name = name, English = english };
        tabs.Add(tab);
        return tab;
    }

    private static void Add(Tab tab, string name, string english, string text, Func<string> getValue, Action<int> adjust, int bigStep = 0, string small = null, string big = null)
    {
        tab.Settings.Add(new Setting
        {
            Name = name,
            English = english,
            Description = text,
            GetValue = getValue,
            Adjust = adjust,
            BigStep = bigStep,
            SmallAmount = small,
            BigAmount = big
        });
    }

    private static string FormatGrid(float beats)
    {
        if (beats <= 0f)
        {
            return "オフ";
        }

        string amount = beats < 1f ? $"1/{Mathf.RoundToInt(1f / beats)}" : $"{beats:0}";
        return $"{amount} 拍";
    }

    private static string OnOff(bool value)
    {
        return value ? "オン" : "オフ";
    }

    // ---- Tabs -----------------------------------------------------------------------

    private void BuildTab(Tab tab)
    {
        int index = tabs.IndexOf(tab);
        tab.Root = UiKit.CreateRect(face, $"Tab_{tab.English}");
        tab.Root.anchorMin = new Vector2(0f, 1f);
        tab.Root.anchorMax = new Vector2(0f, 1f);
        tab.Root.pivot = Vector2.zero;

        tab.Top = CreatePolygon(tab.Root, "Top");
        tab.Side = CreatePolygon(tab.Root, "Side");
        tab.Front = UiKit.CreatePanel(tab.Root, "Front", SignKit.Face);
        tab.Front.raycastTarget = true;
        tab.FrontBorder = UiKit.CreatePanel(tab.Root, "FrontBorder", SignKit.Ink);
        tab.FrontBorder.SetBorder(3f);
        foreach (AngularGraphic graphic in new[] { tab.Front, tab.FrontBorder })
        {
            graphic.rectTransform.anchorMin = Vector2.zero;
            graphic.rectTransform.anchorMax = Vector2.zero;
            graphic.rectTransform.pivot = Vector2.zero;
            graphic.rectTransform.anchoredPosition = Vector2.zero;
        }

        tab.Label = SignKit.CreateText(tab.Front.rectTransform, "Label", tab.Name, 30, TextAnchor.MiddleCenter, SignKit.Ink, true);
        tab.EnglishLabel = SignKit.CreateText(tab.Front.rectTransform, "English", tab.English, 15, TextAnchor.MiddleCenter, SignKit.InkSoft);

        UiPointerRelay relay = tab.Front.gameObject.AddComponent<UiPointerRelay>();
        relay.Clicked = eventData =>
        {
            if (eventData.button == PointerEventData.InputButton.Left && CanTakeInput)
            {
                tabFocus = false;
                ShowTab(index, 0);
            }
        };
    }

    private static PolygonGraphic CreatePolygon(Transform parent, string name)
    {
        RectTransform rect = UiKit.CreateRect(parent, name);
        UiKit.Stretch(rect);
        PolygonGraphic polygon = rect.gameObject.AddComponent<PolygonGraphic>();
        polygon.raycastTarget = false;
        return polygon;
    }

    /// <summary>Sizes and colours every tab box; the chosen one stands larger, in the line colour.</summary>
    private void LayoutTabs()
    {
        float x = TabLeft;
        for (int index = 0; index < tabs.Count; index++)
        {
            Tab tab = tabs[index];
            bool chosen = index == tabIndex;
            float width = chosen ? 272f : 212f;
            float height = chosen ? 112f : 86f;
            float depth = chosen ? 24f : 18f;

            tab.Root.sizeDelta = new Vector2(width + depth, height + depth);
            tab.Root.anchoredPosition = new Vector2(x, RailY);
            x += width + depth + TabGap;

            tab.Front.rectTransform.sizeDelta = new Vector2(width, height);
            tab.FrontBorder.rectTransform.sizeDelta = new Vector2(width, height);

            // Points from the root's centre: the lid leans back to the upper right, the side to the right.
            Vector2 centre = new Vector2(width + depth, height + depth) * 0.5f;
            tab.Top.SetPoints(Offset(centre, new Vector2(0f, height), new Vector2(depth, height + depth), new Vector2(width + depth, height + depth), new Vector2(width, height)));
            tab.Side.SetPoints(Offset(centre, new Vector2(width, 0f), new Vector2(width, height), new Vector2(width + depth, height + depth), new Vector2(width + depth, depth)));

            Color front = chosen ? lineColor : SignKit.Face;
            tab.Front.color = front;
            tab.Front.SetGradient(front);
            tab.FrontBorder.color = chosen ? Color.Lerp(lineColor, Color.black, 0.25f) : SignKit.Ink;
            tab.Top.color = chosen ? Color.Lerp(lineColor, Color.white, 0.35f) : SignKit.SteelLight;
            tab.Side.color = chosen ? Color.Lerp(lineColor, Color.black, 0.35f) : SignKit.SteelDark;

            Color ink = chosen ? (IsLight(lineColor) ? SignKit.DarkInk : Color.white) : SignKit.Ink;
            if (!chosen && tabFocus)
            {
                ink = SignKit.InkSoft;
            }

            int size = chosen ? 34 : 27;
            tab.Label.fontSize = size;
            tab.Label.color = ink;
            UiKit.Stretch(tab.Label.rectTransform, 0f, height * 0.42f, 0f, 0f);
            tab.Label.rectTransform.anchoredPosition += new Vector2(0f, size * SignKit.CapRaise * 0.4f - 4f);
            tab.EnglishLabel.fontSize = chosen ? 17 : 14;
            tab.EnglishLabel.color = UiKit.WithAlpha(ink, 0.75f);
            UiKit.Stretch(tab.EnglishLabel.rectTransform, 0f, 8f, 0f, height * 0.62f);
        }
    }

    private static IEnumerable<Vector2> Offset(Vector2 centre, params Vector2[] points)
    {
        foreach (Vector2 point in points)
        {
            yield return point - centre;
        }
    }

    private static bool IsLight(Color color)
    {
        return color.r * 0.3f + color.g * 0.59f + color.b * 0.11f > 0.62f;
    }

    private void ShowTab(int index, int card)
    {
        tabIndex = Mathf.Clamp(index, 0, tabs.Count - 1);
        LayoutTabs();
        BuildCards(tabs[tabIndex]);
        selected = Mathf.Clamp(card, 0, Mathf.Max(0, cards.Count - 1));
        RefreshCards();
    }

    // ---- Cards ----------------------------------------------------------------------

    private void BuildCards(Tab tab)
    {
        cards.Clear();
        for (int child = content.childCount - 1; child >= 0; child--)
        {
            GameObject old = content.GetChild(child).gameObject;
            old.SetActive(false);
            Destroy(old);
        }

        for (int index = 0; index < tab.Settings.Count; index++)
        {
            int column = index / RowsPerColumn;
            int row = index % RowsPerColumn;
            Card card = BuildCard(tab.Settings[index], index);
            card.Rect.anchoredPosition = new Vector2(column * (CardWidth + CardGapX), -row * (CardHeight + CardGapY));
            cards.Add(card);
        }

        if (tab.Info != null)
        {
            // The rest of the width, beside the first column.
            float left = CardWidth + CardGapX;
            RoundedRectGraphic panel = SignKit.CreateRoundedRect(content, "Info", UiKit.WithAlpha(SignKit.Ink, GameSettings.DarkMode ? 0.01f : 0.045f), 16f);
            panel.rectTransform.anchorMin = new Vector2(0f, 1f);
            panel.rectTransform.anchorMax = new Vector2(0f, 1f);
            panel.rectTransform.pivot = new Vector2(0f, 1f);
            panel.rectTransform.sizeDelta = new Vector2(content.sizeDelta.x - left, content.sizeDelta.y);
            panel.rectTransform.anchoredPosition = new Vector2(left, 0f);

            Text heading = SignKit.CreateText(panel.rectTransform, "Heading", SignKit.Bilingual("操作方法", "Controls", 17), 30, TextAnchor.UpperLeft, SignKit.Ink, true);
            UiKit.Stretch(heading, 40f, 0f, 40f, 28f);
            Text info = SignKit.CreateText(panel.rectTransform, "Text", tab.Info, 22, TextAnchor.UpperLeft, SignKit.InkSoft);
            info.lineSpacing = 1.05f;
            UiKit.Stretch(info, 40f, 20f, 40f, 90f);
        }
    }

    private Card BuildCard(Setting setting, int index)
    {
        Card card = new Card { Setting = setting };
        card.Rect = UiKit.CreateRect(content, $"Card_{setting.English}");
        card.Rect.anchorMin = new Vector2(0f, 1f);
        card.Rect.anchorMax = new Vector2(0f, 1f);
        card.Rect.pivot = new Vector2(0f, 1f);
        card.Rect.sizeDelta = new Vector2(CardWidth, CardHeight);

        card.Back = SignKit.CreateRoundedRect(card.Rect, "Back", Color.clear, 18f);
        UiKit.Stretch(card.Back);
        UiPointerRelay backRelay = SignKit.AddRelay(card.Back);
        backRelay.Entered = () =>
        {
            if (CanTakeInput)
            {
                Select(index, false);
            }
        };
        backRelay.Clicked = eventData =>
        {
            if (eventData.button == PointerEventData.InputButton.Left && CanTakeInput)
            {
                Select(index, false);
                if (setting.Confirm != null)
                {
                    setting.Confirm();
                }
            }
        };

        // The name plate, a rounded box above the value as in the sketch's 速度.
        card.Plate = SignKit.CreateRoundedRect(card.Rect, "Plate", SignKit.Ink, 27f, 3f);
        UiKit.Place(card.Plate, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(380f, 56f));
        card.PlateText = SignKit.CreateText(card.Plate.rectTransform, "Name", SignKit.Bilingual(setting.Name, setting.English, 16), 26, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Stretch(card.PlateText);
        card.PlateText.rectTransform.anchoredPosition = new Vector2(0f, 26f * SignKit.CapRaise * 0.4f);

        bool action = setting.Confirm != null;
        float boxWidth = action ? 360f : setting.BigStep > 0 ? 210f : 280f;
        card.ValueBox = SignKit.CreateRoundedRect(card.Rect, "ValueBox", SignKit.Ink, 10f, 3f);
        UiKit.Place(card.ValueBox, new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(boxWidth, 66f));
        card.Value = SignKit.CreateFittedText(card.ValueBox.rectTransform, "Value", 34, 18, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Stretch(card.Value, 10f, 0f, 10f, 0f);
        card.Value.rectTransform.anchoredPosition = new Vector2(0f, 34f * SignKit.CapRaise);
        card.Value.supportRichText = false;

        if (action)
        {
            Text hint = SignKit.CreateText(card.Rect, "Hint", setting.Hint, 16, TextAnchor.MiddleCenter, SignKit.InkDim, true);
            UiKit.Place(hint, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(400f, 24f));
            card.Marks.Add(hint);
            return card;
        }

        float valueHalf = boxWidth * 0.5f;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            int direction = sign;
            float smallX = sign * (valueHalf + 44f);
            AddArrow(card, direction, 1, smallX, setting.SmallAmount);
            if (setting.BigStep > 0)
            {
                AddArrow(card, direction, setting.BigStep, sign * (valueHalf + 112f), setting.BigAmount);
            }
        }

        return card;
    }

    /// <summary>A ◁ / ▷ button (two triangles for the large step) with its amount under it.</summary>
    private void AddArrow(Card card, int direction, int steps, float x, string amount)
    {
        bool big = steps > 1;
        RectTransform hit = UiKit.CreateRect(card.Rect, big ? "BigArrow" : "Arrow");
        UiKit.Place(hit, new Vector2(0.5f, 1f), new Vector2(x, -80f), new Vector2(big ? 74f : 58f, 78f));
        AngularGraphic hitArea = UiKit.CreatePanel(hit, "Hit", Color.clear);
        UiKit.Stretch(hitArea);
        UiPointerRelay relay = SignKit.AddRelay(hitArea);
        int index = cards.Count;
        relay.Clicked = eventData =>
        {
            if (eventData.button == PointerEventData.InputButton.Left && CanTakeInput)
            {
                Select(index, false);
                Step(card, direction * steps);
            }
        };

        int count = big ? 2 : 1;
        float width = big ? 26f : 30f;
        float spread = big ? 22f : 0f;
        for (int part = 0; part < count; part++)
        {
            ChevronGraphic triangle = SignKit.CreateChevron(hit, "Triangle", SignKit.Ink, 0f);
            float offset = (part - (count - 1) * 0.5f) * spread * direction;
            // Centre-pivoted, so the left arrows turn in place (the hit rect is 78 high; the triangle's middle sits 32 below its top).
            UiKit.Place(triangle, new Vector2(0.5f, 0.5f), new Vector2(offset, 7f), new Vector2(width, big ? 40f : 44f));
            if (direction < 0)
            {
                triangle.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
            }

            card.Marks.Add(triangle);
        }

        if (!string.IsNullOrEmpty(amount))
        {
            Text caption = SignKit.CreateText(hit, "Amount", (direction > 0 ? "+" : "−") + amount, 16, TextAnchor.MiddleCenter, SignKit.InkSoft, true);
            UiKit.Place(caption, new Vector2(0.5f, 0f), new Vector2(0f, -2f), new Vector2(90f, 22f));
        }
    }

    private void RefreshCards()
    {
        for (int index = 0; index < cards.Count; index++)
        {
            Card card = cards[index];
            bool chosen = index == selected && !tabFocus;
            card.Back.color = chosen ? UiKit.WithAlpha(SignKit.Ink, GameSettings.DarkMode ? 0.012f : 0.06f) : Color.clear;
            card.Plate.BorderWidth = chosen ? 0f : 3f;
            card.Plate.color = SignKit.Ink;
            card.PlateText.color = chosen ? SignKit.OnInk : SignKit.Ink;
            card.ValueBox.BorderWidth = chosen ? 5f : 3f;
            card.ValueBox.color = chosen ? lineColor : SignKit.Ink;
            card.Value.text = card.Setting.GetValue();
            foreach (Graphic mark in card.Marks)
            {
                mark.color = mark is Text ? SignKit.InkDim : SignKit.Ink;
            }
        }

        Setting setting = cards.Count > 0 && !tabFocus ? cards[selected].Setting : null;
        description.text = setting != null ? setting.Description : $"{tabs[tabIndex].Name} の設定。← → でタブを切り替え、↓ で項目へ";
        LayoutTabs();
    }

    private void Select(int index, bool keyboard)
    {
        if (index < 0 || index >= cards.Count || (index == selected && !tabFocus))
        {
            return;
        }

        selected = index;
        tabFocus = false;
        RefreshCards();
    }

    private void Step(Card card, int steps)
    {
        if (card.Setting.Confirm != null)
        {
            card.Setting.Confirm();
            return;
        }

        card.Setting.Adjust(steps);
        RefreshCards();
    }

    // ---- Input ----------------------------------------------------------------------

    private bool HasDialog => nameEntry.IsOpen || calibration.IsOpen;

    private bool CanTakeInput => !HasDialog && Time.unscaledTime - openedAt >= 0.1f;

    private void Update()
    {
        if (!CanTakeInput)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        if (MenuInput.Cancel || MenuInput.Option)
        {
            MenuInput.Consume();
            Close();
            return;
        }

        int tabStep = 0;
        if ((keyboard != null && keyboard.qKey.wasPressedThisFrame) || (gamepad != null && gamepad.leftShoulder.wasPressedThisFrame))
        {
            tabStep = -1;
        }
        else if ((keyboard != null && keyboard.eKey.wasPressedThisFrame) || (gamepad != null && gamepad.rightShoulder.wasPressedThisFrame))
        {
            tabStep = 1;
        }

        if (tabFocus)
        {
            if (MenuInput.Left)
            {
                tabStep = -1;
            }
            else if (MenuInput.Right)
            {
                tabStep = 1;
            }
            else if (MenuInput.Down || MenuInput.Confirm)
            {
                tabFocus = false;
                RefreshCards();
                return;
            }
        }

        if (tabStep != 0)
        {
            ShowTab((tabIndex + tabStep + tabs.Count) % tabs.Count, 0);
            return;
        }

        if (tabFocus || cards.Count == 0)
        {
            return;
        }

        if (MenuInput.Up)
        {
            if (selected == 0)
            {
                tabFocus = true;
                RefreshCards();
            }
            else
            {
                Select(selected - 1, true);
            }

            return;
        }

        if (MenuInput.Down)
        {
            Select(Mathf.Min(selected + 1, cards.Count - 1), true);
            return;
        }

        Card current = cards[selected];
        bool large = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        int bigSteps = Mathf.Max(1, current.Setting.BigStep);
        if (MenuInput.Left)
        {
            Step(current, large ? -bigSteps : -1);
        }
        else if (MenuInput.Right)
        {
            Step(current, large ? bigSteps : 1);
        }
        else if (gamepad != null && gamepad.leftTrigger.wasPressedThisFrame)
        {
            Step(current, -bigSteps);
        }
        else if (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame)
        {
            Step(current, bigSteps);
        }
        else if (MenuInput.Confirm)
        {
            MenuInput.Consume();
            Step(current, 1);
        }
    }
}
