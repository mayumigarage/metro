using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rectangular frames around the tunnel — along the ceiling lanes, down the open
/// sides and along the floor lanes — that flow toward the player with the notes, for
/// a sense of space and speed. They sit on the beat: one every GRID option beats
/// (0 = off) of the chart's BPM, at the same progress a note with that time would
/// have, so they move exactly as fast as the notes (HI-SPEED included).
/// </summary>
public sealed class TunnelGridView : MonoBehaviour
{
    private const int MaxFrames = 96;

    // Frames fade in over this much progress from the far end, so they do not pop up.
    private const float FadeInProgress = 0.25f;

    // How far the frame lines sit inside the lane surfaces, so they never z-fight with them.
    private const float Inset = 0.03f;

    [SerializeField] private SongClock songClock;
    [SerializeField] private ChartLoader chartLoader;
    [SerializeField] private NoteSpawner noteSpawner;

    private readonly List<LineRenderer> frames = new List<LineRenderer>();
    private readonly Vector3[] corners = new Vector3[4];
    private Material material;

    private void Start()
    {
        if (songClock == null)
        {
            songClock = FindFirstObjectByType<SongClock>();
        }

        if (chartLoader == null)
        {
            chartLoader = FindFirstObjectByType<ChartLoader>();
        }

        if (noteSpawner == null)
        {
            noteSpawner = FindFirstObjectByType<NoteSpawner>();
        }

        // White: each frame's line colour carries the grid colour and its fade.
        material = RhythmStageView.CreateTransparentMaterial(Color.white);
    }

    private void Update()
    {
        int used = 0;
        float beats = GameSettings.GridBeats;
        if (songClock != null && noteSpawner != null && material != null && beats > 0f)
        {
            // The chart in play (a chart editor preview too), else the selected song.
            SongChartData chart = noteSpawner.Chart ?? (chartLoader != null ? chartLoader.Chart : null);
            float bpm = chart != null && chart.bpm > 0f ? chart.bpm : 120f;
            double offset = chart != null ? chart.offset : 0d;
            double interval = beats * 60d / bpm;
            double songTime = songClock.SongTime;
            float travelTime = noteSpawner.TravelTimeSeconds;

            // Every frame time offset + k * interval between now (judgement line) and now + travel time (far end).
            long first = (long)System.Math.Ceiling((songTime - offset) / interval);
            for (long index = first; used < MaxFrames; index++)
            {
                double time = offset + index * interval;
                float progress = 1f - (float)((time - songTime) / travelTime);
                if (progress <= 0f)
                {
                    break;
                }

                PlaceFrame(GetFrame(used++), RhythmStageView.ApplyApproachCurve(progress));
            }
        }

        for (int index = used; index < frames.Count; index++)
        {
            frames[index].enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (material != null)
        {
            Destroy(material);
        }
    }

    private void PlaceFrame(LineRenderer frame, float progress)
    {
        RhythmStageView.GetTunnelCorners(progress, Inset, corners);
        frame.SetPositions(corners);

        float width = RhythmStageView.GridLineWidth * RhythmStageView.GetDepthScale(progress);
        frame.startWidth = width;
        frame.endWidth = width;

        Color color = RhythmStageView.GridColor;
        color.a *= Mathf.Clamp01(progress / FadeInProgress);
        color = RhythmStageView.ToVertexColor(color);
        frame.startColor = color;
        frame.endColor = color;
        frame.enabled = true;
    }

    private LineRenderer GetFrame(int index)
    {
        while (frames.Count <= index)
        {
            GameObject frameObject = new GameObject($"TunnelFrame_{frames.Count}");
            frameObject.transform.SetParent(transform, false);

            LineRenderer frame = frameObject.AddComponent<LineRenderer>();
            frame.useWorldSpace = true;
            frame.loop = true;
            frame.positionCount = corners.Length;
            frame.sharedMaterial = material;
            frame.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            frame.receiveShadows = false;
            frames.Add(frame);
        }

        return frames[index];
    }
}
