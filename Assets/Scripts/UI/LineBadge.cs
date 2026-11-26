using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A station-number badge as on subway signs: a white disc in a thick coloured ring,
/// the line letter above the middle and a two-digit number below it. Used for the
/// title logo, the genre folders, the stations of the song select, the level on the
/// selected-song sign and the badge on the train doors. SetDiameter keeps every
/// proportion, so a badge can be animated in size.
/// </summary>
public sealed class LineBadge : MonoBehaviour
{
    // Proportions taken from platform signs (badge about 130 px across: ring ~23 px,
    // letter cap ~22 px above the middle, number cap ~28 px below it).
    private const float RingRatio = 0.175f;
    private const float LetterSizeRatio = 0.25f;
    private const float LetterOffsetRatio = 0.165f;
    private const float NumberSizeRatio = 0.34f;
    private const float NumberOffsetRatio = -0.105f;
    private const float SingleSizeRatio = 0.5f;
    private const float HaloGapRatio = 0.06f;
    private const float HaloWidthRatio = 0.028f;

    private CircleGraphic halo;
    private Text letterText;
    private Text numberText;
    private float diameter = 100f;
    private bool singleLetter;

    public RectTransform Rect { get; private set; }
    public CircleGraphic Ring { get; private set; }
    public CircleGraphic Face { get; private set; }
    public CanvasGroup Group { get; private set; }
    public float Diameter => diameter;

    public static LineBadge Create(Transform parent, string name, Color lineColor, float diameter)
    {
        RectTransform rect = UiKit.CreateRect(parent, name);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        LineBadge badge = rect.gameObject.AddComponent<LineBadge>();
        badge.Build(lineColor);
        badge.SetDiameter(diameter);
        return badge;
    }

    /// <summary>Letter above, number below; a null or empty number shows the letter alone, large.</summary>
    public void SetCode(string letter, string number)
    {
        singleLetter = string.IsNullOrEmpty(number);
        letterText.text = letter ?? string.Empty;
        numberText.text = singleLetter ? string.Empty : number;
        LayoutTexts();
    }

    public void SetLineColor(Color color)
    {
        Ring.color = color;
        halo.color = UiKit.WithAlpha(color, halo.color.a);
    }

    public void SetTextColor(Color color)
    {
        letterText.color = color;
        numberText.color = color;
    }

    /// <summary>A thin second ring around the badge (the selected station / folder).</summary>
    public void SetHalo(float alpha)
    {
        halo.enabled = alpha > 0.001f;
        halo.color = UiKit.WithAlpha(Ring.color, alpha);
    }

    public void SetDiameter(float value)
    {
        diameter = Mathf.Max(1f, value);
        Rect.sizeDelta = new Vector2(diameter, diameter);
        Ring.Thickness = diameter * RingRatio;
        float haloSize = diameter * (1f + 2f * (HaloGapRatio + HaloWidthRatio));
        halo.rectTransform.sizeDelta = new Vector2(haloSize, haloSize);
        halo.Thickness = Mathf.Max(1.5f, diameter * HaloWidthRatio);
        LayoutTexts();
    }

    private void Build(Color lineColor)
    {
        Rect = (RectTransform)transform;
        Group = gameObject.AddComponent<CanvasGroup>();
        Group.blocksRaycasts = true;
        Group.interactable = false;

        halo = SignKit.CreateCircle(Rect, "Halo", UiKit.WithAlpha(lineColor, 0f), 2f);
        CentreAnchor(halo.rectTransform);
        halo.enabled = false;

        Face = SignKit.CreateCircle(Rect, "Face", SignKit.Plate);
        UiKit.Stretch(Face);
        Ring = SignKit.CreateCircle(Rect, "Ring", lineColor, 10f);
        UiKit.Stretch(Ring);

        letterText = SignKit.CreateText(Rect, "Letter", string.Empty, 10, TextAnchor.MiddleCenter, SignKit.Ink, true);
        CentreAnchor(letterText.rectTransform);
        numberText = SignKit.CreateText(Rect, "Number", string.Empty, 10, TextAnchor.MiddleCenter, SignKit.Ink, true);
        CentreAnchor(numberText.rectTransform);
    }

    private void LayoutTexts()
    {
        if (letterText == null)
        {
            return;
        }

        if (singleLetter)
        {
            int size = Mathf.Max(1, Mathf.RoundToInt(diameter * SingleSizeRatio));
            letterText.fontSize = size;
            letterText.rectTransform.sizeDelta = new Vector2(diameter, size * 1.5f);
            letterText.rectTransform.anchoredPosition = new Vector2(0f, size * SignKit.CapRaise);
            return;
        }

        int letterSize = Mathf.Max(1, Mathf.RoundToInt(diameter * LetterSizeRatio));
        letterText.fontSize = letterSize;
        letterText.rectTransform.sizeDelta = new Vector2(diameter, letterSize * 1.5f);
        letterText.rectTransform.anchoredPosition = new Vector2(0f, diameter * LetterOffsetRatio + letterSize * SignKit.CapRaise);

        int numberSize = Mathf.Max(1, Mathf.RoundToInt(diameter * NumberSizeRatio));
        numberText.fontSize = numberSize;
        numberText.rectTransform.sizeDelta = new Vector2(diameter, numberSize * 1.5f);
        numberText.rectTransform.anchoredPosition = new Vector2(0f, diameter * NumberOffsetRatio + numberSize * SignKit.CapRaise);
    }

    private static void CentreAnchor(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
    }
}
