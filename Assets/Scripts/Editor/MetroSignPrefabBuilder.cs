using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generates the HUD sign prefab (<see cref="MetroSignHud"/>) and what it uses:
/// the layer sprites (Assets/UI/MetroSign/Sprites, regenerated every time), the
/// TextMeshPro font assets read from the OS fonts (Segoe UI with Yu Gothic UI behind
/// it, dynamic) and the lettering materials with their glow (Assets/UI/MetroSign/
/// Fonts, Materials; created once, so tuning them survives a rebuild). The prefab is
/// written to Assets/Resources/UI/MetroSign.prefab, replacing any edits made to it.
///
/// Layout at the 1920 x 1080 reference: a 146 px sign hanging 8 px below the top of
/// the screen, inside StageLayout.topInset.
/// </summary>
public static class MetroSignPrefabBuilder
{
    public const string AssetRoot = "Assets/UI/MetroSign";
    public const string PrefabPath = "Assets/Resources/" + MetroSignHud.ResourcePath + ".prefab";

    private const string SpriteFolder = AssetRoot + "/Sprites";
    private const string FontFolder = AssetRoot + "/Fonts";
    private const string MaterialFolder = AssetRoot + "/Materials";
    private const string LatinFontName = "MetroSign Latin SDF";
    private const string JapaneseFontName = "MetroSign Japanese SDF";

    // Geometry (reference pixels).
    private const float SignTop = 8f;
    private const float SignHeight = 146f;
    private const float SignSide = 20f;
    private const float FrameSide = 10f;
    private const float FrameTop = 10f;
    private const float FrameBottom = 13f;
    private const float FaceWidth = 1920f - 2f * (SignSide + FrameSide);
    private const float FaceHeight = SignHeight - FrameTop - FrameBottom;

    // Sections, after the hand-drawn layout: thumbnail, title / artist, level badge |
    // score | user badge, name / options, arrow panel (x from the face's left edge,
    // or negative from its right edge).
    private const float ThumbSize = 100f;
    private const float LevelX = 530f;
    private const float RuleSongScoreX = 610f;
    private const float RuleScorePlayerX = -725f;
    private const float PanelWidth = 196f;
    private const float PanelInset = 8f;

    // Colours.
    private static readonly Color FrameColor = new Color32(0x0D, 0x0E, 0x10, 0xFF);
    private static readonly Color HangerColor = new Color32(0x15, 0x16, 0x19, 0xFF);
    private static readonly Color FaceBottom = new Color32(0x05, 0x08, 0x0F, 0xFF);
    private static readonly Color FaceTop = new Color32(0x0B, 0x12, 0x20, 0xFF);
    private static readonly Color TileWhite = new Color32(0xE8, 0xE9, 0xEB, 0xFF);
    private static readonly Color ExitYellow = new Color32(0xF6, 0xD3, 0x00, 0xFF);
    private static readonly Color Ink = new Color32(0x14, 0x14, 0x16, 0xFF);
    private static readonly Color LetteringSoft = new Color32(0xC9, 0xCF, 0xD8, 0xFF);
    private static readonly Color PlaceholderLine = new Color32(0x00, 0x9B, 0x4C, 0xFF);

    private sealed class Sprites
    {
        public Sprite Rounded;
        public Sprite RoundedOutline;
        public Sprite SoftGlow;
        public Sprite InnerShadow;
        public Sprite Circle;
        public Sprite Ring;
        public Sprite RingThin;
        public Sprite SoftDot;
        public Sprite GradientVertical;
        public Sprite GradientDiagonal;
        public Sprite Streaks;
        public Sprite Arrow;
        public Sprite Person;
    }

    private sealed class Lettering
    {
        public TMP_FontAsset Font;
        public Material Glow;
        public Material Bright;
        public Material SoftGlow;
        public Material Ink;
    }

