using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title logo, from the SVG design (viewBox 600 × 270): three stacked bars narrowing
/// downward in three of the route map's line colours, "metro" spread wide under them,
/// and a thin wave below that travels slowly left to right and steps through the route map's line
/// colours at a steady pace. Coordinates below are the SVG's (y down), scaled by
/// <see cref="Scale"/> around <see cref="Centre"/>.
/// </summary>
public sealed class TitleLogo
{
    private const float Scale = 1.6f;
    private static readonly Vector2 Centre = new Vector2(300f, 140f);

    // Bars: x, y, width (height 22), top to bottom.
    private static readonly Vector3[] Bars = { new Vector3(62f, 40f, 478f), new Vector3(86f, 76f, 430f), new Vector3(106f, 110f, 392f) };
    private const float BarHeight = 22f;

    // "metro": left edge, baseline, total length (like SVG textLength / lengthAdjust="spacing"), size.
    private const string Word = "metro";
    private const float WordLeft = 121.5f;
    private const float WordBaseline = 182f;
    private const float WordLength = 365f;
    private const float WordSize = 58f;

    // Wave: one and a quarter sine periods from x 114 to 500 (the SVG's two curves),
    // travelling steadily left to right. Its crest (WaveMid - WaveAmplitude = 202) stays
    // clear of the letters (baseline 182, round letters dip a little below it) wherever it is.
    private const float WaveLeft = 114f;
    private const float WaveRight = 500f;
    private const float WaveMid = 220f;
    private const float WaveAmplitude = 18f;
    private const float WavePeriods = 1.25f;
    private const float WavePhase = -0.55f;
    private const float WaveWidth = 1.6f;
    private const int WaveSamples = 72;
    private const float TravelPeriod = 6f; // seconds for the wave to move on by one wavelength

    /// <summary>Seconds each wave colour holds — about how long a route-map line stays on screen.</summary>
    private const float ColorSeconds = 7f;
    private const float FadeSeconds = 1f;

    /// <summary>The bars' colours, top to bottom: three of the route map's line colours.</summary>
    private static Color[] BarColors => new[] { TitleBackdrop.Red, TitleBackdrop.Amber, TitleBackdrop.Teal };

    /// <summary>The wave steps through every route-map line colour in this order.</summary>
    private static Color[] WaveColors => new[] { TitleBackdrop.Ink, TitleBackdrop.Red, TitleBackdrop.Teal, TitleBackdrop.Orange, TitleBackdrop.Grey, TitleBackdrop.Amber };

    private readonly Vector2[] wavePoints = new Vector2[WaveSamples];
    private RouteLineGraphic wave;
    private Color[] waveColors;

    public RectTransform Root { get; private set; }

    public void Build(RectTransform parent)
    {
        Root = UiKit.CreateRect(parent, "Logo");
        UiKit.Place(Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 270f) * Scale);

        Color[] barColors = BarColors;
        for (int index = 0; index < Bars.Length; index++)
        {
            Vector3 bar = Bars[index];
            AngularGraphic graphic = UiKit.CreatePanel(Root, $"Bar{index}", barColors[index]);
            graphic.raycastTarget = false;
            UiKit.Place(graphic, new Vector2(0.5f, 0.5f), ToLocal(new Vector2(bar.x + bar.z * 0.5f, bar.y + BarHeight * 0.5f)), new Vector2(bar.z, BarHeight) * Scale);
        }

        BuildWord();

        wave = UiKit.CreateRect(Root, "Wave").gameObject.AddComponent<RouteLineGraphic>();
        wave.raycastTarget = false;
        UiKit.Place(wave.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        wave.Width = WaveWidth * Scale;
        waveColors = WaveColors;
        Tick(0f);
    }

    /// <summary>Sways the wave and moves its colour on; <paramref name="time"/> in seconds.</summary>
    public void Tick(float time)
    {
        // Subtracting the time makes the crests move toward larger x (rightward); repeat it
        // so the phase stays small however long the title is shown.
        float travel = Mathf.Repeat(time / TravelPeriod, 1f) * Mathf.PI * 2f;
        for (int index = 0; index < WaveSamples; index++)
        {
            float u = index / (WaveSamples - 1f);
            float x = Mathf.Lerp(WaveLeft, WaveRight, u);
            float y = WaveMid - WaveAmplitude * Mathf.Sin(Mathf.PI * 2f * WavePeriods * u + WavePhase - travel);
            wavePoints[index] = ToLocal(new Vector2(x, y));
        }

        wave.SetPath(wavePoints, 0f);
        wave.SetRange(0f, wave.Length);

        int step = Mathf.FloorToInt(time / ColorSeconds);
        float into = time - step * ColorSeconds;
        Color from = waveColors[(step + waveColors.Length - 1) % waveColors.Length];
        Color to = waveColors[step % waveColors.Length];
        wave.color = step == 0 && into < FadeSeconds ? to : Color.Lerp(from, to, Ease.InOutCubic(into / FadeSeconds));
    }

    /// <summary>
    /// "metro" one Text per letter, since legacy Text has no letter spacing: the letters'
    /// advances are measured and the rest of <see cref="WordLength"/> shared out between them.
    /// </summary>
    private void BuildWord()
    {
        Font font = SignKit.TitleFont;
        int size = Mathf.RoundToInt(WordSize * Scale);
        font.RequestCharactersInTexture(Word, size, FontStyle.Normal);

        float[] advances = new float[Word.Length];
        float total = 0f;
        for (int index = 0; index < Word.Length; index++)
        {
            font.GetCharacterInfo(Word[index], out CharacterInfo info, size, FontStyle.Normal);
            advances[index] = info.advance;
            total += info.advance;
        }

        float gap = (WordLength * Scale - total) / (Word.Length - 1);

        // Legacy Text centres the font's line box in the rect; the baseline sits
        // (lineHeight / 2 - ascent) from the middle.
        float metricsScale = font.fontSize > 0 ? (float)size / font.fontSize : 1f;
        float baselineFromMiddle = (font.lineHeight * 0.5f - font.ascent) * metricsScale;
        float baselineY = ToLocal(new Vector2(0f, WordBaseline)).y;

        float left = (WordLeft - Centre.x) * Scale;
        for (int index = 0; index < Word.Length; index++)
        {
            Text letter = UiKit.CreateText(Root, $"Letter_{Word[index]}", Word[index].ToString(), size, TextAnchor.MiddleCenter, SignKit.Ink);
            letter.font = font;
            letter.raycastTarget = false;
            letter.horizontalOverflow = HorizontalWrapMode.Overflow;
            letter.verticalOverflow = VerticalWrapMode.Overflow;
            UiKit.Place(letter, new Vector2(0.5f, 0.5f), new Vector2(left + advances[index] * 0.5f, baselineY - baselineFromMiddle), new Vector2(advances[index] + 4f, size * 2f));
            left += advances[index] + gap;
        }
    }

    private static Vector2 ToLocal(Vector2 svg)
    {
        return new Vector2(svg.x - Centre.x, Centre.y - svg.y) * Scale;
    }
}
