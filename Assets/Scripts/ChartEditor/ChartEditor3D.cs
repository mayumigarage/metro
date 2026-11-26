using System;
using System.Collections.Generic;
using System.Linq;
using MetroEd;
using UnityEngine;

/// <summary>
/// The 3D edit view of the chart editor (Tab, or 2D ⇄ 3D in the menu bar): the timeline area shows the
/// chart inside a straight box that runs on along time — unlike the game's tunnel it never narrows —
/// with the L / R lanes as its ceiling, ZL / ZR as its floor and the lasers in the space between.
/// Notes are placed right on the lanes and laser points on the playhead's cross-section (the red frame,
/// the "placement plane"), with the same tools as the timeline.
///
/// The camera moves like a CAD viewer around a point that follows the playhead, so during playback or
/// the game preview the box flows by while the game preview sidebar shows the real game:
/// middle drag orbits, Shift + middle drag pans, Ctrl + middle drag / Shift + wheel zooms, the wheel
/// steps the playhead and Ctrl + wheel stretches time. The view is drawn with GL into a render texture
/// (no scene objects, cameras or layers) and picked with the same matrices.
/// </summary>
public sealed partial class ChartEditorScreen
{
    // World layout: x across (right +), y up, z = time × DepthPerSecond.
    private const float Lane3DWidth = 1.6f;
    private const float Box3DHalfWidth = Lane3DWidth;
    private const float Box3DHalfHeight = 1.15f;
    private const float Laser3DHalfWidth = 1.35f;
    private const float Laser3DHalfHeight = 0.85f;
    private const float Toolbar3DHeight = 26f;
    private const float FieldOfView3D = 55f;
    private const float PointPickPixels = 9f;
    private const float LaserPickPixels = 6f;

    private static readonly Color Background3D = new Color(0.045f, 0.045f, 0.06f);

    private enum ViewPreset
    {
        Front,
        Top,
        Side,
        Angle
    }

    private bool editView3D;
    private int placeLaserSide;
    private float camYaw;
    private float camPitch = 5f;
    private float camDistance = 7.5f;
    private Vector3 camPan = new Vector3(0f, -0.35f, 3.5f);
    private bool camOrtho;

    [NonSerialized] private RenderTexture view3DTexture;
    [NonSerialized] private Material solid3D;
    [NonSerialized] private Material blend3D;
    [NonSerialized] private Vector2 view3DSize = Vector2.one;
    [NonSerialized] private Matrix4x4 view3D = Matrix4x4.identity;
    [NonSerialized] private Matrix4x4 projection3D = Matrix4x4.identity;
    [NonSerialized] private Matrix4x4 viewProjection3D = Matrix4x4.identity;
    [NonSerialized] private Matrix4x4 inverseViewProjection3D = Matrix4x4.identity;
    [NonSerialized] private Vector3 camEye;
    [NonSerialized] private Vector3 camForward = Vector3.forward;
    [NonSerialized] private double view3DFrom;
    [NonSerialized] private double view3DTo;

    [NonSerialized] private Vector2 navStartMouse;
    [NonSerialized] private float navStartYaw;
    [NonSerialized] private float navStartPitch;
    [NonSerialized] private float navStartDistance;
    [NonSerialized] private Vector3 navStartPan;
    [NonSerialized] private bool pointDragTime;

    /// <summary>World units per second along the box; follows the timeline zoom (Ctrl + wheel).</summary>
    private float DepthPerSecond => pixelsPerSecond / 60f;

    private float Z(double time) => (float)(time * DepthPerSecond);

    private double TimeAtZ(float z) => z / DepthPerSecond;

    private static float LaneCenterX(int lane) => (lane % 2 == 0 ? -0.5f : 0.5f) * Lane3DWidth;

    private static float SurfaceY(int lane) => lane < 2 ? Box3DHalfHeight : -Box3DHalfHeight;

    private static float Inward(int lane) => lane < 2 ? -1f : 1f;

    private static int RowOfLane(int lane) => lane < 2 ? 0 : 1;

    private static Vector3 LaserWorld(Vector2 value, float z)
    {
        return new Vector3(Mathf.Clamp(value.x, -1f, 1f) * Laser3DHalfWidth, Mathf.Clamp(value.y, -1f, 1f) * Laser3DHalfHeight, z);
    }

    // ------------------------------------------------------------------ mode & camera

    private void ToggleEditView3D()
    {
        editView3D = !editView3D;
        drag = DragMode.None;
        SaveLayout();
        Notify(editView3D ? "3D編集ビュー（中ドラッグで視点を回す ／ Tab で2Dに戻る）" : "2Dタイムライン");
    }

    private void SetViewPreset(ViewPreset preset)
    {
        switch (preset)
        {
            case ViewPreset.Front:
                // From inside the box, between the rows, like the player.
                camYaw = 0f;
                camPitch = 5f;
                camDistance = 7.5f;
                camPan = new Vector3(0f, -0.35f, 3.5f);
                camOrtho = false;
                break;
            case ViewPreset.Top:
                // Looking down with time running up the screen, like the timeline.
                camYaw = 0f;
                camPitch = 90f;
                camDistance = 16f;
                camPan = new Vector3(0f, 0f, 5f);
                camOrtho = true;
                break;
            case ViewPreset.Side:
                // From the right, time running to the right.
                camYaw = -90f;
                camPitch = 0f;
                camDistance = 12f;
                camPan = new Vector3(0f, 0f, 5f);
                camOrtho = true;
                break;
            default:
                camYaw = -32f;
                camPitch = 24f;
                camDistance = 12f;
                camPan = new Vector3(0f, 0f, 4f);
                camOrtho = false;
                break;
        }
    }

    private void SetupCamera3D(Vector2 size)
    {
        view3DSize = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
        float aspect = view3DSize.x / view3DSize.y;
        Quaternion rotation = Quaternion.Euler(camPitch, camYaw, 0f);
        camForward = rotation * Vector3.forward;
        Vector3 target = new Vector3(camPan.x, camPan.y, Z(cursorTime) + camPan.z);
        // An orthographic camera stands far back so nothing near the target is clipped.
        camEye = target - camForward * (camOrtho ? camDistance + 200f : camDistance);
        view3D = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(camEye, rotation, Vector3.one).inverse;
        if (camOrtho)
        {
            float half = camDistance * Mathf.Tan(FieldOfView3D * 0.5f * Mathf.Deg2Rad);
            projection3D = Matrix4x4.Ortho(-half * aspect, half * aspect, -half, half, 0.05f, 1200f);
        }
        else
        {
            projection3D = Matrix4x4.Perspective(FieldOfView3D, aspect, 0.05f, 1200f);
        }

        viewProjection3D = projection3D * view3D;
        inverseViewProjection3D = viewProjection3D.inverse;

        float perSecond = Mathf.Max(0.01f, DepthPerSecond);
        float reach = camDistance * (camOrtho ? 2.6f : 1.6f) + Mathf.Abs(camPan.z);
        view3DFrom = cursorTime - Math.Max(0.4d, reach / perSecond);
        view3DTo = cursorTime + Math.Min(120d, (camDistance * 4f + 60f + Mathf.Abs(camPan.z)) / perSecond);
    }

