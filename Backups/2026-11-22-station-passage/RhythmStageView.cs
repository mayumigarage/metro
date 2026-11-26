using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
    [Tooltip("画面の上からこの割合を HUD の上部バーに空け、ステージ全体をその下の領域に収める（0 = 画面全体）。")]
    [Range(0f, 0.4f)] public float topInset = 0.15f;
    [Tooltip("背景とフォグの色。通路の奥はこの色の暗がりに消える。")]
    public Color backgroundColor = new Color(0.012f, 0.013f, 0.016f, 1f);

    [Header("Depth (notes travel from Back to Front)")]
    public float frontZ = 0.45f;
    public float backZ = 34f;

    [Header("Lanes")]
    [Tooltip("判定ライン位置でのレーン面の高さ（上段は +、下段は -）。")]
    public float frontLaneY = 4.78f;
    [Tooltip("奥でのレーン面の高さ。奥の外側 X は frontOuterX をこの比率で縮めたものになり、全ての辺が画面中心に収束する。")]
    public float backLaneY = 0.65f;
    [Tooltip("判定ライン位置でのレーン外側の端（中心線 x = 0 から）。")]
    public float frontOuterX = 7.6f;
    [Tooltip("false: ZL は L の下、ZR は R の下（コントローラと同じ）。true で下段を左右入れ替える。")]
    public bool lowerRowCrossed = false;
    [Tooltip("ノーツと一緒に流れるトンネルの枠（グリッド）の線の太さ（判定ライン位置）。奥ほど細くなる。間隔はオプションの GRID。")]
    public float gridLineWidth = 0.05f;

    [Header("Laser space")]
    [Tooltip("スティックを倒し切ったときの判定面上の位置（横）。")]
    public float laserFrontHalfWidth = 4.8f;
    [Tooltip("スティックを倒し切ったときの判定面上の位置（縦）。")]
    public float laserFrontHalfHeight = 3.0f;

    [Header("Corridor (the station passage around the lanes)")]
    [Tooltip("判定ラインより手前（カメラ側）に通路を延ばす量（progress 単位）。画面の端まで通路で埋まる。")]
    public float nearExtension = 0.18f;
    [Tooltip("床・天井・壁のタイルの奥行き方向の大きさ（判定ライン位置の縮尺）。")]
    public float floorTileSize = 1.9f;
    [Tooltip("壁のモザイクタイル 1 枚の長さ（判定ライン位置の縮尺）。")]
    public float wallTileLength = 0.3f;
    [Tooltip("天井際の蛍光灯 1 本の長さ。")]
    public float lightTubeLength = 4.5f;
    [Tooltip("壁の広告（電飾看板）の間隔と幅。間隔 0 で広告なし。")]
    public float posterSpacing = 17f;
    public float posterWidth = 5.6f;
    [Tooltip("フォグ（通路の奥が暗がりに消える）の開始・終了距離。")]
    public float fogStart = 16f;
    public float fogEnd = 82f;

    [Header("Colours")]
    [Tooltip("天井（L / R）と床（ZL / ZR）の素材の色。タイルのテクスチャに掛かる。")]
    public Color leftLaneColor = new Color(0.46f, 0.47f, 0.50f, 1f);
    public Color rightLaneColor = new Color(0.46f, 0.47f, 0.50f, 1f);
    [Tooltip("壁のタイルの色。")]
    public Color wallColor = new Color(0.40f, 0.41f, 0.44f, 1f);
    [Tooltip("左スティックで操作するもの全て（レーザー、方向ノーツ、カーソル）の色。")]
    public Color leftStickColor = new Color(0f, 0.64f, 0.93f, 1f);
    [Tooltip("右スティックで操作するもの全て（レーザー、方向ノーツ、カーソル）の色。")]
    public Color rightStickColor = new Color(0.91f, 0.18f, 0.60f, 1f);
    [Tooltip("通常ノーツ（方向ノーツの下のバーも含む）の色。")]
    public Color tapNoteColor = new Color(0.72f, 0.60f, 0.96f, 1f);
    [Tooltip("ロングノーツの色。")]
    public Color holdNoteColor = new Color(0.52f, 0.36f, 0.86f, 1f);
    [Tooltip("L / ZL のボタンを押している間の光の柱の色。")]
    public Color leftButtonColor = new Color(1f, 0.52f, 0.28f, 1f);
    [Tooltip("R / ZR のボタンを押している間の光の柱の色。")]
    public Color rightButtonColor = new Color(0.20f, 0.70f, 1f, 1f);
    [Tooltip("レーンの仕切りと通路の角の金属の線。")]
    public Color boundaryColor = new Color(0.30f, 0.31f, 0.34f, 1f);
    [Tooltip("ノーツと一緒に流れるトンネルの枠（グリッド）の色。手前ほど濃く、奥で消える。")]
    public Color gridColor = new Color(0.78f, 0.82f, 0.90f, 0.14f);
    [Tooltip("判定ライン（黄色い点字ブロック）の色。")]
    public Color judgementLineColor = new Color(1f, 0.80f, 0f, 1f);
    [Tooltip("天井際の蛍光灯の色（HDR の明るさは lightIntensity）。")]
    public Color lightColor = new Color(1f, 0.97f, 0.92f, 1f);
    public Color backWallColor = new Color(0.02f, 0.021f, 0.025f, 1f);
    public Color backFrameColor = new Color(0.16f, 0.17f, 0.19f, 1f);
    public Color laserRailColor = new Color(0.20f, 0.21f, 0.24f, 1f);

    [Header("Glow (HDR brightness; anything above the bloom threshold glows)")]
    [Tooltip("ブルームがかかり始める明るさ（ガンマ空間）。HUD の白い文字（1.0）が少し光るよう 1 より下に置く。")]
    public float bloomThreshold = 0.9f;
    [Tooltip("オプションの BLOOM 100% でのブルーム強度。")]
    public float bloomIntensity = 0.9f;
    [Range(0f, 1f)] public float bloomScatter = 0.7f;
    [Tooltip("ノーツの明るさ倍率。1 より上でブルームがかかる。")]
    public float noteGlow = 1.2f;
    [Tooltip("点字ブロックの明るさ倍率。")]
    public float judgementLineGlow = 1.1f;
    [Tooltip("蛍光灯の HDR の明るさ。")]
    public float lightIntensity = 2.6f;
    [Tooltip("壁の広告の明るさ倍率（電飾看板）。")]
    public float posterGlow = 1.15f;
    [Tooltip("レーンのボタンを押している間の光の柱の濃さ。")]
    [Range(0f, 1f)] public float laneBeamOpacity = 0.34f;

    [Header("Post-processing (live-action camera look)")]
    [Range(0f, 1f)] public float vignette = 0.32f;
    [Range(0f, 1f)] public float filmGrain = 0.22f;
    [Range(0f, 1f)] public float chromaticAberration = 0.035f;
    [Range(-100f, 100f)] public float contrast = 8f;

    /// <summary>The back is the front scaled by backLaneY / frontLaneY, so every edge converges on the centre.</summary>
    public float BackOuterX => frontLaneY > 0f ? frontOuterX * backLaneY / frontLaneY : 0f;
}

