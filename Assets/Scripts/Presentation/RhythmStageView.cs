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
    [Tooltip("背景とフォグの色（背景は後で決める。今は黒）。レーンの奥はこの色に消える。")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 1f);

    [Header("Depth (notes travel from Back to Front)")]
    public float frontZ = 0.45f;
    public float backZ = 34f;

    [Header("Lanes")]
    [Tooltip("判定ライン位置でのレーン面の高さ（上段は +、下段は -）。")]
    public float frontLaneY = 4.78f;
    [Tooltip("奥でのレーン面の高さ。奥の外側 X は frontOuterX をこの比率で縮めたものになり、全ての辺が画面中心に収束する。小さいほど消失点が奥に見える。")]
    public float backLaneY = 0.3f;
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

    [Header("Lane ends")]
    [Tooltip("判定ラインより手前（カメラ側）にレーンを延ばす量（progress 単位）。画面の端までレーンが続く。")]
    public float nearExtension = 0.18f;
    [Tooltip("判定ラインの太さ（判定ライン位置の縮尺）。")]
    public float judgementLineWidth = 0.2f;
    [Tooltip("フォグ（レーンの奥が背景色に消える）の開始・終了距離。")]
    public float fogStart = 16f;
    public float fogEnd = 82f;

    [Header("Colours")]
    [Tooltip("上段（L / R）と下段（ZL / ZR）のレーン面の色。")]
    public Color leftLaneColor = new Color(0.03f, 0.03f, 0.035f, 1f);
    public Color rightLaneColor = new Color(0.03f, 0.03f, 0.035f, 1f);
    [Tooltip("左スティックで操作するもの全て（レーザー、方向ノーツ、カーソル）の色。")]
    public Color leftStickColor = new Color(0f, 0.64f, 0.93f, 1f);
    [Tooltip("右スティックで操作するもの全て（レーザー、方向ノーツ、カーソル）の色。")]
    public Color rightStickColor = new Color(0.91f, 0.18f, 0.60f, 1f);
    [Tooltip("通常ノーツ（方向ノーツの下のバーも含む）の色。")]
    public Color tapNoteColor = Color.white;
    [Tooltip("ロングノーツの色。")]
    public Color holdNoteColor = new Color(0.52f, 0.36f, 0.86f, 1f);
    [Tooltip("同時押しノーツ（同じタイミングに別レーンのボタンノーツがあるもの）の色。ロングノーツは頭のバーだけ。")]
    public Color simultaneousNoteColor = new Color(1f, 0.82f, 0.16f, 1f);
    [Tooltip("L / ZL のボタンを押している間の光の柱の色。")]
    public Color leftButtonColor = new Color(1f, 0.52f, 0.28f, 1f);
    [Tooltip("R / ZR のボタンを押している間の光の柱の色。")]
    public Color rightButtonColor = new Color(0.20f, 0.70f, 1f, 1f);
    [Tooltip("レーンの仕切りと外側の端の線。")]
    public Color boundaryColor = new Color(0.42f, 0.43f, 0.47f, 1f);
    [Tooltip("ノーツと一緒に流れるトンネルの枠（グリッド）の色。手前ほど濃く、奥で消える。")]
    public Color gridColor = new Color(0.78f, 0.82f, 0.90f, 0.14f);
    [Tooltip("判定ラインの色。")]
    public Color judgementLineColor = Color.white;
    public Color backWallColor = new Color(0.02f, 0.021f, 0.025f, 1f);
    public Color backFrameColor = new Color(0.16f, 0.17f, 0.19f, 1f);
    public Color laserRailColor = new Color(0.20f, 0.21f, 0.24f, 1f);

    [Header("Glow (HDR brightness; anything above the bloom threshold glows)")]
    [Tooltip("ブルームがかかり始める明るさ（ガンマ空間）。HUD の白い文字（1.0）が少し光るよう 1 より下に置く。")]
    public float bloomThreshold = 0.9f;
    [Tooltip("オプションの BLOOM 100% でのブルーム強度。")]
    public float bloomIntensity = 0.8f;
    [Tooltip("ブルームの広がり。大きいほど光が遠くまでにじむ。")]
    [Range(0f, 1f)] public float bloomScatter = 0.6f;
    [Tooltip("ノーツの明るさ倍率。1 より上でブルームがかかる。")]
    public float noteGlow = 1.2f;
    [Tooltip("判定ラインの明るさ倍率。1 より上でブルームがかかる。")]
    public float judgementLineGlow = 1.3f;
    [Tooltip("レーンのボタンを押している間の光の柱の濃さ。")]
    [Range(0f, 1f)] public float laneBeamOpacity = 0.34f;

    [Header("Post-processing")]
    [Range(0f, 1f)] public float vignette = 0.25f;
    [Range(0f, 1f)] public float filmGrain = 0f;
    [Range(0f, 1f)] public float chromaticAberration = 0f;
    [Range(-100f, 100f)] public float contrast = 0f;

    /// <summary>The back is the front scaled by backLaneY / frontLaneY, so every edge converges on the centre.</summary>
    public float BackOuterX => frontLaneY > 0f ? frontOuterX * backLaneY / frontLaneY : 0f;
}

