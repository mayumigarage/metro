using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The station-signage look of every screen (title, genre select, song select, the
/// selected-song sign, the train doors, and in the song the HUD boards, pause, result
/// and options): palette, font and builders for the procedural shapes.
/// Sizes follow real platform signs: line badges with a ring 17.5% of the diameter,
/// bold black lettering on an off-white face, a steel frame around every board.
/// </summary>
public static class SignKit
{
    /// <summary>The menus' dim over whatever is behind a modal board (song sign, pause, result, options).</summary>
    public static readonly Color Dim = new Color(0.02f, 0.02f, 0.03f, 0.93f);

    /// <summary>Footer key hints under the boards.</summary>
    public static readonly Color Hint = new Color(1f, 1f, 1f, 0.75f);

    // The signs' palette, in the theme of the THEME option: black lettering on an off-white
    // face, or light lettering on a black one. Read when a screen is built or redrawn, so
    // never keep one of these in a static field.
    public static Color Face => Themed(0xF7F7F3, 0x050505);
    public static Color Ink => Themed(0x1B1C1F, 0xEDEDE8);
    public static Color InkSoft => Themed(0x4B4F55, 0xBDC1C7);
    public static Color InkDim => Themed(0x8C9198, 0x7C818A);
    public static Color Watermark => Themed(0xECECE7, 0x161616);
    public static Color SteelLight => Themed(0xDDE0E4, 0x44484F);
    public static Color SteelDark => Themed(0x9CA1A9, 0x23262B);
    public static Color Disabled => Themed(0xBCC0C6, 0x43464D);

    /// <summary>Lettering and marks on an <see cref="Ink"/> fill (the selected row or key).</summary>
    public static Color OnInk => Themed(0xFFFFFF, 0x0A0A0C);

    /// <summary>The inside of line badges, the start circle and the cover tile.</summary>
    public static Color Plate => Themed(0xFFFFFF, 0x101010);

    /// <summary>The song intro's content area under its header.</summary>
    public static Color Floor => Themed(0xE4E6EA, 0x161616);

    /// <summary>Black lettering for light line colours (Rapid's yellow), the same in both themes.</summary>
    public static readonly Color DarkInk = new Color32(0x1B, 0x1C, 0x1F, 0xFF);

    public static readonly Color Night = new Color32(0x0A, 0x0A, 0x0C, 0xFF);
    public static readonly Color MetroRed = new Color32(0xE6, 0x00, 0x12, 0xFF);

    /// <summary>The 0xRRGGBB colour of the current theme.</summary>
    public static Color Themed(int light, int dark)
    {
        int rgb = GameSettings.DarkMode ? dark : light;
        return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xFF);
    }

    /// <summary>
    /// Legacy Text centres the font's whole line box, which puts capitals and figures
    /// about this much of the font size below the middle of their rect. Raise text by
    /// fontSize * CapRaise where it has to sit centred (badges, code boxes).
    /// </summary>
    public const float CapRaise = 0.19f;

    private static Font signFont;
    private static Font logoFont;

    /// <summary>A Frutiger-like face for Latin with a Japanese gothic behind it, as on station signs.</summary>
    public static Font Font
    {
        get
        {
            if (signFont == null)
            {
                signFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Yu Gothic UI", "Meiryo", "Arial" }, 32);
                if (signFont == null)
                {
                    signFont = UiKit.MainFont;
                }
            }

            return signFont;
        }
    }

    /// <summary>The lowercase "metro" of the title screen.</summary>
    public static Font LogoFont
    {
        get
        {
            if (logoFont == null)
            {
                logoFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Semibold", "Segoe UI", "Arial" }, 64);
                if (logoFont == null)
                {
                    logoFont = Font;
                }
            }

            return logoFont;
        }
    }

    /// <summary>
    /// The title logo's "metro": Outfit ExtraLight when its TTF is at
    /// Resources/Fonts/Outfit-ExtraLight (or the font is installed), else Segoe UI Light.
    /// </summary>
    public static Font TitleFont
    {
        get
        {
            if (titleFont == null)
            {
                titleFont = Resources.Load<Font>("Fonts/Outfit-ExtraLight");
                if (titleFont == null)
                {
                    titleFont = Font.CreateDynamicFontFromOSFont(new[] { "Outfit ExtraLight", "Outfit", "Segoe UI Light", "Segoe UI" }, 64);
                }

                if (titleFont == null)
                {
                    titleFont = Font;
                }
            }

            return titleFont;
        }
    }

    private static Font titleFont;

    public static Text CreateText(Transform parent, string name, string content, int size, TextAnchor alignment, Color color, bool bold = false)
    {
        Text text = UiKit.CreateText(parent, name, content, size, alignment, color, bold);
        text.font = Font;
        return text;
    }

    /// <summary>A one-line text that shrinks (down to <paramref name="minSize"/>) to fit its rect's width.</summary>
    public static Text CreateFittedText(Transform parent, string name, int size, int minSize, TextAnchor alignment, Color color, bool bold)
    {
        Text text = CreateText(parent, name, string.Empty, size, alignment, color, bold);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = minSize;
        text.resizeTextMaxSize = size;
        return text;
    }

    public static RoundedRectGraphic CreateRoundedRect(Transform parent, string name, Color color, float radius = 0f, float border = 0f)
    {
        RectTransform rect = UiKit.CreateRect(parent, name);
        RoundedRectGraphic graphic = rect.gameObject.AddComponent<RoundedRectGraphic>();
        graphic.color = color;
        graphic.raycastTarget = false;
        graphic.Radius = radius;
        graphic.BorderWidth = border;
        return graphic;
    }

    /// <summary>A disc (thickness 0) or a ring; fills its rect, so place or stretch it.</summary>
    public static CircleGraphic CreateCircle(Transform parent, string name, Color color, float thickness = 0f)
    {
        RectTransform rect = UiKit.CreateRect(parent, name);
        CircleGraphic graphic = rect.gameObject.AddComponent<CircleGraphic>();
        graphic.color = color;
        graphic.raycastTarget = false;
        graphic.Thickness = thickness;
        return graphic;
    }

    public static ChevronGraphic CreateChevron(Transform parent, string name, Color color, float thickness)
    {
        RectTransform rect = UiKit.CreateRect(parent, name);
        ChevronGraphic graphic = rect.gameObject.AddComponent<ChevronGraphic>();
        graphic.color = color;
        graphic.raycastTarget = false;
        graphic.Thickness = thickness;
        return graphic;
    }

    /// <summary>
    /// A sign board filling <paramref name="parent"/>: a brushed-steel frame
    /// <paramref name="frame"/> wide around an off-white face. Returns the face.
    /// </summary>
    public static RoundedRectGraphic CreateBoard(Transform parent, string name, float frame, float radius)
    {
        RectTransform root = UiKit.CreateRect(parent, name);
        UiKit.Stretch(root);

        RoundedRectGraphic steel = CreateRoundedRect(root, "Frame", Color.white, radius);
        steel.SetVerticalGradient(SteelLight, SteelDark);
        UiKit.Stretch(steel);

        RoundedRectGraphic face = CreateRoundedRect(root, "Face", Face, Mathf.Max(2f, radius - frame * 0.6f));
        UiKit.Stretch(face, frame, frame, frame, frame);

        // A thin shadow line where the face meets the frame.
        RoundedRectGraphic seam = CreateRoundedRect(root, "Seam", UiKit.WithAlpha(Ink, GameSettings.DarkMode ? 0.04f : 0.22f), Mathf.Max(2f, radius - frame * 0.6f), 2f);
        UiKit.Stretch(seam, frame, frame, frame, frame);
        return face;
    }

    /// <summary>
    /// Judgement colours that read on the off-white face (the HUD's popup colours are
    /// pastel for the dark stage). Same hues: yellow, blue, green, red.
    /// </summary>
    public static Color GetJudgementColor(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect:
                return new Color32(0xF0, 0xA8, 0x00, 0xFF);
            case Judgement.Great:
                return new Color32(0x00, 0x8F, 0xD5, 0xFF);
            case Judgement.Good:
                return new Color32(0x00, 0xA6, 0x5A, 0xFF);
            default:
                return MetroRed;
        }
    }

    /// <summary>
    /// A sign label: Japanese name with the English one after it, smaller, as on the
    /// selected-song sign ("ベスト  Best").
    /// </summary>
    public static string Bilingual(string japanese, string english, int englishSize)
    {
        return $"{japanese}  <size={englishSize}>{english}</size>";
    }

    /// <summary>
    /// A station-code box ("M04"): a rounded outline with centred bold text. Returns the text;
    /// the box is its parent, placed with <paramref name="size"/> at <paramref name="anchor"/>.
    /// </summary>
    public static Text CreateCodeBox(Transform parent, string name, Color color, int fontSize, float border, Vector2 anchor, Vector2 position, Vector2 size)
    {
        RoundedRectGraphic box = CreateRoundedRect(parent, name, color, Mathf.Min(10f, size.y * 0.2f), border);
        UiKit.Place(box, anchor, position, size);
        Text text = CreateText(box.rectTransform, "Text", string.Empty, fontSize, TextAnchor.MiddleCenter, color, true);
        UiKit.Stretch(text);
        text.rectTransform.anchoredPosition = new Vector2(0f, fontSize * CapRaise);
        return text;
    }

    /// <summary>A line band across the full width of <paramref name="parent"/>, <paramref name="top"/> below its top edge.</summary>
    public static AngularGraphic CreateBand(Transform parent, string name, Color color, float top, float height)
    {
        AngularGraphic band = UiKit.CreatePanel(parent, name, color);
        RectTransform rect = band.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, -top);
        return band;
    }

    /// <summary>
    /// A screen's name as signs print it: the Japanese name large, the English one under it
    /// (as on the genre header). Placed with its top left at <paramref name="position"/>.
    /// </summary>
    public static void CreateHeading(Transform parent, string japanese, string english, Vector2 position, int size)
    {
        Vector2 topLeft = new Vector2(0f, 1f);
        Text name = CreateText(parent, "Heading", japanese, size, TextAnchor.MiddleLeft, Ink, true);
        UiKit.Place(name, topLeft, position, new Vector2(600f, size * 1.4f));
        Text englishName = CreateText(parent, "HeadingEnglish", english, Mathf.RoundToInt(size * 0.46f), TextAnchor.MiddleLeft, InkSoft);
        UiKit.Place(englishName, topLeft, position + new Vector2(3f, -size * 1.3f), new Vector2(600f, size * 0.7f));
    }

    /// <summary>The menus' dim over the whole screen; it takes clicks so nothing behind reacts.</summary>
    public static AngularGraphic CreateDim(Transform parent)
    {
        AngularGraphic dim = UiKit.CreatePanel(parent, "Dim", Dim);
        dim.raycastTarget = true;
        UiKit.Stretch(dim);
        return dim;
    }

    /// <summary>Makes the graphic take mouse / touch input and returns its relay.</summary>
    public static UiPointerRelay AddRelay(Graphic graphic)
    {
        graphic.raycastTarget = true;
        return graphic.gameObject.AddComponent<UiPointerRelay>();
    }

    /// <summary>Converts a RectTransform's centre to a position in <paramref name="space"/> (anchored at its centre).</summary>
    public static Vector2 ToLocal(RectTransform space, RectTransform target, out float width)
    {
        width = target.rect.width * target.lossyScale.x / Mathf.Max(0.0001f, space.lossyScale.x);
        Vector3 centre = target.TransformPoint(target.rect.center);
        return space.InverseTransformPoint(centre);
    }
}

/// <summary>Easing curves for the menu animations (input and output 0..1).</summary>
public static class Ease
{
    public static float OutCubic(float t)
    {
        t = 1f - Mathf.Clamp01(t);
        return 1f - t * t * t;
    }

    public static float InCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }

    public static float InOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    /// <summary>Overshoots a little before settling, for things that pop in.</summary>
    public static float OutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float overshoot = 1.70158f;
        float u = t - 1f;
        return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
    }
}
