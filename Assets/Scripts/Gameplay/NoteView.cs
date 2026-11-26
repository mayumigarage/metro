using UnityEngine;

/// <summary>
/// A lane note. A plain note is a white rounded bar hit with the lane button. A
/// directional note adds a chevron in its stick's colour (cyan = left stick,
/// magenta = right stick) that hangs off the lane into the open space, facing the
/// camera; it needs the lane button together with a flick of that stick. A
/// flick-only note is the chevron alone, played with the flick only. A hold note
/// is deeper violet: a bar at each end joined by a body that runs along the lane edges,
/// and after the hit the button has to stay down until the tail arrives.
/// </summary>
public sealed class NoteView : MonoBehaviour
{
    // Bar width relative to the lane, and its thickness / length along the lane (near / far).
    private const float WidthRatio = 0.88f;
    private const float FarHeight = 0.16f;
    private const float NearHeight = 0.24f;
    private const float FarDepth = 0.45f;
    private const float NearDepth = 0.80f;

    // How far the hold body floats off the lane, and the width of its edge lines at the near end.
    private const float HoldBodyLift = 0.04f;
    private const float HoldEdgeWidth = 0.14f;

    // Chevron half-width at the near end (it shrinks with the tunnel toward the far end),
    // and how far its centre sits from the lane, in chevron units.
    private const float ArrowFrontSize = 1.3f;
    private const float ArrowOffset = 1.35f;

    private static readonly Material[] barMaterials = new Material[3];
    private static readonly Material[] arrowMaterials = new Material[2];
    private static Material coreMaterial;
    private static Material holdBodyMaterial;
    private static Mesh chevronMesh;
    private static Mesh spinMesh;

    private readonly Vector3[] bodyVertices = new Vector3[12];
    private readonly Color[] bodyColors = new Color[12];

    private Bar head;
    private Bar tail;
    private Transform arrow;
    private Vector3 arrowFlip = Vector3.one;
    private Mesh bodyMesh;
    private MeshRenderer bodyRenderer;
    private Color bodyColor;

    public int Lane { get; private set; }
    public double TargetTime { get; private set; }
    public double Duration { get; private set; }
    public NoteDirection Direction { get; private set; }

    /// <summary>Stick that plays this note's direction: 0 = left, 1 = right. Need not be the lane's side.</summary>
    public int Side { get; private set; }

    /// <summary>A direction with no button: judged on the flick alone.</summary>
    public bool FlickOnly { get; private set; }

    /// <summary>0 = not a spin note, 1 = counter-clockwise, 2 = clockwise (see <see cref="TapNoteData.spin"/>).</summary>
    public int Spin { get; private set; }

    /// <summary>A spin note: no button, the stick has to be turned around.</summary>
    public bool IsSpin => Spin != 0;

    /// <summary>Degrees the stick has turned the right way inside the note's window so far.</summary>
    public float SpinProgress { get; private set; }

    /// <summary>Adds stick rotation (counter-clockwise positive); only the note's own way counts.</summary>
    public void AddSpin(float counterClockwiseDegrees)
    {
        float turned = Spin == TapNoteData.SpinClockwise ? -counterClockwiseDegrees : counterClockwiseDegrees;
        SpinProgress = Mathf.Max(0f, SpinProgress + turned);
    }

    public bool IsDirectional => Direction != NoteDirection.None;
    public bool IsHold => Duration > 0d;
    public double EndTime => TargetTime + Duration;

    /// <summary>True once the head of a hold was hit and the button is being kept down.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>True when another lane has a button note at the same time: the bar is yellow.</summary>
    public bool IsSimultaneous { get; private set; }

