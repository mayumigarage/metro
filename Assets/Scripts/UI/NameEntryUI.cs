using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>
/// Player name entry, a sign board over the options (the PLAYER NAME row). Typed on
/// the keyboard (Japanese through the IME as well), or picked letter by letter from
/// the key grid with the stick / d-pad and A, or with the mouse. Saves to
/// <see cref="GameSettings.PlayerName"/>; an empty name shows as the default.
///
/// It reads the devices itself instead of through MenuInput: MenuInput treats W / A /
/// S / D as directions and Space / Backspace as confirm / cancel, which would fight
/// with typing. While it is open the options board behind it ignores input.
/// </summary>
public sealed class NameEntryUI : MonoBehaviour
{
    private const string Footer = "KEYS  TYPE / BACKSPACE / ENTER OK / ESC CANCEL        PAD  A INPUT / B DELETE / START OK";
    private const float RepeatDelaySeconds = 0.38f;
    private const float RepeatIntervalSeconds = 0.09f;
    private const float StickThreshold = 0.55f;
    private const float InputGraceSeconds = 0.12f;
    private const float CellSize = 56f;
    private const float CellGap = 6f;

    private static readonly string[] CharacterRows =
    {
        "ABCDEFGHIJKLM",
        "NOPQRSTUVWXYZ",
        "abcdefghijklm",
        "nopqrstuvwxyz",
        "0123456789-_.",
    };

    private enum KeyKind
    {
        Character,
        Space,
        Delete,
        Cancel,
        Ok
    }

    private sealed class Key
    {
        public KeyKind Kind;
        public char Character;
        public int Row;
        public int Column;
        public int Span = 1;
        public RoundedRectGraphic Face;
        public Text Label;
    }

    private readonly StringBuilder name = new StringBuilder();
    private readonly bool[] held = new bool[4];
    private readonly float[] nextRepeat = new float[4];

    private Key[] keys;
    private int columns;
    private int selected;
    private AngularGraphic band;
    private RoundedRectGraphic fieldBox;
    private Text fieldText;
    private Text countText;
    private Color lineColor = SignKit.MetroRed;
    private float openedAt;
    private string composition = string.Empty;
    private Keyboard listening;

    public bool IsOpen => gameObject.activeSelf;

    /// <summary>Raised when it closes, whether the name was saved or not.</summary>
    public event Action Closed;

    public void Build()
    {
        RectTransform root = (RectTransform)transform;
        Vector2 topLeft = new Vector2(0f, 1f);

        SignKit.CreateDim(root);

        columns = CharacterRows[0].Length;
        float gridWidth = columns * CellSize + (columns - 1) * CellGap;
        RectTransform board = UiKit.CreateRect(root, "Sign");
        UiKit.Place(board, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(gridWidth + 112f, 800f));
        Transform face = SignKit.CreateBoard(board, "Board", 16f, 14f).transform;

        SignKit.CreateHeading(face, "プレイヤー名", "Player Name", new Vector2(44f, -26f), 48);
        band = SignKit.CreateBand(face, "LineBand", lineColor, 148f, 14f);

        fieldBox = SignKit.CreateRoundedRect(face, "Field", SignKit.Ink, 10f, 3f);
        UiKit.Place(fieldBox, topLeft, new Vector2(40f, -190f), new Vector2(gridWidth, 76f));
        fieldText = SignKit.CreateText(fieldBox.transform, "Text", string.Empty, 38, TextAnchor.MiddleLeft, SignKit.Ink, true);
        fieldText.supportRichText = true;
        UiKit.Stretch(fieldText, 22f, 0f, 120f, 0f);
        fieldText.rectTransform.anchoredPosition += new Vector2(0f, 38 * SignKit.CapRaise * 0.5f);
        countText = SignKit.CreateText(fieldBox.transform, "Count", string.Empty, 18, TextAnchor.MiddleRight, SignKit.InkDim, true);
        UiKit.Stretch(countText, 0f, 0f, 20f, 0f);

        BuildKeys(face, new Vector2(40f, -296f));

        Text footer = SignKit.CreateText(face, "Footer", Footer, 16, TextAnchor.MiddleLeft, SignKit.InkDim, true);
        UiKit.Place(footer, new Vector2(0f, 0f), new Vector2(44f, 22f), new Vector2(gridWidth, 26f));

        gameObject.SetActive(false);
    }

    /// <param name="color">Colour of the band and the selected key's marker: the browsed song's line.</param>
    public void Open(Color color)
    {
        lineColor = color;
        band.color = color;
        name.Clear();
        string current = GameSettings.HasPlayerName ? GameSettings.PlayerName : string.Empty;
        name.Append(current);
        composition = string.Empty;
        selected = 0;
        openedAt = Time.unscaledTime;
        Array.Clear(held, 0, held.Length);
        gameObject.SetActive(true);
        StartListening();
        RefreshKeys();
        RefreshField();
    }

    public void Close()
    {
        StopListening();
        gameObject.SetActive(false);

        // The press that closed it must not also act on the options behind.
        MenuInput.Consume();
        Closed?.Invoke();
    }

