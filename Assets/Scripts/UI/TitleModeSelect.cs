using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What the title screen leads to.</summary>
public enum TitleMode
{
    Solo,
    Multi,
    Editor,
}

/// <summary>
/// The title screen's second step, after the hand-drawn sketch: one wide route arrow
/// (a chevron leading into a band with a notched tail and a pointed head) carrying the
/// numbered boxes 1 ソロ / 2 マルチ / 3 エディター, names under the band. The band takes
/// the chosen mode's colour; マルチ is not made yet and refuses with a shake.
/// Built into the title screen, which feeds it input and calls <see cref="Tick"/>.
/// </summary>
public sealed class TitleModeSelect
{
    private const string Footer = "← →  SELECT          ENTER / A  DECIDE          ESC / B  BACK";
    private const float BandY = 20f;
    private const float BandLeft = -660f;
    private const float BandRight = 700f;
    private const float BandHeight = 170f;
    private const float Notch = 80f;
    private const float HeadLength = 150f;
    private const float BoxSpacing = 370f;
    private const float BoxSize = 128f;
    private const float RevealLeft = -840f;
    private const float RevealWidth = 1640f;
    private const float RevealSeconds = 0.5f;
    private const float SelectedScale = 1.16f;
    private const float ShakeSeconds = 0.4f;
    private const float HideSeconds = 0.2f;

    private static readonly Color EditorColor = new Color32(0x0A, 0x93, 0x96, 0xFF);

    private sealed class Entry
    {
        public TitleMode Mode;
        public float X;
        public RectTransform Box;
        public RoundedRectGraphic Face;
        public RoundedRectGraphic Outline;
        public Text Number;
        public Text Name;
        public Text English;
        public float Scale = 1f;
        public float ShakeAt = -10f;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private RectTransform root;
    private RectTransform reveal;
    private PolygonGraphic band;
    private ChevronGraphic lead;
    private CanvasGroup chrome;
    private Text message;
    private Color bandColor = SignKit.MetroRed;
    private int selected;
    private float shownAt;
    private float hiddenAt = -1f;
    private float messageUntil;

    /// <summary>The pointer went over an entry.</summary>
    public Action<TitleMode> Hovered;

    /// <summary>An entry was clicked.</summary>
    public Action<TitleMode> Clicked;

    public CanvasGroup Group { get; private set; }
    public bool IsShown => root.gameObject.activeSelf && hiddenAt < 0f;
    public TitleMode Selected => entries[selected].Mode;

    public static bool IsAvailable(TitleMode mode) => mode != TitleMode.Multi;

    public static Color GetColor(TitleMode mode)
    {
        switch (mode)
        {
            case TitleMode.Solo:
                return SignKit.MetroRed;
            case TitleMode.Editor:
                return EditorColor;
            default:
                return SignKit.Disabled;
        }
    }