    private bool Project3D(Vector3 world, out Vector2 screen)
    {
        Vector4 clip = viewProjection3D * new Vector4(world.x, world.y, world.z, 1f);
        if (clip.w <= 0.0001f)
        {
            screen = default;
            return false;
        }

        screen = new Vector2((clip.x / clip.w + 1f) * 0.5f * view3DSize.x, (1f - clip.y / clip.w) * 0.5f * view3DSize.y);
        return true;
    }

    private Ray Ray3D(Vector2 mouse)
    {
        float x = mouse.x / view3DSize.x * 2f - 1f;
        float y = 1f - mouse.y / view3DSize.y * 2f;
        Vector4 near = inverseViewProjection3D * new Vector4(x, y, -1f, 1f);
        Vector4 far = inverseViewProjection3D * new Vector4(x, y, 1f, 1f);
        Vector3 from = (Vector3)near / near.w;
        Vector3 to = (Vector3)far / far.w;
        return new Ray(from, (to - from).normalized);
    }

    private bool InView3D(double from, double to)
    {
        return to >= view3DFrom && from <= view3DTo;
    }

    // ------------------------------------------------------------------ frame

    private void Draw3DEditView(Event current, Vector2 size)
    {
        SetupCamera3D(size);
        if (current.type == EventType.Repaint)
        {
            Render3D(size);
            DrawLabels3D();
            DrawOverlay3D();
        }

        Draw3DToolbar(current, size);
        Handle3DInput(current, size);
    }

