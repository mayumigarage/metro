using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mesh helpers shared by the procedural UI graphics (CircleGraphic, RoundedRectGraphic,
/// ChevronGraphic, HatchGraphic). Every shape gets the same 1-unit transparent fringe
/// AngularGraphic uses, so edges look smooth without MSAA.
/// </summary>
public static class UiMesh
{
    public const float Fringe = 1f;

    private static readonly List<Vector2> fringe = new List<Vector2>();

    /// <summary>
    /// Adds a filled polygon (either winding) with a soft outer fringe. It is triangulated
    /// as a fan from the first point, so every point must be visible from it: any convex
    /// polygon, or a chevron listed from its notch.
    /// </summary>
    public static void AddFanPolygon(VertexHelper vh, List<Vector2> points, Color32 color)
    {
        int count = points.Count;
        if (count < 3)
        {
            return;
        }

        bool clockwise = SignedArea(points) < 0f;
        fringe.Clear();
        for (int index = 0; index < count; index++)
        {
            Vector2 previous = points[(index + count - 1) % count];
            Vector2 current = points[index];
            Vector2 next = points[(index + 1) % count];
            Vector2 before = (current - previous).normalized;
            Vector2 after = (next - current).normalized;

            // Outward normals: going clockwise the inside is on the right of each edge.
            Vector2 normalBefore = clockwise ? new Vector2(-before.y, before.x) : new Vector2(before.y, -before.x);
            Vector2 normalAfter = clockwise ? new Vector2(-after.y, after.x) : new Vector2(after.y, -after.x);
            float denominator = Mathf.Max(0.2f, 1f + Vector2.Dot(normalBefore, normalAfter));
            fringe.Add(current + (normalBefore + normalAfter) * (Fringe / denominator));
        }

        Color32 clear = color;
        clear.a = 0;
        int start = vh.currentVertCount;
        foreach (Vector2 point in points)
        {
            vh.AddVert(point, color, Vector4.zero);
        }

        foreach (Vector2 point in fringe)
        {
            vh.AddVert(point, clear, Vector4.zero);
        }

        for (int index = 1; index < count - 1; index++)
        {
            vh.AddTriangle(start, start + index, start + index + 1);
        }

        for (int index = 0; index < count; index++)
        {
            int next = (index + 1) % count;
            AddQuad(vh, start + index, start + next, start + count + next, start + count + index);
        }
    }

    /// <summary>
    /// Adds an arc of a ring between <paramref name="fromDegrees"/> and <paramref name="toDegrees"/>
    /// (counter-clockwise from +x), with fringes on its inner and outer edge.
    /// </summary>
    public static void AddRing(VertexHelper vh, Vector2 centre, float outerRadius, float thickness, float fromDegrees, float toDegrees, int steps, Color32 color)
    {
        Color32 clear = color;
        clear.a = 0;
        float innerRadius = Mathf.Max(0f, outerRadius - thickness);
        int start = vh.currentVertCount;
        for (int step = 0; step <= steps; step++)
        {
            float angle = Mathf.Lerp(fromDegrees, toDegrees, step / (float)steps) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(centre + direction * (outerRadius + Fringe), clear, Vector4.zero);
            vh.AddVert(centre + direction * outerRadius, color, Vector4.zero);
            vh.AddVert(centre + direction * innerRadius, color, Vector4.zero);
            vh.AddVert(centre + direction * Mathf.Max(0f, innerRadius - Fringe), clear, Vector4.zero);
        }

        for (int step = 0; step < steps; step++)
        {
            int current = start + step * 4;
            int next = current + 4;
            for (int layer = 0; layer < 3; layer++)
            {
                AddQuad(vh, current + layer, next + layer, next + layer + 1, current + layer + 1);
            }
        }
    }

    public static void AddQuad(VertexHelper vh, int a, int b, int c, int d)
    {
        vh.AddTriangle(a, b, c);
        vh.AddTriangle(a, c, d);
    }

    private static float SignedArea(List<Vector2> points)
    {
        float area = 0f;
        for (int index = 0; index < points.Count; index++)
        {
            Vector2 current = points[index];
            Vector2 next = points[(index + 1) % points.Count];
            area += current.x * next.y - next.x * current.y;
        }

        return area * 0.5f;
    }
}
