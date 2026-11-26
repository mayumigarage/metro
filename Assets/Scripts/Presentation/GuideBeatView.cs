using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The chart's guide beats in the editor's game previews: a line across all four lanes (ceiling and
/// floor) that flows in like a note and flashes on the judgement line. In the full-screen preview it
/// sounds them too (in the editor's sidebar preview the editor does that). Never shown in a normal song.
/// </summary>
public sealed class GuideBeatView : MonoBehaviour
{
    /// <summary>The chart editor's ガイド音 toggle.</summary>
    public static bool Visible { get; set; }

    /// <summary>Volume of the knocks in the full-screen preview.</summary>
    public static float Volume { get; set; } = 0.8f;

    private const int MaxLines = 256;
    private const float LineWidth = 0.07f;
    private const float FadeInProgress = 0.25f;
    // How long a line stays on the judgement line after its time.
    private const double FlashSeconds = 0.1d;
    private const float Inset = 0.02f;

    private static readonly Color LineColor = new Color(0.45f, 1f, 0.45f, 0.9f);

    private SongClock songClock;
    private NoteSpawner noteSpawner;
    private GameFlow flow;
    private Material material;
    private ChartEditorPlayback sounds;
    private SongChartData soundChart;
    private double soundTime;
    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private readonly Vector3[] corners = new Vector3[4];

    private void Start()
    {
        songClock = FindFirstObjectByType<SongClock>();
        noteSpawner = FindFirstObjectByType<NoteSpawner>();
        flow = FindFirstObjectByType<GameFlow>();
        material = RhythmStageView.CreateTransparentMaterial(Color.white);
    }

    private void Update()
    {
        SongChartData chart = noteSpawner != null ? noteSpawner.Chart : null;
        bool shown = Visible && songClock != null && flow != null && flow.IsPreview
            && chart != null && chart.guides != null && chart.guides.Length > 0;
        int used = 0;
        if (shown && material != null)
        {
            double songTime = songClock.SongTime - GameSettings.VisualOffsetSeconds;
            float travelTime = noteSpawner.TravelTimeSeconds;
            foreach (double time in chart.guides)
            {
                double ahead = time - songTime;
                if (ahead < -FlashSeconds)
                {
                    continue;
                }

                if (ahead >= travelTime || used >= MaxLines)
                {
                    break;
                }

                float progress = RhythmStageView.ApplyApproachCurve(Mathf.Clamp01(1f - (float)(ahead / travelTime)));
                float alpha = Mathf.Clamp01(progress / FadeInProgress);
                if (ahead < 0d)
                {
                    alpha *= 1f - (float)(-ahead / FlashSeconds);
                }

                RhythmStageView.GetTunnelCorners(progress, Inset, corners);
                PlaceLine(GetLine(used++), corners[0], corners[1], progress, alpha);
                PlaceLine(GetLine(used++), corners[3], corners[2], progress, alpha);
            }
        }

        for (int index = used; index < lines.Count; index++)
        {
            lines[index].enabled = false;
        }

        UpdateSound(shown && !flow.IsEmbeddedPreview, chart);
    }

    private void UpdateSound(bool wanted, SongChartData chart)
    {
        if (!wanted)
        {
            sounds?.StopFollowing();
            soundChart = null;
            return;
        }

        sounds ??= new ChartEditorPlayback { ClapEnabled = false, MetronomeEnabled = false, GuideEnabled = true };
        double songTime = songClock.SongTime;
        // A retry starts the clock over: line the knocks up again.
        if (chart != soundChart || songTime < soundTime - 0.1d)
        {
            sounds.StopFollowing();
            soundChart = chart;
        }

        soundTime = songTime;
        sounds.GuideVolume = Volume;
        sounds.Follow(songTime, chart);
    }

    private void OnDestroy()
    {
        sounds?.Dispose();
        if (material != null)
        {
            Destroy(material);
        }
    }

    private static void PlaceLine(LineRenderer line, Vector3 from, Vector3 to, float progress, float alpha)
    {
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        float width = LineWidth * RhythmStageView.GetDepthScale(progress);
        line.startWidth = width;
        line.endWidth = width;
        Color color = LineColor;
        color.a *= alpha;
        color = RhythmStageView.ToVertexColor(color);
        line.startColor = color;
        line.endColor = color;
        line.enabled = true;
    }

    private LineRenderer GetLine(int index)
    {
        while (lines.Count <= index)
        {
            GameObject lineObject = new GameObject($"GuideBeat_{lines.Count}");
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            lines.Add(line);
        }

        return lines[index];
    }
}
