using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A thick route-map line: a polyline through corner points whose corners are rounded
/// into arcs, with round ends. Only the part between <see cref="From"/> and <see cref="To"/>
/// (distances along the line) is drawn, so it can grow in from one end and run off the
/// other (the title screen's background). Points are in the graphic's local units.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RouteLineGraphic : MaskableGraphic
{
    private const float ArcStepLength = 6f;
    private const int CapSteps = 20;

    private readonly List<Vector2> samples = new List<Vector2>();
    private readonly List<float> distances = new List<float>();
    private readonly List<Vector2> visible = new List<Vector2>();
    private float width = 20f;
    private float from;
    private float to;

    /// <summary>Length of the whole line.</summary>
    public float Length => distances.Count > 0 ? distances[distances.Count - 1] : 0f;

    public float From => from;
    public float To => to;

    public float Width
    {
        get => width;
        set
        {
            width = Mathf.Max(1f, value);
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// Sets the line through <paramref name="corners"/>; every inner corner becomes an arc of
    /// <paramref name="radius"/> (smaller where the segments are too short for it).
    /// </summary>
    public void SetPath(IList<Vector2> corners, float radius)
    {
        samples.Clear();
        distances.Clear();
        if (corners.Count < 2)
        {
            SetVerticesDirty();
            return;
        }

        AddSample(corners[0]);
        for (int index = 1; index < corners.Count - 1; index++)
        {
            Vector2 before = corners[index - 1];
            Vector2 corner = corners[index];
            Vector2 after = corners[index + 1];
            Vector2 inDirection = (corner - before).normalized;
            Vector2 outDirection = (after - corner).normalized;
            float turn = Vector2.Angle(inDirection, outDirection) * Mathf.Deg2Rad;
            if (turn < 0.01f)
            {
                AddSample(corner);
                continue;
            }

            float tangent = Mathf.Tan(turn * 0.5f);
            float cut = Mathf.Min(radius * tangent, (corner - before).magnitude * 0.5f, (after - corner).magnitude * 0.5f);
            float arcRadius = cut / tangent;
            Vector2 arcStart = corner - inDirection * cut;
            float side = inDirection.x * outDirection.y - inDirection.y * outDirection.x > 0f ? 1f : -1f;
            Vector2 normal = new Vector2(-inDirection.y, inDirection.x) * side;
            Vector2 centre = arcStart + normal * arcRadius;
            Vector2 spoke = arcStart - centre;
            int steps = Mathf.Max(3, Mathf.CeilToInt(turn * arcRadius / ArcStepLength));
            for (int step = 0; step <= steps; step++)
            {
                float angle = turn * side * step / steps;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                AddSample(centre + new Vector2(spoke.x * cos - spoke.y * sin, spoke.x * sin + spoke.y * cos));
            }
        }

        AddSample(corners[corners.Count - 1]);
        SetVerticesDirty();
    }

    /// <summary>Draws only the part from <paramref name="fromDistance"/> to <paramref name="toDistance"/> along the line.</summary>
    public void SetRange(float fromDistance, float toDistance)
    {
        fromDistance = Mathf.Clamp(fromDistance, 0f, Length);
        toDistance = Mathf.Clamp(toDistance, fromDistance, Length);
        if (Mathf.Approximately(fromDistance, from) && Mathf.Approximately(toDistance, to))
        {
            return;
        }

        from = fromDistance;
        to = toDistance;
        SetVerticesDirty();
    }

    /// <summary>The point <paramref name="distance"/> along the line.</summary>
    public Vector2 GetPoint(float distance)
    {
        if (samples.Count == 0)
        {
            return Vector2.zero;
        }

        int index = FindSegment(distance);
        if (index >= samples.Count - 1)
        {
            return samples[samples.Count - 1];
        }

        float span = distances[index + 1] - distances[index];
        float t = span > 0f ? (distance - distances[index]) / span : 0f;
        return Vector2.Lerp(samples[index], samples[index + 1], Mathf.Clamp01(t));
    }

    /// <summary>The line's direction (unit) <paramref name="distance"/> along it.</summary>
    public Vector2 GetDirection(float distance)
    {
        if (samples.Count < 2)
        {
            return Vector2.right;
        }

        int index = Mathf.Min(FindSegment(distance), samples.Count - 2);
        return (samples[index + 1] - samples[index]).normalized;
    }

    private void AddSample(Vector2 point)
    {
        if (samples.Count > 0)
        {
            Vector2 last = samples[samples.Count - 1];
            float step = (point - last).magnitude;
            if (step < 0.01f)
            {
                return;
            }

            distances.Add(distances[distances.Count - 1] + step);
        }
        else
        {
            distances.Add(0f);
        }

        samples.Add(point);
    }

    /// <summary>Index of the sample the segment holding <paramref name="distance"/> starts at.</summary>
    private int FindSegment(float distance)
    {
        for (int index = 1; index < distances.Count; index++)
        {
            if (distances[index] >= distance)
            {
                return index - 1;
            }
        }

        return distances.Count - 1;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (samples.Count < 2 || to - from < 0.5f)
        {
            return;
        }

        visible.Clear();
        visible.Add(GetPoint(from));
        for (int index = 0; index < samples.Count; index++)
        {
            if (distances[index] > from + 0.01f && distances[index] < to - 0.01f)
            {
                visible.Add(samples[index]);
            }
        }

        visible.Add(GetPoint(to));

        Color32 solid = color;
        Color32 clear = solid;
        clear.a = 0;
        float half = width * 0.5f;
        int start = vh.currentVertCount;
        for (int index = 0; index < visible.Count; index++)
        {
            Vector2 previous = visible[Mathf.Max(0, index - 1)];
            Vector2 next = visible[Mathf.Min(visible.Count - 1, index + 1)];
            Vector2 tangent = (next - previous).normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            Vector2 point = visible[index];
            vh.AddVert(point + normal * (half + UiMesh.Fringe), clear, Vector4.zero);
            vh.AddVert(point + normal * half, solid, Vector4.zero);
            vh.AddVert(point - normal * half, solid, Vector4.zero);
            vh.AddVert(point - normal * (half + UiMesh.Fringe), clear, Vector4.zero);
        }

        for (int index = 0; index < visible.Count - 1; index++)
        {
            int current = start + index * 4;
            int next = current + 4;
            for (int layer = 0; layer < 3; layer++)
            {
                UiMesh.AddQuad(vh, current + layer, next + layer, next + layer + 1, current + layer + 1);
            }
        }

        AddCap(vh, visible[0], half, solid);
        AddCap(vh, visible[visible.Count - 1], half, solid);
    }

    private static void AddCap(VertexHelper vh, Vector2 centre, float radius, Color32 color)
    {
        Color32 clear = color;
        clear.a = 0;
        int start = vh.currentVertCount;
        vh.AddVert(centre, color, Vector4.zero);
        for (int step = 0; step <= CapSteps; step++)
        {
            float angle = step * Mathf.PI * 2f / CapSteps;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(centre + direction * radius, color, Vector4.zero);
            vh.AddVert(centre + direction * (radius + UiMesh.Fringe), clear, Vector4.zero);
        }

        for (int step = 0; step < CapSteps; step++)
        {
            int edge = start + 1 + step * 2;
            int next = edge + 2;
            vh.AddTriangle(start, edge, next);
            UiMesh.AddQuad(vh, edge, next, next + 1, edge + 1);
        }
    }
}