    private void OnDisable()
    {
        StopListening();
    }

    private void Update()
    {
        // Keep the IME bound to whichever keyboard is current (it can change while open).
        if (listening != Keyboard.current)
        {
            StopListening();
            StartListening();
        }

        RefreshField();
        if (Time.unscaledTime - openedAt < InputGraceSeconds)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        bool composing = composition.Length > 0;

        // While the IME is composing, Enter / Backspace / Esc belong to it.
        if (keyboard != null && !composing)
        {
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                Submit();
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                DeleteLast();
            }
        }

        if (gamepad != null)
        {
            if (gamepad.startButton.wasPressedThisFrame)
            {
                Submit();
                return;
            }

            if (gamepad.buttonEast.wasPressedThisFrame)
            {
                DeleteLast();
            }

            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                Press(keys[selected]);
                if (!IsOpen)
                {
                    return;
                }
            }
        }

        // Arrow keys and the pad move over the grid; W / A / S / D are letters here.
        Vector2 stick = gamepad != null ? gamepad.leftStick.ReadValue() : Vector2.zero;
        bool arrows = keyboard != null && !composing;
        int moveY = 0;
        int moveX = 0;
        if (Repeat(0, (arrows && keyboard.upArrowKey.isPressed) || (gamepad != null && gamepad.dpad.up.isPressed) || stick.y > StickThreshold))
        {
            moveY = -1;
        }

        if (Repeat(1, (arrows && keyboard.downArrowKey.isPressed) || (gamepad != null && gamepad.dpad.down.isPressed) || stick.y < -StickThreshold))
        {
            moveY = 1;
        }

        if (Repeat(2, (arrows && keyboard.leftArrowKey.isPressed) || (gamepad != null && gamepad.dpad.left.isPressed) || stick.x < -StickThreshold))
        {
            moveX = -1;
        }

        if (Repeat(3, (arrows && keyboard.rightArrowKey.isPressed) || (gamepad != null && gamepad.dpad.right.isPressed) || stick.x > StickThreshold))
        {
            moveX = 1;
        }

        if (moveX != 0 || moveY != 0)
        {
            Move(moveX, moveY);
        }
    }

    private void BuildKeys(Transform face, Vector2 origin)
    {
        int rows = CharacterRows.Length;
        Key[] built = new Key[rows * columns + 4];
        int count = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                built[count++] = new Key { Kind = KeyKind.Character, Character = CharacterRows[row][column], Row = row, Column = column };
            }
        }

        // The bottom row: SPACE, DEL, CANCEL, OK over 4 + 3 + 3 + 3 columns.
        built[count++] = new Key { Kind = KeyKind.Space, Row = rows, Column = 0, Span = 4 };
        built[count++] = new Key { Kind = KeyKind.Delete, Row = rows, Column = 4, Span = 3 };
        built[count++] = new Key { Kind = KeyKind.Cancel, Row = rows, Column = 7, Span = 3 };
        built[count++] = new Key { Kind = KeyKind.Ok, Row = rows, Column = 10, Span = 3 };
        keys = built;

        Vector2 topLeft = new Vector2(0f, 1f);
        for (int index = 0; index < keys.Length; index++)
        {
            Key key = keys[index];
            float width = key.Span * CellSize + (key.Span - 1) * CellGap;
            float extraGap = key.Row == rows ? 14f : 0f;
            Vector2 position = origin + new Vector2(key.Column * (CellSize + CellGap), -key.Row * (CellSize + CellGap) - extraGap);

            key.Face = SignKit.CreateRoundedRect(face, $"Key_{index}", SignKit.Ink, 8f);
            UiKit.Place(key.Face, topLeft, position, new Vector2(width, CellSize));
            int size = key.Kind == KeyKind.Character ? 28 : 20;
            key.Label = SignKit.CreateText(key.Face.transform, "Label", GetKeyLabel(key), size, TextAnchor.MiddleCenter, SignKit.Ink, true);
            UiKit.Stretch(key.Label);
            key.Label.rectTransform.anchoredPosition = new Vector2(0f, size * SignKit.CapRaise);

            int keyIndex = index;
            UiPointerRelay relay = SignKit.AddRelay(key.Face);
            relay.Entered = () =>
            {
                selected = keyIndex;
                RefreshKeys();
            };
            relay.Clicked = eventData =>
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    selected = keyIndex;
                    Press(keys[keyIndex]);
                }
            };
        }
    }

    private static string GetKeyLabel(Key key)
    {
        switch (key.Kind)
        {
            case KeyKind.Space:
                return "SPACE";
            case KeyKind.Delete:
                return "DEL";
            case KeyKind.Cancel:
                return "CANCEL";
            case KeyKind.Ok:
                return "OK";
            default:
                return key.Character.ToString();
        }
    }

    private void Press(Key key)
    {
        switch (key.Kind)
        {
            case KeyKind.Character:
                Append(key.Character.ToString());
                break;
            case KeyKind.Space:
                Append(" ");
                break;
            case KeyKind.Delete:
                DeleteLast();
                break;
            case KeyKind.Cancel:
                Close();
                return;
            case KeyKind.Ok:
                Submit();
                return;
        }

        RefreshKeys();
    }

    private void Submit()
    {
        GameSettings.PlayerName = name.ToString();
        Close();
    }

    /// <summary>Adds what was typed, dropping control characters and anything past the length limit.</summary>
    private void Append(string text)
    {
        foreach (char character in text)
        {
            if (name.Length >= GameSettings.MaxPlayerNameLength)
            {
                break;
            }

            // Rich-text brackets and surrogate pairs (emoji) are left out: the HUD's font has no emoji.
            if (char.IsControl(character) || char.IsSurrogate(character) || character == '<' || character == '>')
            {
                continue;
            }

            // No leading spaces, and no runs of them.
            if (character == ' ' && (name.Length == 0 || name[name.Length - 1] == ' '))
            {
                continue;
            }

            name.Append(character);
        }

        RefreshField();
    }

    private void DeleteLast()
    {
        if (name.Length > 0)
        {
            name.Length--;
        }

        RefreshField();
    }

    private void Move(int moveX, int moveY)
    {
        Key current = keys[selected];
        int rowCount = CharacterRows.Length + 1;
        int row = current.Row;
        int column = current.Column;

        if (moveY != 0)
        {
            // Keep the column, measured from the middle of a wide key.
            column = current.Column + (current.Span - 1) / 2;
            row = (row + moveY + rowCount) % rowCount;
        }
        else
        {
            column = moveX > 0 ? current.Column + current.Span : current.Column - 1;
            column = (column + columns) % columns;
        }

        selected = FindKey(row, column);
        RefreshKeys();
    }

    /// <summary>The key covering grid cell (<paramref name="row"/>, <paramref name="column"/>).</summary>
    private int FindKey(int row, int column)
    {
        for (int index = 0; index < keys.Length; index++)
        {
            Key key = keys[index];
            if (key.Row == row && column >= key.Column && column < key.Column + key.Span)
            {
                return index;
            }
        }

        return selected;
    }

    private bool Repeat(int index, bool isHeld)
    {
        float now = Time.unscaledTime;
        bool fire = false;
        if (isHeld && !held[index])
        {
            fire = true;
            nextRepeat[index] = now + RepeatDelaySeconds;
        }
        else if (isHeld && now >= nextRepeat[index])
        {
            fire = true;
            nextRepeat[index] = now + RepeatIntervalSeconds;
        }

        held[index] = isHeld;
        return fire;
    }

    private void RefreshKeys()
    {
        for (int index = 0; index < keys.Length; index++)
        {
            Key key = keys[index];
            bool isSelected = index == selected;
            bool action = key.Kind != KeyKind.Character;
            key.Face.BorderWidth = isSelected ? 0f : 2f;
            key.Face.color = isSelected ? SignKit.Ink : UiKit.WithAlpha(SignKit.Ink, GameSettings.DarkMode ? (action ? 0.12f : 0.04f) : (action ? 0.45f : 0.22f));
            key.Label.color = isSelected ? (key.Kind == KeyKind.Ok ? lineColor : SignKit.OnInk) : SignKit.Ink;
        }
    }

    private void RefreshField()
    {
        // The IME's composition shows grey after the name, then a blinking caret.
        string caret = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f ? "|" : " ";
        string pending = composition.Length > 0 ? $"<color=#{ColorUtility.ToHtmlStringRGB(SignKit.InkDim)}>{Escape(composition)}</color>" : string.Empty;
        string shown = name.Length > 0 || composition.Length > 0
            ? Escape(name.ToString()) + pending
            : $"<color=#{ColorUtility.ToHtmlStringRGB(SignKit.Disabled)}>{GameSettings.DefaultPlayerName}</color>";
        fieldText.text = $"{shown}<color=#{ColorUtility.ToHtmlStringRGB(lineColor)}>{caret}</color>";
        countText.text = $"{name.Length} / {GameSettings.MaxPlayerNameLength}";
        fieldBox.color = lineColor;
    }

    private static string Escape(string text)
    {
        return text.Replace("<", string.Empty).Replace(">", string.Empty);
    }

    private void StartListening()
    {
        listening = Keyboard.current;
        if (listening == null)
        {
            return;
        }

        listening.onTextInput += OnTextInput;
        listening.onIMECompositionChange += OnComposition;
        listening.SetIMEEnabled(true);
    }

    private void StopListening()
    {
        if (listening == null)
        {
            return;
        }

        listening.onTextInput -= OnTextInput;
        listening.onIMECompositionChange -= OnComposition;
        listening.SetIMEEnabled(false);
        listening = null;
        composition = string.Empty;
    }

    private void OnTextInput(char character)
    {
        if (Time.unscaledTime - openedAt < InputGraceSeconds)
        {
            return;
        }

        Append(character.ToString());
    }

    private void OnComposition(IMECompositionString text)
    {
        composition = text.ToString();
    }
}
