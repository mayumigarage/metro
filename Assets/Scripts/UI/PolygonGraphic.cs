using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A filled polygon given in units from the rect's centre, with the soft fringe of the
/// other procedural graphics. Triangulated as a fan from the first point, so every point
/// must be visible from it (any convex shape, or a notched arrow band listed from its notch).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PolygonGraphic : MaskableGraphic
{
    private readonly List<Vector2> points = new List<Vector2>();
    private static readonly List<Vector2> placed = new List<Vector2>();

    public void SetPoints(IEnumerable<Vector2> newPoints)
    {
        points.Clear();
        points.AddRange(newPoints);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Vector2 centre = GetPixelAdjustedRect().center;
        placed.Clear();
        foreach (Vector2 point in points)
        {
            placed.Add(centre + point);
        }

        UiMesh.AddFanPolygon(vh, placed, color);
    }
}
