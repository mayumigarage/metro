using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A rectangle that is solid along one vertical edge and fades to nothing toward the other,
/// like a light spilling in from the edge of the screen (the laser warning at the sides of
/// the stage). Colour is <see cref="Graphic.color"/>; its alpha is the strength at the edge.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EdgeGlowGraphic : MaskableGraphic
{
    [Tooltip("true: strongest on the right edge, fading toward the left.")]
    [SerializeField] private bool strongOnRight;

    public bool StrongOnRight
    {
        get => strongOnRight;
        set
        {
            strongOnRight = value;
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

        Color32 strong = color;
        Color32 clear = color;
        clear.a = 0;
        Color32 left = strongOnRight ? clear : strong;
        Color32 right = strongOnRight ? strong : clear;

        vh.AddVert(new Vector2(rect.xMin, rect.yMin), left, Vector4.zero);
        vh.AddVert(new Vector2(rect.xMin, rect.yMax), left, Vector4.zero);
        vh.AddVert(new Vector2(rect.xMax, rect.yMax), right, Vector4.zero);
        vh.AddVert(new Vector2(rect.xMax, rect.yMin), right, Vector4.zero);
        UiMesh.AddQuad(vh, 0, 1, 2, 3);
    }
}
