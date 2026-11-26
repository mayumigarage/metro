using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Draft chart generator for the chart editor (譜面 > 自動生成…). Notes sit on the chart's beat grid
/// (<c>bpm</c> / <c>offset</c>, 4 beats a measure). Stage 1 (no <see cref="Settings.audio"/>) picks the rhythm
/// from weighted patterns; stage 2 takes it from the song's onsets (<see cref="ChartAudioAnalysis"/>,
/// <see cref="PlaceNotesFromAudio"/>). Either way lanes follow lane patterns and the result is kept playable by a few rules:
/// <list type="bullet">
/// <item>notes closer than half a beat alternate hands (L / ZL = left hand, R / ZR = right hand);</item>
/// <item>no lane repeats within half a beat, chords only take one button per hand;</item>
/// <item>a hold keeps its hand busy until it ends (the other hand plays on);</item>
/// <item>no flicks on a stick while that stick follows a laser (plus a beat on either side),
///   and flicks on one stick are at least half a beat apart (a beat below density 8), never the same way twice in a row;</item>
/// <item>lasers last 2 or 4 measures with points every ½–2 beats, and the buttons thin out while one runs.</item>
/// </list>
/// The same settings and seed always give the same draft.
/// </summary>
public static class ChartAutoGenerator
{
    public sealed class Settings
    {
        public float bpm = 120f;
        public double offset;
        /// <summary>First and last measure (1-based, inclusive).</summary>
        public int firstMeasure = 1;
        public int lastMeasure = 8;
        /// <summary>Nothing is placed after this song time (the end of the music); 0 = no limit.</summary>
        public double songEnd;
        /// <summary>1 (sparse) – 10 (dense).</summary>
        public int density = 5;
        public bool holds = true;
        public bool flicks = true;
        public bool lasers = true;
        public int seed;
        /// <summary>The song's onset analysis (stage 2); null = place on the grid from patterns alone (stage 1).</summary>
        public ChartAudioAnalysis audio;
        /// <summary>The chart's <c>audioOffset</c>: song time = clip time + this.</summary>
        public double audioOffset;
    }

    public sealed class Result
    {
        public readonly List<TapNoteData> Notes = new List<TapNoteData>();
        public readonly List<LaserNoteData> Lasers = new List<LaserNoteData>();
    }

    private const int BeatsPerMeasure = 4;
    private const double Eps = 1e-6;

    // Onsets inside one beat (fractions of a beat).
    private static readonly double[][] Rhythms =
    {
        new double[0],
        new[] { 0d },
        new[] { 0d, 0.5d },
        new[] { 0.5d },
        new[] { 0d, 0.75d },
        new[] { 0d, 0.5d, 0.75d },
        new[] { 0d, 0.25d, 0.5d, 0.75d }
    };

    // Lane orders a measure can follow (0 L, 1 R, 2 ZL, 3 ZR).
    private static readonly int[][] LanePatterns =
    {
        new[] { 0, 1 },          // top row back and forth
        new[] { 2, 3 },          // bottom row back and forth
        new[] { 0, 1, 3, 2 },    // stairs round the square
        new[] { 0, 2, 3, 1 },    // the other way round
        new[] { 0, 3, 1, 2 },    // crossing
        new[] { 0, 1, 2, 3 }     // Z shape
    };

    private sealed class State
    {
        public System.Random Rng;
        public Settings S;
        public double BeatSeconds;
        public readonly double[] LastLaneBeat = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        public readonly double[] HandBusyUntil = { double.NegativeInfinity, double.NegativeInfinity };
        public readonly double[] LastFlickBeat = { double.NegativeInfinity, double.NegativeInfinity };
        public readonly NoteDirection[] LastFlickDirection = { NoteDirection.None, NoteDirection.None };
        public double LastNoteBeat = double.NegativeInfinity;
        public int LastHand = -1;
        public int[] Pattern = LanePatterns[0];
        public int PatternIndex;
        // Beats (from the chart offset) during which a stick follows a laser, widened by a beat each side.
        public readonly List<(double from, double to)>[] LaserBlocks = { new List<(double, double)>(), new List<(double, double)>() };
        public readonly List<(double from, double to)> LaserSpans = new List<(double, double)>();
    }

