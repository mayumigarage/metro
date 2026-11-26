using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The in-game HUD, built in code: one backlit overhead sign hanging across the top of
/// the station passage, like the dark guide signs in a subway concourse — white
/// lettering on a near-black face in an aluminium housing, sections split by thin
/// rules. Left: an arrow, the song's badge on a white tile (or its cover art), title,
/// artist, and the service (difficulty) and level. Centre: スコア / Score with the
/// achievement rate to four decimals, and the song's progress as a line-colour rule
/// along the bottom. Right: the player, the options in effect and a white pictogram
/// tile that is the pause button. The stage is drawn below the sign
/// (<see cref="StageLayout.topInset"/>).
///
/// The canvas is drawn by an overlay camera stacked on the stage camera, so the stage's
/// post-processing reaches it and the white lettering blooms like a lit sign.
/// Judgements are not shown here: LaneJudgementView draws them on the lanes, like the
/// combo (LaneComboView). Only messages (READY) pop up over the far end.
/// </summary>
public sealed class RhythmJudgementUI : MonoBehaviour
{
    private const int UiLayer = 5;
    private const float SignTop = 14f;
    private const float SignHeight = 128f;
    private const float SignSide = 20f;
    private const float Housing = 5f;
    private const float ScoreHalfWidth = 300f;
    private const float TileSize = 84f;
    private const int PercentSize = 66;

    private static readonly Color SignFaceTop = new Color32(0x1E, 0x21, 0x26, 0xFF);
    private static readonly Color SignFaceBottom = new Color32(0x10, 0x12, 0x15, 0xFF);
    private static readonly Color HousingLight = new Color32(0x6E, 0x73, 0x7A, 0xFF);
    private static readonly Color HousingDark = new Color32(0x2A, 0x2D, 0x32, 0xFF);
    private static readonly Color Rule = new Color32(0x4A, 0x4F, 0x57, 0xFF);
    private static readonly Color Lettering = Color.white;
    private static readonly Color LetteringSoft = new Color32(0xC4, 0xC8, 0xCE, 0xFF);
    private static readonly Color LetteringDim = new Color32(0x8A, 0x90, 0x98, 0xFF);

    [SerializeField] private JudgeSystem judgeSystem;

    [Header("Lane labels (placed at runtime from the stage)")]
    [SerializeField] private int laneLabelSize = 26;
    [Tooltip("判定ラインからの上下オフセット（画面の高さに対する割合）。")]
    [SerializeField] private float laneLabelOffset = 0.036f;

    /// <summary>The pause button was clicked.</summary>
    public event Action PauseRequested;

    private readonly RectTransform[] laneLabels = new RectTransform[RhythmStageView.LaneCount];

    private ScoreTracker score;
    private SongClock songClock;
    private Canvas canvas;
    private CanvasGroup popupGroup;
    private RectTransform popupRect;
    private Text messageText;
    private Text percentText;
    private Text titleText;
    private Text artistText;
    private Text serviceText;
    private Text levelText;
    private Text playerText;
    private Text statusText;
    private LineBadge songBadge;
    private RoundedRectGraphic badgeTile;
    private RawImage coverImage;
    private RectTransform progressFill;
    private AngularGraphic progressLine;
    private RoundedRectGraphic pauseTile;
    private RoundedRectGraphic[] pauseBars;
    private SongChartData chart;
    private Color lineColor = SignKit.MetroRed;
    private bool pauseHovered;
    private float popupShownAt;
    private float popupVisibleUntil;

    private void Awake()
    {
        BuildHud();
    }

