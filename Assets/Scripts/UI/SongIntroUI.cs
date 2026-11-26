using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen between the train doors and the song, like the display above a car's door
/// announcing the next stop: the service box (the chart's difficulty: 各停 / 快速 / 急行 /
/// 特急) top left, the artist and, large, "まもなく / Soon" and the song title, "metro" top
/// right, a rule in the difficulty colour, and the cover art (or the song's station badge)
/// below it. <see cref="GameFlow"/> shows it while the doors are shut, holds it for a few
/// seconds once they are open (the bottom rule fills meanwhile) and fades it out over the
/// stage as the song begins.
/// </summary>
public sealed class SongIntroUI : MonoBehaviour
{
    // Layout in canvas units from the top left (1920 x 1080 reference).
    private const float Margin = 64f;
    private const float HeaderHeight = 400f;
    private const float RuleHeight = 14f;
    private const float TitleX = 520f;
    private const float CoverSize = 520f;
    private const float FadeSeconds = 0.4f;

    private CanvasGroup group;
    private RoundedRectGraphic serviceBox;
    private Text serviceName;
    private Text serviceEnglish;
    private Text artistText;
    private Text titleText;
    private RectTransform titleBlock;
    private AngularGraphic headerRule;
    private AngularGraphic bottomTrack;
    private AngularGraphic bottomFill;
    private RectTransform coverRoot;
    private RawImage cover;
    private LineBadge stationBadge;
    private Text soonText;
    private Text soonEnglish;

    private float holdStart = -1f;
    private float holdSeconds = 1f;
    private float fadeStart = -1f;