/// <summary>
/// Builds the stage: the far end (奥) sits in the screen centre and everything
/// travels outward to the near end (前). The L / R lanes form the ceiling and the
/// ZL / ZR lanes the floor of a tunnel, with no gap between the two lanes of a
/// row. The open space between ceiling and floor is where the stick lasers fly.
///
/// The tunnel is dressed as a dark station passage: tiled floor, aluminium ceiling
/// strips, mosaic-tiled side walls with a line-colour band and lit poster boxes,
/// fluorescent tubes where the ceiling meets the walls, and a yellow tactile-paving
/// strip as the judgement line. Every texture is generated here (no assets). The
/// tunnel narrows toward the far end, so textures are laid out along the depth of
/// the straight corridor it looks like (<see cref="CorridorDepth"/>), not along z.
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
    private const int SurfaceRows = 160;
    private const int PosterVariants = 6;
    private const float TactileTilesAcross = 6f;

    private static readonly string[] LaneLabels = { "L", "R", "ZL", "ZR" };

    // The layout every static geometry function reads. Defaults until a stage enables.
    private static StageLayout active = new StageLayout();

    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private StageLayout layout = new StageLayout();

    private Transform stageRoot;
    private bool rebuildRequested;
    private readonly MeshRenderer[] laneBeams = new MeshRenderer[LaneCount];
    private readonly List<Material> lineBandMaterials = new List<Material>();
    private Bloom bloom;
    private int builtBrightnessPercent;
    private float projectedAspect;
    private Color lineColor = new Color(0.55f, 0.57f, 0.60f, 1f);

    private delegate Vector3 Edge(float progress);

    private void OnEnable()
    {
        GameSettings.Changed += OnSettingsChanged;
        Rebuild();
    }

    private void OnDisable()
    {
        GameSettings.Changed -= OnSettingsChanged;
        DestroyStage();
        if (gameplayCamera != null)
        {
            gameplayCamera.ResetProjectionMatrix();
            projectedAspect = 0f;
        }
    }

    private void OnSettingsChanged()
    {
        ApplyBloom();

        // The brightness is baked into the stage materials, so it needs a rebuild.
        if (builtBrightnessPercent != GameSettings.StageBrightnessPercent)
        {
            rebuildRequested = true;
        }
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

        // The projection is set by hand, so Unity no longer follows a change of the view's aspect.
        if (gameplayCamera != null && !Mathf.Approximately(gameplayCamera.aspect, projectedAspect))
        {
            ApplyProjection();
        }

        if (!Application.isPlaying)
        {
            return;
        }

        for (int lane = 0; lane < laneBeams.Length; lane++)
        {
            if (laneBeams[lane] != null)
            {
                laneBeams[lane].enabled = JudgeSystem.IsLaneHeld(lane);
            }
        }
    }

    /// <summary>The song's line (difficulty) colour, painted as the band along the walls.</summary>
    public void SetLineColor(Color color)
    {
        lineColor = color;
        foreach (Material material in lineBandMaterials)
        {
            if (material != null)
            {
                SetMaterialColor(material, Dim(color * 0.8f));
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

    /// <summary>Colour of a lane's button-held beam: follows the button side (L / ZL, R / ZR).</summary>
    public static Color GetButtonColor(int side)
    {
        return side <= 0 ? active.leftButtonColor : active.rightButtonColor;
    }

    /// <summary>Colour of a note's bar: lilac for a plain note, deeper violet for a hold.</summary>
    public static Color GetNoteColor(bool hold)
    {
        return hold ? active.holdNoteColor : active.tapNoteColor;
    }

    /// <summary>HDR brightness of the notes.</summary>
    public static float NoteGlow => active.noteGlow;

    /// <summary>
    /// A colour as a mesh vertex colour. Material colours are converted to the
    /// project's linear colour space by Unity; vertex colours are not, and would
    /// come out pale unless converted here.
    /// </summary>
    public static Color ToVertexColor(Color color)
    {
        return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
    }

    /// <summary>Scales a colour's RGB past 1 so it blooms; alpha is kept.</summary>
    public static Color Boost(Color color, float intensity)
    {
        return new Color(color.r * intensity, color.g * intensity, color.b * intensity, color.a);
    }

    /// <summary>Colour and near-end width of the scrolling tunnel frames (TunnelGridView).</summary>
    public static Color GridColor => active.gridColor;
    public static float GridLineWidth => active.gridLineWidth;

    /// <summary>
    /// The four corners of the tunnel's cross-section at <paramref name="progress"/>:
    /// the outer edges of the ceiling and floor lanes (top-left, top-right,
    /// bottom-right, bottom-left), pulled in by <paramref name="inset"/> so lines drawn
    /// along them sit just off the lane surfaces.
    /// </summary>
    public static void GetTunnelCorners(float progress, float inset, Vector3[] corners)
    {
        float t = Mathf.Clamp01(progress);
        float x = OuterX(t) - inset;
        float y = LaneY(t) - inset;
        float z = DepthZ(t);
        corners[0] = new Vector3(-x, y, z);
        corners[1] = new Vector3(x, y, z);
        corners[2] = new Vector3(x, -y, z);
        corners[3] = new Vector3(-x, -y, z);
    }

    public static float GetLaneWidth(float progress)
    {
        return OuterX(Mathf.Clamp01(progress));
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

    // Unclamped, so the corridor can run on past the judgement line toward the camera (progress > 1).
    private static float OuterX(float t) => active.BackOuterX + (active.frontOuterX - active.BackOuterX) * t;
    private static float LaneY(float t) => active.backLaneY + (active.frontLaneY - active.backLaneY) * t;
    private static float DepthZ(float t) => active.backZ + (active.frontZ - active.backZ) * t;

    private static Vector3 GetLaneEdge(int lane, float t, bool outer)
    {
        float x = outer ? OuterX(t) : 0f;
        return new Vector3(x * GetLaneColumnSign(lane), LaneY(t) * GetLaneRowSign(lane), DepthZ(t));
    }

    /// <summary>A point on a side wall (<paramref name="side"/> -1 left, 1 right) at height <paramref name="height"/> (-1 floor .. 1 ceiling).</summary>
    private static Vector3 GetWallPoint(float side, float t, float height, float inset)
    {
        return new Vector3(side * (OuterX(t) - inset), height * LaneY(t), DepthZ(t));
    }

    /// <summary>
    /// The tunnel narrows toward the far end, which on screen looks exactly like a straight
    /// corridor of the near end's cross-section seen in perspective. This is the depth of a
    /// progress along that corridor (from the camera); textures are laid out along it, so
    /// tiles keep their size and the passage reads as a real, straight one.
    /// </summary>
    private static float CorridorDepth(float t)
    {
        return (DepthZ(t) - active.cameraZ) * active.frontOuterX / Mathf.Max(0.001f, OuterX(t));
    }

    /// <summary>The progress at a corridor depth (inverse of <see cref="CorridorDepth"/>).</summary>
    private static float ProgressAtCorridorDepth(float depth)
    {
        float k = active.frontOuterX;
        float a = active.backZ - active.cameraZ;
        float b = active.frontZ - active.backZ;
        float c = active.BackOuterX;
        float d = active.frontOuterX - active.BackOuterX;
        float denominator = depth * d - k * b;
        return Mathf.Abs(denominator) < 1e-5f ? 1f : (k * a - depth * c) / denominator;
    }

    private void BuildStage()
    {
        stageRoot = new GameObject(StageRootName).transform;
        stageRoot.SetParent(transform, false);
        builtBrightnessPercent = GameSettings.StageBrightnessPercent;
        lineBandMaterials.Clear();

        var textures = new StageTextures();
        for (int lane = 0; lane < LaneCount; lane++)
        {
            CreateLane(stageRoot, lane, textures);
            laneBeams[lane] = CreateLaneBeam(stageRoot, lane);
        }

        CreateWall(stageRoot, -1f, textures);
        CreateWall(stageRoot, 1f, textures);
        CreatePosters(stageRoot, textures);
        CreateBackPanel(stageRoot);
        CreatePostFx(stageRoot);
        CreateLaserRails(stageRoot);

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
    /// Removes the generated stage together with the meshes, materials and textures it
    /// created, including any copy left over from before a script reload.
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
        bloom = null;
        lineBandMaterials.Clear();
        Array.Clear(laneBeams, 0, laneBeams.Length);
    }

    private static HashSet<Object> CollectGeneratedAssets(Transform root)
    {
        var assets = new HashSet<Object>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material material = renderer.sharedMaterial;
            if (material == null)
            {
                continue;
            }

            assets.Add(material);
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
            {
                assets.Add(material.GetTexture("_BaseMap"));
            }

            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
            {
                assets.Add(material.GetTexture("_MainTex"));
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

        foreach (Volume volume in root.GetComponentsInChildren<Volume>(true))
        {
            if (volume.sharedProfile != null)
            {
                foreach (VolumeComponent component in volume.sharedProfile.components)
                {
                    assets.Add(component);
                }

                assets.Add(volume.sharedProfile);
            }
        }

        return assets;
    }

    private static void DestroyGenerated(Object target)
    {
        if (target == null)
        {
            return;
        }

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

        // The far end of the passage fades into the dark. Unlit materials take fog too.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = layout.backgroundColor;
        RenderSettings.fogStartDistance = layout.fogStart;
        RenderSettings.fogEndDistance = Mathf.Max(layout.fogStart + 1f, layout.fogEnd);

        if (gameplayCamera == null)
        {
            return;
        }

        gameplayCamera.orthographic = false;
        gameplayCamera.transform.position = new Vector3(0f, 0f, layout.cameraZ);
        gameplayCamera.transform.LookAt(new Vector3(0f, 0f, layout.cameraLookAtZ));
        gameplayCamera.nearClipPlane = 0.05f;
        gameplayCamera.farClipPlane = active.backZ - active.cameraZ + 10f;
        gameplayCamera.backgroundColor = layout.backgroundColor;
        ApplyProjection();

        // Bloom needs HDR and the camera's post-processing; SMAA smooths the thin lines.
        gameplayCamera.allowHDR = true;
        UniversalAdditionalCameraData cameraData = gameplayCamera.GetUniversalAdditionalCameraData();
        if (cameraData != null)
        {
            // When the HUD's overlay camera is stacked on this one it post-processes the whole
            // stack (stage and sign); doing it here as well would bloom the stage twice.
            bool stackPostProcesses = false;
            if (cameraData.renderType == CameraRenderType.Base && cameraData.cameraStack != null)
            {
                foreach (Camera overlay in cameraData.cameraStack)
                {
                    UniversalAdditionalCameraData overlayData = overlay != null ? overlay.GetUniversalAdditionalCameraData() : null;
                    stackPostProcesses |= overlayData != null && overlayData.renderPostProcessing;
                }
            }

            cameraData.renderPostProcessing = !stackPostProcesses;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }
    }

    /// <summary>
    /// Fits the stage into the part of the screen below <see cref="StageLayout.topInset"/>
    /// (the HUD's top bar sits above it): an off-centre projection whose centre is that
    /// region's centre, with the design FOV spanning the region's height. Everything,
    /// the far end included, moves down and shrinks together, as if the region were the
    /// whole view. On regions narrower than 16:9 the FOV widens so the outer lane edges
    /// stay on screen.
    /// </summary>
    private void ApplyProjection()
    {
        if (gameplayCamera == null)
        {
            return;
        }

        float aspect = Mathf.Max(0.01f, gameplayCamera.aspect);
        float stageShare = 1f - Mathf.Clamp(layout.topInset, 0f, 0.4f);
        float stageAspect = aspect / stageShare;
        float halfTan = Mathf.Tan(layout.designFieldOfView * 0.5f * Mathf.Deg2Rad);
        if (stageAspect < DesignAspect)
        {
            halfTan *= DesignAspect / stageAspect;
        }

        // The full view is taller than the stage region by 1 / stageShare.
        float fieldOfView = 2f * Mathf.Atan(halfTan / stageShare) * Mathf.Rad2Deg;
        gameplayCamera.ResetProjectionMatrix();
        gameplayCamera.fieldOfView = fieldOfView;

        // m12 moves the image down by that much of the half-height (NDC): the region's
        // centre is (1 - stageShare) below the screen centre.
        Matrix4x4 projection = Matrix4x4.Perspective(fieldOfView, aspect, gameplayCamera.nearClipPlane, gameplayCamera.farClipPlane);
        projection.m12 = 1f - stageShare;
        gameplayCamera.projectionMatrix = projection;
        projectedAspect = gameplayCamera.aspect;
    }

    /// <summary>
    /// Global volume for the stage, a live-action camera look: bloom from a threshold
    /// just under white (the lights, notes, tactile paving and the HUD sign's white
    /// lettering glow), a vignette, film grain and a touch of chromatic aberration. No
    /// tonemapping, so the sign's white stays white.
    /// </summary>
    private void CreatePostFx(Transform parent)
    {
        GameObject volumeObject = new GameObject("StagePostFx");
        volumeObject.transform.SetParent(parent, false);

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "StagePostFx";
        bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = active.bloomThreshold;
        bloom.scatter.value = active.bloomScatter;
        bloom.highQualityFiltering.value = true;

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.value = TonemappingMode.None;

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.value = active.vignette;
        vignette.smoothness.value = 0.5f;
        vignette.color.value = Color.black;

        FilmGrain grain = profile.Add<FilmGrain>(true);
        grain.type.value = FilmGrainLookup.Thin1;
        grain.intensity.value = active.filmGrain;
        grain.response.value = 0.85f;
        grain.active = active.filmGrain > 0f;

        ChromaticAberration aberration = profile.Add<ChromaticAberration>(true);
        aberration.intensity.value = active.chromaticAberration;
        aberration.active = active.chromaticAberration > 0f;

        ColorAdjustments adjustments = profile.Add<ColorAdjustments>(true);
        adjustments.contrast.value = active.contrast;

        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;
        ApplyBloom();
    }

    private void ApplyBloom()
    {
        if (bloom == null)
        {
            return;
        }

        float intensity = layout.bloomIntensity * GameSettings.BloomPercent / 100f;
        bloom.intensity.value = intensity;
        bloom.active = intensity > 0f;
    }

    /// <summary>Lane / wall colour at the STAGE BRIGHTNESS option.</summary>
    private static Color Dim(Color color)
    {
        float brightness = GameSettings.StageBrightnessPercent / 100f;
        return new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
    }

    /// <summary>
    /// A glow rising from the judgement line up the lane, shown while the lane's
    /// button is down. Fades out toward the far end.
    /// </summary>
    private static MeshRenderer CreateLaneBeam(Transform parent, int lane)
    {
        const float farProgress = 0.35f;
        Vector3 lift = GetLaneInward(lane) * 0.025f;
        Color near = Boost(GetButtonColor(GetStickSide(lane)), 1.2f);
        near.a = active.laneBeamOpacity;
        Color far = near;
        far.a = 0f;

        GameObject beamObject = new GameObject($"LaneBeam_{GetLaneLabel(lane)}");
        beamObject.transform.SetParent(parent, false);

        Mesh mesh = new Mesh { name = beamObject.name };
        mesh.vertices = new[]
        {
            GetLaneEdge(lane, 1f, false) + lift,
            GetLaneEdge(lane, 1f, true) + lift,
            GetLaneEdge(lane, farProgress, false) + lift,
            GetLaneEdge(lane, farProgress, true) + lift
        };
        mesh.colors = new[] { ToVertexColor(near), ToVertexColor(near), ToVertexColor(far), ToVertexColor(far) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };
        mesh.RecalculateBounds();

        beamObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = beamObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateTransparentMaterial(Color.white);
        renderer.enabled = false;
        return renderer;
    }

    /// <summary>
    /// One lane: the ceiling (aluminium strips) or floor (stone tiles) surface from the
    /// far end to past the judgement line, the metal divider and corner lines, and the
    /// yellow tactile paving (点字ブロック) laid across it as the judgement line.
    /// </summary>
    private static void CreateLane(Transform parent, int lane, StageTextures textures)
    {
        string label = GetLaneLabel(lane);
        bool upper = IsUpperLane(lane);
        int column = GetLaneColumnSign(lane) < 0f ? 0 : 1;
        float near = 1f + active.nearExtension;

        Material surface = CreateTexturedMaterial(
            Dim(column == 0 ? active.leftLaneColor : active.rightLaneColor),
            upper ? textures.Ceiling : textures.Floor,
            Vector2.one);
        CreateSurface(parent, $"LaneSurface_{label}",
            t => GetLaneEdge(lane, t, false),
            t => GetLaneEdge(lane, t, true),
            0f, near, SurfaceRows, upper ? active.floorTileSize * 2f : active.floorTileSize * 4f, 0f, surface);

        // Lines sit slightly off the surface (toward the screen centre) so they never z-fight with it.
        Vector3 lift = GetLaneInward(lane) * 0.02f;
        Vector3 frontInner = GetLaneEdge(lane, near, false);
        Vector3 frontOuter = GetLaneEdge(lane, near, true);
        Vector3 backInner = GetLaneEdge(lane, 0f, false);
        Vector3 backOuter = GetLaneEdge(lane, 0f, true);
        if (column == 0)
        {
            // The two lanes of a row share their inner edge, so only one of them draws the divider.
            CreateLine(parent, $"LaneDivider_{(upper ? "Upper" : "Lower")}", frontInner + lift, backInner + lift, 0.07f, Dim(active.boundaryColor));
        }

        CreateLine(parent, $"LaneBoundary_{label}_Outer", frontOuter + lift, backOuter + lift, 0.08f, Dim(active.boundaryColor));

        // The tactile paving: one row of square warning blocks centred on the judgement line.
        float tile = active.frontOuterX / TactileTilesAcross;
        float centre = CorridorDepth(1f);
        float tNear = ProgressAtCorridorDepth(centre - tile * 0.5f);
        float tFar = ProgressAtCorridorDepth(centre + tile * 0.5f);
        Vector3 pavingLift = GetLaneInward(lane) * 0.012f;
        Material paving = CreateTexturedMaterial(Boost(active.judgementLineColor, active.judgementLineGlow), textures.Tactile, Vector2.one);
        CreateSurface(parent, $"JudgementLine_{label}",
            t => GetLaneEdge(lane, t, false) + pavingLift,
            t => GetLaneEdge(lane, t, true) + pavingLift,
            tFar, tNear, 6, tile, centre - tile * 0.5f, paving, TactileTilesAcross);
    }

    /// <summary>
    /// A side wall between ceiling and floor: mosaic tiles, the song's line-colour band,
    /// a steel skirting at the floor and a run of fluorescent tubes just under the ceiling.
    /// </summary>
    private void CreateWall(Transform parent, float side, StageTextures textures)
    {
        string name = side < 0f ? "Left" : "Right";
        float near = 1f + active.nearExtension;

        Material tiles = CreateTexturedMaterial(Dim(active.wallColor), textures.Wall, Vector2.one);
        CreateSurface(parent, $"Wall_{name}",
            t => GetWallPoint(side, t, -1f, 0f),
            t => GetWallPoint(side, t, 1f, 0f),
            0f, near, SurfaceRows, active.wallTileLength * StageTextures.WallTilesAlong, 0f, tiles);

        Material band = CreateMaterial(Dim(lineColor * 0.8f));
        lineBandMaterials.Add(band);
        CreateSurface(parent, $"WallLineBand_{name}",
            t => GetWallPoint(side, t, -0.19f, 0.015f),
            t => GetWallPoint(side, t, -0.14f, 0.015f),
            0f, near, SurfaceRows, 1f, 0f, band);

        CreateSurface(parent, $"WallSkirting_{name}",
            t => GetWallPoint(side, t, -1f, 0.015f),
            t => GetWallPoint(side, t, -0.95f, 0.015f),
            0f, near, SurfaceRows, 1f, 0f, CreateMaterial(Dim(active.boundaryColor * 0.8f)));

        // The tubes sit in a dark trough along the top of the wall.
        CreateSurface(parent, $"LightTrough_{name}",
            t => GetWallPoint(side, t, 0.93f, 0.01f),
            t => GetWallPoint(side, t, 1f, 0.01f),
            0f, near, SurfaceRows, 1f, 0f, CreateMaterial(new Color(0.05f, 0.05f, 0.055f, 1f)));
        Material tubes = CreateTexturedMaterial(Boost(active.lightColor, active.lightIntensity), textures.Tube, Vector2.one);
        CreateSurface(parent, $"LightTubes_{name}",
            t => GetWallPoint(side, t, 0.945f, 0.03f),
            t => GetWallPoint(side, t, 0.985f, 0.03f),
            0f, near, SurfaceRows, active.lightTubeLength, 0f, tubes);
    }

    /// <summary>
    /// Lit advertising boxes along both walls (as in any station passage), above the
    /// line band, one every posterSpacing of corridor depth from just behind the
    /// judgement line into the dark. Their pictures are generated (abstract layouts).
    /// </summary>
    private static void CreatePosters(Transform parent, StageTextures textures)
    {
        if (active.posterSpacing <= 0f)
        {
            return;
        }

        Material frame = CreateMaterial(Dim(new Color(0.20f, 0.21f, 0.23f, 1f)));
        float farthest = CorridorDepth(0f) * 0.55f;
        int index = 0;
        for (float start = CorridorDepth(1f) + 3f; start + active.posterWidth < farthest; start += active.posterSpacing, index++)
        {
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;

                // Staggered so the two walls do not mirror each other.
                float offset = sideIndex == 0 ? 0f : active.posterSpacing * 0.5f;
                float from = start + offset;
                float to = from + active.posterWidth;
                float tNear = ProgressAtCorridorDepth(from);
                float tFar = ProgressAtCorridorDepth(to);
                float frameNear = ProgressAtCorridorDepth(from - 0.18f);
                float frameFar = ProgressAtCorridorDepth(to + 0.18f);

                CreateSurface(parent, $"PosterFrame_{index}_{sideIndex}",
                    t => GetWallPoint(side, t, 0.02f, 0.03f),
                    t => GetWallPoint(side, t, 0.68f, 0.03f),
                    frameFar, frameNear, 4, 1f, 0f, frame);

                Texture2D picture = textures.Posters[(index * 2 + sideIndex * 3) % PosterVariants];
                Material poster = CreateTexturedMaterial(new Color(active.posterGlow, active.posterGlow, active.posterGlow, 1f), picture, Vector2.one);
                CreateSurface(parent, $"Poster_{index}_{sideIndex}",
                    t => GetWallPoint(side, t, 0.06f, 0.05f),
                    t => GetWallPoint(side, t, 0.64f, 0.05f),
                    tFar, tNear, 8, active.posterWidth, from, poster);
            }
        }
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
        backWall.GetComponent<Renderer>().sharedMaterial = CreateMaterial(Dim(active.backWallColor));

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
    /// Four faint rails running from the far end to the judgement plane at the edges
    /// of the laser space (a full stick tilt up / down / left / right), for depth.
    /// Lasers are followed by direction, not aimed at, so there is no cursor or reticle.
    /// </summary>
    private static void CreateLaserRails(Transform parent)
    {
        Vector2[] axes = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        foreach (Vector2 axis in axes)
        {
            CreateLine(parent, $"LaserRail_{axis.x:0}_{axis.y:0}", GetLaserWorldPosition(axis, 0f), GetLaserWorldPosition(axis, 1f), 0.03f, active.laserRailColor);
        }
    }

    /// <summary>
    /// A strip between two edges from progress <paramref name="t0"/> to <paramref name="t1"/>,
    /// textured with U across (0 at <paramref name="edgeA"/>, <paramref name="uScale"/> at
    /// <paramref name="edgeB"/>) and V along the corridor depth (one repeat every
    /// <paramref name="vLength"/>, starting at corridor depth <paramref name="vOrigin"/>).
    /// Rows are spaced evenly on screen (in 1 / depth), so the texture stays straight.
    /// </summary>
    private static void CreateSurface(Transform parent, string name, Edge edgeA, Edge edgeB, float t0, float t1, int rows, float vLength, float vOrigin, Material material, float uScale = 1f)
    {
        var vertices = new Vector3[(rows + 1) * 2];
        var uvs = new Vector2[vertices.Length];
        float inverseFrom = 1f / CorridorDepth(t0);
        float inverseTo = 1f / CorridorDepth(t1);
        for (int row = 0; row <= rows; row++)
        {
            float depth = 1f / Mathf.Lerp(inverseFrom, inverseTo, row / (float)rows);
            float t = row == 0 ? t0 : row == rows ? t1 : ProgressAtCorridorDepth(depth);
            float v = (depth - vOrigin) / Mathf.Max(0.001f, vLength);
            vertices[row * 2] = edgeA(t);
            vertices[row * 2 + 1] = edgeB(t);
            uvs[row * 2] = new Vector2(0f, v);
            uvs[row * 2 + 1] = new Vector2(uScale, v);
        }

        // Both windings, so the surface shows from either side.
        var triangles = new int[rows * 12];
        for (int row = 0; row < rows; row++)
        {
            int a = row * 2;
            int offset = row * 12;
            triangles[offset] = a;
            triangles[offset + 1] = a + 2;
            triangles[offset + 2] = a + 1;
            triangles[offset + 3] = a + 1;
            triangles[offset + 4] = a + 2;
            triangles[offset + 5] = a + 3;
            triangles[offset + 6] = a;
            triangles[offset + 7] = a + 1;
            triangles[offset + 8] = a + 2;
            triangles[offset + 9] = a + 1;
            triangles[offset + 10] = a + 3;
            triangles[offset + 11] = a + 2;
        }

        GameObject meshObject = new GameObject(name);
        meshObject.transform.SetParent(parent, false);

        Mesh mesh = new Mesh { name = name };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
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
        line.shadowCastingMode = ShadowCastingMode.Off;
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

    /// <summary>Opaque unlit material with a texture, tinted (HDR tints glow).</summary>
    public static Material CreateTexturedMaterial(Color tint, Texture texture, Vector2 scale)
    {
        Material material = CreateMaterial(tint);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", scale);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", scale);
        }

        return material;
    }

    /// <summary>Opaque unlit material brighter than white by <paramref name="intensity"/>, so it blooms.</summary>
    public static Material CreateGlowMaterial(Color color, float intensity)
    {
        return CreateMaterial(Boost(color, intensity));
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
