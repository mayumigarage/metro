using System;
using System.Collections.Generic;
using System.Linq;
using MetroEd;
using UnityEngine;

/// <summary>
/// The chart editor's screen layout, laid out like a drawing application:
///
///   menu bar (ファイル / 編集 / ツール / 譜面 / 表示 / 再生 / ウィンドウ / ヘルプ, transport, sidebar toggles)
///   control bar (grid, zoom, and the settings of the selection or the current tool)
///   tool strip | timeline (laser 2D views under the laser columns) | 2D preview | game preview (3D)
///   status bar
///
/// Both previews are sidebars on the right that are shown and hidden with the toggles at the right
/// of the menu bar and resized by dragging their left edge. Chart info, playback settings and the
/// key list are floating dialogs opened from the menus.
///
/// IMGUI hands an event to controls in drawing order, so everything that floats (menus, the chart
/// list, dialogs) is drawn last but takes its clicks first: <see cref="HandleOverlayInput"/> runs
/// before the rest and the base screen is disabled for a click that lands on a floating part.
/// Changes that add or remove GUILayout controls are deferred to the next Layout event
/// (<see cref="Defer"/>), so one event's passes always see the same layout.
/// </summary>
public sealed partial class ChartEditorScreen
{
    private const float MenuBarHeight = 26f;
    private const float ControlBarHeight = 32f;
    private const float ToolStripWidth = 44f;
    private const float ToolButtonSize = 36f;
    private const float SidebarHeaderHeight = 24f;
    private const float MinSidebarWidth = 200f;
    private const float MinTimelineWidth = 460f;
    private const float MenuRowHeight = 22f;
    private const float MenuSeparatorHeight = 7f;
    private const float MenuWidth = 290f;
    private const float LaserViewHeader = 18f;

    private static readonly Color BarColor = new Color(0.09f, 0.09f, 0.1f);
    private static readonly Color ControlBarColor = new Color(0.15f, 0.15f, 0.165f);
    private static readonly Color AccentColor = new Color(0.25f, 0.45f, 0.78f);
    private static readonly Color BorderColor = new Color(1f, 1f, 1f, 0.1f);
    private static readonly string[] StickShort = { "自動", "左", "右" };
    private static readonly NoteDirection[] DirectionRowLayout =
    {
        NoteDirection.None, NoteDirection.Left, NoteDirection.UpLeft, NoteDirection.Up, NoteDirection.UpRight,
        NoteDirection.Right, NoteDirection.DownRight, NoteDirection.Down, NoteDirection.DownLeft
    };

    private enum Tool
    {
        Select,
        Box,
        Tap,
        Hold,
        Flick,
        Spin,
        Laser,
        Erase
    }

    private struct ToolInfo
    {
        public Tool Tool;
        public string Name;
        public KeyCode Key;
        public string Hint;
    }

    private static readonly ToolInfo[] Tools =
    {
        new ToolInfo { Tool = Tool.Select, Name = "選択", Key = KeyCode.V, Hint = "クリック: 選択 ／ ドラッグ: 移動 ／ 空きをドラッグ: 範囲選択 ／ Ctrl+クリック: 追加選択 ／ Shift+ノーツをドラッグ: 方向 ／ 右クリック: 削除" },
        new ToolInfo { Tool = Tool.Box, Name = "範囲選択", Key = KeyCode.B, Hint = "ドラッグで囲んだノーツを選択（Ctrl で追加）" },
        new ToolInfo { Tool = Tool.Tap, Name = "ノーツ", Key = KeyCode.N, Hint = "クリック: 配置 ／ 空きからドラッグ: 方向付き ／ 右ドラッグ: フリックのみ ／ Ctrl+上へドラッグ: ロング ／ ノーツをドラッグ: 移動 ／ 右クリック: 削除 ／ Alt: 吸着なし" },
        new ToolInfo { Tool = Tool.Hold, Name = "ロング", Key = KeyCode.H, Hint = "上へドラッグ: ロングを配置 ／ ノーツをドラッグ: 長さを変える（タップはロングになる）" },
        new ToolInfo { Tool = Tool.Flick, Name = "フリック", Key = KeyCode.F, Hint = "ドラッグした向きのフリックのみノーツ（クリックだけなら設定した方向）／ ノーツをドラッグ: フリックのみに変える" },
        new ToolInfo { Tool = Tool.Spin, Name = "回転", Key = KeyCode.R, Hint = "クリック: スティックを回すノーツを配置（向きは右のバーで反時計 / 時計）／ ノーツをクリック: 回転ノーツに変える ／ ドラッグ: 移動" },
        new ToolInfo { Tool = Tool.Laser, Name = "レーザー", Key = KeyCode.L, Hint = "レーザー列をクリック: 選択中のレーザーに点を追加（なければ新規）／ Shift+クリック: 新しいレーザー ／ 下の2Dビューをクリック: 再生位置に点" },
        new ToolInfo { Tool = Tool.Erase, Name = "消しゴム", Key = KeyCode.E, Hint = "クリック / なぞる: ノーツ・レーザー点を削除" }
    };

    private Tool tool = Tool.Tap;
    private bool show3D = true;
    private bool show2D;
    private bool showLaserView = true;
    private float width3D = -1f;
    private float width2D = 260f;

    [NonSerialized] private Rect gameViewport = new Rect(0f, 0f, 1f, 1f);
    [NonSerialized] private Rect controlBarRect;
    [NonSerialized] private Rect toolStripRect;
    [NonSerialized] private Rect timelineRect;
    [NonSerialized] private Rect sidebar2DRect;
    [NonSerialized] private Rect sidebar3DRect;
    [NonSerialized] private Rect statusRect;
    [NonSerialized] private Rect laserViewLeft;
    [NonSerialized] private Rect laserViewRight;
    [NonSerialized] private float laserViewHeight;
    [NonSerialized] private bool pointerBlocked;
    [NonSerialized] private bool followOnLayout;
    [NonSerialized] private string tooltipText = string.Empty;
    [NonSerialized] private readonly List<Action> deferred = new List<Action>();

    // Menus
    private sealed class MenuEntry
    {
        public string Label;
        public string Shortcut;
        public Func<bool> Checked;
        public Func<bool> Enabled;
        public Action Run;
        public bool IsSeparator;
    }

    private sealed class MenuDef
    {
        public string Title;
        public MenuEntry[] Entries;
        public Rect TitleRect;
    }

    [NonSerialized] private List<MenuDef> menus;
    [NonSerialized] private int openMenu = -1;
    [NonSerialized] private Rect chartNameRect;

    // Dialogs
    private sealed class Dialog
    {
        public string Title;
        public Rect Rect;
        public bool Open;
        public Vector2 Scroll;
        public Action Body;
    }

    [NonSerialized] private Dialog infoDialog;
    [NonSerialized] private Dialog playDialog;
    [NonSerialized] private Dialog helpDialog;
    [NonSerialized] private Dialog renameDialog;
    [NonSerialized] private Dialog generateDialog;
    [NonSerialized] private readonly ChartAutoGenerator.Settings generateSettings = new ChartAutoGenerator.Settings { seed = Environment.TickCount & 0xFFFF };
    [NonSerialized] private bool generateReplace = true;
    [NonSerialized] private bool generateFromAudio = true;
    [NonSerialized] private ChartAudioAnalysis generateAnalysis;
    [NonSerialized] private AudioClip generateAnalysisClip;
    [NonSerialized] private string renameText = string.Empty;
    [NonSerialized] private string renameArtist = string.Empty;
    [NonSerialized] private readonly List<Dialog> dialogs = new List<Dialog>();
    [NonSerialized] private Vector2 dialogDragOffset;

    private float PlayheadLift => Mathf.Max((timelineHeight - HeaderHeight) * 0.2f, showLaserView ? laserViewHeight + 30f : 0f);

    /// <summary>Scrolls so the playhead sits in the lower part of the timeline (above the laser views).</summary>
    private void FollowCursor()
    {
        viewTime = cursorTime - PlayheadLift / pixelsPerSecond;
    }

    /// <summary>Scrolls to the playhead when it is out of view (or hidden behind the laser views).</summary>
    private void RevealCursor()
    {
        float y = TimeToY(cursorTime);
        if (y < HeaderHeight || y > timelineHeight - PlayheadLift + 10f)
        {
            FollowCursor();
        }
    }

    /// <summary>Runs <paramref name="action"/> at the start of the next Layout event.</summary>
    private void Defer(Action action)
    {
        if (action != null)
        {
            deferred.Add(action);
        }
    }

    private void RunDeferred()
    {
        if (deferred.Count == 0)
        {
            return;
        }

        Action[] actions = deferred.ToArray();
        deferred.Clear();
        foreach (Action action in actions)
        {
            action();
        }
    }

    private bool IsTimelineBlocked(Vector2 mouse)
    {
        return pointerBlocked || showLaserView && (laserViewLeft.Contains(mouse) || laserViewRight.Contains(mouse));
    }

    // ------------------------------------------------------------------ settings

    private void LoadLayout()
    {
        show3D = PlayerPrefs.GetInt("ChartEditor.Show3D", 1) == 1;
        show2D = PlayerPrefs.GetInt("ChartEditor.Show2D", 0) == 1;
        showLaserView = PlayerPrefs.GetInt("ChartEditor.ShowLaserView", 1) == 1;
        width3D = PlayerPrefs.GetFloat("ChartEditor.Width3D", -1f);
        width2D = PlayerPrefs.GetFloat("ChartEditor.Width2D", 260f);
        tool = (Tool)Mathf.Clamp(PlayerPrefs.GetInt("ChartEditor.Tool", (int)Tool.Tap), 0, Tools.Length - 1);
        editView3D = PlayerPrefs.GetInt("ChartEditor.EditView3D", 0) == 1;
    }

    private void SaveLayout()
    {
        PlayerPrefs.SetInt("ChartEditor.Show3D", show3D ? 1 : 0);
        PlayerPrefs.SetInt("ChartEditor.Show2D", show2D ? 1 : 0);
        PlayerPrefs.SetInt("ChartEditor.ShowLaserView", showLaserView ? 1 : 0);
        PlayerPrefs.SetFloat("ChartEditor.Width3D", width3D);
        PlayerPrefs.SetFloat("ChartEditor.Width2D", width2D);
        PlayerPrefs.SetInt("ChartEditor.Tool", (int)tool);
        PlayerPrefs.SetInt("ChartEditor.EditView3D", editView3D ? 1 : 0);
    }

    private void SetTool(Tool value)
    {
        tool = value;
        SaveLayout();
    }

    private void Toggle3D()
    {
        show3D = !show3D;
        if (!show3D && flow != null && flow.IsEmbeddedPreview)
        {
            flow.StopEmbeddedPreview();
            previewWasRunning = false;
        }

        SaveLayout();
    }

    private void Toggle2D()
    {
        show2D = !show2D;
        SaveLayout();
    }

    private void ToggleLaserView()
    {
        showLaserView = !showLaserView;
        SaveLayout();
    }

    private void ResetLayout()
    {
        if (!show3D)
        {
            Toggle3D();
        }

        show2D = false;
        showLaserView = true;
        width3D = -1f;
        width2D = 260f;
        SaveLayout();
    }

    private static string ToolName(Tool value)
    {
        return Tools.First(info => info.Tool == value).Name;
    }

    private string LaneHint()
    {
        return Tools.First(info => info.Tool == tool).Hint;
    }

    private string LaserColumnHint()
    {
        return tool == Tool.Laser
            ? "クリック: 選択中のレーザーに点を追加（なければ新規） ／ Shift+クリック: 新しいレーザー ／ ドラッグ: 点を移動 ／ 右クリック: 点を削除（線上でレーザーごと） ／ Alt: 細かく"
            : tool == Tool.Erase
                ? "クリック: 点を削除（線上でレーザーごと）"
                : "点をドラッグ: 移動 ／ 線をクリック: 選択 ／ 右クリック: 削除 ／ 配置はレーザーツール (L)";
    }

    // ------------------------------------------------------------------ frame

