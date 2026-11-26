using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A disc or a ring filling its rect (diameter = the shorter side), optionally split
/// into arcs with gaps between them. Drawn as a mesh with a soft fringe, so it needs
/// no sprite and stays round at any size. Line badges, route-map stations and the
/// spinning start ring are made of it.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CircleGraphic : MaskableGraphic
{
    [Tooltip("0 = filled disc. Otherwise a ring this wide.")]
    [SerializeField] private float thickness;
    [Tooltip("Number of arcs the ring is split into (1 = a whole ring).")]
    [SerializeField] private int segments = 1;
    [Tooltip("Gap between the arcs, in degrees.")]
    [SerializeField] private float gapDegrees;
    [Tooltip("Part of the ring drawn, in degrees counter-clockwise from +x (0..360 = all of it).")]
    [SerializeField] private float arcFromDegrees;
    [SerializeField] private float arcToDegrees = 360f;

    public float Thickness
    {
        get => thickness;
        set
        {
            thickness = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    /// <summary>Splits the ring into <paramref name="count"/> arcs separated by <paramref name="gap"/> degrees.</summary>
    public void SetSegments(int count, float gap)
    {
        segments = Mathf.Max(1, count);
        gapDegrees = Mathf.Clamp(gap, 0f, 360f / segments - 1f);
        SetVerticesDirty();
    }

    /// <summary>Draws only the part of the ring from <paramref name="fromDegrees"/> to <paramref name="toDegrees"/> (counter-clockwise from +x).</summary>
    public void SetArc(float fromDegrees, float toDegrees)
    {
        arcFromDegrees = fromDegrees;
        arcToDegrees = fromDegrees + Mathf.Clamp(toDegrees - fromDegrees, 0f, 360f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (radius <= 0.5f)
        {
            return;
        }

        Vector2 centre = rect.center;
        Color32 solid = color;
        int fullSteps = Mathf.Clamp(Mathf.CeilToInt(radius * 0.6f), 32, 192);

        if (thickness <= 0f || thickness >= radius)
        {
            AddDisc(vh, centre, radius, fullSteps, solid);
            return;
        }

        int count = Mathf.Max(1, segments);
        float gap = count > 1 ? gapDegrees : 0f;
        float span = Mathf.Clamp(arcToDegrees - arcFromDegrees, 0f, 360f) / count;
        for (int segment = 0; segment < count; segment++)
        {
            float from = arcFromDegrees + segment * span + gap * 0.5f;
            float to = arcFromDegrees + (segment + 1) * span - gap * 0.5f;
            int steps = Mathf.Max(2, Mathf.CeilToInt(fullSteps * (to - from) / 360f));
            UiMesh.AddRing(vh, centre, radius, thickness, from, to, steps, solid);
        }
    }

    private static void AddDisc(VertexHelper vh, Vector2 centre, float radius, int steps, Color32 color)
    {
        Color32 clear = color;
        clear.a = 0;
        int start = vh.currentVertCount;
        vh.AddVert(centre, color, Vector4.zero);
        for (int step = 0; step <= steps; step++)
        {
            float angle = step * Mathf.PI * 2f / steps;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(centre + direction * radius, color, Vector4.zero);
            vh.AddVert(centre + direction * (radius + UiMesh.Fringe), clear, Vector4.zero);
        }

        for (int step = 0; step < steps; step++)
        {
            int edge = start + 1 + step * 2;
            int next = edge + 2;
            vh.AddTriangle(start, edge, next);
            UiMesh.AddQuad(vh, edge, next, next + 1, edge + 1);
        }
    }
}
