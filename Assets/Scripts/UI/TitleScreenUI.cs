using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Title screen, after the hand-drawn sketch: "metro" and "Ⓐ Button" in the middle of
/// the off-white sign face, with a route map moving behind them (<see cref="TitleBackdrop"/>:
/// lines and stations growing in and running off, arrows gliding across). Confirm (or a
/// click) sweeps the map away, lifts "metro" to the top and brings in the mode select
/// (<see cref="TitleModeSelect"/>): ソロ rides the train doors to the genre select, エディター
/// to the chart editor, マルチ is not made yet. Back returns to the title, and
/// Back on the title asks to quit (<see cref="TitleQuitDialog"/>). `E` still opens
/// the chart editor straight from the title.
/// </summary>
public sealed class TitleScreenUI : MonoBehaviour
{
    private const float IntroSeconds = 0.6f;
    private const float LogoY = 60f;
    private const float HeaderY = 352f;
    private const float HeaderScale = 0.42f;
    private const float HeaderSeconds = 0.5f;
    private const float ModesDelay = 0.3f;

    private GameFlow flow;
    private CanvasGroup group;
    private TitleBackdrop backdrop;
    private TitleModeSelect modes;
    private TitleLogo mark;
    private TitleQuitDialog quit;
    private RectTransform logo;
    private CanvasGroup promptGroup;
    private RectTransform promptButton;
    private bool inModes;
    private float header;
    private float shownAt;
    private float phaseAt;

    public void Build(GameFlow gameFlow)
    {
        flow = gameFlow;
        RectTransform root = (RectTransform)transform;
        group = gameObject.AddComponent<CanvasGroup>();
        Vector2 centre = new Vector2(0.5f, 0.5f);

        // The same off-white board with a steel frame as the song select.
        RoundedRectGraphic face = SignKit.CreateBoard(root, "Board", 26f, 10f);
        SignKit.AddRelay(face).Clicked = eventData => OpenModes();

        backdrop = new TitleBackdrop();
        backdrop.Build(root);

        mark = new TitleLogo();
        mark.Build(root);
        logo = mark.Root;

        // "Ⓐ Button", as the sketch writes it, with the keyboard's key after it.
        RectTransform prompt = UiKit.CreateRect(root, "Prompt");
        UiKit.Place(prompt, centre, new Vector2(0f, -178f), new Vector2(420f, 70f));
        promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
        promptGroup.blocksRaycasts = false;
        CircleGraphic disc = SignKit.CreateCircle(prompt, "A", SignKit.Ink);
        promptButton = disc.rectTransform;
        UiKit.Place(disc, centre, new Vector2(-118f, 0f), new Vector2(60f, 60f));
        Text letter = SignKit.CreateText(disc.rectTransform, "Letter", "A", 34, TextAnchor.MiddleCenter, SignKit.Face, true);
        UiKit.Stretch(letter);
        letter.rectTransform.anchoredPosition = new Vector2(0f, 34 * SignKit.CapRaise);
        Text button = SignKit.CreateText(prompt, "Button", "Button  <size=24><color=#8C9198>/ ENTER</color></size>", 44, TextAnchor.MiddleLeft, SignKit.Ink);
        UiKit.Place(button, centre, new Vector2(84f, 44 * SignKit.CapRaise), new Vector2(340f, 70f));

        modes = new TitleModeSelect();
        modes.Build(root);
        modes.Hovered = mode =>
        {
            if (inModes && !flow.IsBusy)
            {
                modes.Select(mode);
            }
        };
        modes.Clicked = mode =>
        {
            if (inModes && !flow.IsBusy)
            {
                modes.Select(mode);
                Decide();
            }
        };

        quit = new TitleQuitDialog();
        quit.Build(root);
    }

    /// <summary>The quit confirmation is up.</summary>
    public bool IsConfirmingQuit => quit.IsOpen;

    /// <summary>Shows the title with the route map starting over.</summary>
    /// <summary>The mode select is up (rather than the logo and "Ⓐ Button").</summary>
    public bool IsInModes => inModes;

