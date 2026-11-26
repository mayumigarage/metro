using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A rectangle with round corners, filled or drawn as an outline, optionally with a
/// top-to-bottom gradient (the sign boards, their steel frames, train doors and
/// windows). Drawn as a mesh with a soft fringe, like AngularGraphic.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RoundedRectGraphic : MaskableGraphic
{
    private const int CornerSteps = 8;

    [SerializeField] private float radius;
    [Tooltip("0 = filled. Otherwise only an outline this wide is drawn.")]
    [SerializeField] private float borderWidth;
    [SerializeField] private bool useGradient;
    [Tooltip("With the gradient on, the top and bottom colours (multiplied by Color).")]
    [SerializeField] private Color topColor = Color.white;
    [SerializeField] private Color bottomColor = Color.white;

    private static readonly List<Vector2> fringe = new List<Vector2>();
    private static readonly List<Vector2> edge = new List<Vector2>();
    private static readonly List<Vector2> inner = new List<Vector2>();
    private static readonly List<Vector2> innerFringe = new List<Vector2>();

    public float Radius
    {
        get => radius;
        set
        {
            radius = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    public float BorderWidth
    {
        get => borderWidth;
        set
        {
            borderWidth = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    /// <summary>Shades from <paramref name="top"/> to <paramref name="bottom"/> (Color still tints both).</summary>
    public void SetVerticalGradient(Color top, Color bottom)
    {
        useGradient = true;
        topColor = top;
        bottomColor = bottom;
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

        float halfSize = Mathf.Min(rect.width, rect.height) * 0.5f;
        float cornerRadius = Mathf.Clamp(radius, 0f, halfSize);
        BuildOutline(rect, cornerRadius, -UiMesh.Fringe, fringe);
        BuildOutline(rect, cornerRadius, 0f, edge);
        int count = edge.Count;
        int start = vh.currentVertCount;

        if (borderWidth > 0f && borderWidth + UiMesh.Fringe < halfSize)
        {
            BuildOutline(rect, cornerRadius, borderWidth, inner);
            BuildOutline(rect, cornerRadius, borderWidth + UiMesh.Fringe, innerFringe);
            AddRing(vh, rect, fringe, 0f);
            AddRing(vh, rect, edge, 1f);
            AddRing(vh, rect, inner, 1f);
            AddRing(vh, rect, innerFringe, 0f);
            for (int layer = 0; layer < 3; layer++)
            {
                for (int index = 0; index < count; index++)
                {
                    int next = (index + 1) % count;
                    int outerRow = start + layer * count;
                    int innerRow = outerRow + count;
                    UiMesh.AddQuad(vh, outerRow + index, outerRow + next, innerRow + next, innerRow + index);
                }
            }

            return;
        }

        AddRing(vh, rect, edge, 1f);
        AddRing(vh, rect, fringe, 0f);
        int centre = vh.currentVertCount;
        vh.AddVert(rect.center, GetColor(rect, rect.center.y, 1f), Vector4.zero);
        for (int index = 0; index < count; index++)
        {
            int next = (index + 1) % count;
            vh.AddTriangle(centre, start + index, start + next);
            UiMesh.AddQuad(vh, start + index, start + next, start + count + next, start + count + index);
        }
    }

    /// <summary>The outline moved inward by <paramref name="inset"/> (outward if negative), clockwise from the top left.</summary>
    private static void BuildOutline(Rect rect, float cornerRadius, float inset, List<Vector2> result)
    {
        result.Clear();
        float r = Mathf.Max(0f, cornerRadius - inset);
        float xMin = rect.xMin + inset;
        float xMax = rect.xMax - inset;
        float yMin = rect.yMin + inset;
        float yMax = rect.yMax - inset;
        AddArc(result, new Vector2(xMin + r, yMax - r), r, 180f, 90f);
        AddArc(result, new Vector2(xMax - r, yMax - r), r, 90f, 0f);
        AddArc(result, new Vector2(xMax - r, yMin + r), r, 0f, -90f);
        AddArc(result, new Vector2(xMin + r, yMin + r), r, -90f, -180f);
    }

    private static void AddArc(List<Vector2> result, Vector2 centre, float r, float fromDegrees, float toDegrees)
    {
        for (int step = 0; step <= CornerSteps; step++)
        {
            float angle = Mathf.Lerp(fromDegrees, toDegrees, step / (float)CornerSteps) * Mathf.Deg2Rad;
            result.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r);
        }
    }

    private void AddRing(VertexHelper vh, Rect rect, List<Vector2> points, float alpha)
    {
        foreach (Vector2 point in points)
        {
            vh.AddVert(point, GetColor(rect, point.y, alpha), Vector4.zero);
        }
    }

    private Color32 GetColor(Rect rect, float y, float alpha)
    {
        Color result = useGradient
            ? Color.Lerp(bottomColor, topColor, Mathf.InverseLerp(rect.yMin, rect.yMax, y)) * color
            : color;
        result.a *= alpha;
        return result;
    }
}