/// <summary>
/// Builds the stage: the far end (奥) sits in the screen centre and everything
/// travels outward to the near end (前). The L / R lanes form the ceiling and the
/// ZL / ZR lanes the floor of a tunnel, with no gap between the two lanes of a
/// row. The open space between ceiling and floor is where the stick lasers fly.
///
/// The lanes are plain black surfaces with grey edge lines and a glowing white
/// judgement line; there is nothing around them yet (the background is to be
/// decided). The tunnel narrows toward the far end, so surfaces are divided along
/// the depth of the straight corridor it looks like (<see cref="CorridorDepth"/>),
/// not along z, and their rows stay evenly spaced on screen.
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
    private const int SurfaceRows = 48;

    private static readonly string[] LaneLabels = { "L", "R", "ZL", "ZR" };

    // The layout every static geometry function reads. Defaults until a stage enables.
    private static StageLayout active = new StageLayout();

    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private StageLayout layout = new StageLayout();

    private Transform stageRoot;
    private bool rebuildRequested;
    private readonly MeshRenderer[] laneBeams = new MeshRenderer[LaneCount];
    private Bloom bloom;
    private int builtBrightnessPercent;
    private float projectedAspect;

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

    /// <summary>
    /// Colour of everything played with a stick: the LASER COLOR option if one was chosen,
    /// otherwise the stage layout's colour.
    /// </summary>
    public static Color GetStickColor(int side)
    {
        if (GameSettings.TryGetLaserColor(side, out Color chosen))
        {
            return chosen;
        }

        return side <= 0 ? active.leftStickColor : active.rightStickColor;
    }

    /// <summary>Share of the screen height, from the top, that the HUD sign takes (the stage sits below it).</summary>
    public static float TopInset => Mathf.Clamp(active.topInset, 0f, 0.4f);

    /// <summary>Colour of a lane's button-held beam: follows the button side (L / ZL, R / ZR).</summary>
    public static Color GetButtonColor(int side)
    {
        return side <= 0 ? active.leftButtonColor : active.rightButtonColor;
    }

    /// <summary>Colour of a note's bar: white for a plain note, violet for a hold.</summary>
    public static Color GetNoteColor(bool hold)
    {
        return hold ? active.holdNoteColor : active.tapNoteColor;
    }

    /// <summary>Colour of the bar of a note hit together with another lane's button note: yellow.</summary>
    public static Color SimultaneousNoteColor => active.simultaneousNoteColor;

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

    /// <summary>
    /// Turns the time-linear approach (0 = just spawned at the far end, 1 = on the judgement
    /// line) into the progress to draw at. With the APPROACH option on, progress = linear ^ k
    /// (<see cref="ApproachExponent"/>): it crawls at the far end and speeds up toward the line.
    /// Spawning is <see cref="ApproachTimeScale"/> (= k) times earlier, which makes the speed at
    /// the judgement line exactly the LINEAR one (k / (k * T) = 1 / T); the far end is where
    /// the extra time goes. The line is still reached on the beat.
    /// </summary>
    public static float ApplyApproachCurve(float linear)
    {
        if (!GameSettings.ApproachAccelerate || linear <= 0f || linear >= 1f)
        {
            return linear;
        }

        return Mathf.Pow(linear, ApproachExponent);
    }

    /// <summary>How many times longer notes are in view (travel time) with APPROACH on: 1 when LINEAR.</summary>
    public static float ApproachTimeScale => GameSettings.ApproachAccelerate ? ApproachExponent : 1f;

    private static float ApproachExponent => 1f + 0.2f * GameSettings.ApproachStrength;

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

    /// <summary>
    /// The tunnel narrows toward the far end, which on screen looks exactly like a straight
    /// corridor of the near end's cross-section seen in perspective. This is the depth of a
    /// progress along that corridor (from the camera); surfaces are divided along it, so
    /// their rows are evenly spaced on screen and widths stay true to the near end's scale.
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

        for (int lane = 0; lane < LaneCount; lane++)
        {
            CreateLane(stageRoot, lane);
            laneBeams[lane] = CreateLaneBeam(stageRoot, lane);
        }

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

        // The far end of the lanes fades into the background. Unlit materials take fog too.
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
    /// Global volume for the stage: bloom from a threshold just under white (the notes,
    /// the judgement line, the lasers and the HUD sign's white lettering glow) and a
    /// vignette; film grain, chromatic aberration and contrast are there to tune but
    /// off by default. No tonemapping, so the sign's white stays white.
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
        adjustments.active = !Mathf.Approximately(active.contrast, 0f);

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
    /// One lane: a plain black surface from the far end to past the judgement line,
    /// the grey divider and outer edge lines, and a glowing white strip across it at
    /// the judgement line (progress 1).
    /// </summary>
    private static void CreateLane(Transform parent, int lane)
    {
        string label = GetLaneLabel(lane);
        bool upper = IsUpperLane(lane);
        int column = GetLaneColumnSign(lane) < 0f ? 0 : 1;
        float near = 1f + active.nearExtension;

        Material surface = CreateMaterial(Dim(column == 0 ? active.leftLaneColor : active.rightLaneColor));
        CreateSurface(parent, $"LaneSurface_{label}",
            t => GetLaneEdge(lane, t, false),
            t => GetLaneEdge(lane, t, true),
            0f, near, SurfaceRows, 1f, 0f, surface);

        // Lines sit slightly off the surface (toward the screen centre) so they never z-fight with it.
        Vector3 lift = GetLaneInward(lane) * 0.02f;
        Vector3 frontInner = GetLaneEdge(lane, near, false);
        Vector3 frontOuter = GetLaneEdge(lane, near, true);
        Vector3 backInner = GetLaneEdge(lane, 0f, false);
        Vector3 backOuter = GetLaneEdge(lane, 0f, true);
        if (column == 0)
        {
            // The two lanes of a row share their inner edge, so only one of them draws the divider.
            CreateLine(parent, $"LaneDivider_{(upper ? "Upper" : "Lower")}", frontInner + lift, backInner + lift, 0.05f, Dim(active.boundaryColor));
        }

        CreateLine(parent, $"LaneBoundary_{label}_Outer", frontOuter + lift, backOuter + lift, 0.07f, Dim(active.boundaryColor));

        // The judgement line: a strip judgementLineWidth deep (at the near end's scale) centred on progress 1.
        float centre = CorridorDepth(1f);
        float halfWidth = Mathf.Max(0.01f, active.judgementLineWidth) * 0.5f;
        float tNear = ProgressAtCorridorDepth(centre - halfWidth);
        float tFar = ProgressAtCorridorDepth(centre + halfWidth);
        Vector3 lineLift = GetLaneInward(lane) * 0.03f;
        Material line = CreateGlowMaterial(active.judgementLineColor, active.judgementLineGlow);
        CreateSurface(parent, $"JudgementLine_{label}",
            t => GetLaneEdge(lane, t, false) + lineLift,
            t => GetLaneEdge(lane, t, true) + lineLift,
            tFar, tNear, 2, 1f, 0f, line);
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