    private void OnGUI()
    {
        if (document == null)
        {
            return;
        }

        Font font = SignKit.Font;
        if (font != null && GUI.skin.font != font)
        {
            // The sign font is created at 32 px; the default IMGUI styles would inherit that size.
            GUI.skin.font = font;
            foreach (GUIStyle style in new[] { GUI.skin.label, GUI.skin.button, GUI.skin.toggle, GUI.skin.textField, GUI.skin.textArea, GUI.skin.box })
            {
                style.fontSize = 12;
            }
        }

        Event current = Event.current;
        if (current.isKey && Time.frameCount == openedFrame)
        {
            current.Use();
        }

        if (current.type == EventType.Layout)
        {
            RunDeferred();
        }

        GuiState.BeginFrame();
        Handles.BeginFrame();
        if (current.type == EventType.Repaint)
        {
            tooltipText = string.Empty;
        }

        BuildMenus();
        ComputeLayout();
        HandleOverlayInput(current);
        pointerBlocked = (current.type == EventType.MouseDown || current.type == EventType.ScrollWheel) && PointerOverOverlay(current.mousePosition);

        bool enabledBefore = GUI.enabled;
        if (pointerBlocked)
        {
            GUI.enabled = false;
        }

        HandleKeyboard(current);
        GetAudioClip();

        DrawMenuBar(current);
        DrawControlBar();
        DrawToolStrip(current);
        HandleSplitters(current);
        DrawTimelineArea(current);
        if (show2D)
        {
            Draw2DSidebar(sidebar2DRect);
        }

        if (show3D)
        {
            Draw3DHeader(sidebar3DRect);
        }

        DrawSplitters(current);
        string guiTip = GUI.tooltip;
        if (current.type == EventType.Repaint && string.IsNullOrEmpty(tooltipText) && !string.IsNullOrEmpty(guiTip))
        {
            tooltipText = guiTip;
        }

        DrawStatusBar(statusRect);
        GUI.enabled = enabledBefore;

        DrawChartList();
        DrawDialogs(current);
        DrawMenuDropdown(current);
    }

    private void ComputeLayout()
    {
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;
        controlBarRect = new Rect(0f, MenuBarHeight, screenWidth, ControlBarHeight);
        float top = controlBarRect.yMax;
        float bodyHeight = Mathf.Max(100f, screenHeight - top - StatusHeight);
        statusRect = new Rect(0f, screenHeight - StatusHeight, screenWidth, StatusHeight);
        toolStripRect = new Rect(0f, top, ToolStripWidth, bodyHeight);

        float bodyWidth = screenWidth - ToolStripWidth;
        if (width3D < 0f)
        {
            width3D = Mathf.Round(screenWidth * 0.38f);
        }

        float room = Mathf.Max(0f, bodyWidth - MinTimelineWidth);
        float w3 = show3D ? Mathf.Clamp(width3D, MinSidebarWidth, Mathf.Max(MinSidebarWidth, room - (show2D ? MinSidebarWidth : 0f))) : 0f;
        float w2 = show2D ? Mathf.Clamp(width2D, MinSidebarWidth, Mathf.Max(MinSidebarWidth, room - w3)) : 0f;
        timelineRect = new Rect(ToolStripWidth, top, Mathf.Max(120f, bodyWidth - w2 - w3), bodyHeight);
        sidebar2DRect = new Rect(timelineRect.xMax, top, w2, bodyHeight);
        sidebar3DRect = new Rect(sidebar2DRect.xMax, top, w3, bodyHeight);

        Rect game = new Rect(sidebar3DRect.x, sidebar3DRect.y + SidebarHeaderHeight, sidebar3DRect.width, sidebar3DRect.height - SidebarHeaderHeight);
        gameViewport = show3D && game.width > 1f && game.height > 1f
            ? new Rect(game.x / screenWidth, (screenHeight - game.yMax) / screenHeight, game.width / screenWidth, game.height / screenHeight)
            : new Rect(0f, 0f, 1f, 1f);
    }

    // ------------------------------------------------------------------ menus

    private static MenuEntry Item(string label, string shortcut, Action run, Func<bool> enabled = null, Func<bool> isChecked = null)
    {
        return new MenuEntry { Label = label, Shortcut = shortcut, Run = run, Enabled = enabled, Checked = isChecked };
    }

    private static readonly MenuEntry Separator = new MenuEntry { IsSeparator = true };

    private bool HasSelection() => selectedNotes.Count > 0 || selectedLaser != null || selectedLasers.Count > 0;

    private void BuildMenus()
    {
        if (menus != null)
        {
            LayoutMenuTitles();
            return;
        }

        infoDialog = new Dialog { Title = "譜面情報", Rect = new Rect(120f, 100f, 360f, 560f), Body = DrawInfoDialog };
        playDialog = new Dialog { Title = "再生設定", Rect = new Rect(160f, 120f, 320f, 250f), Body = DrawPlayDialog };
        helpDialog = new Dialog { Title = "操作一覧", Rect = new Rect(200f, 110f, 560f, 470f), Body = DrawHelpDialog };
        renameDialog = new Dialog { Title = "譜面名・アーティスト", Rect = new Rect(220f, 140f, 380f, 200f), Body = DrawRenameDialog };
        generateDialog = new Dialog { Title = "自動生成（下書き）", Rect = new Rect(240f, 120f, 380f, 440f), Body = DrawGenerateDialog };
        dialogs.Clear();
        dialogs.Add(generateDialog);
        dialogs.Add(renameDialog);
        dialogs.Add(infoDialog);
        dialogs.Add(playDialog);
        dialogs.Add(helpDialog);

        List<MenuEntry> chartMenu = new List<MenuEntry>
        {
            Item("譜面情報…", "I", () => infoDialog.Open = !infoDialog.Open, null, () => infoDialog.Open),
            Item("自動生成…", string.Empty, OpenGenerateDialog),
            Separator,
            Item("オフセットを再生位置にする", string.Empty, () =>
            {
                double cursor = cursorTime;
                Modify("Set offset", () => Chart.offset = Math.Round(cursor * 10000d) / 10000d);
            }),
            Item("音源ファイルを取り込む…", string.Empty, ImportAudio),
            Item("波形から開始位置を自動調整", string.Empty, AutoAlignAudio, () => waveform != null)
        };

        menus = new List<MenuDef>
        {
            new MenuDef
            {
                Title = "ファイル",
                Entries = new[]
                {
                    Item("新規", "Ctrl+N", NewChart),
                    Item("開く…", "Ctrl+O", OpenChartFile),
                    Item("譜面一覧", string.Empty, () =>
                    {
                        RefreshChartList();
                        showChartList = true;
                    }),
                    Separator,
                    Item("保存", "Ctrl+S", Save),
                    Item("名前を変更…", "F2", OpenRename),
                    Item("書き出し…", string.Empty, ExportChart),
                    Separator,
                    Item("タイトルへ戻る", string.Empty, ExitToTitle)
                }
            },
            new MenuDef
            {
                Title = "編集",
                Entries = new[]
                {
                    Item("元に戻す", "Ctrl+Z", () =>
                    {
                        Undo.PerformUndo(document);
                        UpdateDirty();
                    }),
                    Item("やり直し", "Ctrl+Y", () =>
                    {
                        Undo.PerformRedo(document);
                        UpdateDirty();
                    }),
                    Separator,
                    Item("切り取り", "Ctrl+X", () =>
                    {
                        CopySelection();
                        DeleteSelection();
                    }, HasSelection),
                    Item("コピー", "Ctrl+C", CopySelection, HasSelection),
                    Item("貼り付け（再生位置へ）", "Ctrl+V", Paste, () => clipboardNotes.Count > 0 || clipboardLasers.Count > 0),
                    Item("複製", "Ctrl+D", () =>
                    {
                        CopySelection();
                        Paste();
                    }, HasSelection),
                    Item("削除", "Delete", DeleteSelection, HasSelection),
                    Separator,
                    Item("すべてのノーツを選択", "Ctrl+A", SelectAllNotes),
                    Item("選択範囲を反転", "Ctrl+I", InvertSelection),
                    Item("選択を解除", "Esc", ClearSelection, HasSelection),
                    Separator,
                    Item("左右ミラー（L↔R・左↔右レーザー）", "M", () => MirrorSelection(true, false), CanMirror),
                    Item("上下ミラー（L↔ZL・R↔ZR）", "Shift+M", () => MirrorSelection(false, true), CanMirror),
                    Item("上下左右ミラー（L↔ZR・R↔ZL）", "Ctrl+M", () => MirrorSelection(true, true), CanMirror),
                    Item("前後反転（時間を逆に）", string.Empty, ReverseSelectionInTime, HasSelection),
                    Separator,
                    Item("フリックのみを切り替え", "Shift+F", ToggleFlickOnly, () => selectedNotes.Count > 0)
                }
            },
            new MenuDef
            {
                Title = "ツール",
                Entries = Tools.Select(info => Item(info.Name, info.Key.ToString(), () => SetTool(info.Tool), null, () => tool == info.Tool)).ToArray()
            },
            new MenuDef { Title = "譜面", Entries = chartMenu.ToArray() },
            new MenuDef
            {
                Title = "表示",
                Entries = new[]
                {
                    Item("3D編集ビュー", "Tab", ToggleEditView3D, null, () => editView3D),
                    Item("3D視点をリセット", string.Empty, () => SetViewPreset(ViewPreset.Front), () => editView3D),
                    Separator,
                    Item("ズームイン", "Ctrl+=", () => SetZoom(pixelsPerSecond * 1.25f, TimeToY(cursorTime))),
                    Item("ズームアウト", "Ctrl+-", () => SetZoom(pixelsPerSecond / 1.25f, TimeToY(cursorTime))),
                    Separator,
                    Item("グリッドを細かく", "]", () => StepDivision(1)),
                    Item("グリッドを粗く", "[", () => StepDivision(-1)),
                    Item("グリッドに吸着", "G", () => snapEnabled = !snapEnabled, null, () => snapEnabled),
                    Separator,
                    Item("先頭へ", "Home", GoToStart),
                    Item("再生位置を表示", string.Empty, FollowCursor)
                }
            },
            new MenuDef
            {
                Title = "再生",
                Entries = new[]
                {
                    Item("音楽を再生 / 停止", "Space", TogglePlayback),
                    Item("プレビュー（再生位置から）", "P", TogglePreview),
                    Item("全画面でプレビュー", "Shift+P", PreviewInGame),
                    Item("先頭から再生", string.Empty, () => PlayFrom(0d)),
                    Separator,
                    Item("音楽を鳴らす", string.Empty, () => musicEnabled = !musicEnabled, null, () => musicEnabled),
                    Item("クラップを鳴らす", string.Empty, () => clapEnabled = !clapEnabled, null, () => clapEnabled),
                    Item("停止で開始位置に戻る", string.Empty, () => returnOnStop = !returnOnStop, null, () => returnOnStop),
                    Item("全画面プレビューをオートプレイ", string.Empty, () => GameSettings.AutoPlay = !GameSettings.AutoPlay, () => !flow.IsEmbeddedPreview, () => GameSettings.AutoPlay),
                    Separator,
                    Item("再生設定…", string.Empty, () => playDialog.Open = true)
                }
            },
            new MenuDef
            {
                Title = "ウィンドウ",
                Entries = new[]
                {
                    Item("ゲームプレビュー (3D)", "Ctrl+1", Toggle3D, null, () => show3D),
                    Item("2Dプレビュー", "Ctrl+2", Toggle2D, null, () => show2D),
                    Item("レーザー2Dビュー", "Ctrl+3", ToggleLaserView, null, () => showLaserView),
                    Item("譜面情報", "Ctrl+4", () => infoDialog.Open = !infoDialog.Open, null, () => infoDialog.Open),
                    Separator,
                    Item("レイアウトを初期化", string.Empty, ResetLayout)
                }
            },
            new MenuDef
            {
                Title = "ヘルプ",
                Entries = new[] { Item("操作一覧…", "F1", () => helpDialog.Open = true) }
            }
        };

        LayoutMenuTitles();
    }

    private void LayoutMenuTitles()
    {
        GUIStyle style = MenuTitleStyle;
        float x = 6f;
        foreach (MenuDef menu in menus)
        {
            float width = style.CalcSize(new GUIContent(menu.Title)).x + 18f;
            menu.TitleRect = new Rect(x, 2f, width, MenuBarHeight - 4f);
            x += width;
        }

        GUIStyle nameStyle = EditorStyles.miniLabel;
        float nameWidth = Mathf.Clamp(nameStyle.CalcSize(new GUIContent(ChartName + "*  ▾")).x + 20f, 120f, 260f);
        chartNameRect = new Rect(x + 14f, 3f, nameWidth, MenuBarHeight - 6f);
    }

    private static GUIStyle menuTitleStyle;