    public void Initialize(int lane, double targetTime, NoteDirection direction, double duration, int side, bool flickOnly, int spin = 0, bool simultaneous = false)
    {
        Lane = lane;
        TargetTime = targetTime;
        Spin = spin == TapNoteData.SpinCounterClockwise || spin == TapNoteData.SpinClockwise ? spin : 0;
        Direction = IsSpin ? NoteDirection.None : direction;
        Side = side <= 0 ? 0 : 1;
        FlickOnly = flickOnly && IsDirectional;
        Duration = FlickOnly || IsSpin ? 0d : Mathf.Max(0f, (float)duration);
        IsHeld = false;
        IsSimultaneous = simultaneous && !FlickOnly && !IsSpin;
        gameObject.name = IsSpin ? $"SpinNote_Lane{lane}_{(Spin == TapNoteData.SpinClockwise ? "CW" : "CCW")}_{targetTime:0.000}" :
            FlickOnly ? $"FlickOnlyNote_Lane{lane}_{direction}_{targetTime:0.000}" :
            IsHold ? $"HoldNote_Lane{lane}_{targetTime:0.000}" :
            IsDirectional ? $"FlickNote_Lane{lane}_{direction}_{targetTime:0.000}" :
            $"TapNote_Lane{lane}_{targetTime:0.000}";

        // The bars are built from parts; a renderer on the root (e.g. from a prefab) would cover them.
        Renderer rootRenderer = GetComponent<Renderer>();
        if (rootRenderer != null)
        {
            rootRenderer.enabled = false;
        }

        bodyColor = RhythmStageView.GetNoteColor(IsHold);
        if (!FlickOnly && !IsSpin)
        {
            head = Bar.Create(transform, IsSimultaneous ? GetSimultaneousBarMaterial() : GetBarMaterial(IsHold));
        }

        if (IsSpin)
        {
            CreateArrow(GetSpinMesh(), Quaternion.identity, Spin == TapNoteData.SpinClockwise ? new Vector3(-1f, 1f, 1f) : Vector3.one);
        }
        else if (IsDirectional)
        {
            Vector2 vector = StickInput.GetDirectionVector(Direction);
            CreateArrow(GetChevronMesh(), Quaternion.Euler(0f, 0f, Mathf.Atan2(-vector.x, vector.y) * Mathf.Rad2Deg), Vector3.one);
        }

        if (IsHold)
        {
            CreateHoldBody();
        }

        UpdateView(0f, 0f);
    }

    /// <summary>Seconds between combo steps while a hold is kept down (3 per second).</summary>
    public const double ComboTickSeconds = 1d / 3d;

    /// <summary>True once the button was let go before the end: judged already, still drawn until its end.</summary>
    public bool IsReleased { get; private set; }

    private int ticksPassed;

    /// <summary>The button was let go early: the hold dims but stays on screen.</summary>
    public void Release()
    {
        IsReleased = true;
        IsHeld = false;
    }

    /// <summary>Combo steps that came due up to <paramref name="time"/>; only those with the button down count.</summary>
    public int TakeComboTicks(double time, bool held)
    {
        int taken = 0;
        double until = System.Math.Min(time, EndTime);
        while (TargetTime + (ticksPassed + 1) * ComboTickSeconds <= until)
        {
            ticksPassed++;
            if (held)
            {
                taken++;
            }
        }

        return taken;
    }

