using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The combo counter, drawn on the lanes themselves: one copy lying on the floor
/// (ZL / ZR) and one on the ceiling (L / R), centred on the divider between the
/// two lanes of the row, in white at low opacity on the dark passage. They are
/// world-space canvases, so the number recedes
/// with the lane and the notes pass over it.
/// </summary>
public sealed class LaneComboView : MonoBehaviour
{
    private const int RowCount = 2;
    private const float PopSeconds = 0.12f;

    [Tooltip("レーン上の位置（0 = 奥、1 = 判定ライン）。")]
    [SerializeField] private float laneProgress = 0.6f;
    [Tooltip("この数以上でコンボを表示する。")]
    [SerializeField] private int minimumCombo = 2;
    [Tooltip("文字の大きさ（横、レーンに沿った縦）。")]
    [SerializeField] private Vector2 worldScale = new Vector2(0.0125f, 0.019f);
    [SerializeField] private float opacity = 0.26f;

    private readonly RectTransform[] rows = new RectTransform[RowCount];
    private readonly CanvasGroup[] groups = new CanvasGroup[RowCount];
    private readonly Text[] numberTexts = new Text[RowCount];
    private readonly Text[] labelTexts = new Text[RowCount];
    private Color lineColor = SignKit.MetroRed;
    private ScoreTracker score;
    private int shownCombo = -1;
    private float changedAt;
    private bool visible = true;

    /// <summary>GameFlow shows the combo only while a song is on screen.</summary>
    public void SetVisible(bool value)
    {
        visible = value;
    }

    /// <summary>The "COMBO" caption takes the song's line (difficulty) colour, like the HUD boards.</summary>
    public void SetLineColor(Color color)
    {
        lineColor = color;
        for (int row = 0; row < RowCount; row++)
        {
            if (labelTexts[row] != null)
            {
                labelTexts[row].color = color;
            }
        }
    }

    private void Start()
    {
        for (int row = 0; row < RowCount; row++)
        {
            BuildRow(row);
        }

        score = FindFirstObjectByType<ScoreTracker>();
        if (score != null)
        {
            score.Changed += Refresh;
        }

        Refresh();
    }

    private void OnDestroy()
    {
        if (score != null)
        {
            score.Changed -= Refresh;
        }
    }

    private void Update()
    {
        // A short swell whenever the number goes up; a quick fade when the combo breaks.
        float pop = 1f + 0.18f * Mathf.Clamp01(1f - (Time.unscaledTime - changedAt) / PopSeconds);
        bool shown = visible && shownCombo >= minimumCombo;
        for (int row = 0; row < RowCount; row++)
        {
            rows[row].localScale = new Vector3(worldScale.x * pop, worldScale.y * pop, 1f);
            groups[row].alpha = Mathf.MoveTowards(groups[row].alpha, shown ? opacity : 0f, Time.unscaledDeltaTime * 8f);
        }
    }

    private void Refresh()
    {
        int combo = score != null ? score.Combo : 0;
        if (combo == shownCombo)
        {
            return;
        }

        if (combo > shownCombo)
        {
            changedAt = Time.unscaledTime;
        }

        shownCombo = combo;
        if (combo < minimumCombo)
        {
            return;
        }

        for (int row = 0; row < RowCount; row++)
        {
            numberTexts[row].text = combo.ToString();
            PlaceRow(row);
        }
    }

    private void BuildRow(int row)
    {
        GameObject canvasObject = new GameObject(row == 0 ? "LaneCombo_Upper" : "LaneCombo_Lower");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // Drawn before the other see-through things (hold bodies, lasers), so they pass over it.
        canvas.sortingOrder = -10;
        canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
        groups[row] = canvasObject.AddComponent<CanvasGroup>();
        groups[row].alpha = 0f;
        groups[row].blocksRaycasts = false;

        rows[row] = (RectTransform)canvasObject.transform;
        rows[row].sizeDelta = new Vector2(900f, 360f);

        numberTexts[row] = SignKit.CreateText(rows[row], "Number", "0", 230, TextAnchor.MiddleCenter, Color.white, true);
        UiKit.Place(numberTexts[row], new Vector2(0.5f, 1f), Vector2.zero, new Vector2(900f, 270f));

        labelTexts[row] = SignKit.CreateText(rows[row], "Label", "COMBO", 64, TextAnchor.MiddleCenter, lineColor, true);
        UiKit.Place(labelTexts[row], new Vector2(0.5f, 0f), Vector2.zero, new Vector2(900f, 84f));
        PlaceRow(row);
    }

    /// <summary>
    /// Lays the row's canvas flat on its lanes. Text "up" runs toward the far end on
    /// the floor and toward the near end on the ceiling, which is screen-up for both.
    /// </summary>
    private void PlaceRow(int row)
    {
        int leftLane = row * 2;
        Vector3 inward = RhythmStageView.GetLaneInward(leftLane);
        Vector3 far = GetRowCentre(leftLane, 0f);
        Vector3 near = GetRowCentre(leftLane, 1f);
        Vector3 up = (far - near).normalized * (RhythmStageView.IsUpperLane(leftLane) ? -1f : 1f);
        Vector3 forward = Vector3.Cross(Vector3.right, up);

        rows[row].SetPositionAndRotation(
            GetRowCentre(leftLane, laneProgress) + inward * 0.03f,
            Quaternion.LookRotation(forward, up));
    }

    /// <summary>The point on the divider between a row's two lanes at <paramref name="progress"/>.</summary>
    private static Vector3 GetRowCentre(int leftLane, float progress)
    {
        return (RhythmStageView.GetLaneWorldPosition(leftLane, progress)
            + RhythmStageView.GetLaneWorldPosition(leftLane + 1, progress)) * 0.5f;
    }
}
