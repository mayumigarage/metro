using UnityEngine;

/// <summary>
/// A lane note. A plain note is a slab hit with the lane button; a directional
/// note also carries a camera-facing arrow and needs the lane button together
/// with a flick of the side's stick in that direction.
/// </summary>
public sealed class NoteView : MonoBehaviour
{
    private const float WidthRatio = 0.6f;

    // Arrow half-length at the near end; it shrinks with the tunnel toward the far end.
    private const float ArrowFrontSize = 0.85f;
    private const float ArrowGapRatio = 0.3f;

    // Plain notes follow the button side: L / ZL orange, R / ZR cyan.
    private static readonly Color[] TapColors =
    {
        new Color(1f, 0.52f, 0.28f, 1f),
        new Color(0.32f, 0.92f, 1f, 1f)
    };

    private static readonly Material[] tapMaterials = new Material[2];
    private static readonly Material[] directionalMaterials = new Material[2];
    private static Material arrowMaterial;
    private static Mesh arrowMesh;

    private Transform arrow;

    public int Lane { get; private set; }
    public double TargetTime { get; private set; }
    public NoteDirection Direction { get; private set; }

    public bool IsDirectional => Direction != NoteDirection.None;

    /// <summary>Stick that plays this note when it is directional: 0 = left, 1 = right.</summary>
    public int Side => RhythmStageView.GetStickSide(Lane);

    public void Initialize(int lane, double targetTime, NoteDirection direction)
    {
        Lane = lane;
        TargetTime = targetTime;
        Direction = direction;
        gameObject.name = IsDirectional
            ? $"FlickNote_Lane{lane}_{direction}_{targetTime:0.000}"
            : $"TapNote_Lane{lane}_{targetTime:0.000}";

        Renderer noteRenderer = GetComponent<Renderer>();
        if (noteRenderer != null)
        {
            noteRenderer.sharedMaterial = GetBodyMaterial();
        }

        if (IsDirectional)
        {
            CreateArrow();
        }

        UpdateView(0f);
    }

    /// <summary>Places the note at <paramref name="progress"/> along its lane (0 = far, 1 = judgement line).</summary>
    public void UpdateView(float progress)
    {
        Vector3 position = RhythmStageView.GetLaneWorldPosition(Lane, progress);
        transform.SetPositionAndRotation(position, RhythmStageView.GetLaneRotation(Lane));
        transform.localScale = new Vector3(
            RhythmStageView.GetLaneWidth(progress) * WidthRatio,
            Mathf.Lerp(0.16f, 0.24f, progress),
            Mathf.Lerp(0.45f, 0.80f, progress));

        if (arrow != null)
        {
            // The arrow hangs off the lane into the open space, facing the camera.
            float size = ArrowFrontSize * RhythmStageView.GetDepthScale(progress);
            arrow.position = position + RhythmStageView.GetLaneInward(Lane) * (size * (1f + ArrowGapRatio));
            arrow.localScale = new Vector3(size, size, 1f);
        }
    }

    private void OnDestroy()
    {
        if (arrow != null)
        {
            Destroy(arrow.gameObject);
        }
    }

    private Material GetBodyMaterial()
    {
        Material[] cache = IsDirectional ? directionalMaterials : tapMaterials;
        int side = Side;
        if (cache[side] == null)
        {
            cache[side] = RhythmStageView.CreateMaterial(IsDirectional ? RhythmStageView.GetStickColor(side) : TapColors[side]);
        }

        return cache[side];
    }

    private void CreateArrow()
    {
        // A sibling rather than a child, so the slab's rotation and stretch do not skew it.
        GameObject arrowObject = new GameObject($"{gameObject.name}_Arrow");
        arrowObject.transform.SetParent(transform.parent, false);
        arrowObject.transform.rotation = Quaternion.Euler(0f, 0f, GetArrowAngle(Direction));

        if (arrowMaterial == null)
        {
            arrowMaterial = RhythmStageView.CreateMaterial(Color.white);
        }

        arrowObject.AddComponent<MeshFilter>().sharedMesh = GetArrowMesh();
        arrowObject.AddComponent<MeshRenderer>().sharedMaterial = arrowMaterial;
        arrow = arrowObject.transform;
    }

    private static float GetArrowAngle(NoteDirection direction)
    {
        switch (direction)
        {
            case NoteDirection.Left:
                return 90f;
            case NoteDirection.Down:
                return 180f;
            case NoteDirection.Right:
                return -90f;
            default:
                return 0f;
        }
    }

    /// <summary>Block arrow pointing up, spanning -1..1, shared by every directional note.</summary>
    private static Mesh GetArrowMesh()
    {
        if (arrowMesh != null)
        {
            return arrowMesh;
        }

        const float shaftHalfWidth = 0.34f;
        const float headHalfWidth = 0.9f;
        const float neck = 0f;
        arrowMesh = new Mesh { name = "FlickArrow" };
        arrowMesh.vertices = new[]
        {
            new Vector3(-shaftHalfWidth, -1f, 0f),
            new Vector3(-shaftHalfWidth, neck, 0f),
            new Vector3(-headHalfWidth, neck, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(headHalfWidth, neck, 0f),
            new Vector3(shaftHalfWidth, neck, 0f),
            new Vector3(shaftHalfWidth, -1f, 0f)
        };

        // Both windings, so the arrow is visible whichever way it is rotated.
        arrowMesh.triangles = new[]
        {
            0, 1, 5, 0, 5, 6, 2, 3, 4,
            0, 5, 1, 0, 6, 5, 2, 4, 3
        };
        arrowMesh.RecalculateBounds();
        return arrowMesh;
    }
}
