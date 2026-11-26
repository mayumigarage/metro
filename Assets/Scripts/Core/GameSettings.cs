using System;
using UnityEngine;

/// <summary>
/// Player options, kept in PlayerPrefs. Everything that reads an option reads it
/// from here each frame (or listens to <see cref="Changed"/>), so a change made in
/// the pause menu applies at once unless noted otherwise.
/// </summary>
public static class GameSettings
{
    public const float MinHiSpeed = 0.5f;
    public const float MaxHiSpeed = 5f;
    public const int MaxJudgeOffsetMs = 200;
    public const int MaxVisualOffsetMs = 200;
    public const int MinStickAngle = 15;
    public const int MaxStickAngle = 60;

    private const string Prefix = "metro.option.";

    private static bool loaded;
    private static float hiSpeed = 1f;
    private static int judgeOffsetMs;
    private static int visualOffsetMs;
    private static bool mirror;
    private static bool autoPlay;
    private static bool showFastSlow = true;
    private static bool showTimingDetail = true;
    private static int bloomPercent = 100;
    private static int stageBrightnessPercent = 100;
    private static int stickAngleDegrees = 30;
    private static int gridIntervalIndex = DefaultGridIntervalIndex;
    private static bool approachAccelerate;
    private static bool darkMode;
    private static int approachStrength = DefaultApproachStrength;
    private static int masterVolumePercent = 100;
    private static int musicVolumePercent = 100;
    private static int audioOffsetMs;
    private static bool noteSound;
    private static int noteSoundVolumePercent = 60;

    public const int MaxAudioOffsetMs = 300;

    public const int MinApproachStrength = 1;
    public const int MaxApproachStrength = 10;
    private const int DefaultApproachStrength = 6;

    /// <summary>GRID choices in beats; 0 = off.</summary>
    public static readonly float[] GridIntervalBeats = { 0f, 0.25f, 0.5f, 1f, 2f, 4f };
    private const int DefaultGridIntervalIndex = 3;

    /// <summary>Shown when no player name has been set.</summary>
    public const string DefaultPlayerName = "PLAYER";

    /// <summary>Longest player name, in characters.</summary>
    public const int MaxPlayerNameLength = 16;

    /// <summary>Raised after any option changed.</summary>
    public static event Action Changed;

    /// <summary>Scroll speed multiplier: the travel time of notes and lasers is divided by it.</summary>
    public static float HiSpeed
    {
        get { Load(); return hiSpeed; }
        set { Load(); Apply(ref hiSpeed, Mathf.Clamp(Mathf.Round(value * 10f) / 10f, MinHiSpeed, MaxHiSpeed)); }
    }

    /// <summary>Shifts button / flick judgement. Positive suits a player who presses late.</summary>
    public static int JudgeOffsetMs
    {
        get { Load(); return judgeOffsetMs; }
        set { Load(); Apply(ref judgeOffsetMs, Mathf.Clamp(value, -MaxJudgeOffsetMs, MaxJudgeOffsetMs)); }
    }

    public static double JudgeOffsetSeconds => JudgeOffsetMs / 1000d;

    /// <summary>
    /// Draws notes, lasers and the tunnel frames later (negative = earlier) than the song
    /// clock, for the display latency of the player's screen. Judgement is not moved.
    /// </summary>
    public static int VisualOffsetMs
    {
        get { Load(); return visualOffsetMs; }
        set { Load(); Apply(ref visualOffsetMs, Mathf.Clamp(value, -MaxVisualOffsetMs, MaxVisualOffsetMs)); }
    }

    public static double VisualOffsetSeconds => VisualOffsetMs / 1000d;

    /// <summary>
    /// false: notes and lasers move at a constant speed along the tunnel (they only look
    /// faster near because of the perspective). true: they appear earlier, crawl at the far
    /// end and speed up to the LINEAR speed at the judgement line
    /// (<see cref="RhythmStageView.ApplyApproachCurve"/>). They reach the line on the beat either way.
    /// </summary>
    public static bool ApproachAccelerate
    {
        get { Load(); return approachAccelerate; }
        set { Load(); Apply(ref approachAccelerate, value); }
    }