    public static Result Generate(Settings settings)
    {
        State state = new State
        {
            Rng = new System.Random(settings.seed),
            S = settings,
            BeatSeconds = 60d / Math.Max(1f, settings.bpm)
        };
        settings.density = Mathf.Clamp(settings.density, 1, 10);
        int first = Math.Max(1, settings.firstMeasure);
        int last = Math.Max(first, settings.lastMeasure);

        Result result = new Result();
        if (settings.lasers)
        {
            PlanLasers(state, first, last, result);
        }

        if (settings.audio != null)
        {
            PlaceNotesFromAudio(state, first, last, result);
        }
        else
        {
            PlaceNotes(state, first, last, result);
        }

        return result;
    }

    /// <summary>Clip time (for <see cref="ChartAudioAnalysis"/>) of a beat.</summary>
    private static double ClipTime(State state, double beat) => state.S.offset + beat * state.BeatSeconds - state.S.audioOffset;

    // ------------------------------------------------------------------ helpers

    private static double TimeAt(State state, double beat) => Math.Round((state.S.offset + beat * state.BeatSeconds) * 10000d) / 10000d;

    private static double MeasureBeat(int measure) => (measure - 1) * (double)BeatsPerMeasure;

    private static bool InSong(State state, double beat) => state.S.songEnd <= 0d || TimeAt(state, beat) <= state.S.songEnd + Eps;

    private static int HandOf(int lane) => lane % 2;

    private static bool Chance(State state, double probability) => state.Rng.NextDouble() < probability;

    private static T Pick<T>(State state, IList<T> items) => items[state.Rng.Next(items.Count)];

    private static int PickWeighted(State state, IList<double> weights)
    {
        double total = 0d;
        foreach (double weight in weights)
        {
            total += Math.Max(0d, weight);
        }

        double roll = state.Rng.NextDouble() * total;
        for (int index = 0; index < weights.Count; index++)
        {
            roll -= Math.Max(0d, weights[index]);
            if (roll < 0d)
            {
                return index;
            }
        }

        return weights.Count - 1;
    }

    private static bool InLaser(State state, double beat)
    {
        foreach ((double from, double to) in state.LaserSpans)
        {
            if (beat >= from - Eps && beat <= to + Eps)
            {
                return true;
            }
        }

        return false;
    }

