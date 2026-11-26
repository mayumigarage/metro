using System;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Every tunable number and colour of the stage. Edited on the RhythmStageView
/// component in the scene; the static geometry functions read the active copy.
/// </summary>
[Serializable]
public sealed class StageLayout
{
    [Header("Camera")]
    [Tooltip("16:9 で使う縦 FOV。これより細い画面では外側のレーン端が収まるよう自動で広げる。")]
    public float designFieldOfView = 52f;
    [Tooltip("カメラは Z 軸上に置かれ、トンネルの奥をまっすぐ見る。")]
    public float cameraZ = -11.5f;
    public float cameraLookAtZ = 4.1f;
    public Color backgroundColor = new Color(0.008f, 0.012f, 0.035f, 1f);

    [Header("Depth (notes travel from Back to Front)")]
    public float frontZ = 0.45f;
    public float backZ = 10f;

    [Header("Lanes")]
    [Tooltip("判定ライン位置でのレーン面の高さ（上段は +、下段は -）。")]
    public float frontLaneY = 4.78f;
    [Tooltip("奥でのレーン面の高さ。奥の外側 X は frontOuterX をこの比率で縮めたものになり、全ての辺が画面中心に収束する。")]
    public float backLaneY = 1.9f;
    [Tooltip("判定ライン位置でのレーン外側の端（中心線 x = 0 から）。")]
    public float frontOuterX = 7.6f;
    [Tooltip("false: ZL は L の下、ZR は R の下（コントローラと同じ）。true で下段を左右入れ替える。")]
    public bool lowerRowCrossed = false;

    [Header("Laser space")]
    [Tooltip("スティックを倒し切ったときの判定面上の位置（横）。")]
    public float laserFrontHalfWidth = 4.8f;
    [Tooltip("スティックを倒し切ったときの判定面上の位置（縦）。")]
    public float laserFrontHalfHeight = 3.0f;
    public float laserCursorHalfSize = 0.42f;
    [Min(3)] public int laserGuideSegments = 48;

    [Header("Colours")]
    public Color leftLaneColor = new Color(0.08f, 0.25f, 0.40f, 1f);
    public Color rightLaneColor = new Color(0.10f, 0.34f, 0.54f, 1f);
    [Tooltip("左スティックで操作するもの全て（レーザー、方向ノーツ、カーソル）の色。")]
    public Color leftStickColor = new Color(0.28f, 0.92f, 1f, 1f);
    [Tooltip("右スティックで操作するもの全て（レーザー、方向ノーツ、カーソル）の色。")]
    public Color rightStickColor = new Color(1f, 0.32f, 0.82f, 1f);
    public Color boundaryColor = new Color(0.42f, 0.76f, 0.92f, 0.9f);
    public Color judgementLineColor = new Color(0.98f, 0.88f, 0.32f, 1f);
    public Color judgementMarkerColor = new Color(1f, 0.9f, 0.35f, 1f);
    public Color backWallColor = new Color(0.02f, 0.035f, 0.10f, 1f);
    public Color backFrameColor = new Color(0.20f, 0.36f, 0.50f, 1f);
    public Color laserGuideColor = new Color(0.20f, 0.26f, 0.42f, 1f);
    public Color laserRailColor = new Color(0.10f, 0.14f, 0.26f, 1f);

    /// <summary>The back is the front scaled by backLaneY / frontLaneY, so every edge converges on the centre.</summary>
    public float BackOuterX => frontLaneY > 0f ? frontOuterX * backLaneY / frontLaneY : 0f;
}

/// <summary>
/// Builds the stage: the far end (奥) sits in the screen centre and everything
/// travels outward to the near end (前). The L / R lanes form the ceiling and the
/// ZL / ZR lanes the floor of a tunnel, with no gap between the two lanes of a
/// row. The open space between ceiling and floor is where the stick lasers fly.
///
/// The geometry is generated from <see cref="StageLayout"/>, also in edit mode,
/// so the stage is visible in the Scene view without pressing Play. The
/// generated objects are not saved with the scene (they are rebuilt from the
/// layout); edit the values on this component instead of the objects.
/// </summary>
[ExecuteAlways]
public sealed class RhythmStageView : MonoBehaviour
{
    public const int LaneCount = 4;

