using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The in-game HUD: the backlit overhead subway sign across the top (the MetroSignHud
/// prefab, Resources/UI/MetroSign — the cyan logo tile, the song as a station with its
/// line badge, the score, the player as the exit with the lit yellow panel), the lane
/// names and the message popup (READY) over the far end. The stage is drawn below the
/// sign (<see cref="StageLayout.topInset"/>).
///
/// The canvas is drawn by an overlay camera stacked on the stage camera, so the stage's
/// post-processing reaches it and the sign's HDR layers and lettering bloom like a lit
/// sign. Judgements are not shown here: LaneJudgementView draws them on the lanes, like
/// the combo (LaneComboView).
/// </summary>
public sealed class RhythmJudgementUI : MonoBehaviour
{
    private const int UiLayer = 5;

    // The popup is a small sign of the same make as the HUD sign.
    private static readonly Color SignFaceTop = new Color32(0x0B, 0x12, 0x20, 0xFF);
    private static readonly Color SignFaceBottom = new Color32(0x05, 0x08, 0x0F, 0xFF);
    private static readonly Color HousingLight = new Color32(0x2A, 0x2C, 0x31, 0xFF);
    private static readonly Color HousingDark = new Color32(0x0D, 0x0E, 0x10, 0xFF);
    private static readonly Color Lettering = Color.white;
    private static readonly Color LetteringSoft = new Color32(0xC4, 0xC8, 0xCE, 0xFF);

    [SerializeField] private JudgeSystem judgeSystem;

    [Header("Lane labels (placed at runtime from the stage)")]
    [SerializeField] private int laneLabelSize = 26;
    [Tooltip("判定ラインからの上下オフセット（画面の高さに対する割合）。")]
    [SerializeField] private float laneLabelOffset = 0.036f;

    private readonly RectTransform[] laneLabels = new RectTransform[RhythmStageView.LaneCount];

    private ScoreTracker score;
    private SongClock songClock;
    private NoteSpawner noteSpawner;
    private LaserWarningUI laserWarning;
    private Canvas canvas;
    private Camera hudCamera;
    private CanvasGroup popupGroup;
    private RectTransform popupRect;
    private Text messageText;
    private MetroSignHud sign;
    private SongChartData chart;
    private float popupShownAt;
    private float popupVisibleUntil;

    private void Awake()
    {
        BuildHud();
    }

    private void Start()
    {
        songClock = FindFirstObjectByType<SongClock>();
        noteSpawner = FindFirstObjectByType<NoteSpawner>();
        score = FindFirstObjectByType<ScoreTracker>();
        if (score != null)
        {
            score.Changed += RefreshScore;
        }

        GameSettings.Changed += RefreshStatus;
        RefreshScore();
        RefreshStatus();
    }

    private void OnDestroy()
    {
        if (score != null)
        {
            score.Changed -= RefreshScore;
        }

        GameSettings.Changed -= RefreshStatus;
    }

    private void Update()
    {
        if (canvas == null || !canvas.gameObject.activeSelf)
        {
            return;
        }

        // The message pops in slightly oversized and fades out at the end of its time.
        float now = Time.unscaledTime;
        popupGroup.alpha = Mathf.Clamp01((popupVisibleUntil - now) * 6f);
        float pop = 1f + 0.16f * Mathf.Clamp01(1f - (now - popupShownAt) / 0.1f);
        popupRect.localScale = new Vector3(pop, pop, 1f);

        if (chart != null && songClock != null && sign != null)
        {
            double end = Math.Max(0.01d, chart.EndTime);
            sign.SetProgress((float)(songClock.SongTime / end));
        }

        // The chart being played (also a chart editor preview), so the sides warn about the mirrored lasers too.
        if (laserWarning != null && songClock != null && noteSpawner != null)
        {
            laserWarning.Tick(noteSpawner.Chart, songClock.SongTime - GameSettings.VisualOffsetSeconds, Time.unscaledDeltaTime);
        }
    }

    /// <summary>
    /// Draws the stage and the HUD into part of the screen (the chart editor's game preview sidebar) or,
    /// with the full rect, the whole screen. The HUD canvas is scaled to the part's width, so the
    /// sign (laid out 1920 units wide) fits a narrow part too.
    /// </summary>
    public void SetViewport(Rect viewport)
    {
        Camera stageCamera = Camera.main;
        if (stageCamera != null)
        {
            stageCamera.rect = viewport;
        }

        if (hudCamera != null)
        {
            hudCamera.rect = viewport;
        }

        CanvasScaler scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
        if (scaler == null)
        {
            return;
        }

        if (viewport.width >= 0.999f && viewport.height >= 0.999f)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        }
        else
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = Mathf.Max(0.1f, viewport.width * Screen.width / UiKit.ReferenceWidth);
        }
    }

    public void SetVisible(bool visible)
    {
        canvas.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Prepares the HUD for a new play of <paramref name="newChart"/>, the station
    /// <paramref name="genreLetter"/><paramref name="stationNumber"/> of its line.
    /// </summary>
    public void BeginSong(SongChartData newChart, string genreLetter, string stationNumber)
    {
        chart = newChart;
        popupVisibleUntil = 0f;
        if (sign != null)
        {
            sign.SetSong(
                chart != null ? chart.title : string.Empty,
                chart != null ? chart.artist : string.Empty,
                chart != null ? chart.Difficulty.Color : SignKit.MetroRed,
                genreLetter,
                stationNumber,
                chart != null ? chart.level : 0,
                LoadCover(chart));
        }

        PlaceStageLabels();
        RefreshScore();
        RefreshStatus();
    }

    /// <summary>Shows a message (READY, ...) over the far end of the lanes.</summary>
    public void ShowMessage(string message, float seconds)
    {
        messageText.text = message;
        popupShownAt = Time.unscaledTime;
        popupVisibleUntil = popupShownAt + seconds;
    }

    private void BuildHud()
    {
        canvas = UiKit.CreateCanvas(transform, "RhythmHUD", 100);
        Transform root = canvas.transform;

        MetroSignHud prefab = Resources.Load<MetroSignHud>(MetroSignHud.ResourcePath);
        if (prefab != null)
        {
            sign = Instantiate(prefab, root, false);
            sign.name = prefab.name;
        }
        else
        {
            Debug.LogError($"HUD sign prefab 'Resources/{MetroSignHud.ResourcePath}' not found; run Metro > Build HUD Sign Prefab.", this);
        }

        BuildPopup(root);

        // Lane names in station-code boxes, in white on the dark stage.
        for (int lane = 0; lane < laneLabels.Length; lane++)
        {
            string label = RhythmStageView.GetLaneLabel(lane);
            Text text = SignKit.CreateCodeBox(root, $"LaneLabel_{label}", UiKit.WithAlpha(LetteringSoft, 0.7f), laneLabelSize, 2.5f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(label.Length > 1 ? 72f : 52f, 42f));
            text.text = label;
            laneLabels[lane] = (RectTransform)text.transform.parent;
        }

        // Blinking notice of a coming laser at each side of the stage (left stick left, right stick right).
        laserWarning = new LaserWarningUI(root);

        DrawThroughPostProcessing();
    }

    /// <summary>
    /// Moves the HUD canvas onto an overlay camera stacked on the stage camera, so the
    /// stage's volume (bloom, grain, vignette) applies to the sign as well. The overlay
    /// camera applies the post-processing to the whole stack, so the stage camera stops
    /// applying its own (RhythmStageView leaves it off while such a camera is stacked).
    /// Without a URP stage camera the canvas stays a plain overlay.
    /// </summary>
    private void DrawThroughPostProcessing()
    {
        Camera stageCamera = Camera.main;
        UniversalAdditionalCameraData stageData = stageCamera != null ? stageCamera.GetUniversalAdditionalCameraData() : null;
        if (stageData == null || stageData.renderType != CameraRenderType.Base)
        {
            return;
        }

        GameObject cameraObject = new GameObject("HudCamera");
        cameraObject.transform.SetParent(transform, false);

        // Far below the stage, so it never sees any of it (its mask is the UI layer only anyway).
        cameraObject.transform.position = new Vector3(0f, -5000f, 0f);
        hudCamera = cameraObject.AddComponent<Camera>();
        hudCamera.orthographic = true;
        hudCamera.cullingMask = 1 << UiLayer;
        hudCamera.nearClipPlane = 0.1f;
        hudCamera.farClipPlane = 50f;
        hudCamera.clearFlags = CameraClearFlags.Nothing;

        UniversalAdditionalCameraData hudData = hudCamera.GetUniversalAdditionalCameraData();
        hudData.renderType = CameraRenderType.Overlay;
        hudData.renderPostProcessing = true;
        stageData.cameraStack.Add(hudCamera);
        stageData.renderPostProcessing = false;
        stageCamera.cullingMask &= ~(1 << UiLayer);

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = hudCamera;
        canvas.planeDistance = 10f;
        foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = UiLayer;
        }
    }

    /// <summary>
    /// Messages (READY) in a small dark sign just above the far end of the lanes;
    /// the centre itself is where the lasers fly.
    /// </summary>
    private void BuildPopup(Transform root)
    {
        popupRect = UiKit.CreateRect(root, "MessagePopup");
        UiKit.Place(popupRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(220f, 56f));
        popupGroup = popupRect.gameObject.AddComponent<CanvasGroup>();
        popupGroup.alpha = 0f;
        popupGroup.blocksRaycasts = false;

        RoundedRectGraphic housing = SignKit.CreateRoundedRect(popupRect, "Housing", Color.white, 6f);
        housing.SetVerticalGradient(HousingLight, HousingDark);
        UiKit.Stretch(housing);
        RoundedRectGraphic face = SignKit.CreateRoundedRect(popupRect, "Face", Color.white, 3f);
        face.SetVerticalGradient(SignFaceTop, SignFaceBottom);
        UiKit.Stretch(face, 4f, 4f, 4f, 4f);

        messageText = SignKit.CreateText(popupRect, "Message", string.Empty, 30, TextAnchor.MiddleCenter, Lettering, true);
        UiKit.Stretch(messageText);
        messageText.rectTransform.anchoredPosition = new Vector2(0f, 30f * SignKit.CapRaise);
    }

    /// <summary>
    /// Puts each lane's label just outside its judgement line, and the message popup
    /// over the far end. Done when a song starts, by which time the stage has placed
    /// the camera (the stage sits below the sign, so neither is at a fixed spot).
    /// </summary>
    private void PlaceStageLabels()
    {
        Camera stageCamera = Camera.main;
        if (stageCamera == null)
        {
            return;
        }

        for (int lane = 0; lane < laneLabels.Length; lane++)
        {
            Vector3 viewport = stageCamera.WorldToViewportPoint(RhythmStageView.GetLaneWorldPosition(lane, 1f));
            float outward = RhythmStageView.IsUpperLane(lane) ? laneLabelOffset : -laneLabelOffset;
            PlaceAt(laneLabels[lane], new Vector2(viewport.x, viewport.y + outward), Vector2.zero);
        }

        Vector3 farWall = stageCamera.WorldToViewportPoint(RhythmStageView.GetLaserWorldPosition(Vector2.zero, 0f));
        PlaceAt(popupRect, farWall, new Vector2(0f, 40f));
    }

    private static void PlaceAt(RectTransform rect, Vector2 viewport, Vector2 offset)
    {
        rect.anchorMin = viewport;
        rect.anchorMax = viewport;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
    }

    /// <summary>The chart's cover art, or null when it has none (the sign then shows the station badge).</summary>
    private Texture2D LoadCover(SongChartData song)
    {
        if (song == null || string.IsNullOrWhiteSpace(song.jacket))
        {
            return null;
        }

        Texture2D art = Resources.Load<Texture2D>(song.jacket);
        if (art == null)
        {
            Debug.LogWarning($"Chart '{song.id}': cover art 'Resources/{song.jacket}' not found; showing the station badge.", this);
        }

        return art;
    }

    private void RefreshScore()
    {
        if (score == null || sign == null)
        {
            return;
        }

        sign.SetScore(score.Percent);
    }

    /// <summary>The player's name, and the options that change how the song plays so a glance tells what is on.</summary>
    private void RefreshStatus()
    {
        if (sign == null)
        {
            return;
        }

        string status = $"HI-SPEED x{GameSettings.HiSpeed:0.0}";
        if (GameSettings.Mirror)
        {
            status += "  ·  MIRROR";
        }

        if (GameSettings.AutoPlay)
        {
            status += $"  ·  <color=#{ColorUtility.ToHtmlStringRGB(new Color(1f, 0.42f, 0.42f))}>AUTO</color>";
        }

        sign.SetPlayer(GameSettings.PlayerName, status);
    }
}
