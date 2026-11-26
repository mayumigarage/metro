using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The judgements, drawn on the lanes themselves like the combo (LaneComboView): each
/// lane has its own word lying flat on the lane a little before the judgement line, so
/// nothing pops up in the middle of the screen where the lasers fly. A note's judgement
/// shows on its lane with FAST / SLOW (and the ms, if on) under it; a laser's on the
/// floor lane of its stick, with how much of it was followed. Each is a world-space
/// canvas, so it recedes with the lane and the notes pass over it.
/// </summary>
public sealed class LaneJudgementView : MonoBehaviour
{
    private const float PopSeconds = 0.08f;

    [Tooltip("レーン上の位置（0 = 奥、1 = 判定ライン）。コンボ（0.6 付近）と判定ラインの間。")]
    [SerializeField] private float laneProgress = 0.85f;
    [Tooltip("文字の大きさ（横、レーンに沿った縦）。")]
    [SerializeField] private Vector2 worldScale = new Vector2(0.0085f, 0.013f);
    [Tooltip("判定を表示する秒数。")]
    [SerializeField] private float showSeconds = 0.4f;
    [SerializeField] private float opacity = 0.95f;

    private readonly Slot[] slots = new Slot[RhythmStageView.LaneCount];
    private JudgeSystem judgeSystem;
    private bool visible = true;

    private sealed class Slot
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public Text Word;
        public Text Detail;
        public float ShownAt = -10f;
        public float VisibleUntil;
    }

    /// <summary>GameFlow shows the judgements only while a song is on screen.</summary>
    public void SetVisible(bool value)
    {
        visible = value;
        if (!value)
        {
            foreach (Slot slot in slots)
            {
                if (slot != null)
                {
                    slot.VisibleUntil = 0f;
                }
            }
        }
    }

    private void Start()
    {
        for (int lane = 0; lane < slots.Length; lane++)
        {
            slots[lane] = BuildSlot(lane);
        }
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
        float now = Time.unscaledTime;
        foreach (Slot slot in slots)
        {
            if (slot == null)
            {
                continue;
            }

            // Pops in slightly oversized, fades out at the end of its time.
            float alpha = visible ? Mathf.Clamp01((slot.VisibleUntil - now) * 8f) : 0f;
            slot.Group.alpha = alpha * opacity;
            float pop = 1f + 0.22f * Mathf.Clamp01(1f - (now - slot.ShownAt) / PopSeconds);
            slot.Root.localScale = new Vector3(worldScale.x * pop, worldScale.y * pop, 1f);
        }
    }

    private void OnJudgementResolved(JudgementResult result)
    {
        string detail = string.Empty;
        Color detailColor = UiKit.TextDim;

        // Early / late only says something about a timed hit that was off but not missed.
        bool timed = result.part != NotePart.HoldEnd
            && (result.judgement == Judgement.Great || result.judgement == Judgement.Good)
            && result.deltaSeconds != 0d;
        if (timed)
        {
            bool fast = result.deltaSeconds < 0d;
            if (GameSettings.ShowFastSlow)
            {
                detail = fast ? "FAST" : "SLOW";
            }

            if (GameSettings.ShowTimingDetail)
            {
                detail += $"{(detail.Length > 0 ? "  " : string.Empty)}{result.deltaSeconds * 1000d:+0;-0;0} ms";
            }

            detailColor = fast ? UiKit.Fast : UiKit.Slow;
        }
        else if (result.part == NotePart.HoldEnd && result.judgement == Judgement.Miss && result.deltaSeconds < 0d)
        {
            detail = "HOLD RELEASED";
        }

        Show(result.lane, result.judgement, detail, detailColor);
    }

    private void OnLaserJudgementResolved(LaserJudgementResult result)
    {
        // The floor lane of the laser's stick (ZL for the left stick, ZR for the right).
        int lane = 2 + Mathf.Clamp(result.side, 0, 1);
        Show(lane, result.judgement, $"LASER  {result.trackingAccuracy * 100f:0}%", RhythmStageView.GetStickColor(result.side));
    }

    private void Show(int lane, Judgement judgement, string detail, Color detailColor)
    {
        Slot slot = lane >= 0 && lane < slots.Length ? slots[lane] : null;
        if (slot == null)
        {
            return;
        }

        slot.Word.text = judgement.ToString().ToUpperInvariant();
        slot.Word.color = UiKit.GetJudgementColor(judgement);
        slot.Detail.text = detail;
        slot.Detail.color = detailColor;
        slot.ShownAt = Time.unscaledTime;
        slot.VisibleUntil = slot.ShownAt + showSeconds;
        Place(slot, lane);
    }

    private Slot BuildSlot(int lane)
    {
        GameObject canvasObject = new GameObject($"LaneJudgement_{RhythmStageView.GetLaneLabel(lane)}");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // With the combo, before the other see-through things (hold bodies, lasers), so they pass over it.
        canvas.sortingOrder = -9;
        canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;

        var slot = new Slot
        {
            Root = (RectTransform)canvasObject.transform,
            Group = canvasObject.AddComponent<CanvasGroup>()
        };
        slot.Group.alpha = 0f;
        slot.Group.blocksRaycasts = false;
        slot.Root.sizeDelta = new Vector2(900f, 230f);

        slot.Word = SignKit.CreateText(slot.Root, "Word", string.Empty, 128, TextAnchor.MiddleCenter, Color.white, true);
        UiKit.Place(slot.Word, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(900f, 160f));
        slot.Detail = SignKit.CreateText(slot.Root, "Detail", string.Empty, 54, TextAnchor.MiddleCenter, UiKit.TextDim, true);
        UiKit.Place(slot.Detail, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(900f, 70f));
        Place(slot, lane);
        return slot;
    }

    /// <summary>
    /// Lays the slot flat on its lane. Text "up" runs toward the far end on the floor
    /// and toward the near end on the ceiling, which is screen-up for both.
    /// </summary>
    private void Place(Slot slot, int lane)
    {
        Vector3 far = RhythmStageView.GetLaneWorldPosition(lane, 0f);
        Vector3 near = RhythmStageView.GetLaneWorldPosition(lane, 1f);
        Vector3 up = (far - near).normalized * (RhythmStageView.IsUpperLane(lane) ? -1f : 1f);
        Vector3 forward = Vector3.Cross(Vector3.right, up);
        slot.Root.SetPositionAndRotation(
            RhythmStageView.GetLaneWorldPosition(lane, laneProgress) + RhythmStageView.GetLaneInward(lane) * 0.04f,
            Quaternion.LookRotation(forward, up));
    }
}
