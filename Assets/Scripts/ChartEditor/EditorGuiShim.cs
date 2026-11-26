using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Runtime stand-ins for the UnityEditor GUI helpers the chart editor was written with
// (EditorGUILayout, EditorGUI, EditorStyles, Handles, Undo ...), so that the editor can
// be a screen of the game itself. They live in their own namespace so they never collide
// with the real UnityEditor classes.
namespace MetroEd
{
    public enum MessageType
    {
        None,
        Info,
        Warning,
        Error
    }

    /// <summary>Per-frame bookkeeping for the shims: control numbering and deferred state changes.</summary>
    public static class GuiState
    {
        internal static int PopupCounter;
        internal static int FieldCounter;
        internal static int OpenPopup = -1;
        internal static int PendingOpenPopup = -2;
        internal static readonly Dictionary<int, string> FieldBuffers = new Dictionary<int, string>();

        /// <summary>Call at the start of every OnGUI pass.</summary>
        public static void BeginFrame()
        {
            PopupCounter = 0;
            FieldCounter = 0;
            // Layout changes caused by a click are applied on the next layout pass, so
            // the controls of one pass always match the layout that was measured.
            if (Event.current.type == EventType.Layout && PendingOpenPopup != -2)
            {
                OpenPopup = PendingOpenPopup;
                PendingOpenPopup = -2;
            }
        }

        /// <summary>True while a text field has the keyboard.</summary>
        public static bool EditingText => GUIUtility.keyboardControl != 0;
    }

    public static class EditorGUIUtility
    {
        public static float labelWidth = 80f;

        public static bool editingTextField => GuiState.EditingText;
    }

    public static class EditorStyles
    {
        private static GUIStyle miniLabelStyle;
        private static GUIStyle boldLabelStyle;
        private static GUIStyle miniBoldLabelStyle;
        private static GUIStyle wrappedMiniLabelStyle;
        private static GUIStyle toolbarStyle;
        private static GUIStyle toolbarButtonStyle;
        private static GUIStyle miniButtonStyle;
        private static GUIStyle radioStyle;
        private static GUIStyle helpBoxStyle;

        public static GUIStyle miniLabel => miniLabelStyle ??= Make(GUI.skin.label, 11, FontStyle.Normal, false);
        public static GUIStyle boldLabel => boldLabelStyle ??= Make(GUI.skin.label, 13, FontStyle.Bold, false);
        public static GUIStyle miniBoldLabel => miniBoldLabelStyle ??= Make(GUI.skin.label, 11, FontStyle.Bold, false);
        public static GUIStyle wordWrappedMiniLabel => wrappedMiniLabelStyle ??= Make(GUI.skin.label, 11, FontStyle.Normal, true);
        public static GUIStyle toolbar => toolbarStyle ??= new GUIStyle(GUI.skin.box) { margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(2, 2, 1, 1) };
        public static GUIStyle toolbarButton => toolbarButtonStyle ??= Make(GUI.skin.button, 12, FontStyle.Normal, false);
        public static GUIStyle toolbarPopup => toolbarButton;
        public static GUIStyle miniButton => miniButtonStyle ??= Make(GUI.skin.button, 11, FontStyle.Normal, false);
        public static GUIStyle radioButton => radioStyle ??= new GUIStyle(GUI.skin.toggle);
        public static GUIStyle helpBox => helpBoxStyle ??= Make(GUI.skin.box, 11, FontStyle.Normal, true, TextAnchor.MiddleLeft);

        private static GUIStyle Make(GUIStyle source, int size, FontStyle fontStyle, bool wrap, TextAnchor? anchor = null)
        {
            GUIStyle style = new GUIStyle(source) { fontSize = size, fontStyle = fontStyle, wordWrap = wrap };
            if (anchor.HasValue)
            {
                style.alignment = anchor.Value;
            }

            return style;
        }
    }

    public static class EditorGUI
    {
        private static readonly Stack<bool> changeStack = new Stack<bool>();

