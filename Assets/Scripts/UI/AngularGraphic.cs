using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The angular panel every piece of UI is made of: a rectangle with any of its
/// corners cut off, optionally slanted (skew) and optionally drawn as an outline
/// only. Drawn as a mesh, so it needs no sprite and scales to any size.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class AngularGraphic : MaskableGraphic
{
    // Width of the transparent fringe that softens the slanted edges.
    private const float FringeWidth = 1f;

    [SerializeField] private float cutTopLeft;
    [SerializeField] private float cutTopRight;
    [SerializeField] private float cutBottomRight;
    [SerializeField] private float cutBottomLeft;
    [Tooltip("How far the top edge leans to the right of the bottom edge.")]
    [SerializeField] private float skew;
    [Tooltip("0 = filled. Otherwise only an outline this wide is drawn.")]
    [SerializeField] private float borderWidth;
    [SerializeField] private bool useGradient;
    [Tooltip("Colour at the right edge when the gradient is on (the left edge uses Color).")]
    [SerializeField] private Color gradientColor = Color.clear;

    private static readonly List<Vector2> shape = new List<Vector2>();
    private static readonly List<Vector2> inner = new List<Vector2>();
    private static readonly List<Vector2> outer = new List<Vector2>();

    /// <summary>Corner cuts in canvas units, clockwise from the top left.</summary>
    public void SetCuts(float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        cutTopLeft = topLeft;
        cutTopRight = topRight;
        cutBottomRight = bottomRight;
        cutBottomLeft = bottomLeft;
        SetVerticesDirty();
    }

    public void SetSkew(float value)
    {
        skew = value;
        SetVerticesDirty();
    }

    /// <summary>Draws only an outline of <paramref name="width"/> (0 fills the shape again).</summary>
    public void SetBorder(float width)
    {
        borderWidth = Mathf.Max(0f, width);
        SetVerticesDirty();
    }

    /// <summary>Fades from Color on the left to <paramref name="rightColor"/> on the right.</summary>
    public void SetGradient(Color rightColor)
    {
        useGradient = true;
        gradientColor = rightColor;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        BuildShape(rect);
        if (shape.Count < 3)
        {
            return;
        }

        Offset(shape, -FringeWidth, outer);
        int count = shape.Count;
        if (borderWidth > 0f)
        {
            Offset(shape, borderWidth, inner);
            AddRing(vh, rect, shape, 1f);
            AddRing(vh, rect, inner, 1f);
            AddRing(vh, rect, outer, 0f);
            for (int index = 0; index < count; index++)
            {
                int next = (index + 1) % count;
                AddQuad(vh, index, next, count + next, count + index);
                AddQuad(vh, index, next, count * 2 + next, count * 2 + index);
            }

            return;
        }

        AddRing(vh, rect, shape, 1f);
        AddRing(vh, rect, outer, 0f);
        Vector2 centre = Vector2.zero;
        foreach (Vector2 point in shape)
        {
            centre += point;
        }

        centre /= count;
        vh.AddVert(centre, GetColor(rect, centre.x, 1f), Vector4.zero);
        int centreIndex = count * 2;
        for (int index = 0; index < count; index++)
        {
            int next = (index + 1) % count;
            vh.AddTriangle(centreIndex, index, next);
            AddQuad(vh, index, next, count + next, count + index);
        }
    }

    /// <summary>The outline, clockwise from the top left, without zero-length edges.</summary>
    private void BuildShape(Rect rect)
    {
        shape.Clear();
        float limit = Mathf.Min(rect.width, rect.height) * 0.5f;
        float topLeft = Mathf.Clamp(cutTopLeft, 0f, limit);
        float topRight = Mathf.Clamp(cutTopRight, 0f, limit);
        float bottomRight = Mathf.Clamp(cutBottomRight, 0f, limit);
        float bottomLeft = Mathf.Clamp(cutBottomLeft, 0f, limit);

        AddCorner(rect, new Vector2(rect.xMin, rect.yMax - topLeft), new Vector2(rect.xMin + topLeft, rect.yMax));
        AddCorner(rect, new Vector2(rect.xMax - topRight, rect.yMax), new Vector2(rect.xMax, rect.yMax - topRight));
        AddCorner(rect, new Vector2(rect.xMax, rect.yMin + bottomRight), new Vector2(rect.xMax - bottomRight, rect.yMin));
        AddCorner(rect, new Vector2(rect.xMin + bottomLeft, rect.yMin), new Vector2(rect.xMin, rect.yMin + bottomLeft));
    }

    private void AddCorner(Rect rect, Vector2 first, Vector2 second)
    {
        AddPoint(rect, first);
        if ((second - first).sqrMagnitude > 0.0001f)
        {
            AddPoint(rect, second);
        }
    }

    private void AddPoint(Rect rect, Vector2 point)
    {
        point.x += skew * ((point.y - rect.center.y) / rect.height);
        shape.Add(point);
    }

    /// <summary>
    /// Moves every edge of a clockwise convex outline inward by <paramref name="distance"/>
    /// (outward if negative) and writes the new corners to <paramref name="result"/>.
    /// </summary>
    private static void Offset(List<Vector2> source, float distance, List<Vector2> result)
    {
        result.Clear();
        int count = source.Count;
        for (int index = 0; index < count; index++)
        {
            Vector2 previous = source[(index + count - 1) % count];
            Vector2 current = source[index];
            Vector2 next = source[(index + 1) % count];

            // Going clockwise the inside is on the right of each edge.
            Vector2 before = (current - previous).normalized;
            Vector2 after = (next - current).normalized;
            Vector2 normalBefore = new Vector2(before.y, -before.x);
            Vector2 normalAfter = new Vector2(after.y, -after.x);
            float denominator = Mathf.Max(0.2f, 1f + Vector2.Dot(normalBefore, normalAfter));
            result.Add(current + (normalBefore + normalAfter) * (distance / denominator));
        }
    }

    private void AddRing(VertexHelper vh, Rect rect, List<Vector2> points, float alpha)
    {
        foreach (Vector2 point in points)
        {
            vh.AddVert(point, GetColor(rect, point.x, alpha), Vector4.zero);
        }
    }

    private static void AddQuad(VertexHelper vh, int a, int b, int c, int d)
    {
        vh.AddTriangle(a, b, c);
        vh.AddTriangle(a, c, d);
    }

    private Color32 GetColor(Rect rect, float x, float alpha)
    {
        Color result = useGradient
            ? Color.Lerp(color, gradientColor, Mathf.InverseLerp(rect.xMin, rect.xMax, x))
            : color;
        result.a *= alpha;
        return result;
    }
}
