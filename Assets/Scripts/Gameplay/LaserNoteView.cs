using System;
using UnityEngine;

/// <summary>
/// A laser flying through the space between the upper and lower lanes. Its path is
/// a list of timed points in stick units; the part that has not yet reached the
/// judgement plane is drawn.
///
/// It is not aimed at: while it crosses the judgement plane the player leans the
/// side's stick the way the laser is moving (any of eight directions, within the
/// STICK ANGLE option), and as long as that matches the laser counts as followed
/// exactly. A stretch that holds still needs nothing. While followed the laser
/// shows stronger, otherwise it fades; a rounded-square marker shows where it is now, with
/// an arrow on a dark round plate beside it pointing the way the laser turns next.
/// The path is drawn as a round glowing tube: a soft outer glow, a band shaded like a
/// cylinder (bright middle, darker soft rims) and a hot core line.
///
/// Depth is shown by a colour gradient along the ribbon: the far end is dark, faint and
/// shifted toward deep blue / violet (it sinks into the black stage), the middle is the
/// stick's own colour, and the part about to arrive is solid and pale, with a hotter
/// core line through the band. The stick's colour is the LASER COLOR option.
/// </summary>
public sealed class LaserNoteView : MonoBehaviour
{
    /// <summary>
    /// Seconds either side of now whose movement also counts, so a corner can be
    /// taken a moment early or late and the first instant of a move is free.
    /// </summary>
    public const double DirectionGraceSeconds = 0.08d;

    private const int SegmentSubdivisions = 6;

    // Half widths of the three ribbons: a soft outer glow, the shaded tube and its hot core.
    private const float GlowHalfWidth = 0.52f;
    private const float BandHalfWidth = 0.32f;
    private const float CoreHalfWidth = 0.10f;

    // The marker is a rounded square ring with a rounded square inside; half the side length.
    private const float MarkerHalfSize = 0.24f;
    private const float MarkerCornerRadius = 0.09f;
    private const float MarkerRingWidth = 0.045f;
    private const float MarkerDotHalfSize = 0.085f;

    // The next-turn arrow: a notched arrowhead with a dark outline on a dark round plate,
    // just outside the marker, tip pointing along the new direction.
    private const float TurnArrowGap = 0.04f;
    private const float TurnArrowLength = 0.36f;
    private const float TurnArrowHalfWidth = 0.27f;
    private const float TurnPlateRadius = 0.40f;

    // A later move counts as a turn when it points this far (degrees) off the current one.
    private const float TurnMinAngle = 15f;

    // The triangle is at full strength this close (seconds) before the turn and softer farther out.
    private const double TurnNearSeconds = 0.5d;

    // Ribbon brightness while approaching, while followed and while not followed,
    // and how fast it changes between them (per second).
    private const float ApproachBrightness = 1f;
    private const float FollowedBrightness = 1.7f;
    private const float MissedBrightness = 0.28f;
    private const float BrightnessSpeed = 14f;

    // Depth colouring: hue the far end leans toward, by how much, and where the
    // stick's own colour sits along the way (0 = far, 1 = judgement plane).
    private const float FarHueDegrees = 250f;
    private const float FarHueShiftDegrees = 42f;
    private const float MidProgress = 0.5f;

    // HDR brightness of the core line, so it blooms on the dark passage.
    private const float CoreGlow = 1.5f;

    /// <summary>
    /// A strip along the laser's path, turned to face the camera, cut lengthwise into columns
    /// whose colour is scaled by a cross profile. A profile bright in the middle and fading
    /// to nothing at both edges makes the strip read as a round, glowing tube, not a flat band.
    /// </summary>
    private sealed class Ribbon
    {
        private readonly Mesh mesh;
        private readonly Vector3[] vertices;
        private readonly Color[] colors;
        private readonly MeshRenderer renderer;
        private readonly float[] offsets;
        private readonly Color[] profile;

