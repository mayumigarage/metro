using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays the music for the chart editor and schedules a short clap on every note so the
/// timing can be heard. Its AudioSources live on a hidden GameObject.
///
/// The clock is the wall clock, pulled back onto the music's sample position whenever they
/// drift apart, so the timeline keeps running even where there is no music (no audio, or a
/// song shorter than the chart).
/// </summary>
public sealed class ChartEditorPlayback : IDisposable
{
    private const int ClapVoices = 8;
    // How far ahead claps are handed to the audio system.
    private const double ClapLookAheadSeconds = 0.1d;
    // Drift between the wall clock and the music that is corrected.
    private const double ResyncSeconds = 0.03d;

    private GameObject host;
    private AudioSource music;
    private readonly AudioSource[] claps = new AudioSource[ClapVoices];
    private AudioClip clapClip;
    private readonly List<double> clapTimes = new List<double>();
    private int nextClap;
    private int nextVoice;
    private double wallSongStart;
    private double audioOffset;

    public bool IsPlaying { get; private set; }

    public bool ClapEnabled { get; set; } = true;

    public bool MusicEnabled { get; set; } = true;

    public float MusicVolume { get; set; } = 0.8f;
    public float ClapVolume { get; set; } = 0.6f;

    /// <summary>Song time of the playback.</summary>
    public double SongTime => Time.realtimeSinceStartupAsDouble - wallSongStart;

    public void Play(AudioClip clip, double fromSongTime, SongChartData chart)
    {
        Stop();
        EnsureHost();

        fromSongTime = Math.Max(0d, fromSongTime);
        wallSongStart = Time.realtimeSinceStartupAsDouble - fromSongTime;

        audioOffset = chart != null ? chart.audioOffset : 0d;
        double clipTime = fromSongTime - audioOffset;
        if (MusicEnabled && clip != null && clipTime < clip.length)
        {
            music.clip = clip;
            music.volume = MusicVolume;
            if (clipTime < 0d)
            {
                // The music starts later than the playback does.
                music.time = 0f;
                music.PlayScheduled(AudioSettings.dspTime - clipTime);
            }
            else
            {
                music.time = (float)clipTime;
                music.Play();
            }
        }

        clapTimes.Clear();
        if (chart != null && chart.notes != null)
        {
            foreach (TapNoteData note in chart.notes)
            {
                clapTimes.Add(note.time);
            }
        }

        clapTimes.Sort();
        nextClap = 0;
        // Claps on notes before the start point are skipped.
        while (nextClap < clapTimes.Count && clapTimes[nextClap] < fromSongTime - 0.001d)
        {
            nextClap++;
        }

        IsPlaying = true;
        Update();
    }

    /// <summary>Hands the claps of the next moment to the audio system. Call it every editor frame.</summary>
    public void Update()
    {
        if (!IsPlaying)
        {
            return;
        }

        music.volume = MusicVolume;
        if (music.isPlaying && music.clip != null && SongTime - audioOffset > 0.05d)
        {
            double musicTime = music.timeSamples / (double)music.clip.frequency + audioOffset;
            if (Math.Abs(SongTime - musicTime) > ResyncSeconds)
            {
                wallSongStart = Time.realtimeSinceStartupAsDouble - musicTime;
            }
        }

        double now = SongTime;
        double dspNow = AudioSettings.dspTime;
        while (nextClap < clapTimes.Count && clapTimes[nextClap] <= now + ClapLookAheadSeconds)
        {
            double time = clapTimes[nextClap];
            nextClap++;
            // Notes on the same moment share one clap.
            if (nextClap > 1 && Math.Abs(clapTimes[nextClap - 2] - time) < 0.0005d)
            {
                continue;
            }

            if (!ClapEnabled || time < now - 0.02d)
            {
                continue;
            }

            AudioSource voice = claps[nextVoice];
            nextVoice = (nextVoice + 1) % claps.Length;
            voice.volume = ClapVolume;
            // Scheduled the remaining moment ahead on the audio clock, so the clap does
            // not wait for the next editor frame.
            voice.PlayScheduled(dspNow + Math.Max(0d, time - now));
        }
    }

    public void Stop()
    {
        IsPlaying = false;
        if (music != null)
        {
            music.Stop();
        }

        foreach (AudioSource voice in claps)
        {
            if (voice != null)
            {
                voice.Stop();
            }
        }
    }

    public void Dispose()
    {
        Stop();
        if (host != null)
        {
            UnityEngine.Object.DestroyImmediate(host);
        }

        if (clapClip != null)
        {
            UnityEngine.Object.DestroyImmediate(clapClip);
        }

        host = null;
        music = null;
        clapClip = null;
    }

    private void EnsureHost()
    {
        if (host != null)
        {
            return;
        }

        host = new GameObject("ChartEditorPlayback") { hideFlags = HideFlags.HideAndDontSave };
        music = host.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.spatialBlend = 0f;

        if (clapClip == null)
        {
            clapClip = CreateClapClip();
        }

        for (int index = 0; index < claps.Length; index++)
        {
            AudioSource voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voice.clip = clapClip;
            claps[index] = voice;
        }
    }

    /// <summary>A short, bright tick: a decaying high sine plus a little noise.</summary>
    private static AudioClip CreateClapClip()
    {
        const int sampleRate = 44100;
        const int length = sampleRate / 20;
        float[] data = new float[length];
        System.Random random = new System.Random(7);
        for (int index = 0; index < length; index++)
        {
            float time = index / (float)sampleRate;
            float envelope = Mathf.Exp(-time * 90f);
            float tone = Mathf.Sin(2f * Mathf.PI * 2200f * time);
            float noise = (float)(random.NextDouble() * 2d - 1d);
            data[index] = envelope * (0.6f * tone + 0.4f * noise) * 0.8f;
        }

        AudioClip clip = AudioClip.Create("ChartEditorClap", length, 1, sampleRate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
