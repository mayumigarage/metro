using System;
using UnityEngine;

/// <summary>
/// A laser flying through the space between the upper and lower lanes. Its path is
/// a list of timed points in stick units; the part that has not yet reached the
/// judgement plane is drawn, and the player follows it with the side's stick.
/// </summary>
public sealed class LaserNoteView : MonoBehaviour
{
    // Stick distance from the laser that still counts as fully on it / completely off it.
    public const float FullAccuracyDistance = 0.30f;
    public const float ZeroAccuracyDistance = 0.75f;

    private const int SegmentSubdivisions = 6;
    private const int TargetSegments = 20;
    private const float BandHalfWidth = 0.38f;
    private const float CoreHalfWidth = 0.08f;
    private const float TargetRadius = 0.62f;

    /// <summary>A flat strip along the laser's path, turned to face the camera.</summary>
    private sealed class Ribbon
    {
        private readonly Mesh mesh;
        private readonly Vector3[] vertices;
        private readonly MeshRenderer renderer;

        public Ribbon(Transform parent, string name, Material material, int maxPoints, int sortingOrder)
        {
            GameObject ribbonObject = new GameObject(name);
            ribbonObject.transform.SetParent(parent, false);

            // Two vertices per path point. Both windings, so it shows from either side.
            vertices = new Vector3[maxPoints * 2];
            int[] triangles = new int[(maxPoints - 1) * 12];
            for (int index = 0; index < maxPoints - 1; index++)
            {
                int vertex = index * 2;
                int triangle = index * 12;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
                triangles[triangle + 6] = vertex;
                triangles[triangle + 7] = vertex + 1;
                triangles[triangle + 8] = vertex + 2;
                triangles[triangle + 9] = vertex + 1;
                triangles[triangle + 10] = vertex + 3;
                triangles[triangle + 11] = vertex + 2;
            }

            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
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

        /// <param name="points">Path centre line, near end first.</param>
        /// <param name="sides">Unit vector across the path at each point, scaled by its depth.</param>
        public void Show(Vector3[] points, Vector3[] sides, int count, float halfWidth)
        {
            int last = count - 1;
            for (int index = 0; index * 2 < vertices.Length; index++)
            {
                // Unused points collapse onto the last one and draw nothing.
                int source = Mathf.Min(index, last);
                vertices[index * 2] = points[source] + sides[source] * halfWidth;
                vertices[index * 2 + 1] = points[source] - sides[source] * halfWidth;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            renderer.enabled = true;
        }

        public void Destroy()
        {
            UnityEngine.Object.Destroy(renderer.sharedMaterial);
            UnityEngine.Object.Destroy(mesh);
        }
    }

    private Ribbon band;
    private Ribbon core;
    private LineRenderer target;
    private Material targetMaterial;
    private Color color;
    private Vector3[] points;
    private Vector3[] sides;

    private int side;
    private double[] times;
    private Vector2[] values;
    private float travelTimeSeconds;
    private float accuracySum;
    private int sampleCount;

    public int Side => side;
    public double StartTime => times[0];
    public double EndTime => times[times.Length - 1];
    public float TrackingAccuracy => sampleCount == 0 ? 0f : accuracySum / sampleCount;

    public void Initialize(LaserNoteData data, float travelTime)
    {
        side = Mathf.Clamp(data.side, 0, 1);
        travelTimeSeconds = Mathf.Max(0.01f, travelTime);
        accuracySum = 0f;
        sampleCount = 0;

        times = new double[data.points.Length];
        values = new Vector2[data.points.Length];
        for (int index = 0; index < data.points.Length; index++)
        {
            // The stick cannot leave the unit circle, so neither may the laser.
            times[index] = data.points[index].time;
            values[index] = Vector2.ClampMagnitude(new Vector2(data.points[index].x, data.points[index].y), 1f);
        }

        int maxPoints = (times.Length + 1) * SegmentSubdivisions + 1;
        points = new Vector3[maxPoints];
        sides = new Vector3[maxPoints];
        color = RhythmStageView.GetStickColor(side);
        gameObject.name = $"LaserNote_{(side == 0 ? "Left" : "Right")}_{StartTime:0.000}";

        Color bandColor = color;
        bandColor.a = 0.5f;
        band = new Ribbon(transform, "Band", RhythmStageView.CreateTransparentMaterial(bandColor), maxPoints, 0);
        core = new Ribbon(transform, "Core", RhythmStageView.CreateTransparentMaterial(Color.Lerp(color, Color.white, 0.7f)), maxPoints, 1);
        CreateTarget();
    }

    public void UpdateView(double songTime)
    {
        if (band == null)
        {
            return;
        }

        UpdateTarget(songTime);

        // Only the stretch between the judgement plane (now) and the far end is drawn.
        double visibleStart = Math.Max(StartTime, songTime);
        double visibleEnd = Math.Min(EndTime, songTime + travelTimeSeconds);
        if (visibleEnd <= visibleStart)
        {
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
                AddPoint(count++, spanStart + (spanEnd - spanStart) * step / SegmentSubdivisions, songTime);
            }

            spanStart = spanEnd;
        }

        AddPoint(count++, visibleEnd, songTime);
        OrientSides(count);
        band.Show(points, sides, count, BandHalfWidth);
        core.Show(points, sides, count, CoreHalfWidth);
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

    public void SampleInput(Vector2 stick, double songTime)
    {
        if (songTime < StartTime || songTime > EndTime)
        {
            return;
        }

        float distance = Vector2.Distance(stick, GetValue(songTime));
        accuracySum += 1f - Mathf.InverseLerp(FullAccuracyDistance, ZeroAccuracyDistance, distance);
        sampleCount++;
    }

    private void OnDestroy()
    {
        band?.Destroy();
        core?.Destroy();
        if (targetMaterial != null)
        {
            Destroy(targetMaterial);
        }
    }

    /// <summary>Stores the path point for <paramref name="time"/>; its side holds the depth scale until OrientSides runs.</summary>
    private void AddPoint(int index, double time, double songTime)
    {
        float progress = 1f - (float)((time - songTime) / travelTimeSeconds);
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

    private void CreateTarget()
    {
        GameObject targetObject = new GameObject("Target");
        targetObject.transform.SetParent(transform, false);

        targetMaterial = RhythmStageView.CreateTransparentMaterial(color);
        target = targetObject.AddComponent<LineRenderer>();
        target.useWorldSpace = false;
        target.loop = true;
        target.positionCount = TargetSegments;
        target.startWidth = 0.08f;
        target.endWidth = 0.08f;
        target.sortingOrder = 2;
        target.sharedMaterial = targetMaterial;
        for (int index = 0; index < TargetSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / TargetSegments;
            target.SetPosition(index, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * TargetRadius);
        }
    }

    /// <summary>
    /// Ring on the judgement plane marking where the stick has to be: the start
    /// position (dim) while the laser approaches, then the live position (bright).
    /// </summary>
    private void UpdateTarget(double songTime)
    {
        bool active = songTime >= StartTime;
        target.transform.position = RhythmStageView.GetLaserWorldPosition(GetValue(songTime), 1f) + new Vector3(0f, 0f, -0.05f);

        Color targetColor = color;
        targetColor.a = active ? 1f : 0.4f;
        RhythmStageView.SetMaterialColor(targetMaterial, targetColor);
    }
}