    private void Start()
    {
        songClock = FindFirstObjectByType<SongClock>();
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

        if (chart != null && songClock != null)
        {
            double end = Math.Max(0.01d, chart.EndTime);
            float progress = Mathf.Clamp01((float)(songClock.SongTime / end));
            progressFill.anchorMax = new Vector2(progress, 1f);
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
        lineColor = chart != null ? chart.Difficulty.Color : SignKit.MetroRed;
        titleText.text = chart != null ? chart.title : string.Empty;
        artistText.text = chart != null ? chart.artist ?? string.Empty : string.Empty;
        serviceText.text = chart != null ? chart.Difficulty.Name.ToUpperInvariant() : string.Empty;
        serviceText.color = Color.Lerp(lineColor, Color.white, 0.15f);
        levelText.text = chart != null ? $"<size=20>Lv</size> {chart.level}" : string.Empty;
        songBadge.SetLineColor(lineColor);
        songBadge.SetCode(genreLetter, stationNumber);
        progressLine.color = lineColor;
        progressFill.anchorMax = new Vector2(0f, 1f);
        popupVisibleUntil = 0f;
        ShowCover(chart);
        RefreshPauseButton();
        PlaceStageLabels();
        RefreshScore();
        RefreshStatus();
    }

    /// <summary>Shows a message (READY, ...) over the far end of the passage.</summary>
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

        RectTransform face = BuildSign(root);
        BuildSongSection(face);
        BuildScoreSection(face);
        BuildPlayerSection(face);
        BuildPopup(root);

        // Lane names in station-code boxes, in white on the dark passage.
        for (int lane = 0; lane < laneLabels.Length; lane++)
        {
            string label = RhythmStageView.GetLaneLabel(lane);
            Text text = SignKit.CreateCodeBox(root, $"LaneLabel_{label}", UiKit.WithAlpha(LetteringSoft, 0.7f), laneLabelSize, 2.5f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(label.Length > 1 ? 72f : 52f, 42f));
            text.text = label;
            laneLabels[lane] = (RectTransform)text.transform.parent;
        }

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
        Camera hudCamera = cameraObject.AddComponent<Camera>();
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
    /// The sign: two hanger rods from the ceiling, an aluminium housing and the dark
    /// backlit face across the whole width, with the rules that split its sections.
    /// Returns the face.
    /// </summary>
    private static RectTransform BuildSign(Transform root)
    {
        for (int side = 0; side < 2; side++)
        {
            RoundedRectGraphic rod = SignKit.CreateRoundedRect(root, $"Hanger{side}", Color.white, 2f);
            rod.SetVerticalGradient(HousingDark, HousingLight);
            UiKit.Place(rod, new Vector2(side == 0 ? 0.2f : 0.8f, 1f), new Vector2(0f, -SignTop * 0.5f), new Vector2(8f, SignTop + 4f));
        }

        RectTransform sign = UiKit.CreateRect(root, "Sign");
        sign.anchorMin = new Vector2(0f, 1f);
        sign.anchorMax = new Vector2(1f, 1f);
        sign.pivot = new Vector2(0.5f, 1f);
        sign.offsetMin = new Vector2(SignSide, -SignTop - SignHeight);
        sign.offsetMax = new Vector2(-SignSide, -SignTop);

        // A soft shadow under the sign on the ceiling behind it.
        RoundedRectGraphic shadow = SignKit.CreateRoundedRect(sign, "Shadow", new Color(0f, 0f, 0f, 0.45f), 10f);
        UiKit.Stretch(shadow, -6f, -10f, -6f, 2f);

        RoundedRectGraphic housing = SignKit.CreateRoundedRect(sign, "Housing", Color.white, 6f);
        housing.SetVerticalGradient(HousingLight, HousingDark);
        UiKit.Stretch(housing);

        RoundedRectGraphic face = SignKit.CreateRoundedRect(sign, "Face", Color.white, 3f);
        face.SetVerticalGradient(SignFaceTop, SignFaceBottom);
        UiKit.Stretch(face, Housing, Housing, Housing, Housing);

        // The light inside shows as a faint brighter band near the top of the face.
        AngularGraphic sheen = UiKit.CreatePanel(face.transform, "Sheen", new Color(1f, 1f, 1f, 0.035f));
        RectTransform sheenRect = sheen.rectTransform;
        sheenRect.anchorMin = new Vector2(0f, 0.55f);
        sheenRect.anchorMax = new Vector2(1f, 1f);
        sheenRect.offsetMin = Vector2.zero;
        sheenRect.offsetMax = Vector2.zero;

        for (int side = -1; side <= 1; side += 2)
        {
            AngularGraphic rule = UiKit.CreatePanel(face.transform, side < 0 ? "RuleLeft" : "RuleRight", Rule);
            RectTransform rect = rule.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(3f, -20f);
            rect.anchoredPosition = new Vector2(side * ScoreHalfWidth, 0f);
        }

        return face.rectTransform;
    }

    /// <summary>
    /// Left: an up arrow ("this way"), the song's badge on a white tile as on the line
    /// signs (its cover art there instead when it has one), title and artist in white,
    /// and the service and level at the end like a sign's distance.
    /// </summary>
    private void BuildSongSection(Transform face)
    {
        Vector2 left = new Vector2(0f, 0.5f);
        BuildArrow(face, new Vector2(24f, 0f));

        badgeTile = SignKit.CreateRoundedRect(face, "BadgeTile", Color.white, 12f);
        UiKit.Place(badgeTile, left, new Vector2(82f, 0f), new Vector2(TileSize, TileSize));
        songBadge = LineBadge.Create(badgeTile.transform, "SongBadge", lineColor, TileSize - 16f);

        coverImage = UiKit.CreateRect(face, "Cover").gameObject.AddComponent<RawImage>();
        coverImage.raycastTarget = false;
        UiKit.Place(coverImage, left, new Vector2(82f, 0f), new Vector2(TileSize, TileSize));
        coverImage.enabled = false;

        float textLeft = 82f + TileSize + 20f;
        float levelWidth = 112f;
        float sectionWidth = 960f - SignSide - Housing - ScoreHalfWidth;
        float textWidth = sectionWidth - textLeft - levelWidth - 16f;

        titleText = SignKit.CreateFittedText(face, "Title", 40, 20, TextAnchor.MiddleLeft, Lettering, true);
        UiKit.Place(titleText, left, new Vector2(textLeft, 16f), new Vector2(textWidth, 52f));
        artistText = SignKit.CreateFittedText(face, "Artist", 22, 14, TextAnchor.MiddleLeft, LetteringSoft, false);
        UiKit.Place(artistText, left, new Vector2(textLeft + 2f, -26f), new Vector2(textWidth, 30f));

        float levelLeft = sectionWidth - levelWidth - 12f;
        serviceText = SignKit.CreateText(face, "Service", string.Empty, 15, TextAnchor.MiddleRight, lineColor, true);
        UiKit.Place(serviceText, left, new Vector2(levelLeft, 20f), new Vector2(levelWidth, 22f));
        levelText = SignKit.CreateText(face, "Level", string.Empty, 34, TextAnchor.MiddleRight, Lettering, true);
        levelText.supportRichText = true;
        UiKit.Place(levelText, left, new Vector2(levelLeft, -14f), new Vector2(levelWidth, 44f));
    }

    /// <summary>The sign's white arrow pointing ahead: a shaft and a triangular head.</summary>
    private static void BuildArrow(Transform face, Vector2 position)
    {
        RectTransform arrow = UiKit.CreateRect(face, "Arrow");
        UiKit.Place(arrow, new Vector2(0f, 0.5f), position, new Vector2(44f, 64f));

        AngularGraphic shaft = UiKit.CreatePanel(arrow, "Shaft", Lettering);
        UiKit.Place(shaft, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(11f, 42f));

        // The chevron points right; turned a quarter round it points up.
        ChevronGraphic head = SignKit.CreateChevron(arrow, "Head", Lettering, 0f);
        RectTransform headRect = head.rectTransform;
        headRect.anchorMin = new Vector2(0.5f, 1f);
        headRect.anchorMax = new Vector2(0.5f, 1f);
        headRect.pivot = new Vector2(0.5f, 0.5f);
        headRect.sizeDelta = new Vector2(26f, 40f);
        headRect.anchoredPosition = new Vector2(0f, -13f);
        headRect.localRotation = Quaternion.Euler(0f, 0f, 90f);
    }

    /// <summary>
    /// Centre: スコア with "Score" under it, as a sign names a line, and the achievement
    /// rate large on the right like the distance; the song's progress fills a rule in the
    /// line colour along the bottom.
    /// </summary>
    private void BuildScoreSection(Transform face)
    {
        RectTransform section = UiKit.CreateRect(face, "Score");
        section.anchorMin = new Vector2(0.5f, 0f);
        section.anchorMax = new Vector2(0.5f, 1f);
        section.pivot = new Vector2(0.5f, 0.5f);
        section.sizeDelta = new Vector2(ScoreHalfWidth * 2f, 0f);
        Vector2 left = new Vector2(0f, 0.5f);

        Text caption = SignKit.CreateText(section, "Caption", "スコア", 36, TextAnchor.MiddleLeft, Lettering, true);
        UiKit.Place(caption, left, new Vector2(30f, 18f), new Vector2(170f, 46f));
        Text english = SignKit.CreateText(section, "CaptionEnglish", "Score", 20, TextAnchor.MiddleLeft, LetteringSoft, true);
        UiKit.Place(english, left, new Vector2(32f, -20f), new Vector2(170f, 28f));

        percentText = SignKit.CreateText(section, "Percent", FormatScore(0f), PercentSize, TextAnchor.MiddleRight, Lettering, true);
        UiKit.Place(percentText, new Vector2(1f, 0.5f), new Vector2(-28f, 4f + PercentSize * SignKit.CapRaise), new Vector2(420f, 80f));

        AngularGraphic track = UiKit.CreatePanel(section, "Progress", new Color(1f, 1f, 1f, 0.10f));
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.offsetMin = new Vector2(24f, 10f);
        trackRect.offsetMax = new Vector2(-24f, 15f);

        progressLine = UiKit.CreatePanel(track.transform, "Fill", lineColor);
        progressFill = progressLine.rectTransform;
        progressFill.anchorMin = Vector2.zero;
        progressFill.anchorMax = new Vector2(0f, 1f);
        progressFill.offsetMin = Vector2.zero;
        progressFill.offsetMax = Vector2.zero;
    }

    /// <summary>Right: the player's name and the options in effect, then the pause pictogram tile at the end.</summary>
    private void BuildPlayerSection(Transform face)
    {
        // Text column from just right of the score's rule to just left of the pause tile.
        const float width = 470f;
        float centre = ScoreHalfWidth + 30f + width * 0.5f;
        Vector2 middle = new Vector2(0.5f, 0.5f);

        Text caption = SignKit.CreateText(face, "PlayerCaption", SignKit.Bilingual("プレイヤー", "Player", 15), 20, TextAnchor.MiddleLeft, LetteringSoft, true);
        UiKit.Place(caption, middle, new Vector2(centre, 34f), new Vector2(width, 28f));

        playerText = SignKit.CreateFittedText(face, "PlayerName", 38, 18, TextAnchor.MiddleLeft, Lettering, true);
        UiKit.Place(playerText, middle, new Vector2(centre, -2f), new Vector2(width, 50f));

        statusText = SignKit.CreateText(face, "Status", string.Empty, 16, TextAnchor.MiddleLeft, LetteringDim, true);
        statusText.supportRichText = true;
        UiKit.Place(statusText, middle, new Vector2(centre, -38f), new Vector2(width, 24f));

        BuildPauseButton(face);
    }

    /// <summary>A white pictogram tile with the pause mark, like the facility tiles on the sign; clicking it pauses.</summary>
    private void BuildPauseButton(Transform face)
    {
        pauseTile = SignKit.CreateRoundedRect(face, "PauseButton", Color.white, 12f);
        UiKit.Place(pauseTile, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(TileSize, TileSize));

        pauseBars = new RoundedRectGraphic[2];
        for (int bar = 0; bar < 2; bar++)
        {
            pauseBars[bar] = SignKit.CreateRoundedRect(pauseTile.transform, $"Bar{bar}", SignFaceBottom, 2f);
            UiKit.Place(pauseBars[bar], new Vector2(0.5f, 0.5f), new Vector2((bar == 0 ? -1f : 1f) * 10f, 0f), new Vector2(11f, 38f));
        }

        UiPointerRelay relay = SignKit.AddRelay(pauseTile);
        relay.Entered = () =>
        {
            pauseHovered = true;
            RefreshPauseButton();
        };
        relay.Exited = () =>
        {
            pauseHovered = false;
            RefreshPauseButton();
        };
        relay.Clicked = eventData => PauseRequested?.Invoke();
    }

    private void RefreshPauseButton()
    {
        if (pauseTile == null)
        {
            return;
        }

        pauseTile.color = pauseHovered ? lineColor : Color.white;
        foreach (RoundedRectGraphic bar in pauseBars)
        {
            bar.color = pauseHovered ? Color.white : SignFaceBottom;
        }
    }

    /// <summary>
    /// Messages (READY) in a small dark sign just above the far end of the passage;
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

    /// <summary>The chart's cover art, cropped to a square, or the song's badge on its white tile when it has none.</summary>
    private void ShowCover(SongChartData song)
    {
        Texture2D art = song != null && !string.IsNullOrWhiteSpace(song.jacket) ? Resources.Load<Texture2D>(song.jacket) : null;
        if (art == null && song != null && !string.IsNullOrWhiteSpace(song.jacket))
        {
            Debug.LogWarning($"Chart '{song.id}': cover art 'Resources/{song.jacket}' not found; showing the station badge.", this);
        }

        coverImage.enabled = art != null;
        badgeTile.gameObject.SetActive(art == null);
        if (art == null)
        {
            return;
        }

        coverImage.texture = art;
        float aspect = (float)art.width / Mathf.Max(1, art.height);
        coverImage.uvRect = aspect >= 1f
            ? new Rect((1f - 1f / aspect) * 0.5f, 0f, 1f / aspect, 1f)
            : new Rect(0f, (1f - aspect) * 0.5f, 1f, aspect);
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

    private void RefreshScore()
    {
        if (score == null || percentText == null)
        {
            return;
        }

        percentText.text = FormatScore(score.Percent);
    }

    /// <summary>The achievement rate as the sign shows it: four decimals and a smaller %, e.g. 99.9999%.</summary>
    private static string FormatScore(float percent)
    {
        return $"{percent:0.0000}<size={PercentSize / 2}>%</size>";
    }

    /// <summary>The player's name, and the options that change how the song plays so a glance tells what is on.</summary>
    private void RefreshStatus()
    {
        if (statusText == null)
        {
            return;
        }

        playerText.text = GameSettings.PlayerName;

        string status = $"HI-SPEED x{GameSettings.HiSpeed:0.0}";
        if (GameSettings.Mirror)
        {
            status += "  ·  MIRROR";
        }

        if (GameSettings.AutoPlay)
        {
            status += $"  ·  <color=#{ColorUtility.ToHtmlStringRGB(new Color(1f, 0.42f, 0.42f))}>AUTO</color>";
        }

        statusText.text = status;
    }
}
