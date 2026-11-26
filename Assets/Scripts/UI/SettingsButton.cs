using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The settings button in the top-left corner of the menu screens: a station-code box the
/// same shape and size as the screens' "M00" box (TitleModeSelect / GenreSelectUI, 156 x 96,
/// 6 px ink outline) mirrored to the left, with a gear in it. Also the settings board's
/// "× 閉じる" plate. Neither animates. Clicks go through the UiPointerRelay on the returned rect.
/// </summary>
public static class SettingsButton
{
    private const int Teeth = 8;

    /// <summary>The gear box, anchored to <paramref name="parent"/>'s top-left corner.</summary>
    public static RectTransform CreateGear(Transform parent, string name)
    {
        RectTransform root = UiKit.CreateRect(parent, name);
        UiKit.Place(root, new Vector2(0f, 1f), new Vector2(64f, -58f), new Vector2(156f, 96f));

        // Filled with the face so it reads cleanly over whatever the screen draws there.
        RoundedRectGraphic fill = SignKit.CreateRoundedRect(root, "Fill", SignKit.Face, 10f);
        UiKit.Stretch(fill);
        RoundedRectGraphic border = SignKit.CreateRoundedRect(root, "Border", SignKit.Ink, 10f, 6f);
        UiKit.Stretch(border);
        AddRelay(root, fill);

        RectTransform gear = UiKit.CreateRect(root, "Gear");
        UiKit.Place(gear, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f));
        CircleGraphic ring = SignKit.CreateCircle(gear, "Ring", SignKit.Ink, 13f);
        UiKit.Place(ring, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f));
        for (int tooth = 0; tooth < Teeth; tooth++)
        {
            RectTransform pivot = UiKit.CreateRect(gear, "Tooth");
            UiKit.Place(pivot, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            pivot.localEulerAngles = new Vector3(0f, 0f, tooth * 360f / Teeth);
            AngularGraphic block = UiKit.CreatePanel(pivot, "Block", SignKit.Ink);
            UiKit.Place(block, new Vector2(0.5f, 0.5f), new Vector2(0f, 25f), new Vector2(13f, 14f));
        }

        return root;
    }

    /// <summary>The "× 閉じる" plate, anchored to <paramref name="parent"/>'s top-right corner.</summary>
    public static RectTransform CreateClose(Transform parent, string name)
    {
        RectTransform root = UiKit.CreateRect(parent, name);
        UiKit.Place(root, new Vector2(1f, 1f), new Vector2(-40f, -6f), new Vector2(160f, 50f));

        RoundedRectGraphic plate = SignKit.CreateRoundedRect(root, "Plate", SignKit.Face, 25f);
        UiKit.Stretch(plate);
        RoundedRectGraphic border = SignKit.CreateRoundedRect(root, "Border", SignKit.Ink, 25f, 3f);
        UiKit.Stretch(border);
        AddRelay(root, plate);

        Text cross = SignKit.CreateText(root, "Cross", "×", 30, TextAnchor.MiddleCenter, SignKit.Ink, true);
        UiKit.Place(cross, new Vector2(0f, 0.5f), new Vector2(18f, 30f * SignKit.CapRaise * 0.5f), new Vector2(30f, 44f));

        Text text = SignKit.CreateText(root, "Label", "閉じる", 24, TextAnchor.MiddleLeft, SignKit.Ink, true);
        UiKit.Stretch(text, 52f, 0f, 12f, 0f);
        // Kanji fill more of the line box than capitals, so they need less of the raise.
        text.rectTransform.anchoredPosition += new Vector2(0f, 24f * SignKit.CapRaise * 0.4f);
        return root;
    }

    /// <summary>Clicks on <paramref name="target"/> go to a relay on the root, where callers find it.</summary>
    private static void AddRelay(RectTransform root, Graphic target)
    {
        UiPointerRelay relay = SignKit.AddRelay(target);
        UiPointerRelay rootRelay = root.gameObject.AddComponent<UiPointerRelay>();
        relay.Clicked = eventData => rootRelay.Clicked?.Invoke(eventData);
    }
}