    public void Build(RectTransform parent)
    {
        root = UiKit.CreateRect(parent, "ModeSelect");
        UiKit.Stretch(root);
        Group = root.gameObject.AddComponent<CanvasGroup>();
        Vector2 centre = new Vector2(0.5f, 0.5f);

        string[] names = { "ソロ", "マルチ", "エディター" };
        string[] english = { "Solo", "Multi  ·  coming soon", "Chart Editor" };
        TitleMode[] modes = { TitleMode.Solo, TitleMode.Multi, TitleMode.Editor };
        float middle = (BandLeft + Notch + BandRight - HeadLength) * 0.5f;

        // Hit areas first, so they sit under everything: a box and its name together.
        for (int index = 0; index < modes.Length; index++)
        {
            TitleMode mode = modes[index];
            float x = middle + (index - 1) * BoxSpacing;
            RoundedRectGraphic hit = SignKit.CreateRoundedRect(root, $"Hit_{mode}", Color.clear);
            UiKit.Place(hit, centre, new Vector2(x, BandY - 70f), new Vector2(BoxSpacing - 20f, 380f));
            UiPointerRelay relay = SignKit.AddRelay(hit);
            relay.Entered = () => Hovered?.Invoke(mode);
            relay.Clicked = eventData => Clicked?.Invoke(mode);
            entries.Add(new Entry { Mode = mode, X = x });
        }

        // The band grows in from the left behind a mask.
        reveal = UiKit.CreateRect(root, "Reveal");
        reveal.anchorMin = centre;
        reveal.anchorMax = centre;
        reveal.pivot = new Vector2(0f, 0.5f);
        reveal.anchoredPosition = new Vector2(RevealLeft, BandY);
        reveal.sizeDelta = new Vector2(0f, 420f);
        reveal.gameObject.AddComponent<RectMask2D>();
        RectTransform content = UiKit.CreateRect(reveal, "Content");
        content.anchorMin = new Vector2(0f, 0.5f);
        content.anchorMax = new Vector2(0f, 0.5f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = new Vector2(-RevealLeft, 0f);

        lead = SignKit.CreateChevron(content, "Lead", bandColor, 52f);
        UiKit.Place(lead, centre, new Vector2(BandLeft - 70f, 0f), new Vector2(Notch + 20f, BandHeight));

        band = UiKit.CreateRect(content, "Band").gameObject.AddComponent<PolygonGraphic>();
        band.raycastTarget = false;
        band.color = bandColor;
        UiKit.Place(band.rectTransform, centre, Vector2.zero, Vector2.zero);
        float half = BandHeight * 0.5f;
        band.SetPoints(new[]
        {
            new Vector2(BandLeft + Notch, 0f),
            new Vector2(BandLeft, -half),
            new Vector2(BandRight - HeadLength, -half),
            new Vector2(BandRight, 0f),
            new Vector2(BandRight - HeadLength, half),
            new Vector2(BandLeft, half),
        });

        for (int index = 0; index < entries.Count; index++)
        {
            Entry entry = entries[index];
            entry.Box = UiKit.CreateRect(content, $"Box_{entry.Mode}");
            UiKit.Place(entry.Box, centre, new Vector2(entry.X, 0f), new Vector2(BoxSize, BoxSize));
            entry.Outline = SignKit.CreateRoundedRect(entry.Box, "Outline", SignKit.Face, 22f, 6f);
            UiKit.Stretch(entry.Outline, -18f, -18f, -18f, -18f);
            entry.Face = SignKit.CreateRoundedRect(entry.Box, "Face", SignKit.Face, 12f);
            UiKit.Stretch(entry.Face);
            entry.Number = SignKit.CreateText(entry.Box, "Number", (index + 1).ToString(), 84, TextAnchor.MiddleCenter, SignKit.Ink, true);
            UiKit.Stretch(entry.Number);
            entry.Number.rectTransform.anchoredPosition = new Vector2(0f, 84 * SignKit.CapRaise);

            entry.Name = SignKit.CreateText(root, $"Name_{entry.Mode}", names[index], 58, TextAnchor.MiddleCenter, SignKit.Ink, true);
            UiKit.Place(entry.Name, centre, new Vector2(entry.X, BandY - 170f), new Vector2(BoxSpacing, 80f));
            entry.English = SignKit.CreateText(root, $"English_{entry.Mode}", english[index], 26, TextAnchor.MiddleCenter, SignKit.InkSoft);
            UiKit.Place(entry.English, centre, new Vector2(entry.X, BandY - 228f), new Vector2(BoxSpacing, 40f));
        }

        message = SignKit.CreateText(root, "Message", "マルチプレイは準備中です  <size=26>Multiplayer is coming soon</size>", 34, TextAnchor.MiddleCenter, SignKit.MetroRed, true);
        UiKit.Place(message, centre, new Vector2(0f, BandY - 320f), new Vector2(1400f, 60f));

        RectTransform chromeRect = UiKit.CreateRect(root, "Chrome");
        UiKit.Stretch(chromeRect);
        chrome = chromeRect.gameObject.AddComponent<CanvasGroup>();
        chrome.blocksRaycasts = false;
        Text footer = SignKit.CreateText(chromeRect, "Footer", Footer, 24, TextAnchor.MiddleRight, SignKit.InkDim, true);
        UiKit.Place(footer, new Vector2(1f, 0f), new Vector2(-66f, 54f), new Vector2(1700f, 34f));
        Text code = SignKit.CreateCodeBox(chromeRect, "CodeBox", SignKit.Ink, 54, 6f, new Vector2(1f, 1f), new Vector2(-64f, -58f), new Vector2(156f, 96f));
        code.text = "M00";

        root.gameObject.SetActive(false);
    }

    /// <summary>Brings the band in (after <paramref name="delay"/> seconds) with <paramref name="mode"/> chosen.</summary>
    public void Show(TitleMode mode, float delay)
    {
        root.gameObject.SetActive(true);
        Group.alpha = 1f;
        Group.blocksRaycasts = true;
        shownAt = Time.unscaledTime + delay;
        hiddenAt = -1f;
        messageUntil = 0f;
        selected = Mathf.Max(0, entries.FindIndex(entry => entry.Mode == mode));
        bandColor = GetColor(Selected);
        foreach (Entry entry in entries)
        {
            entry.Scale = entry.Mode == mode ? SelectedScale : 1f;
            entry.ShakeAt = -10f;
        }

        Tick(Time.unscaledTime);
    }

    /// <summary>Fades the band out.</summary>
    public void Hide()
    {
        if (root.gameObject.activeSelf && hiddenAt < 0f)
        {
            hiddenAt = Time.unscaledTime;
            Group.blocksRaycasts = false;
        }
    }

    public void HideInstantly()
    {
        hiddenAt = -1f;
        root.gameObject.SetActive(false);
    }

    public void Select(TitleMode mode)
    {
        int index = entries.FindIndex(entry => entry.Mode == mode);
        if (index >= 0)
        {
            selected = index;
        }
    }

    /// <summary>Moves the choice; returns false (and shakes the end box) past either end.</summary>
    public bool Move(int delta)
    {
        int next = selected + delta;
        if (next < 0 || next >= entries.Count)
        {
            entries[selected].ShakeAt = Time.unscaledTime;
            return false;
        }

        selected = next;
        return true;
    }

    /// <summary>The chosen mode cannot be entered: its box shakes and the notice shows.</summary>
    public void Refuse()
    {
        entries[selected].ShakeAt = Time.unscaledTime;
        messageUntil = Time.unscaledTime + 2.2f;
    }

    public void Tick(float now)
    {
        if (!root.gameObject.activeSelf)
        {
            return;
        }

        if (hiddenAt >= 0f)
        {
            float fade = (now - hiddenAt) / HideSeconds;
            Group.alpha = 1f - Mathf.Clamp01(fade);
            if (fade >= 1f)
            {
                HideInstantly();
                return;
            }
        }

        float age = now - shownAt;
        float deltaTime = Time.unscaledDeltaTime;
        reveal.sizeDelta = new Vector2(RevealWidth * Ease.OutCubic(age / RevealSeconds), reveal.sizeDelta.y);
        chrome.alpha = Mathf.Clamp01((age - 0.3f) / 0.3f);

        bandColor = Color.Lerp(bandColor, GetColor(Selected), 1f - Mathf.Exp(-12f * deltaTime));
        band.color = bandColor;
        lead.color = bandColor;

        float pulse = 0.5f + 0.5f * Mathf.Sin(now * 4f);
        for (int index = 0; index < entries.Count; index++)
        {
            Entry entry = entries[index];
            bool isSelected = index == selected;
            bool available = IsAvailable(entry.Mode);
            entry.Scale = Mathf.Lerp(entry.Scale, isSelected ? SelectedScale : 1f, 1f - Mathf.Exp(-16f * deltaTime));

            float pop = Ease.OutBack((age - 0.12f - index * 0.1f) / 0.35f);
            float shakeT = (now - entry.ShakeAt) / ShakeSeconds;
            float shake = shakeT < 1f ? Mathf.Sin(shakeT * Mathf.PI * 6f) * 12f * (1f - shakeT) : 0f;
            entry.Box.localScale = Vector3.one * Mathf.Max(0f, pop * entry.Scale);
            entry.Box.anchoredPosition = new Vector2(entry.X + shake, 0f);

            entry.Face.color = available ? SignKit.Face : SignKit.SteelLight;
            entry.Number.color = !available ? SignKit.InkDim : isSelected ? GetColor(entry.Mode) : SignKit.Ink;
            entry.Outline.color = UiKit.WithAlpha(SignKit.Face, isSelected ? 0.55f + 0.45f * pulse : 0f);

            float label = Mathf.Clamp01((age - 0.3f - index * 0.08f) / 0.3f);
            float lift = (1f - Ease.OutCubic(label)) * -20f;
            entry.Name.rectTransform.anchoredPosition = new Vector2(entry.X + shake, BandY - 170f + lift);
            entry.English.rectTransform.anchoredPosition = new Vector2(entry.X + shake, BandY - 228f + lift);
            Color nameColor = !available ? SignKit.Disabled : isSelected ? SignKit.Ink : SignKit.InkDim;
            entry.Name.color = UiKit.WithAlpha(nameColor, label);
            entry.English.color = UiKit.WithAlpha(available && isSelected ? SignKit.InkSoft : SignKit.Disabled, label);
        }

        float notice = Mathf.Clamp01((messageUntil - now) / 0.3f);
        message.color = UiKit.WithAlpha(SignKit.MetroRed, notice);
    }
}
