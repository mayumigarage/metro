using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Blinking notice at the left and right edges of the stage that a laser is about to
/// come: the left edge for the left stick's lasers, the right edge for the right stick's,
/// in that stick's colour (the LASER COLOR option). Each edge is a soft glow spilling in
/// from the screen edge, a solid strip on it and chevrons pointing inward. It blinks
/// faster the nearer the laser's start gets and goes out when the laser begins.
///
/// Built into the HUD canvas by <see cref="RhythmJudgementUI"/>, which calls
/// <see cref="Tick"/> every frame while a song is on screen.
/// </summary>
public sealed class LaserWarningUI
{
    /// <summary>How long before a laser starts its edge begins to blink.</summary>
    public const double WarningSeconds = 2d;

    private const float GlowWidth = 300f;
    private const float StripWidth = 10f;
    private const int ChevronCount = 3;
    private static readonly Vector2 ChevronSize = new Vector2(44f, 96f);
    private const float ChevronSpacing = 62f;
    private const float ChevronFirstOffset = 40f;

    // Brightness multiplied into the colours, so they pass the bloom threshold.
    private const float Emission = 1.5f;

    private const float SlowBlinkHz = 3f;
    private const float FastBlinkHz = 7f;
    private const float FadeSpeed = 12f;

    private sealed class Edge
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public Graphic[] Graphics;
        public float Phase;
        public Color AppliedColor;
    }

    private readonly Edge[] edges = new Edge[2];

    public LaserWarningUI(Transform parent)
    {
        for (int side = 0; side < edges.Length; side++)
        {
            edges[side] = BuildEdge(parent, side);
        }
    }

    /// <param name="chart">The chart being played (null when idle).</param>
    public void Tick(SongChartData chart, double songTime, float deltaTime)
    {
        float top = 1f - RhythmStageView.TopInset;
        for (int side = 0; side < edges.Length; side++)
        {
            Edge edge = edges[side];
            if (!Mathf.Approximately(edge.Root.anchorMax.y, top))
            {
                edge.Root.anchorMax = new Vector2(edge.Root.anchorMax.x, top);
            }

            double remaining = GetSecondsToNextLaser(chart, side, songTime);
            bool warned = remaining <= WarningSeconds;
            float goal = 0f;
            if (warned)
            {
                float closeness = Mathf.Clamp01((float)(1d - remaining / WarningSeconds));
                edge.Phase += deltaTime * Mathf.Lerp(SlowBlinkHz, FastBlinkHz, closeness) * Mathf.PI * 2f;

                // Mostly on or off with short soft edges; on for a little longer than off.
                goal = Mathf.Clamp01(Mathf.Sin(edge.Phase) * 1.8f + 0.7f);
            }
            else
            {
                // Starts bright the next time it comes on.
                edge.Phase = Mathf.PI * 0.5f;
            }

            edge.Group.alpha = Mathf.MoveTowards(edge.Group.alpha, goal, FadeSpeed * deltaTime);
            ApplyColor(edge, RhythmStageView.GetStickColor(side));
        }
    }

    /// <summary>Seconds until the next laser of a stick starts (infinity if none is still to come).</summary>
    private static double GetSecondsToNextLaser(SongChartData chart, int side, double songTime)
    {
        double nearest = double.PositiveInfinity;
        if (chart == null || chart.lasers == null)
        {
            return nearest;
        }

        foreach (LaserNoteData laser in chart.lasers)
        {
            if (Mathf.Clamp(laser.side, 0, 1) != side)
            {
                continue;
            }

            double remaining = laser.StartTime - songTime;
            if (remaining > 0d && remaining < nearest)
            {
                nearest = remaining;
            }
        }

        return nearest;
    }

    private static Edge BuildEdge(Transform parent, int side)
    {
        bool onRight = side == 1;
        Edge edge = new Edge { AppliedColor = Color.clear };

        RectTransform root = UiKit.CreateRect(parent, onRight ? "LaserWarning_Right" : "LaserWarning_Left");
        float edgeX = onRight ? 1f : 0f;
        root.anchorMin = new Vector2(edgeX, 0f);
        root.anchorMax = new Vector2(edgeX, 1f - RhythmStageView.TopInset);
        root.pivot = new Vector2(edgeX, 0.5f);
        root.sizeDelta = new Vector2(GlowWidth, 0f);
        root.anchoredPosition = Vector2.zero;
        edge.Root = root;

        edge.Group = root.gameObject.AddComponent<CanvasGroup>();
        edge.Group.alpha = 0f;
        edge.Group.blocksRaycasts = false;
        edge.Group.interactable = false;

        var graphics = new System.Collections.Generic.List<Graphic>();

        EdgeGlowGraphic glow = CreateGraphic<EdgeGlowGraphic>(root, "Glow");
        glow.StrongOnRight = onRight;
        UiKit.Stretch(glow);
        graphics.Add(glow);

        // A solid strip on the screen edge, as tall as the edge.
        Image strip = CreateGraphic<Image>(root, "Strip");
        RectTransform stripRect = strip.rectTransform;
        stripRect.anchorMin = new Vector2(edgeX, 0f);
        stripRect.anchorMax = new Vector2(edgeX, 1f);
        stripRect.pivot = new Vector2(edgeX, 0.5f);
        stripRect.sizeDelta = new Vector2(StripWidth, 0f);
        stripRect.anchoredPosition = Vector2.zero;
        graphics.Add(strip);

        // Chevrons pointing inward, away from the screen edge (the graphic points right).
        for (int index = 0; index < ChevronCount; index++)
        {
            ChevronGraphic chevron = CreateGraphic<ChevronGraphic>(root, $"Chevron{index}");
            chevron.Thickness = 16f;
            float inward = ChevronFirstOffset + index * ChevronSpacing + ChevronSize.x * 0.5f;
            RectTransform chevronRect = chevron.rectTransform;
            chevronRect.anchorMin = new Vector2(edgeX, 0.5f);
            chevronRect.anchorMax = new Vector2(edgeX, 0.5f);
            chevronRect.pivot = new Vector2(0.5f, 0.5f);
            chevronRect.sizeDelta = ChevronSize;
            chevronRect.anchoredPosition = new Vector2(onRight ? -inward : inward, 0f);
            chevronRect.localRotation = Quaternion.Euler(0f, 0f, onRight ? 180f : 0f);
            graphics.Add(chevron);
        }

        edge.Graphics = graphics.ToArray();
        return edge;
    }

    private static T CreateGraphic<T>(Transform parent, string name) where T : Graphic
    {
        RectTransform rect = UiKit.CreateRect(parent, name);
        T graphic = rect.gameObject.AddComponent<T>();
        graphic.raycastTarget = false;

        // HDR tint so the edge blooms like the rest of the lit HUD.
        rect.gameObject.AddComponent<SignEmission>().Emission = new Color(Emission, Emission, Emission, 1f);
        return graphic;
    }

    /// <summary>Tints every part of an edge in the stick's colour (glow soft, strip and chevrons solid).</summary>
    private static void ApplyColor(Edge edge, Color color)
    {
        if (edge.AppliedColor == color)
        {
            return;
        }

        edge.AppliedColor = color;
        for (int index = 0; index < edge.Graphics.Length; index++)
        {
            Color part = color;
            part.a = index == 0 ? 0.6f : 0.95f;
            edge.Graphics[index].color = part;
        }
    }
}
