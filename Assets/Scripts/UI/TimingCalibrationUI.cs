using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Timing calibration, a sign board over the settings (the タイミング調整 card). The player
/// taps along to a steady beat and the median of how far off the taps were becomes the offset:
///
/// * sound (clicks only) → <see cref="GameSettings.JudgeOffsetMs"/>, what the judge subtracts
///   from the time of an input;
/// * picture (a falling note only) → <see cref="GameSettings.VisualOffsetMs"/> = the judge
///   offset minus the taps' median, so the note is seen when its sound is heard.
///
/// Taps are timed the way the game times them (the DSP clock, read in the frame of the press),
/// and the clicks are shifted by the AUDIO OFFSET like the music, so the numbers carry over.
/// It reads the devices itself while measuring: every lane button is a tap.
/// </summary>
public sealed class TimingCalibrationUI : MonoBehaviour
{
    private const double BeatSeconds = 0.6d;
    private const double StartDelaySeconds = 1d;
    private const double ScheduleAheadSeconds = 0.25d;

    // The first beats only set the rhythm; taps on them are not counted.
    private const int CountInBeats = 4;
    private const int TapsWanted = 16;
    private const int MinTaps = 8;
    private const int MaxBeats = CountInBeats + 28;
    private const float InputGraceSeconds = 0.15f;

    // The picture test's lane: how far apart two beats' notes are.
    private const float UnitsPerBeat = 190f;
    private const float LineY = 36f;
    private const float LaneHeight = 360f;
    private const int NoteCount = 3;

    private enum Step
    {
        Choose,
        Measure,
        Result
    }

    private sealed class Choice
    {
        public RoundedRectGraphic Face;
        public Text Label;
        public Action Act;
    }

    private readonly List<double> taps = new List<double>();
    private readonly List<Choice> buttons = new List<Choice>();
    private readonly AudioSource[] voices = new AudioSource[4];
    private readonly RectTransform[] notes = new RectTransform[NoteCount];

    private AngularGraphic band;
    private Text message;
    private Text big;
    private RectTransform lane;
    private RectTransform buttonRow;
    private AngularGraphic tapFlash;
    private AudioClip click;
    private Color lineColor = SignKit.MetroRed;
    private Step step;
    private bool visual;
    private int selected;
    private int nextClick;
    private int measuredMs;
    private double startTime;
    private float openedAt;
    private float flashedAt;

    public bool IsOpen => gameObject.activeSelf;

    public event Action Closed;

    public void Build()
    {
        RectTransform root = (RectTransform)transform;
        SignKit.CreateDim(root);

        RectTransform board = UiKit.CreateRect(root, "Sign");
        UiKit.Place(board, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 900f));
        RectTransform face = SignKit.CreateBoard(board, "Board", 16f, 14f).rectTransform;

        SignKit.CreateHeading(face, "タイミング調整", "Calibration", new Vector2(44f, -26f), 48);
        band = SignKit.CreateBand(face, "LineBand", lineColor, 148f, 14f);

        message = SignKit.CreateText(face, "Message", string.Empty, 26, TextAnchor.UpperCenter, SignKit.Ink, true);
        UiKit.Place(message, new Vector2(0.5f, 1f), new Vector2(0f, -188f), new Vector2(980f, 130f));
        message.lineSpacing = 1.15f;

        big = SignKit.CreateText(face, "Big", string.Empty, 96, TextAnchor.MiddleCenter, SignKit.Ink, true);

        lane = UiKit.CreateRect(face, "Lane");
        UiKit.Place(lane, new Vector2(0.5f, 1f), new Vector2(0f, -340f), new Vector2(360f, LaneHeight));
        AngularGraphic back = UiKit.CreatePanel(lane, "Back", SignKit.Night);
        UiKit.Stretch(back);
        lane.gameObject.AddComponent<RectMask2D>();
        tapFlash = UiKit.CreatePanel(lane, "TapFlash", Color.clear);
        PlaceInLane(tapFlash.rectTransform, new Vector2(360f, 44f), LineY);
        AngularGraphic line = UiKit.CreatePanel(lane, "Line", Color.white);
        PlaceInLane(line.rectTransform, new Vector2(360f, 6f), LineY);
        for (int index = 0; index < NoteCount; index++)
        {
            RoundedRectGraphic note = SignKit.CreateRoundedRect(lane, $"Note_{index}", Color.white, 12f);
            notes[index] = note.rectTransform;
            PlaceInLane(notes[index], new Vector2(300f, 24f), LineY);
        }

