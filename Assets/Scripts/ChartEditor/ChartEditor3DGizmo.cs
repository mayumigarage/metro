using System;
using System.Collections.Generic;
using System.Linq;
using MetroEd;
using UnityEngine;

/// <summary>
/// CAD-like moving of laser points in the 3D edit view. A gizmo on the selected points moves them along
/// one axis (X / Y / depth = time) or in one plane; dragging a point itself, and placing one with the
/// laser tool, works on the "work plane" (XY at the playhead, or XZ / YZ running along time).
/// The grid rules are the timeline's: time snaps to the beat grid, X / Y to the laser grid (Alt skips
/// both), values stay in -1..1 and a point stays between its neighbours.
/// </summary>
public sealed partial class ChartEditorScreen
{
    private enum WorkPlane
    {
        Auto,
        XY,
        XZ,
        YZ
    }

    /// <summary>A constraint of a move, as the axes it frees: one axis or a plane of two.</summary>
    [Flags]
    private enum Axes
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 4,
        XY = X | Y,
        XZ = X | Z,
        YZ = Y | Z
    }

    private static readonly Axes[] GizmoAxes = { Axes.X, Axes.Y, Axes.Z };
    private static readonly Axes[] GizmoPlanes = { Axes.XY, Axes.XZ, Axes.YZ };

    private const float GizmoPixels = 72f;
    private const float GizmoPickPixels = 7f;
    private const float DragStartPixels = 3f;
    private const float GizmoPlaneFrom = 0.28f;
    private const float GizmoPlaneTo = 0.56f;

    private WorkPlane workPlane = WorkPlane.Auto;

    [NonSerialized] private Material overlay3D;
    [NonSerialized] private Vector2 hoverMouse3D;
    [NonSerialized] private Axes gizmoDragPart; // None = the point itself is dragged, on the work plane
    [NonSerialized] private Ray gizmoStartRay;
    [NonSerialized] private Vector2 gizmoStartMouse;
    [NonSerialized] private LaserPointData gizmoAnchor;
    [NonSerialized] private bool gizmoMoved;
    [NonSerialized] private double gizmoMinTimeDelta;
    [NonSerialized] private double gizmoMaxTimeDelta;

    private bool DraggingPoints3D => (drag == DragMode.MovePoint || drag == DragMode.PadPoint) && gizmoAnchor != null;

    /// <summary>The plane dragging and placing work on; "auto" takes the one the camera faces most.</summary>
    private Axes ResolvedWorkPlane()
    {
        switch (workPlane)
        {
            case WorkPlane.XY: return Axes.XY;
            case WorkPlane.XZ: return Axes.XZ;
            case WorkPlane.YZ: return Axes.YZ;
        }

        float x = Mathf.Abs(camForward.x);
        float y = Mathf.Abs(camForward.y);
        float z = Mathf.Abs(camForward.z);
        return z >= x && z >= y ? Axes.XY : y >= x ? Axes.XZ : Axes.YZ;
    }

    private static Vector3 AxisVector(Axes axis)
    {
        return axis == Axes.X ? Vector3.right : axis == Axes.Y ? Vector3.up : Vector3.forward;
    }

    private static Vector3 PlaneNormal(Axes plane)
    {
        return plane == Axes.XY ? Vector3.forward : plane == Axes.XZ ? Vector3.up : Vector3.right;
    }

    private static Color AxisColor(Axes part)
    {
        // A plane takes the colour of the axis it holds still.
        switch (part)
        {
            case Axes.X:
            case Axes.YZ:
                return new Color(1f, 0.32f, 0.32f);
            case Axes.Y:
            case Axes.XZ:
                return new Color(0.4f, 0.9f, 0.35f);
            default:
                return new Color(0.35f, 0.6f, 1f);
        }
    }

    private Vector3 PointWorld(LaserPointData point)
    {
        return LaserWorld(new Vector2(point.x, point.y), Z(point.time));
    }

    // ------------------------------------------------------------------ what moves

    /// <summary>Every laser point the selection holds: single points and whole lasers.</summary>
    private HashSet<LaserPointData> MovingPoints()
    {
        HashSet<LaserPointData> points = new HashSet<LaserPointData>(selectedPoints);
        if (selectedLaser != null)
        {
            if (selectedPoint >= 0 && selectedPoint < selectedLaser.points.Length)
            {
                points.Add(selectedLaser.points[selectedPoint]);
            }
            else
            {
                points.UnionWith(selectedLaser.points);
            }
        }

        foreach (LaserNoteData laser in selectedLasers)
        {
            points.UnionWith(laser.points);
        }

        return points;
    }

    /// <summary>The point the gizmo stands on: the dragged one, else the selected one nearest the playhead.</summary>
    private bool TryGetGizmo(out LaserPointData anchor, out float length)
    {
        anchor = null;
        length = 0f;
        if (selectedNotes.Count > 0 || tool == Tool.Erase)
        {
            return false;
        }

        if (DraggingPoints3D)
        {
            anchor = gizmoAnchor;
        }
        else
        {
            foreach (LaserPointData point in MovingPoints())
            {
                if (anchor == null || Math.Abs(point.time - cursorTime) < Math.Abs(anchor.time - cursorTime))
                {
                    anchor = point;
                }
            }
        }

        if (anchor == null || anchor.time < view3DFrom || anchor.time > view3DTo)
        {
            return false;
        }

        Vector3 center = PointWorld(anchor);
        float depth = camOrtho ? camDistance : Vector3.Dot(center - camEye, camForward);
        if (depth <= 0.05f || !Project3D(center, out _))
        {
            return false;
        }

        // The same size on screen wherever the point is.
        length = GizmoPixels * 2f * depth * Mathf.Tan(FieldOfView3D * 0.5f * Mathf.Deg2Rad) / view3DSize.y;
        return true;
    }

    /// <summary>An axis pointing straight at the camera, or a plane seen edge-on, cannot be dragged.</summary>
    private bool GizmoPartUsable(Axes part, Vector3 center)
    {
        Vector3 look = camOrtho ? camForward : (center - camEye).normalized;
        bool axis = part == Axes.X || part == Axes.Y || part == Axes.Z;
        return axis
            ? Mathf.Abs(Vector3.Dot(AxisVector(part), look)) < 0.985f
            : Mathf.Abs(Vector3.Dot(PlaneNormal(part), look)) > 0.2f;
    }

    private static void PlaneCorners(Axes plane, Vector3 center, float length, Vector3[] corners)
    {
        Vector3 u = plane == Axes.YZ ? Vector3.up : Vector3.right;
        Vector3 v = plane == Axes.XY ? Vector3.up : Vector3.forward;
        float from = length * GizmoPlaneFrom;
        float to = length * GizmoPlaneTo;
        corners[0] = center + u * from + v * from;
        corners[1] = center + u * to + v * from;
        corners[2] = center + u * to + v * to;
        corners[3] = center + u * from + v * to;
    }

    private Axes HitGizmo(Vector2 mouse)
    {
        if (!TryGetGizmo(out LaserPointData anchor, out float length))
        {
            return Axes.None;
        }

        Vector3 center = PointWorld(anchor);
        Vector3[] corners = new Vector3[4];
        Vector2[] screen = new Vector2[4];
        foreach (Axes plane in GizmoPlanes)
        {
            if (!GizmoPartUsable(plane, center))
            {
                continue;
            }

            PlaneCorners(plane, center, length, corners);
            bool visible = true;
            for (int index = 0; index < 4; index++)
            {
                visible &= Project3D(corners[index], out screen[index]);
            }

            if (visible && InsideConvex(mouse, screen))
            {
                return plane;
            }
        }

        Axes best = Axes.None;
        float bestDistance = GizmoPickPixels;
        foreach (Axes axis in GizmoAxes)
        {
            Vector3 along = AxisVector(axis);
            if (!GizmoPartUsable(axis, center)
                || !Project3D(center + along * (length * 0.2f), out Vector2 a)
                || !Project3D(center + along * (length * 1.22f), out Vector2 b))
            {
                continue;
            }

            float distance = DistanceToSegment(mouse, a, b);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = axis;
            }
        }

        return best;
    }

    private static bool InsideConvex(Vector2 point, Vector2[] corners)
    {
        int sign = 0;
        for (int index = 0; index < corners.Length; index++)
        {
            Vector2 edge = corners[(index + 1) % corners.Length] - corners[index];
            Vector2 to = point - corners[index];
            float cross = edge.x * to.y - edge.y * to.x;
            int side = cross > 0f ? 1 : -1;
            if (sign != 0 && side != sign)
            {
                return false;
            }

            sign = side;
        }

        return true;
    }

    // ------------------------------------------------------------------ dragging

    /// <summary>
    /// Starts moving the selected points (after <see cref="BeginDrag"/>). <paramref name="part"/> is the
    /// gizmo handle held, or None when <paramref name="anchor"/> itself is dragged.
    /// </summary>
    private void BeginPointMove3D(Axes part, LaserPointData anchor, Vector2 mouse)
    {
        HashSet<LaserPointData> moving = MovingPoints();
        moving.Add(anchor);
        pointOrigins.Clear();
        foreach (LaserPointData point in moving)
        {
            pointOrigins[point] = (point.time, point.x, point.y);
        }

        gizmoDragPart = part;
        gizmoAnchor = anchor;
        gizmoStartMouse = mouse;
        gizmoStartRay = Ray3D(mouse);
        gizmoMoved = false;

        // How far in time the points can travel before one passes a neighbour that stays.
        gizmoMinTimeDelta = double.NegativeInfinity;
        gizmoMaxTimeDelta = double.PositiveInfinity;
        foreach (LaserNoteData laser in Chart.lasers)
        {
            for (int index = 0; index < laser.points.Length; index++)
            {
                LaserPointData point = laser.points[index];
                if (!moving.Contains(point))
                {
                    continue;
                }

                gizmoMinTimeDelta = Math.Max(gizmoMinTimeDelta, -point.time);
                if (index > 0 && !moving.Contains(laser.points[index - 1]))
                {
                    gizmoMinTimeDelta = Math.Max(gizmoMinTimeDelta, laser.points[index - 1].time + 0.001d - point.time);
                }

                if (index < laser.points.Length - 1 && !moving.Contains(laser.points[index + 1]))
                {
                    gizmoMaxTimeDelta = Math.Min(gizmoMaxTimeDelta, laser.points[index + 1].time - 0.001d - point.time);
                }
            }
        }
    }

    /// <summary>How far the pointer has moved the constraint, in world units.</summary>
    private bool TryGetMove(Axes part, Vector3 origin, Ray ray, out Vector3 move)
    {
        move = Vector3.zero;
        if (part == Axes.X || part == Axes.Y || part == Axes.Z)
        {
            Vector3 axis = AxisVector(part);
            if (!TryAxisParameter(origin, axis, gizmoStartRay, out float from) || !TryAxisParameter(origin, axis, ray, out float to))
            {
                return false;
            }

            move = axis * (to - from);
            return true;
        }

        Vector3 normal = PlaneNormal(part);
        Plane plane = new Plane(normal, origin);
        if (Mathf.Abs(Vector3.Dot(ray.direction, normal)) < 0.02f
            || !plane.Raycast(gizmoStartRay, out float start) || !plane.Raycast(ray, out float now))
        {
            return false;
        }

        move = ray.GetPoint(now) - gizmoStartRay.GetPoint(start);
        return true;
    }

    /// <summary>Where along the axis line it comes closest to the ray.</summary>
    private static bool TryAxisParameter(Vector3 origin, Vector3 axis, Ray ray, out float parameter)
    {
        parameter = 0f;
        float b = Vector3.Dot(axis, ray.direction);
        float denominator = 1f - b * b;
        if (denominator < 0.002f)
        {
            return false;
        }

        Vector3 offset = origin - ray.origin;
        parameter = (b * Vector3.Dot(ray.direction, offset) - Vector3.Dot(axis, offset)) / denominator;
        return true;
    }

    private void DragPoints3D(Vector2 mouse)
    {
        if (gizmoAnchor == null || !pointOrigins.TryGetValue(gizmoAnchor, out (double time, float x, float y) anchor))
        {
            return;
        }

        // A click that barely moves only selects.
        if (!gizmoMoved && (mouse - gizmoStartMouse).magnitude < DragStartPixels)
        {
            return;
        }

        gizmoMoved = true;
        Axes part = gizmoDragPart;
        if (part == Axes.None)
        {
            Event current = Event.current;
            part = current != null && (current.control || current.command) ? Axes.Z : ResolvedWorkPlane();
        }

        Vector3 origin = LaserWorld(new Vector2(anchor.x, anchor.y), Z(anchor.time));
        if (!TryGetMove(part, origin, Ray3D(mouse), out Vector3 move))
        {
            return;
        }

        bool moveX = (part & Axes.X) != 0;
        bool moveY = (part & Axes.Y) != 0;
        bool moveZ = (part & Axes.Z) != 0;
        float dx = move.x / Laser3DHalfWidth;
        float dy = move.y / Laser3DHalfHeight;
        double timeDelta = 0d;
        if (moveZ)
        {
            timeDelta = Snap(anchor.time + TimeAtZ(move.z)) - anchor.time;
            timeDelta = Math.Min(Math.Max(timeDelta, gizmoMinTimeDelta), gizmoMaxTimeDelta);
        }

        List<(LaserPointData point, double time, float x, float y)> changes = new List<(LaserPointData, double, float, float)>();
        foreach (KeyValuePair<LaserPointData, (double time, float x, float y)> pair in pointOrigins)
        {
            double time = moveZ ? Math.Max(0d, Math.Round(pair.Value.time + timeDelta, 6)) : pair.Value.time;
            float x = moveX ? SnapLaserValue(pair.Value.x + dx) : pair.Value.x;
            float y = moveY ? SnapLaserValue(pair.Value.y + dy) : pair.Value.y;
            LaserPointData point = pair.Key;
            if (Math.Abs(time - point.time) > 0.000001d || !Mathf.Approximately(x, point.x) || !Mathf.Approximately(y, point.y))
            {
                changes.Add((point, time, x, y));
            }
        }

        if (changes.Count == 0)
        {
            return;
        }

        DragModify("Move laser points", () =>
        {
            foreach ((LaserPointData point, double time, float x, float y) change in changes)
            {
                change.point.time = change.time;
                change.point.x = change.x;
                change.point.y = change.y;
            }
        });
    }

    // ------------------------------------------------------------------ placing

    /// <summary>The X / Y the XZ and YZ planes stand at: the selected point, so a new one carries on from it.</summary>
    private Vector2 PlaneReference()
    {
        if (selectedLaser == null || selectedLaser.points.Length == 0)
        {
            return Vector2.zero;
        }

        if (selectedPoint >= 0 && selectedPoint < selectedLaser.points.Length)
        {
            return new Vector2(selectedLaser.points[selectedPoint].x, selectedLaser.points[selectedPoint].y);
        }

        return Evaluate(selectedLaser, cursorTime);
    }

    /// <summary>Where on the work plane a new laser point would go (already snapped).</summary>
    private bool TryHitPlacement(Ray ray, out Vector2 value, out double time)
    {
        Axes plane = ResolvedWorkPlane();
        value = default;
        time = Snap(cursorTime);
        if (plane == Axes.XY)
        {
            if (!TryHitLaserPlane(ray, out Vector2 hitValue))
            {
                return false;
            }

            value = new Vector2(SnapLaserValue(hitValue.x), SnapLaserValue(hitValue.y));
            return true;
        }

        Vector2 reference = PlaneReference();
        Vector3 on = LaserWorld(reference, 0f);
        if (!new Plane(PlaneNormal(plane), on).Raycast(ray, out float distance) || distance <= 0f)
        {
            return false;
        }

        Vector3 hit = ray.GetPoint(distance);
        float x = plane == Axes.XZ ? hit.x / Laser3DHalfWidth : reference.x;
        float y = plane == Axes.YZ ? hit.y / Laser3DHalfHeight : reference.y;
        double hitTime = TimeAtZ(hit.z);
        if (Mathf.Abs(x) > 1.15f || Mathf.Abs(y) > 1.15f || hitTime < Math.Max(-0.05d, view3DFrom) || hitTime > view3DTo)
        {
            return false;
        }

        value = new Vector2(plane == Axes.XZ ? SnapLaserValue(x) : x, plane == Axes.YZ ? SnapLaserValue(y) : y);
        time = Snap(hitTime);
        return true;
    }

    // ------------------------------------------------------------------ drawing

    /// <summary>Grid, guide lines, the ghost of the next point and the gizmo (inside the 3D render).</summary>
    private void DrawGizmo3D()
    {
        if (overlay3D == null)
        {
            return;
        }

        bool dragging = DraggingPoints3D;
        Axes plane = ResolvedWorkPlane();
        if (plane != Axes.XY && (tool == Tool.Laser || dragging))
        {
            Vector2 reference = dragging ? new Vector2(gizmoAnchor.x, gizmoAnchor.y) : PlaneReference();
            DrawWorkPlaneGrid(plane, reference);
        }

        bool hasGizmo = TryGetGizmo(out LaserPointData anchor, out float length);
        Axes hovered = dragging ? gizmoDragPart : drag == DragMode.None ? HitGizmo(hoverMouse3D) : Axes.None;

        Begin(overlay3D, GL.LINES);
        if (hasGizmo)
        {
            DrawGuides3D(anchor.x, anchor.y, anchor.time, new Color(1f, 1f, 1f, dragging ? 0.55f : 0.28f));
        }

        bool ghost = TryGetGhost(out Vector2 ghostValue, out double ghostTime);
        if (ghost)
        {
            DrawGuides3D(ghostValue.x, ghostValue.y, ghostTime, new Color(1f, 1f, 1f, 0.3f));
            BoxEdges(LaserWorld(ghostValue, Z(ghostTime)), Vector3.one * 0.07f, RhythmStageView.GetStickColor(selectedLaser?.side ?? placeLaserSide));
        }

        GL.End();

        if (!hasGizmo)
        {
            return;
        }

        Vector3 center = PointWorld(anchor);
        Vector3[] corners = new Vector3[4];
        Begin(overlay3D, GL.QUADS);
        foreach (Axes part in GizmoPlanes)
        {
            if (!GizmoPartUsable(part, center))
            {
                continue;
            }

            PlaneCorners(part, center, length, corners);
            bool lit = hovered == part;
            Color color = lit ? Color.yellow : AxisColor(part);
            Quad(corners[0], corners[1], corners[2], corners[3], WithAlpha(color, lit ? 0.6f : 0.28f));
            for (int index = 0; index < 4; index++)
            {
                ThickLine(corners[index], corners[(index + 1) % 4], length * 0.018f, WithAlpha(color, 0.9f));
            }
        }

        foreach (Axes axis in GizmoAxes)
        {
            if (!GizmoPartUsable(axis, center))
            {
                continue;
            }

            Vector3 along = AxisVector(axis);
            Color color = hovered == axis ? Color.yellow : AxisColor(axis);
            ThickLine(center + along * (length * 0.18f), center + along * length, length * 0.045f, color);
            Vector3 look = camOrtho ? camForward : center - camEye;
            Vector3 side = Vector3.Cross(along, look).normalized * (length * 0.095f);
            Vector3 neck = center + along * (length * 0.98f);
            Triangle(neck + side, center + along * (length * 1.24f), neck - side, color);
        }

        GL.End();
    }

    /// <summary>Lines from a point to the left wall and the floor of the laser space, and the cross-section it stands in.</summary>
    private void DrawGuides3D(float x, float y, double time, Color color)
    {
        float z = Z(time);
        Vector3 point = LaserWorld(new Vector2(x, y), z);
        Line(point, LaserWorld(new Vector2(-1f, y), z), color);
        Line(point, LaserWorld(new Vector2(x, -1f), z), color);
        Color frame = WithAlpha(color, color.a * 0.5f);
        Vector3 a = LaserWorld(new Vector2(-1f, -1f), z);
        Vector3 b = LaserWorld(new Vector2(1f, -1f), z);
        Vector3 c = LaserWorld(new Vector2(1f, 1f), z);
        Vector3 d = LaserWorld(new Vector2(-1f, 1f), z);
        Line(a, b, frame);
        Line(b, c, frame);
        Line(c, d, frame);
        Line(d, a, frame);
    }

    /// <summary>The XZ / YZ work plane: the laser grid across, the beat grid along time.</summary>
    private void DrawWorkPlaneGrid(Axes plane, Vector2 reference)
    {
        double from = Math.Max(0d, view3DFrom);
        double to = view3DTo;
        float z0 = Z(from);
        float z1 = Z(to);
        float step = Mathf.Max(0.05f, LaserGridSteps[Mathf.Clamp(laserGridIndex, 0, LaserGridSteps.Length - 1)]);
        Vector3 At(float value, float z) => LaserWorld(plane == Axes.XZ ? new Vector2(value, reference.y) : new Vector2(reference.x, value), z);

        Begin(blend3D, GL.QUADS);
        Quad(At(-1f, z0), At(1f, z0), At(1f, z1), At(-1f, z1), new Color(0.5f, 0.7f, 1f, 0.05f));
        GL.End();

        Begin(blend3D, GL.LINES);
        for (int index = Mathf.CeilToInt(-1f / step - 0.001f); index * step <= 1.001f; index++)
        {
            float value = index * step;
            Line(At(value, z0), At(value, z1), new Color(0.6f, 0.8f, 1f, index == 0 ? 0.35f : 0.14f));
        }

        Line(At(-1f, z0), At(-1f, z1), new Color(0.6f, 0.8f, 1f, 0.35f));
        Line(At(1f, z0), At(1f, z1), new Color(0.6f, 0.8f, 1f, 0.35f));

        double timeStep = GridStep;
        if (timeStep * DepthPerSecond < 0.12d)
        {
            timeStep = BeatSeconds;
        }

        long first = (long)Math.Ceiling((from - Chart.offset) / timeStep);
        long last = (long)Math.Floor((to - Chart.offset) / timeStep);
        if (last - first <= 3000)
        {
            for (long index = first; index <= last; index++)
            {
                double beats = index * timeStep / BeatSeconds;
                bool onBeat = Math.Abs(beats - Math.Round(beats)) < 0.0001d;
                float z = Z(Chart.offset + index * timeStep);
                Line(At(-1f, z), At(1f, z), new Color(0.6f, 0.8f, 1f, onBeat ? 0.3f : 0.11f));
            }
        }

        GL.End();
    }

    /// <summary>With the laser tool over empty space: where a click would put a point.</summary>
    private bool TryGetGhost(out Vector2 value, out double time)
    {
        value = default;
        time = 0d;
        Vector2 mouse = hoverMouse3D;
        if (tool != Tool.Laser || drag != DragMode.None || pointerBlocked
            || mouse.x < 0f || mouse.y < Toolbar3DHeight || mouse.x >= view3DSize.x || mouse.y >= view3DSize.y)
        {
            return false;
        }

        Ray ray = Ray3D(mouse);
        return HitGizmo(mouse) == Axes.None
               && !HitLaserPoint3D(mouse, out _, out _)
               && HitNote3D(ray, out _) == null
               && HitLaserLine3D(mouse) == null
               && TryHitPlacement(ray, out value, out time);
    }

    /// <summary>The axis names at the gizmo's tips and the coordinates of the point being placed or moved.</summary>
    private void DrawGizmoLabels3D()
    {
        GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
        bool hasGizmo = TryGetGizmo(out LaserPointData anchor, out float length);
        if (hasGizmo)
        {
            Vector3 center = PointWorld(anchor);
            foreach (Axes axis in GizmoAxes)
            {
                if (GizmoPartUsable(axis, center) && Project3D(center + AxisVector(axis) * (length * 1.42f), out Vector2 tip))
                {
                    style.normal.textColor = AxisColor(axis);
                    GUI.Label(new Rect(tip.x - 14f, tip.y - 8f, 28f, 16f), axis == Axes.Z ? "奥" : axis.ToString(), style);
                }
            }
        }

        string text = null;
        Vector3 at = Vector3.zero;
        if (TryGetGhost(out Vector2 value, out double time))
        {
            text = Coordinates(value.x, value.y, time);
            at = LaserWorld(value, Z(time));
        }
        else if (hasGizmo && (DraggingPoints3D || selectedPoint >= 0 || selectedPoints.Count > 0))
        {
            text = Coordinates(anchor.x, anchor.y, anchor.time);
            at = PointWorld(anchor);
        }

        if (text != null && Project3D(at, out Vector2 screen))
        {
            // Below the point, clear of the gizmo's arrows.
            Rect box = new Rect(screen.x - 95f, screen.y + 42f, 190f, 17f);
            EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.7f));
            style.normal.textColor = Color.white;
            GUI.Label(box, text, style);
        }
    }

    private string Coordinates(float x, float y, double time)
    {
        return $"X {x:0.00}  Y {y:0.00}  {FormatTime(time)}";
    }
}