    private const float DesignAspect = 16f / 9f;
    private const string StageRootName = "RhythmStage_3D";

    private static readonly string[] LaneLabels = { "L", "R", "ZL", "ZR" };

    // The layout every static geometry function reads. Defaults until a stage enables.
    private static StageLayout active = new StageLayout();

    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private StageLayout layout = new StageLayout();

    private Transform stageRoot;
    private bool rebuildRequested;
    private readonly Transform[] laserCursors = new Transform[2];

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDisable()
    {
        DestroyStage();
    }

    private void OnValidate()
    {
        if (layout == null)
        {
            layout = new StageLayout();
        }

        // Objects cannot be created or destroyed inside OnValidate; Update rebuilds instead.
        rebuildRequested = true;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif
    }

    private void Update()
    {
        if (rebuildRequested)
        {
            Rebuild();
        }

        if (!Application.isPlaying)
        {
            return;
        }

        for (int side = 0; side < laserCursors.Length; side++)
        {
            if (laserCursors[side] != null)
            {
                laserCursors[side].position = GetLaserWorldPosition(StickInput.Read(side), 1f) + new Vector3(0f, 0f, -0.1f);
            }
        }
    }

    private void Rebuild()
    {
        // Unity may swap in a freshly deserialized layout instance, so re-point every time.
        active = layout;
        rebuildRequested = false;
        DestroyStage();
        BuildStage();
        ConfigureCamera();
    }

    public static bool IsUpperLane(int lane)
    {
        return Mathf.Clamp(lane, 0, LaneCount - 1) < 2;
    }

    public static string GetLaneLabel(int lane)
    {
        return LaneLabels[Mathf.Clamp(lane, 0, LaneCount - 1)];
    }

    /// <summary>Which stick belongs to a lane: 0 = left (L / ZL), 1 = right (R / ZR).</summary>
    public static int GetStickSide(int lane)
    {
        return Mathf.Clamp(lane, 0, LaneCount - 1) % 2;
    }

    public static Color GetStickColor(int side)
    {
        return side <= 0 ? active.leftStickColor : active.rightStickColor;
    }

    public static float GetLaneWidth(float progress)
    {
        return Mathf.Lerp(active.BackOuterX, active.frontOuterX, Mathf.Clamp01(progress));
    }

    public static Vector3 GetLaneWorldPosition(int lane, float progress)
    {
        float t = Mathf.Clamp01(progress);
        return (GetLaneEdge(lane, t, false) + GetLaneEdge(lane, t, true)) * 0.5f;
    }

    /// <summary>Rotation that lays a note flat on its lane's slope.</summary>
    public static Quaternion GetLaneRotation(int lane)
    {
        Vector3 slope = new Vector3(0f, (active.frontLaneY - active.backLaneY) * GetLaneRowSign(lane), active.frontZ - active.backZ);
        return Quaternion.LookRotation(slope.normalized, Vector3.up);
    }

    /// <summary>Unit vector from a lane's surface into the open space between the rows.</summary>
    public static Vector3 GetLaneInward(int lane)
    {
        return new Vector3(0f, -GetLaneRowSign(lane), 0f);
    }

    /// <summary>
    /// How much smaller things are drawn at <paramref name="progress"/> than at the
    /// near end (1), so they shrink with the tunnel as it recedes.
    /// </summary>
    public static float GetDepthScale(float progress)
    {
        return Mathf.Lerp(active.frontLaneY > 0f ? active.backLaneY / active.frontLaneY : 1f, 1f, Mathf.Clamp01(progress));
    }

    /// <summary>
    /// World position of a laser. <paramref name="value"/> is in stick units
    /// (-1..1 on each axis), so at progress 1 it is exactly where the stick cursor sits.
    /// </summary>
    public static Vector3 GetLaserWorldPosition(Vector2 value, float progress)
    {
        float t = Mathf.Clamp01(progress);
        float scale = GetDepthScale(t);
        return new Vector3(
            value.x * active.laserFrontHalfWidth * scale,
            value.y * active.laserFrontHalfHeight * scale,
            Mathf.Lerp(active.backZ, active.frontZ, t));
    }