    private static bool StickBlocked(State state, int stick, double beat)
    {
        foreach ((double from, double to) in state.LaserBlocks[stick])
        {
            if (beat >= from - Eps && beat <= to + Eps)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ lasers

    private static void PlanLasers(State state, int first, int last, Result result)
    {
        int density = state.S.density;
        int lastSide = state.Rng.Next(2);
        double share = 0.25d + density * 0.025d;

        // With the audio, lasers go to the phrases that sound most held (loud with few hits) instead of at random.
        HashSet<int> chosen = null;
        if (state.S.audio != null)
        {
            List<(int phrase, double score)> scored = new List<(int, double)>();
            for (int phrase = first + 4; phrase <= last; phrase += 4)
            {
                double from = MeasureBeat(phrase);
                double to = MeasureBeat(Math.Min(last + 1, phrase + 4));
                double hits = 0d;
                int slots = 0;
                for (double beat = from; beat < to - Eps; beat += 0.5d)
                {
                    hits += state.S.audio.Onset(ClipTime(state, beat), 0.04d);
                    slots++;
                }

                double energy = state.S.audio.Energy(ClipTime(state, from), ClipTime(state, to));
                if (energy > 0.15d)
                {
                    scored.Add((phrase, energy - 0.5d * hits / Math.Max(1, slots) + state.Rng.NextDouble() * 0.1d));
                }
            }

            int count = (int)Math.Round((Math.Max(0, last - first - 3) / 4d) * share);
            chosen = new HashSet<int>(scored.OrderByDescending(item => item.score).Take(count).Select(item => item.phrase));
        }

        // Phrases of 4 measures; the first one stays free of lasers so the draft opens on buttons.
        for (int phrase = first + 4; phrase <= last; phrase += 4)
        {
            if (chosen != null ? !chosen.Contains(phrase) : !Chance(state, share))
            {
                continue;
            }

            int measuresLeft = last - phrase + 1;
            int length = measuresLeft >= 4 && Chance(state, 0.5d) ? 4 : Math.Min(2, measuresLeft);
            int side = Chance(state, 0.7d) ? 1 - lastSide : lastSide;
            lastSide = side;
            double startBeat = MeasureBeat(phrase);
            AddLaser(state, side, startBeat, length * BeatsPerMeasure, result);

            // Dense drafts sometimes run both sticks at once over the second half.
            if (density >= 8 && length == 4 && Chance(state, 0.35d))
            {
                AddLaser(state, 1 - side, startBeat + 2 * BeatsPerMeasure, 2 * BeatsPerMeasure, result);
            }
        }
    }

    private static void AddLaser(State state, int side, double startBeat, double lengthBeats, Result result)
    {
        int density = state.S.density;
        double step = density >= 7 && Chance(state, 0.5d) ? 0.5d : density <= 3 ? 2d : 1d;
        int count = (int)Math.Round(lengthBeats / step) + 1;

        // Positions are the eight stick directions on the unit circle (index * 45°, 0 = right).
        List<int> positions = new List<int>();
        int shape = state.Rng.Next(3);
        int current = state.Rng.Next(8);
        int turn = Chance(state, 0.5d) ? 1 : -1;
        int zigzagA = Chance(state, 0.5d) ? 0 : 2;
        for (int index = 0; index < count; index++)
        {
            switch (shape)
            {
                case 0: // zigzag between two opposite points
                    current = index % 2 == 0 ? zigzagA : zigzagA + 4;
                    break;
                case 1: // turning round the circle, 90° (or 135° when sparse) at a time
                    if (index > 0)
                    {
                        current = (current + turn * (density <= 4 ? 3 : 2) + 8) % 8;
                    }

                    break;
                default: // wandering: each move 90–270° round, sometimes holding still for a step (never twice running)
                    bool stillBefore = index >= 2 && positions[index - 1] == positions[index - 2];
                    bool holdStill = index > 0 && index < count - 1 && !stillBefore && Chance(state, 0.15d);
                    if (index > 0 && !holdStill)
                    {
                        current = (current + Pick(state, new[] { 2, 3, 4, 5, 6 })) % 8;
                    }

                    break;
            }

            positions.Add(current);
        }

        List<LaserPointData> points = new List<LaserPointData>();
        for (int index = 0; index < count; index++)
        {
            double beat = startBeat + index * step;
            if (!InSong(state, beat))
            {
                break;
            }

            float angle = positions[index] * 45f * Mathf.Deg2Rad;
            points.Add(new LaserPointData
            {
                time = TimeAt(state, beat),
                x = (float)Math.Round(Mathf.Cos(angle), 3),
                y = (float)Math.Round(Mathf.Sin(angle), 3)
            });
        }

        if (points.Count < 2)
        {
            return;
        }

        double endBeat = startBeat + (points.Count - 1) * step;
        result.Lasers.Add(new LaserNoteData { side = side, points = points.ToArray() });
        state.LaserBlocks[side].Add((startBeat - 1d, endBeat + 1d));
        state.LaserSpans.Add((startBeat, endBeat));
    }

    // ------------------------------------------------------------------ notes

    private static void PlaceNotes(State state, int first, int last, Result result)
    {
        int density = state.S.density;
        // The first two measures of each 4-measure phrase; the last two may repeat their rhythm, like a phrase answering itself.
        List<double[]> phraseRhythms = new List<double[]>();
        bool repeat = false;
        for (int measure = first; measure <= last; measure++)
        {
            state.Pattern = Pick(state, LanePatterns);
            state.PatternIndex = state.Rng.Next(state.Pattern.Length);

            int inPhrase = (measure - first) % 4;
            if (inPhrase == 0)
            {
                phraseRhythms.Clear();
                repeat = false;
            }
            else if (inPhrase == 2)
            {
                repeat = phraseRhythms.Count == 2 * BeatsPerMeasure && Chance(state, 0.5d);
            }
            for (int beatInMeasure = 0; beatInMeasure < BeatsPerMeasure; beatInMeasure++)
            {
                double beat = MeasureBeat(measure) + beatInMeasure;
                if (!InSong(state, beat))
                {
                    return;
                }

                int localDensity = InLaser(state, beat) ? Math.Max(1, density - 3) : density;
                double[] rhythm;
                if (repeat)
                {
                    rhythm = phraseRhythms[(inPhrase - 2) * BeatsPerMeasure + beatInMeasure];
                }
                else
                {
                    rhythm = PickRhythm(state, localDensity, beatInMeasure == 0);
                }

                if (inPhrase < 2)
                {
                    phraseRhythms.Add(rhythm);
                }

                foreach (double onset in rhythm)
                {
                    double noteBeat = beat + onset;
                    if (InSong(state, noteBeat))
                    {
                        bool roomy = rhythm.Length <= 2;
                        bool downbeat = onset == 0d && beatInMeasure == 0;
                        double chord = downbeat && roomy && localDensity >= 3 ? 0.1d + localDensity * 0.03d : 0d;
                        PlaceNote(state, noteBeat, localDensity, chord, roomy, result);
                    }
                }
            }
        }
    }

    private static double[] PickRhythm(State state, int d, bool downbeat)
    {
        double[] weights =
        {
            downbeat && d >= 2 ? 0d : Math.Max(0d, 6 - d) * 0.5d + 0.2d,
            3d,
            d * 0.5d,
            0.3d + d * 0.05d,
            d >= 4 ? 0.3d + d * 0.05d : 0d,
            d >= 6 ? (d - 5) * 0.3d : 0d,
            d >= 8 ? (d - 7) * 0.4d : 0d
        };
        double[] rhythm = Rhythms[PickWeighted(state, weights)];
        if (downbeat && rhythm.Length > 0 && rhythm[0] > 0d)
        {
            rhythm = Rhythms[1];
        }

        return rhythm;
    }

    private static bool LaneFree(State state, int lane, double beat)
    {
        return beat >= state.HandBusyUntil[HandOf(lane)] - Eps && beat - state.LastLaneBeat[lane] >= 0.5d - Eps;
    }

    /// <summary>
    /// Stage 2: the rhythm comes from the song. Each measure's grid slots (8ths, or 16ths from density 7) are
    /// scored by the onset there plus a small bonus for downbeats and beats; the measure keeps as many of the
    /// best as its density allows (fewer in quiet measures, none where nothing starts). Low-band hits go to the
    /// floor (ZL / ZR), high-band ones to the ceiling (L / R); the strongest hits may become chords or flicks,
    /// and a sound that rings on until the next note becomes a hold.
    /// </summary>
    private static void PlaceNotesFromAudio(State state, int first, int last, Result result)
    {
        ChartAudioAnalysis audio = state.S.audio;
        int density = state.S.density;
        int slotsPerBeat = density >= 7 ? 4 : 2;
        double slotBeats = 1d / slotsPerBeat;
        double halfWindow = Math.Min(0.06d, slotBeats * state.BeatSeconds * 0.45d);
        for (int measure = first; measure <= last; measure++)
        {
            state.Pattern = Pick(state, LanePatterns);
            state.PatternIndex = state.Rng.Next(state.Pattern.Length);
            double measureBeat = MeasureBeat(measure);
            if (!InSong(state, measureBeat))
            {
                return;
            }

            float energy = audio.Energy(ClipTime(state, measureBeat), ClipTime(state, measureBeat + BeatsPerMeasure));
            float effective = density * Mathf.Lerp(0.35f, 1.1f, Mathf.Clamp01(energy));
            if (InLaser(state, measureBeat + BeatsPerMeasure / 2d))
            {
                effective -= 3f;
            }

            int target = Mathf.Max(0, Mathf.RoundToInt(BeatsPerMeasure * (0.45f + 0.17f * (effective - 1f))));
            List<(double beat, float strength, double score)> slots = new List<(double, float, double)>();
            for (int slot = 0; slot < BeatsPerMeasure * slotsPerBeat; slot++)
            {
                double beat = measureBeat + slot * slotBeats;
                if (!InSong(state, beat))
                {
                    break;
                }

                float strength = audio.Onset(ClipTime(state, beat), halfWindow);
                if (strength < 0.2f)
                {
                    continue;
                }

                double prior = slot == 0 ? 0.15d : slot % slotsPerBeat == 0 ? 0.08d : slotsPerBeat == 4 && slot % 2 == 0 ? 0.03d : 0d;
                slots.Add((beat, strength, strength + prior + state.Rng.NextDouble() * 0.02d));
            }

            List<(double beat, float strength, double score)> picked = slots.OrderByDescending(item => item.score).Take(target).OrderBy(item => item.beat).ToList();
            for (int index = 0; index < picked.Count; index++)
            {
                double beat = picked[index].beat;
                float strength = picked[index].strength;
                double next = index + 1 < picked.Count ? picked[index + 1].beat : measureBeat + BeatsPerMeasure;
                double gap = next - beat;
                bool roomy = gap >= 0.5d - Eps;
                int d = Mathf.Clamp(Mathf.RoundToInt(effective), 1, 10);

                Vector3 bands = audio.Bands(ClipTime(state, beat), halfWindow);
                float total = bands.x + bands.y + bands.z;
                int row = -1;
                if (total > 1e-4f)
                {
                    if (bands.x / total > 0.45f)
                    {
                        row = 1;
                    }
                    else if (bands.z / total > 0.4f)
                    {
                        row = 0;
                    }
                }

                double hold = 0d;
                if (state.S.holds && gap >= 1.5d - Eps)
                {
                    double length = Math.Min(2d, Math.Floor((gap - 0.5d) * 2d + Eps) / 2d);
                    if (length >= 1d && audio.Sustain(ClipTime(state, beat), ClipTime(state, beat + length)) >= 0.55f)
                    {
                        hold = length;
                    }
                }

                double chord = strength >= 0.9f && roomy && d >= 3 ? 0.15d + d * 0.04d : 0d;
                PlaceNote(state, beat, d, chord, roomy, result, row, hold, strength);
            }
        }
    }

    /// <param name="chordChance">Chance of a second button on the other hand at the same time.</param>
    /// <param name="preferredRow">0 = ceiling (L / R), 1 = floor (ZL / ZR), -1 = follow the lane pattern.</param>
    /// <param name="holdBeats">Length of hold to make; negative = decide at random (stage 1), 0 = none.</param>
    /// <param name="accent">Onset strength (stage 2), scales the chance of a flick; negative = not known.</param>
    private static void PlaceNote(State state, double beat, int d, double chordChance, bool roomy, Result result,
        int preferredRow = -1, double holdBeats = -1d, float accent = -1f)
    {
        bool fast = beat - state.LastNoteBeat < 0.5d - Eps;
        int lane = -1;
        if (preferredRow >= 0)
        {
            // Other hand first, so a row played fast still alternates.
            int firstHand = state.LastHand < 0 ? state.Rng.Next(2) : 1 - state.LastHand;
            foreach (int candidate in new[] { preferredRow * 2 + firstHand, preferredRow * 2 + 1 - firstHand })
            {
                if (lane < 0 && LaneFree(state, candidate, beat) && !(fast && HandOf(candidate) == state.LastHand))
                {
                    lane = candidate;
                }
            }
        }

        for (int attempt = 0; attempt < state.Pattern.Length && lane < 0; attempt++)
        {
            int candidate = state.Pattern[(state.PatternIndex + attempt) % state.Pattern.Length];
            if (LaneFree(state, candidate, beat) && !(fast && HandOf(candidate) == state.LastHand))
            {
                lane = candidate;
                state.PatternIndex = (state.PatternIndex + attempt + 1) % state.Pattern.Length;
            }
        }

        if (lane < 0)
        {
            List<int> free = new List<int>();
            for (int candidate = 0; candidate < 4; candidate++)
            {
                if (LaneFree(state, candidate, beat) && !(fast && HandOf(candidate) == state.LastHand))
                {
                    free.Add(candidate);
                }
            }

            if (free.Count == 0)
            {
                return;
            }

            lane = Pick(state, free);
        }

        TapNoteData note = MakeNote(state, lane, beat, d, roomy, result, true, holdBeats, accent);
        if (note == null)
        {
            return;
        }

        state.LastNoteBeat = beat;
        state.LastHand = HandOf(lane);

        // Chord: one button of the other hand.
        if (chordChance > 0d && Chance(state, chordChance))
        {
            int otherHand = 1 - HandOf(lane);
            List<int> free = new List<int>();
            foreach (int candidate in new[] { otherHand, otherHand + 2 })
            {
                if (LaneFree(state, candidate, beat))
                {
                    free.Add(candidate);
                }
            }

            if (free.Count > 0)
            {
                MakeNote(state, Pick(state, free), beat, d, false, result, false, 0d);
            }
        }
    }

    private static TapNoteData MakeNote(State state, int lane, double beat, int d, bool roomy, Result result,
        bool allowFlick, double holdBeats = -1d, float accent = -1f)
    {
        int hand = HandOf(lane);
        TapNoteData note = new TapNoteData { lane = lane, time = TimeAt(state, beat) };

        // Hold: only while the other hand is free, so one hand always has the notes.
        bool wantHold = holdBeats < 0d ? roomy && Chance(state, 0.05d + d * 0.012d) : holdBeats > 0d;
        if (state.S.holds && wantHold && beat >= state.HandBusyUntil[1 - hand])
        {
            double length = holdBeats > 0d ? holdBeats : d <= 4 ? 2d : Chance(state, 0.5d) ? 1d : 2d;
            if (InSong(state, beat + length))
            {
                note.duration = Math.Round(length * state.BeatSeconds * 10000d) / 10000d;
                state.HandBusyUntil[hand] = beat + length + 0.5d;
            }
        }

        // Flick: on the lane's own stick, unless that stick is on a laser or flicked too recently.
        int stick = hand;
        double flickGap = d >= 8 ? 0.5d : 1d;
        if (state.S.flicks && allowFlick && !note.IsHold && !StickBlocked(state, stick, beat)
            && beat - state.LastFlickBeat[stick] >= flickGap - Eps
            && Chance(state, (0.08d + d * 0.02d) * (accent < 0f ? 1d : Math.Min(1.6d, 0.3d + accent))))
        {
            NoteDirection direction;
            do
            {
                direction = d <= 4
                    ? Pick(state, new[] { NoteDirection.Up, NoteDirection.Down, NoteDirection.Left, NoteDirection.Right })
                    : (NoteDirection)(1 + state.Rng.Next(8));
            }
            while (direction == state.LastFlickDirection[stick] && beat - state.LastFlickBeat[stick] < 2d);

            note.direction = direction;
            note.flickOnly = Chance(state, 0.5d);
            state.LastFlickBeat[stick] = beat;
            state.LastFlickDirection[stick] = direction;
        }

        state.LastLaneBeat[lane] = note.IsHold ? beat + note.duration / state.BeatSeconds : beat;
        result.Notes.Add(note);
        return note;
    }
}