    private void Draw3DToolbar(Event current, Vector2 size)
    {
        Rect bar = new Rect(0f, 0f, size.x, Toolbar3DHeight);
        if (current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(bar, new Color(0.09f, 0.09f, 0.1f, 0.88f));
            EditorGUI.DrawRect(new Rect(0f, bar.yMax - 1f, bar.width, 1f), BorderColor);
        }

        float x = 6f;
        Rect Next(float width)
        {
            Rect rect = new Rect(x, 4f, width, Toolbar3DHeight - 8f);
            x += width + 3f;
            return rect;
        }

        GUIStyle title = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
        title.normal.textColor = new Color(0.6f, 0.75f, 1f);
        GUI.Label(Next(52f), "3D編集", title);
        GUI.Label(Next(28f), "視点", EditorStyles.miniLabel);
        if (GUI.Button(Next(40f), new GUIContent("正面", "プレイヤーの位置から奥を見る"), EditorStyles.miniButton))
        {
            SetViewPreset(ViewPreset.Front);
        }

        if (GUI.Button(Next(40f), new GUIContent("上", "真上から（時間が上へ進む）"), EditorStyles.miniButton))
        {
            SetViewPreset(ViewPreset.Top);
        }

        if (GUI.Button(Next(40f), new GUIContent("横", "右横から（時間が右へ進む）"), EditorStyles.miniButton))
        {
            SetViewPreset(ViewPreset.Side);
        }

        if (GUI.Button(Next(40f), new GUIContent("斜め", "斜め上から全体を見る"), EditorStyles.miniButton))
        {
            SetViewPreset(ViewPreset.Angle);
        }

        x += 6f;
        camOrtho = GUI.Toggle(Next(64f), camOrtho, new GUIContent("平行投影", "遠近感なしで表示（図面のように）"), EditorStyles.miniButton);

        if (current.type == EventType.Repaint && x < size.x - 120f)
        {
            GUIStyle hint = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(x + 8f, 3f, size.x - x - 16f, Toolbar3DHeight - 6f),
                "中ドラッグ 回転 ／ Shift+中 移動 ／ Shift+ホイール ズーム ／ ホイール 再生位置 ／ Ctrl+ホイール 奥行き", hint);
        }
    }

    // ------------------------------------------------------------------ rendering

    private static Material CreateGLMaterial(bool depthWrite)
    {
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.SetInt("_ZWrite", depthWrite ? 1 : 0);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        return material;
    }

    private bool Ensure3DResources(Vector2 size)
    {
        if (solid3D == null)
        {
            solid3D = CreateGLMaterial(true);
        }

        if (blend3D == null)
        {
            blend3D = CreateGLMaterial(false);
        }

        if (solid3D == null || blend3D == null)
        {
            return false;
        }

        int width = Mathf.Max(1, Mathf.RoundToInt(size.x));
        int height = Mathf.Max(1, Mathf.RoundToInt(size.y));
        if (view3DTexture == null || view3DTexture.width != width || view3DTexture.height != height)
        {
            ReleaseTexture3D();
            view3DTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "ChartEditor3D",
                antiAliasing = 4,
                hideFlags = HideFlags.HideAndDontSave
            };
            view3DTexture.Create();
        }

        return true;
    }

    private void ReleaseTexture3D()
    {
        if (view3DTexture != null)
        {
            view3DTexture.Release();
            Destroy(view3DTexture);
            view3DTexture = null;
        }
    }

    private void Release3DView()
    {
        ReleaseTexture3D();
        if (solid3D != null)
        {
            Destroy(solid3D);
        }

        if (blend3D != null)
        {
            Destroy(blend3D);
        }
    }

    private void Render3D(Vector2 size)
    {
        if (!Ensure3DResources(size))
        {
            return;
        }

        RenderTexture previous = RenderTexture.active;
        Graphics.SetRenderTarget(view3DTexture);
        GL.Clear(true, true, Lin(Background3D));
        GL.PushMatrix();
        GL.LoadProjectionMatrix(projection3D);
        GL.modelview = view3D;

        DrawStage3D();
        DrawNotes3D();
        DrawLasers3D();
        DrawPlayhead3D();

        GL.PopMatrix();
        RenderTexture.active = previous;
        GUI.DrawTexture(new Rect(0f, 0f, size.x, size.y), view3DTexture, ScaleMode.StretchToFill, false);
    }

    // GL helpers (inside GL.Begin / GL.End). Colours go in as linear values, like the IMGUI line shim.

    /// <summary>
    /// Vertex colours go into the render texture as they are: the GL pass writes them without sRGB
    /// encoding and drawing the texture decodes them, so gamma values come out as authored.
    /// </summary>
    private static Color Lin(Color color)
    {
        return color;
    }

    private static Color Shade(Color color, float factor)
    {
        return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, alpha);
    }

    private static void Vertex(Vector3 point)
    {
        GL.Vertex3(point.x, point.y, point.z);
    }

    private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        GL.Color(Lin(color));
        Vertex(a);
        Vertex(b);
        Vertex(c);
        Vertex(d);
    }

    private static void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        Quad(a, b, c, c, color);
    }

    private static void Line(Vector3 a, Vector3 b, Color color)
    {
        GL.Color(Lin(color));
        Vertex(a);
        Vertex(b);
    }

    /// <summary>A line of world <paramref name="width"/> that always faces the camera (in GL.QUADS).</summary>
    private void ThickLine(Vector3 a, Vector3 b, float width, Color color)
    {
        Vector3 look = camOrtho ? camForward : (a + b) * 0.5f - camEye;
        Vector3 side = Vector3.Cross(b - a, look);
        if (side.sqrMagnitude < 1e-10f)
        {
            return;
        }

        side = side.normalized * (width * 0.5f);
        Quad(a - side, a + side, b + side, b - side, color);
    }

    /// <summary>A line of world <paramref name="width"/> lying in the plane with <paramref name="normal"/> (in GL.QUADS).</summary>
    private static void FlatLine(Vector3 a, Vector3 b, float width, Vector3 normal, Color color)
    {
        Vector3 side = Vector3.Cross(b - a, normal);
        if (side.sqrMagnitude < 1e-10f)
        {
            return;
        }

        side = side.normalized * (width * 0.5f);
        Quad(a - side, a + side, b + side, b - side, color);
    }

    /// <summary>A shaded box (in GL.QUADS): top and bottom full, sides darker, so it reads as a solid.</summary>
    private static void Box(Vector3 center, Vector3 half, Color color)
    {
        Vector3 P(float x, float y, float z) => center + new Vector3(half.x * x, half.y * y, half.z * z);
        Color top = color;
        Color side = Shade(color, 0.72f);
        Color end = Shade(color, 0.86f);
        Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), top);
        Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), top);
        Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), side);
        Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), side);
        Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), end);
        Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), end);
    }

    /// <summary>The twelve edges of a box (in GL.LINES).</summary>
    private static void BoxEdges(Vector3 center, Vector3 half, Color color)
    {
        Vector3 P(float x, float y, float z) => center + new Vector3(half.x * x, half.y * y, half.z * z);
        for (int a = -1; a <= 1; a += 2)
        {
            for (int b = -1; b <= 1; b += 2)
            {
                Line(P(-1, a, b), P(1, a, b), color);
                Line(P(a, -1, b), P(a, 1, b), color);
                Line(P(a, b, -1), P(a, b, 1), color);
            }
        }
    }

    private static void Begin(Material material, int mode)
    {
        material.SetPass(0);
        GL.Begin(mode);
    }

    private void DrawStage3D()
    {
        float z0 = Z(Math.Max(-1d, view3DFrom));
        float z1 = Z(view3DTo);
        float zc = Z(cursorTime);
        bool laserTool = tool == Tool.Laser;

        Begin(blend3D, GL.QUADS);
        for (int lane = 0; lane < 4; lane++)
        {
            float x0 = lane % 2 == 0 ? -Box3DHalfWidth : 0f;
            float x1 = lane % 2 == 0 ? 0f : Box3DHalfWidth;
            float y = SurfaceY(lane);
            Color color = lane % 2 == 0 ? new Color(0.2f, 0.2f, 0.24f, 0.94f) : new Color(0.17f, 0.17f, 0.21f, 0.94f);
            Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), color);
        }

        // The placement plane: where laser points go (the stick's square at the playhead).
        Quad(LaserWorld(new Vector2(-1f, -1f), zc), LaserWorld(new Vector2(1f, -1f), zc), LaserWorld(new Vector2(1f, 1f), zc), LaserWorld(new Vector2(-1f, 1f), zc),
            new Color(1f, 1f, 1f, laserTool ? 0.07f : 0.025f));
        GL.End();

        Begin(blend3D, GL.LINES);
        foreach (float y in new[] { Box3DHalfHeight, -Box3DHalfHeight })
        {
            Line(new Vector3(-Box3DHalfWidth, y, z0), new Vector3(-Box3DHalfWidth, y, z1), new Color(1f, 1f, 1f, 0.38f));
            Line(new Vector3(0f, y, z0), new Vector3(0f, y, z1), new Color(1f, 1f, 1f, 0.16f));
            Line(new Vector3(Box3DHalfWidth, y, z0), new Vector3(Box3DHalfWidth, y, z1), new Color(1f, 1f, 1f, 0.38f));
        }

        DrawGrid3D();

        // The placement plane's frame and, with the laser tool, its 0.25 grid.
        Color frame = new Color(1f, 1f, 1f, laserTool ? 0.45f : 0.16f);
        Vector2[] corners = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
        for (int index = 0; index < 4; index++)
        {
            Line(LaserWorld(corners[index], zc), LaserWorld(corners[(index + 1) % 4], zc), frame);
        }

        if (laserTool)
        {
            for (float value = -0.75f; value <= 0.76f; value += 0.25f)
            {
                Color grid = new Color(1f, 1f, 1f, Mathf.Abs(value) < 0.01f ? 0.22f : 0.07f);
                Line(LaserWorld(new Vector2(value, -1f), zc), LaserWorld(new Vector2(value, 1f), zc), grid);
                Line(LaserWorld(new Vector2(-1f, value), zc), LaserWorld(new Vector2(1f, value), zc), grid);
            }
        }

        GL.End();
    }

    private void DrawGrid3D()
    {
        double beat = BeatSeconds;
        double step = GridStep;
        float perSecond = DepthPerSecond;
        if (step * perSecond < 0.12d)
        {
            step = beat;
        }

        if (step * perSecond < 0.12d)
        {
            step = beat * 4d;
        }

        long first = (long)Math.Ceiling((Math.Max(-1d, view3DFrom) - Chart.offset) / step);
        long last = (long)Math.Floor((view3DTo - Chart.offset) / step);
        if (last - first > 3000)
        {
            return;
        }

        for (long index = first; index <= last; index++)
        {
            double time = Chart.offset + index * step;
            double beats = index * step / beat;
            bool onBeat = Math.Abs(beats - Math.Round(beats)) < 0.0001d;
            bool onMeasure = onBeat && (long)Math.Round(beats) % 4 == 0;
            float z = Z(time);
            Color color = onMeasure ? new Color(1f, 1f, 1f, 0.42f) : onBeat ? new Color(1f, 1f, 1f, 0.18f) : new Color(1f, 1f, 1f, 0.06f);
            Line(new Vector3(-Box3DHalfWidth, Box3DHalfHeight, z), new Vector3(Box3DHalfWidth, Box3DHalfHeight, z), color);
            Line(new Vector3(-Box3DHalfWidth, -Box3DHalfHeight, z), new Vector3(Box3DHalfWidth, -Box3DHalfHeight, z), color);
            if (onMeasure)
            {
                // A measure closes the box's cross-section, like the frames that flow by in the game.
                Color side = new Color(1f, 1f, 1f, 0.2f);
                Line(new Vector3(-Box3DHalfWidth, Box3DHalfHeight, z), new Vector3(-Box3DHalfWidth, -Box3DHalfHeight, z), side);
                Line(new Vector3(Box3DHalfWidth, Box3DHalfHeight, z), new Vector3(Box3DHalfWidth, -Box3DHalfHeight, z), side);
            }
        }
    }

    // Note shapes in the box.
    private Vector3 NoteHeadCenter(TapNoteData note, double time)
    {
        int lane = Mathf.Clamp(note.lane, 0, 3);
        return new Vector3(LaneCenterX(lane), SurfaceY(lane) + Inward(lane) * 0.06f, Z(time));
    }

    private static readonly Vector3 NoteHeadHalf = new Vector3(Lane3DWidth * 0.44f, 0.06f, 0.07f);
    private static readonly Vector3 HoldTailHalf = new Vector3(Lane3DWidth * 0.36f, 0.04f, 0.05f);
    private const float IconSize = 0.2f;

    /// <summary>Where a note's chevron / spin mark stands: in the cross-section plane, a little inside the box from its lane.</summary>
    private Vector3 NoteIconCenter(TapNoteData note)
    {
        int lane = Mathf.Clamp(note.lane, 0, 3);
        return new Vector3(LaneCenterX(lane), SurfaceY(lane) + Inward(lane) * 0.42f, Z(note.time));
    }

    /// <summary>The point of a note box selection looks at.</summary>
    private Vector3 NoteAnchor3D(TapNoteData note)
    {
        return note.IsFlickOnly || note.IsSpin ? NoteIconCenter(note) : NoteHeadCenter(note, note.time);
    }

    private void DrawNotes3D()
    {
        bool playing = playback.IsPlaying || flow.IsEmbeddedPreview;

        // Hold bodies, translucent on the lane.
        Begin(blend3D, GL.QUADS);
        foreach (TapNoteData note in Chart.notes)
        {
            if (!note.IsHold || note.IsFlickOnly || !InView3D(note.time, note.EndTime))
            {
                continue;
            }

            int lane = Mathf.Clamp(note.lane, 0, 3);
            float y = SurfaceY(lane) + Inward(lane) * 0.01f;
            float x = LaneCenterX(lane);
            float half = Lane3DWidth * 0.28f;
            float from = Z(note.time);
            float to = Z(note.EndTime);
            bool selected = selectedNotes.Contains(note);
            Color body = selected ? WithAlpha(SelectColor, 0.5f) : WithAlpha(HoldColor, note.EndTime < cursorTime ? 0.18f : 0.42f);
            Quad(new Vector3(x - half, y, from), new Vector3(x + half, y, from), new Vector3(x + half, y, to), new Vector3(x - half, y, to), body);
        }

        GL.End();

        Begin(solid3D, GL.QUADS);
        foreach (TapNoteData note in Chart.notes)
        {
            if (!InView3D(note.time, note.EndTime))
            {
                continue;
            }

            bool selected = selectedNotes.Contains(note);
            bool past = note.EndTime < cursorTime - 0.0001d;
            double age = cursorTime - note.time;
            float flash = playing && age >= 0d && age < 0.15d ? 1f - (float)(age / 0.15d) : 0f;
            Color stick = RhythmStageView.GetStickColor(note.GetStickSide(note.lane));

            Color Tone(Color color)
            {
                if (selected)
                {
                    color = Color.Lerp(color, SelectColor, 0.6f);
                }

                if (past)
                {
                    color = Shade(color, 0.42f);
                }

                return Color.Lerp(color, Color.white, flash);
            }

            if (!note.IsFlickOnly && !note.IsSpin)
            {
                Box(NoteHeadCenter(note, note.time), NoteHeadHalf, Tone(note.IsHold ? HoldColor : TapColor));
                if (note.IsHold)
                {
                    Box(NoteHeadCenter(note, note.EndTime), HoldTailHalf, Tone(HoldColor));
                }
            }

            if (!note.HasDirection && !note.IsSpin)
            {
                continue;
            }

            Vector3 icon = NoteIconCenter(note);
            if (note.IsFlickOnly || note.IsSpin)
            {
                // A dark tile behind the mark, outlined in the stick colour.
                float half = IconSize * 1.25f;
                Vector3 back = icon + new Vector3(0f, 0f, 0.012f);
                Quad(back + new Vector3(-half, -half, 0f), back + new Vector3(half, -half, 0f), back + new Vector3(half, half, 0f), back + new Vector3(-half, half, 0f),
                    Tone(new Color(0.07f, 0.07f, 0.09f)));
                Color outline = Tone(stick);
                Vector3 front = icon + new Vector3(0f, 0f, 0.006f);
                FlatLine(front + new Vector3(-half, -half, 0f), front + new Vector3(half, -half, 0f), 0.03f, Vector3.forward, outline);
                FlatLine(front + new Vector3(half, -half, 0f), front + new Vector3(half, half, 0f), 0.03f, Vector3.forward, outline);
                FlatLine(front + new Vector3(half, half, 0f), front + new Vector3(-half, half, 0f), 0.03f, Vector3.forward, outline);
                FlatLine(front + new Vector3(-half, half, 0f), front + new Vector3(-half, -half, 0f), 0.03f, Vector3.forward, outline);
            }

            if (note.IsSpin)
            {
                SpinMark3D(icon, note.spin, IconSize, Tone(stick));
            }
            else
            {
                Arrow3D(icon, note.direction, IconSize, Tone(stick));
            }
        }

        GL.End();

        // Selection outlines.
        Begin(blend3D, GL.LINES);
        foreach (TapNoteData note in selectedNotes)
        {
            if (!InView3D(note.time, note.EndTime))
            {
                continue;
            }

            if (note.IsFlickOnly || note.IsSpin)
            {
                BoxEdges(NoteIconCenter(note), new Vector3(IconSize * 1.5f, IconSize * 1.5f, 0.05f), SelectColor);
            }
            else
            {
                BoxEdges(NoteHeadCenter(note, note.time), NoteHeadHalf + new Vector3(0.05f, 0.04f, 0.04f), SelectColor);
            }
        }

        GL.End();
    }

    /// <summary>The chevron of a direction, standing in the cross-section plane (stick right = +x, up = +y).</summary>
    private static void Arrow3D(Vector3 center, NoteDirection direction, float size, Color color)
    {
        if (direction == NoteDirection.None)
        {
            Box(center, new Vector3(size * 0.2f, size * 0.2f, 0.01f), color);
            return;
        }

        Vector2 vector = StickInput.GetDirectionVector(direction);
        Vector3 forward = new Vector3(vector.x, vector.y, 0f).normalized;
        Vector3 across = new Vector3(-forward.y, forward.x, 0f);
        Vector3 tip = center + forward * size;
        Vector3 baseCenter = center - forward * size * 0.55f;
        Vector3 notch = center - forward * size * 0.05f;
        Triangle(tip, baseCenter + across * size * 0.9f, notch, color);
        Triangle(tip, notch, baseCenter - across * size * 0.9f, color);
    }

    /// <summary>The spin mark (an arc with an arrowhead) in the cross-section plane, as the timeline draws it.</summary>
    private static void SpinMark3D(Vector3 center, int spin, float size, Color color)
    {
        float mirror = spin == TapNoteData.SpinClockwise ? -1f : 1f;
        const float startDegrees = -15f;
        const float endDegrees = 195f;
        const int segments = 14;
        Vector3 Point(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return center + new Vector3(0f, -size * 0.45f, 0f) + new Vector3(Mathf.Cos(radians) * mirror, Mathf.Sin(radians), 0f) * (size * 0.75f);
        }

        Vector3 previous = Point(startDegrees);
        for (int index = 1; index <= segments; index++)
        {
            Vector3 next = Point(Mathf.Lerp(startDegrees, endDegrees, index / (float)segments));
            FlatLine(previous, next, size * 0.2f, Vector3.forward, color);
            previous = next;
        }

        float endRadians = endDegrees * Mathf.Deg2Rad;
        Vector3 radial = new Vector3(Mathf.Cos(endRadians) * mirror, Mathf.Sin(endRadians), 0f).normalized;
        Vector3 tangent = new Vector3(-Mathf.Sin(endRadians) * mirror, Mathf.Cos(endRadians), 0f).normalized * -1f;
        float head = size * 0.5f;
        Vector3 end = Point(endDegrees);
        Triangle(end + radial * head, end - radial * head, end + tangent * head * 1.3f, color);
    }

    private void DrawLasers3D()
    {
        Begin(blend3D, GL.QUADS);
        foreach (LaserNoteData laser in Chart.lasers)
        {
            if (laser.points.Length < 2 || !InView3D(LaserStart(laser), LaserEnd(laser)))
            {
                continue;
            }

            bool selected = IsLaserSelected(laser);
            Color color = RhythmStageView.GetStickColor(laser.side);
            float width = selected ? 0.08f : 0.05f;
            for (int index = 0; index < laser.points.Length - 1; index++)
            {
                double from = Math.Max(laser.points[index].time, view3DFrom);
                double to = Math.Min(laser.points[index + 1].time, view3DTo);
                if (to < from)
                {
                    continue;
                }

                // The part already passed is dim; the part still to come bright.
                if (from < cursorTime && to > cursorTime)
                {
                    LaserPiece(laser, from, cursorTime, width, WithAlpha(color, 0.2f));
                    LaserPiece(laser, cursorTime, to, width, WithAlpha(color, selected ? 1f : 0.85f));
                }
                else
                {
                    LaserPiece(laser, from, to, width, WithAlpha(color, to <= cursorTime ? 0.2f : selected ? 1f : 0.85f));
                }
            }

            // Where the stick has to be at the playhead: a white square on the placement plane.
            if (LaserStart(laser) <= cursorTime && LaserEnd(laser) >= cursorTime)
            {
                Vector3 head = LaserWorld(Evaluate(laser, cursorTime), Z(cursorTime));
                const float half = 0.11f;
                Vector3 a = head + new Vector3(-half, -half, 0f);
                Vector3 b = head + new Vector3(half, -half, 0f);
                Vector3 c = head + new Vector3(half, half, 0f);
                Vector3 d = head + new Vector3(-half, half, 0f);
                FlatLine(a, b, 0.035f, Vector3.forward, Color.white);
                FlatLine(b, c, 0.035f, Vector3.forward, Color.white);
                FlatLine(c, d, 0.035f, Vector3.forward, Color.white);
                FlatLine(d, a, 0.035f, Vector3.forward, Color.white);
            }
        }

        GL.End();

        // Points as small cubes; the selected point white.
        Begin(solid3D, GL.QUADS);
        foreach (LaserNoteData laser in Chart.lasers)
        {
            if (!InView3D(LaserStart(laser), LaserEnd(laser)))
            {
                continue;
            }

            bool selected = IsLaserSelected(laser);
            Color color = RhythmStageView.GetStickColor(laser.side);
            for (int index = 0; index < laser.points.Length; index++)
            {
                LaserPointData point = laser.points[index];
                if (point.time < view3DFrom || point.time > view3DTo)
                {
                    continue;
                }

                bool pointSelected = laser == selectedLaser && index == selectedPoint || selectedPoints.Contains(point);
                float half = pointSelected ? 0.09f : selected ? 0.07f : 0.05f;
                Color shown = pointSelected ? Color.white : point.time < cursorTime ? Shade(color, 0.5f) : color;
                Box(LaserWorld(new Vector2(point.x, point.y), Z(point.time)), Vector3.one * half, shown);
            }
        }

        GL.End();

        Begin(blend3D, GL.LINES);
        if (selectedLaser != null && selectedPoint >= 0 && selectedPoint < selectedLaser.points.Length)
        {
            LaserPointData point = selectedLaser.points[selectedPoint];
            BoxEdges(LaserWorld(new Vector2(point.x, point.y), Z(point.time)), Vector3.one * 0.13f, SelectColor);
        }

        GL.End();
    }

    private void LaserPiece(LaserNoteData laser, double from, double to, float width, Color color)
    {
        Vector3 a = LaserWorld(Evaluate(laser, from), Z(from));
        Vector3 b = LaserWorld(Evaluate(laser, to), Z(to));
        ThickLine(a, b, width, color);
    }

    private void DrawPlayhead3D()
    {
        float z = Z(cursorTime);
        Color color = playback.IsPlaying || flow.IsEmbeddedPreview ? new Color(0.3f, 1f, 0.5f) : CursorColor;
        Vector3 a = new Vector3(-Box3DHalfWidth, Box3DHalfHeight, z);
        Vector3 b = new Vector3(Box3DHalfWidth, Box3DHalfHeight, z);
        Vector3 c = new Vector3(Box3DHalfWidth, -Box3DHalfHeight, z);
        Vector3 d = new Vector3(-Box3DHalfWidth, -Box3DHalfHeight, z);
        Begin(blend3D, GL.QUADS);
        ThickLine(a, b, 0.045f, color);
        ThickLine(c, d, 0.045f, color);
        ThickLine(b, c, 0.025f, WithAlpha(color, 0.6f));
        ThickLine(d, a, 0.025f, WithAlpha(color, 0.6f));
        GL.End();
    }

    private void DrawLabels3D()
    {
        GUIStyle laneStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
        laneStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
        float zc = Z(cursorTime);
        for (int lane = 0; lane < 4; lane++)
        {
            Vector3 world = new Vector3(LaneCenterX(lane), SurfaceY(lane) + Inward(lane) * 0.2f, zc - 0.25f);
            if (Project3D(world, out Vector2 screen) && screen.y > Toolbar3DHeight)
            {
                GUI.Label(new Rect(screen.x - 20f, screen.y - 8f, 40f, 16f), LaneNames[lane], laneStyle);
            }
        }

        // Measure numbers at the top-left edge of the box.
        GUIStyle measureStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };
        measureStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        double measure = BeatSeconds * 4d;
        long first = (long)Math.Ceiling((Math.Max(0d, view3DFrom) - Chart.offset) / measure);
        long last = (long)Math.Floor((view3DTo - Chart.offset) / measure);
        Vector2 previous = new Vector2(-1000f, -1000f);
        for (long index = Math.Max(0, first); index <= last && index - first < 120; index++)
        {
            Vector3 world = new Vector3(-Box3DHalfWidth - 0.08f, Box3DHalfHeight, Z(Chart.offset + index * measure));
            if (!Project3D(world, out Vector2 screen) || screen.y < Toolbar3DHeight || screen.x < 0f || screen.y > view3DSize.y)
            {
                continue;
            }

            // Skip numbers that would sit on top of the previous one (far away they bunch up).
            if (Vector2.Distance(screen, previous) < 26f)
            {
                continue;
            }

            previous = screen;
            GUI.Label(new Rect(screen.x - 44f, screen.y - 8f, 40f, 16f), "#" + (index + 1), measureStyle);
        }

        // The playhead's time at its top-right corner.
        if (Project3D(new Vector3(Box3DHalfWidth, Box3DHalfHeight, zc), out Vector2 corner) && corner.y > Toolbar3DHeight)
        {
            GUIStyle timeStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.LowerLeft };
            timeStyle.normal.textColor = playback.IsPlaying || flow.IsEmbeddedPreview ? new Color(0.3f, 1f, 0.5f) : CursorColor;
            GUI.Label(new Rect(corner.x + 6f, corner.y - 16f, 160f, 16f), FormatTime(cursorTime), timeStyle);
        }

        if (Chart.notes.Length == 0 && Chart.lasers.Length == 0)
        {
            GUIStyle hint = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { alignment = TextAnchor.MiddleCenter };
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(0f, view3DSize.y * 0.75f, view3DSize.x, 40f), "天井 (L / R) と床 (ZL / ZR) をクリックしてノーツを置く\nレーザーツール (L) で赤い枠の中をクリックしてレーザーの点を置く", hint);
        }
    }

    private void DrawOverlay3D()
    {
        if (drag == DragMode.BoxSelect)
        {
            EditorGUI.DrawRect(boxRect, new Color(SelectColor.r, SelectColor.g, SelectColor.b, 0.12f));
            DrawOutline(boxRect, SelectColor, 1f);
        }
        else if (drag == DragMode.SetDirection && (dirMouse - dirOrigin).magnitude >= DirectionDragThreshold)
        {
            Handles.SetClip(new Rect(0f, 0f, view3DSize.x, view3DSize.y));
            Handles.color = dirForceFlick ? RhythmStageView.GetStickColor(dragAnchor != null ? dragAnchor.GetStickSide(dirLane) : 0) : Color.white;
            Handles.DrawAAPolyLine(2f, new Vector3(dirOrigin.x, dirOrigin.y, 0f), new Vector3(dirMouse.x, dirMouse.y, 0f));
            Handles.ClearClip();
        }
    }

    // ------------------------------------------------------------------ picking

    private bool TryHitLane3D(Ray ray, int row, out int lane, out double time)
    {
        lane = -1;
        time = 0d;
        float best = float.MaxValue;
        for (int candidate = 0; candidate < 2; candidate++)
        {
            if (row >= 0 && candidate != row)
            {
                continue;
            }

            float y = candidate == 0 ? Box3DHalfHeight : -Box3DHalfHeight;
            if (!new Plane(Vector3.up, new Vector3(0f, y, 0f)).Raycast(ray, out float distance) || distance <= 0f || distance >= best)
            {
                continue;
            }

            Vector3 hit = ray.GetPoint(distance);
            if (row < 0 && Mathf.Abs(hit.x) > Box3DHalfWidth + 0.02f)
            {
                continue;
            }

            best = distance;
            lane = candidate * 2 + (hit.x < 0f ? 0 : 1);
            time = TimeAtZ(hit.z);
        }

        return lane >= 0;
    }

    private bool TryHitLaserPlane(Ray ray, out Vector2 value)
    {
        value = default;
        if (!new Plane(Vector3.forward, new Vector3(0f, 0f, Z(cursorTime))).Raycast(ray, out float distance) || distance <= 0f)
        {
            return false;
        }

        Vector3 hit = ray.GetPoint(distance);
        value = new Vector2(hit.x / Laser3DHalfWidth, hit.y / Laser3DHalfHeight);
        return Mathf.Abs(value.x) <= 1.15f && Mathf.Abs(value.y) <= 1.15f;
    }

    /// <summary>The nearest note under the ray (its bar, chevron, hold body or tail).</summary>
    private TapNoteData HitNote3D(Ray ray, out bool tailHit)
    {
        TapNoteData best = null;
        float bestDistance = float.MaxValue;
        bool bestTail = false;
        foreach (TapNoteData note in Chart.notes)
        {
            if (!InView3D(note.time, note.EndTime))
            {
                continue;
            }

            void Try(Bounds bounds, bool tail)
            {
                if (bounds.IntersectRay(ray, out float distance) && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = note;
                    bestTail = tail;
                }
            }

            Vector3 thickness = new Vector3(0f, 0f, 0.1f);
            if (!note.IsFlickOnly && !note.IsSpin)
            {
                Try(new Bounds(NoteHeadCenter(note, note.time), (NoteHeadHalf + new Vector3(0.02f, 0.06f, 0.06f)) * 2f), false);
                if (note.IsHold)
                {
                    Try(new Bounds(NoteHeadCenter(note, note.EndTime), (HoldTailHalf + new Vector3(0.04f, 0.06f, 0.06f)) * 2f), true);
                    Vector3 from = NoteHeadCenter(note, note.time);
                    Vector3 to = NoteHeadCenter(note, note.EndTime);
                    Try(new Bounds((from + to) * 0.5f, new Vector3(Lane3DWidth * 0.56f, 0.08f, Mathf.Abs(to.z - from.z))), false);
                }
            }

            if (note.HasDirection || note.IsSpin)
            {
                Try(new Bounds(NoteIconCenter(note), new Vector3(IconSize * 2.6f, IconSize * 2.6f, 0f) + thickness), false);
            }
        }

        tailHit = bestTail;
        return best;
    }

    private bool HitLaserPoint3D(Vector2 mouse, out LaserNoteData hitLaser, out int hitPoint)
    {
        hitLaser = null;
        hitPoint = -1;
        float best = PointPickPixels;
        IEnumerable<LaserNoteData> candidates = selectedLaser != null ? new[] { selectedLaser } : Array.Empty<LaserNoteData>();
        foreach (LaserNoteData laser in Concat(candidates, Chart.lasers))
        {
            for (int index = 0; index < laser.points.Length; index++)
            {
                LaserPointData point = laser.points[index];
                if (point.time < view3DFrom || point.time > view3DTo
                    || !Project3D(LaserWorld(new Vector2(point.x, point.y), Z(point.time)), out Vector2 screen))
                {
                    continue;
                }

                float distance = Vector2.Distance(mouse, screen);
                if (distance < best)
                {
                    best = distance;
                    hitLaser = laser;
                    hitPoint = index;
                }
            }

            if (hitLaser != null && laser == selectedLaser)
            {
                // A point of the selected laser wins over the others.
                break;
            }
        }

        return hitLaser != null;
    }

    private static IEnumerable<T> Concat<T>(IEnumerable<T> first, IEnumerable<T> second)
    {
        foreach (T item in first)
        {
            yield return item;
        }

        foreach (T item in second)
        {
            yield return item;
        }
    }

    private LaserNoteData HitLaserLine3D(Vector2 mouse)
    {
        LaserNoteData best = null;
        float bestDistance = LaserPickPixels;
        foreach (LaserNoteData laser in Chart.lasers)
        {
            if (laser.points.Length < 2 || !InView3D(LaserStart(laser), LaserEnd(laser)))
            {
                continue;
            }

            for (int index = 0; index < laser.points.Length - 1; index++)
            {
                double from = Math.Max(laser.points[index].time, view3DFrom);
                double to = Math.Min(laser.points[index + 1].time, view3DTo);
                if (to < from
                    || !Project3D(LaserWorld(Evaluate(laser, from), Z(from)), out Vector2 a)
                    || !Project3D(LaserWorld(Evaluate(laser, to), Z(to)), out Vector2 b))
                {
                    continue;
                }

                float distance = DistanceToSegment(mouse, a, b);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = laser;
                }
            }
        }

        return best;
    }

    // ------------------------------------------------------------------ input

    private void Handle3DInput(Event current, Vector2 size)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Vector2 mouse = current.mousePosition;
        bool inside = mouse.x >= 0f && mouse.y >= Toolbar3DHeight && mouse.x < size.x && mouse.y < size.y && !pointerBlocked;

        switch (current.GetTypeForControl(controlId))
        {
            case EventType.Repaint:
            case EventType.MouseMove:
                if (inside && drag == DragMode.None)
                {
                    hoverHint = LaneHint() + " ／ 中ドラッグ: 視点を回す ・Shift+中: 移動 ・Shift+ホイール: ズーム ・ホイール: 再生位置";
                }

                break;

            case EventType.ScrollWheel:
                {
                    if (!inside)
                    {
                        break;
                    }

                    float delta = Mathf.Abs(current.delta.y) > 0.0001f ? current.delta.y : current.delta.x;
                    float sign = Mathf.Sign(delta);
                    if (current.shift)
                    {
                        camDistance = Mathf.Clamp(camDistance * Mathf.Pow(1.12f, sign), 1.2f, 150f);
                    }
                    else if (current.control || current.command)
                    {
                        SetZoom(pixelsPerSecond * Mathf.Pow(1.12f, -sign), TimeToY(cursorTime));
                    }
                    else
                    {
                        StepCursor3D(-(int)sign);
                    }

                    current.Use();
                    break;
                }

            case EventType.MouseDown:
                if (!inside)
                {
                    break;
                }

                GUIUtility.keyboardControl = 0;
                if (OnMouseDown3D(current, mouse))
                {
                    GUIUtility.hotControl = controlId;
                }

                current.Use();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != controlId)
                {
                    break;
                }

                OnMouseDrag3D(mouse);
                current.Use();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != controlId)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                if (drag == DragMode.BoxSelect)
                {
                    FinishBoxSelect3D();
                }
                else if (drag == DragMode.SetDirection)
                {
                    FinishDirectionDrag();
                }

                EndDrag();
                current.Use();
                break;
        }
    }

    /// <summary>The wheel moves the playhead (and with it the placement plane) by grid steps.</summary>
    private void StepCursor3D(int steps)
    {
        if (steps == 0)
        {
            return;
        }

        double target = Math.Max(0d, Snap(cursorTime + steps * GridStep));
        if (flow.IsEmbeddedPreview)
        {
            flow.StopEmbeddedPreview();
            previewWasRunning = false;
        }

        if (playback.IsPlaying)
        {
            PlayFrom(target);
        }
        else
        {
            cursorTime = target;
        }
    }

    /// <returns>True when a drag starts.</returns>
    private bool OnMouseDown3D(Event current, Vector2 mouse)
    {
        if (current.button == 2)
        {
            navStartMouse = mouse;
            navStartYaw = camYaw;
            navStartPitch = camPitch;
            navStartDistance = camDistance;
            navStartPan = camPan;
            drag = current.shift ? DragMode.PanView : current.control || current.command ? DragMode.Dolly : DragMode.Orbit;
            return true;
        }

        Ray ray = Ray3D(mouse);
        bool additive = current.control || current.command;
        bool pointHit = HitLaserPoint3D(mouse, out LaserNoteData pointLaser, out int pointIndex);
        TapNoteData note = HitNote3D(ray, out bool tailHit);
        LaserNoteData lineLaser = pointHit ? null : HitLaserLine3D(mouse);
        bool laneHit = TryHitLane3D(ray, -1, out int lane, out double laneTime);
        double noteTime = note != null && TryHitLane3D(ray, RowOfLane(note.lane), out _, out double rowTime) ? rowTime : note?.time ?? 0d;

        if (current.button == 1)
        {
            if (pointHit)
            {
                DeleteLaserPoint(pointLaser, pointIndex);
                return false;
            }

            if (note != null)
            {
                return OnLaneMouseDown(current, note.lane, mouse, noteTime, note, tailHit);
            }

            if (lineLaser != null)
            {
                LaserNoteData laser = lineLaser;
                Modify("Delete laser", () => Chart.lasers = Chart.lasers.Where(item => item != laser).ToArray());
                ClearSelection();
                return false;
            }

            return laneHit && OnLaneMouseDown(current, lane, mouse, laneTime, null, false);
        }

        if (current.button != 0)
        {
            return false;
        }

        if (tool == Tool.Erase)
        {
            BeginDrag(DragMode.Erase, "Erase");
            EraseAt3D(mouse);
            return true;
        }

        if (tool != Tool.Box)
        {
            if (pointHit)
            {
                if (additive)
                {
                    TogglePointInSelection(pointLaser, pointIndex);
                    return false;
                }

                SelectLaser(pointLaser, pointIndex);
                placeLaserSide = pointLaser.side;
                pointDragTime = false;
                BeginDrag(DragMode.MovePoint, "Move laser point");
                return true;
            }

            if (note != null)
            {
                return OnLaneMouseDown(current, note.lane, mouse, noteTime, note, tailHit);
            }

            if (lineLaser != null)
            {
                if (additive)
                {
                    ToggleLaserInSelection(lineLaser);
                    FinishSelectionOfLasers();
                }
                else
                {
                    SelectLaser(lineLaser, -1);
                    placeLaserSide = lineLaser.side;
                }

                return false;
            }

            if (tool == Tool.Laser && TryHitLaserPlane(ray, out Vector2 value))
            {
                LaserNoteData target = selectedLaser != null && selectedLaser.side == placeLaserSide ? selectedLaser : null;
                PlaceLaserPointAtCursor(placeLaserSide, target, value);
                pointDragTime = false;
                return true;
            }
        }

        if (tool == Tool.Box || !laneHit || current.shift || tool == Tool.Select || tool == Tool.Laser)
        {
            if (!additive)
            {
                ClearSelection();
            }

            drag = DragMode.BoxSelect;
            boxStart = mouse;
            boxRect = new Rect(mouse, Vector2.zero);
            return true;
        }

        return OnLaneMouseDown(current, lane, mouse, laneTime, null, false);
    }

    private void DeleteLaserPoint(LaserNoteData laser, int point)
    {
        if (laser.points.Length > 2)
        {
            Modify("Delete laser point", () => laser.points = laser.points.Where((_, index) => index != point).ToArray());
            SelectLaser(laser, -1);
        }
        else
        {
            Modify("Delete laser", () => Chart.lasers = Chart.lasers.Where(item => item != laser).ToArray());
            ClearSelection();
        }
    }

    private void OnMouseDrag3D(Vector2 mouse)
    {
        Vector2 delta = mouse - navStartMouse;
        Ray ray = Ray3D(mouse);
        switch (drag)
        {
            case DragMode.Orbit:
                camYaw = navStartYaw + delta.x * 0.3f;
                camPitch = Mathf.Clamp(navStartPitch + delta.y * 0.3f, -89.9f, 90f);
                break;

            case DragMode.PanView:
                {
                    Quaternion rotation = Quaternion.Euler(camPitch, camYaw, 0f);
                    float unitsPerPixel = 2f * camDistance * Mathf.Tan(FieldOfView3D * 0.5f * Mathf.Deg2Rad) / view3DSize.y;
                    camPan = navStartPan - rotation * Vector3.right * (delta.x * unitsPerPixel) + rotation * Vector3.up * (delta.y * unitsPerPixel);
                    break;
                }

            case DragMode.Dolly:
                camDistance = Mathf.Clamp(navStartDistance * Mathf.Exp(delta.y * 0.01f), 1.2f, 150f);
                break;

            case DragMode.BoxSelect:
                boxRect = Rect.MinMaxRect(
                    Mathf.Min(boxStart.x, mouse.x),
                    Mathf.Min(boxStart.y, mouse.y),
                    Mathf.Max(boxStart.x, mouse.x),
                    Mathf.Max(boxStart.y, mouse.y));
                break;

            case DragMode.ResizeHold:
                if (dragAnchor != null && TryHitLane3D(ray, RowOfLane(dragAnchor.lane), out _, out double tailTime))
                {
                    TapNoteData note = dragAnchor;
                    double duration = Snap(tailTime) - note.time;
                    duration = duration < 0.0005d ? 0d : duration;
                    if (Math.Abs(duration - note.duration) > 0.00001d)
                    {
                        DragModify("Resize hold", () => note.duration = duration);
                    }
                }

                break;

            case DragMode.MoveNotes:
                if (TryHitLane3D(ray, -1, out int lane, out double time))
                {
                    DragNotesTo(time, SlotOfLane(lane));
                }
                else if (dragAnchor != null && TryHitLane3D(ray, RowOfLane(dragAnchor.lane), out _, out time))
                {
                    DragNotesTo(time, -1);
                }

                break;

            case DragMode.MovePoint:
            case DragMode.PadPoint:
                DragPoint3D(ray);
                break;

            case DragMode.SetDirection:
                DragDirection(mouse);
                break;

            case DragMode.Erase:
                EraseAt3D(mouse);
                break;
        }
    }

    /// <summary>Drags the selected laser point across its cross-section (X / Y), or along time with Ctrl.</summary>
    private void DragPoint3D(Ray ray)
    {
        LaserNoteData laser = selectedLaser;
        if (laser == null || selectedPoint < 0 || selectedPoint >= laser.points.Length)
        {
            return;
        }

        int index = selectedPoint;
        LaserPointData point = laser.points[index];
        Vector3 world = LaserWorld(new Vector2(point.x, point.y), Z(point.time));
        Event current = Event.current;
        if (pointDragTime || current != null && (current.control || current.command))
        {
            // Along time: a plane holding the time axis that faces the camera as much as it can.
            Vector3 normal = Mathf.Abs(camForward.y) > Mathf.Abs(camForward.x) ? Vector3.up : Vector3.right;
            if (!new Plane(normal, world).Raycast(ray, out float distance))
            {
                return;
            }

            double newTime = Snap(TimeAtZ(ray.GetPoint(distance).z));
            if (index > 0)
            {
                newTime = Math.Max(newTime, laser.points[index - 1].time + 0.001d);
            }

            if (index < laser.points.Length - 1)
            {
                newTime = Math.Min(newTime, laser.points[index + 1].time - 0.001d);
            }

            DragModify("Move laser point", () => point.time = newTime);
            return;
        }

        if (!new Plane(Vector3.forward, world).Raycast(ray, out float enter))
        {
            return;
        }

        Vector3 hit = ray.GetPoint(enter);
        float x = SnapLaserValue(hit.x / Laser3DHalfWidth);
        float y = SnapLaserValue(hit.y / Laser3DHalfHeight);
        if (!Mathf.Approximately(x, point.x) || !Mathf.Approximately(y, point.y))
        {
            DragModify("Move laser point", () =>
            {
                point.x = x;
                point.y = y;
            });
        }
    }

    private void FinishBoxSelect3D()
    {
        bool additive = Event.current.control || Event.current.command;
        if (!additive)
        {
            selectedNotes.Clear();
            selectedLasers.Clear();
        }
        else if (selectedLaser != null && !selectedLasers.Contains(selectedLaser))
        {
            selectedLasers.Add(selectedLaser);
        }

        selectedLaser = null;
        selectedPoint = -1;
        if (!additive)
        {
            selectedPoints.Clear();
        }

        foreach (TapNoteData note in Chart.notes)
        {
            if (InView3D(note.time, note.time) && !selectedNotes.Contains(note)
                && Project3D(NoteAnchor3D(note), out Vector2 screen) && boxRect.Contains(screen))
            {
                selectedNotes.Add(note);
            }
        }

        foreach (LaserNoteData laser in Chart.lasers)
        {
            if (selectedLasers.Contains(laser))
            {
                continue;
            }

            // Every point in the box: the whole laser comes along; only some: just those points.
            List<LaserPointData> inside = laser.points.Where(point => point.time >= view3DFrom && point.time <= view3DTo
                && Project3D(LaserWorld(new Vector2(point.x, point.y), Z(point.time)), out Vector2 screen) && boxRect.Contains(screen)).ToList();
            if (inside.Count == laser.points.Length)
            {
                selectedLasers.Add(laser);
            }
            else
            {
                foreach (LaserPointData point in inside)
                {
                    selectedPoints.Add(point);
                }
            }
        }

        selectedPoints.RemoveWhere(point => selectedLasers.Any(laser => Array.IndexOf(laser.points, point) >= 0));
        FinishSelectionOfLasers();
        FinishSelectionOfPoints();
    }

    /// <summary>Eraser in the 3D view: a laser point, else a note, else a laser line under the pointer.</summary>
    private void EraseAt3D(Vector2 mouse)
    {
        if (HitLaserPoint3D(mouse, out LaserNoteData laser, out int point))
        {
            if (laser.points.Length > 2)
            {
                DragModify("Erase", () => laser.points = laser.points.Where((_, index) => index != point).ToArray());
                if (laser == selectedLaser)
                {
                    selectedPoint = -1;
                }
            }
            else
            {
                DragModify("Erase", () => Chart.lasers = Chart.lasers.Where(item => item != laser).ToArray());
                if (IsLaserSelected(laser))
                {
                    ClearSelection();
                }
            }

            return;
        }

        TapNoteData note = HitNote3D(Ray3D(mouse), out _);
        if (note != null)
        {
            DragModify("Erase", () => Chart.notes = Chart.notes.Where(item => item != note).ToArray());
            selectedNotes.Remove(note);
            return;
        }

        LaserNoteData line = HitLaserLine3D(mouse);
        if (line != null)
        {
            DragModify("Erase", () => Chart.lasers = Chart.lasers.Where(item => item != line).ToArray());
            if (IsLaserSelected(line))
            {
                ClearSelection();
            }
        }
    }

    /// <summary>The 2D ⇄ 3D switch of the menu bar: two halves, the active one lit.</summary>
    private bool EditViewToggle(Rect rect, Event current)
    {
        bool hovered = rect.Contains(current.mousePosition);
        if (current.type == EventType.Repaint)
        {
            Rect left = new Rect(rect.x, rect.y, rect.width * 0.5f, rect.height);
            Rect right = new Rect(left.xMax, rect.y, rect.width * 0.5f, rect.height);
            Color on = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.75f);
            Color off = hovered ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.04f);
            EditorGUI.DrawRect(left, editView3D ? off : on);
            EditorGUI.DrawRect(right, editView3D ? on : off);
            DrawOutline(rect, new Color(1f, 1f, 1f, 0.18f), 1f);
            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = editView3D ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
            GUI.Label(left, "2D 編集", style);
            style.normal.textColor = editView3D ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(right, "3D 編集", style);
            if (hovered)
            {
                tooltipText = "ノーツを置く画面を 2D タイムライン / 3D の箱 で切り替える (Tab)";
            }
        }

        if (current.type == EventType.MouseDown && current.button == 0 && hovered && !pointerBlocked)
        {
            current.Use();
            return true;
        }

        return false;
    }
}
