using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Genre (folder) select, laid out like a transfer sign: a full-screen board with the
/// station name "Genre" and a row of line badges, each with its English name below
/// (this screen is English only). A badge's number is how many songs (stations) the genre has. Left /
/// right choose, confirm rides the chosen line into the song select.
/// </summary>
public sealed class GenreSelectUI : MonoBehaviour
{
    private const string Footer = "← →  SELECT          ENTER / A  DECIDE          ESC / B  TITLE";
    private const float BadgeDiameter = 250f;
    private const float BadgeSpacing = 380f;
    private const float BadgeY = -110f;
    private const float SelectedScale = 1.12f;
    private const float IntroSeconds = 0.45f;

    private sealed class Folder
    {
        public SongGenre Genre;
        public LineBadge Badge;
        public Text Name;
        public int SongCount;
        public float Scale = 1f;
        public float ShakeUntil;
    }

    private readonly List<Folder> folders = new List<Folder>();
    private GameFlow flow;
    private ChartLoader charts;
    private Text message;
    private int selected;
    private float shownAt;
    private float messageUntil;

    public CanvasGroup Group { get; private set; }
    public SongGenre Selected => folders[selected].Genre;

    /// <summary>The selected folder's badge (where the flight into the song select starts).</summary>
    public LineBadge SelectedBadge => folders[selected].Badge;

    public void Build(GameFlow gameFlow, ChartLoader chartLoader)
    {
        flow = gameFlow;
        charts = chartLoader;
        RectTransform root = (RectTransform)transform;
        Group = gameObject.AddComponent<CanvasGroup>();
        Vector2 centre = new Vector2(0.5f, 0.5f);

        RoundedRectGraphic face = SignKit.CreateBoard(root, "Board", 26f, 10f);
        face.raycastTarget = true;

        Text title = SignKit.CreateText(root, "Title", "Genre", 150, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Place(title, centre, new Vector2(0f, 250f), new Vector2(1400f, 200f));

        // Station code box in the corner, like the exit numbers on the sign.
        RoundedRectGraphic codeBox = SignKit.CreateRoundedRect(root, "CodeBox", SignKit.Ink, 12f, 6f);
        UiKit.Place(codeBox, new Vector2(1f, 1f), new Vector2(-64f, -58f), new Vector2(156f, 96f));
        Text code = SignKit.CreateText(codeBox.rectTransform, "Code", "M01", 54, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Stretch(code);
        code.rectTransform.anchoredPosition = new Vector2(0f, 54 * SignKit.CapRaise);

        for (int index = 0; index < SongGenre.All.Length; index++)
        {
            SongGenre genre = SongGenre.All[index];
            float x = (index - (SongGenre.All.Length - 1) * 0.5f) * BadgeSpacing;
            Folder folder = new Folder { Genre = genre };

            folder.Badge = LineBadge.Create(root, $"Badge_{genre.Id}", genre.Color, BadgeDiameter);
            folder.Badge.Rect.anchoredPosition = new Vector2(x, BadgeY);
            int folderIndex = index;
            UiPointerRelay relay = SignKit.AddRelay(folder.Badge.Face);
            relay.Entered = () =>
            {
                if (!flow.IsBusy)
                {
                    Select(folderIndex);
                }
            };
            relay.Clicked = eventData =>
            {
                if (!flow.IsBusy)
                {
                    Select(folderIndex);
                    Decide();
                }
            };

            folder.Name = SignKit.CreateText(root, $"Name_{genre.Id}", genre.EnglishName, 54, TextAnchor.MiddleCenter, SignKit.Ink, true);
            UiKit.Place(folder.Name, centre, new Vector2(x, BadgeY - 210f), new Vector2(BadgeSpacing, 70f));
            folders.Add(folder);
        }

        message = SignKit.CreateText(root, "Message", string.Empty, 30, TextAnchor.MiddleCenter, SignKit.MetroRed, true);
        UiKit.Place(message, centre, new Vector2(0f, -425f), new Vector2(1200f, 44f));

        Text footer = SignKit.CreateText(root, "Footer", Footer, 24, TextAnchor.MiddleCenter, SignKit.InkDim, true);
        UiKit.Place(footer, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1600f, 34f));
    }

    public void Show(SongGenre genre)
    {
        gameObject.SetActive(true);
        Group.alpha = 1f;
        shownAt = Time.unscaledTime;
        messageUntil = 0f;
        selected = genre != null ? genre.Index : selected;
        foreach (Folder folder in folders)
        {
            folder.SongCount = charts.GetSongCount(folder.Genre);
            bool empty = folder.SongCount == 0;
            folder.Badge.SetCode(folder.Genre.Letter, SongGenre.FormatStation(folder.SongCount));
            folder.Badge.SetLineColor(empty ? SignKit.Disabled : folder.Genre.Color);
            folder.Badge.SetTextColor(empty ? SignKit.InkDim : SignKit.Ink);
            folder.Name.color = empty ? SignKit.InkDim : SignKit.Ink;
            folder.Scale = 1f;
        }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float age = now - shownAt;
        message.enabled = now < messageUntil;

        for (int index = 0; index < folders.Count; index++)
        {
            Folder folder = folders[index];
            bool isSelected = index == selected;
            folder.Scale = Mathf.Lerp(folder.Scale, isSelected ? SelectedScale : 1f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));

            // The badges pop in one after another.
            float pop = Ease.OutBack((age - index * 0.06f) / IntroSeconds);
            float shake = now < folder.ShakeUntil ? Mathf.Sin(now * 60f) * 10f * (folder.ShakeUntil - now) / 0.35f : 0f;
            float x = (index - (folders.Count - 1) * 0.5f) * BadgeSpacing;
            folder.Badge.Rect.anchoredPosition = new Vector2(x + shake, BadgeY);
            folder.Badge.Rect.localScale = Vector3.one * folder.Scale * Mathf.Lerp(0.6f, 1f, pop);
            folder.Badge.Group.alpha = Mathf.Clamp01(pop * 2f);

            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 4f);
            folder.Badge.SetHalo(isSelected && folder.SongCount > 0 ? 0.35f + 0.35f * pulse : 0f);
        }

        if (flow.IsBusy || age < 0.15f)
        {
            return;
        }

        if (MenuInput.Left)
        {
            Select((selected + folders.Count - 1) % folders.Count);
        }

        if (MenuInput.Right)
        {
            Select((selected + 1) % folders.Count);
        }

        if (MenuInput.Confirm)
        {
            MenuInput.Consume();
            Decide();
        }
        else if (MenuInput.Cancel)
        {
            MenuInput.Consume();
            flow.BackToTitle();
        }
    }

    private void Select(int index)
    {
        selected = Mathf.Clamp(index, 0, folders.Count - 1);
    }

    private void Decide()
    {
        Folder folder = folders[selected];
        if (folder.SongCount == 0)
        {
            folder.ShakeUntil = Time.unscaledTime + 0.35f;
            message.text = $"No songs in {folder.Genre.EnglishName} yet   ( set \"genre\": \"{folder.Genre.Id}\" in a chart )";
            messageUntil = Time.unscaledTime + 2.2f;
            return;
        }

        flow.OpenGenre(folder.Genre);
    }
}