    /// <summary>
    /// 1..10: how slow ACCEL is at the far end (progress = linear ^ k with k = 1 + 0.2 * strength,
    /// and notes are in view k times longer; the speed at the judgement line stays the LINEAR one).
    /// Only used while <see cref="ApproachAccelerate"/> is on.
    /// </summary>
    public static int ApproachStrength
    {
        get { Load(); return approachStrength; }
        set { Load(); Apply(ref approachStrength, Mathf.Clamp(value, MinApproachStrength, MaxApproachStrength)); }
    }

    /// <summary>
    /// The menus' theme: false = black lettering on off-white signs, true = light lettering on
    /// black ones (<see cref="SignKit"/>). GameFlow rebuilds the menus when it changes.
    /// </summary>
    public static bool DarkMode
    {
        get { Load(); return darkMode; }
        set { Load(); Apply(ref darkMode, value); }
    }

    /// <summary>Swaps left and right in the chart. Read when a song starts.</summary>
    public static bool Mirror
    {
        get { Load(); return mirror; }
        set { Load(); Apply(ref mirror, value); }
    }

    /// <summary>The game plays itself (every note Perfect). Such a play is not recorded as a best.</summary>
    public static bool AutoPlay
    {
        get { Load(); return autoPlay; }
        set { Load(); Apply(ref autoPlay, value); }
    }

    public static bool ShowFastSlow
    {
        get { Load(); return showFastSlow; }
        set { Load(); Apply(ref showFastSlow, value); }
    }

    public static bool ShowTimingDetail
    {
        get { Load(); return showTimingDetail; }
        set { Load(); Apply(ref showTimingDetail, value); }
    }

    /// <summary>0..200: scales the bloom on notes and lasers. 0 switches it off.</summary>
    public static int BloomPercent
    {
        get { Load(); return bloomPercent; }
        set { Load(); Apply(ref bloomPercent, Mathf.Clamp(value, 0, 200)); }
    }

    /// <summary>20..100: how bright the lane surfaces and the far wall are drawn.</summary>
    public static int StageBrightnessPercent
    {
        get { Load(); return stageBrightnessPercent; }
        set { Load(); Apply(ref stageBrightnessPercent, Mathf.Clamp(value, 20, 100)); }
    }

    /// <summary>
    /// How far (degrees, either way) the stick may lean off the direction a laser
    /// moves in, or a directional note asks for, and still count as that direction.
    /// 22.5 splits the eight directions exactly; more lets neighbours overlap.
    /// </summary>
    public static int StickAngleDegrees
    {
        get { Load(); return stickAngleDegrees; }
        set { Load(); Apply(ref stickAngleDegrees, Mathf.Clamp(value, MinStickAngle, MaxStickAngle)); }
    }

    /// <summary>Index into <see cref="GridIntervalBeats"/>: how far apart the scrolling tunnel frames are.</summary>
    public static int GridIntervalIndex
    {
        get { Load(); return gridIntervalIndex; }
        set { Load(); Apply(ref gridIntervalIndex, Mathf.Clamp(value, 0, GridIntervalBeats.Length - 1)); }
    }

    /// <summary>0..100: every sound of the game (AudioListener.volume, applied by GameFlow).</summary>
    public static int MasterVolumePercent
    {
        get { Load(); return masterVolumePercent; }
        set { Load(); Apply(ref masterVolumePercent, Mathf.Clamp(value, 0, 100)); }
    }

    /// <summary>0..100: the song's music (SongClock's AudioSource).</summary>
    public static int MusicVolumePercent
    {
        get { Load(); return musicVolumePercent; }
        set { Load(); Apply(ref musicVolumePercent, Mathf.Clamp(value, 0, 100)); }
    }

    /// <summary>A tick whenever a note is hit (<see cref="NoteSoundPlayer"/>). Off by default.</summary>
    public static bool NoteSound
    {
        get { Load(); return noteSound; }
        set { Load(); Apply(ref noteSound, value); }
    }

    /// <summary>0..100: how loud <see cref="NoteSound"/> is.</summary>
    public static int NoteSoundVolumePercent
    {
        get { Load(); return noteSoundVolumePercent; }
        set { Load(); Apply(ref noteSoundVolumePercent, Mathf.Clamp(value, 0, 100)); }
    }

