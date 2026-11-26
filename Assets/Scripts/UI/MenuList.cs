using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A vertical list of angular rows inside a container: action rows (confirm runs
/// them), value rows (left / right changes them) and headers. Driven by MenuInput
/// through <see cref="Tick"/> and by the mouse; rows that do not fit scroll.
/// </summary>
public sealed class MenuList
{
    public sealed class Item
    {
        public string Label;

        /// <summary>One line explaining the row, for the screen to show under the list.</summary>
        public string Description;

        /// <summary>Text on the right of the row; re-read on every Refresh.</summary>
        public Func<string> GetValue;
        public Action Confirm;

        /// <summary>Called with -1 / +1 when the row's value is stepped.</summary>
        public Action<int> Adjust;
        public bool IsHeader;

        internal RectTransform Rect;
        internal AngularGraphic Background;
        internal AngularGraphic Marker;
        internal Text LabelText;
        internal Text ValueText;
    }

    /// <summary>
    /// Dark: angular translucent rows for dark screens. Sign: square rows on a sign
    /// board's off-white face, the selected one inked black with a line-colour marker.
    /// </summary>
    public enum Look
    {
        Dark,
        Sign
    }

    // Clicking in this much of a value row's right end steps the value down / up.
    private const float ValueZoneWidth = 280f;

    private static readonly Color SelectedFill = new Color(0.10f, 0.30f, 0.42f, 0.95f);
    private static readonly Color SelectedFade = new Color(0.06f, 0.12f, 0.24f, 0.90f);
    private static Color SignRow => UiKit.WithAlpha(SignKit.Ink, GameSettings.DarkMode ? 0.012f : 0.05f);

    private readonly RectTransform container;
    private readonly float rowHeight;
    private readonly float spacing;
    private readonly int labelSize;
    private readonly Look look;
    private readonly List<Item> items = new List<Item>();
    private int selected;
    private int firstVisible;
    private Color lineColor = SignKit.MetroRed;

    /// <summary>When false the list ignores the mouse (another list has the focus).</summary>
    public bool Interactable = true;

    public event Action SelectionChanged;

    public MenuList(RectTransform container, float rowHeight, float spacing, int labelSize, Look look = Look.Dark)
    {
        this.container = container;
        this.rowHeight = rowHeight;
        this.spacing = spacing;
        this.labelSize = labelSize;
        this.look = look;
    }

    /// <summary>Colour of the selection marker in the Sign look (the song's line colour).</summary>
    public Color LineColor
    {
        get => lineColor;
        set
        {
            lineColor = value;
            Refresh();
        }
    }

    public int Count => items.Count;
    public int SelectedIndex => selected;
    public Item Selected => selected >= 0 && selected < items.Count ? items[selected] : null;

    public Item AddAction(string label, string description, Action confirm, Func<string> getValue = null)
    {
        return Add(new Item { Label = label, Description = description, Confirm = confirm, GetValue = getValue });
    }

    public Item AddValue(string label, string description, Func<string> getValue, Action<int> adjust)
    {
        return Add(new Item { Label = label, Description = description, GetValue = getValue, Adjust = adjust });
    }

    public Item AddHeader(string label)
    {
        return Add(new Item { Label = label, IsHeader = true });
    }

    public void Select(int index)
    {
        if (index < 0 || index >= items.Count || items[index].IsHeader || index == selected)
        {
            return;
        }

        selected = index;
        Refresh();
        SelectionChanged?.Invoke();
    }

    /// <summary>Handles this frame's keyboard / gamepad input. Call it only while the list has the focus.</summary>
    public void Tick()
    {
        if (items.Count == 0)
        {
            return;
        }

        if (MenuInput.Up)
        {
            Move(-1);
        }

        if (MenuInput.Down)
        {
            Move(1);
        }

        Item item = Selected;
        if (item == null)
        {
            return;
        }

        if (item.Adjust != null)
        {
            if (MenuInput.Left)
            {
                Step(item, -1);
            }

            if (MenuInput.Right)
            {
                Step(item, 1);
            }
        }

        if (MenuInput.Confirm)
        {
            Activate(item);
        }
    }

    /// <summary>Re-reads every value and redraws the selection.</summary>
    public void Refresh()
    {
        if (items.Count == 0)
        {
            return;
        }

        if (items[selected].IsHeader)
        {
            selected = FindSelectable(selected, 1);
        }

        int visibleCount = Mathf.Max(1, Mathf.FloorToInt((container.rect.height + spacing) / (rowHeight + spacing)));
        firstVisible = Mathf.Clamp(firstVisible, Mathf.Max(0, selected - visibleCount + 1), selected);
        firstVisible = Mathf.Clamp(firstVisible, 0, Mathf.Max(0, items.Count - visibleCount));

        for (int index = 0; index < items.Count; index++)
        {
            Item item = items[index];
            bool visible = index >= firstVisible && index < firstVisible + visibleCount;
            item.Rect.gameObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }

            item.Rect.anchoredPosition = new Vector2(0f, -(index - firstVisible) * (rowHeight + spacing));
            if (item.IsHeader)
            {
                continue;
            }

            bool isSelected = index == selected;
            if (look == Look.Sign)
            {
                Color fill = isSelected ? SignKit.Ink : SignRow;
                item.Background.color = fill;
                item.Background.SetGradient(fill);
                item.Marker.enabled = isSelected;
                item.Marker.color = lineColor;
                item.LabelText.color = isSelected ? SignKit.OnInk : SignKit.Ink;
                if (item.ValueText != null)
                {
                    string value = item.GetValue != null ? item.GetValue() : string.Empty;
                    item.ValueText.text = item.Adjust != null ? $"<   {value}   >" : value;
                    item.ValueText.color = isSelected ? SignKit.OnInk : SignKit.InkSoft;
                }

                continue;
            }

            item.Background.color = isSelected ? SelectedFill : UiKit.PanelSoft;
            item.Background.SetGradient(isSelected ? SelectedFade : UiKit.WithAlpha(UiKit.PanelSoft, 0.45f));
            item.Marker.enabled = isSelected;
            item.LabelText.color = isSelected ? UiKit.TextMain : UiKit.TextDim;
            if (item.ValueText != null)
            {
                string value = item.GetValue != null ? item.GetValue() : string.Empty;
                item.ValueText.text = item.Adjust != null ? $"<   {value}   >" : value;
                item.ValueText.color = isSelected ? UiKit.Accent : UiKit.WithAlpha(UiKit.Accent, 0.7f);
            }
        }
    }

    private Item Add(Item item)
    {
        int index = items.Count;
        items.Add(item);

        RectTransform rect = UiKit.CreateRect(container, $"Row_{item.Label}");
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, rowHeight);
        item.Rect = rect;

        bool sign = look == Look.Sign;
        if (item.IsHeader)
        {
            Text header = CreateText(rect, "Label", item.Label, Mathf.RoundToInt(labelSize * 0.72f), TextAnchor.LowerLeft, sign ? SignKit.InkSoft : UiKit.Accent);
            UiKit.Stretch(header, 8f, 6f, 8f, 0f);
            AngularGraphic rule = UiKit.CreatePanel(rect, "Rule", sign ? UiKit.WithAlpha(SignKit.Ink, 0.3f) : UiKit.WithAlpha(UiKit.Accent, 0.35f));
            rule.rectTransform.anchorMin = new Vector2(0f, 0f);
            rule.rectTransform.anchorMax = new Vector2(1f, 0f);
            rule.rectTransform.pivot = new Vector2(0.5f, 0f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 2f);
            rule.rectTransform.anchoredPosition = Vector2.zero;
            return item;
        }

        float cut = sign ? 0f : rowHeight * 0.3f;
        item.Background = UiKit.CreatePanel(rect, "Background", UiKit.PanelSoft, cut, 0f, cut, 0f);
        item.Background.raycastTarget = true;
        UiKit.Stretch(item.Background);

        // The selection marker runs down the left edge, below the cut corner.
        item.Marker = UiKit.CreatePanel(rect, "Marker", UiKit.Accent);
        item.Marker.rectTransform.anchorMin = new Vector2(0f, 0f);
        item.Marker.rectTransform.anchorMax = new Vector2(0f, 1f);
        item.Marker.rectTransform.pivot = new Vector2(0f, 0.5f);
        item.Marker.rectTransform.offsetMin = new Vector2(0f, 0f);
        item.Marker.rectTransform.offsetMax = new Vector2(sign ? 10f : 7f, -cut);

        // Legacy Text sits low in its rect; the sign font is raised to the row's middle.
        float raise = sign ? labelSize * SignKit.CapRaise : 0f;
        item.LabelText = CreateText(rect, "Label", item.Label, labelSize, TextAnchor.MiddleLeft, UiKit.TextDim);
        UiKit.Stretch(item.LabelText, cut + (sign ? 28f : 16f), raise, 24f, -raise);

        if (item.GetValue != null)
        {
            item.ValueText = CreateText(rect, "Value", string.Empty, labelSize, TextAnchor.MiddleRight, UiKit.Accent);
            item.ValueText.supportRichText = false;
            UiKit.Stretch(item.ValueText, 24f, raise, cut + (sign ? 24f : 14f), -raise);
        }

        UiPointerRelay relay = item.Background.gameObject.AddComponent<UiPointerRelay>();
        relay.Entered = () =>
        {
            if (Interactable)
            {
                Select(index);
            }
        };
        relay.Clicked = eventData => OnClicked(index, eventData);

        if (items.Count == 1 || items[selected].IsHeader)
        {
            selected = index;
        }

        return item;
    }

    private Text CreateText(Transform parent, string name, string content, int size, TextAnchor alignment, Color color)
    {
        return look == Look.Sign
            ? SignKit.CreateText(parent, name, content, size, alignment, color, true)
            : UiKit.CreateText(parent, name, content, size, alignment, color, true);
    }

    private void OnClicked(int index, PointerEventData eventData)
    {
        if (!Interactable || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        Select(index);
        Item item = items[index];
        if (item.Adjust == null)
        {
            Activate(item);
            return;
        }

        // The left half of the value zone steps down, the right half up.
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(item.Rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
        {
            return;
        }

        float zoneStart = item.Rect.rect.xMax - ValueZoneWidth;
        if (local.x >= zoneStart)
        {
            Step(item, local.x < zoneStart + ValueZoneWidth * 0.5f ? -1 : 1);
        }
    }

    private void Activate(Item item)
    {
        if (item.Confirm != null)
        {
            // The press that confirmed must not reach the screen this opens.
            MenuInput.Consume();
            item.Confirm();
        }
        else if (item.Adjust != null)
        {
            Step(item, 1);
        }
    }

    private void Step(Item item, int direction)
    {
        item.Adjust(direction);
        Refresh();
    }

    private void Move(int direction)
    {
        int target = FindSelectable(selected + direction, direction);
        if (target == selected)
        {
            return;
        }

        selected = target;
        Refresh();
        SelectionChanged?.Invoke();
    }

    /// <summary>The first row at or after <paramref name="start"/> (wrapping) that can be selected.</summary>
    private int FindSelectable(int start, int direction)
    {
        int count = items.Count;
        for (int step = 0; step < count; step++)
        {
            int index = ((start + step * direction) % count + count) % count;
            if (!items[index].IsHeader)
            {
                return index;
            }
        }

        return selected;
    }
}
