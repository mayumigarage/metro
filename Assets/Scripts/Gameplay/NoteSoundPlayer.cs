using UnityEngine;

/// <summary>
/// The NOTE SOUND option: a tick whenever a button or flick note is hit (not on a Miss, a
/// hold's end or a laser). The chart editor's own preview makes its note sounds itself.
/// </summary>
public sealed class NoteSoundPlayer : MonoBehaviour
{
    private JudgeSystem judgeSystem;
    private GameFlow flow;
    private AudioSource source;
    private AudioClip clip;
    private int playedFrame = -1;

    private void Awake()
    {
        judgeSystem = FindFirstObjectByType<JudgeSystem>();
        flow = GetComponent<GameFlow>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        clip = ChartEditorPlayback.CreateClapClip();
    }

    private void OnEnable()
    {
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved += OnJudgementResolved;
        }
    }

    private void OnDisable()
    {
        if (judgeSystem != null)
        {
            judgeSystem.JudgementResolved -= OnJudgementResolved;
        }
    }

    private void OnDestroy()
    {
        if (clip != null)
        {
            Destroy(clip);
        }
    }

    private void OnJudgementResolved(JudgementResult result)
    {
        if (!GameSettings.NoteSound || result.judgement == Judgement.Miss || result.part == NotePart.HoldEnd
            || flow != null && flow.IsEmbeddedPreview
            // Notes hit in the same frame share one tick, so chords are not louder.
            || playedFrame == Time.frameCount)
        {
            return;
        }

        playedFrame = Time.frameCount;
        source.PlayOneShot(clip, GameSettings.NoteSoundVolumePercent / 100f);
    }
}