    [MenuItem("Metro/Build HUD Sign Prefab")]
    private static void BuildFromMenu()
    {
        if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog(
                "Build HUD Sign Prefab",
                $"{PrefabPath} already exists. Rebuilding replaces it (and any edits made to it). Fonts and lettering materials are kept.",
                "Rebuild",
                "Cancel"))
        {
            return;
        }

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Build());
    }

    /// <summary>Generates everything and writes the prefab; returns its path.</summary>
    public static string Build()
    {
        EnsureFolder(AssetRoot);
        EnsureFolder(SpriteFolder);
        EnsureFolder(FontFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));

        Sprites sprites = CreateSprites();
        Lettering lettering = CreateLettering();

        GameObject root = BuildSign(sprites, lettering);
        try
        {
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        return PrefabPath;
    }

    // ------------------------------------------------------------------ hierarchy

    private static GameObject BuildSign(Sprites sprites, Lettering lettering)
    {
        GameObject rootObject = new GameObject("MetroSign", typeof(RectTransform));
        RectTransform root = (RectTransform)rootObject.transform;
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.offsetMin = new Vector2(0f, -(SignTop + SignHeight));
        root.offsetMax = Vector2.zero;
        MetroSignHud hud = rootObject.AddComponent<MetroSignHud>();
        SerializedObject wiring = new SerializedObject(hud);

        // Hanger rods from the ceiling.
        RectTransform hangers = Group(root, "0_Hangers");
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? 0.12f : 0.88f;
            Image rod = CreateImage(hangers, side == 0 ? "HangerLeft" : "HangerRight", null, HangerColor);
            rod.rectTransform.anchorMin = new Vector2(x, 1f);
            rod.rectTransform.anchorMax = new Vector2(x, 1f);
            rod.rectTransform.pivot = new Vector2(0.5f, 1f);
            rod.rectTransform.sizeDelta = new Vector2(12f, SignTop + 6f);
            rod.rectTransform.anchoredPosition = Vector2.zero;
            Image edge = CreateImage(rod.rectTransform, "Highlight", null, new Color(1f, 1f, 1f, Overlay(0.12f)));
            edge.rectTransform.anchorMin = new Vector2(0f, 0f);
            edge.rectTransform.anchorMax = new Vector2(0f, 1f);
            edge.rectTransform.pivot = new Vector2(0f, 0.5f);
            edge.rectTransform.sizeDelta = new Vector2(2f, 0f);
            edge.rectTransform.anchoredPosition = new Vector2(2f, 0f);
        }

        RectTransform sign = CreateRect(root, "Sign");
        Stretch(sign, SignSide, 0f, SignSide, SignTop);

        // 1 Housing: black metal box, drop shadow behind it, light catching its edges.
        RectTransform housing = Group(sign, "1_Housing");
        Image shadow = CreateImage(housing, "DropShadow", sprites.SoftGlow, new Color(0f, 0f, 0f, 0.8f), Image.Type.Sliced, 1f);
        Stretch(shadow.rectTransform, -34f, -44f, -34f, -14f);
        Image frame = CreateImage(housing, "Frame", sprites.Rounded, FrameColor, Image.Type.Sliced, 3f);
        Stretch(frame.rectTransform);
        Image frameSheen = CreateImage(housing, "FrameSheen", sprites.GradientVertical, new Color(1f, 1f, 1f, Overlay(0.07f)));
        Stretch(frameSheen.rectTransform, 2f, SignHeight * 0.45f, 2f, 1f);
        Image topEdge = CreateImage(housing, "FrameTopEdge", null, new Color(1f, 1f, 1f, Overlay(0.16f)));
        StretchTop(topEdge.rectTransform, 5f, 5f, 1f, 1.5f);
        Image innerHighlight = CreateImage(housing, "InnerEdgeHighlight", sprites.RoundedOutline, new Color(1f, 1f, 1f, Overlay(0.2f)), Image.Type.Sliced, 4f);
        Stretch(innerHighlight.rectTransform, FrameSide - 1.5f, FrameBottom - 1.5f, FrameSide - 1.5f, FrameTop - 1.5f);

        // 2 Face: the backlit panel, never quite black.
        RectTransform face = Group(sign, "2_Face");
        Stretch(face, FrameSide, FrameBottom, FrameSide, FrameTop);
        Image faceBase = CreateImage(face, "FaceBase", null, FaceBottom);
        Stretch(faceBase.rectTransform);
        Emit(faceBase, Color.white);
        Image faceGradient = CreateImage(face, "FaceGradient", sprites.GradientVertical, FaceTop);
        Stretch(faceGradient.rectTransform);
        Emit(faceGradient, Color.white);
        Image innerShadow = CreateImage(face, "InnerShadow", sprites.InnerShadow, new Color(0f, 0f, 0f, 0.55f), Image.Type.Sliced, 2f);
        Stretch(innerShadow.rectTransform);

        Vector2 left = new Vector2(0f, 0.5f);
        Vector2 right = new Vector2(1f, 0.5f);
        Vector2 centre = new Vector2(0.5f, 0.5f);

        // 3 Rules: thick white lit rules between the sections, with a halo.
        RectTransform rules = Group(face, "3_Rules");
        CreateRule(rules, sprites, "RuleSongScore", left, RuleSongScoreX);
        CreateRule(rules, sprites, "RuleScorePlayer", right, RuleScorePlayerX);

        // 4 Song: the thumbnail (cover art, or the station badge on a white tile; unlit),
        // title and artist, and the level as a lit line badge.
        RectTransform song = Group(face, "4_Song");
        Vector2 thumbCentre = new Vector2(16f + ThumbSize * 0.5f, 0f);
        RectTransform badge = CreateRect(song, "StationTile");
        Place(badge, left, centre, thumbCentre, new Vector2(ThumbSize, ThumbSize));
        Image tile = CreateImage(badge, "Tile", sprites.Rounded, TileWhite, Image.Type.Sliced, 3f);
        Stretch(tile.rectTransform);
        Image ring = CreateImage(badge, "Ring", sprites.Circle, PlaceholderLine);
        Place(ring.rectTransform, centre, centre, Vector2.zero, new Vector2(80f, 80f));
        Image ringFace = CreateImage(badge, "Face", sprites.Circle, TileWhite);
        Place(ringFace.rectTransform, centre, centre, Vector2.zero, new Vector2(54f, 54f));
        TextMeshProUGUI badgeLetter = CreateText(badge, "Letter", "G", 19f, TextAlignmentOptions.Midline, lettering, lettering.Ink, Ink, bold: true);
        Place(badgeLetter.rectTransform, centre, centre, new Vector2(0f, 12f), new Vector2(52f, 24f));
        TextMeshProUGUI badgeNumber = CreateText(badge, "Number", "01", 26f, TextAlignmentOptions.Midline, lettering, lettering.Ink, Ink, bold: true);
        Place(badgeNumber.rectTransform, centre, centre, new Vector2(0f, -9f), new Vector2(52f, 30f));

        RectTransform coverRect = CreateRect(song, "Cover");
        Place(coverRect, left, centre, thumbCentre, new Vector2(ThumbSize, ThumbSize));
        RawImage cover = coverRect.gameObject.AddComponent<RawImage>();
        cover.raycastTarget = false;
        coverRect.gameObject.SetActive(false);

        float textLeft = 16f + ThumbSize + 18f;
        float textWidth = LevelX - 49f - 14f - textLeft;
        TextMeshProUGUI title = CreateText(song, "Title", "Song Title", 42f, TextAlignmentOptions.MidlineLeft, lettering, lettering.Bright, Color.white, 22f);
        Place(title.rectTransform, left, left, new Vector2(textLeft, 16f), new Vector2(textWidth, 54f));
        TextMeshProUGUI artist = CreateText(song, "Artist", "Artist", 24f, TextAlignmentOptions.MidlineLeft, lettering, lettering.SoftGlow, LetteringSoft, 15f);
        Place(artist.rectTransform, left, left, new Vector2(textLeft + 2f, -27f), new Vector2(textWidth, 32f));

        Vector2 levelCentre = new Vector2(LevelX, 0f);
        Image levelBleed = CreateImage(song, "LevelBleed", sprites.SoftDot, WithAlpha(PlaceholderLine, 0.35f));
        Place(levelBleed.rectTransform, left, centre, levelCentre, new Vector2(160f, 160f));
        Emit(levelBleed, Hdr(Color.white, 0.6f));
        RectTransform level = CreateRect(song, "LevelBadge");
        Place(level, left, centre, levelCentre, new Vector2(98f, 98f));
        Image levelRing = CreateImage(level, "Ring", sprites.Circle, PlaceholderLine);
        Place(levelRing.rectTransform, centre, centre, Vector2.zero, new Vector2(86f, 86f));
        Emit(levelRing, Hdr(Color.white, 0.7f));
        Image levelFace = CreateImage(level, "Face", sprites.Circle, Color.white);
        Place(levelFace.rectTransform, centre, centre, Vector2.zero, new Vector2(58f, 58f));
        Emit(levelFace, Hdr(Color.white, 0.6f));
        TextMeshProUGUI levelLabel = CreateText(level, "Label", "LV", 19f, TextAlignmentOptions.Midline, lettering, lettering.Ink, Ink, bold: true);
        Place(levelLabel.rectTransform, centre, centre, new Vector2(0f, 13f), new Vector2(56f, 24f));
        TextMeshProUGUI levelNumber = CreateText(level, "Number", "07", 28f, TextAlignmentOptions.Midline, lettering, lettering.Ink, Ink, bold: true);
        Place(levelNumber.rectTransform, centre, centre, new Vector2(0f, -9f), new Vector2(56f, 32f));

        // 5 Score: lit digits, and the song's progress lit along the rule under them.
        RectTransform score = Group(face, "5_Score");
        float scoreX = (RuleSongScoreX + FaceWidth + RuleScorePlayerX) * 0.5f;
        TextMeshProUGUI scoreText = CreateText(score, "Percent", "0.0000<size=50%>%</size>", 76f, TextAlignmentOptions.Midline, lettering, lettering.Bright, Color.white);
        Place(scoreText.rectTransform, left, centre, new Vector2(scoreX, 8f), new Vector2(440f, 84f));
        Image trackGlow = CreateImage(score, "ProgressGlow", sprites.SoftDot, new Color(1f, 1f, 1f, Overlay(0.12f)));
        Place(trackGlow.rectTransform, left, centre, new Vector2(scoreX, -46f), new Vector2(470f, 26f));
        Emit(trackGlow, Hdr(Color.white, 0.6f));
        Image track = CreateImage(score, "ProgressTrack", null, new Color(0.54f, 0.57f, 0.62f, 0.55f));
        Place(track.rectTransform, left, centre, new Vector2(scoreX, -46f), new Vector2(420f, 5f));
        Image fill = CreateImage(track.rectTransform, "Fill", null, PlaceholderLine);
        Stretch(fill.rectTransform);
        fill.rectTransform.anchorMax = new Vector2(0.4f, 1f);
        Emit(fill, Hdr(Color.white, 0.9f));

        // 6 Player: the user badge in a lit line-colour ring, the name and the options in effect.
        RectTransform player = Group(face, "6_Player");
        Vector2 userCentre = new Vector2(RuleScorePlayerX + 30f + 44f, 0f);
        RectTransform user = CreateRect(player, "UserBadge");
        Place(user, right, centre, userCentre, new Vector2(88f, 88f));
        Image userBleed = CreateImage(player, "UserBleed", sprites.SoftDot, WithAlpha(PlaceholderLine, 0.35f));
        Place(userBleed.rectTransform, right, centre, userCentre, new Vector2(160f, 160f));
        Emit(userBleed, Hdr(Color.white, 0.6f));
        userBleed.transform.SetSiblingIndex(user.GetSiblingIndex());
        Image userRing = CreateImage(user, "Ring", sprites.Ring, PlaceholderLine);
        Place(userRing.rectTransform, centre, centre, Vector2.zero, new Vector2(86f, 86f));
        Emit(userRing, Hdr(Color.white, 0.7f));
        Image userIcon = CreateImage(user, "Icon", sprites.Person, LetteringSoft);
        Place(userIcon.rectTransform, centre, centre, new Vector2(0f, -2f), new Vector2(52f, 52f));

        float nameLeft = userCentre.x + 44f + 18f;
        float nameWidth = -PanelInset - PanelWidth - 18f - nameLeft;
        TextMeshProUGUI playerName = CreateText(player, "PlayerName", "PLAYER", 40f, TextAlignmentOptions.MidlineLeft, lettering, lettering.Bright, Color.white, 22f);
        Place(playerName.rectTransform, right, left, new Vector2(nameLeft, 16f), new Vector2(nameWidth, 54f));
        TextMeshProUGUI playerStatus = CreateText(player, "PlayerStatus", "HI-SPEED x1.0", 20f, TextAlignmentOptions.MidlineLeft, lettering, lettering.SoftGlow, LetteringSoft, 12f);
        Place(playerStatus.rectTransform, right, left, new Vector2(nameLeft + 2f, -27f), new Vector2(nameWidth, 28f));

        // 7 Arrow panel: a lit yellow panel with square corners, the arrow in black on it.
        RectTransform exit = Group(face, "7_ArrowPanel");
        Vector2 panelCentre = new Vector2(-PanelInset - PanelWidth * 0.5f, 0f);
        Vector2 panelSize = new Vector2(PanelWidth, FaceHeight - 14f);
        Image panelBleed = CreateImage(exit, "FrameBleed", sprites.SoftGlow, WithAlpha(ExitYellow, 0.3f), Image.Type.Sliced, 1f);
        Place(panelBleed.rectTransform, right, centre, panelCentre, panelSize + new Vector2(90f, 60f));
        Emit(panelBleed, Hdr(Color.white, 0.4f));
        Image panel = CreateImage(exit, "Panel", null, Color.white);
        Place(panel.rectTransform, right, centre, panelCentre, panelSize);
        Emit(panel, Hdr(ExitYellow, 1.0f));
        Image arrow = CreateImage(exit, "Arrow", sprites.Arrow, Ink);
        arrow.preserveAspect = true;
        Place(arrow.rectTransform, right, centre, panelCentre, new Vector2(84f, 84f));

        // 8 Glass in front: a faint sheen from the top left, a weak reflection, light on its top edge.
        RectTransform glass = Group(face, "8_Glass");
        Image glassGradient = CreateImage(glass, "GlassGradient", sprites.GradientDiagonal, new Color(1f, 1f, 1f, Overlay(0.08f)));
        Stretch(glassGradient.rectTransform);
        Image glassStreaks = CreateImage(glass, "GlassReflection", sprites.Streaks, new Color(1f, 1f, 1f, Overlay(0.035f)));
        Stretch(glassStreaks.rectTransform);
        Image glassEdge = CreateImage(glass, "GlassTopEdge", null, new Color(1f, 1f, 1f, Overlay(0.1f)));
        StretchTop(glassEdge.rectTransform, 0f, 0f, 0f, 2f);

        Wire(wiring, "badge", badge.gameObject);
        Wire(wiring, "badgeRing", ring);
        Wire(wiring, "badgeLetter", badgeLetter);
        Wire(wiring, "badgeNumber", badgeNumber);
        Wire(wiring, "cover", cover);
        Wire(wiring, "titleText", title);
        Wire(wiring, "artistText", artist);
        Wire(wiring, "levelRing", levelRing);
        Wire(wiring, "levelBleed", levelBleed);
        Wire(wiring, "levelNumber", levelNumber);
        Wire(wiring, "userRing", userRing);
        Wire(wiring, "userBleed", userBleed);
        Wire(wiring, "scoreText", scoreText);
        Wire(wiring, "progressFill", fill.rectTransform);
        Wire(wiring, "progressFillGraphic", fill);
        Wire(wiring, "playerName", playerName);
        Wire(wiring, "playerStatus", playerStatus);
        wiring.ApplyModifiedPropertiesWithoutUndo();

        foreach (Transform child in rootObject.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = 5;
        }

        return rootObject;
    }

    private static void Wire(SerializedObject wiring, string field, UnityEngine.Object value)
    {
        SerializedProperty property = wiring.FindProperty(field);
        if (property == null)
        {
            throw new InvalidOperationException($"MetroSignHud has no field '{field}'.");
        }

        property.objectReferenceValue = value;
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    /// <summary>A layer group filling its parent.</summary>
    private static RectTransform Group(Transform parent, string name)
    {
        RectTransform rect = CreateRect(parent, name);
        Stretch(rect);
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Sprite sprite, Color color, Image.Type type = Image.Type.Simple, float pixelsPerUnitMultiplier = 1f)
    {
        RectTransform rect = CreateRect(parent, name);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null ? type : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float size, TextAlignmentOptions alignment, Lettering lettering, Material material, Color color, float autoSizeMin = 0f, bool bold = false)
    {
        RectTransform rect = CreateRect(parent, name);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = lettering.Font;
        text.fontSharedMaterial = material;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = true;
        text.raycastTarget = false;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        if (autoSizeMin > 0f)
        {
            text.enableAutoSizing = true;
            text.fontSizeMax = size;
            text.fontSizeMin = autoSizeMin;
        }

        text.text = content;
        return text;
    }

    private static SignEmission Emit(Graphic graphic, Color emission)
    {
        SignEmission emissive = graphic.gameObject.AddComponent<SignEmission>();
        emissive.Emission = emission;
        return emissive;
    }

    /// <summary><paramref name="color"/> made brighter by <paramref name="stops"/> exposure stops, as the HDR picker's Intensity.</summary>
    private static Color Hdr(Color color, float stops)
    {
        float scale = Mathf.Pow(2f, stops);
        return new Color(color.r * scale, color.g * scale, color.b * scale, 1f);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    /// <summary>
    /// The opacity a white overlay needs over the near-black sign to look like
    /// <paramref name="gammaAlpha"/> would in gamma space. The project blends in linear
    /// space, where a faint white layer over black comes out several times brighter
    /// (8 % white reads as about 30 % grey), so the glass and edge highlights use this.
    /// </summary>
    private static float Overlay(float gammaAlpha)
    {
        const float background = 0.06f;
        float target = Mathf.GammaToLinearSpace(gammaAlpha + (1f - gammaAlpha) * background);
        float under = Mathf.GammaToLinearSpace(background);
        return Mathf.Clamp01((target - under) / (1f - under));
    }

    /// <summary>Hangs <paramref name="rect"/> from <paramref name="anchor"/> by its <paramref name="pivot"/>, offset by <paramref name="position"/>.</summary>
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>A strip <paramref name="height"/> high along the top of the parent, <paramref name="top"/> below it.</summary>
    private static void StretchTop(RectTransform rect, float left, float right, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    // ------------------------------------------------------------------ lettering

    private static Lettering CreateLettering()
    {
        TMP_FontAsset japanese = LoadOrCreateFont(JapaneseFontName, "Yu Gothic UI", "Bold", null);
        TMP_FontAsset latin = LoadOrCreateFont(LatinFontName, "Segoe UI", "Semibold", japanese);

        return new Lettering
        {
            Font = latin,
            Glow = LoadOrCreateTextMaterial(latin, "Glow", Hdr(new Color(0.94f, 0.96f, 1f), 0.3f), new Color(0.78f, 0.87f, 1f, 0.45f), 0.4f),
            Bright = LoadOrCreateTextMaterial(latin, "Bright", Hdr(new Color(0.96f, 0.98f, 1f), 0.8f), new Color(0.85f, 0.92f, 1f, 0.5f), 0.4f),
            SoftGlow = LoadOrCreateTextMaterial(latin, "Soft Glow", Hdr(Color.white, 0.1f), new Color(0.78f, 0.87f, 1f, 0.25f), 0.3f),
            Ink = LoadOrCreateTextMaterial(latin, "Ink", Color.white, Color.clear, 0f),
        };
    }

    /// <summary>A dynamic font asset read from an installed OS font (not bundled), saved with its atlas and material.</summary>
    private static TMP_FontAsset LoadOrCreateFont(string name, string family, string style, TMP_FontAsset fallback)
    {
        string path = $"{FontFolder}/{name}.asset";
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
        {
            font = TMP_FontAsset.CreateFontAsset(family, style) ?? TMP_FontAsset.CreateFontAsset(family, "Bold") ?? TMP_FontAsset.CreateFontAsset(family, "Regular");
            if (font == null)
            {
                throw new InvalidOperationException($"OS font '{family}' was not found.");
            }

            font.name = name;
            AssetDatabase.CreateAsset(font, path);
            Texture2D atlas = font.atlasTextures[0];
            atlas.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, font);
            font.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
        }

        if (fallback != null && (font.fallbackFontAssetTable == null || !font.fallbackFontAssetTable.Contains(fallback)))
        {
            font.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
        }

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        return font;
    }

    /// <summary>
    /// A material preset for <paramref name="font"/> (named after it, so TextMeshPro's
    /// Material Preset menu lists it) on the full Distance Field shader, with an HDR
    /// face colour and an outer glow. Kept if it already exists.
    /// </summary>
    private static Material LoadOrCreateTextMaterial(TMP_FontAsset font, string preset, Color face, Color glow, float glowOuter)
    {
        string path = $"{MaterialFolder}/{font.name} - {preset}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        material = new Material(font.material)
        {
            name = $"{font.name} - {preset}",
            shader = Shader.Find("TextMeshPro/Distance Field"),
        };
        material.SetColor(ShaderUtilities.ID_FaceColor, face);
        if (glowOuter > 0f)
        {
            material.EnableKeyword(ShaderUtilities.Keyword_Glow);
            material.SetColor(ShaderUtilities.ID_GlowColor, glow);
            material.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
            material.SetFloat(ShaderUtilities.ID_GlowInner, 0.05f);
            material.SetFloat(ShaderUtilities.ID_GlowOuter, glowOuter);
            material.SetFloat(ShaderUtilities.ID_GlowPower, 1f);
        }
        else
        {
            material.DisableKeyword(ShaderUtilities.Keyword_Glow);
        }

        ShaderUtilities.UpdateShaderRatios(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // ------------------------------------------------------------------ sprites

    private static Sprites CreateSprites()
    {
        return new Sprites
        {
            Rounded = Bake("Rounded", 64, 64, 1, 20, false, (x, y) => Fill(RoundedRect(x, y, 32f, 32f, 32f, 32f, 16f))),
            RoundedOutline = Bake("RoundedOutline", 64, 64, 1, 20, false, (x, y) =>
            {
                float d = RoundedRect(x, y, 32f, 32f, 32f, 32f, 16f);
                return Fill(d) * Mathf.Clamp01(d + 4f + 0.5f);
            }),
            SoftGlow = Bake("SoftGlow", 128, 128, 1, 60, false, (x, y) =>
            {
                float d = RoundedRect(x, y, 64f, 64f, 20f, 20f, 12f);
                return d <= 0f ? 1f : Mathf.Exp(-d * d / (2f * 14f * 14f));
            }),
            InnerShadow = Bake("InnerShadow", 64, 64, 1, 28, false, (x, y) =>
            {
                float edge = Mathf.Min(Mathf.Min(x, 64f - x), Mathf.Min(y, 64f - y));
                return Mathf.Pow(1f - Mathf.Clamp01(edge / 24f), 2.2f);
            }),
            Circle = Bake("Circle", 128, 128, 1, 0, true, (x, y) => Fill(Radius(x, y, 64f) - 63f)),
            Ring = Bake("Ring", 128, 128, 1, 0, true, (x, y) =>
            {
                float r = Radius(x, y, 64f);
                return Fill(r - 63f) * Mathf.Clamp01(r - (63f - 9f) + 0.5f);
            }),
            RingThin = Bake("RingThin", 128, 128, 1, 0, true, (x, y) =>
            {
                float r = Radius(x, y, 64f);
                return Fill(r - 63f) * Mathf.Clamp01(r - (63f - 5f) + 0.5f);
            }),
            SoftDot = Bake("SoftDot", 128, 128, 1, 0, false, (x, y) =>
            {
                float r = Radius(x, y, 64f);
                return Mathf.Exp(-r * r / (2f * 22f * 22f));
            }),
            GradientVertical = Bake("GradientVertical", 4, 128, 1, 0, false, (x, y) => y / 128f),
            GradientDiagonal = Bake("GradientDiagonal", 128, 128, 1, 0, false, (x, y) =>
            {
                float t = Mathf.Clamp01(1f - (x / 128f + (1f - y / 128f)) * 0.5f);
                return Mathf.Pow(t, 1.4f);
            }),
            Streaks = Bake("GlassReflection", 512, 64, 1, 0, false, (x, y) =>
            {
                // Two soft bands leaning like a reflection on the glass (steep once stretched over the face).
                float lean = x - y / 3.3f;
                float wide = Mathf.Exp(-Mathf.Pow((lean - 300f) / 18f, 2f));
                float thin = 0.7f * Mathf.Exp(-Mathf.Pow((lean - 346f) / 5f, 2f));
                return (wide + thin) * (0.55f + 0.45f * y / 64f);
            }),
            Arrow = Bake("Arrow", 256, 256, 4, 0, true, (x, y) => ArrowCoverage(x / 256f, y / 256f)),
            Person = Bake("Person", 128, 128, 4, 0, true, (x, y) => PersonCoverage(x / 128f, y / 128f)),
        };
    }

    /// <summary>The sign's arrow pointing up and to the right: a diagonal shaft and an open, square-cornered head.</summary>
    private static float ArrowCoverage(float u, float v)
    {
        const float corner = 0.88f;
        const float stroke = 0.13f;
        const float arm = 0.48f;
        bool topArm = u >= corner - arm && u <= corner && v >= corner - stroke && v <= corner;
        bool sideArm = u >= corner - stroke && u <= corner && v >= corner - arm && v <= corner;

        Vector2 start = new Vector2(0.1f, 0.1f);
        Vector2 direction = new Vector2(0.70710678f, 0.70710678f);
        Vector2 offset = new Vector2(u, v) - start;
        float along = Vector2.Dot(offset, direction);
        float across = Mathf.Abs(offset.x * -direction.y + offset.y * direction.x);
        bool shaft = along >= 0f && along <= (corner - stroke * 0.5f - 0.1f) * 1.41421356f && across <= stroke * 0.5f;
        return topArm || sideArm || shaft ? 1f : 0f;
    }

    /// <summary>A user silhouette: a round head over rounded shoulders, with a gap between them.</summary>
    private static float PersonCoverage(float u, float v)
    {
        float fromHead = new Vector2(u - 0.5f, v - 0.68f).magnitude;
        Vector2 body = new Vector2((u - 0.5f) / 0.36f, (v - 0.12f) / 0.34f);
        bool head = fromHead <= 0.19f;
        bool shoulders = v >= 0.06f && body.magnitude <= 1f && fromHead > 0.25f;
        return head || shoulders ? 1f : 0f;
    }

    /// <summary>A thick lit white line with a soft halo behind it, hung from <paramref name="anchor"/> at <paramref name="x"/>.</summary>
    private static void CreateRule(Transform parent, Sprites sprites, string name, Vector2 anchor, float x)
    {
        Vector2 centre = new Vector2(0.5f, 0.5f);
        float height = FaceHeight - 22f;
        Image halo = CreateImage(parent, name + "Halo", sprites.SoftDot, new Color(1f, 1f, 1f, Overlay(0.18f)));
        Place(halo.rectTransform, anchor, centre, new Vector2(x, 0f), new Vector2(48f, height + 40f));
        Emit(halo, Hdr(Color.white, 0.6f));
        Image line = CreateImage(parent, name, null, Color.white);
        Place(line.rectTransform, anchor, centre, new Vector2(x, 0f), new Vector2(7f, height));
        Emit(line, Hdr(Color.white, 0.6f));
    }

    private static float Fill(float distance)
    {
        return Mathf.Clamp01(0.5f - distance);
    }

    private static float Radius(float x, float y, float centre)
    {
        return new Vector2(x - centre, y - centre).magnitude;
    }

    /// <summary>Signed distance (pixels) from a rounded rectangle; negative inside.</summary>
    private static float RoundedRect(float x, float y, float cx, float cy, float halfWidth, float halfHeight, float radius)
    {
        float qx = Mathf.Abs(x - cx) - halfWidth + radius;
        float qy = Mathf.Abs(y - cy) - halfHeight + radius;
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    /// <summary>
    /// Writes a white sprite whose alpha is <paramref name="alpha"/>(x, y) in pixels (y up,
    /// sampled at pixel centres, <paramref name="supersample"/>² samples per pixel) and imports
    /// it for UI: no compression, clamped, 9-slice <paramref name="border"/> when it is above 0.
    /// </summary>
    private static Sprite Bake(string name, int width, int height, int supersample, int border, bool mipmaps, Func<float, float, float> alpha)
    {
        Color32[] pixels = new Color32[width * height];
        float step = 1f / supersample;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                for (int sy = 0; sy < supersample; sy++)
                {
                    for (int sx = 0; sx < supersample; sx++)
                    {
                        sum += alpha(x + (sx + 0.5f) * step, y + (sy + 0.5f) * step);
                    }
                }

                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(sum / (supersample * supersample)) * 255f);
                pixels[y * width + x] = new Color32(255, 255, 255, a);
            }
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        string path = $"{SpriteFolder}/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = mipmaps;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = 100f;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteBorder = new Vector4(border, border, border, border);
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