    public void Show()
    {
        gameObject.SetActive(true);
        shownAt = Time.unscaledTime;
        phaseAt = shownAt;
        group.alpha = 0f;
        inModes = false;
        header = 0f;
        backdrop.Restart();
        modes.HideInstantly();
        quit.Close();
        ApplyHeader();
    }

    /// <summary>Shows the mode select straight away (coming back from the genre select or the chart editor).</summary>
    public void ShowModes(TitleMode mode)
    {
        gameObject.SetActive(true);
        shownAt = Time.unscaledTime - IntroSeconds;
        phaseAt = Time.unscaledTime;
        group.alpha = 1f;
        inModes = true;
        header = 1f;
        backdrop.ClearInstantly();
        modes.Show(mode, 0f);
        quit.Close();
        ApplyHeader();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float deltaTime = Time.unscaledDeltaTime;
        float age = now - shownAt;
        group.alpha = Ease.OutCubic(age / IntroSeconds);

        backdrop.Tick(deltaTime);
        mark.Tick(now);
        modes.Tick(now);
        header = Mathf.MoveTowards(header, inModes ? 1f : 0f, deltaTime / HeaderSeconds);
        ApplyHeader();

        float pop = Ease.OutBack(age / IntroSeconds);
        if (!inModes && header <= 0f)
        {
            logo.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, pop);
        }

        // The prompt breathes; its button gives a small press now and then.
        float breath = 0.5f + 0.5f * Mathf.Cos(now * 3.2f);
        promptGroup.alpha = (0.55f + 0.45f * breath) * (1f - Mathf.Clamp01(header * 3f));
        quit.SetPlateAlpha(1f - Mathf.Clamp01(header * 3f));
        float press = Mathf.Repeat(now, 1.6f);
        promptButton.localScale = Vector3.one * (press < 0.12f ? 0.9f : 1f);

        if (flow.IsBusy || age < 0.2f || now - phaseAt < 0.25f)
        {
            return;
        }

        if (inModes)
        {
            UpdateModes();
            return;
        }

        if (quit.IsOpen)
        {
            quit.Tick();
            return;
        }

        if (MenuInput.Cancel)
        {
            MenuInput.Consume();
            quit.Open();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            flow.OpenChartEditor();
            return;
        }

        if (MenuInput.Confirm)
        {
            MenuInput.Consume();
            OpenModes();
        }
    }

    private void UpdateModes()
    {
        if (MenuInput.Cancel)
        {
            MenuInput.Consume();
            CloseModes();
            return;
        }

        if (MenuInput.Left)
        {
            modes.Move(-1);
        }
        else if (MenuInput.Right)
        {
            modes.Move(1);
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            TitleMode? shortcut = keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame ? TitleMode.Solo
                : keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame ? TitleMode.Multi
                : keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame ? TitleMode.Editor
                : (TitleMode?)null;
            if (shortcut.HasValue)
            {
                modes.Select(shortcut.Value);
                Decide();
                return;
            }
        }

        if (MenuInput.Confirm)
        {
            MenuInput.Consume();
            Decide();
        }
    }

    /// <summary>Title → mode select: the map sweeps off, "metro" rises, the band grows in.</summary>
    private void OpenModes()
    {
        if (inModes || flow.IsBusy || Time.unscaledTime - shownAt < 0.2f)
        {
            return;
        }

        inModes = true;
        phaseAt = Time.unscaledTime;
        backdrop.Clear();
        modes.Show(TitleMode.Solo, ModesDelay);
    }

    /// <summary>Mode select → title: the band fades, the map starts over.</summary>
    private void CloseModes()
    {
        inModes = false;
        phaseAt = Time.unscaledTime;
        modes.Hide();
        backdrop.Restart();
    }

    private void Decide()
    {
        switch (modes.Selected)
        {
            case TitleMode.Solo:
                flow.LeaveTitle();
                break;
            case TitleMode.Editor:
                flow.OpenChartEditor();
                break;
            default:
                modes.Refuse();
                break;
        }
    }

    private void ApplyHeader()
    {
        float t = Ease.InOutCubic(header);
        logo.anchoredPosition = new Vector2(0f, Mathf.Lerp(LogoY, HeaderY, t));
        logo.localScale = Vector3.one * Mathf.Lerp(1f, HeaderScale, t);
    }
}