        public static void DrawRect(Rect rect, Color color)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
            }
        }

        public static void BeginChangeCheck()
        {
            changeStack.Push(GUI.changed);
            GUI.changed = false;
        }

        public static bool EndChangeCheck()
        {
            bool changed = GUI.changed;
            GUI.changed = changeStack.Count > 0 && changeStack.Pop() || changed;
            return changed;
        }

        public readonly struct DisabledScope : IDisposable
        {
            private readonly bool previous;

            public DisabledScope(bool disabled)
            {
                previous = GUI.enabled;
                GUI.enabled = previous && !disabled;
            }

            public void Dispose()
            {
                GUI.enabled = previous;
            }
        }
    }

    public static class EditorGUILayout
    {
        private static readonly GUILayoutOption[] NoOptions = new GUILayoutOption[0];

        public static void BeginHorizontal() => GUILayout.BeginHorizontal();
        public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginHorizontal(style, options);
        public static void EndHorizontal() => GUILayout.EndHorizontal();
        public static void BeginVertical(params GUILayoutOption[] options) => GUILayout.BeginVertical(options);
        public static void EndVertical() => GUILayout.EndVertical();
        public static Vector2 BeginScrollView(Vector2 scroll) => GUILayout.BeginScrollView(scroll, false, false);
        public static void EndScrollView() => GUILayout.EndScrollView();

        public static void LabelField(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            GUILayout.Label(text, style, options);
        }

        public static void HelpBox(string message, MessageType type)
        {
            string prefix = type == MessageType.Warning ? "⚠ " : type == MessageType.Error ? "✖ " : type == MessageType.Info ? "ⓘ " : string.Empty;
            GUILayout.Label(prefix + message, EditorStyles.helpBox);
        }

        public static bool Foldout(bool value, string label, bool toggleOnLabelClick)
        {
            return GUILayout.Toggle(value, (value ? "▼ " : "▶ ") + label, EditorStyles.boldLabel);
        }

        public static bool Toggle(string label, bool value)
        {
            BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            value = GUILayout.Toggle(value, GUIContent.none);
            EndHorizontal();
            return value;
        }

        public static bool ToggleLeft(string label, bool value)
        {
            return GUILayout.Toggle(value, label);
        }

        // ---------------------------------------------------------------- fields

        public static string TextField(string label, string value)
        {
            BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            string result = ChartLoader.RemoveTagBrackets(GUILayout.TextField(value ?? string.Empty));
            EndHorizontal();
            return result;
        }

        public static int IntField(int value, params GUILayoutOption[] options)
        {
            return (int)NumberField(value, options);
        }

        public static int IntSlider(string label, int value, int min, int max)
        {
            BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            float slider = GUILayout.HorizontalSlider(value, min, max);
            int result = Mathf.Clamp(Mathf.RoundToInt(slider), min, max);
            GUILayout.Label(result.ToString(), GUILayout.Width(26f));
            EndHorizontal();
            return result;
        }

        public static float Slider(string label, float value, float min, float max)
        {
            BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            float result = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(result.ToString("0.00"), GUILayout.Width(34f));
            EndHorizontal();
            return result;
        }

        public static float FloatField(string label, float value)
        {
            BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            float result = FloatField(value);
            EndHorizontal();
            return result;
        }

        public static float FloatField(float value, params GUILayoutOption[] options)
        {
            return (float)NumberField(value, options);
        }

        public static double DoubleField(string label, double value)
        {
            return DoubleField(new GUIContent(label), value);
        }

        public static double DoubleField(GUIContent label, double value)
        {
            BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            double result = NumberField(value);
            EndHorizontal();
            return result;
        }

        public static double DoubleField(double value, params GUILayoutOption[] options)
        {
            return NumberField(value, options);
        }

        /// <summary>
        /// A text field that edits a number. While it has the keyboard it shows what is being
        /// typed (so "0." and "-" survive) and applies the value whenever the text parses.
        /// </summary>
        private static double NumberField(double value, params GUILayoutOption[] options)
        {
            int id = GuiState.FieldCounter++;
            string name = "num" + id;
            bool focused = GUI.GetNameOfFocusedControl() == name;
            string shown = focused && GuiState.FieldBuffers.TryGetValue(id, out string buffered)
                ? buffered
                : value.ToString("0.####", CultureInfo.InvariantCulture);

            GUI.SetNextControlName(name);
            string edited = GUILayout.TextField(shown, options);
            if (edited != shown)
            {
                GuiState.FieldBuffers[id] = edited;
                if (double.TryParse(edited, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    GUI.changed = true;
                    return parsed;
                }
            }
            else if (!focused)
            {
                GuiState.FieldBuffers.Remove(id);
            }

            return value;
        }

        // ---------------------------------------------------------------- popups

        public static int Popup(int selected, string[] options, params GUILayoutOption[] layout)
        {
            return DoPopup(selected, options, null, layout);
        }

        public static int Popup(int selected, string[] options, GUIStyle style, params GUILayoutOption[] layout)
        {
            return DoPopup(selected, options, style, layout);
        }

        public static int Popup(string label, int selected, string[] options)
        {
            return Popup(new GUIContent(label), selected, options);
        }

        public static int Popup(GUIContent label, int selected, string[] options)
        {
            BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            int result = DoPopup(selected, options, null, NoOptions);
            EndHorizontal();
            return result;
        }

        /// <summary>A button that opens its choices as a list right under it.</summary>
        private static int DoPopup(int selected, string[] options, GUIStyle style, GUILayoutOption[] layout)
        {
            int id = GuiState.PopupCounter++;
            bool open = GuiState.OpenPopup == id;
            string shown = selected >= 0 && selected < options.Length ? options[selected] : "-";

            GUILayout.BeginVertical(layout);
            if (GUILayout.Button(shown + (open ? "  ▴" : "  ▾"), style ?? GUI.skin.button))
            {
                GuiState.PendingOpenPopup = open ? -1 : id;
            }

            int result = selected;
            if (open)
            {
                for (int index = 0; index < options.Length; index++)
                {
                    string text = (index == selected ? "● " : "　") + options[index];
                    if (GUILayout.Button(text, EditorStyles.miniButton))
                    {
                        result = index;
                        GuiState.PendingOpenPopup = -1;
                        GUI.changed = true;
                    }
                }
            }

            GUILayout.EndVertical();
            return result;
        }
    }

    /// <summary>
    /// Lines and small polygons for the IMGUI screen. Lines are GL quads in screen pixels, cut
    /// here at <see cref="SetClip"/>: IMGUI clips a rotated texture as if it were not rotated, so
    /// the old rotated-rectangle lines spilled out of their area or vanished near its right edge.
    /// Polygons are filled with one-pixel rows, which IMGUI clips correctly.
    /// </summary>
    public static class Handles
    {
        public static Color color = Color.white;

        private static Material lineMaterial;
        private static Vector2 baseOffset;
        private static Rect clip;
        private static bool clipSet;

        /// <summary>Call once per OnGUI pass at the top level (no clip, identity matrix).</summary>
        public static void BeginFrame()
        {
            // GUIToScreenPoint may add the editor window's position; measuring from the top-level
            // origin keeps every point in game-screen pixels.
            baseOffset = GUIUtility.GUIToScreenPoint(Vector2.zero);
            ClearClip();
        }

        /// <summary>Lines drawn from now on are cut at <paramref name="localRect"/> (in the current GUI space).</summary>
        public static void SetClip(Rect localRect)
        {
            Vector2 min = ToScreen(localRect.min);
            Vector2 max = ToScreen(localRect.max);
            clip = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            clipSet = true;
        }

        public static void ClearClip()
        {
            clipSet = false;
        }

        private static Vector2 ToScreen(Vector2 point)
        {
            return GUIUtility.GUIToScreenPoint(point) - baseOffset;
        }

        public static void DrawLine(Vector3 from, Vector3 to)
        {
            DrawSegment(from, to, color, 1.5f);
        }

        public static void DrawAAPolyLine(float width, params Vector3[] points)
        {
            if (Event.current.type != EventType.Repaint || points.Length < 2 || !BeginLines(color))
            {
                return;
            }

            for (int index = 0; index < points.Length - 1; index++)
            {
                AddSegment(ToScreen(points[index]), ToScreen(points[index + 1]), width);
            }

            EndLines();
        }

        public static void DrawAAConvexPolygon(params Vector3[] points)
        {
            if (Event.current.type != EventType.Repaint || points.Length < 3)
            {
                return;
            }

            // Fan of triangles, each filled with one-pixel rows.
            for (int index = 1; index < points.Length - 1; index++)
            {
                FillTriangle(points[0], points[index], points[index + 1], color);
            }
        }

        public static void DrawWireDisc(Vector3 center, Vector3 normal, float radius)
        {
            if (Event.current.type != EventType.Repaint || !BeginLines(color))
            {
                return;
            }

            const int segments = 48;
            Vector2 previous = new Vector2(center.x + radius, center.y);
            for (int index = 1; index <= segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                Vector2 next = new Vector2(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius);
                AddSegment(ToScreen(previous), ToScreen(next), 1.5f);
                previous = next;
            }

            EndLines();
        }

        public static void DrawSegment(Vector2 from, Vector2 to, Color lineColor, float width)
        {
            if (Event.current.type != EventType.Repaint || !BeginLines(lineColor))
            {
                return;
            }

            AddSegment(ToScreen(from), ToScreen(to), width);
            EndLines();
        }

        private static bool BeginLines(Color lineColor)
        {
            if (lineMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                if (shader == null)
                {
                    return false;
                }

                lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                lineMaterial.SetInt("_ZWrite", 0);
                lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }

            if (!clipSet)
            {
                clip = new Rect(0f, 0f, Screen.width, Screen.height);
            }

            lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            GL.Begin(GL.QUADS);
            Color shown = lineColor * GUI.color;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                shown = new Color(Mathf.GammaToLinearSpace(shown.r), Mathf.GammaToLinearSpace(shown.g), Mathf.GammaToLinearSpace(shown.b), shown.a);
            }

            GL.Color(shown);
            return true;
        }

        private static void EndLines()
        {
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>One quad from <paramref name="from"/> to <paramref name="to"/> (screen pixels), cut at the clip rect.</summary>
        private static void AddSegment(Vector2 from, Vector2 to, float width)
        {
            if (!ClipSegment(clip, ref from, ref to))
            {
                return;
            }

            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f)
            {
                return;
            }

            // The line's width is clamped at the clip rect too, so a line along an edge stays inside.
            Vector2 across = new Vector2(-delta.y, delta.x) / length * (width * 0.5f);
            GL.Vertex(Clamp(from + across));
            GL.Vertex(Clamp(to + across));
            GL.Vertex(Clamp(to - across));
            GL.Vertex(Clamp(from - across));
        }

        private static Vector3 Clamp(Vector2 point)
        {
            return new Vector3(Mathf.Clamp(point.x, clip.xMin, clip.xMax), Mathf.Clamp(point.y, clip.yMin, clip.yMax), 0f);
        }

        /// <summary>Liang–Barsky: shortens the segment to its part inside <paramref name="rect"/>.</summary>
        private static bool ClipSegment(Rect rect, ref Vector2 from, ref Vector2 to)
        {
            float t0 = 0f;
            float t1 = 1f;
            Vector2 delta = to - from;
            if (!ClipEdge(-delta.x, from.x - rect.xMin, ref t0, ref t1)
                || !ClipEdge(delta.x, rect.xMax - from.x, ref t0, ref t1)
                || !ClipEdge(-delta.y, from.y - rect.yMin, ref t0, ref t1)
                || !ClipEdge(delta.y, rect.yMax - from.y, ref t0, ref t1))
            {
                return false;
            }

            Vector2 start = from + delta * t0;
            to = from + delta * t1;
            from = start;
            return true;
        }

        private static bool ClipEdge(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-6f)
            {
                return q >= 0f;
            }

            float r = q / p;
            if (p < 0f)
            {
                if (r > t1)
                {
                    return false;
                }

                t0 = Mathf.Max(t0, r);
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                t1 = Mathf.Min(t1, r);
            }

            return true;
        }

        private static void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Color fill)
        {
            float top = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float bottom = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            for (float y = Mathf.Floor(top); y <= bottom; y += 1f)
            {
                float sample = y + 0.5f;
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                Cross(a, b, sample, ref minX, ref maxX);
                Cross(b, c, sample, ref minX, ref maxX);
                Cross(c, a, sample, ref minX, ref maxX);
                if (minX < maxX)
                {
                    GUI.DrawTexture(new Rect(minX, y, maxX - minX, 1f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, fill, 0f, 0f);
                }
            }
        }

        private static void Cross(Vector2 from, Vector2 to, float y, ref float minX, ref float maxX)
        {
            if (from.y == to.y || y < Mathf.Min(from.y, to.y) || y > Mathf.Max(from.y, to.y))
            {
                return;
            }

            float x = from.x + (y - from.y) / (to.y - from.y) * (to.x - from.x);
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
        }
    }

    /// <summary>
    /// Undo / redo by whole-chart snapshots (JsonUtility keeps every field). A group (a drag)
    /// is one entry: only the state before its first change is kept.
    /// </summary>
    public static class Undo
    {
        private const int MaxEntries = 200;
        private const double MergeSeconds = 0.6d;

        private static readonly List<string> undoStack = new List<string>();
        private static readonly List<string> redoStack = new List<string>();
        private static bool inGroup;
        private static bool groupRecorded;
        private static string lastName;
        private static double lastTime;

        public static event Action undoRedoPerformed;

        public static void ClearUndo(ChartEditorDocument document)
        {
            undoStack.Clear();
            redoStack.Clear();
            inGroup = false;
            lastName = null;
        }

        /// <summary>Call before changing the chart.</summary>
        public static void RecordObject(ChartEditorDocument document, string name)
        {
            if (inGroup)
            {
                if (groupRecorded)
                {
                    return;
                }

                groupRecorded = true;
            }
            else
            {
                // Typing in a field changes the value on every key; those are one step.
                double now = Time.realtimeSinceStartupAsDouble;
                bool merge = name == lastName && now - lastTime < MergeSeconds;
                lastName = name;
                lastTime = now;
                if (merge)
                {
                    return;
                }
            }

            Push(document);
        }

        public static void IncrementCurrentGroup()
        {
        }

        public static void SetCurrentGroupName(string name)
        {
        }

        /// <summary>Starts a group: all RecordObject calls up to the collapse count once.</summary>
        public static int GetCurrentGroup()
        {
            inGroup = true;
            groupRecorded = false;
            return 0;
        }

        /// <summary>Ends the group; a group that changed nothing leaves no entry.</summary>
        public static void CollapseUndoOperations(int group, ChartEditorDocument document)
        {
            if (groupRecorded && undoStack.Count > 0 && undoStack[undoStack.Count - 1] == JsonUtility.ToJson(document.chart))
            {
                undoStack.RemoveAt(undoStack.Count - 1);
            }

            inGroup = false;
            groupRecorded = false;
            lastName = null;
        }

        public static bool PerformUndo(ChartEditorDocument document)
        {
            return Step(document, undoStack, redoStack);
        }

        public static bool PerformRedo(ChartEditorDocument document)
        {
            return Step(document, redoStack, undoStack);
        }

        private static bool Step(ChartEditorDocument document, List<string> from, List<string> to)
        {
            if (from.Count == 0)
            {
                return false;
            }

            to.Add(JsonUtility.ToJson(document.chart));
            string json = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            document.chart = JsonUtility.FromJson<SongChartData>(json);
            inGroup = false;
            lastName = null;
            undoRedoPerformed?.Invoke();
            return true;
        }

        private static void Push(ChartEditorDocument document)
        {
            undoStack.Add(JsonUtility.ToJson(document.chart));
            if (undoStack.Count > MaxEntries)
            {
                undoStack.RemoveAt(0);
            }

            redoStack.Clear();
        }
    }
}
