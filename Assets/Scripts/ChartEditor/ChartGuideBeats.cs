using System;
using System.Collections.Generic;

/// <summary>
/// Guide beats for charting by hand (譜面 > ガイド拍を生成…): the moments someone listening would tap on
/// a desk — one kind of beat, with no lanes or note types — taken from the song's onsets
/// (<see cref="ChartAudioAnalysis.Peaks"/>). Separate from <see cref="ChartAutoGenerator"/>: no notes are made.
/// </summary>
public static class ChartGuideBeats
{
    /// <param name="sensitivity">1 (only the strong hits) – 10 (every small sound).</param>
    /// <param name="snapDivision">Parts of a beat of <paramref name="bpm"/> / <paramref name="offset"/> to line up with; 0 = as heard.</param>
    /// <returns>Song times in order.</returns>
    public static double[] Generate(ChartAudioAnalysis audio, float bpm, double offset, double audioOffset, int sensitivity, int snapDivision)
    {
        double amount = (Math.Max(1, Math.Min(10, sensitivity)) - 1) / 9d;
        float threshold = (float)(0.8d - 0.7d * amount);
        double minGap = 0.24d - 0.16d * amount;
        double step = snapDivision > 0 ? 60d / Math.Max(1f, bpm) / snapDivision : 0d;

        List<double> times = new List<double>();
        foreach ((double clipTime, float _) in audio.Peaks(threshold, minGap))
        {
            double time = clipTime + audioOffset;
            if (step > 0d)
            {
                time = offset + Math.Round((time - offset) / step) * step;
            }

            time = Math.Round(time * 10000d) / 10000d;
            // Two sounds snapped onto the same line are one tap.
            if (time >= 0d && (times.Count == 0 || time - times[times.Count - 1] > 0.001d))
            {
                times.Add(time);
            }
        }

        return times.ToArray();
    }
}
