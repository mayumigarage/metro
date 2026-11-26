using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Score / combo HUD. The canvas and its texts live in the scene (create them with
/// Metro > Build HUD Into Scene) so their layout, fonts and wording are edited
/// there; if the references are missing, the same HUD is built at runtime.
/// The lane labels follow the stage geometry and are always placed at runtime.
/// </summary>
public sealed class RhythmJudgementUI : MonoBehaviour
{
    [SerializeField] private JudgeSystem judgeSystem;

    [Header("HUD objects (in the scene)")]
    [SerializeField] private Canvas hudCanvas;
    [SerializeField] private Text resultText;
    [SerializeField] private Text scoreText;
    [SerializeField] private Text timingText;

    [Header("Judgement display")]
    [SerializeField] private float readySeconds = 2f;
    [SerializeField] private float resultSeconds = 0.35f;
    [SerializeField] private Color perfectColor = new Color(1f, 0.93f, 0.35f, 1f);
    [SerializeField] private Color greatColor = new Color(0.35f, 0.86f, 1f, 1f);
    [SerializeField] private Color goodColor = new Color(0.48f, 1f, 0.55f, 1f);
    [SerializeField] private Color missColor = new Color(1f, 0.35f, 0.4f, 1f);

    [Header("Lane labels (placed at runtime from the stage)")]
    [SerializeField] private int laneLabelFontSize = 24;
    [Tooltip("判定ラインからの上下オフセット（画面の高さに対する割合）。")]
    [SerializeField] private float laneLabelOffset = 0.045f;
    [SerializeField] private Color laneLabelColor = new Color(1f, 0.9f, 0.35f, 1f);

    private CanvasGroup resultGroup;
    private int score;
    private int combo;
    private float resultVisibleUntil;

    public bool HasHud => hudCanvas != null && resultText != null && scoreText != null && timingText != null;

    private void Awake()
    {
        judgeSystem = judgeSystem != null ? judgeSystem : FindFirstObjectByType<JudgeSystem>();
        if (!HasHud)
        {
            BuildHud();
        }

        resultGroup = resultText.GetComponent<CanvasGroup>();
        if (resultGroup == null)
        {
            resultGroup = resultText.gameObject.AddComponent<CanvasGroup>();
        }

        resultText.text = "READY";
        resultVisibleUntil = Time.unscaledTime + readySeconds;
    }

    private void Start()
    {
        BuildStageLabels();
    }

    private void OnEnable()
    {
        judgeSystem = judgeSystem != null ? judgeSystem : FindFirstObjectByType<JudgeSystem>();
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved += OnJudgementResolved;
            judgeSystem.LaserJudgementResolved += OnLaserJudgementResolved;
        }
    }

    private void OnDisable()
    {
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved -= OnJudgementResolved;
            judgeSystem.LaserJudgementResolved -= OnLaserJudgementResolved;
        }
    }

    private void Update()
    {
        if (resultGroup == null)
        {
            return;
        }

        float remaining = resultVisibleUntil - Time.unscaledTime;
        resultGroup.alpha = Mathf.Clamp01(remaining * 5f);
    }

    /// <summary>
    /// Creates the HUD canvas and texts under this object and wires the references.
    /// Called by the editor menu to put them in the scene, and at runtime as a fallback.
    /// </summary>
    public GameObject BuildHud()
    {
        GameObject canvasObject = new GameObject("RhythmHUD");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        canvasObject.AddComponent<GraphicRaycaster>();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hudCanvas = canvas;

        // Small and on the upper half of the far wall: the screen centre itself is
        // where the stick cursors rest and the lasers fly.
        resultText = CreateText(canvas.transform, "JudgementResult", font, 30, TextAnchor.MiddleCenter);
        SetRect(resultText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(200f, 36f), new Vector2(0f, 45f));
        resultText.gameObject.AddComponent<CanvasGroup>();
        resultText.text = "READY";

        // Left edge at mid height, mirroring the help text on the right.
        scoreText = CreateText(canvas.transform, "ScoreAndCombo", font, 24, TextAnchor.MiddleLeft);
        SetRect(scoreText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(250f, 120f), new Vector2(34f, 0f));
        scoreText.text = "SCORE 000000\nCOMBO 00";

        // Right edge at mid height: the lanes span the top and bottom of the screen,
        // and the laser space ends well short of the side edges.
        timingText = CreateText(canvas.transform, "TimingWindows", font, 16, TextAnchor.MiddleRight);
        SetRect(timingText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(250f, 150f), new Vector2(-34f, 0f));
        timingText.text = "TAP: L R ZL ZR\n(KEYS D K F J)\nARROW NOTE: BUTTON + STICK\nLASER: FOLLOW WITH STICK\n(KEYS ↑ ↓ ← →)\nPERFECT ±35ms  GREAT ±70ms\nGOOD ±110ms  MISS ±160ms";
        timingText.color = new Color(0.75f, 0.86f, 0.96f, 1f);

        return canvasObject;
    }

    /// <summary>
    /// Labels that follow the 3D stage. Built in Start so RhythmStageView.Awake
    /// has already placed the camera.
    /// </summary>
    private void BuildStageLabels()
    {
        Camera stageCamera = Camera.main;
        if (stageCamera == null || hudCanvas == null)
        {
            return;
        }

        for (int lane = 0; lane < RhythmStageView.LaneCount; lane++)
        {
            Vector3 viewport = stageCamera.WorldToViewportPoint(RhythmStageView.GetLaneWorldPosition(lane, 1f));
            float outward = RhythmStageView.IsUpperLane(lane) ? laneLabelOffset : -laneLabelOffset;
            CreateStageLabel(
                $"LaneLabel_{RhythmStageView.GetLaneLabel(lane)}",
                RhythmStageView.GetLaneLabel(lane),
                new Vector2(viewport.x, viewport.y + outward),
                laneLabelFontSize,
                laneLabelColor);
        }
    }

    private void CreateStageLabel(string name, string content, Vector2 viewportPosition, int size, Color color)
    {
        Text label = CreateText(hudCanvas.transform, name, resultText.font, size, TextAnchor.MiddleCenter);
        RectTransform rect = label.rectTransform;
        rect.anchorMin = viewportPosition;
        rect.anchorMax = viewportPosition;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(200f, 32f);
        rect.anchoredPosition = Vector2.zero;
        label.text = content;
        label.color = color;
    }

    private void OnJudgementResolved(JudgementResult result)
    {
        if (result.judgement == Judgement.Miss)
        {
            combo = 0;
        }
        else
        {
            combo++;
            score += result.judgement == Judgement.Perfect ? 1000 :
                result.judgement == Judgement.Great ? 700 : 400;
        }

        resultText.text = result.judgement.ToString().ToUpperInvariant();
        resultText.color = GetJudgementColor(result.judgement);
        resultVisibleUntil = Time.unscaledTime + resultSeconds;

        string kind = result.direction == NoteDirection.None ? "DELTA" : $"FLICK {GetDirectionLabel(result.direction)}";
        scoreText.text = $"SCORE {score:000000}\nCOMBO {combo:00}\n{kind} {result.deltaSeconds * 1000.0:+0;-0;0} ms";
    }

    private void OnLaserJudgementResolved(LaserJudgementResult result)
    {
        if (result.judgement == Judgement.Miss)
        {
            combo = 0;
        }
        else
        {
            combo++;
            score += result.judgement == Judgement.Perfect ? 1000 :
                result.judgement == Judgement.Great ? 700 : 400;
        }

        resultText.text = result.judgement.ToString().ToUpperInvariant();
        resultText.color = GetJudgementColor(result.judgement);
        resultVisibleUntil = Time.unscaledTime + resultSeconds;

        string side = result.side == 0 ? "L" : "R";
        scoreText.text = $"SCORE {score:000000}\nCOMBO {combo:00}\nLASER {side} {result.trackingAccuracy * 100f:0}%";
    }

    private static string GetDirectionLabel(NoteDirection direction)
    {
        switch (direction)
        {
            case NoteDirection.Up:
                return "↑";
            case NoteDirection.Down:
                return "↓";
            case NoteDirection.Left:
                return "←";
            default:
                return "→";
        }
    }

    private Color GetJudgementColor(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect:
                return perfectColor;
            case Judgement.Great:
                return greatColor;
            case Judgement.Good:
                return goodColor;
            default:
                return missColor;
        }
    }

    private static Text CreateText(Transform parent, string name, Font font, int size, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = Color.white;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 anchoredPosition)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = anchorMin;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
    }
}
