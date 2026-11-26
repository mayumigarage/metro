using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Runs the game from screen to screen: title, genre select, song select (route map),
/// the selected-song sign, play, pause (with the options), the countdown back into the
/// song, and the result. It builds the menu canvases, plays the transitions between
/// them (the train doors, the badge flying from a genre to its line), starts and stops
/// the clock, and is the only thing that begins a song.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class GameFlow : MonoBehaviour
{
    public enum State
    {
        Title,
        GenreSelect,
        SongSelect,
        SongConfirm,
        Playing,
        Paused,
        Resuming,
        Result,
        ChartEditor,
        SongIntro
    }

    private const float FlightSeconds = 0.6f;

    [Tooltip("曲開始前の待ち時間（秒）。この間にノーツが奥から流れてくる。")]
    [SerializeField] private float leadInSeconds = 2f;
    [Tooltip("曲決定後、ドアが開いてから曲紹介画面（まもなく）を表示しておく時間（秒）。")]
    [SerializeField] private float songIntroSeconds = 3f;
    [Tooltip("ポーズ解除時のカウントダウン（秒）。")]
    [SerializeField] private float resumeCountdownSeconds = 1.5f;
    [Tooltip("最後のノーツが終わってからリザルトを出すまでの時間（秒）。")]
    [SerializeField] private float resultDelaySeconds = 1.5f;

    private SongClock songClock;
    private ChartLoader chartLoader;
    private NoteSpawner noteSpawner;
    private ScoreTracker score;
    private RhythmJudgementUI hud;
    private LaneComboView laneCombo;
    private LaneJudgementView laneJudgement;
    private RectTransform menuRoot;
    private TitleScreenUI title;
    private GenreSelectUI genreSelect;
    private SongSelectUI songSelect;
    private SongSignUI songSign;
    private SongIntroUI songIntro;
    private SettingsScreenUI options;
    private RectTransform settingsButton;
    private PauseMenuUI pauseMenu;
    private ResultUI result;
    private TrainDoorTransition doors;
    private LineBadge countdownBadge;
    private SongChartData chart;
    private string playingLetter = "M";
    private string playingStation = "00";
    private float resumeAt;
    private bool autoPlayed;
    private bool flying;
    private bool menusDark;
    private bool resultNewRecord;

    // The line being browsed and the song last highlighted on it (a library index).
    private SongGenre genre = SongGenre.Default;
    private int browsedSong = -1;
    private string browsedStation = "01";

    // Chart editor preview: the chart played from previewStartTime instead of a library song.
    private SongChartData previewChart;
    private double previewStartTime;
    private ChartEditorScreen chartEditor;
    private bool previewFromEditor;
    private bool previewCloseKeyDown;
    private bool previewCloseKeyArmed;
    private float previewOpenedAt;

    // Chart editor game preview: the stage runs in a sidebar of the editor (SetGameViewport).
    private bool embeddedPreview;
    private bool autoPlayBeforePreview;
    private Rect appliedViewport = new Rect(0f, 0f, 1f, 1f);
    private Vector2Int viewportScreen;

    public State CurrentState { get; private set; }

    /// <summary>True while the chart editor's game preview sidebar is playing a preview.</summary>
    public bool IsEmbeddedPreview => embeddedPreview;

    /// <summary>Song time of the running preview (the editor's playhead follows it).</summary>
    public double PreviewSongTime => songClock.SongTime;

    /// <summary>True while the song being played is a chart editor preview.</summary>
    public bool IsPreview => previewChart != null;

    /// <summary>A transition or the options panel has the screen: menus must not take input.</summary>
    public bool IsBusy => flying || (doors != null && doors.IsRunning) || (options != null && options.IsOpen);

    private void Awake()
    {
        GameSettings.Load();
        songClock = FindFirstObjectByType<SongClock>();
        chartLoader = FindFirstObjectByType<ChartLoader>();
        noteSpawner = FindFirstObjectByType<NoteSpawner>();
        hud = FindFirstObjectByType<RhythmJudgementUI>();
        score = GetOrAdd<ScoreTracker>();
        laneCombo = GetOrAdd<LaneComboView>();
        laneJudgement = GetOrAdd<LaneJudgementView>();
        GetOrAdd<NoteSoundPlayer>();
        GetOrAdd<GuideBeatView>();

        // The clock waits for a song to be chosen instead of starting with the scene.
        songClock.DisableAutoStart();
        ApplyAudioSettings();
        GameSettings.Changed += ApplyAudioSettings;
    }

    private void OnDestroy()
    {
        GameSettings.Changed -= ApplyAudioSettings;
    }

    /// <summary>The audio tab's volumes: every sound (AudioListener) and the song's music.</summary>
    private void ApplyAudioSettings()
    {
        AudioListener.volume = GameSettings.MasterVolumePercent / 100f;
        songClock.SetMusicVolume(GameSettings.MusicVolumePercent / 100f);
    }

    private void Start()
    {
        // Built here rather than in Awake: this Awake runs before ChartLoader's,
        // and the song lists need the loaded library.
        BuildMenus();
        chartEditor = gameObject.AddComponent<ChartEditorScreen>();
        chartEditor.enabled = false;

        StopSongAndHideGame();
        title.Show();
        CurrentState = State.Title;
    }

    private void Update()
    {
        MenuInput.Poll();
        if (ClosePreviewByKey())
        {
            return;
        }

        // Tab / Y opens the settings on the screens that have no handling of their own
        // (the song select and the song sign call OpenOptions themselves).
        if (MenuInput.Option && !IsBusy && ((CurrentState == State.Title && !title.IsConfirmingQuit) || CurrentState == State.GenreSelect
            || CurrentState == State.Paused || CurrentState == State.Result))
        {
            MenuInput.Consume();
            OpenOptions();
        }

        switch (CurrentState)
        {
            case State.Playing:
                UpdatePlaying();
                break;
            case State.Resuming:
                UpdateResuming();
                break;
            case State.ChartEditor:
                if (embeddedPreview && noteSpawner.IsFinished && songClock.SongTime >= chart.EndTime + 1d)
                {
                    StopEmbeddedPreview();
                }

                break;
        }
    }

    /// <summary>
    /// P closes the chart editor's full-screen preview. It acts on the release of a press
    /// made during the preview: the P that opened it does not close it, and the editor is
    /// not back yet to take this press as "preview" again. The editor reads that opening P
    /// from IMGUI, and the Input System may only report it a frame later, so presses count
    /// once the key has been seen up a moment after the preview opened.
    /// </summary>
    private bool ClosePreviewByKey()
    {
        Keyboard keyboard = Keyboard.current;
        if (!previewFromEditor || IsBusy || keyboard == null)
        {
            previewCloseKeyDown = false;
            return false;
        }

        if (!previewCloseKeyArmed)
        {
            previewCloseKeyArmed = Time.unscaledTime >= previewOpenedAt + 0.15f
                && !keyboard.pKey.isPressed && !keyboard.pKey.wasReleasedThisFrame;
            return false;
        }

        if (keyboard.pKey.wasPressedThisFrame)
        {
            previewCloseKeyDown = true;
        }

        if (!previewCloseKeyDown || !keyboard.pKey.wasReleasedThisFrame)
        {
            return false;
        }

        previewCloseKeyDown = false;
        ReturnToEditor();
        return true;
    }

    // ---- Menus ----------------------------------------------------------------------

    /// <summary>Title (mode select: ソロ) → genre select, through the train doors.</summary>
    public void LeaveTitle()
    {
        if (CurrentState != State.Title || IsBusy)
        {
            return;
        }

        doors.Run(SignKit.MetroRed, "M", "00", "M", "01", () =>
        {
            title.Hide();
            genreSelect.Show(genre);
            CurrentState = State.GenreSelect;
        });
    }

    /// <summary>Title (mode select: エディター, or `E`) → the chart editor, at once (no train doors).</summary>
    public void OpenChartEditor()
    {
        if (CurrentState != State.Title || IsBusy)
        {
            return;
        }

        title.Hide();
        chartEditor.Open(this);
        CurrentState = State.ChartEditor;
    }

    /// <summary>The chart editor's way back to the title screen's mode select.</summary>
    public void CloseChartEditor()
    {
        if (CurrentState != State.ChartEditor || IsBusy)
        {
            return;
        }

        StopEmbeddedPreview();
        SetGameViewport(FullViewport);
        chartEditor.enabled = false;
        // Charts saved in the editor appear in the song select; library indices may have moved.
        chartLoader.LoadLibrary();
        chartLoader.Select(0, false);
        browsedSong = -1;
        title.ShowModes(TitleMode.Editor);
        CurrentState = State.Title;
    }

    /// <summary>
    /// The chart editor's game preview: the stage (and the HUD of a preview) is drawn into
    /// <paramref name="viewport"/> (normalized, origin bottom left, like <see cref="Camera.rect"/>);
    /// the full rect gives the game the whole screen again. Call it every frame with the wanted rect.
    /// </summary>
    public void SetGameViewport(Rect viewport)
    {
        Vector2Int screen = new Vector2Int(Screen.width, Screen.height);
        if (appliedViewport == viewport && viewportScreen == screen)
        {
            return;
        }

        appliedViewport = viewport;
        viewportScreen = screen;
        hud.SetViewport(viewport);
    }

    private static readonly Rect FullViewport = new Rect(0f, 0f, 1f, 1f);

    /// <summary>
    /// Plays <paramref name="source"/> from <paramref name="fromSongTime"/> in the chart editor's game preview sidebar, on auto play,
    /// without leaving the editor. The run-up is as long as the notes take to fly in, so the first
    /// notes come in from the far end instead of popping up.
    /// </summary>
    public void StartEmbeddedPreview(SongChartData source, double fromSongTime)
    {
        if (CurrentState != State.ChartEditor || IsBusy || source == null)
        {
            return;
        }

        StopEmbeddedPreview();
        embeddedPreview = true;
        autoPlayBeforePreview = GameSettings.AutoPlay;
        GameSettings.AutoPlay = true;
        previewChart = source;
        previewStartTime = Math.Max(0d, fromSongTime);
        Play(CreatePreviewChart(source, previewStartTime), previewStartTime, noteSpawner.TravelTimeSeconds);
        CurrentState = State.ChartEditor;
    }

    public void StopEmbeddedPreview()
    {
        if (!embeddedPreview)
        {
            return;
        }

        embeddedPreview = false;
        previewChart = null;
        GameSettings.AutoPlay = autoPlayBeforePreview;
        StopSongAndHideGame();
    }

    /// <summary>Plays a chart of the editor from <paramref name="fromSongTime"/>; quitting the pause menu or the result returns to the editor.</summary>
    public void PreviewFromEditor(SongChartData source, double fromSongTime)
    {
        if (CurrentState != State.ChartEditor || IsBusy)
        {
            return;
        }

        StopEmbeddedPreview();
        SetGameViewport(FullViewport);
        chartEditor.enabled = false;
        previewFromEditor = true;
        previewCloseKeyArmed = false;
        previewOpenedAt = Time.unscaledTime;
        StartPreview(source, fromSongTime);
    }

    private void ReturnToEditor()
    {
        previewFromEditor = false;
        previewChart = null;
        StopSongAndHideGame();
        HideMenuScreens();
        chartEditor.Open(this);
        CurrentState = State.ChartEditor;
    }

    public void BackToTitle()
    {
        if (CurrentState != State.GenreSelect || IsBusy)
        {
            return;
        }

        genreSelect.Hide();
        title.ShowModes(TitleMode.Solo);
        CurrentState = State.Title;
    }

    /// <summary>Genre select → song select: the folder's badge flies onto its line.</summary>
    public void OpenGenre(SongGenre selectedGenre)
    {
        if (CurrentState != State.GenreSelect || IsBusy || chartLoader.GetSongIndices(selectedGenre).Count == 0)
        {
            return;
        }

        if (genre != selectedGenre)
        {
            browsedSong = -1;
        }

        genre = selectedGenre;
        StartCoroutine(FlyIntoLine(genreSelect.SelectedBadge));
    }

    public void BackToGenreSelect()
    {
        if (CurrentState != State.SongSelect || IsBusy)
        {
            return;
        }

        browsedSong = songSelect.SelectedSongIndex;
        songSelect.Hide();
        genreSelect.Show(genre);
        CurrentState = State.GenreSelect;
    }

    /// <summary>Song select → the selected-song sign.</summary>
    public void ConfirmSong(int songIndex, string stationNumber)
    {
        if (CurrentState != State.SongSelect || IsBusy || songIndex < 0 || songIndex >= chartLoader.Songs.Count)
        {
            return;
        }

        browsedSong = songIndex;
        browsedStation = stationNumber;
        songSign.Show(chartLoader.Songs[songIndex], genre, stationNumber);
        CurrentState = State.SongConfirm;
    }

    public void CancelSong()
    {
        if (CurrentState != State.SongConfirm || IsBusy)
        {
            return;
        }

        songSign.Hide();
        CurrentState = State.SongSelect;
    }

    /// <summary>
    /// The sign's start: the doors close on the menus and open on the song's intro screen
    /// ("まもなく"), which holds for <see cref="songIntroSeconds"/> before the song begins.
    /// </summary>
    public void DepartSong()
    {
        if (CurrentState != State.SongConfirm || IsBusy)
        {
            return;
        }

        int songIndex = browsedSong;
        string station = browsedStation;
        doors.Run(GetBrowsedLineColor(), genre.Letter, station, genre.Letter, station, () =>
        {
            HideMenuScreens();
            songIntro.Show(chartLoader.Songs[songIndex], genre, station);
            CurrentState = State.SongIntro;
        }, () => StartCoroutine(HoldSongIntro(songIndex)));
    }

    private IEnumerator HoldSongIntro(int songIndex)
    {
        songIntro.BeginHold(songIntroSeconds);
        float end = Time.unscaledTime + songIntroSeconds;

        // Music imported in a build is a file: read it now, and keep the intro up until it is there.
        yield return LoadSongAudio(chartLoader.Songs[songIndex].audio);
        while (Time.unscaledTime < end)
        {
            yield return null;
        }

        // The song starts under the intro, which fades out over the stage.
        StartSong(songIndex);
        songIntro.FadeOut();
    }

    /// <summary>
    /// Loads the clip of a <c>file:</c> audio value for <see cref="Play"/> (nothing to do for a Resources
    /// clip or a clip already loaded). A file that cannot be read leaves the song silent.
    /// </summary>
    private IEnumerator LoadSongAudio(string audio)
    {
        if (!AudioImport.IsFileAudio(audio) || AudioImport.GetClip(audio) != null)
        {
            yield break;
        }

        yield return AudioImport.LoadFile(audio, (clip, error) =>
        {
            if (clip != null)
            {
                AudioImport.SetFileClip(audio, clip);
            }
            else
            {
                Debug.LogWarning(error, this);
            }
        });
    }

    /// <summary>
    /// The full-screen settings, from the settings button at the top right of every menu screen
    /// (not the chart editor) or Tab / Y.
    /// </summary>
    public void OpenOptions()
    {
        if (!IsBusy && CanOpenSettings)
        {
            options.Open(GetSettingsLineColor());
        }
    }

    private bool CanOpenSettings => CurrentState == State.Title || CurrentState == State.GenreSelect || CurrentState == State.SongSelect
        || CurrentState == State.SongConfirm || CurrentState == State.Paused || CurrentState == State.Result;

    /// <summary>The line colour of the song in view: the highlighted station, the sign's song or the song being played.</summary>
    private Color GetSettingsLineColor()
    {
        switch (CurrentState)
        {
            case State.SongSelect:
                return songSelect.SelectedColor;
            case State.SongConfirm:
                return GetBrowsedLineColor();
            case State.Paused:
            case State.Result:
                return chart != null ? chart.Difficulty.Color : SignKit.MetroRed;
            default:
                return SignKit.MetroRed;
        }
    }

    // ---- Songs ----------------------------------------------------------------------

    public void StartSong(int songIndex)
    {
        if (chartLoader.Songs.Count == 0)
        {
            return;
        }

        previewChart = null;
        Play(chartLoader.Select(songIndex, GameSettings.Mirror), 0d);
    }

    /// <summary>
    /// Chart editor preview: plays <paramref name="source"/> as written (no mirror) from
    /// <paramref name="fromSongTime"/>, leaving out everything that starts before it. The
    /// score of a preview is never saved as a best.
    /// </summary>
    public void StartPreview(SongChartData source, double fromSongTime)
    {
        if (source == null)
        {
            return;
        }

        previewChart = source;
        previewStartTime = Math.Max(0d, fromSongTime);
        Play(CreatePreviewChart(source, previewStartTime), previewStartTime);
    }

    private void Play(SongChartData songChart, double fromSongTime, double leadIn = -1d)
    {
        if (leadIn < 0d)
        {
            leadIn = leadInSeconds;
        }

        chart = songChart;
        autoPlayed = GameSettings.AutoPlay;
        score.Begin(chart);
        noteSpawner.Begin(chart);

        AudioClip clip = AudioImport.GetClip(chart.audio);
        if (clip == null && !string.IsNullOrWhiteSpace(chart.audio))
        {
            Debug.LogWarning($"Chart '{chart.id}': audio '{chart.audio}' not found or not loaded; playing silently.", this);
        }

        songClock.StartSong(leadIn, clip, fromSongTime, chart.audioOffset + GameSettings.AudioOffsetSeconds);

        HideMenuScreens();
        pauseMenu.Hide();
        result.Hide();
        countdownBadge.gameObject.SetActive(false);
        playingLetter = chart.Genre.Letter;
        playingStation = GetStationNumber(chart.id, chart.Genre);
        countdownBadge.SetLineColor(chart.Difficulty.Color);
        hud.SetVisible(true);
        laneCombo.SetVisible(true);
        laneJudgement.SetVisible(true);
        laneCombo.SetLineColor(chart.Difficulty.Color);
        hud.BeginSong(chart, playingLetter, playingStation);
        hud.ShowMessage("READY", (float)leadIn * 0.75f);
        CurrentState = State.Playing;
    }

    public void Pause()
    {
        if (CurrentState != State.Playing)
        {
            return;
        }

        MenuInput.Consume();
        songClock.Pause();
        pauseMenu.Show(chart, playingLetter, playingStation);
        CurrentState = State.Paused;
    }

    /// <summary>Leaves the pause menu; the song continues after a short countdown.</summary>
    public void Resume()
    {
        if (CurrentState != State.Paused || IsBusy)
        {
            return;
        }

        pauseMenu.Hide();
        countdownBadge.gameObject.SetActive(true);
        resumeAt = Time.unscaledTime + resumeCountdownSeconds;
        CurrentState = State.Resuming;
        UpdateResuming();
    }

    public void Retry()
    {
        if (IsBusy)
        {
            return;
        }

        if (previewChart != null)
        {
            StartPreview(previewChart, previewStartTime);
            return;
        }

        StartSong(chartLoader.SelectedIndex);
    }

    /// <summary>From the pause menu or the result, back to the line through the train doors (an editor preview goes straight back to the editor).</summary>
    public void QuitToSongSelect()
    {
        if (IsBusy)
        {
            return;
        }

        if (previewFromEditor)
        {
            ReturnToEditor();
            return;
        }

        if (previewChart != null)
        {
            // A preview returns to the line of the chart it previewed.
            genre = previewChart.Genre;
            browsedSong = FindSong(previewChart.id);
        }

        string station = browsedStation;
        doors.Run(GetBrowsedLineColor(), genre.Letter, station, genre.Letter, station, EnterSongSelect);
    }

    /// <summary>Line colour of the doors: the difficulty colour of the song browsed last (the genre's colour when there is none).</summary>
    private Color GetBrowsedLineColor()
    {
        return browsedSong >= 0 && browsedSong < chartLoader.Songs.Count ? chartLoader.Songs[browsedSong].Difficulty.Color : genre.Color;
    }

    private void EnterSongSelect()
    {
        previewChart = null;
        StopSongAndHideGame();
        HideMenuScreens();
        if (chartLoader.GetSongIndices(genre).Count == 0)
        {
            genreSelect.Show(genre);
            CurrentState = State.GenreSelect;
            return;
        }

        songSelect.Show(genre, browsedSong);
        CurrentState = State.SongSelect;
    }

    private void StopSongAndHideGame()
    {
        songClock.StopSong();
        noteSpawner.Clear();
        score.Begin(null);
        hud.SetVisible(false);
        laneCombo.SetVisible(false);
        laneJudgement.SetVisible(false);
        pauseMenu.Hide();
        result.Hide();
        countdownBadge.gameObject.SetActive(false);
    }

    private void HideMenuScreens()
    {
        title.Hide();
        genreSelect.Hide();
        songSelect.Hide();
        songSign.Hide();
        options.Close();
    }

    private void UpdatePlaying()
    {
        autoPlayed |= GameSettings.AutoPlay;
        if (MenuInput.Pause)
        {
            Pause();
            return;
        }

        if (noteSpawner.IsFinished && songClock.SongTime >= chart.EndTime + resultDelaySeconds)
        {
            ShowResult();
        }
    }

    private void UpdateResuming()
    {
        if (MenuInput.Pause)
        {
            // Paused again during the countdown.
            MenuInput.Consume();
            countdownBadge.gameObject.SetActive(false);
            pauseMenu.Show(chart, playingLetter, playingStation);
            CurrentState = State.Paused;
            return;
        }

        float remaining = resumeAt - Time.unscaledTime;
        if (remaining > 0f)
        {
            // Counts 3, 2, 1 over the countdown, however long it is set to be, in a
            // station badge that pops in with each number.
            float count = remaining / resumeCountdownSeconds * 3f;
            countdownBadge.SetCode(Mathf.Clamp(Mathf.CeilToInt(count), 1, 3).ToString(), null);
            float sinceTick = 1f - (count - Mathf.Floor(count));
            countdownBadge.Rect.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, Ease.OutBack(sinceTick / 0.35f));
            return;
        }

        countdownBadge.gameObject.SetActive(false);
        songClock.Resume();
        CurrentState = State.Playing;
    }

    private void ShowResult()
    {
        songClock.StopSong();
        noteSpawner.Clear();
        bool newRecord = !autoPlayed && previewChart == null && ScoreTracker.TrySaveBest(chart.id, score.Percent);
        resultNewRecord = newRecord;
        hud.SetVisible(false);
        laneCombo.SetVisible(false);
        laneJudgement.SetVisible(false);
        result.Show(chart, score, newRecord, autoPlayed, playingLetter, playingStation);
        CurrentState = State.Result;
    }

    // ---- Transitions ----------------------------------------------------------------

    /// <summary>
    /// The chosen folder's badge flies to the selected station of its line while the
    /// genre sign fades out and the route map fades in, its number turning from the
    /// song count into the station number on the way.
    /// </summary>
    private IEnumerator FlyIntoLine(LineBadge from)
    {
        flying = true;
        songSelect.Show(genre, browsedSong);
        songSelect.SetSelectedStationHidden(true);
        songSelect.Group.alpha = 0f;

        Vector2 start = SignKit.ToLocal(menuRoot, from.Rect, out float startSize);
        LineBadge target = songSelect.SelectedBadge;
        Vector2 end = SignKit.ToLocal(menuRoot, target.Rect, out float endSize);
        // Out sideways along the genre row first, then onto the line, like the sketch's arrow.
        Vector2 control = new Vector2(end.x, start.y);

        LineBadge flyer = LineBadge.Create(menuRoot, "FlyingBadge", genre.Color, startSize);
        flyer.SetCode(genre.Letter, SongGenre.FormatStation(chartLoader.GetSongCount(genre)));
        flyer.Rect.anchoredPosition = start;
        flyer.SetHalo(0.6f);

        for (float elapsed = 0f; elapsed < FlightSeconds; elapsed += Time.unscaledDeltaTime)
        {
            float t = elapsed / FlightSeconds;
            float p = Ease.InOutCubic(t);
            float u = 1f - p;
            flyer.Rect.anchoredPosition = u * u * start + 2f * u * p * control + p * p * end;
            flyer.SetDiameter(Mathf.Lerp(startSize, endSize, p));
            // The folder's colour turns into the selected song's difficulty colour on the way.
            flyer.SetLineColor(Color.Lerp(genre.Color, songSelect.SelectedColor, p));
            if (p >= 0.5f)
            {
                flyer.SetCode(genre.Letter, songSelect.SelectedStationNumber);
            }

            genreSelect.Group.alpha = 1f - Mathf.Clamp01(t * 2.2f);
            songSelect.Group.alpha = Mathf.Clamp01((t - 0.2f) / 0.6f);
            yield return null;
        }

        Destroy(flyer.gameObject);
        songSelect.Group.alpha = 1f;
        songSelect.SetSelectedStationHidden(false);
        genreSelect.Hide();
        genreSelect.Group.alpha = 1f;
        CurrentState = State.SongSelect;
        flying = false;
    }

    private void LateUpdate()
    {
        // After the menus' own input, so the list that changed the theme has finished with it.
        if (menusDark != GameSettings.DarkMode && CanOpenSettings && !flying && !doors.IsRunning)
        {
            RebuildMenus();
        }

        // The settings button: on the menu screens while nothing else has the screen, but not on
        // the title (its logo and the ソロ / マルチ mode select) or the result. Tab / Y still opens them there.
        settingsButton.gameObject.SetActive(CanOpenSettings && !IsBusy && CurrentState != State.Title && CurrentState != State.Result);
    }

    /// <summary>
    /// The menus take their colours when they are built, so a THEME change builds them again and
    /// puts back what was on screen.
    /// </summary>
    private void RebuildMenus()
    {
        bool optionsOpen = options.IsOpen;
        int optionsTab = options.CurrentTab;
        int optionsCard = options.SelectedCard;
        bool titleModes = title.IsInModes;
        int pauseRow = pauseMenu.SelectedRow;
        if (CurrentState == State.SongSelect)
        {
            browsedSong = songSelect.SelectedSongIndex;
        }

        GameObject oldMenus = menuRoot.gameObject;
        GameObject oldDoors = doors.gameObject;
        oldMenus.SetActive(false);
        Destroy(oldMenus);
        Destroy(oldDoors);
        BuildMenus();

        switch (CurrentState)
        {
            case State.Title:
                if (titleModes)
                {
                    title.ShowModes(TitleMode.Solo);
                }
                else
                {
                    title.Show();
                }

                break;
            case State.GenreSelect:
                genreSelect.Show(genre);
                break;
            case State.SongSelect:
                songSelect.Show(genre, browsedSong);
                break;
            case State.SongConfirm:
                songSelect.Show(genre, browsedSong);
                songSign.Show(chartLoader.Songs[browsedSong], genre, browsedStation);
                break;
            case State.Paused:
                pauseMenu.Show(chart, playingLetter, playingStation);
                pauseMenu.SelectedRow = pauseRow;
                break;
            case State.Result:
                result.Show(chart, score, resultNewRecord, autoPlayed, playingLetter, playingStation);
                break;
        }

        if (optionsOpen)
        {
            options.Open(GetSettingsLineColor(), optionsTab, optionsCard);
        }
    }

    private void BuildMenus()
    {
        menusDark = GameSettings.DarkMode;
        UiKit.EnsureEventSystem();
        Canvas canvas = UiKit.CreateCanvas(transform, "MetroMenus", 200);
        menuRoot = (RectTransform)canvas.transform;

        title = CreateScreen<TitleScreenUI>(menuRoot, "Title");
        title.Build(this);
        genreSelect = CreateScreen<GenreSelectUI>(menuRoot, "GenreSelect");
        genreSelect.Build(this, chartLoader);
        songSelect = CreateScreen<SongSelectUI>(menuRoot, "SongSelect");
        songSelect.Build(this, chartLoader);
        songSign = CreateScreen<SongSignUI>(menuRoot, "SongSign");
        songSign.Build(this);
        songIntro = CreateScreen<SongIntroUI>(menuRoot, "SongIntro");
        songIntro.Build();
        pauseMenu = CreateScreen<PauseMenuUI>(menuRoot, "PauseMenu");
        pauseMenu.Build(this);
        pauseMenu.Hide();
        result = CreateScreen<ResultUI>(menuRoot, "Result");
        result.Build(this);
        result.Hide();

        countdownBadge = LineBadge.Create(menuRoot, "Countdown", SignKit.MetroRed, 260f);
        countdownBadge.Group.blocksRaycasts = false;
        countdownBadge.gameObject.SetActive(false);

        // The settings button and the settings board above every screen, pause and result included.
        settingsButton = SettingsButton.CreateGear(menuRoot, "SettingsButton");
        settingsButton.GetComponent<UiPointerRelay>().Clicked = eventData =>
        {
            if (eventData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left)
            {
                OpenOptions();
            }
        };
        options = CreateScreen<SettingsScreenUI>(menuRoot, "Settings");
        options.Build(this);

        // The doors cover everything, the in-song HUD included.
        Canvas doorCanvas = UiKit.CreateCanvas(transform, "MetroDoors", 500);
        doors = doorCanvas.gameObject.AddComponent<TrainDoorTransition>();
        doors.Build();

        HideMenuScreens();
        songIntro.Hide();
    }

    /// <summary>
    /// Station number ("01"...) of the song with chart id <paramref name="id"/> on the line of
    /// <paramref name="songGenre"/>, as the song select numbers it; "00" when it is not in the library.
    /// </summary>
    private string GetStationNumber(string id, SongGenre songGenre)
    {
        int songIndex = FindSong(id);
        List<List<int>> groups = chartLoader.GetSongGroups(songGenre);
        for (int station = 0; station < groups.Count; station++)
        {
            if (groups[station].Contains(songIndex))
            {
                return SongGenre.FormatStation(station + 1);
            }
        }

        return SongGenre.FormatStation(0);
    }

    /// <summary>Library index of the song with chart id <paramref name="id"/>, -1 if it is not in the library.</summary>
    private int FindSong(string id)
    {
        for (int index = 0; index < chartLoader.Songs.Count; index++)
        {
            if (chartLoader.Songs[index].id == id)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>A copy of the chart with only the notes and lasers that start at or after <paramref name="fromSongTime"/>.</summary>
    private static SongChartData CreatePreviewChart(SongChartData source, double fromSongTime)
    {
        // A little slack so a note placed exactly on the cursor is kept.
        double from = fromSongTime - 0.001d;
        List<TapNoteData> notes = new List<TapNoteData>();
        foreach (TapNoteData note in source.notes)
        {
            if (note.time >= from)
            {
                notes.Add(note);
            }
        }

        List<LaserNoteData> lasers = new List<LaserNoteData>();
        foreach (LaserNoteData laser in source.lasers)
        {
            if (laser.StartTime >= from)
            {
                lasers.Add(laser);
            }
        }

        return new SongChartData
        {
            id = source.id,
            title = source.title,
            artist = source.artist,
            bpm = source.bpm,
            offset = source.offset,
            level = source.level,
            difficulty = source.difficulty,
            genre = source.genre,
            audio = source.audio,
            audioOffset = source.audioOffset,
            jacket = source.jacket,
            guides = System.Array.FindAll(source.guides, time => time >= from),
            notes = notes.ToArray(),
            lasers = lasers.ToArray()
        };
    }

    private static T CreateScreen<T>(Transform parent, string name) where T : Component
    {
        RectTransform rect = UiKit.CreateRect(parent, name);
        UiKit.Stretch(rect);
        return rect.gameObject.AddComponent<T>();
    }

    private T GetOrAdd<T>() where T : Component
    {
        T component = FindFirstObjectByType<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }
}