    public void Build()
    {
        RectTransform root = (RectTransform)transform;
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        Vector2 topLeft = new Vector2(0f, 1f);
        Vector2 topRight = new Vector2(1f, 1f);

        // The display: a white header over a light grey content area.
        AngularGraphic content = UiKit.CreatePanel(root, "Content", SignKit.Floor);
        content.raycastTarget = true;
        UiKit.Stretch(content);
        AngularGraphic header = UiKit.CreatePanel(root, "Header", SignKit.Face);
        header.rectTransform.anchorMin = topLeft;
        header.rectTransform.anchorMax = topRight;
        header.rectTransform.pivot = new Vector2(0.5f, 1f);
        header.rectTransform.sizeDelta = new Vector2(0f, HeaderHeight);
        header.rectTransform.anchoredPosition = Vector2.zero;

        // Service box (the difficulty), as the train's type sits in the display's corner.
        serviceBox = SignKit.CreateRoundedRect(root, "ServiceBox", SignKit.MetroRed, 12f);
        UiKit.Place(serviceBox, topLeft, new Vector2(Margin, -48f), new Vector2(392f, 150f));
        serviceName = SignKit.CreateText(serviceBox.rectTransform, "Name", string.Empty, 76, TextAnchor.MiddleCenter, Color.white, true);
        UiKit.Place(serviceName, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(392f, 96f));
        serviceEnglish = SignKit.CreateText(serviceBox.rectTransform, "English", string.Empty, 26, TextAnchor.MiddleCenter, Color.white, true);
        UiKit.Place(serviceEnglish, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(392f, 38f));

        // "まもなく / Soon" under the box, the title beside it.
        soonText = SignKit.CreateText(root, "Soon", "まもなく", 36, TextAnchor.MiddleLeft, SignKit.InkSoft, true);
        UiKit.Place(soonText, topLeft, new Vector2(Margin + 4f, -208f), new Vector2(420f, 50f));
        soonEnglish = SignKit.CreateText(root, "SoonEnglish", "Soon", 92, TextAnchor.MiddleLeft, SignKit.Ink, true);
        UiKit.Place(soonEnglish, topLeft, new Vector2(Margin, -244f), new Vector2(420f, 120f));

        titleBlock = UiKit.CreateRect(root, "TitleBlock");
        titleBlock.anchorMin = topLeft;
        titleBlock.anchorMax = topRight;
        titleBlock.pivot = topLeft;
        titleBlock.offsetMin = new Vector2(TitleX, -HeaderHeight);
        titleBlock.offsetMax = new Vector2(-Margin, 0f);
        artistText = SignKit.CreateFittedText(titleBlock, "Artist", 42, 24, TextAnchor.MiddleLeft, SignKit.InkSoft, false);
        artistText.rectTransform.anchorMin = topLeft;
        artistText.rectTransform.anchorMax = topRight;
        artistText.rectTransform.pivot = topLeft;
        artistText.rectTransform.offsetMin = new Vector2(0f, -112f);
        artistText.rectTransform.offsetMax = new Vector2(-300f, -52f);
        titleText = SignKit.CreateFittedText(titleBlock, "SongTitle", 150, 56, TextAnchor.MiddleLeft, SignKit.Ink, true);
        titleText.rectTransform.anchorMin = topLeft;
        titleText.rectTransform.anchorMax = topRight;
        titleText.rectTransform.pivot = topLeft;
        titleText.rectTransform.offsetMin = new Vector2(0f, -380f);
        titleText.rectTransform.offsetMax = new Vector2(0f, -150f);

        // "metro" in the top right corner, with the red M badge.
        Text logo = SignKit.CreateText(root, "Logo", "metro", 64, TextAnchor.MiddleRight, SignKit.Ink);
        logo.font = SignKit.LogoFont;
        UiKit.Place(logo, topRight, new Vector2(-Margin, -40f), new Vector2(220f, 84f));
        LineBadge logoBadge = LineBadge.Create(root, "LogoBadge", SignKit.MetroRed, 64f);
        logoBadge.SetCode("M", null);
        logoBadge.Rect.anchorMin = topRight;
        logoBadge.Rect.anchorMax = topRight;
        logoBadge.Rect.anchoredPosition = new Vector2(-Margin - 220f, -86f);

        headerRule = SignKit.CreateBand(root, "HeaderRule", SignKit.MetroRed, HeaderHeight, RuleHeight);

        // The cover art in the middle of the content area, framed like a poster in the car.
        float contentTop = HeaderHeight + RuleHeight;
        float coverTop = contentTop + (1080f - contentTop - RuleHeight - CoverSize) * 0.5f;
        coverRoot = UiKit.CreateRect(root, "Cover");
        UiKit.Place(coverRoot, new Vector2(0.5f, 1f), new Vector2(0f, -coverTop), new Vector2(CoverSize, CoverSize));
        RoundedRectGraphic shadow = SignKit.CreateRoundedRect(coverRoot, "Shadow", new Color(0f, 0f, 0f, 0.18f), 14f);
        UiKit.Stretch(shadow, -6f, -16f, -10f, 0f);
        RoundedRectGraphic frame = SignKit.CreateRoundedRect(coverRoot, "Frame", Color.white, 8f);
        frame.SetVerticalGradient(SignKit.SteelLight, SignKit.SteelDark);
        UiKit.Stretch(frame, -10f, -10f, -10f, -10f);
        RoundedRectGraphic tile = SignKit.CreateRoundedRect(coverRoot, "Tile", SignKit.Plate, 2f);
        UiKit.Stretch(tile);
        stationBadge = LineBadge.Create(coverRoot, "StationBadge", SignKit.MetroRed, CoverSize * 0.62f);

        RectTransform coverRect = UiKit.CreateRect(coverRoot, "Art");
        UiKit.Stretch(coverRect);
        cover = coverRect.gameObject.AddComponent<RawImage>();
        cover.raycastTarget = false;

        // The bottom rule: a track that fills in the difficulty colour while the screen holds.
        bottomTrack = UiKit.CreatePanel(root, "BottomTrack", SignKit.SteelDark);
        PlaceBottom(bottomTrack.rectTransform);
        bottomTrack.rectTransform.anchorMax = new Vector2(1f, 0f);
        bottomFill = UiKit.CreatePanel(root, "BottomFill", SignKit.MetroRed);
        PlaceBottom(bottomFill.rectTransform);
    }

