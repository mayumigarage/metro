using System;
using UnityEngine;

/// <summary>
/// The score of the song being played, built from the judge's events. The score is
/// an achievement rate: the points earned so far out of the points the whole chart
/// can give, so it climbs from 0% to at most 100%. The only place the score table
/// lives; the HUD, the lane combo and the result screen all read from here.
/// </summary>
public sealed class ScoreTracker : MonoBehaviour
{
    public const int PerfectPoints = 1000;
    public const int GreatPoints = 700;
    public const int GoodPoints = 400;

    private const string BestPrefix = "metro.best.";

    [SerializeField] private JudgeSystem judgeSystem;

    private readonly int[] counts = new int[4];
    private long earnedPoints;

    /// <summary>Raised after every judgement and after a reset.</summary>
    public event Action Changed;

    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }

    /// <summary>Great / Good hits that were early (FAST) or late (SLOW).</summary>
    public int FastCount { get; private set; }
    public int SlowCount { get; private set; }

    /// <summary>Judgements the chart produces in total, and how many have happened.</summary>
    public int TotalJudgements { get; private set; }
    public int JudgedCount { get; private set; }

    /// <summary>Achievement rate, 0..100.</summary>
    public float Percent => TotalJudgements <= 0
        ? 0f
        : (float)(earnedPoints * 100d / (TotalJudgements * (double)PerfectPoints));

    public string Rank => GetRank(Percent);

    public int GetCount(Judgement judgement)
    {
        return counts[(int)judgement];
    }

    /// <summary>Clears the score for a new play of <paramref name="chart"/>.</summary>
    public void Begin(SongChartData chart)
    {
        TotalJudgements = chart != null ? chart.JudgementCount : 0;
        Array.Clear(counts, 0, counts.Length);
        earnedPoints = 0;
        Combo = 0;
        MaxCombo = 0;
        FastCount = 0;
        SlowCount = 0;
        JudgedCount = 0;
        Changed?.Invoke();
    }

    public static int GetPoints(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect:
                return PerfectPoints;
            case Judgement.Great:
                return GreatPoints;
            case Judgement.Good:
                return GoodPoints;
            default:
                return 0;
        }
    }

    public static string GetRank(float percent)
    {
        return percent >= 98f ? "S+" :
            percent >= 95f ? "S" :
            percent >= 90f ? "A" :
            percent >= 80f ? "B" :
            percent >= 70f ? "C" : "D";
    }

    /// <summary>Best achievement rate saved for a song, or a negative number if it was never played.</summary>
    public static float GetBest(string songId)
    {
        return string.IsNullOrEmpty(songId) ? -1f : PlayerPrefs.GetFloat(BestPrefix + songId, -1f);
    }

    /// <summary>Saves <paramref name="percent"/> if it beats the song's best. True if it did.</summary>
    public static bool TrySaveBest(string songId, float percent)
    {
        if (string.IsNullOrEmpty(songId) || percent <= GetBest(songId))
        {
            return false;
        }

        PlayerPrefs.SetFloat(BestPrefix + songId, percent);
        PlayerPrefs.Save();
        return true;
    }

    private void OnEnable()
    {
        if (judgeSystem == null)
        {
            judgeSystem = FindFirstObjectByType<JudgeSystem>();
        }

        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved += OnJudgementResolved;
            judgeSystem.LaserJudgementResolved += OnLaserJudgementResolved;
            judgeSystem.ComboTicked += OnComboTicked;
        }
    }

    private void OnDisable()
    {
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved -= OnJudgementResolved;
            judgeSystem.LaserJudgementResolved -= OnLaserJudgementResolved;
            judgeSystem.ComboTicked -= OnComboTicked;
        }
    }

    private void OnJudgementResolved(JudgementResult result)
    {
        // Early / late only means something for a timed hit that was off but not missed.
        bool timed = result.part != NotePart.HoldEnd
            && (result.judgement == Judgement.Great || result.judgement == Judgement.Good);
        if (timed && result.deltaSeconds < 0d)
        {
            FastCount++;
        }
        else if (timed && result.deltaSeconds > 0d)
        {
            SlowCount++;
        }

        Add(result.judgement);
    }

    private void OnLaserJudgementResolved(LaserJudgementResult result)
    {
        Add(result.judgement);
    }

    /// <summary>A step of a followed laser or a kept hold: raises the combo only, no score.</summary>
    private void OnComboTicked()
    {
        Combo++;
        MaxCombo = Mathf.Max(MaxCombo, Combo);
        Changed?.Invoke();
    }

    private void Add(Judgement judgement)
    {
        counts[(int)judgement]++;
        JudgedCount++;
        earnedPoints += GetPoints(judgement);
        if (judgement == Judgement.Miss)
        {
            Combo = 0;
        }
        else
        {
            Combo++;
            MaxCombo = Mathf.Max(MaxCombo, Combo);
        }

        Changed?.Invoke();
    }
}
