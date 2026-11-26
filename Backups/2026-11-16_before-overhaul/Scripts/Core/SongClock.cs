using UnityEngine;

/// <summary>
/// Keeps the song position on the audio DSP clock instead of frame time.
/// </summary>
public sealed class SongClock : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private double startDelaySeconds = 0.1d;
    [SerializeField] private bool playOnStart = true;

    private double dspStartTime;
    private bool started;

    public bool IsStarted => started;
    public double SongTime => started ? AudioSettings.dspTime - dspStartTime : 0d;

    private void Start()
    {
        if (playOnStart)
        {
            StartSong();
        }
    }

    public void StartSong()
    {
        dspStartTime = AudioSettings.dspTime + Mathf.Max(0f, (float)startDelaySeconds);
        started = true;

        if (audioSource == null || audioSource.clip == null)
        {
            return;
        }

        audioSource.Stop();
        audioSource.PlayScheduled(dspStartTime);
    }

    public void StopSong()
    {
        if (audioSource != null)
        {
            audioSource.Stop();
        }

        started = false;
    }
}