    /// <summary>Shows <paramref name="song"/> (the chart about to be played), still; <see cref="BeginHold"/> starts its time.</summary>
    public void Show(SongChartData song, SongGenre genre, string stationNumber)
    {
        gameObject.SetActive(true);
        holdStart = -1f;
        fadeStart = -1f;
        group.alpha = 1f;

        SongDifficulty difficulty = song.Difficulty;
        Color lineColor = difficulty.Color;
        // Dark lettering on a light colour (Rapid's yellow), white otherwise.
        Color letter = lineColor.grayscale > 0.6f ? SignKit.DarkInk : Color.white;
        serviceBox.color = lineColor;
        serviceName.color = letter;
        serviceEnglish.color = letter;
        serviceName.text = difficulty.JapaneseName;
        serviceEnglish.text = $"{difficulty.Name.ToUpperInvariant()}   LV {SongGenre.FormatStation(song.level)}";
        headerRule.color = lineColor;
        bottomFill.color = lineColor;

        artistText.text = string.IsNullOrWhiteSpace(song.artist) ? string.Empty : song.artist;
        titleText.text = song.title;

        Texture2D art = string.IsNullOrWhiteSpace(song.jacket) ? null : Resources.Load<Texture2D>(song.jacket);
        bool hasCover = art != null;
        cover.gameObject.SetActive(hasCover);
        stationBadge.gameObject.SetActive(!hasCover);
        if (hasCover)
        {
            // Cropped to the middle square, as on the HUD sign.
            cover.texture = art;
            float aspect = (float)art.width / Mathf.Max(1, art.height);
            cover.uvRect = aspect >= 1f
                ? new Rect((1f - 1f / aspect) * 0.5f, 0f, 1f / aspect, 1f)
                : new Rect(0f, (1f - aspect) * 0.5f, 1f, aspect);
        }
        else
        {
            stationBadge.SetLineColor(lineColor);
            stationBadge.SetCode(genre.Letter, stationNumber);
        }

        Animate();
    }

    /// <summary>Starts the hold (the doors are open): the bottom rule fills over <paramref name="seconds"/>.</summary>
    public void BeginHold(float seconds)
    {
        holdSeconds = Mathf.Max(0.01f, seconds);
        holdStart = Time.unscaledTime;
    }

    /// <summary>Fades the screen out over the stage and hides it.</summary>
    public void FadeOut()
    {
        if (gameObject.activeSelf && fadeStart < 0f)
        {
            fadeStart = Time.unscaledTime;
        }
    }

    public void Hide()
    {
        fadeStart = -1f;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        Animate();
        if (fadeStart >= 0f && Time.unscaledTime - fadeStart >= FadeSeconds)
        {
            Hide();
        }
    }

    private void Animate()
    {
        float held = holdStart < 0f ? 0f : Time.unscaledTime - holdStart;
        float progress = holdStart < 0f ? 0f : Mathf.Clamp01(held / holdSeconds);
        float width = ((RectTransform)transform).rect.width;
        bottomFill.rectTransform.sizeDelta = new Vector2(width * progress, RuleHeight);

        // Once the doors are open the title slides in like the display's text, and the cover settles.
        float enter = holdStart < 0f ? 0f : Ease.OutCubic(held / 0.5f);
        titleBlock.anchoredPosition = new Vector2(TitleX + Mathf.Lerp(60f, 0f, enter), 0f);
        coverRoot.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, enter);

        group.alpha = fadeStart < 0f ? 1f : 1f - Ease.InCubic((Time.unscaledTime - fadeStart) / FadeSeconds);
        group.blocksRaycasts = fadeStart < 0f;
    }

    private static void PlaceBottom(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, RuleHeight);
    }
}
