using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Shared look of the UI: palette, font, and the helpers every screen is built
/// with. All UI is created in code on a 1920 x 1080 reference canvas.
/// </summary>
public static class UiKit
{
    public const float ReferenceWidth = 1920f;
    public const float ReferenceHeight = 1080f;

    public static readonly Color Panel = new Color(0.030f, 0.045f, 0.095f, 0.90f);
    public static readonly Color PanelSoft = new Color(0.050f, 0.070f, 0.140f, 0.72f);
    public static readonly Color Backdrop = new Color(0.006f, 0.010f, 0.028f, 0.86f);
    public static readonly Color Accent = new Color(0.28f, 0.92f, 1f, 1f);
    public static readonly Color AccentAlt = new Color(1f, 0.32f, 0.82f, 1f);
    public static readonly Color Gold = new Color(1f, 0.85f, 0.36f, 1f);
    public static readonly Color TextMain = new Color(0.94f, 0.97f, 1f, 1f);
    public static readonly Color TextDim = new Color(0.56f, 0.66f, 0.82f, 1f);
    public static readonly Color Fast = new Color(0.40f, 0.72f, 1f, 1f);
    public static readonly Color Slow = new Color(1f, 0.55f, 0.36f, 1f);

    private static readonly Color PerfectColor = new Color(1f, 0.90f, 0.40f, 1f);
    private static readonly Color GreatColor = new Color(0.38f, 0.88f, 1f, 1f);
    private static readonly Color GoodColor = new Color(0.50f, 1f, 0.60f, 1f);
    private static readonly Color MissColor = new Color(1f, 0.38f, 0.45f, 1f);

    private static Font mainFont;

    /// <summary>
    /// A condensed technical face where the system has one, with Japanese-capable
    /// fonts behind it for song titles and descriptions.
    /// </summary>
    public static Font MainFont
    {
        get
        {
            if (mainFont == null)
            {
                mainFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Bahnschrift", "Segoe UI", "Yu Gothic UI", "Meiryo", "Arial" }, 32);
                if (mainFont == null)
                {
                    mainFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
            }

            return mainFont;
        }
    }

    public static Color GetJudgementColor(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect:
                return PerfectColor;
            case Judgement.Great:
                return GreatColor;
            case Judgement.Good:
                return GoodColor;
            default:
                return MissColor;
        }
    }

    public static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    /// <summary>Screen-space canvas scaled from the 1920 x 1080 reference by screen height.</summary>
    public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
    {
        GameObject canvasObject = new GameObject(name);
        canvasObject.transform.SetParent(parent, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    /// <summary>The mouse needs an EventSystem; keyboard and gamepad are polled by MenuInput.</summary>
    public static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    public static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject rectObject = new GameObject(name, typeof(RectTransform));
        rectObject.transform.SetParent(parent, false);
        return (RectTransform)rectObject.transform;
    }

    /// <summary>Angular panel. Cuts are clockwise from the top left; place it with Place / Stretch.</summary>
    public static AngularGraphic CreatePanel(Transform parent, string name, Color color, float cutTopLeft = 0f, float cutTopRight = 0f, float cutBottomRight = 0f, float cutBottomLeft = 0f)
    {
        RectTransform rect = CreateRect(parent, name);
        AngularGraphic graphic = rect.gameObject.AddComponent<AngularGraphic>();
        graphic.color = color;
        graphic.raycastTarget = false;
        graphic.SetCuts(cutTopLeft, cutTopRight, cutBottomRight, cutBottomLeft);
        return graphic;
    }

    public static Text CreateText(Transform parent, string name, string content, int size, TextAnchor alignment, Color color, bool bold = false)
    {
        RectTransform rect = CreateRect(parent, name);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = MainFont;
        text.fontSize = size;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    /// <summary>
    /// Fixed-size placement: <paramref name="anchor"/> is the point of the parent (0..1) the
    /// rect hangs from and also its own pivot; <paramref name="position"/> is the offset from it.
    /// </summary>
    public static void Place(Graphic graphic, Vector2 anchor, Vector2 position, Vector2 size)
    {
        Place(graphic.rectTransform, anchor, position, size);
    }

    public static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    /// <summary>Fills the parent, inset by the given margins.</summary>
    public static void Stretch(Graphic graphic, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        Stretch(graphic.rectTransform, left, bottom, right, top);
    }

    public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>A filled panel plus a thin outline of the same shape.</summary>
    public static RectTransform CreateFramedPanel(Transform parent, string name, Color fill, Color frame, float cutTopLeft, float cutTopRight, float cutBottomRight, float cutBottomLeft)
    {
        RectTransform root = CreateRect(parent, name);
        Stretch(CreatePanel(root, "Fill", fill, cutTopLeft, cutTopRight, cutBottomRight, cutBottomLeft));
        AngularGraphic border = CreatePanel(root, "Frame", frame, cutTopLeft, cutTopRight, cutBottomRight, cutBottomLeft);
        border.SetBorder(2f);
        Stretch(border);
        return root;
    }

    /// <summary>Formats an achievement rate with a smaller percent sign, e.g. 98.45%.</summary>
    public static string FormatPercent(float percent, int signSize)
    {
        return $"{percent:0.00}<size={signSize}>%</size>";
    }
}