    /// <summary>
    /// Shifts the music later on the song clock (negative = earlier), added to the chart's own
    /// audioOffset, for the output latency of the player's speakers. Read when a song starts.
    /// </summary>
    public static int AudioOffsetMs
    {
        get { Load(); return audioOffsetMs; }
        set { Load(); Apply(ref audioOffsetMs, Mathf.Clamp(value, -MaxAudioOffsetMs, MaxAudioOffsetMs)); }
    }

    public static double AudioOffsetSeconds => AudioOffsetMs / 1000d;

    /// <summary>Beats between tunnel frames (0 = no frames).</summary>
    public static float GridBeats => GridIntervalBeats[GridIntervalIndex];

    /// <summary>LASER COLOR choices. Index 0 is the stage's own colour for the stick (StageLayout); the rest replace it.</summary>
    public static readonly string[] LaserColorNames = { "DEFAULT", "BLUE", "CYAN", "GREEN", "YELLOW", "ORANGE", "RED", "PINK", "PURPLE", "WHITE" };

    private static readonly Color[] LaserColors =
    {
        Color.clear,
        new Color(0.10f, 0.35f, 1.00f, 1f),
        new Color(0.00f, 0.85f, 0.95f, 1f),
        new Color(0.20f, 0.90f, 0.35f, 1f),
        new Color(1.00f, 0.85f, 0.10f, 1f),
        new Color(1.00f, 0.50f, 0.10f, 1f),
        new Color(1.00f, 0.20f, 0.25f, 1f),
        new Color(1.00f, 0.45f, 0.75f, 1f),
        new Color(0.60f, 0.35f, 1.00f, 1f),
        new Color(0.95f, 0.95f, 0.95f, 1f),
    };

    private static readonly int[] laserColorIndex = new int[2];

    /// <summary>Index into <see cref="LaserColorNames"/> for the left stick's laser (and everything else it plays).</summary>
    public static int LeftLaserColorIndex
    {
        get { Load(); return laserColorIndex[0]; }
        set { Load(); SetLaserColorIndex(0, value); }
    }

    /// <summary>Index into <see cref="LaserColorNames"/> for the right stick's laser.</summary>
    public static int RightLaserColorIndex
    {
        get { Load(); return laserColorIndex[1]; }
        set { Load(); SetLaserColorIndex(1, value); }
    }

    /// <summary>The chosen colour for a stick (0 = left, 1 = right), or false for DEFAULT (the stage's own).</summary>
    public static bool TryGetLaserColor(int side, out Color color)
    {
        Load();
        int index = laserColorIndex[side <= 0 ? 0 : 1];
        color = LaserColors[index];
        return index > 0;
    }

    /// <summary>Opaque preview colour of a LASER COLOR choice (index 0 has none and returns white).</summary>
    public static Color GetLaserColorChoice(int index)
    {
        return index > 0 && index < LaserColors.Length ? LaserColors[index] : Color.white;
    }