    /// <summary>
    /// Where a world point falls on the camera's view plane (perspective divide only).
    /// Used to find which way a path runs on screen.
    /// </summary>
    public static Vector2 ProjectToView(Vector3 world)
    {
        return new Vector2(world.x, world.y) / (world.z - active.cameraZ);
    }

    private static float GetLaneRowSign(int lane)
    {
        return IsUpperLane(lane) ? 1f : -1f;
    }

    private static float GetLaneColumnSign(int lane)
    {
        bool rightButton = GetStickSide(lane) == 1;
        bool onRight = !IsUpperLane(lane) && active.lowerRowCrossed ? !rightButton : rightButton;
        return onRight ? 1f : -1f;
    }

    private static Vector3 GetLaneEdge(int lane, float t, bool outer)
    {
        float x = outer ? Mathf.Lerp(active.BackOuterX, active.frontOuterX, t) : 0f;
        return new Vector3(
            x * GetLaneColumnSign(lane),
            Mathf.Lerp(active.backLaneY, active.frontLaneY, t) * GetLaneRowSign(lane),
            Mathf.Lerp(active.backZ, active.frontZ, t));
    }

    private void BuildStage()
    {
        stageRoot = new GameObject(StageRootName).transform;
        stageRoot.SetParent(transform, false);

        for (int lane = 0; lane < LaneCount; lane++)
        {
            CreateLane(stageRoot, lane);
        }

        CreateBackPanel(stageRoot);
        CreateLaserGuide(stageRoot);
        laserCursors[0] = CreateLaserCursor(stageRoot, 0, "LaserCursor_Left");
        laserCursors[1] = CreateLaserCursor(stageRoot, 1, "LaserCursor_Right");

        if (!Application.isPlaying)
        {
            // Edit-mode preview: shown in the hierarchy but never written to the scene,
            // and locked so it is clear the values on this component are what to edit.
            const HideFlags previewFlags = HideFlags.DontSave | HideFlags.NotEditable;
            foreach (Transform child in stageRoot.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.hideFlags = previewFlags;
            }

            foreach (Object asset in CollectGeneratedAssets(stageRoot))
            {
                asset.hideFlags = previewFlags;
            }
        }
    }

    /// <summary>
    /// Removes the generated stage together with the meshes and materials it created,
    /// including any copy left over from before a script reload.
    /// </summary>
    private void DestroyStage()
    {
        for (int index = transform.childCount - 1; index >= 0; index--)
        {
            Transform child = transform.GetChild(index);
            if (child.name != StageRootName)
            {
                continue;
            }

            foreach (Object asset in CollectGeneratedAssets(child))
            {
                DestroyGenerated(asset);
            }

            DestroyGenerated(child.gameObject);
        }

        stageRoot = null;
        laserCursors[0] = null;
        laserCursors[1] = null;
    }