    private static GUIStyle MenuTitleStyle => menuTitleStyle ??= new GUIStyle(GUI.skin.label)
    {
        alignment = TextAnchor.MiddleCenter,
        fontSize = 12,
        padding = new RectOffset(0, 0, 0, 0)
    };

    private Rect DropdownRect(int index)
    {
        MenuDef menu = menus[index];
        float height = 6f;
        foreach (MenuEntry entry in menu.Entries)
        {
            height += entry.IsSeparator ? MenuSeparatorHeight : MenuRowHeight;
        }

        float x = Mathf.Min(menu.TitleRect.x, Screen.width - MenuWidth - 2f);
        return new Rect(x, MenuBarHeight - 1f, MenuWidth, height);
    }

    private MenuEntry EntryAt(int index, Vector2 mouse, out Rect row)
    {
        Rect drop = DropdownRect(index);
        float y = drop.y + 3f;
        foreach (MenuEntry entry in menus[index].Entries)
        {
            float height = entry.IsSeparator ? MenuSeparatorHeight : MenuRowHeight;
            row = new Rect(drop.x + 2f, y, drop.width - 4f, height);
            if (row.Contains(mouse))
            {
                return entry;
            }

            y += height;
        }

        row = Rect.zero;
        return null;
    }

    private int MenuTitleAt(Vector2 mouse)
    {
        for (int index = 0; index < menus.Count; index++)
        {
            if (menus[index].TitleRect.Contains(mouse))
            {
                return index;
            }
        }

        return -1;
    }

    private Rect ChartListRect()
    {
        const float rowHeight = 24f;
        float height = Mathf.Min(Screen.height - MenuBarHeight - 40f, chartPaths.Length * rowHeight + 8f);
        return new Rect(chartNameRect.x, MenuBarHeight - 1f, 280f, Mathf.Max(rowHeight + 8f, height));
    }

    private bool PointerOverOverlay(Vector2 mouse)
    {
        if (openMenu >= 0 && DropdownRect(openMenu).Contains(mouse))
        {
            return true;
        }

        if (showChartList && ChartListRect().Contains(mouse))
        {
            return true;
        }

        return dialogs.Any(dialog => dialog.Open && dialog.Rect.Contains(mouse));
    }

    /// <summary>Menu bar titles, open menus and the chart list: they take their clicks before anything else.</summary>
    private void HandleOverlayInput(Event current)
    {
        Vector2 mouse = current.mousePosition;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape && (openMenu >= 0 || showChartList))
        {
            openMenu = -1;
            showChartList = false;
            current.Use();
            return;
        }

        if (openMenu >= 0 && (current.type == EventType.Repaint || current.type == EventType.MouseMove || current.type == EventType.MouseDrag))
        {
            // Moving along the bar while a menu is open opens the menu under the pointer.
            int hovered = MenuTitleAt(mouse);
            if (hovered >= 0)
            {
                openMenu = hovered;
            }
        }

        if (current.type != EventType.MouseDown)
        {
            return;
        }

        if (openMenu >= 0)
        {
            if (DropdownRect(openMenu).Contains(mouse))
            {
                MenuEntry entry = EntryAt(openMenu, mouse, out _);
                if (entry != null && !entry.IsSeparator && (entry.Enabled == null || entry.Enabled()))
                {
                    Defer(entry.Run);
                    openMenu = -1;
                }

                current.Use();
                return;
            }

            int title = MenuTitleAt(mouse);
            openMenu = title >= 0 && title != openMenu ? title : -1;
            current.Use();
            return;
        }

        int clicked = MenuTitleAt(mouse);
        if (clicked >= 0)
        {
            openMenu = clicked;
            showChartList = false;
            GUIUtility.keyboardControl = 0;
            current.Use();
            return;
        }

        if (chartNameRect.Contains(mouse))
        {
            RefreshChartList();
            showChartList = !showChartList;
            current.Use();
            return;
        }

