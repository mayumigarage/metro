using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Older inspector-wired score display. It shows what <see cref="ScoreTracker"/>
/// holds; with its Text references unassigned (as in Game.unity) it does nothing.
/// </summary>
public sealed class ScoreUI : MonoBehaviour
{
    [SerializeField] private JudgeSystem judgeSystem;
    [SerializeField] private Text judgementText;
    [SerializeField] private Text scoreText;

    private ScoreTracker score;

    private void Start()
    {
        judgeSystem = judgeSystem != null ? judgeSystem : FindFirstObjectByType<JudgeSystem>();
        score = FindFirstObjectByType<ScoreTracker>();
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved += OnJudgementResolved;
            judgeSystem.LaserJudgementResolved += OnLaserJudgementResolved;
        }

        if (score != null)
        {
            score.Changed += UpdateScore;
        }

        SetJudgement("READY");
        UpdateScore();
    }

    private void OnDestroy()
    {
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved -= OnJudgementResolved;
            judgeSystem.LaserJudgementResolved -= OnLaserJudgementResolved;
        }

        if (score != null)
        {
            score.Changed -= UpdateScore;
        }
    }

    private void OnJudgementResolved(JudgementResult result)
    {
        SetJudgement(result.judgement.ToString().ToUpperInvariant());
    }

    private void OnLaserJudgementResolved(LaserJudgementResult result)
    {
        SetJudgement(result.judgement.ToString().ToUpperInvariant());
    }

    private void SetJudgement(string judgement)
    {
        if (judgementText != null)
        {
            judgementText.text = judgement;
        }
    }

    private void UpdateScore()
    {
        if (scoreText != null && score != null)
        {
            scoreText.text = $"SCORE {score.Percent:0.00}%\nCOMBO {score.Combo:00}";
        }
    }
}
