using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A thick "&gt;" filling its rect and pointing right, like the chevrons between the
/// station numbers on a platform sign, or a filled triangle (thickness 0). Rotate the
/// RectTransform to point it elsewhere.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChevronGraphic : MaskableGraphic
{
    [Tooltip("Horizontal width of each arm. 0 (or the full width) = a filled triangle.")]
    [SerializeField] private float thickness;

    private static readonly List<Vector2> points = new List<Vector2>();

    public float Thickness
    {
        get => thickness;
        set
        {
            thickness = Mathf.Max(0f, value);
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

        float middle = rect.center.y;
        points.Clear();
        if (thickness <= 0f || thickness >= rect.width)
        {
            points.Add(new Vector2(rect.xMin, rect.yMax));
            points.Add(new Vector2(rect.xMax, middle));
            points.Add(new Vector2(rect.xMin, rect.yMin));
        }
        else
        {
            // From the notch, so the fan reaches every corner of both arms.
            points.Add(new Vector2(rect.xMax - thickness, middle));
            points.Add(new Vector2(rect.xMin, rect.yMax));
            points.Add(new Vector2(rect.xMin + thickness, rect.yMax));
            points.Add(new Vector2(rect.xMax, middle));
            points.Add(new Vector2(rect.xMin + thickness, rect.yMin));
            points.Add(new Vector2(rect.xMin, rect.yMin));
        }

        UiMesh.AddFanPolygon(vh, points, color);
    }
}
