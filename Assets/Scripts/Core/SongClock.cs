using UnityEngine;

/// <summary>
/// Keeps the song position on the audio DSP clock instead of frame time.
/// The clock can be paused; while it is, <see cref="SongTime"/> stands still.
/// </summary>
public sealed class SongClock : MonoBehaviour
{
    // Lead time given to the audio system when playback resumes after a pause.
    private const double ResumeScheduleSeconds = 0.05d;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private double startDelaySeconds = 0.1d;
    [SerializeField] private bool playOnStart = true;

    private double dspStartTime;
    private double audioOffset;
    private double pausedSongTime;
    private bool started;
    private bool paused;
    private float musicVolume = 1f;

    public bool IsStarted => started;
    public bool IsPaused => paused;

    /// <summary>Started and not paused: the only state in which gameplay advances.</summary>
    public bool IsRunning => started && !paused;

    /// <summary>Seconds since the song's start. Negative during the lead-in.</summary>
    public double SongTime => !started ? 0d : paused ? pausedSongTime : AudioSettings.dspTime - dspStartTime;

    private void Start()
    {
        if (playOnStart)
        {
            StartSong();
        }
    }

    /// <summary>Volume of the music (0..1), from the MUSIC VOLUME option.</summary>
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (audioSource != null)
        {
            audioSource.volume = musicVolume;
        }
    }

    /// <summary>Called by GameFlow, which starts the song itself once one is chosen.</summary>
    public void DisableAutoStart()
    {
        playOnStart = false;
    }

    /// <summary>Starts with the serialized delay and whatever clip the AudioSource already has.</summary>
    public void StartSong()
    {
        Begin(startDelaySeconds);
    }

    /// <summary>
    /// Starts after <paramref name="leadInSeconds"/> with <paramref name="clip"/>
    /// as the music (null = run silently).
    /// </summary>
    public void StartSong(double leadInSeconds, AudioClip clip)
    {
        StartSong(leadInSeconds, clip, 0d);
    }

    /// <summary>
    /// Like <see cref="StartSong(double, AudioClip)"/>, but the song begins at
    /// <paramref name="fromSongTime"/> seconds (the chart editor's preview from the cursor):
    /// after the lead-in, <see cref="SongTime"/> reaches that time and the music plays from there.
    /// </summary>
    public void StartSong(double leadInSeconds, AudioClip clip, double fromSongTime)
    {
        StartSong(leadInSeconds, clip, fromSongTime, 0d);
    }

    /// <summary>
    /// Like the overload above, with the music shifted <paramref name="audioOffsetSeconds"/>
    /// later on the song clock (negative = earlier).
    /// </summary>
    public void StartSong(double leadInSeconds, AudioClip clip, double fromSongTime, double audioOffsetSeconds)
    {
        audioOffset = audioOffsetSeconds;
        if (audioSource == null && clip != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.volume = musicVolume;
        }

        Begin(leadInSeconds, fromSongTime);
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        pausedSongTime = SongTime;
        paused = true;
        if (audioSource != null)
        {
            // Stop rather than Pause: it also cancels a start that is still scheduled.
            audioSource.Stop();
        }
    }

    public void Resume()
    {
        if (!started || !paused)
        {
            return;
        }

        paused = false;
        double resumeDspTime = AudioSettings.dspTime + (pausedSongTime < 0d ? 0d : ResumeScheduleSeconds);
        dspStartTime = resumeDspTime - pausedSongTime;
        StartMusic(resumeDspTime, pausedSongTime);
    }

    public void StopSong()
    {
        if (audioSource != null)
        {
            audioSource.Stop();
        }

        started = false;
        paused = false;
    }

    private void Begin(double delaySeconds, double fromSongTime = 0d)
    {
        fromSongTime = System.Math.Max(0d, fromSongTime);
        double playDspTime = AudioSettings.dspTime + System.Math.Max(0d, delaySeconds);
        dspStartTime = playDspTime - fromSongTime;
        started = true;
        paused = false;

        if (!HasClip())
        {
            return;
        }

        audioSource.Stop();
        StartMusic(playDspTime, fromSongTime);
    }

    /// <summary>
    /// Schedules the music so that song time <paramref name="songTime"/> is heard at
    /// <paramref name="atDspTime"/>, honouring the audio offset. Does nothing past the clip's end.
    /// </summary>
    private void StartMusic(double atDspTime, double songTime)
    {
        if (!HasClip())
        {
            return;
        }

        double clipTime = songTime - audioOffset;
        if (clipTime >= audioSource.clip.length)
        {
            // Past the end of the music: the clock runs on silently.
            return;
        }

        if (clipTime < 0d)
        {
            // The music has not begun yet: start it from its first sample later.
            audioSource.time = 0f;
            audioSource.PlayScheduled(atDspTime - clipTime);
            return;
        }

        audioSource.time = (float)clipTime;
        audioSource.PlayScheduled(atDspTime);
    }

    private bool HasClip()
    {
        return audioSource != null && audioSource.clip != null;
    }
}
