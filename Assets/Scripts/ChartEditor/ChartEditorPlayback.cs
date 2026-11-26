using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays the music for the chart editor and schedules a short clap on every note so the
/// timing can be heard, plus a metronome click on every beat of the chart's bpm (higher on the
/// first beat of a measure). Its AudioSources live on a hidden GameObject. During the game
/// preview it plays no music and only sounds along with the game's clock (<see cref="Follow"/>).
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
    private const int TickVoices = 4;
    private readonly AudioSource[] ticks = new AudioSource[TickVoices];
    private AudioClip tickClip;
    private AudioClip accentClip;
    private int nextTickVoice;
    private const int GuideVoices = 4;
    private readonly AudioSource[] guides = new AudioSource[GuideVoices];
    private AudioClip guideClip;
    private double[] guideTimes = Array.Empty<double>();
    private int nextGuide;
    private int nextGuideVoice;
    // Beat n of the metronome is at beatOrigin + n * beatSeconds; every fourth one starts a measure.
    private double beatOrigin;
    private double beatSeconds;
    private long nextBeat;
    private bool following;
    private double wallSongStart;
    private double audioOffset;

    public bool IsPlaying { get; private set; }

    public bool ClapEnabled { get; set; } = true;

    public bool MusicEnabled { get; set; } = true;

    public float MusicVolume { get; set; } = 0.8f;
    public float ClapVolume { get; set; } = 0.6f;

    public bool MetronomeEnabled { get; set; }

    public float MetronomeVolume { get; set; } = 0.6f;

    /// <summary>Sounds the chart's guide beats.</summary>
    public bool GuideEnabled { get; set; }

    public float GuideVolume { get; set; } = 0.8f;

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

        PrepareSounds(fromSongTime, chart);
        IsPlaying = true;
        Update();
    }

    /// <summary>Lines up the claps and metronome clicks from <paramref name="fromSongTime"/> on.</summary>
    private void PrepareSounds(double fromSongTime, SongChartData chart)
    {
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

        guideTimes = chart != null && chart.guides != null ? (double[])chart.guides.Clone() : Array.Empty<double>();
        nextGuide = 0;
        while (nextGuide < guideTimes.Length && guideTimes[nextGuide] < fromSongTime - 0.001d)
        {
            nextGuide++;
        }

        beatSeconds = chart != null ? 60d / Math.Max(1f, chart.bpm) : 0.5d;
        beatOrigin = chart != null ? chart.offset : 0d;
        nextBeat = (long)Math.Ceiling((fromSongTime - 0.001d - beatOrigin) / beatSeconds);
    }

    /// <summary>
    /// Claps and metronome alongside the game preview: call it every frame with the game's song
    /// time while it runs. The music is the game's own.
    /// </summary>
    public void Follow(double songTime, SongChartData chart)
    {
        if (!following)
        {
            Stop();
            EnsureHost();
            PrepareSounds(songTime, chart);
            following = true;
        }

        Schedule(songTime);
    }

    /// <summary>Ends <see cref="Follow"/>; does nothing when it is not running.</summary>
    public void StopFollowing()
    {
        if (following)
        {
            Stop();
        }
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

        Schedule(SongTime);
    }

    private void Schedule(double now)
    {
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

        while (nextGuide < guideTimes.Length && guideTimes[nextGuide] <= now + ClapLookAheadSeconds)
        {
            double time = guideTimes[nextGuide];
            nextGuide++;
            if (!GuideEnabled || time < now - 0.02d)
            {
                continue;
            }

            AudioSource voice = guides[nextGuideVoice];
            nextGuideVoice = (nextGuideVoice + 1) % guides.Length;
            voice.volume = GuideVolume;
            voice.PlayScheduled(dspNow + Math.Max(0d, time - now));
        }

        while (beatOrigin + nextBeat * beatSeconds <= now + ClapLookAheadSeconds)
        {
            double time = beatOrigin + nextBeat * beatSeconds;
            bool accent = ((nextBeat % 4) + 4) % 4 == 0;
            nextBeat++;
            if (!MetronomeEnabled || time < now - 0.02d)
            {
                continue;
            }

            AudioSource voice = ticks[nextTickVoice];
            nextTickVoice = (nextTickVoice + 1) % ticks.Length;
            voice.clip = accent ? accentClip : tickClip;
            voice.volume = MetronomeVolume;
            voice.PlayScheduled(dspNow + Math.Max(0d, time - now));
        }
    }

    public void Stop()
    {
        IsPlaying = false;
        following = false;
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

        foreach (AudioSource voice in guides)
        {
            if (voice != null)
            {
                voice.Stop();
            }
        }

        foreach (AudioSource voice in ticks)
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

        if (tickClip != null)
        {
            UnityEngine.Object.DestroyImmediate(tickClip);
        }

        if (accentClip != null)
        {
            UnityEngine.Object.DestroyImmediate(accentClip);
        }

        if (guideClip != null)
        {
            UnityEngine.Object.DestroyImmediate(guideClip);
        }

        host = null;
        guideClip = null;
        music = null;
        clapClip = null;
        tickClip = null;
        accentClip = null;
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

        if (guideClip == null)
        {
            guideClip = CreateKnockClip();
        }

        for (int index = 0; index < guides.Length; index++)
        {
            AudioSource voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voice.clip = guideClip;
            guides[index] = voice;
        }

        if (tickClip == null)
        {
            tickClip = CreateTickClip("ChartEditorTick", 1000f);
            accentClip = CreateTickClip("ChartEditorAccent", 1600f);
        }

        for (int index = 0; index < ticks.Length; index++)
        {
            AudioSource voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            ticks[index] = voice;
        }
    }

    /// <summary>The guide beat: a low, dry knock like a tap on a desk, apart from the claps and the metronome.</summary>
    private static AudioClip CreateKnockClip()
    {
        const int sampleRate = 44100;
        const int length = sampleRate / 16;
        float[] data = new float[length];
        System.Random random = new System.Random(11);
        for (int index = 0; index < length; index++)
        {
            float time = index / (float)sampleRate;
            float body = Mathf.Exp(-time * 70f) * Mathf.Sin(2f * Mathf.PI * 320f * time);
            float contact = Mathf.Exp(-time * 600f) * (float)(random.NextDouble() * 2d - 1d);
            data[index] = Mathf.Clamp(body * 0.9f + contact * 0.5f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("ChartEditorKnock", length, 1, sampleRate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    /// <summary>The metronome's click: a short plain tone, unlike the noisy clap.</summary>
    private static AudioClip CreateTickClip(string name, float frequency)
    {
        const int sampleRate = 44100;
        const int length = sampleRate / 25;
        float[] data = new float[length];
        for (int index = 0; index < length; index++)
        {
            float time = index / (float)sampleRate;
            data[index] = Mathf.Exp(-time * 110f) * Mathf.Sin(2f * Mathf.PI * frequency * time) * 0.9f;
        }

        AudioClip clip = AudioClip.Create(name, length, 1, sampleRate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    /// <summary>A short, bright tick: a decaying high sine plus a little noise. Also the game's note sound.</summary>
    internal static AudioClip CreateClapClip()
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
