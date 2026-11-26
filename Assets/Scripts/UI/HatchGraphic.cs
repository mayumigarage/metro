using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Parallel diagonal lines across the rect (the title screen's background). Offset
/// slides them sideways, so animating it makes the hatching drift.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HatchGraphic : MaskableGraphic
{
    [SerializeField] private float spacing = 120f;
    [SerializeField] private float lineWidth = 2f;
    [Tooltip("Angle of the lines from the horizontal, in degrees.")]
    [SerializeField] private float angle = 60f;
    [SerializeField] private float offset;

    private static readonly List<Vector2> quad = new List<Vector2>();
    private static readonly float[] clipP = new float[4];
    private static readonly float[] clipQ = new float[4];

    public void Configure(float lineSpacing, float width, float degrees)
    {
        spacing = Mathf.Max(4f, lineSpacing);
        lineWidth = Mathf.Max(0.5f, width);
        angle = degrees;
        SetVerticesDirty();
    }

    public float Offset
    {
        get => offset;
        set
        {
            offset = Mathf.Repeat(value, spacing);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        float radians = angle * Mathf.Deg2Rad;
        Vector2 along = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 across = new Vector2(-along.y, along.x);
        Vector2 centre = rect.center;

        // The range of "across" distances the rect covers.
        float reach = Mathf.Abs(across.x) * rect.width * 0.5f + Mathf.Abs(across.y) * rect.height * 0.5f;
        float length = rect.width + rect.height;
        int first = Mathf.FloorToInt((-reach - offset) / spacing) - 1;
        int last = Mathf.CeilToInt((reach - offset) / spacing) + 1;
        Vector2 half = across * (lineWidth * 0.5f);

        for (int line = first; line <= last; line++)
        {
            Vector2 middle = centre + across * (line * spacing + offset);
            if (!Clip(rect, middle - along * length, middle + along * length, out Vector2 start, out Vector2 end))
            {
                continue;
            }

            quad.Clear();
            quad.Add(start + half);
            quad.Add(end + half);
            quad.Add(end - half);
            quad.Add(start - half);
            UiMesh.AddFanPolygon(vh, quad, color);
        }
    }

    /// <summary>Liang-Barsky: the part of the segment inside the rect.</summary>
    private static bool Clip(Rect rect, Vector2 from, Vector2 to, out Vector2 start, out Vector2 end)
    {
        float t0 = 0f;
        float t1 = 1f;
        Vector2 delta = to - from;
        float[] p = clipP;
        float[] q = clipQ;
        p[0] = -delta.x;
        p[1] = delta.x;
        p[2] = -delta.y;
        p[3] = delta.y;
        q[0] = from.x - rect.xMin;
        q[1] = rect.xMax - from.x;
        q[2] = from.y - rect.yMin;
        q[3] = rect.yMax - from.y;
        for (int index = 0; index < 4; index++)
        {
            if (Mathf.Approximately(p[index], 0f))
            {
                if (q[index] < 0f)
                {
                    start = end = Vector2.zero;
                    return false;
                }

                continue;
            }

            float t = q[index] / p[index];
            if (p[index] < 0f)
            {
                t0 = Mathf.Max(t0, t);
            }
            else
            {
                t1 = Mathf.Min(t1, t);
            }
        }

        start = from + delta * t0;
        end = from + delta * t1;
        return t0 < t1;
    }
}