        if (showChartList && !ChartListRect().Contains(mouse))
        {
            showChartList = false;
            current.Use();
        }
    }

    private void DrawMenuBar(Event current)
    {
        Rect bar = new Rect(0f, 0f, Screen.width, MenuBarHeight);
        EditorGUI.DrawRect(bar, BarColor);
        EditorGUI.DrawRect(new Rect(0f, bar.yMax - 1f, bar.width, 1f), BorderColor);
        Vector2 mouse = current.mousePosition;

        if (current.type == EventType.Repaint)
        {
            GUIStyle style = MenuTitleStyle;
            for (int index = 0; index < menus.Count; index++)
            {
                Rect rect = menus[index].TitleRect;
                if (index == openMenu)
                {
                    EditorGUI.DrawRect(rect, AccentColor);
                }
                else if (rect.Contains(mouse) && !pointerBlocked)
                {
                    EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.08f));
                }

                GUI.Label(rect, menus[index].Title, style);
            }

            EditorGUI.DrawRect(chartNameRect, showChartList ? AccentColor : chartNameRect.Contains(mouse) ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.05f));
            GUIStyle nameStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            nameStyle.normal.textColor = dirty ? new Color(1f, 0.85f, 0.5f) : new Color(0.9f, 0.9f, 0.9f);
            GUI.Label(new Rect(chartNameRect.x + 8f, chartNameRect.y, chartNameRect.width - 12f, chartNameRect.height), ChartName + (dirty ? "*" : string.Empty) + "  ▾", nameStyle);
            if (chartNameRect.Contains(mouse))
            {
                tooltipText = "譜面フォルダの譜面を開く";
            }
        }

        // Sidebar toggles at the right end: the game preview (3D) and the 2D preview.
        Rect toggle3D = new Rect(Screen.width - 62f, 3f, 56f, MenuBarHeight - 6f);
        Rect toggle2D = new Rect(toggle3D.x - 60f, 3f, 56f, MenuBarHeight - 6f);
        if (SidebarToggle(toggle2D, show2D, "2D", "2Dプレビューの表示 / 非表示 (Ctrl+2)", current))
        {
            Defer(Toggle2D);
        }

        if (SidebarToggle(toggle3D, show3D, "3D", "ゲームプレビューの表示 / 非表示 (Ctrl+1)", current))
        {
            Defer(Toggle3D);
        }

        // The edit view: the 2D timeline or the 3D box.
        Rect viewToggle = new Rect(toggle2D.x - 128f, 3f, 116f, MenuBarHeight - 6f);
        if (EditViewToggle(viewToggle, current))
        {
            Defer(ToggleEditView3D);
        }

        // Transport between the chart name and the toggles.
        float left = chartNameRect.xMax + 16f;
        Rect transport = new Rect(left, 2f, Mathf.Max(0f, viewToggle.x - 12f - left), MenuBarHeight - 4f);
        GUILayout.BeginArea(transport);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(new GUIContent(playback.IsPlaying ? "■ 音楽" : "▶ 音楽", "再生位置から音楽だけを再生 / 停止 (Space)"), EditorStyles.miniButton, GUILayout.Width(70f)))
        {
            Defer(TogglePlayback);
        }

        bool previewing = flow.IsEmbeddedPreview;
        string previewTip = show3D ? "再生位置からゲームプレビューで譜面を再生 / 停止 (P)" : "再生位置から全画面で譜面を再生 (P)";
        if (GUILayout.Button(new GUIContent(previewing ? "■ プレビュー" : "▶ プレビュー", previewTip), EditorStyles.miniButton, GUILayout.Width(92f)))
        {
            Defer(TogglePreview);
        }

        GUIStyle timeStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
        GUILayout.Label(FormatTime(cursorTime), timeStyle, GUILayout.Width(124f));
        GUILayout.Label("小節", EditorStyles.miniLabel, GUILayout.Width(26f));
        jumpMeasure = Mathf.Max(1, EditorGUILayout.IntField(jumpMeasure, GUILayout.Width(40f)));
        if (GUILayout.Button(new GUIContent("移動", "その小節の頭へ再生位置を移す"), EditorStyles.miniButton, GUILayout.Width(40f)))
        {
            Defer(() =>
            {
                playback.Stop();
                flow.StopEmbeddedPreview();
                cursorTime = Math.Max(0d, MeasureStart(jumpMeasure));
                FollowCursor();
            });
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    /// <summary>A toggle drawn as the sidebar icon (a frame with its right part filled) and a short label.</summary>
    private bool SidebarToggle(Rect rect, bool on, string label, string tip, Event current)
    {
        bool hovered = rect.Contains(current.mousePosition);
        if (current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, on ? new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.55f) : hovered ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.04f));
            Color ink = on ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            Rect icon = new Rect(rect.x + 7f, rect.center.y - 6f, 18f, 12f);
            DrawOutline(icon, ink, 1.5f);
            EditorGUI.DrawRect(new Rect(icon.xMax - 7f, icon.y, 7f, icon.height), on ? ink : new Color(ink.r, ink.g, ink.b, 0.35f));
            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = ink;
            GUI.Label(new Rect(icon.xMax + 5f, rect.y, rect.width - 30f, rect.height), label, style);
            if (hovered)
            {
                tooltipText = tip;
            }
        }

        if (current.type == EventType.MouseDown && current.button == 0 && hovered && !pointerBlocked)
        {
            current.Use();
            return true;
        }

        return false;
    }

    private void DrawMenuDropdown(Event current)
    {
        if (openMenu < 0 || current.type != EventType.Repaint)
        {
            return;
        }

        Rect drop = DropdownRect(openMenu);
        EditorGUI.DrawRect(new Rect(drop.x + 3f, drop.y + 3f, drop.width, drop.height), new Color(0f, 0f, 0f, 0.35f));
        EditorGUI.DrawRect(drop, new Color(0.16f, 0.16f, 0.18f));
        DrawOutline(drop, new Color(1f, 1f, 1f, 0.18f), 1f);

        GUIStyle label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
        GUIStyle shortcut = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        EntryAt(openMenu, current.mousePosition, out Rect hoveredRow);
        float y = drop.y + 3f;
        foreach (MenuEntry entry in menus[openMenu].Entries)
        {
            if (entry.IsSeparator)
            {
                EditorGUI.DrawRect(new Rect(drop.x + 8f, y + 3f, drop.width - 16f, 1f), new Color(1f, 1f, 1f, 0.12f));
                y += MenuSeparatorHeight;
                continue;
            }

            Rect row = new Rect(drop.x + 2f, y, drop.width - 4f, MenuRowHeight);
            bool enabled = entry.Enabled == null || entry.Enabled();
            if (enabled && row == hoveredRow)
            {
                EditorGUI.DrawRect(row, AccentColor);
            }

            Color ink = enabled ? new Color(0.92f, 0.92f, 0.92f) : new Color(1f, 1f, 1f, 0.3f);
            label.normal.textColor = ink;
            shortcut.normal.textColor = new Color(ink.r, ink.g, ink.b, ink.a * 0.6f);
            if (entry.Checked != null && entry.Checked())
            {
                GUI.Label(new Rect(row.x + 6f, row.y, 16f, row.height), "✓", label);
            }

            GUI.Label(new Rect(row.x + 24f, row.y, row.width - 30f, row.height), entry.Label, label);
            GUI.Label(new Rect(row.x, row.y, row.width - 10f, row.height), entry.Shortcut, shortcut);
            y += MenuRowHeight;
        }
    }

    /// <summary>The list of the chart folder's files under the chart name.</summary>
    private void DrawChartList()
    {
        if (!showChartList)
        {
            return;
        }

        const float rowHeight = 24f;
        Rect box = ChartListRect();
        EditorGUI.DrawRect(new Rect(box.x + 3f, box.y + 3f, box.width, box.height), new Color(0f, 0f, 0f, 0.35f));
        EditorGUI.DrawRect(box, new Color(0.16f, 0.16f, 0.18f));
        DrawOutline(box, new Color(1f, 1f, 1f, 0.3f), 1f);
        if (chartPaths.Length == 0)
        {
            GUI.Label(new Rect(box.x + 8f, box.y + 4f, box.width - 16f, rowHeight), "譜面フォルダに譜面がありません", EditorStyles.miniLabel);
            return;
        }

        chartListScroll = GUI.BeginScrollView(new Rect(box.x + 4f, box.y + 4f, box.width - 8f, box.height - 8f), chartListScroll, new Rect(0f, 0f, box.width - 28f, chartPaths.Length * rowHeight));
        for (int index = 0; index < chartPaths.Length; index++)
        {
            string path = chartPaths[index];
            string name = (path == chartPath ? "● " : "　") + chartNames[index];
            if (GUI.Button(new Rect(0f, index * rowHeight, box.width - 28f, rowHeight - 2f), name, EditorStyles.miniButton))
            {
                showChartList = false;
                Defer(() =>
                {
                    if (path != chartPath && ConfirmDiscard())
                    {
                        Load(path);
                    }
                });
            }
        }

        GUI.EndScrollView();
    }

    // ------------------------------------------------------------------ control bar

    /// <summary>The bar under the menus: grid and zoom, then the settings of the selection (or of the current tool).</summary>
    private void DrawControlBar()
    {
        EditorGUI.DrawRect(controlBarRect, ControlBarColor);
        EditorGUI.DrawRect(new Rect(controlBarRect.x, controlBarRect.yMax - 1f, controlBarRect.width, 1f), BorderColor);
        GUILayout.BeginArea(new Rect(controlBarRect.x + 8f, controlBarRect.y + 5f, controlBarRect.width - 16f, controlBarRect.height - 8f));
        GUILayout.BeginHorizontal();

        GUIStyle toolLabel = new GUIStyle(EditorStyles.miniBoldLabel);
        toolLabel.normal.textColor = new Color(0.6f, 0.75f, 1f);
        GUILayout.Label(ToolName(tool), toolLabel, GUILayout.Width(58f));

        GUILayout.Label(new GUIContent("グリッド", "BPMの1拍を何分割するか（[ ] で粗く/細かく）"), EditorStyles.miniLabel);
        if (GUILayout.Button("◀", EditorStyles.miniButton, GUILayout.Width(22f)))
        {
            StepDivision(-1);
        }

        GUILayout.Label(DivisionLabel(Division), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(46f));
        if (GUILayout.Button("▶", EditorStyles.miniButton, GUILayout.Width(22f)))
        {
            StepDivision(1);
        }

        gridDivisions = Mathf.Clamp(EditorGUILayout.IntField(gridDivisions, GUILayout.Width(32f)), 1, MaxDivisions);
        snapEnabled = GUILayout.Toggle(snapEnabled, new GUIContent("吸着", "ノーツ・レーザー点をグリッドに吸着させる（G で切替、Alt 押しながらで一時的に解除）"), EditorStyles.miniButton, GUILayout.Width(44f));
        GUILayout.Space(10f);
        GUILayout.Label("ズーム", EditorStyles.miniLabel);
        float zoom = GUILayout.HorizontalSlider(Mathf.Log(pixelsPerSecond), Mathf.Log(MinPixelsPerSecond), Mathf.Log(MaxPixelsPerSecond), GUILayout.Width(90f));
        SetZoom(Mathf.Exp(zoom), TimeToY(cursorTime));

        ControlSeparator();
        if (selectedNotes.Count == 1 && selectedLasers.Count == 0)
        {
            NoteControls(selectedNotes[0]);
        }
        else if (selectedNotes.Count > 0 || selectedLasers.Count > 0)
        {
            MultipleNoteControls();
        }
        else if (selectedLaser != null)
        {
            LaserControls(selectedLaser);
        }
        else
        {
            PlacementControls();
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private static void ControlSeparator()
    {
        GUILayout.Space(8f);
        Rect rect = GUILayoutUtility.GetRect(1f, 20f, GUILayout.Width(1f), GUILayout.Height(20f));
        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.15f));
        GUILayout.Space(8f);
    }

    private static void ControlLabel(string text)
    {
        GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
    }

    private void NoteControls(TapNoteData note)
    {
        ControlLabel("ノーツ");
        GUILayout.Space(6f);
        EditorGUI.BeginChangeCheck();
        ControlLabel("レーン");
        int lane = GUILayout.Toolbar(Mathf.Clamp(note.lane, 0, 3), LaneNames, EditorStyles.miniButton, GUILayout.Width(132f));
        ControlLabel("時刻");
        double time = EditorGUILayout.DoubleField(note.time, GUILayout.Width(62f));
        double duration;
        using (new EditorGUI.DisabledScope(note.IsFlickOnly))
        {
            ControlLabel("ロング");
            duration = EditorGUILayout.DoubleField(note.duration, GUILayout.Width(48f));
        }

        bool flickOnly = GUILayout.Toggle(note.flickOnly, new GUIContent("フリックのみ", "ボタンなしでフリックだけのノーツ (Shift+F)"), EditorStyles.miniButton, GUILayout.Width(80f));
        ControlLabel("スティック");
        int stick = string.IsNullOrEmpty(note.stick) ? 0 : char.ToUpperInvariant(note.stick[0]) == 'L' ? 1 : 2;
        stick = GUILayout.Toolbar(stick, StickShort, EditorStyles.miniButton, GUILayout.Width(108f));
        if (EditorGUI.EndChangeCheck())
        {
            Modify("Edit note", () =>
            {
                note.lane = Mathf.Clamp(lane, 0, 3);
                note.time = Math.Max(0d, time);
                note.duration = Math.Max(0d, duration);
                note.flickOnly = flickOnly;
                note.stick = stick == 1 ? "L" : stick == 2 ? "R" : string.Empty;
            });
        }

        ControlLabel("方向");
        NoteDirection direction = DirectionRow(note.direction, RhythmStageView.GetStickColor(note.GetStickSide(note.lane)));
        if (direction != note.direction)
        {
            ApplyDirection(direction);
        }

        ControlLabel("回転");
        SpinToolbar(note.IsSpin ? note.spin : 0);

        if (note.flickOnly && !note.HasDirection)
        {
            GUILayout.Label(new GUIContent("⚠ 方向なし", "方向がないので、フリックのみは無視されてバーになります。"), EditorStyles.miniLabel);
        }

        GUILayout.Space(6f);
        MirrorButtons();
        if (GUILayout.Button(new GUIContent("削除", "Delete"), EditorStyles.miniButton, GUILayout.Width(44f)))
        {
            Defer(DeleteSelection);
        }
    }

    /// <summary>The three mirror buttons of the control bar (selection: notes and / or lasers).</summary>
    private void MirrorButtons()
    {
        ControlLabel("ミラー");
        if (GUILayout.Button(new GUIContent("左右", "L↔R, ZL↔ZR, 左レーザー↔右レーザー（X反転）(M)"), EditorStyles.miniButton, GUILayout.Width(36f)))
        {
            Defer(() => MirrorSelection(true, false));
        }

        if (GUILayout.Button(new GUIContent("上下", "L↔ZL, R↔ZR, レーザーのY反転 (Shift+M)"), EditorStyles.miniButton, GUILayout.Width(36f)))
        {
            Defer(() => MirrorSelection(false, true));
        }

        if (GUILayout.Button(new GUIContent("上下左右", "L↔ZR, R↔ZL, 左レーザー↔右レーザー（X・Y反転）(Ctrl+M)"), EditorStyles.miniButton, GUILayout.Width(58f)))
        {
            Defer(() => MirrorSelection(true, true));
        }

        GUILayout.Space(6f);
    }

    private static readonly string[] SpinNames = { "反時計回り", "時計回り" };

    /// <summary>The 反時計回り / 時計回り toolbar; <paramref name="current"/> is 1 / 2, or 0 for none shown. A click applies it.</summary>
    private void SpinToolbar(int current)
    {
        EditorGUI.BeginChangeCheck();
        int choice = GUILayout.Toolbar(current - 1, SpinNames, EditorStyles.miniButton, GUILayout.Width(132f)) + 1;
        if (EditorGUI.EndChangeCheck() && choice >= 1 && choice <= 2)
        {
            Defer(() => ApplySpin(choice));
        }
    }

    private void MultipleNoteControls()
    {
        ControlLabel(selectedLasers.Count == 0 ? $"ノーツ {selectedNotes.Count} 個"
            : selectedNotes.Count == 0 ? $"レーザー {selectedLasers.Count} 本"
            : $"ノーツ {selectedNotes.Count} ・レーザー {selectedLasers.Count}");
        GUILayout.Space(6f);
        if (selectedNotes.Count > 0)
        {
            ControlLabel("方向");
            NoteDirection common = selectedNotes.All(note => note.direction == selectedNotes[0].direction) ? selectedNotes[0].direction : (NoteDirection)(-1);
            NoteDirection direction = DirectionRow(common, new Color(0.85f, 0.85f, 0.85f));
            if (direction != common)
            {
                ApplyDirection(direction);
            }

            GUILayout.Space(6f);
        }

        MirrorButtons();
        if (GUILayout.Button(new GUIContent("前後反転", "選択範囲の時間を逆にする"), EditorStyles.miniButton, GUILayout.Width(58f)))
        {
            Defer(ReverseSelectionInTime);
        }

        if (selectedNotes.Count > 0 && GUILayout.Button(new GUIContent("フリック切替", "Shift+F"), EditorStyles.miniButton, GUILayout.Width(80f)))
        {
            ToggleFlickOnly();
        }

        if (GUILayout.Button(new GUIContent("削除", "Delete"), EditorStyles.miniButton, GUILayout.Width(44f)))
        {
            Defer(DeleteSelection);
        }
    }

    private void LaserControls(LaserNoteData laser)
    {
        ControlLabel("レーザー");
        EditorGUI.BeginChangeCheck();
        int side = GUILayout.Toolbar(Mathf.Clamp(laser.side, 0, 1), new[] { "左", "右" }, EditorStyles.miniButton, GUILayout.Width(64f));
        if (EditorGUI.EndChangeCheck())
        {
            Modify("Laser side", () => laser.side = side);
        }

        GUILayout.Space(6f);
        int count = laser.points.Length;
        if (GUILayout.Button("◀", EditorStyles.miniButton, GUILayout.Width(22f)))
        {
            Defer(() => StepLaserPoint(laser, -1));
        }

        bool valid = selectedPoint >= 0 && selectedPoint < count;
        GUILayout.Label(valid ? $"点 {selectedPoint + 1}/{count}" : $"点 -/{count}", new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(56f));
        if (GUILayout.Button("▶", EditorStyles.miniButton, GUILayout.Width(22f)))
        {
            Defer(() => StepLaserPoint(laser, 1));
        }

        if (valid)
        {
            LaserPointData point = laser.points[selectedPoint];
            EditorGUI.BeginChangeCheck();
            ControlLabel("時刻");
            double time = EditorGUILayout.DoubleField(point.time, GUILayout.Width(62f));
            ControlLabel("X");
            float x = EditorGUILayout.FloatField(point.x, GUILayout.Width(44f));
            ControlLabel("Y");
            float y = EditorGUILayout.FloatField(point.y, GUILayout.Width(44f));
            if (EditorGUI.EndChangeCheck())
            {
                Modify("Edit laser point", () =>
                {
                    point.time = Math.Max(0d, time);
                    point.x = Mathf.Clamp(x, -1f, 1f);
                    point.y = Mathf.Clamp(y, -1f, 1f);
                });
            }

            using (new EditorGUI.DisabledScope(count <= 2))
            {
                if (GUILayout.Button(new GUIContent("点を削除", "Delete（2点しかないレーザーは消せない）"), EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    Defer(DeleteSelection);
                }
            }
        }

        GUILayout.Space(6f);
        LaserGridControls();
        GUILayout.Space(6f);
        MirrorButtons();
        if (GUILayout.Button("レーザーを削除", EditorStyles.miniButton, GUILayout.Width(90f)))
        {
            Defer(() =>
            {
                selectedPoint = -1;
                DeleteSelection();
            });
        }
    }

    private void StepLaserPoint(LaserNoteData laser, int step)
    {
        if (laser != selectedLaser || laser.points.Length == 0)
        {
            return;
        }

        selectedPoint = selectedPoint < 0 ? 0 : Mathf.Clamp(selectedPoint + step, 0, laser.points.Length - 1);
        cursorTime = laser.points[selectedPoint].time;
        FollowCursor();
    }

    private void PlacementControls()
    {
        switch (tool)
        {
            case Tool.Tap:
            case Tool.Hold:
            case Tool.Flick:
                ControlLabel("方向");
                Color stickColor = placeStick == 0 ? new Color(0.85f, 0.85f, 0.85f) : RhythmStageView.GetStickColor(placeStick - 1);
                placeDirection = DirectionRow(placeDirection, stickColor);
                GUILayout.Space(6f);
                ControlLabel("スティック");
                placeStick = GUILayout.Toolbar(placeStick, StickShort, EditorStyles.miniButton, GUILayout.Width(108f));
                break;
            case Tool.Spin:
                ControlLabel("回転");
                SpinToolbar(placeSpin);
                GUILayout.Space(6f);
                ControlLabel("スティック");
                placeStick = GUILayout.Toolbar(placeStick, StickShort, EditorStyles.miniButton, GUILayout.Width(108f));
                break;
            case Tool.Laser:
                LaserGridControls();
                if (editView3D)
                {
                    GUILayout.Space(6f);
                    ControlLabel("新しいレーザー");
                    placeLaserSide = GUILayout.Toolbar(placeLaserSide, new[] { "左", "右" }, EditorStyles.miniButton, GUILayout.Width(64f));
                }

                break;
        }

        GUILayout.Space(8f);
        GUIStyle hint = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
        hint.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
        GUILayout.Label(LaneHint(), hint, GUILayout.MinWidth(40f));
    }

    /// <summary>A row of the nine directions (none first); returns the clicked one.</summary>
    private static NoteDirection DirectionRow(NoteDirection current, Color color)
    {
        NoteDirection result = current;
        foreach (NoteDirection direction in DirectionRowLayout)
        {
            Rect rect = GUILayoutUtility.GetRect(22f, 20f, GUILayout.Width(22f), GUILayout.Height(20f));
            bool on = current == direction;
            if (GUI.Toggle(rect, on, GUIContent.none, EditorStyles.miniButton) && !on)
            {
                result = direction;
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawArrow(rect.center, direction, 6f, on ? color : new Color(0.6f, 0.6f, 0.6f));
            }
        }

        return result;
    }

    // ------------------------------------------------------------------ tool strip

    private void DrawToolStrip(Event current)
    {
        EditorGUI.DrawRect(toolStripRect, BarColor);
        EditorGUI.DrawRect(new Rect(toolStripRect.xMax - 1f, toolStripRect.y, 1f, toolStripRect.height), BorderColor);
        GUIStyle keyStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.LowerRight, fontSize = 9, padding = new RectOffset(0, 2, 0, 0) };
        keyStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);

        for (int index = 0; index < Tools.Length; index++)
        {
            ToolInfo info = Tools[index];
            Rect rect = new Rect(toolStripRect.x + 4f, toolStripRect.y + 8f + index * (ToolButtonSize + 4f), ToolButtonSize, ToolButtonSize);
            if (index == 2 || index == 5 || index == 6)
            {
                // Groups: selecting | notes | lasers | eraser.
                rect.y += 6f * (index == 2 ? 1 : index == 5 ? 2 : 3);
            }

            bool hovered = rect.Contains(current.mousePosition) && !pointerBlocked;
            if (current.type == EventType.Repaint)
            {
                if (index == 2 || index == 5 || index == 6)
                {
                    EditorGUI.DrawRect(new Rect(rect.x + 4f, rect.y - 5f, rect.width - 8f, 1f), new Color(1f, 1f, 1f, 0.12f));
                }

                bool on = tool == info.Tool;
                EditorGUI.DrawRect(rect, on ? AccentColor : hovered ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.03f));
                DrawToolIcon(info.Tool, rect);
                GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height - 1f), info.Key.ToString(), keyStyle);
                if (hovered)
                {
                    tooltipText = $"{info.Name} ({info.Key}) — {info.Hint}";
                }
            }
            else if (current.type == EventType.MouseDown && current.button == 0 && hovered)
            {
                Tool chosen = info.Tool;
                Defer(() => SetTool(chosen));
                current.Use();
            }
        }
    }

    private void DrawToolIcon(Tool value, Rect rect)
    {
        Vector2 c = rect.center;
        Color ink = new Color(0.92f, 0.92f, 0.92f);
        switch (value)
        {
            case Tool.Select:
                Handles.color = ink;
                Handles.DrawAAConvexPolygon(c + new Vector2(-6f, -10f), c + new Vector2(-6f, 6f), c + new Vector2(5f, 2f));
                Handles.DrawSegment(c + new Vector2(-1f, 2f), c + new Vector2(3f, 9f), ink, 3f);
                break;
            case Tool.Box:
                Rect box = new Rect(c.x - 10f, c.y - 8f, 20f, 16f);
                for (float x = box.x; x < box.xMax; x += 5f)
                {
                    EditorGUI.DrawRect(new Rect(x, box.y, 3f, 1.5f), ink);
                    EditorGUI.DrawRect(new Rect(x, box.yMax - 1.5f, 3f, 1.5f), ink);
                }

                for (float y = box.y; y < box.yMax; y += 5f)
                {
                    EditorGUI.DrawRect(new Rect(box.x, y, 1.5f, 3f), ink);
                    EditorGUI.DrawRect(new Rect(box.xMax - 1.5f, y, 1.5f, 3f), ink);
                }

                break;
            case Tool.Tap:
                EditorGUI.DrawRect(new Rect(c.x - 11f, c.y - 3f, 22f, 6f), TapColor);
                break;
            case Tool.Hold:
                EditorGUI.DrawRect(new Rect(c.x - 3f, c.y - 9f, 6f, 16f), new Color(HoldColor.r, HoldColor.g, HoldColor.b, 0.45f));
                EditorGUI.DrawRect(new Rect(c.x - 11f, c.y + 4f, 22f, 5f), HoldColor);
                EditorGUI.DrawRect(new Rect(c.x - 8f, c.y - 10f, 16f, 2f), HoldColor);
                break;
            case Tool.Flick:
                {
                    Color stick = RhythmStageView.GetStickColor(0);
                    Rect frame = new Rect(c.x - 10f, c.y - 8f, 20f, 16f);
                    EditorGUI.DrawRect(frame, new Color(0.06f, 0.06f, 0.08f));
                    DrawOutline(frame, stick, 1f);
                    DrawArrow(c, NoteDirection.UpRight, 6f, stick);
                    break;
                }

            case Tool.Spin:
                {
                    Color stick = RhythmStageView.GetStickColor(0);
                    Rect frame = new Rect(c.x - 10f, c.y - 8f, 20f, 16f);
                    EditorGUI.DrawRect(frame, new Color(0.06f, 0.06f, 0.08f));
                    DrawOutline(frame, stick, 1f);
                    DrawSpinIcon(c, TapNoteData.SpinCounterClockwise, 6f, stick);
                    break;
                }

            case Tool.Laser:
                Handles.color = RhythmStageView.GetStickColor(0);
                Handles.DrawAAPolyLine(2.5f, c + new Vector2(-10f, 8f), c + new Vector2(-4f, -6f), c + new Vector2(3f, 5f), c + new Vector2(10f, -8f));
                break;
            case Tool.Erase:
                {
                    Vector2 Corner(float x, float y)
                    {
                        const float cos = 0.7071f;
                        return c + new Vector2((x - y) * cos, (x + y) * -cos) * 1f + new Vector2(0f, -1f);
                    }

                    Handles.color = ink;
                    Handles.DrawAAConvexPolygon(Corner(-10f, -4f), Corner(2f, -4f), Corner(2f, 4f), Corner(-10f, 4f));
                    Handles.color = new Color(1f, 0.45f, 0.55f);
                    Handles.DrawAAConvexPolygon(Corner(2f, -4f), Corner(9f, -4f), Corner(9f, 4f), Corner(2f, 4f));
                    EditorGUI.DrawRect(new Rect(c.x - 9f, c.y + 10f, 18f, 1f), new Color(1f, 1f, 1f, 0.5f));
                    break;
                }
        }
    }

    // ------------------------------------------------------------------ timeline + laser views

    private void DrawTimelineArea(Event current)
    {
        GUI.BeginClip(timelineRect);
        timelineHeight = timelineRect.height;
        LayoutColumns(timelineRect.width);
        LayoutLaserViews();
        if (editView3D)
        {
            followOnLayout = false;
            Draw3DEditView(current, timelineRect.size);
            GUI.EndClip();
            return;
        }

        if (followOnLayout)
        {
            // A chart was opened (or the view went to the start): the playhead goes above the laser views.
            followOnLayout = false;
            FollowCursor();
        }
        HandleTimelineInput(current, timelineRect.size);
        if (current.type == EventType.Repaint)
        {
            DrawTimeline(timelineRect.size);
        }

        if (showLaserView)
        {
            DrawLaserView(laserViewLeft, 0, current);
            DrawLaserView(laserViewRight, 1, current);
        }

        GUI.EndClip();
    }

    /// <summary>The laser 2D views sit at the bottom of the laser columns, one per stick, as wide as the stick's two columns.</summary>
    private void LayoutLaserViews()
    {
        Rect LaserSpan(int side)
        {
            Rect x = columns.First(column => column.Kind == ColumnKind.LaserX && column.Index == side).Rect;
            Rect y = columns.First(column => column.Kind == ColumnKind.LaserY && column.Index == side).Rect;
            return new Rect(x.x, 0f, y.xMax - x.x, 0f);
        }

        Rect left = LaserSpan(0);
        Rect right = LaserSpan(1);
        float available = timelineHeight - HeaderHeight;
        laserViewHeight = Mathf.Clamp(left.width + LaserViewHeader + 6f, Mathf.Min(140f, available * 0.5f), Mathf.Max(80f, available * 0.45f));
        float top = timelineHeight - laserViewHeight;
        laserViewLeft = new Rect(left.x, top, left.width, laserViewHeight);
        laserViewRight = new Rect(right.x, top, right.width, laserViewHeight);
    }

    /// <summary>
    /// One stick's laser seen as the stick sees it, right under its columns: the selected laser's whole
    /// path with numbered points (drag them), the lasers of the next measure fading out, and where the
    /// stick is at the playhead. With the laser tool a click puts a point at the playhead.
    /// </summary>
    private void DrawLaserView(Rect view, int side, Event current)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Color color = RhythmStageView.GetStickColor(side);
        float size = Mathf.Max(20f, Mathf.Min(view.width - 10f, view.height - LaserViewHeader - 8f));
        Rect pad = new Rect(view.center.x - size * 0.5f, view.y + LaserViewHeader + (view.height - LaserViewHeader - size) * 0.5f, size, size);
        Vector2 center = pad.center;
        float radius = size * 0.5f - 6f;
        double window = BeatSeconds * 4d;
        LaserNoteData selected = selectedLaser != null && selectedLaser.side == side ? selectedLaser : null;

        Vector2 ToPad(Vector2 value)
        {
            value = StickInput.ClampToSquare(value);
            return center + new Vector2(value.x, -value.y) * radius;
        }

        bool Visible(LaserNoteData laser)
        {
            return laser.side == side && laser.points.Length >= 2 && LaserEnd(laser) >= cursorTime && LaserStart(laser) <= cursorTime + window;
        }

        switch (current.GetTypeForControl(controlId))
        {
            case EventType.Repaint:
                {
                    EditorGUI.DrawRect(view, new Color(0.07f, 0.07f, 0.09f));
                    EditorGUI.DrawRect(new Rect(view.x, view.y, view.width, 2f), new Color(color.r, color.g, color.b, 0.7f));
                    GUIStyle title = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                    title.normal.textColor = color;
                    GUI.Label(new Rect(view.x + 4f, view.y + 2f, view.width - 8f, LaserViewHeader - 2f), side == 0 ? "左レーザー" : "右レーザー", title);
                    if (selected != null)
                    {
                        GUIStyle info = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                        info.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
                        string pointText = selectedPoint >= 0 && selectedPoint < selected.points.Length ? $"点 {selectedPoint + 1}/{selected.points.Length}" : $"{selected.points.Length} 点";
                        GUI.Label(new Rect(view.x, view.y + 2f, view.width - 4f, LaserViewHeader - 2f), pointText, info);
                    }

                    EditorGUI.DrawRect(pad, new Color(0.1f, 0.1f, 0.12f));
                    Handles.SetClip(view);
                    Handles.color = new Color(1f, 1f, 1f, 0.22f);
                    Handles.DrawWireDisc(center, Vector3.forward, radius);
                    Handles.DrawLine(center + Vector2.left * radius, center + Vector2.right * radius);
                    Handles.DrawLine(center + Vector2.up * radius, center + Vector2.down * radius);
                    DrawPadSquare(center, radius);

                    // The lasers of the coming measure, fading toward its end.
                    foreach (LaserNoteData laser in Chart.lasers)
                    {
                        if (laser == selected || !Visible(laser))
                        {
                            continue;
                        }

                        double from = Math.Max(cursorTime, LaserStart(laser));
                        double to = Math.Min(cursorTime + window, LaserEnd(laser));
                        const int steps = 24;
                        Vector2 previous = ToPad(Evaluate(laser, from));
                        for (int step = 1; step <= steps; step++)
                        {
                            Vector2 next = ToPad(Evaluate(laser, from + (to - from) * step / steps));
                            Color fade = color;
                            fade.a = 0.6f * (1f - step / (float)steps * 0.7f);
                            Handles.color = fade;
                            Handles.DrawAAPolyLine(2.5f, previous, next);
                            previous = next;
                        }
                    }

                    // The selected laser: its whole path with numbered points.
                    if (selected != null)
                    {
                        Vector3[] line = selected.points.Select(point => (Vector3)ToPad(new Vector2(point.x, point.y))).ToArray();
                        if (line.Length >= 2)
                        {
                            Handles.color = color;
                            Handles.DrawAAPolyLine(3f, line);
                        }

                        GUIStyle number = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
                        number.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                        for (int index = 0; index < line.Length; index++)
                        {
                            bool pointSelected = index == selectedPoint;
                            float half = pointSelected ? 5f : 3.5f;
                            Rect dot = new Rect(line[index].x - half, line[index].y - half, half * 2f, half * 2f);
                            if (!view.Contains(dot.center))
                            {
                                continue;
                            }

                            EditorGUI.DrawRect(dot, pointSelected ? Color.white : color);
                            if (pointSelected)
                            {
                                DrawOutline(new Rect(dot.x - 2f, dot.y - 2f, dot.width + 4f, dot.height + 4f), SelectColor, 2f);
                            }

                            GUI.Label(new Rect(line[index].x + 5f, line[index].y - 15f, 24f, 14f), (index + 1).ToString(), number);
                        }
                    }

                    // Where the stick is at the playhead.
                    foreach (LaserNoteData laser in Chart.lasers)
                    {
                        if (laser.side != side || laser.points.Length < 2 || LaserStart(laser) > cursorTime || LaserEnd(laser) < cursorTime)
                        {
                            continue;
                        }

                        Vector2 head = ToPad(Evaluate(laser, cursorTime));
                        DrawOutline(new Rect(head.x - 7f, head.y - 7f, 14f, 14f), Color.white, 2f);
                        EditorGUI.DrawRect(new Rect(head.x - 3f, head.y - 3f, 6f, 6f), Color.white);
                    }

                    Handles.ClearClip();
                    break;
                }

            case EventType.MouseDown:
                {
                    Vector2 mouse = current.mousePosition;
                    if (pointerBlocked || !view.Contains(mouse))
                    {
                        break;
                    }

                    GUIUtility.keyboardControl = 0;
                    LaserNoteData hitLaser = null;
                    int hitPoint = -1;
                    float best = 10f;
                    IEnumerable<LaserNoteData> candidates = selected != null
                        ? new[] { selected }.Concat(Chart.lasers.Where(laser => laser != selected && Visible(laser)))
                        : Chart.lasers.Where(Visible);
                    foreach (LaserNoteData laser in candidates)
                    {
                        for (int index = 0; index < laser.points.Length; index++)
                        {
                            LaserPointData point = laser.points[index];
                            if (laser != selected && (point.time < cursorTime || point.time > cursorTime + window))
                            {
                                continue;
                            }

                            float distance = Vector2.Distance(mouse, ToPad(new Vector2(point.x, point.y)));
                            if (distance < best)
                            {
                                best = distance;
                                hitLaser = laser;
                                hitPoint = index;
                            }
                        }

                        if (hitLaser != null)
                        {
                            break;
                        }
                    }

                    if (current.button == 1 || current.button == 0 && tool == Tool.Erase)
                    {
                        if (hitLaser != null)
                        {
                            LaserNoteData laser = hitLaser;
                            int point = hitPoint;
                            if (laser.points.Length > 2)
                            {
                                Modify("Delete laser point", () => laser.points = laser.points.Where((_, index) => index != point).ToArray());
                                SelectLaser(laser, -1);
                            }
                            else
                            {
                                Modify("Delete laser", () => Chart.lasers = Chart.lasers.Where(item => item != laser).ToArray());
                                ClearSelection();
                            }
                        }

                        current.Use();
                        break;
                    }

                    if (current.button != 0)
                    {
                        break;
                    }

                    if (hitLaser != null)
                    {
                        SelectLaser(hitLaser, hitPoint);
                        BeginDrag(DragMode.PadPoint, "Move laser point");
                        GUIUtility.hotControl = controlId;
                    }
                    else if (tool == Tool.Laser)
                    {
                        PlaceLaserPointAtCursor(side, selected, mouse, center, radius);
                        GUIUtility.hotControl = controlId;
                    }
                    else
                    {
                        Notify("レーザーツール (L) でクリックすると再生位置に点を置けます");
                    }

                    current.Use();
                    break;
                }

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != controlId)
                {
                    break;
                }

                if (selectedLaser != null && drag == DragMode.PadPoint)
                {
                    MovePadPoint(selectedLaser, current.mousePosition, center, radius);
                }

                current.Use();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != controlId)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                EndDrag();
                current.Use();
                break;
        }
    }

    /// <summary>Laser tool on a laser view: a point of the selected laser at the playhead (a new laser when none is selected).</summary>
    private void PlaceLaserPointAtCursor(int side, LaserNoteData selected, Vector2 mouse, Vector2 center, float radius)
    {
        Vector2 value = (mouse - center) / radius;
        value.y = -value.y;
        PlaceLaserPointAtCursor(side, selected, value);
    }

    /// <summary>A point of <paramref name="selected"/> at the playhead (a new laser of <paramref name="side"/> when none is given or Shift is held).</summary>
    private void PlaceLaserPointAtCursor(int side, LaserNoteData selected, Vector2 value)
    {
        value = StickInput.ClampToSquare(value);
        float x = SnapLaserValue(value.x);
        float y = SnapLaserValue(value.y);
        double time = Snap(cursorTime);
        BeginDrag(DragMode.PadPoint, "Add laser point");

        if (selected != null && !Event.current.shift)
        {
            LaserNoteData laser = selected;
            int same = Array.FindIndex(laser.points, point => Math.Abs(point.time - time) < 0.0005d);
            if (same >= 0)
            {
                LaserPointData point = laser.points[same];
                DragModify("Move laser point", () =>
                {
                    point.x = x;
                    point.y = y;
                });
                SelectLaser(laser, same);
                return;
            }

            LaserPointData added = new LaserPointData { time = time, x = x, y = y };
            DragModify("Add laser point", () => laser.points = laser.points.Append(added).OrderBy(point => point.time).ToArray());
            SelectLaser(laser, Array.IndexOf(laser.points, added));
            return;
        }

        LaserNoteData created = new LaserNoteData
        {
            side = side,
            points = new[]
            {
                new LaserPointData { time = time, x = x, y = y },
                new LaserPointData { time = time + BeatSeconds, x = x, y = y }
            }
        };

        DragModify("Add laser", () => Chart.lasers = Chart.lasers.Append(created).ToArray());
        SelectLaser(created, 0);
    }

    // ------------------------------------------------------------------ sidebars

    [NonSerialized] private readonly int[] splitterIds = new int[2];

    /// <summary>Input of the sidebars' draggable left edges; it runs before the timeline so the edge is not a click on it.</summary>
    private void HandleSplitters(Event current)
    {
        splitterIds[0] = Splitter(show2D ? sidebar2DRect : Rect.zero, true, current);
        splitterIds[1] = Splitter(show3D ? sidebar3DRect : Rect.zero, false, current);
    }

    /// <summary>The edges are drawn after the sidebars, over their backgrounds.</summary>
    private void DrawSplitters(Event current)
    {
        if (current.type != EventType.Repaint)
        {
            return;
        }

        for (int index = 0; index < 2; index++)
        {
            Rect sidebar = index == 0 ? (show2D ? sidebar2DRect : Rect.zero) : (show3D ? sidebar3DRect : Rect.zero);
            if (sidebar.width <= 0f)
            {
                continue;
            }

            Rect handle = new Rect(sidebar.x - 3f, sidebar.y, 7f, sidebar.height);
            bool active = GUIUtility.hotControl == splitterIds[index] || handle.Contains(current.mousePosition) && GUIUtility.hotControl == 0 && openMenu < 0;
            EditorGUI.DrawRect(new Rect(sidebar.x - (active ? 1f : 0f), sidebar.y, active ? 3f : 1f, sidebar.height), active ? AccentColor : new Color(1f, 1f, 1f, 0.16f));
            if (active)
            {
                tooltipText = "ドラッグで幅を変える";
            }
        }
    }

    /// <summary>The draggable left edge of a sidebar; returns its control id.</summary>
    private int Splitter(Rect sidebar, bool is2D, Event current)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (sidebar.width <= 0f)
        {
            return controlId;
        }

        Rect handle = new Rect(sidebar.x - 3f, sidebar.y, 7f, sidebar.height);
        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (current.button == 0 && handle.Contains(current.mousePosition) && !pointerBlocked)
                {
                    GUIUtility.hotControl = controlId;
                    current.Use();
                }

                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId)
                {
                    float width = Mathf.Max(MinSidebarWidth, sidebar.xMax - current.mousePosition.x);
                    if (is2D)
                    {
                        width2D = width;
                    }
                    else
                    {
                        width3D = width;
                    }

                    current.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    SaveLayout();
                    current.Use();
                }

                break;
        }

        return controlId;
    }

    /// <summary>Title strip of a sidebar with a close button; returns true when the close button was clicked.</summary>
    private bool SidebarHeader(Rect sidebar, string title, string detail)
    {
        Rect header = new Rect(sidebar.x, sidebar.y, sidebar.width, SidebarHeaderHeight);
        EditorGUI.DrawRect(header, ControlBarColor);
        EditorGUI.DrawRect(new Rect(header.x, header.yMax - 1f, header.width, 1f), BorderColor);
        GUI.Label(new Rect(header.x + 8f, header.y + 3f, header.width - 40f, header.height - 4f), title, EditorStyles.miniBoldLabel);
        if (!string.IsNullOrEmpty(detail))
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(header.x, header.y + 2f, header.width - 32f, header.height - 4f), detail, style);
        }

        return GUI.Button(new Rect(header.xMax - 26f, header.y + 3f, 22f, header.height - 6f), new GUIContent("×", "閉じる"), EditorStyles.miniButton);
    }

    private void Draw3DHeader(Rect sidebar)
    {
        Rect header = new Rect(sidebar.x, sidebar.y, sidebar.width, SidebarHeaderHeight);
        if (SidebarHeader(sidebar, "ゲームプレビュー", string.Empty))
        {
            Defer(Toggle3D);
        }

        bool previewing = flow.IsEmbeddedPreview;
        Rect button = new Rect(header.xMax - 130f, header.y + 3f, 100f, header.height - 6f);
        if (GUI.Button(button, new GUIContent(previewing ? "■ 停止 (P)" : "▶ プレビュー (P)", "再生位置からゲーム画面で譜面を再生 / 停止"), EditorStyles.miniButton))
        {
            Defer(TogglePreview);
        }
    }

    /// <summary>
    /// The chart as the player sees it, in 2D: notes fall to the judgement line and the sticks show
    /// the lasers. It follows the playhead (the cursor while stopped).
    /// </summary>
    private void Draw2DSidebar(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.07f, 0.07f, 0.09f));
        if (SidebarHeader(rect, "2Dプレビュー", FormatTime(cursorTime)))
        {
            Defer(Toggle2D);
        }

        const float controlsHeight = 28f;
        float padSize = Mathf.Clamp((rect.width - 36f) / 2f, 50f, 150f);
        float contentTop = rect.y + SidebarHeaderHeight + 6f;
        float padTop = rect.yMax - controlsHeight - padSize - 6f;
        Rect lanes = new Rect(rect.x + 10f, contentTop, rect.width - 20f, Mathf.Max(40f, padTop - contentTop - 8f));
        float laneWidth = lanes.width / 4f;
        float judgeY = lanes.yMax - 26f;
        float span = judgeY - lanes.y;
        double lookAhead = Math.Max(0.2d, previewLookAhead);

        if (Event.current.type == EventType.Repaint)
        {
            GUIStyle laneLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            for (int slot = 0; slot < 4; slot++)
            {
                Rect column = new Rect(lanes.x + slot * laneWidth, lanes.y, laneWidth, lanes.height);
                EditorGUI.DrawRect(column, slot % 2 == 0 ? new Color(0.13f, 0.13f, 0.16f) : new Color(0.11f, 0.11f, 0.14f));
                GUI.Label(new Rect(column.x, lanes.yMax - 20f, column.width, 18f), LaneNames[LaneOrder[slot]], laneLabel);
            }

            // Beat lines
            double beat = BeatSeconds;
            long firstBeat = (long)Math.Ceiling((cursorTime - Chart.offset) / beat);
            for (long index = firstBeat; ; index++)
            {
                double time = Chart.offset + index * beat;
                if (time > cursorTime + lookAhead)
                {
                    break;
                }

                float y = judgeY - (float)((time - cursorTime) / lookAhead) * span;
                EditorGUI.DrawRect(new Rect(lanes.x, y, lanes.width, 1f), new Color(1f, 1f, 1f, index % 4 == 0 ? 0.2f : 0.07f));
            }

            // Hit flashes on lanes whose note has just been passed
            if (playback.IsPlaying || flow.IsEmbeddedPreview)
            {
                foreach (TapNoteData note in Chart.notes)
                {
                    double age = cursorTime - note.time;
                    if (age >= 0d && age < 0.15d && !note.IsFlickOnly)
                    {
                        int slot = SlotOfLane(note.lane);
                        EditorGUI.DrawRect(new Rect(lanes.x + slot * laneWidth, judgeY - 40f, laneWidth, 40f), new Color(1f, 1f, 1f, 0.35f * (1f - (float)(age / 0.15d))));
                    }
                }
            }

            foreach (TapNoteData note in Chart.notes)
            {
                if (note.EndTime < cursorTime - 0.05d || note.time > cursorTime + lookAhead)
                {
                    continue;
                }

                DrawPreviewNote(note, lanes, laneWidth, judgeY, span, lookAhead);
            }

            EditorGUI.DrawRect(new Rect(lanes.x, judgeY - 1f, lanes.width, 3f), new Color(1f, 1f, 1f, 0.9f));
        }

        float padsWidth = padSize * 2f + 12f;
        for (int side = 0; side < 2; side++)
        {
            DrawPreviewStick(new Rect(rect.center.x - padsWidth * 0.5f + side * (padSize + 12f), padTop, padSize, padSize), side, lookAhead);
        }

        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.yMax - controlsHeight, rect.width - 16f, controlsHeight - 4f));
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent("速度", "2Dプレビューのノーツの速さ"), EditorStyles.miniLabel, GUILayout.Width(28f));
        // Right = faster = a shorter look-ahead.
        previewLookAhead = 2.7f - GUILayout.HorizontalSlider(2.7f - previewLookAhead, 0.2f, 2.5f);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ------------------------------------------------------------------ dialogs

    private void DrawDialogs(Event current)
    {
        // The topmost (last) open dialog under the pointer takes a click; the others are disabled for it.
        int top = -1;
        if (current.type == EventType.MouseDown || current.type == EventType.ScrollWheel)
        {
            for (int index = dialogs.Count - 1; index >= 0; index--)
            {
                if (dialogs[index].Open && dialogs[index].Rect.Contains(current.mousePosition))
                {
                    top = index;
                    break;
                }
            }
        }

        for (int index = 0; index < dialogs.Count; index++)
        {
            Dialog dialog = dialogs[index];
            if (!dialog.Open)
            {
                continue;
            }

            bool enabled = GUI.enabled;
            GUI.enabled = top < 0 || top == index;
            DrawDialog(dialog, current);
            GUI.enabled = enabled;
        }
    }

    private void DrawDialog(Dialog dialog, Event current)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Rect rect = dialog.Rect;
        rect.width = Mathf.Min(rect.width, Screen.width - 8f);
        rect.height = Mathf.Min(rect.height, Screen.height - MenuBarHeight - 8f);
        rect.x = Mathf.Clamp(rect.x, 0f, Screen.width - rect.width);
        rect.y = Mathf.Clamp(rect.y, MenuBarHeight, Screen.height - rect.height);
        dialog.Rect = rect;

        Rect title = new Rect(rect.x, rect.y, rect.width, 26f);
        Rect close = new Rect(title.xMax - 26f, title.y + 3f, 22f, 20f);
        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (current.button == 0 && GUI.enabled && title.Contains(current.mousePosition) && !close.Contains(current.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    dialogDragOffset = current.mousePosition - rect.position;
                    current.Use();
                }

                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId)
                {
                    dialog.Rect.position = current.mousePosition - dialogDragOffset;
                    current.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    current.Use();
                }

                break;
        }

        EditorGUI.DrawRect(new Rect(rect.x + 4f, rect.y + 4f, rect.width, rect.height), new Color(0f, 0f, 0f, 0.35f));
        EditorGUI.DrawRect(rect, new Color(0.17f, 0.17f, 0.19f));
        EditorGUI.DrawRect(title, new Color(0.22f, 0.22f, 0.25f));
        DrawOutline(rect, new Color(1f, 1f, 1f, 0.2f), 1f);
        GUI.Label(new Rect(title.x + 10f, title.y + 4f, title.width - 40f, title.height - 6f), dialog.Title, EditorStyles.miniBoldLabel);
        if (GUI.Button(close, "×", EditorStyles.miniButton))
        {
            Defer(() => dialog.Open = false);
        }

        GUILayout.BeginArea(new Rect(rect.x + 8f, title.yMax + 6f, rect.width - 14f, rect.height - title.height - 10f));
        dialog.Scroll = GUILayout.BeginScrollView(dialog.Scroll, false, false);
        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 92f;
        dialog.Body();
        EditorGUIUtility.labelWidth = labelWidth;
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void OpenRename()
    {
        if (string.IsNullOrEmpty(chartPath))
        {
            Notify("先に保存してください（保存すると名前を付けられます）");
            return;
        }

        renameText = ChartName;
        renameArtist = Chart.artist ?? string.Empty;
        renameDialog.Open = true;
    }

    private void DrawRenameDialog()
    {
        GUILayout.Label("譜面名（ファイル名・日本語可）", EditorStyles.miniLabel);
        renameText = GUILayout.TextField(renameText);
        GUILayout.Space(4f);
        GUILayout.Label("アーティスト名（日本語可）", EditorStyles.miniLabel);
        renameArtist = GUILayout.TextField(renameArtist ?? string.Empty);
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("キャンセル", GUILayout.Width(90f)))
        {
            Defer(() => renameDialog.Open = false);
        }

        if (GUILayout.Button("変更", GUILayout.Width(90f)))
        {
            Defer(() =>
            {
                string artist = renameArtist ?? string.Empty;
                if (artist != (Chart.artist ?? string.Empty))
                {
                    Modify("Set artist", () => Chart.artist = artist);
                }

                if (RenameChart(renameText))
                {
                    renameDialog.Open = false;
                }
            });
        }

        GUILayout.EndHorizontal();
    }

    // ------------------------------------------------------------------ auto generation

    /// <summary>End of the music on the song clock, else of the chart; 0 when there is neither.</summary>
    private double GenerateSongEnd()
    {
        if (audioClip != null)
        {
            return audioClip.length + Chart.audioOffset;
        }

        return Chart.notes.Length > 0 || Chart.lasers.Length > 0 ? Chart.EndTime : 0d;
    }

    private int MeasureAt(double time)
    {
        return Math.Max(1, (int)Math.Floor((time - Chart.offset) / (4d * BeatSeconds) + 1e-6) + 1);
    }

    private void GenerateRangeWhole()
    {
        double end = GenerateSongEnd();
        generateSettings.firstMeasure = MeasureAt(Math.Max(0d, Chart.offset));
        generateSettings.lastMeasure = end > 0d ? Math.Max(generateSettings.firstMeasure, MeasureAt(end - 1e-3)) : generateSettings.firstMeasure + 15;
    }

    private void OpenGenerateDialog()
    {
        if (selectedNotes.Count > 0 || SelectedLaserList().Count > 0)
        {
            double from = selectedNotes.Select(note => note.time).Concat(SelectedLaserList().Select(LaserStart)).Min();
            double to = selectedNotes.Select(note => note.EndTime).Concat(SelectedLaserList().Select(laser => laser.points[laser.points.Length - 1].time)).Max();
            generateSettings.firstMeasure = MeasureAt(from);
            generateSettings.lastMeasure = MeasureAt(to);
        }
        else
        {
            GenerateRangeWhole();
        }

        generateDialog.Open = true;
    }

    private void DrawGenerateDialog()
    {
        GUIStyle note = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
        GUILayout.Label("BPM・オフセットの拍に合わせてノーツを並べます。結果は選択された状態で入るので、Ctrl+Z で丸ごと戻せます。", note);
        GUILayout.Space(6f);
        bool canAnalyze = AudioImport.CanReadSamples(audioClip);
        using (new EditorGUI.DisabledScope(!canAnalyze))
        {
            generateFromAudio = EditorGUILayout.ToggleLeft("音源のリズムに合わせる（音の立ち上がりを解析）", generateFromAudio);
        }

        if (!canAnalyze)
        {
            GUILayout.Label(audioClip == null ? "音源がないので、パターンだけで並べます。" : "解析には音源の Load Type を Decompress On Load にしてください。", note);
        }

        GUILayout.Space(4f);

        GUILayout.BeginHorizontal();
        GUILayout.Label("小節", GUILayout.Width(40f));
        generateSettings.firstMeasure = Mathf.Max(1, EditorGUILayout.IntField(generateSettings.firstMeasure, GUILayout.Width(44f)));
        GUILayout.Label("〜", GUILayout.Width(18f));
        generateSettings.lastMeasure = Mathf.Max(generateSettings.firstMeasure, EditorGUILayout.IntField(generateSettings.lastMeasure, GUILayout.Width(44f)));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("曲全体", EditorStyles.miniButton, GUILayout.Width(56f)))
        {
            GenerateRangeWhole();
        }

        if (GUILayout.Button(new GUIContent("再生位置+8", "再生位置の小節から 8 小節"), EditorStyles.miniButton, GUILayout.Width(72f)))
        {
            generateSettings.firstMeasure = MeasureAt(cursorTime);
            generateSettings.lastMeasure = generateSettings.firstMeasure + 7;
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(4f);
        generateSettings.density = EditorGUILayout.IntSlider("密度", generateSettings.density, 1, 10);
        GUILayout.Space(4f);
        generateSettings.holds = EditorGUILayout.ToggleLeft("ロングノーツ", generateSettings.holds);
        generateSettings.flicks = EditorGUILayout.ToggleLeft("フリック（方向ノーツ）", generateSettings.flicks);
        generateSettings.lasers = EditorGUILayout.ToggleLeft("レーザー（5 小節目以降、2〜4 小節ずつ）", generateSettings.lasers);
        generateReplace = EditorGUILayout.ToggleLeft("範囲内の既存ノーツ・レーザーを置き換える", generateReplace);
        GUILayout.Space(4f);

        GUILayout.BeginHorizontal();
        GUILayout.Label("シード", GUILayout.Width(40f));
        generateSettings.seed = EditorGUILayout.IntField(generateSettings.seed, GUILayout.Width(70f));
        if (GUILayout.Button(new GUIContent("別の案", "シードを変えて生成し直す"), EditorStyles.miniButton, GUILayout.Width(60f)))
        {
            generateSettings.seed = UnityEngine.Random.Range(0, 100000);
            Defer(RunAutoGenerate);
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("閉じる", GUILayout.Width(80f)))
        {
            Defer(() => generateDialog.Open = false);
        }

        if (GUILayout.Button("生成", GUILayout.Width(80f)))
        {
            Defer(RunAutoGenerate);
        }

        GUILayout.EndHorizontal();
    }

    private void RunAutoGenerate()
    {
        ChartAutoGenerator.Settings settings = generateSettings;
        settings.bpm = Chart.bpm;
        settings.offset = Chart.offset;
        settings.songEnd = audioClip != null ? audioClip.length + Chart.audioOffset : 0d;
        settings.audioOffset = Chart.audioOffset;
        settings.audio = null;
        if (generateFromAudio && AudioImport.CanReadSamples(audioClip))
        {
            if (generateAnalysisClip != audioClip)
            {
                generateAnalysis = ChartAudioAnalysis.FromClip(audioClip);
                generateAnalysisClip = generateAnalysis != null ? audioClip : null;
            }

            settings.audio = generateAnalysis;
            if (generateAnalysis == null)
            {
                Notify("音源を解析できなかったので、パターンだけで並べます");
            }
        }

        ChartAutoGenerator.Result result = ChartAutoGenerator.Generate(settings);

        const double Eps = 1e-4;
        double from = MeasureStart(settings.firstMeasure) - Eps;
        double to = MeasureStart(settings.lastMeasure + 1) - Eps;
        bool replace = generateReplace;
        Modify("Auto generate", () =>
        {
            List<TapNoteData> kept = Chart.notes.ToList();
            List<LaserNoteData> keptLasers = Chart.lasers.ToList();
            if (replace)
            {
                kept.RemoveAll(note => note.time >= from && note.time < to);
                keptLasers.RemoveAll(laser => laser.points.Length > 0 && laser.points[0].time >= from && laser.points[0].time < to);
            }
            else
            {
                // Merging: drop generated notes that land on a note already in the same lane.
                result.Notes.RemoveAll(added => kept.Any(note => note.lane == added.lane && Math.Abs(note.time - added.time) < 0.01d));
            }

            Chart.notes = kept.Concat(result.Notes).ToArray();
            Chart.lasers = keptLasers.Concat(result.Lasers).ToArray();
        });

        ClearSelection();
        selectedNotes.AddRange(result.Notes);
        selectedLasers.AddRange(result.Lasers);
        Notify((settings.audio != null ? "自動生成（音源）: ノーツ " : "自動生成: ノーツ ") + result.Notes.Count + " / レーザー " + result.Lasers.Count + "（" + settings.firstMeasure + "〜" + settings.lastMeasure + " 小節）");
    }

    private void DrawInfoDialog()
    {
        DrawChartSection();
        GUILayout.Space(10f);
        DrawStatsSection();
    }

    private void DrawPlayDialog()
    {
        musicVolume = EditorGUILayout.Slider("音量", musicVolume, 0f, 1f);
        clapVolume = EditorGUILayout.Slider("クラップ音量", clapVolume, 0f, 1f);
        GUILayout.Space(6f);
        musicEnabled = EditorGUILayout.ToggleLeft("音楽を鳴らす", musicEnabled);
        clapEnabled = EditorGUILayout.ToggleLeft("ノーツでクラップを鳴らす", clapEnabled);
        returnOnStop = EditorGUILayout.ToggleLeft("停止で開始位置に戻る", returnOnStop);
        using (new EditorGUI.DisabledScope(flow.IsEmbeddedPreview))
        {
            bool autoPlay = EditorGUILayout.ToggleLeft("全画面プレビューをオートプレイ（ゲームプレビューは常にオート）", GameSettings.AutoPlay);
            if (autoPlay != GameSettings.AutoPlay)
            {
                GameSettings.AutoPlay = autoPlay;
            }
        }

        GUILayout.Space(6f);
        previewLookAhead = 2.7f - EditorGUILayout.Slider("2Dプレビュー速度", 2.7f - previewLookAhead, 0.2f, 2.5f);
    }

    private void DrawHelpDialog()
    {
        GUIStyle text = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = 12, richText = true };
        GUILayout.Label(
            "<b>ツール</b>\n" +
            "V 選択　B 範囲選択　N ノーツ　H ロング　F フリック　L レーザー　E 消しゴム\n" +
            string.Join("\n", Tools.Select(info => $"・{info.Name}: {info.Hint}")) + "\n\n" +
            "<b>再生</b>\n" +
            "Space 音楽の再生/停止（再生位置から）　ルーラーのクリックで再生位置を移動\n" +
            "P プレビュー（ゲームプレビューが出ていればその中で、なければ全画面）　Shift+P 全画面でプレビュー\n\n" +
            "<b>移動・表示</b>\n" +
            "↑↓ 再生位置をグリッド1つ（Shift で1小節）　PageUp/Down 1小節　Home 先頭\n" +
            "ホイール スクロール　Ctrl+ホイール ズーム　中ボタンドラッグ スクロール\n" +
            "[ ] グリッドを粗く/細かく（1拍の分割数）　G 吸着のON/OFF　Alt 押している間だけ吸着なし\n" +
            "Ctrl+1 ゲームプレビュー　Ctrl+2 2Dプレビュー　Ctrl+3 レーザー2Dビュー　Ctrl+4 / I 譜面情報\n\n" +
            "<b>編集</b>\n" +
            "1〜9 方向（テンキー配置）、5/0 なし　Shift+F フリックのみ切替\n" +
            "M 左右ミラー（L↔R・左↔右レーザー）　Shift+M 上下ミラー（L↔ZL・R↔ZR）　Ctrl+M 上下左右（L↔ZR・R↔ZL）\n" +
            "Ctrl+I 選択範囲を反転　編集メニュー > 前後反転（時間を逆に）　Ctrl+クリック（レーザー）: 選択に追加\n" +
            "Ctrl+C / X / V / D コピー・切り取り・貼り付け・複製（再生位置へ）\n" +
            "Ctrl+A 全ノーツ選択　Delete 削除　Esc 選択解除\n" +
            "Ctrl+Z / Y 元に戻す・やり直し　Ctrl+S 保存　Ctrl+N 新規　Ctrl+O 開く\n\n" +
            "<b>3D編集ビュー</b>（Tab / メニューバーの 2D⇄3D）\n" +
            "まっすぐ続く箱の天井が L / R、床が ZL / ZR、間がレーザーの空間。赤い枠が再生位置（配置面）\n" +
            "中ドラッグ 視点を回す　Shift+中ドラッグ 平行移動　Ctrl+中ドラッグ / Shift+ホイール ズーム\n" +
            "ホイール 再生位置をグリッド1つ　Ctrl+ホイール 奥行きの間隔　左上のボタン: 正面 / 上 / 横 / 斜め・透視 / 平行\n" +
            "レーンをクリック: ノーツ（ツールは2Dと同じ）　レーザーツールで配置面をクリック: 再生位置に点\n" +
            "レーザー点をドラッグ: X・Y（Ctrl で時刻）\n\n" +
            "<b>レーザー2Dビュー</b>（レーザー列の下）\n" +
            "点をドラッグ: 位置を変える　右クリック: 点を削除　レーザーツールでクリック: 再生位置に点（Shift で新しいレーザー）",
            text);
    }

    // ------------------------------------------------------------------ status bar & keys

    private void DrawStatusBar(Rect rect)
    {
        EditorGUI.DrawRect(rect, BarColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), BorderColor);
        Rect text = new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, rect.height - 4f);
        string selection = selectedNotes.Count > 0 || selectedLasers.Count > 0 ? $"選択: ノーツ {selectedNotes.Count}" + (selectedLasers.Count > 0 ? $" ・レーザー {selectedLasers.Count}" : string.Empty)
            : selectedLaser != null ? $"選択: レーザー（{(selectedLaser.side == 0 ? "左" : "右")}）"
            : string.Empty;
        string message = !string.IsNullOrEmpty(tooltipText) ? tooltipText
            : Time.realtimeSinceStartupAsDouble < noticeUntil ? notice
            : hoverHint;
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
        GUI.Label(text, $"{FormatTime(cursorTime)}    [{ToolName(tool)}]    {selection}    {message}", style);
    }

    private void OpenChartFile()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        string openPath = NativeDialogs.OpenFile("譜面を開く", NativeDialogs.JsonFilter, ChartsDirectory);
        if (!string.IsNullOrEmpty(openPath))
        {
            Load(openPath);
        }
    }

    private void ImportAudio()
    {
        string source = NativeDialogs.OpenFile("音源を取り込む", NativeDialogs.AudioFilter, Application.dataPath);
        string audioValue = string.IsNullOrEmpty(source) ? null : AudioImport.Import(source);
        if (audioValue != null)
        {
            Modify("Import audio", () => Chart.audio = audioValue);
            loadedAudioPath = null; // reload the clip and its waveform from the new file
            Notify($"{System.IO.Path.GetFileName(source)} を取り込みました");
        }
        else if (!string.IsNullOrEmpty(source))
        {
            Notify("音源を取り込めませんでした");
        }
    }

    private void SelectAllNotes()
    {
        ClearSelection();
        selectedNotes.AddRange(Chart.notes);
    }

    private void GoToStart()
    {
        cursorTime = 0d;
        followOnLayout = true;
    }

    /// <summary>
    /// Keyboard shortcuts. The action runs at the next Layout event, since many of them change
    /// what the control bar shows.
    /// </summary>
    private void HandleKeyboard(Event current)
    {
        if (current.type != EventType.KeyDown)
        {
            return;
        }

        if (EditorGUIUtility.editingTextField)
        {
            if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter || current.keyCode == KeyCode.Escape)
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                current.Use();
            }

            return;
        }

        if (current.keyCode == KeyCode.None)
        {
            return;
        }

        Action action = ShortcutAction(current);
        if (action != null)
        {
            current.Use();
            Defer(action);
        }
    }

    private Action ShortcutAction(Event current)
    {
        bool shift = current.shift;
        if (current.control || current.command)
        {
            Action Edit(Action action) => () =>
            {
                action();
                UpdateDirty();
            };

            switch (current.keyCode)
            {
                case KeyCode.S: return Save;
                case KeyCode.N: return NewChart;
                case KeyCode.O: return OpenChartFile;
                case KeyCode.C: return CopySelection;
                case KeyCode.X:
                    return Edit(() =>
                    {
                        CopySelection();
                        DeleteSelection();
                    });
                case KeyCode.V: return Edit(Paste);
                case KeyCode.D:
                    return Edit(() =>
                    {
                        CopySelection();
                        Paste();
                    });
                case KeyCode.A: return SelectAllNotes;
                case KeyCode.I: return InvertSelection;
                case KeyCode.M: return () => MirrorSelection(true, true);
                case KeyCode.Z: return Edit(() => (shift ? (Func<ChartEditorDocument, bool>)Undo.PerformRedo : Undo.PerformUndo)(document));
                case KeyCode.Y: return Edit(() => Undo.PerformRedo(document));
                case KeyCode.Alpha1: return Toggle3D;
                case KeyCode.Alpha2: return Toggle2D;
                case KeyCode.Alpha3: return ToggleLaserView;
                case KeyCode.Alpha4: return () => infoDialog.Open = !infoDialog.Open;
                case KeyCode.Equals:
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                    return () => SetZoom(pixelsPerSecond * 1.25f, TimeToY(cursorTime));
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    return () => SetZoom(pixelsPerSecond / 1.25f, TimeToY(cursorTime));
                default: return null;
            }
        }

        foreach (ToolInfo info in Tools)
        {
            if (current.keyCode == info.Key && !(info.Tool == Tool.Flick && shift))
            {
                Tool chosen = info.Tool;
                return () => SetTool(chosen);
            }
        }

        switch (current.keyCode)
        {
            case KeyCode.Space: return TogglePlayback;
            case KeyCode.P: return shift ? (Action)PreviewInGame : TogglePreview;
            case KeyCode.F: return ToggleFlickOnly; // Shift+F (plain F picks the flick tool)
            case KeyCode.F1: return () => helpDialog.Open = !helpDialog.Open;
            case KeyCode.F2: return OpenRename;
            case KeyCode.I: return () => infoDialog.Open = !infoDialog.Open;
            case KeyCode.M: return shift ? (Action)(() => MirrorSelection(false, true)) : () => MirrorSelection(true, false);
            case KeyCode.Tab: return ToggleEditView3D;
            case KeyCode.Escape: return ClearSelection;
            case KeyCode.Delete:
            case KeyCode.Backspace:
                return DeleteSelection;
            case KeyCode.UpArrow: return () => MoveCursor(shift ? BeatSeconds * 4d : GridStep);
            case KeyCode.DownArrow: return () => MoveCursor(shift ? -BeatSeconds * 4d : -GridStep);
            case KeyCode.PageUp: return () => MoveCursor(BeatSeconds * 4d);
            case KeyCode.PageDown: return () => MoveCursor(-BeatSeconds * 4d);
            case KeyCode.Home: return GoToStart;
            case KeyCode.LeftBracket: return () => StepDivision(-1);
            case KeyCode.RightBracket: return () => StepDivision(1);
            case KeyCode.G:
                return () =>
                {
                    snapEnabled = !snapEnabled;
                    notice = snapEnabled ? "スナップ ON" : "スナップ OFF";
                    noticeUntil = Time.realtimeSinceStartupAsDouble + 1.5d;
                };
            default:
                KeyCode key = current.keyCode;
                return IsDirectionKey(key) ? () => TryDirectionKey(key) : (Action)null;
        }
    }

    private static bool IsDirectionKey(KeyCode key)
    {
        return key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 || key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9;
    }
}