    private static System.Collections.Generic.List<Object> CollectGeneratedAssets(Transform root)
    {
        var assets = new System.Collections.Generic.List<Object>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.sharedMaterial != null)
            {
                assets.Add(renderer.sharedMaterial);
            }
        }

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            // Primitives (cubes) share Unity's built-in mesh, which must not be destroyed.
            if (filter.sharedMesh != null && filter.GetComponent<BoxCollider>() == null)
            {
                assets.Add(filter.sharedMesh);
            }
        }

        return assets;
    }

    private static void DestroyGenerated(Object target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void ConfigureCamera()
    {
        gameplayCamera = gameplayCamera != null ? gameplayCamera : Camera.main;
        if (gameplayCamera == null)
        {
            return;
        }

        // The layout is designed for 16:9. On narrower views, widen the FOV so the
        // outer lane edges stay on screen.
        float fieldOfView = layout.designFieldOfView;
        if (gameplayCamera.aspect < DesignAspect)
        {
            float halfTan = Mathf.Tan(layout.designFieldOfView * 0.5f * Mathf.Deg2Rad) * DesignAspect / gameplayCamera.aspect;
            fieldOfView = 2f * Mathf.Atan(halfTan) * Mathf.Rad2Deg;
        }

        gameplayCamera.orthographic = false;
        gameplayCamera.transform.position = new Vector3(0f, 0f, layout.cameraZ);
        gameplayCamera.transform.LookAt(new Vector3(0f, 0f, layout.cameraLookAtZ));
        gameplayCamera.fieldOfView = fieldOfView;
        gameplayCamera.nearClipPlane = 0.05f;
        gameplayCamera.farClipPlane = 35f;
        gameplayCamera.backgroundColor = layout.backgroundColor;
    }

    private static void CreateLane(Transform parent, int lane)
    {
        string label = GetLaneLabel(lane);
        Vector3 backInner = GetLaneEdge(lane, 0f, false);
        Vector3 backOuter = GetLaneEdge(lane, 0f, true);
        Vector3 frontInner = GetLaneEdge(lane, 1f, false);
        Vector3 frontOuter = GetLaneEdge(lane, 1f, true);
        int column = GetLaneColumnSign(lane) < 0f ? 0 : 1;

        CreateMesh(
            parent,
            $"LaneSurface_{label}",
            new[] { frontInner, backInner, backOuter, frontOuter },
            new[] { 0, 1, 2, 0, 2, 3 },
            CreateMaterial(column == 0 ? active.leftLaneColor : active.rightLaneColor));

        // Lines sit slightly off the surface (toward the screen centre) so they never z-fight with it.
        Vector3 lift = new Vector3(0f, -0.02f * GetLaneRowSign(lane), 0f);
        Vector3 nearer = new Vector3(0f, 0f, -0.08f);
        if (column == 0)
        {
            // The two lanes of a row share their inner edge, so only one of them draws the divider.
            CreateLine(parent, $"LaneDivider_{(IsUpperLane(lane) ? "Upper" : "Lower")}", frontInner + lift, backInner + lift, 0.05f, active.boundaryColor);
        }

        CreateLine(parent, $"LaneBoundary_{label}_Outer", frontOuter + lift, backOuter + lift, 0.06f, active.boundaryColor);
        CreateLine(parent, $"LaneBoundary_{label}_Back", backInner + lift, backOuter + lift, 0.035f, active.boundaryColor);
        CreateLine(parent, $"JudgementLine_{label}", frontInner + lift + nearer, frontOuter + lift + nearer, 0.12f, active.judgementLineColor);

        GameObject hitMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hitMarker.name = $"JudgementMarker_{label}";
        hitMarker.transform.SetParent(parent, false);
        hitMarker.transform.position = GetLaneWorldPosition(lane, 1f) + new Vector3(0f, 0f, -0.14f);
        hitMarker.transform.localScale = new Vector3(GetLaneWidth(1f) * 0.8f, 0.045f, 0.10f);
        hitMarker.GetComponent<Renderer>().sharedMaterial = CreateMaterial(active.judgementMarkerColor);
    }

    private static void CreateBackPanel(Transform parent)
    {
        GameObject backWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backWall.name = "StageBackWall";
        backWall.transform.SetParent(parent, false);
        float backZ = active.backZ;
        float backX = active.BackOuterX;
        float backY = active.backLaneY;
        backWall.transform.localPosition = new Vector3(0f, 0f, backZ + 0.25f);
        backWall.transform.localScale = new Vector3(backX * 2f, backY * 2f, 0.2f);
        backWall.GetComponent<Renderer>().sharedMaterial = CreateMaterial(active.backWallColor);

        CreateLoop(
            parent,
            "StageBackFrame",
            new[]
            {
                new Vector3(-backX, backY, backZ),
                new Vector3(backX, backY, backZ),
                new Vector3(backX, -backY, backZ),
                new Vector3(-backX, -backY, backZ)
            },
            0.03f,
            CreateMaterial(active.backFrameColor));
    }

    /// <summary>
    /// Faint guide for the laser space: the ring a full stick tilt reaches at the
    /// near end, its up / down / left / right axes, and four rails back to the far end.
    /// </summary>
    private static void CreateLaserGuide(Transform parent)
    {
        Vector3[] ring = new Vector3[Mathf.Max(3, active.laserGuideSegments)];
        for (int index = 0; index < ring.Length; index++)
        {
            float angle = index * Mathf.PI * 2f / ring.Length;
            ring[index] = GetLaserWorldPosition(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), 1f);
        }

        CreateLoop(parent, "LaserGuide_Ring", ring, 0.04f, CreateMaterial(active.laserGuideColor));
        CreateLine(parent, "LaserGuide_Horizontal", GetLaserWorldPosition(Vector2.left, 1f), GetLaserWorldPosition(Vector2.right, 1f), 0.03f, active.laserGuideColor);
        CreateLine(parent, "LaserGuide_Vertical", GetLaserWorldPosition(Vector2.down, 1f), GetLaserWorldPosition(Vector2.up, 1f), 0.03f, active.laserGuideColor);

        Vector2[] axes = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        foreach (Vector2 axis in axes)
        {
            CreateLine(parent, $"LaserGuide_Rail_{axis.x:0}_{axis.y:0}", GetLaserWorldPosition(axis, 0f), GetLaserWorldPosition(axis, 1f), 0.03f, active.laserRailColor);
        }
    }

    /// <summary>
    /// Stick cursor on the judgement plane. The left one is a diamond and the right
    /// one a square so both stay readable when they overlap.
    /// </summary>
    private static Transform CreateLaserCursor(Transform parent, int side, string name)
    {
        float s = active.laserCursorHalfSize;
        Vector3[] outline = side == 0
            ? new[] { new Vector3(0f, s, 0f), new Vector3(s, 0f, 0f), new Vector3(0f, -s, 0f), new Vector3(-s, 0f, 0f) }
            : new[] { new Vector3(-s, s, 0f) * 0.75f, new Vector3(s, s, 0f) * 0.75f, new Vector3(s, -s, 0f) * 0.75f, new Vector3(-s, -s, 0f) * 0.75f };

        LineRenderer line = CreateLoop(parent, name, outline, 0.09f, CreateMaterial(GetStickColor(side)));
        line.useWorldSpace = false;
        return line.transform;
    }

    private static void CreateMesh(Transform parent, string name, Vector3[] vertices, int[] triangles, Material material)
    {
        // Emit both windings so the surface is visible from either side.
        int[] doubleSided = new int[triangles.Length * 2];
        for (int index = 0; index < triangles.Length; index += 3)
        {
            doubleSided[index * 2] = triangles[index];
            doubleSided[index * 2 + 1] = triangles[index + 1];
            doubleSided[index * 2 + 2] = triangles[index + 2];
            doubleSided[index * 2 + 3] = triangles[index];
            doubleSided[index * 2 + 4] = triangles[index + 2];
            doubleSided[index * 2 + 5] = triangles[index + 1];
        }

        GameObject meshObject = new GameObject(name);
        meshObject.transform.SetParent(parent, false);

        Mesh mesh = new Mesh { name = name };
        mesh.vertices = vertices;
        mesh.triangles = doubleSided;
        mesh.RecalculateBounds();

        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void CreateLine(Transform parent, string name, Vector3 start, Vector3 end, float width, Color color)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(parent, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 4;
        line.sharedMaterial = CreateMaterial(color);
    }

    private static LineRenderer CreateLoop(Transform parent, string name, Vector3[] points, float width, Material material)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(parent, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.startWidth = width;
        line.endWidth = width;
        line.numCornerVertices = 2;
        line.sharedMaterial = material;
        return line;
    }

    public static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    /// <summary>Opaque unlit material. Every runtime material in the game comes from here.</summary>
    public static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material material = new Material(shader);
        SetMaterialColor(material, color);
        return material;
    }

    /// <summary>
    /// Alpha-blended unlit material (the colour's alpha is its opacity). Falls back
    /// to the opaque material if the blending shader is unavailable.
    /// </summary>
    public static Material CreateTransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            return CreateMaterial(color);
        }

        Material material = new Material(shader);
        SetMaterialColor(material, color);
        return material;
    }
}