    /// <summary>The head was hit: it now waits on the judgement line while the body runs out.</summary>
    public void BeginHold()
    {
        IsHeld = true;
        if (arrow != null)
        {
            arrow.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Places the note at <paramref name="progress"/> along its lane (0 = far, 1 = judgement
    /// line). <paramref name="tailProgress"/> is where a hold's tail is; other notes ignore it.
    /// </summary>
    public void UpdateView(float progress, float tailProgress)
    {
        Vector3 position = RhythmStageView.GetLaneWorldPosition(Lane, progress);
        Quaternion rotation = RhythmStageView.GetLaneRotation(Lane);
        if (head != null)
        {
            PlaceBar(head, progress, rotation);
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        if (arrow != null)
        {
            // The chevron hangs off the lane into the open space, facing the camera.
            float size = ArrowFrontSize * RhythmStageView.GetDepthScale(progress);
            arrow.position = position + RhythmStageView.GetLaneInward(Lane) * (size * ArrowOffset);
            arrow.localScale = new Vector3(size * arrowFlip.x, size, 1f);
        }

        if (bodyMesh == null)
        {
            return;
        }

        float tailAt = Mathf.Min(progress, tailProgress);
        SetBodyRow(0, progress);
        SetBodyRow(1, tailAt);

        // Brighter toward the head, and brighter overall while the hold is being kept.
        Color baseColor = IsHeld ? Color.Lerp(bodyColor, Color.white, 0.3f) : bodyColor;
        // A faint fill, so the edge lines along the lane read as the outline of the hold.
        SetBodyColors(0, baseColor, IsHeld ? 0.32f : 0.2f, 1f);
        SetBodyColors(1, baseColor, IsHeld ? 0.18f : 0.08f, IsHeld ? 0.85f : 0.7f);

        bodyMesh.vertices = bodyVertices;
        bodyMesh.colors = bodyColors;
        bodyMesh.RecalculateBounds();
        bodyRenderer.enabled = progress > tailAt;

        // The tail bar only shows once the end of the hold has entered the lane.
        bool tailVisible = tailProgress > 0f && tailProgress < progress;
        tail.Root.gameObject.SetActive(tailVisible);
        if (tailVisible)
        {
            PlaceBar(tail, tailProgress, rotation);
        }
    }

    private void OnDestroy()
    {
        if (arrow != null)
        {
            Destroy(arrow.gameObject);
        }

        if (tail != null)
        {
            Destroy(tail.Root.gameObject);
        }

        if (bodyRenderer != null)
        {
            Destroy(bodyRenderer.gameObject);
        }

        if (bodyMesh != null)
        {
            Destroy(bodyMesh);
        }
    }

    private void PlaceBar(Bar bar, float progress, Quaternion rotation)
    {
        bar.Place(
            RhythmStageView.GetLaneWorldPosition(Lane, progress),
            rotation,
            RhythmStageView.GetLaneWidth(progress) * WidthRatio,
            Mathf.Lerp(FarHeight, NearHeight, progress),
            Mathf.Lerp(FarDepth, NearDepth, progress));
    }

    /// <summary>
    /// Writes one row (0 = head, 1 = tail) of the hold body across the lane at
    /// <paramref name="progress"/>: the fill (vertices 0-3) and the two edge lines (4-7, 8-11).
    /// </summary>
    private void SetBodyRow(int row, float progress)
    {
        Vector3 centre = RhythmStageView.GetLaneWorldPosition(Lane, progress) + RhythmStageView.GetLaneInward(Lane) * HoldBodyLift;
        Vector3 half = new Vector3(RhythmStageView.GetLaneWidth(progress) * WidthRatio * 0.5f, 0f, 0f);
        Vector3 edge = new Vector3(HoldEdgeWidth * RhythmStageView.GetDepthScale(progress), 0f, 0f);
        int offset = row * 2;
        bodyVertices[offset] = centre - half;
        bodyVertices[offset + 1] = centre + half;
        bodyVertices[4 + offset] = centre - half;
        bodyVertices[4 + offset + 1] = centre - half + edge;
        bodyVertices[8 + offset] = centre + half - edge;
        bodyVertices[8 + offset + 1] = centre + half;
    }

    private void SetBodyColors(int row, Color color, float fillAlpha, float edgeAlpha)
    {
        Color fill = color;
        fill.a = fillAlpha;
        Color edge = color;
        edge.a = edgeAlpha;
        fill = RhythmStageView.ToVertexColor(fill);
        edge = RhythmStageView.ToVertexColor(edge);

        int offset = row * 2;
        bodyColors[offset] = fill;
        bodyColors[offset + 1] = fill;
        bodyColors[4 + offset] = edge;
        bodyColors[4 + offset + 1] = edge;
        bodyColors[8 + offset] = edge;
        bodyColors[8 + offset + 1] = edge;
    }

    private static Material GetBarMaterial(bool hold)
    {
        int index = hold ? 1 : 0;
        if (barMaterials[index] == null)
        {
            barMaterials[index] = RhythmStageView.CreateGlowMaterial(RhythmStageView.GetNoteColor(hold), RhythmStageView.NoteGlow);
        }

        return barMaterials[index];
    }

    /// <summary>The head bar of a note hit together with another lane's (the hold body and tail keep their colour).</summary>
    private static Material GetSimultaneousBarMaterial()
    {
        if (barMaterials[2] == null)
        {
            barMaterials[2] = RhythmStageView.CreateGlowMaterial(RhythmStageView.SimultaneousNoteColor, RhythmStageView.NoteGlow);
        }

        return barMaterials[2];
    }

    private static Material GetCoreMaterial()
    {
        if (coreMaterial == null)
        {
            coreMaterial = RhythmStageView.CreateGlowMaterial(Color.white, RhythmStageView.NoteGlow * 0.85f);
        }

        return coreMaterial;
    }

    private void CreateHoldBody()
    {
        if (holdBodyMaterial == null)
        {
            // White: the vertex colours carry the hold colour and the head-to-tail fade.
            holdBodyMaterial = RhythmStageView.CreateTransparentMaterial(RhythmStageView.Boost(Color.white, RhythmStageView.NoteGlow * 0.8f));
        }

        // Siblings rather than children, so moving the head does not drag them along.
        GameObject bodyObject = new GameObject($"{gameObject.name}_Body");
        bodyObject.transform.SetParent(transform.parent, false);
        bodyObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        bodyMesh = new Mesh { name = "HoldBody" };
        bodyMesh.MarkDynamic();
        bodyMesh.vertices = bodyVertices;
        bodyMesh.colors = bodyColors;

        // Fill first, edge lines after, so the lines draw over it. Both windings for each quad.
        int[] triangles = new int[36];
        for (int quad = 0; quad < 3; quad++)
        {
            int v = quad * 4;
            int t = quad * 12;
            int[] quadTriangles = { v, v + 2, v + 1, v + 1, v + 2, v + 3, v, v + 1, v + 2, v + 1, v + 3, v + 2 };
            quadTriangles.CopyTo(triangles, t);
        }

        bodyMesh.triangles = triangles;

        bodyObject.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
        bodyRenderer = bodyObject.AddComponent<MeshRenderer>();
        bodyRenderer.sharedMaterial = holdBodyMaterial;

        GameObject tailObject = new GameObject($"{gameObject.name}_Tail");
        tailObject.transform.SetParent(transform.parent, false);
        tail = Bar.Create(tailObject.transform, GetBarMaterial(true));
    }

    private void CreateArrow(Mesh mesh, Quaternion rotation, Vector3 flip)
    {
        // A sibling rather than a child, so the bar's rotation does not tilt it away from the camera.
        GameObject arrowObject = new GameObject($"{gameObject.name}_Arrow");
        arrowObject.transform.SetParent(transform.parent, false);
        arrowObject.transform.rotation = rotation;
        arrowFlip = flip;

        if (arrowMaterials[Side] == null)
        {
            arrowMaterials[Side] = RhythmStageView.CreateGlowMaterial(RhythmStageView.GetStickColor(Side), RhythmStageView.NoteGlow * 1.2f);
        }

        arrowObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        arrowObject.AddComponent<MeshRenderer>().sharedMaterial = arrowMaterials[Side];
        arrow = arrowObject.transform;
    }

    /// <summary>
    /// A right-angled chevron pointing up, 2 wide and centred on the origin, shared by
    /// every directional note. Rotated in 45° steps it gives all eight directions.
    /// </summary>
    private static Mesh GetChevronMesh()
    {
        if (chevronMesh != null)
        {
            return chevronMesh;
        }

        // Arms at 45°: an arm thickness of 0.32 drops the inner corner by 0.32 * √2.
        const float apex = 0.725f;
        const float drop = 0.45f;
        chevronMesh = new Mesh { name = "FlickChevron" };
        chevronMesh.vertices = new[]
        {
            new Vector3(-1f, apex - 1f, 0f),
            new Vector3(0f, apex, 0f),
            new Vector3(0f, apex - drop, 0f),
            new Vector3(-1f, apex - 1f - drop, 0f),
            new Vector3(1f, apex - 1f, 0f),
            new Vector3(1f, apex - 1f - drop, 0f)
        };

        // Left arm 0-1-2-3, right arm 1-4-5-2; both windings so it shows whichever way it turns.
        chevronMesh.triangles = new[]
        {
            0, 1, 2, 0, 2, 3, 1, 4, 5, 1, 5, 2,
            0, 2, 1, 0, 3, 2, 1, 5, 4, 1, 2, 5
        };
        chevronMesh.RecalculateBounds();
        return chevronMesh;
    }

    /// <summary>
    /// The spin mark, counter-clockwise: a thick arc from the lower right over the top to the
    /// lower left with a round start and an arrowhead pointing down-left at the end, 2 wide
    /// and centred on the origin. Clockwise is this mesh mirrored (see <c>arrowFlip</c>).
    /// </summary>
    private static Mesh GetSpinMesh()
    {
        if (spinMesh != null)
        {
            return spinMesh;
        }

        const float thickness = 0.3f;
        const float startDegrees = -15f;
        const float endDegrees = 195f;
        const float centreShift = -0.4f;
        const int segments = 24;
        const int capSegments = 8;

        System.Collections.Generic.List<Vector3> vertices = new System.Collections.Generic.List<Vector3>();
        System.Collections.Generic.List<int> triangles = new System.Collections.Generic.List<int>();

        Vector3 OnArc(float degrees, float radius)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians) * radius, Mathf.Sin(radians) * radius + centreShift, 0f);
        }

        void AddTriangle(int a, int b, int c)
        {
            // Both windings, so it shows whichever way it turns.
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
        }

        // The arc strip.
        for (int index = 0; index <= segments; index++)
        {
            float degrees = Mathf.Lerp(startDegrees, endDegrees, index / (float)segments);
            vertices.Add(OnArc(degrees, 1f - thickness * 0.5f));
            vertices.Add(OnArc(degrees, 1f + thickness * 0.5f));
        }

        for (int index = 0; index < segments; index++)
        {
            int v = index * 2;
            AddTriangle(v, v + 1, v + 2);
            AddTriangle(v + 1, v + 3, v + 2);
        }

        // A round start: a half disc turned away from the arc.
        int capCentre = vertices.Count;
        vertices.Add(OnArc(startDegrees, 1f));
        for (int index = 0; index <= capSegments; index++)
        {
            float around = (startDegrees - 180f) + 180f * index / capSegments;
            float radians = around * Mathf.Deg2Rad;
            Vector3 centre = vertices[capCentre];
            vertices.Add(centre + new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * (thickness * 0.5f));
        }

        for (int index = 0; index < capSegments; index++)
        {
            AddTriangle(capCentre, capCentre + 1 + index, capCentre + 2 + index);
        }

        // The arrowhead: its base straddles the arc's end, its tip points the way the arc turns (counter-clockwise).
        float endRadians = endDegrees * Mathf.Deg2Rad;
        Vector3 radial = new Vector3(Mathf.Cos(endRadians), Mathf.Sin(endRadians), 0f);
        Vector3 tangent = new Vector3(-Mathf.Sin(endRadians), Mathf.Cos(endRadians), 0f);
        Vector3 endPoint = OnArc(endDegrees, 1f);
        int head = vertices.Count;
        vertices.Add(endPoint + radial * 0.62f);
        vertices.Add(endPoint - radial * 0.62f);
        vertices.Add(endPoint + tangent * 0.85f);
        AddTriangle(head, head + 1, head + 2);

        spinMesh = new Mesh { name = "SpinMark" };
        spinMesh.SetVertices(vertices);
        spinMesh.SetTriangles(triangles, 0);
        spinMesh.RecalculateBounds();
        return spinMesh;
    }

    /// <summary>
    /// A bar with rounded ends: a box between two upright cylinders, plus a white-hot
    /// strip through the middle so it reads as lit from inside. The parts are sized
    /// directly (the root is never scaled), so the ends stay round at any width.
    /// </summary>
    private sealed class Bar
    {
        private Transform middle;
        private Transform leftCap;
        private Transform rightCap;
        private Transform core;

        public Transform Root { get; private set; }

        public static Bar Create(Transform root, Material material)
        {
            return new Bar
            {
                Root = root,
                middle = CreatePart(PrimitiveType.Cube, "Middle", root, material),
                leftCap = CreatePart(PrimitiveType.Cylinder, "LeftCap", root, material),
                rightCap = CreatePart(PrimitiveType.Cylinder, "RightCap", root, material),
                core = CreatePart(PrimitiveType.Cube, "Core", root, GetCoreMaterial())
            };
        }

        /// <summary>Width across the lane, height off it and depth along it, all in world units.</summary>
        public void Place(Vector3 position, Quaternion rotation, float width, float height, float depth)
        {
            Root.SetPositionAndRotation(position, rotation);
            Root.localScale = Vector3.one;

            float radius = Mathf.Min(width, depth) * 0.5f;
            float straight = Mathf.Max(0f, width - radius * 2f);
            middle.localScale = new Vector3(straight, height, depth);

            // The cylinder primitive is 1 across and 2 tall along its own Y (the lane normal here).
            Vector3 capScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            leftCap.localPosition = new Vector3(-straight * 0.5f, 0f, 0f);
            leftCap.localScale = capScale;
            rightCap.localPosition = new Vector3(straight * 0.5f, 0f, 0f);
            rightCap.localScale = capScale;

            core.localScale = new Vector3(straight + radius, height * 1.12f, depth * 0.22f);
        }

        private static Transform CreatePart(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part.transform;
        }
    }
}