        /// <param name="columnOffsets">Positions across the strip, -1 (one edge) to 1 (the other).</param>
        /// <param name="columnFactors">Multiplier on the point's colour (RGB and alpha) in each column.</param>
        public Ribbon(Transform parent, string name, Material material, int maxPoints, int sortingOrder, float[] columnOffsets, Color[] columnFactors)
        {
            GameObject ribbonObject = new GameObject(name);
            ribbonObject.transform.SetParent(parent, false);
            offsets = columnOffsets;
            profile = columnFactors;
            int columns = offsets.Length;

            // Both windings, so it shows from either side.
            vertices = new Vector3[maxPoints * columns];
            colors = new Color[maxPoints * columns];
            int[] triangles = new int[(maxPoints - 1) * (columns - 1) * 12];
            int triangle = 0;
            for (int index = 0; index < maxPoints - 1; index++)
            {
                for (int column = 0; column < columns - 1; column++)
                {
                    int a = index * columns + column;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    triangles[triangle++] = a;
                    triangles[triangle++] = c;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = d;
                    triangles[triangle++] = a;
                    triangles[triangle++] = b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = b;
                    triangles[triangle++] = d;
                    triangles[triangle++] = c;
                }
            }

            mesh = new Mesh { name = name };
            if (vertices.Length > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;

            ribbonObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = ribbonObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
        }

        public void Hide()
        {
            renderer.enabled = false;
        }

        public void SetColor(Color materialColor)
        {
            RhythmStageView.SetMaterialColor(renderer.sharedMaterial, materialColor);
        }

        /// <param name="points">Path centre line, near end first.</param>
        /// <param name="sides">Unit vector across the path at each point, scaled by its depth.</param>
        /// <param name="pointColors">Colour of the strip at each point.</param>
        public void Show(Vector3[] points, Vector3[] sides, Color[] pointColors, int count, float halfWidth)
        {
            int last = count - 1;
            int columns = offsets.Length;
            int maxPoints = vertices.Length / columns;
            for (int index = 0; index < maxPoints; index++)
            {
                // Unused points collapse onto the last one and draw nothing.
                int source = Mathf.Min(index, last);
                Vector3 across = sides[source] * halfWidth;
                Color pointColor = pointColors[source];
                for (int column = 0; column < columns; column++)
                {
                    vertices[index * columns + column] = points[source] + across * offsets[column];
                    colors[index * columns + column] = pointColor * profile[column];
                }
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            renderer.enabled = true;
        }

        public void Destroy()
        {
            UnityEngine.Object.Destroy(renderer.sharedMaterial);
            UnityEngine.Object.Destroy(mesh);
        }
    }

    /// <summary>One flat, camera-facing mesh of the marker or the turn arrow with its own material.</summary>
    private sealed class FlatPart
    {
        private readonly Mesh mesh;
        private readonly Material material;

        public FlatPart(Transform parent, string name, Mesh partMesh, int sortingOrder)
        {
            GameObject partObject = new GameObject(name);
            partObject.transform.SetParent(parent, false);
            mesh = partMesh;
            partObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            material = RhythmStageView.CreateTransparentMaterial(Color.white);
            Renderer = partObject.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = material;
            Renderer.sortingOrder = sortingOrder;
        }

        public MeshRenderer Renderer { get; }
        public Transform Transform => Renderer.transform;

        public void SetColor(Color color)
        {
            RhythmStageView.SetMaterialColor(material, color);
        }

        public void Destroy()
        {
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(mesh);
        }
    }
    private Ribbon glow;
    private Ribbon band;
    private Ribbon core;
    private Transform marker;
    private Transform markerBody;
    private FlatPart markerHalo;
    private FlatPart markerRing;
    private FlatPart markerDot;
    private FlatPart markerRipple;
    private Transform turnArrow;
    private FlatPart turnPlate;
    private FlatPart turnRim;
    private FlatPart turnOutline;
    private FlatPart turnHead;
    private float brightness = ApproachBrightness;
    private Color color;
    private Color bandFar;
    private Color bandMid;
    private Color bandNear;
    private Color coreFar;
    private Color coreNear;
    private Vector3[] points;
    private Vector3[] sides;
    private float[] progresses;
    private Color[] bandColors;
    private Color[] coreColors;

    private int side;
    private double[] times;
    private Vector2[] values;
    private double lastSampleTime;
    private double followedSeconds;
    private double sampledSeconds;

    public int Side => side;
    public double StartTime => times[0];
    public double EndTime => times[times.Length - 1];

    /// <summary>True on a frame the laser is crossing the judgement plane and the stick follows it.</summary>
    public bool IsFollowed { get; private set; }

    /// <summary>Share of the laser's time it was followed (1 if it was too short to sample).</summary>
    public float TrackingAccuracy => sampledSeconds <= 0d ? 1f : (float)(followedSeconds / sampledSeconds);

    /// <summary>Seconds between combo steps while the laser is followed (4 per second).</summary>
    public const double ComboTickSeconds = 0.25d;

    private int ticksPassed;

    /// <summary>Combo steps that came due up to <paramref name="songTime"/>; only those while followed count.</summary>
    public int TakeComboTicks(double songTime)
    {
        int taken = 0;
        double until = Math.Min(songTime, EndTime);
        while (StartTime + (ticksPassed + 1) * ComboTickSeconds <= until)
        {
            ticksPassed++;
            if (IsFollowed)
            {
                taken++;
            }
        }

        return taken;
    }

    public void Initialize(LaserNoteData data)
    {
        ticksPassed = 0;
        side = Mathf.Clamp(data.side, 0, 1);
        followedSeconds = 0d;
        sampledSeconds = 0d;
        IsFollowed = false;

        times = new double[data.points.Length];
        values = new Vector2[data.points.Length];
        for (int index = 0; index < data.points.Length; index++)
        {
            // The laser may use the whole square, corners included (it is followed by direction).
            times[index] = data.points[index].time;
            values[index] = StickInput.ClampToSquare(new Vector2(data.points[index].x, data.points[index].y));
        }

        int maxPoints = (times.Length + 1) * SegmentSubdivisions + 1;
        points = new Vector3[maxPoints];
        sides = new Vector3[maxPoints];
        progresses = new float[maxPoints];
        bandColors = new Color[maxPoints];
        coreColors = new Color[maxPoints];
        color = RhythmStageView.GetStickColor(side);
        gameObject.name = $"LaserNote_{(side == 0 ? "Left" : "Right")}_{StartTime:0.000}";
        CreateDepthColors();

        // White materials: the vertex colours carry the depth gradient, the material's alpha
        // how strongly the laser shows (UpdateBrightness).
        // Cross profiles: the glow fades out to both sides; the tube is lit like a cylinder
        // (full in the middle, darker and thinner toward its rims, soft edge); the core is a hot line.
        glow = new Ribbon(transform, "Glow", RhythmStageView.CreateTransparentMaterial(Color.white), maxPoints, 0,
            new[] { -1f, -0.55f, -0.2f, 0.2f, 0.55f, 1f },
            new[] { Clear, Fade(0.25f), Fade(0.7f), Fade(0.7f), Fade(0.25f), Clear });
        band = new Ribbon(transform, "Band", RhythmStageView.CreateTransparentMaterial(Color.white), maxPoints, 1,
            TubeOffsets, CreateTubeProfile());
        core = new Ribbon(transform, "Core", RhythmStageView.CreateTransparentMaterial(Color.white), maxPoints, 2,
            new[] { -1f, -0.45f, 0f, 0.45f, 1f },
            new[] { Clear, Fade(0.75f), Color.white, Fade(0.75f), Clear });
        lastSampleTime = StartTime;
        CreateMarker();

        // A LASER COLOR change (the options can be open while a song is paused) shows on lasers already in play.
        GameSettings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        color = RhythmStageView.GetStickColor(side);
        CreateDepthColors();
    }

    /// <param name="travelTimeSeconds">Seconds from the far end to the judgement plane (follows HI-SPEED).</param>
    public void UpdateView(double songTime, float travelTimeSeconds)
    {
        if (band == null)
        {
            return;
        }

        float travelTime = Mathf.Max(0.01f, travelTimeSeconds);
        UpdateMarker(songTime);
        UpdateBrightness(songTime);

        // Only the stretch between the judgement plane (now) and the far end is drawn.
        double visibleStart = Math.Max(StartTime, songTime);
        double visibleEnd = Math.Min(EndTime, songTime + travelTime);
        if (visibleEnd <= visibleStart)
        {
            glow.Hide();
            band.Hide();
            core.Hide();
            return;
        }

        int count = 0;
        double spanStart = visibleStart;
        for (int index = 0; index <= times.Length; index++)
        {
            double spanEnd = index < times.Length ? Math.Min(times[index], visibleEnd) : visibleEnd;
            if (spanEnd <= spanStart)
            {
                continue;
            }

            // Subdivide each straight move: its screen path is curved because the
            // tunnel widens while the laser slides sideways.
            for (int step = 0; step < SegmentSubdivisions; step++)
            {
                AddPoint(count++, spanStart + (spanEnd - spanStart) * step / SegmentSubdivisions, songTime, travelTime);
            }

            spanStart = spanEnd;
        }

        AddPoint(count++, visibleEnd, songTime, travelTime);
        OrientSides(count);
        for (int index = 0; index < count; index++)
        {
            bandColors[index] = GetBandColor(progresses[index]);
            // Squared, so the core stays dim until the laser is close.
            coreColors[index] = Color.Lerp(coreFar, coreNear, progresses[index] * progresses[index]);
        }

        glow.Show(points, sides, bandColors, count, GlowHalfWidth);
        band.Show(points, sides, bandColors, count, BandHalfWidth);
        core.Show(points, sides, coreColors, count, CoreHalfWidth);
    }

    /// <summary>Where the laser is at <paramref name="time"/>, in stick units.</summary>
    public Vector2 GetValue(double time)
    {
        if (time <= times[0])
        {
            return values[0];
        }

        for (int index = 1; index < times.Length; index++)
        {
            if (time > times[index])
            {
                continue;
            }

            double span = times[index] - times[index - 1];
            float amount = span <= 0d ? 1f : (float)((time - times[index - 1]) / span);
            return Vector2.Lerp(values[index - 1], values[index], amount);
        }

        return values[values.Length - 1];
    }

    public bool IsExpired(double songTime)
    {
        return songTime > EndTime + 0.5d;
    }

    public bool IsReadyToJudge(double songTime)
    {
        return songTime >= EndTime;
    }

    /// <summary>
    /// Unit vector of the way the laser moves at <paramref name="time"/>, in stick
    /// units; zero while it holds still (or outside its points).
    /// </summary>
    public Vector2 GetMotion(double time)
    {
        for (int index = 1; index < times.Length; index++)
        {
            if (time < times[index - 1] || time >= times[index])
            {
                continue;
            }

            Vector2 delta = values[index] - values[index - 1];
            return delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector2.zero;
        }

        return Vector2.zero;
    }

    /// <summary>
    /// Whether <paramref name="stick"/> follows the laser at <paramref name="time"/>:
    /// it leans the way the laser moves now or within <see cref="DirectionGraceSeconds"/>,
    /// or the laser holds still there.
    /// </summary>
    public bool IsFollowing(Vector2 stick, double time)
    {
        for (int step = 0; step < 3; step++)
        {
            double offset = step == 0 ? 0d : step == 1 ? -DirectionGraceSeconds : DirectionGraceSeconds;
            double at = Math.Min(Math.Max(time + offset, StartTime), EndTime);
            Vector2 motion = GetMotion(at);
            if (motion == Vector2.zero || StickInput.PointsToward(stick, motion))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Judges the stick for the song time since the last sample (time-weighted, so the
    /// frame rate does not matter). The last call after the end covers the tail.
    /// </summary>
    public void SampleInput(Vector2 stick, double songTime)
    {
        double time = Math.Min(songTime, EndTime);
        if (songTime < StartTime || lastSampleTime >= EndTime)
        {
            IsFollowed = false;
            return;
        }

        IsFollowed = IsFollowing(stick, time);
        double elapsed = Math.Max(0d, time - lastSampleTime);
        lastSampleTime = time;
        sampledSeconds += elapsed;
        if (IsFollowed)
        {
            followedSeconds += elapsed;
        }
    }

    private void OnDestroy()
    {
        GameSettings.Changed -= OnSettingsChanged;
        glow?.Destroy();
        band?.Destroy();
        core?.Destroy();
        markerHalo?.Destroy();
        markerRing?.Destroy();
        markerDot?.Destroy();
        markerRipple?.Destroy();
        turnPlate?.Destroy();
        turnRim?.Destroy();
        turnOutline?.Destroy();
        turnHead?.Destroy();
    }
    /// <summary>
    /// The way the laser turns next after <paramref name="time"/>: the direction of the first
    /// later move that points more than <see cref="TurnMinAngle"/> off the current one (the first
    /// move at all while it holds still or has not begun), and when that move starts.
    /// False when the laser just carries on to its end.
    /// </summary>
    public bool TryGetNextTurn(double time, out Vector2 direction, out double turnTime)
    {
        direction = Vector2.zero;
        turnTime = 0d;
        Vector2 current = GetMotion(time);
        for (int index = 1; index < times.Length; index++)
        {
            if (times[index - 1] <= time)
            {
                continue;
            }

            Vector2 delta = values[index] - values[index - 1];
            if (delta.sqrMagnitude <= 1e-6f)
            {
                continue;
            }

            Vector2 candidate = delta.normalized;
            if (current != Vector2.zero && Vector2.Angle(current, candidate) < TurnMinAngle)
            {
                continue;
            }

            direction = candidate;
            turnTime = times[index - 1];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Strengthens the ribbons while the laser is followed and fades them while it is not.
    /// That is opacity, not darkening, so a missed laser recedes instead of standing out.
    /// </summary>
    private void UpdateBrightness(double songTime)
    {
        bool crossing = songTime >= StartTime && songTime <= EndTime;
        float goal = !crossing ? ApproachBrightness : IsFollowed ? FollowedBrightness : MissedBrightness;
        brightness = Mathf.MoveTowards(brightness, goal, BrightnessSpeed * Time.deltaTime);

        float opacity = Mathf.Clamp01(brightness / FollowedBrightness);
        glow.SetColor(new Color(1f, 1f, 1f, opacity * 0.38f));
        band.SetColor(new Color(1f, 1f, 1f, opacity));
        core.SetColor(new Color(CoreGlow, CoreGlow, CoreGlow, Mathf.Clamp01(opacity * 1.4f)));
    }

    /// <summary>The colours the ribbons run through from the far end to the judgement plane.</summary>
    private void CreateDepthColors()
    {
        Color.RGBToHSV(color, out float hue, out float _, out float _);
        float farHue = Mathf.Repeat(Mathf.MoveTowardsAngle(hue * 360f, FarHueDegrees, FarHueShiftDegrees), 360f) / 360f;

        // Far: dark and nearly transparent, so it sinks into the black stage. Near: solid and pale.
        bandFar = Color.HSVToRGB(farHue, 1f, 0.30f);
        bandFar.a = 0.10f;
        bandMid = color;
        bandMid.a = 0.55f;
        bandNear = Color.Lerp(color, Color.white, 0.20f);
        bandNear.a = 0.92f;

        // A hot, paler line through the band (it blooms), faint far away and solid where it is about to arrive.
        coreFar = Color.HSVToRGB(farHue, 0.9f, 0.30f);
        coreFar.a = 0.08f;
        coreNear = Color.Lerp(color, Color.white, 0.40f);

        bandFar = RhythmStageView.ToVertexColor(bandFar);
        bandMid = RhythmStageView.ToVertexColor(bandMid);
        bandNear = RhythmStageView.ToVertexColor(bandNear);
        coreFar = RhythmStageView.ToVertexColor(coreFar);
        coreNear = RhythmStageView.ToVertexColor(coreNear);
    }

    private Color GetBandColor(float progress)
    {
        return progress < MidProgress
            ? Color.Lerp(bandFar, bandMid, progress / MidProgress)
            : Color.Lerp(bandMid, bandNear, (progress - MidProgress) / (1f - MidProgress));
    }

    /// <summary>Stores the path point for <paramref name="time"/>; its side holds the depth scale until OrientSides runs.</summary>
    private void AddPoint(int index, double time, double songTime, float travelTime)
    {
        float progress = RhythmStageView.ApplyApproachCurve(Mathf.Clamp01(1f - (float)((time - songTime) / travelTime)));
        progresses[index] = progress;
        points[index] = RhythmStageView.GetLaserWorldPosition(GetValue(time), progress);
        sides[index] = new Vector3(RhythmStageView.GetDepthScale(progress), 0f, 0f);
    }

    /// <summary>
    /// Turns each point's side vector across the path as the camera sees it. A laser
    /// that holds still flies straight at the camera, so the direction is taken from
    /// the projected path rather than from the world-space one.
    /// </summary>
    private void OrientSides(int count)
    {
        Vector2 across = Vector2.up;
        int firstWithDirection = -1;
        for (int index = 0; index < count; index++)
        {
            Vector2 before = RhythmStageView.ProjectToView(points[Mathf.Max(index - 1, 0)]);
            Vector2 after = RhythmStageView.ProjectToView(points[Mathf.Min(index + 1, count - 1)]);
            Vector2 along = after - before;
            if (along.sqrMagnitude > 1e-10f)
            {
                along.Normalize();
                across = new Vector2(-along.y, along.x);
                if (firstWithDirection < 0)
                {
                    firstWithDirection = index;
                }
            }

            // A point with no direction of its own keeps the previous point's.
            sides[index] = (Vector3)across * sides[index].x;
        }

        // Leading points before the first direction borrow it, so the strip does not twist.
        for (int index = 0; index < firstWithDirection; index++)
        {
            sides[index] = sides[firstWithDirection].normalized * sides[index].magnitude;
        }
    }

    private static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

    private static Color Fade(float alpha)
    {
        return new Color(1f, 1f, 1f, alpha);
    }

    private static readonly float[] TubeOffsets = { -1f, -0.88f, -0.7f, -0.45f, -0.18f, 0f, 0.18f, 0.45f, 0.7f, 0.88f, 1f };

    /// <summary>
    /// Lights the band like a glowing cylinder seen side-on: the facing middle full, the rims
    /// darker and more see-through, the outermost column clear so the edge is soft.
    /// </summary>
    private static Color[] CreateTubeProfile()
    {
        Color[] profile = new Color[TubeOffsets.Length];
        for (int column = 0; column < TubeOffsets.Length; column++)
        {
            float x = Mathf.Abs(TubeOffsets[column]);
            float facing = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x));
            float shade = Mathf.Lerp(0.45f, 1f, facing);
            float alpha = x >= 0.999f ? 0f : Mathf.Pow(facing, 0.45f);
            profile[column] = new Color(shade, shade, shade, alpha);
        }

        return profile;
    }

    private void CreateMarker()
    {
        GameObject markerObject = new GameObject("Marker");
        markerObject.transform.SetParent(transform, false);
        marker = markerObject.transform;

        // A soft halo, a crisp rounded-square ring with a rounded square inside (they pulse
        // together), a ring that ripples outward while followed, and the next-turn arrow.
        markerHalo = new FlatPart(marker, "Halo",
            CreateRoundedShape(MarkerHalfSize * 0.6f, MarkerHalfSize * 0.6f, 1f, new[] { 0f, 0.34f }, new[] { 0.55f, 0f }), 3);

        GameObject bodyObject = new GameObject("Body");
        bodyObject.transform.SetParent(marker, false);
        markerBody = bodyObject.transform;
        markerRing = new FlatPart(markerBody, "Ring", CreateRoundedRing(MarkerHalfSize, MarkerCornerRadius, MarkerRingWidth), 4);
        markerDot = new FlatPart(markerBody, "Dot",
            CreateRoundedShape(MarkerDotHalfSize, MarkerDotHalfSize * 0.45f, 1f, new[] { 0f, 0.012f }, new[] { 1f, 0f }), 5);
        markerRipple = new FlatPart(marker, "Ripple", CreateRoundedRing(MarkerHalfSize, MarkerCornerRadius, MarkerRingWidth * 0.5f), 4);

        // The arrow: plate, outline, head; the head's tip points along +x (UpdateTurnArrow turns it).
        GameObject arrowObject = new GameObject("TurnArrow");
        arrowObject.transform.SetParent(marker, false);
        turnArrow = arrowObject.transform;
        turnPlate = new FlatPart(turnArrow, "Plate",
            CreateRoundedShape(TurnPlateRadius, TurnPlateRadius, 1f, new[] { 0f, 0.12f }, new[] { 1f, 0f }), 6);
        turnRim = new FlatPart(turnArrow, "Rim", CreateRoundedRing(TurnPlateRadius, TurnPlateRadius, 0.03f), 7);
        turnOutline = new FlatPart(turnArrow, "Outline", CreateArrowMesh(TurnArrowLength + 0.14f, TurnArrowHalfWidth + 0.10f, -0.04f), 7);
        turnHead = new FlatPart(turnArrow, "Head", CreateArrowMesh(TurnArrowLength, TurnArrowHalfWidth, 0f), 8);
        arrowObject.SetActive(false);
    }

    /// <summary>Points around a rounded square centred on the origin, counter-clockwise from the right.</summary>
    private static Vector3[] RoundedSquareLoop(float half, float radius, int cornerSegments)
    {
        half = Mathf.Max(0f, half);
        radius = Mathf.Clamp(radius, 0f, half);
        float inner = half - radius;
        Vector3[] loop = new Vector3[4 * (cornerSegments + 1)];
        for (int corner = 0; corner < 4; corner++)
        {
            float cx = corner == 0 || corner == 3 ? inner : -inner;
            float cy = corner < 2 ? inner : -inner;
            for (int step = 0; step <= cornerSegments; step++)
            {
                float angle = (corner * 90f + 90f * step / cornerSegments) * Mathf.Deg2Rad;
                loop[corner * (cornerSegments + 1) + step] = new Vector3(cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius, 0f);
            }
        }

        return loop;
    }

    /// <summary>
    /// A rounded square (a circle when <paramref name="radius"/> equals <paramref name="half"/>)
    /// made of nested loops grown by <paramref name="loopOffsets"/> from its edge, each with its
    /// own alpha, so edges can be feathered; <paramref name="centreAlpha"/> fills the middle
    /// (negative leaves a hole inside the first loop). Vertex colours are white: the material tints it.
    /// </summary>
    private static Mesh CreateRoundedShape(float half, float radius, float centreAlpha, float[] loopOffsets, float[] loopAlphas)
    {
        const int CornerSegments = 8;
        int perLoop = 4 * (CornerSegments + 1);
        bool filled = centreAlpha >= 0f;
        int first = filled ? 1 : 0;
        Vector3[] vertices = new Vector3[first + perLoop * loopOffsets.Length];
        Color[] colors = new Color[vertices.Length];
        if (filled)
        {
            vertices[0] = Vector3.zero;
            colors[0] = Fade(centreAlpha);
        }

        for (int loop = 0; loop < loopOffsets.Length; loop++)
        {
            Vector3[] loopPoints = RoundedSquareLoop(half + loopOffsets[loop], radius + loopOffsets[loop], CornerSegments);
            for (int index = 0; index < perLoop; index++)
            {
                vertices[first + loop * perLoop + index] = loopPoints[index];
                colors[first + loop * perLoop + index] = Fade(loopAlphas[loop]);
            }
        }

        System.Collections.Generic.List<int> triangles = new System.Collections.Generic.List<int>();
        void AddBoth(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
        }

        for (int index = 0; index < perLoop; index++)
        {
            int next = (index + 1) % perLoop;
            if (filled)
            {
                AddBoth(0, first + index, first + next);
            }

            for (int loop = 0; loop < loopOffsets.Length - 1; loop++)
            {
                int a = first + loop * perLoop + index;
                int b = first + loop * perLoop + next;
                int c = a + perLoop;
                int d = b + perLoop;
                AddBoth(a, b, c);
                AddBoth(b, d, c);
            }
        }

        Mesh mesh = new Mesh { name = "LaserMarkerShape", vertices = vertices, colors = colors };
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>A rounded-square ring whose outer edge is at <paramref name="half"/>, with feathered edges.</summary>
    private static Mesh CreateRoundedRing(float half, float radius, float width)
    {
        const float Feather = 0.014f;
        return CreateRoundedShape(half, radius, -1f,
            new[] { -width - Feather, -width, 0f, Feather },
            new[] { 0f, 1f, 1f, 0f });
    }

    /// <summary>
    /// A notched arrowhead centred on the origin, tip along +x: tip, upper wing, notch, lower wing.
    /// <paramref name="shift"/> moves it along x (the outline sits slightly behind the head).
    /// </summary>
    private static Mesh CreateArrowMesh(float length, float halfWidth, float shift)
    {
        float back = -length * 0.5f + shift;
        Vector3 tip = new Vector3(length * 0.5f + shift, 0f, 0f);
        Vector3 upper = new Vector3(back, halfWidth, 0f);
        Vector3 notch = new Vector3(back + length * 0.38f, 0f, 0f);
        Vector3 lower = new Vector3(back, -halfWidth, 0f);
        Mesh mesh = new Mesh
        {
            name = "LaserTurnArrow",
            vertices = new[] { tip, upper, notch, lower },
            colors = new[] { Color.white, Color.white, Color.white, Color.white },
            triangles = new[] { 0, 1, 2, 0, 2, 1, 0, 2, 3, 0, 3, 2 }
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Rounded-square marker on the judgement plane at the laser's current point. Faint at
    /// the start position while the laser approaches; while it crosses, it glows, pulses and
    /// sends out ripples with a lit centre when followed, and fades when not. An arrow on a
    /// dark plate beside it points the way the laser turns next, brighter and nudging
    /// outward as the turn nears.
    /// </summary>
    private void UpdateMarker(double songTime)
    {
        bool crossing = songTime >= StartTime && songTime <= EndTime;
        bool followed = crossing && IsFollowed;
        marker.position = RhythmStageView.GetLaserWorldPosition(GetValue(songTime), 1f) + new Vector3(0f, 0f, -0.05f);

        // The song clock drives the pulse and the ripples, so they stay in step with the music.
        float pulse = followed ? 1f + 0.08f * (0.5f + 0.5f * Mathf.Sin((float)(songTime * Math.PI * 2d * 6d))) : 1f;
        markerBody.localScale = new Vector3(pulse, pulse, 1f);

        Color pale = Color.Lerp(color, Color.white, 0.35f);
        Color halo = color;
        halo.a = followed ? 0.6f : crossing ? 0.22f : 0.12f;
        markerHalo.SetColor(halo);
        markerHalo.Transform.localScale = Vector3.one * (followed ? 1.15f : 1f);

        Color ring = followed ? RhythmStageView.Boost(pale, 1.7f) : color;
        ring.a = followed ? 1f : crossing ? 0.5f : 0.35f;
        markerRing.SetColor(ring);

        Color dot = followed ? RhythmStageView.Boost(Color.Lerp(color, Color.white, 0.6f), 1.4f) : color;
        dot.a = followed ? 1f : crossing ? 0.3f : 0.2f;
        markerDot.SetColor(dot);

        markerRipple.Renderer.enabled = followed;
        if (followed)
        {
            float phase = (float)((songTime * 2d) % 1d);
            markerRipple.Transform.localScale = Vector3.one * (1f + 0.75f * phase);
            Color ripple = pale;
            ripple.a = 0.55f * (1f - phase) * (1f - phase);
            markerRipple.SetColor(ripple);
        }

        UpdateTurnArrow(songTime);
    }

    private void UpdateTurnArrow(double songTime)
    {
        if (songTime > EndTime || !TryGetNextTurn(songTime, out Vector2 direction, out double turnTime))
        {
            turnArrow.gameObject.SetActive(false);
            return;
        }

        // The stick direction in the world: the stage is wider than tall, so a diagonal is not 45 degrees there.
        Vector3 world = RhythmStageView.GetLaserWorldPosition(direction, 1f) - RhythmStageView.GetLaserWorldPosition(Vector2.zero, 1f);
        Vector2 heading = new Vector2(world.x, world.y).normalized;

        // How close the turn is: 0 far out, 1 within TurnNearSeconds.
        float nearness = Mathf.Clamp01((float)(1d - (turnTime - songTime - TurnNearSeconds) / (TurnNearSeconds * 2d)));
        float bob = 0.06f * nearness * (0.5f + 0.5f * Mathf.Sin((float)(songTime * Math.PI * 2d * 4d)));

        // The plate sits just outside the marker, whichever way it points (a diagonal leaves through a corner).
        float toEdge = MarkerHalfSize / Mathf.Max(Mathf.Abs(heading.x), Mathf.Abs(heading.y));
        turnArrow.localPosition = (Vector3)(heading * (toEdge + TurnArrowGap + TurnPlateRadius + bob));
        turnArrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg);
        turnArrow.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.05f, nearness);

        // Dark plate and outline so the arrowhead stands out against the laser and its glow.
        float strength = Mathf.Lerp(0.6f, 1f, nearness);
        turnPlate.SetColor(new Color(0f, 0f, 0f, 0.72f * strength));
        turnOutline.SetColor(new Color(0f, 0f, 0f, 0.95f * strength));
        Color rim = color;
        rim.a = 0.85f * strength;
        turnRim.SetColor(rim);
        Color head = RhythmStageView.Boost(Color.Lerp(color, Color.white, 0.55f), Mathf.Lerp(1f, 1.35f, nearness));
        head.a = strength;
        turnHead.SetColor(head);
        turnArrow.gameObject.SetActive(true);
    }
}

