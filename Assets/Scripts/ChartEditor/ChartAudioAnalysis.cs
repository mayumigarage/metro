using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Onset analysis of a song for <see cref="ChartAutoGenerator"/> (stage 2 of 譜面 > 自動生成…).
/// The clip is mixed to mono and brought down to about 22 kHz, cut into 1024-sample Hann frames every
/// 256 samples (~11.6 ms), and each frame's spectrum is compared with the previous one: the rise of the
/// log magnitudes (spectral flux) in three bands — low (&lt; 200 Hz: kick, bass), mid (200 Hz – 2 kHz:
/// snare body, voice, chords) and high (&gt; 2 kHz: hats, snare snap) — is how hard a new sound starts there.
/// Each band is scaled by its 95th percentile over the song, and the combined curve has its local mean
/// (±0.4 s) taken off, so a loud passage does not read as one long onset. RMS energy is kept as well,
/// for how busy a section should be and whether a sound is held (for long notes).
/// Times are on the clip's own clock (seconds from the start of the file); add the chart's
/// <c>audioOffset</c> to get song time.
/// </summary>
public sealed class ChartAudioAnalysis
{
    private const int FrameSize = 1024;
    private const int Hop = 256;
    private const float LowCutHz = 200f;
    private const float HighCutHz = 2000f;

    /// <summary>Seconds between analysis frames.</summary>
    public double FrameSeconds { get; private set; }
    public double Length { get; private set; }

    private float[] low;
    private float[] mid;
    private float[] high;
    private float[] onset;
    private float[] energy;

    /// <summary>Reads the clip (it must be Decompress On Load) and analyses it; null when its samples cannot be read.</summary>
    public static ChartAudioAnalysis FromClip(AudioClip clip)
    {
        if (!AudioImport.CanReadSamples(clip))
        {
            return null;
        }

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData();
        }

        int channels = Mathf.Max(1, clip.channels);
        int frequency = Mathf.Max(1, clip.frequency);
        // Average pairs of samples above 32 kHz: 22.05 / 24 kHz is plenty for onsets and halves the work.
        int decimate = frequency > 32000 ? 2 : 1;
        float[] mono = new float[clip.samples / decimate + 1];
        float[] buffer = new float[frequency * channels];
        int written = 0;
        float accumulator = 0f;
        int accumulated = 0;
        for (int offset = 0; offset < clip.samples; offset += frequency)
        {
            int frames = Mathf.Min(frequency, clip.samples - offset);
            if (frames < frequency)
            {
                buffer = new float[frames * channels];
            }

            if (!clip.GetData(buffer, offset))
            {
                return null;
            }

            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += buffer[frame * channels + channel];
                }

