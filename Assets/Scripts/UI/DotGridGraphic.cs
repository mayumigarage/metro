using UnityEngine;
using UnityEngine.UI;

/// <summary>A faint grid of small square dots filling its rect (the title screen's ground).</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DotGridGraphic : MaskableGraphic
{
    [SerializeField] private float spacing = 36f;
    [SerializeField] private float dotSize = 3f;

    public void Configure(float gridSpacing, float size)
    {
        spacing = Mathf.Max(4f, gridSpacing);
        dotSize = Mathf.Max(0.5f, size);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Color32 dot = color;
        float half = dotSize * 0.5f;
        int columns = Mathf.FloorToInt(rect.width / spacing);
        int rows = Mathf.FloorToInt(rect.height / spacing);
        Vector2 origin = rect.center - new Vector2(columns, rows) * (spacing * 0.5f);
        for (int row = 0; row <= rows; row++)
        {
            for (int column = 0; column <= columns; column++)
            {
                Vector2 centre = origin + new Vector2(column, row) * spacing;
                int start = vh.currentVertCount;
                vh.AddVert(centre + new Vector2(-half, -half), dot, Vector4.zero);
                vh.AddVert(centre + new Vector2(-half, half), dot, Vector4.zero);
                vh.AddVert(centre + new Vector2(half, half), dot, Vector4.zero);
                vh.AddVert(centre + new Vector2(half, -half), dot, Vector4.zero);
                UiMesh.AddQuad(vh, start, start + 1, start + 2, start + 3);
            }
        }
    }
}