        buttonRow = UiKit.CreateRect(face, "Buttons");
        UiKit.Place(buttonRow, new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(1000f, 84f));

        Text footer = SignKit.CreateText(face, "Footer", "ESC / B  戻る        測定中のタップ： D F J K / SPACE ・ LB RB LT RT / A", 17, TextAnchor.MiddleCenter, SignKit.InkDim, true);
        UiKit.Place(footer, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1000f, 28f));

        click = ChartEditorPlayback.CreateClapClip();
        for (int index = 0; index < voices.Length; index++)
        {
            voices[index] = gameObject.AddComponent<AudioSource>();
            voices[index].playOnAwake = false;
            voices[index].spatialBlend = 0f;
            voices[index].clip = click;
        }

        gameObject.SetActive(false);
    }

    public void Open(Color color)
    {
        lineColor = color;
        band.color = color;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        ShowChoose(0);
    }

    public void Close()
    {
        StopClicks();
        gameObject.SetActive(false);

        // The press that closed it must not also act on the settings behind.
        MenuInput.Consume();
        Closed?.Invoke();
    }

    private void OnDestroy()
    {
        if (click != null)
        {
            Destroy(click);
        }
    }

    // ---- Steps ----------------------------------------------------------------------

    private void ShowChoose(int choice)
    {
        StopClicks();
        step = Step.Choose;
        openedAt = Time.unscaledTime;
        lane.gameObject.SetActive(false);
        message.text = "リズムに合わせてボタンを押すと、ズレを測って自動で補正します。\n"
            + "先に ① 音、そのあと ② 画面 の順に合わせるのがおすすめです。";
        PlaceBig(40, -420f, 180f);
        big.text = $"判定タイミング  {Format(GameSettings.JudgeOffsetMs)}\n表示タイミング  {Format(GameSettings.VisualOffsetMs)}";
        SetButtons(choice,
            ("① 音で合わせる", () => StartMeasure(false)),
            ("② 画面で合わせる", () => StartMeasure(true)),
            ("戻る", Close));
    }

    private void StartMeasure(bool picture)
    {
        step = Step.Measure;
        visual = picture;
        openedAt = Time.unscaledTime;
        taps.Clear();
        nextClick = 0;
        flashedAt = -1f;
        startTime = AudioSettings.dspTime + StartDelaySeconds;
        lane.gameObject.SetActive(picture);
        tapFlash.color = Color.clear;
        if (picture)
        {
            message.text = "音は鳴りません。ノーツが白い線に重なる瞬間にボタンを押してください。";
            PlaceBig(30, -262f, 60f);
            MoveNotes(-StartDelaySeconds);
        }
        else
        {
            message.text = GameSettings.MasterVolumePercent == 0
                ? "全体の音量が 0 です。音が聞こえるように、オーディオの設定を上げてください。"
                : "画面を見ずに、聞こえる音に合わせてボタンを押してください。";
            PlaceBig(96, -420f, 180f);
        }

        SetButtons(-1, ("中止（ESC / B）", () => ShowChoose(visual ? 1 : 0)));
        RefreshCount();
    }

    private void ShowResult()
    {
        StopClicks();
        step = Step.Result;
        openedAt = Time.unscaledTime;
        lane.gameObject.SetActive(false);
        PlaceBig(96, -420f, 180f);
        int again = visual ? 1 : 0;

        if (taps.Count < MinTaps)
        {
            message.text = $"タップが足りませんでした（{taps.Count} 回。{MinTaps} 回以上必要です）。";
            big.text = "測定できません";
            SetButtons(0, ("もう一度", () => StartMeasure(visual)), ("戻る", () => ShowChoose(again)));
            return;
        }

        List<double> sorted = new List<double>(taps);
        sorted.Sort();
        double median = Median(sorted);

        // Typical distance from the median: how steady the taps were.
        List<double> deviations = new List<double>();
        foreach (double tap in sorted)
        {
            deviations.Add(Math.Abs(tap - median));
        }

        deviations.Sort();
        int spreadMs = (int)Math.Round(Median(deviations) * 1000d);

        int medianMs = (int)Math.Round(median * 1000d);
        int max = visual ? GameSettings.MaxVisualOffsetMs : GameSettings.MaxJudgeOffsetMs;
        measuredMs = Mathf.Clamp(visual ? GameSettings.JudgeOffsetMs - medianMs : medianMs, -max, max);
        int current = visual ? GameSettings.VisualOffsetMs : GameSettings.JudgeOffsetMs;
        string name = visual ? "表示タイミング" : "判定タイミング";

        message.text = $"{name}  {Format(current)}  →  {Format(measuredMs)}\nばらつき ±{spreadMs} ms"
            + (spreadMs > 40 ? "（大きいので、もう一度測るのがおすすめです）" : string.Empty);
        big.text = Format(measuredMs);
        SetButtons(0,
            ("適用する", Apply),
            ("もう一度", () => StartMeasure(visual)),
            ("適用しない", () => ShowChoose(again)));
    }

    private void Apply()
    {
        if (visual)
        {
            GameSettings.VisualOffsetMs = measuredMs;
        }
        else
        {
            GameSettings.JudgeOffsetMs = measuredMs;
        }

        // On to the next thing: the picture after the sound, back out after the picture.
        ShowChoose(visual ? 2 : 1);
    }

    private static double Median(List<double> sorted)
    {
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) * 0.5d;
    }

    private static string Format(int milliseconds)
    {
        return $"{milliseconds:+0;-0;0} ms";
    }

    private void PlaceBig(int fontSize, float y, float height)
    {
        big.fontSize = fontSize;
        UiKit.Place(big, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(980f, height));
    }

    // ---- Input and the beat ---------------------------------------------------------

    private void Update()
    {
        if (Time.unscaledTime - openedAt < InputGraceSeconds)
        {
            return;
        }

        if (step == Step.Measure)
        {
            UpdateMeasure();
            return;
        }

        if (MenuInput.Cancel)
        {
            MenuInput.Consume();
            if (step == Step.Choose)
            {
                Close();
            }
            else
            {
                ShowChoose(visual ? 1 : 0);
            }

            return;
        }

        if (MenuInput.Left || MenuInput.Up)
        {
            Select((selected + buttons.Count - 1) % buttons.Count);
        }
        else if (MenuInput.Right || MenuInput.Down)
        {
            Select((selected + 1) % buttons.Count);
        }
        else if (MenuInput.Confirm)
        {
            MenuInput.Consume();
            buttons[selected].Act();
        }
    }

    private void UpdateMeasure()
    {
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        if ((keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame))
            || (gamepad != null && (gamepad.buttonEast.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame)))
        {
            MenuInput.Consume();
            ShowChoose(visual ? 1 : 0);
            return;
        }

        double time = AudioSettings.dspTime - startTime;
        if (visual)
        {
            MoveNotes(time);
        }
        else
        {
            ScheduleClicks(time);
        }

        if (WasTapped(keyboard, gamepad))
        {
            Tap(time);
        }

        if (taps.Count >= TapsWanted || time > MaxBeats * BeatSeconds)
        {
            ShowResult();
        }
    }

    private static bool WasTapped(Keyboard keyboard, Gamepad gamepad)
    {
        return (keyboard != null && (keyboard.dKey.wasPressedThisFrame || keyboard.fKey.wasPressedThisFrame
                || keyboard.jKey.wasPressedThisFrame || keyboard.kKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            || (gamepad != null && (gamepad.leftShoulder.wasPressedThisFrame || gamepad.rightShoulder.wasPressedThisFrame
                || gamepad.leftTrigger.wasPressedThisFrame || gamepad.rightTrigger.wasPressedThisFrame
                || gamepad.buttonSouth.wasPressedThisFrame));
    }

    /// <summary>Counts a press against the beat nearest to it.</summary>
    private void Tap(double time)
    {
        flashedAt = Time.unscaledTime;
        int beat = (int)Math.Round(time / BeatSeconds);
        if (beat < CountInBeats)
        {
            return;
        }

        taps.Add(time - beat * BeatSeconds);
        RefreshCount();
    }

    private void RefreshCount()
    {
        big.text = $"{taps.Count} / {TapsWanted}";
    }

    private void ScheduleClicks(double time)
    {
        double offset = GameSettings.AudioOffsetSeconds;
        while (nextClick < MaxBeats && nextClick * BeatSeconds + offset - time <= ScheduleAheadSeconds)
        {
            voices[nextClick % voices.Length].PlayScheduled(startTime + nextClick * BeatSeconds + offset);
            nextClick++;
        }
    }

    private void StopClicks()
    {
        foreach (AudioSource voice in voices)
        {
            if (voice != null)
            {
                voice.Stop();
            }
        }
    }

    /// <summary>The notes falling onto the line, one per beat.</summary>
    private void MoveNotes(double time)
    {
        // A note stays a moment past the line, so it does not vanish just as it is hit.
        int first = Math.Max(0, (int)Math.Ceiling(time / BeatSeconds - 0.15d));
        for (int index = 0; index < NoteCount; index++)
        {
            double ahead = (first + index) * BeatSeconds - time;
            notes[index].anchoredPosition = new Vector2(0f, LineY + (float)(ahead / BeatSeconds) * UnitsPerBeat);
        }

        float since = Time.unscaledTime - flashedAt;
        tapFlash.color = UiKit.WithAlpha(lineColor, flashedAt < 0f ? 0f : Mathf.Clamp01(1f - since / 0.15f));
    }

    /// <summary>Anchors a lane part by its middle, <paramref name="y"/> above the lane's bottom.</summary>
    private static void PlaceInLane(RectTransform rect, Vector2 size, float y)
    {
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(0f, y);
    }

    // ---- Buttons --------------------------------------------------------------------

    /// <param name="choice">The button selected first; -1 for none (measuring, mouse only).</param>
    private void SetButtons(int choice, params (string label, Action act)[] entries)
    {
        foreach (Choice old in buttons)
        {
            old.Face.gameObject.SetActive(false);
            Destroy(old.Face.gameObject);
        }

        buttons.Clear();
        const float width = 300f;
        const float gap = 30f;
        float left = -(entries.Length * width + (entries.Length - 1) * gap) * 0.5f + width * 0.5f;
        for (int index = 0; index < entries.Length; index++)
        {
            Choice button = new Choice { Act = entries[index].act };
            button.Face = SignKit.CreateRoundedRect(buttonRow, $"Button_{index}", SignKit.Ink, 12f);
            UiKit.Place(button.Face, new Vector2(0.5f, 0.5f), new Vector2(left + index * (width + gap), 0f), new Vector2(width, 72f));
            button.Label = SignKit.CreateText(button.Face.transform, "Label", entries[index].label, 26, TextAnchor.MiddleCenter, SignKit.Ink, true);
            UiKit.Stretch(button.Label);
            button.Label.rectTransform.anchoredPosition = new Vector2(0f, 26f * SignKit.CapRaise * 0.4f);

            int own = index;
            UiPointerRelay relay = SignKit.AddRelay(button.Face);
            relay.Entered = () =>
            {
                if (step != Step.Measure)
                {
                    Select(own);
                }
            };
            relay.Clicked = eventData =>
            {
                if (eventData.button == PointerEventData.InputButton.Left && Time.unscaledTime - openedAt >= InputGraceSeconds)
                {
                    button.Act();
                }
            };
            buttons.Add(button);
        }

        Select(choice);
    }

    private void Select(int index)
    {
        selected = index;
        for (int own = 0; own < buttons.Count; own++)
        {
            bool chosen = own == selected;
            buttons[own].Face.BorderWidth = chosen ? 0f : 3f;
            buttons[own].Face.color = SignKit.Ink;
            buttons[own].Label.color = chosen ? SignKit.OnInk : SignKit.Ink;
        }
    }
}