    private static void SetLaserColorIndex(int side, int value)
    {
        int index = ((value % LaserColors.Length) + LaserColors.Length) % LaserColors.Length;
        if (laserColorIndex[side] == index)
        {
            return;
        }

        laserColorIndex[side] = index;
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// The name on the in-song HUD's top bar, entered with the PLAYER NAME row of the
    /// options (NameEntryUI). Kept in PlayerPrefs; <see cref="DefaultPlayerName"/> while unset.
    /// </summary>
    public static string PlayerName
    {
        get
        {
            string name = PlayerPrefs.GetString(Prefix + "playerName", string.Empty).Trim();
            return name.Length > 0 ? name : DefaultPlayerName;
        }
        set
        {
            string name = (value ?? string.Empty).Trim();
            if (name.Length > MaxPlayerNameLength)
            {
                name = name.Substring(0, MaxPlayerNameLength).TrimEnd();
            }

            if (name == PlayerPrefs.GetString(Prefix + "playerName", string.Empty))
            {
                return;
            }

            PlayerPrefs.SetString(Prefix + "playerName", name);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>False while the player has not entered a name (<see cref="PlayerName"/> shows the default).</summary>
    public static bool HasPlayerName => PlayerPrefs.GetString(Prefix + "playerName", string.Empty).Trim().Length > 0;

    public static void Load()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        hiSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "hiSpeed", hiSpeed), MinHiSpeed, MaxHiSpeed);
        judgeOffsetMs = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "judgeOffsetMs", judgeOffsetMs), -MaxJudgeOffsetMs, MaxJudgeOffsetMs);
        visualOffsetMs = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "visualOffsetMs", visualOffsetMs), -MaxVisualOffsetMs, MaxVisualOffsetMs);
        mirror = PlayerPrefs.GetInt(Prefix + "mirror", 0) != 0;
        darkMode = PlayerPrefs.GetInt(Prefix + "darkMode", 0) != 0;
        autoPlay = PlayerPrefs.GetInt(Prefix + "autoPlay", 0) != 0;
        showFastSlow = PlayerPrefs.GetInt(Prefix + "showFastSlow", 1) != 0;
        showTimingDetail = PlayerPrefs.GetInt(Prefix + "showTimingDetail", 1) != 0;
        bloomPercent = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "bloomPercent", bloomPercent), 0, 200);
        stageBrightnessPercent = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "stageBrightnessPercent", stageBrightnessPercent), 20, 100);
        stickAngleDegrees = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "stickAngleDegrees", stickAngleDegrees), MinStickAngle, MaxStickAngle);
        gridIntervalIndex = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "gridIntervalIndex", gridIntervalIndex), 0, GridIntervalBeats.Length - 1);
        approachAccelerate = PlayerPrefs.GetInt(Prefix + "approachAccelerate", 0) != 0;
        approachStrength = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "approachStrength", approachStrength), MinApproachStrength, MaxApproachStrength);
        masterVolumePercent = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "masterVolume", masterVolumePercent), 0, 100);
        musicVolumePercent = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "musicVolume", musicVolumePercent), 0, 100);
        audioOffsetMs = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "audioOffsetMs", audioOffsetMs), -MaxAudioOffsetMs, MaxAudioOffsetMs);
        noteSound = PlayerPrefs.GetInt(Prefix + "noteSound", 0) != 0;
        noteSoundVolumePercent = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "noteSoundVolume", noteSoundVolumePercent), 0, 100);
        laserColorIndex[0] = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "leftLaserColor", 0), 0, LaserColors.Length - 1);
        laserColorIndex[1] = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "rightLaserColor", 0), 0, LaserColors.Length - 1);
    }

    private static void Apply<T>(ref T field, T value) where T : IEquatable<T>
    {
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        PlayerPrefs.SetFloat(Prefix + "hiSpeed", hiSpeed);
        PlayerPrefs.SetInt(Prefix + "judgeOffsetMs", judgeOffsetMs);
        PlayerPrefs.SetInt(Prefix + "visualOffsetMs", visualOffsetMs);
        PlayerPrefs.SetInt(Prefix + "mirror", mirror ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "darkMode", darkMode ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "autoPlay", autoPlay ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "showFastSlow", showFastSlow ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "showTimingDetail", showTimingDetail ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "bloomPercent", bloomPercent);
        PlayerPrefs.SetInt(Prefix + "stageBrightnessPercent", stageBrightnessPercent);
        PlayerPrefs.SetInt(Prefix + "stickAngleDegrees", stickAngleDegrees);
        PlayerPrefs.SetInt(Prefix + "gridIntervalIndex", gridIntervalIndex);
        PlayerPrefs.SetInt(Prefix + "approachAccelerate", approachAccelerate ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "approachStrength", approachStrength);
        PlayerPrefs.SetInt(Prefix + "masterVolume", masterVolumePercent);
        PlayerPrefs.SetInt(Prefix + "musicVolume", musicVolumePercent);
        PlayerPrefs.SetInt(Prefix + "audioOffsetMs", audioOffsetMs);
        PlayerPrefs.SetInt(Prefix + "noteSound", noteSound ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "noteSoundVolume", noteSoundVolumePercent);
        PlayerPrefs.SetInt(Prefix + "leftLaserColor", laserColorIndex[0]);
        PlayerPrefs.SetInt(Prefix + "rightLaserColor", laserColorIndex[1]);
    }
}
