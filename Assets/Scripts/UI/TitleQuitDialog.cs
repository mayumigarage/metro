using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quitting from the title screen: a small "終了" plate in the bottom right corner and the
/// confirmation it opens (also Esc / B). Built into the title screen, which calls
/// <see cref="Tick"/> and leaves the input to it while <see cref="IsOpen"/>.
/// </summary>
public sealed class TitleQuitDialog
{
    private readonly RoundedRectGraphic[] faces = new RoundedRectGraphic[2];
    private readonly Text[] labels = new Text[2];
    private CanvasGroup plate;
    private GameObject dialog;
    private int selected;

    public bool IsOpen => dialog.activeSelf;

    public void Build(RectTransform parent)
    {
        Vector2 centre = new Vector2(0.5f, 0.5f);
        Vector2 corner = new Vector2(1f, 0f);

        RoundedRectGraphic button = SignKit.CreateRoundedRect(parent, "QuitPlate", SignKit.Face, 10f);
        UiKit.Place(button, corner, new Vector2(-170f, 78f), new Vector2(250f, 64f));
        plate = button.gameObject.AddComponent<CanvasGroup>();
        SignKit.AddRelay(button).Clicked = eventData => Open();
        RoundedRectGraphic outline = SignKit.CreateRoundedRect(button.rectTransform, "Outline", SignKit.InkDim, 10f, 3f);
        UiKit.Stretch(outline);
        Text caption = SignKit.CreateText(button.rectTransform, "Text", "終了  <size=20>ESC / B</size>", 28, TextAnchor.MiddleCenter, SignKit.InkDim, true);
        UiKit.Stretch(caption);
        caption.rectTransform.anchoredPosition = new Vector2(0f, 28 * SignKit.CapRaise);

        AngularGraphic dim = SignKit.CreateDim(parent);
        dialog = dim.gameObject;

        RoundedRectGraphic board = SignKit.CreateRoundedRect(dim.rectTransform, "Board", SignKit.Face, 18f);
        UiKit.Place(board, centre, Vector2.zero, new Vector2(820f, 360f));
        Text question = SignKit.CreateText(board.rectTransform, "Question", "ゲームを終了しますか？", 46, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Place(question, centre, new Vector2(0f, 90f), new Vector2(780f, 70f));
        Text english = SignKit.CreateText(board.rectTransform, "English", "Quit the game?", 26, TextAnchor.MiddleCenter, SignKit.InkSoft);
        UiKit.Place(english, centre, new Vector2(0f, 36f), new Vector2(780f, 40f));

        string[] names = { "いいえ", "終了する" };
        for (int index = 0; index < names.Length; index++)
        {
            int choice = index;
            RoundedRectGraphic face = SignKit.CreateRoundedRect(board.rectTransform, $"Choice_{index}", SignKit.Face, 12f);
            UiKit.Place(face, centre, new Vector2(index == 0 ? -180f : 180f, -80f), new Vector2(320f, 100f));
            UiPointerRelay relay = SignKit.AddRelay(face);
            relay.Entered = () => Select(choice);
            relay.Clicked = eventData =>
            {
                Select(choice);
                Decide();
            };
            faces[index] = face;
            labels[index] = SignKit.CreateText(face.rectTransform, "Text", names[index], 38, TextAnchor.MiddleCenter, SignKit.Ink, true);
            UiKit.Stretch(labels[index]);
            labels[index].rectTransform.anchoredPosition = new Vector2(0f, 38 * SignKit.CapRaise);
        }

        dialog.SetActive(false);
    }

    /// <summary>Opens the confirmation with "いいえ" chosen.</summary>
    public void Open()
    {
        dialog.SetActive(true);
        Select(0);
    }

    public void Close()
    {
        dialog.SetActive(false);
    }

    /// <summary>Fades the corner plate (it leaves with the title's prompt).</summary>
    public void SetPlateAlpha(float alpha)
    {
        plate.alpha = alpha;
        plate.blocksRaycasts = alpha > 0.5f;
    }

    /// <summary>Input while the confirmation is open.</summary>
    public void Tick()
    {
        if (MenuInput.Cancel)
        {
            MenuInput.Consume();
            Close();
            return;
        }

        if (MenuInput.Left)
        {
            Select(0);
        }
        else if (MenuInput.Right)
        {
            Select(1);
        }

        if (MenuInput.Confirm)
        {
            MenuInput.Consume();
            Decide();
        }
    }

    private void Select(int choice)
    {
        selected = choice;
        for (int index = 0; index < faces.Length; index++)
        {
            bool isSelected = index == selected;
            faces[index].color = isSelected ? SignKit.Ink : SignKit.SteelLight;
            labels[index].color = isSelected ? SignKit.OnInk : SignKit.Ink;
        }
    }

    private void Decide()
    {
        if (selected == 0)
        {
            Close();
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
