using UnityEngine;
using UnityEngine.UI;

public sealed class ScoreUI : MonoBehaviour
{
    [SerializeField] private JudgeSystem judgeSystem;
    [SerializeField] private Text judgementText;
    [SerializeField] private Text scoreText;

    private int score;
    private int combo;

    private void Start()
    {
        judgeSystem ??= FindFirstObjectByType<JudgeSystem>();
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved += OnJudgementResolved;
            judgeSystem.LaserJudgementResolved += OnLaserJudgementResolved;
        }

        UpdateLabels("READY");
    }

    private void OnDestroy()
    {
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved -= OnJudgementResolved;
            judgeSystem.LaserJudgementResolved -= OnLaserJudgementResolved;
        }
    }

    private void OnJudgementResolved(JudgementResult result)
    {
        if (result.judgement == Judgement.Miss)
        {
            combo = 0;
        }
        else
        {
            combo++;
            score += result.judgement == Judgement.Perfect ? 1000 :
                result.judgement == Judgement.Great ? 700 : 400;
        }

        UpdateLabels(result.judgement.ToString().ToUpperInvariant());
    }

    private void OnLaserJudgementResolved(LaserJudgementResult result)
    {
        if (result.judgement == Judgement.Miss)
        {
            combo = 0;
        }
        else
        {
            combo++;
            score += result.judgement == Judgement.Perfect ? 1000 :
                result.judgement == Judgement.Great ? 700 : 400;
        }

        UpdateLabels(result.judgement.ToString().ToUpperInvariant());
    }

    private void UpdateLabels(string judgement)
    {
        if (judgementText != null)
        {
            judgementText.text = judgement;
        }

        if (scoreText != null)
        {
            scoreText.text = $"SCORE {score:000000}\nCOMBO {combo:00}";
        }
    }
}