                accumulator += sum / channels;
                if (++accumulated == decimate)
                {
                    mono[written++] = accumulator / decimate;
                    accumulator = 0f;
                    accumulated = 0;
                }
            }
        }

        Array.Resize(ref mono, written);
        return Analyze(mono, frequency / decimate);
    }

    public static ChartAudioAnalysis Analyze(float[] samples, int sampleRate)
    {
        ChartAudioAnalysis analysis = new ChartAudioAnalysis
        {
            FrameSeconds = Hop / (double)sampleRate,
            Length = samples.Length / (double)sampleRate
        };

        int frameCount = Math.Max(1, (samples.Length - FrameSize) / Hop + 1);
        analysis.low = new float[frameCount];
        analysis.mid = new float[frameCount];
        analysis.high = new float[frameCount];
        analysis.energy = new float[frameCount];

        int bins = FrameSize / 2;
        float binHz = sampleRate / (float)FrameSize;
        int lowEnd = Mathf.Clamp(Mathf.RoundToInt(LowCutHz / binHz), 2, bins);
        int midEnd = Mathf.Clamp(Mathf.RoundToInt(HighCutHz / binHz), lowEnd + 1, bins);

        float[] window = new float[FrameSize];
        for (int index = 0; index < FrameSize; index++)
        {
            window[index] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * index / (FrameSize - 1));
        }

        float[] re = new float[FrameSize];
        float[] im = new float[FrameSize];
        float[] previous = new float[bins];
        float[] current = new float[bins];
        for (int frame = 0; frame < frameCount; frame++)
        {
            int start = frame * Hop;
            double squares = 0d;
            for (int index = 0; index < FrameSize; index++)
            {
                float value = start + index < samples.Length ? samples[start + index] : 0f;
                squares += value * value;
                re[index] = value * window[index];
                im[index] = 0f;
            }

            analysis.energy[frame] = (float)Math.Sqrt(squares / FrameSize);
            Fft(re, im);
            float lowFlux = 0f;
            float midFlux = 0f;
            float highFlux = 0f;
            for (int bin = 1; bin < bins; bin++)
            {
                float magnitude = Mathf.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]);
                current[bin] = Mathf.Log(1f + 100f * magnitude);
                float rise = Mathf.Max(0f, current[bin] - previous[bin]);
                if (bin < lowEnd)
                {
                    lowFlux += rise;
                }
                else if (bin < midEnd)
                {
                    midFlux += rise;
                }
                else
                {
                    highFlux += rise;
                }
            }

            if (frame > 0)
            {
                analysis.low[frame] = lowFlux / (lowEnd - 1);
                analysis.mid[frame] = midFlux / (midEnd - lowEnd);
                analysis.high[frame] = highFlux / (bins - midEnd);
            }

            float[] swap = previous;
            previous = current;
            current = swap;
        }

        NormalizeByPercentile(analysis.low, 0.95f);
        NormalizeByPercentile(analysis.mid, 0.95f);
        NormalizeByPercentile(analysis.high, 0.95f);
        NormalizeByPercentile(analysis.energy, 0.9f);

        // Combined onset curve with its local mean taken off.
        float[] combined = new float[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            combined[frame] = (analysis.low[frame] + analysis.mid[frame] + 0.8f * analysis.high[frame]) / 2.8f;
        }

        int radius = Mathf.Max(1, Mathf.RoundToInt(0.4f / (float)analysis.FrameSeconds));
        float[] prefix = new float[frameCount + 1];
        for (int frame = 0; frame < frameCount; frame++)
        {
            prefix[frame + 1] = prefix[frame] + combined[frame];
        }

        analysis.onset = new float[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            int from = Math.Max(0, frame - radius);
            int to = Math.Min(frameCount, frame + radius + 1);
            float mean = (prefix[to] - prefix[from]) / (to - from);
            analysis.onset[frame] = Mathf.Max(0f, combined[frame] - mean);
        }

        NormalizeByPercentile(analysis.onset, 0.97f);
        return analysis;
    }

    // ------------------------------------------------------------------ queries (clip time)

    private int FrameAt(double time) => Mathf.Clamp((int)Math.Round(time / FrameSeconds), 0, onset.Length - 1);

    private static float MaxIn(float[] curve, int from, int to)
    {
        float best = 0f;
        for (int frame = from; frame <= to; frame++)
        {
            best = Mathf.Max(best, curve[frame]);
        }

        return best;
    }

    /// <summary>How strongly a sound starts within ±<paramref name="halfWindow"/> of <paramref name="time"/> (≈1 for the song's strong hits).</summary>
    public float Onset(double time, double halfWindow)
    {
        if (time < -halfWindow || time > Length + halfWindow)
        {
            return 0f;
        }

        return MaxIn(onset, FrameAt(time - halfWindow), FrameAt(time + halfWindow));
    }

    /// <summary>Strongest rise in each band (low, mid, high) within the window.</summary>
    public Vector3 Bands(double time, double halfWindow)
    {
        if (time < -halfWindow || time > Length + halfWindow)
        {
            return Vector3.zero;
        }

        int from = FrameAt(time - halfWindow);
        int to = FrameAt(time + halfWindow);
        return new Vector3(MaxIn(low, from, to), MaxIn(mid, from, to), MaxIn(high, from, to));
    }

    /// <summary>Mean loudness over a span (≈1 for the loud parts of the song, 0 for silence).</summary>
    public float Energy(double from, double to)
    {
        if (to <= 0d || from >= Length)
        {
            return 0f;
        }

        int start = FrameAt(Math.Max(0d, from));
        int end = Math.Max(start, FrameAt(Math.Min(Length, to)));
        float sum = 0f;
        for (int frame = start; frame <= end; frame++)
        {
            sum += energy[frame];
        }

        return sum / (end - start + 1);
    }

    /// <summary>
    /// How well the sound starting at <paramref name="from"/> carries on to <paramref name="to"/>: the quietest
    /// stretch (50 ms average) in the span over the loudness just after the start. Near 1 = held, near 0 = died away.
    /// </summary>
    public float Sustain(double from, double to)
    {
        if (from < 0d || to > Length)
        {
            return 0f;
        }

        int start = FrameAt(from);
        int end = FrameAt(to);
        int span = Mathf.Max(1, Mathf.RoundToInt(0.05f / (float)FrameSeconds));
        float attack = 0f;
        for (int frame = start; frame < Math.Min(end, start + 2 * span); frame++)
        {
            attack = Mathf.Max(attack, energy[frame]);
        }

        if (attack <= 1e-4f)
        {
            return 0f;
        }

        float quietest = float.MaxValue;
        for (int frame = start + span; frame + span <= end; frame += span)
        {
            float sum = 0f;
            for (int index = 0; index < span; index++)
            {
                sum += energy[frame + index];
            }

            quietest = Mathf.Min(quietest, sum / span);
        }

        return quietest == float.MaxValue ? 0f : Mathf.Clamp01(quietest / attack);
    }

    // Where a peak of the flux lies relative to its frame's start: a hit shows up once it is well inside the window.
    private const double PeakLatencyFrames = 3d;

    /// <summary>
    /// The moments a new sound starts, as if tapped along: the onset curve's peaks of at least
    /// <paramref name="threshold"/>, the strongest first claiming <paramref name="minGap"/> seconds around
    /// itself, in time order.
    /// </summary>
    public List<(double time, float strength)> Peaks(float threshold, double minGap)
    {
        List<int> candidates = new List<int>();
        for (int frame = 1; frame + 1 < onset.Length; frame++)
        {
            if (onset[frame] >= threshold && onset[frame] > onset[frame - 1] && onset[frame] >= onset[frame + 1])
            {
                candidates.Add(frame);
            }
        }

        candidates.Sort((left, right) => onset[right].CompareTo(onset[left]));
        int gap = Math.Max(1, (int)Math.Round(minGap / FrameSeconds));
        bool[] taken = new bool[onset.Length];
        List<(double time, float strength)> peaks = new List<(double, float)>();
        foreach (int frame in candidates)
        {
            if (taken[frame])
            {
                continue;
            }

            for (int near = Math.Max(0, frame - gap + 1); near < Math.Min(onset.Length, frame + gap); near++)
            {
                taken[near] = true;
            }

            peaks.Add(((frame + PeakLatencyFrames) * FrameSeconds, onset[frame]));
        }

        peaks.Sort((left, right) => left.time.CompareTo(right.time));
        return peaks;
    }

    // ------------------------------------------------------------------ helpers

    private static void NormalizeByPercentile(float[] curve, float percentile)
    {
        if (curve.Length == 0)
        {
            return;
        }

        float[] sorted = (float[])curve.Clone();
        Array.Sort(sorted);
        float reference = sorted[Mathf.Clamp((int)(percentile * (sorted.Length - 1)), 0, sorted.Length - 1)];
        if (reference <= 1e-6f)
        {
            reference = sorted[sorted.Length - 1];
        }

        if (reference <= 1e-6f)
        {
            return;
        }

        for (int index = 0; index < curve.Length; index++)
        {
            curve[index] /= reference;
        }
    }

    /// <summary>In-place iterative radix-2 FFT; the length must be a power of two.</summary>
    private static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2d * Math.PI / length;
            float wRe = (float)Math.Cos(angle);
            float wIm = (float)Math.Sin(angle);
            for (int i = 0; i < n; i += length)
            {
                float curRe = 1f;
                float curIm = 0f;
                for (int k = 0; k < length / 2; k++)
                {
                    int a = i + k;
                    int b = a + length / 2;
                    float tRe = re[b] * curRe - im[b] * curIm;
                    float tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    float nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
