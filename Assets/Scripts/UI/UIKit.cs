using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace BeefCity.UI
{
    public sealed class CyclerUI
    {
        readonly string[] _options;
        readonly Action<int> _onChange;
        public Text Value;
        public Button Prev, Next;
        public int Index { get; private set; }

        public CyclerUI(string[] options, int index, Action<int> onChange)
        {
            _options = options;
            _onChange = onChange;
            Index = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1));
        }

        public void Step(int dir) => SetIndex((Index + dir + _options.Length) % _options.Length, true);

        public void SetIndex(int i, bool notify)
        {
            if (_options.Length == 0) return;
            Index = Mathf.Clamp(i, 0, _options.Length - 1);
            if (Value != null) Value.text = _options[Index];
            if (notify && _onChange != null) _onChange(Index);
        }

        public void SetInteractable(bool v)
        {
            if (Prev != null) Prev.interactable = v;
            if (Next != null) Next.interactable = v;
        }
    }

    public sealed class SwatchUI
    {
        public Button Button;
        public Outline Outline;
        public void SetSelected(bool selected) { if (Outline != null) Outline.enabled = selected; }
    }

    public sealed class ProgressBarUI
    {
        public RectTransform Fill;
        public Text Label;

        public void SetValue(float v, string text = null)
        {
            v = Mathf.Clamp01(v);
            if (Fill != null) Fill.anchorMax = new Vector2(v, 1f);
            if (Label != null) Label.text = text ?? Mathf.RoundToInt(v * 100f) + "%";
        }
    }

    /// <summary>Tiny immediate-style helpers so every menu is built from code (no prefabs, no art).</summary>
    public static class UIKit
    {
        public static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.10f, 0.88f);
        public static readonly Color Accent = new Color(0.98f, 0.55f, 0.18f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.18f, 0.24f, 1f);
        public static readonly Color TextColor = Color.white;
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

        static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        // ------------------------------------------------------------------ canvas + event system

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        public static bool AnyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            Mouse m = Mouse.current;
            Gamepad g = Gamepad.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame)
                || (m != null && m.leftButton.wasPressedThisFrame)
                || (g != null && g.buttonSouth.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.anyKeyDown;
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------ layout helpers

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Fixed-size placement relative to an anchor point (e.g. top-right corner button).</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static void Layout(RectTransform rt, float width, float height)
        {
            LayoutElement le = rt.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            if (width > 0f) le.preferredWidth = width;
            if (height > 0f) le.preferredHeight = height;
        }

        public static VerticalLayoutGroup Column(RectTransform rt, float spacing = 12f, int padding = 20, TextAnchor align = TextAnchor.UpperCenter)
        {
            VerticalLayoutGroup g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        public static RectTransform AddRow(Transform parent, float height = 56f, float spacing = 12f, TextAnchor align = TextAnchor.MiddleCenter)
        {
            RectTransform rt = NewRect("Row", parent);
            Layout(rt, -1f, height);
            HorizontalLayoutGroup g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return rt;
        }

        public static RectTransform AddGrid(Transform parent, float cell, float spacing, int columns, float height)
        {
            RectTransform rt = NewRect("Grid", parent);
            Layout(rt, -1f, height);
            GridLayoutGroup g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(cell, cell);
            g.spacing = new Vector2(spacing, spacing);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            g.childAlignment = TextAnchor.UpperCenter;
            return rt;
        }

        public static void AddSpacer(Transform parent, float height)
        {
            RectTransform rt = NewRect("Spacer", parent);
            Layout(rt, -1f, height);
        }

        // ------------------------------------------------------------------ widgets

        public static RectTransform AddPanel(Transform parent, string name, Color color)
        {
            RectTransform rt = NewRect(name, parent);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        public static Text AddLabel(Transform parent, string text, int size = 28, TextAnchor align = TextAnchor.MiddleCenter,
                                    Color? color = null, float height = 0f)
        {
            RectTransform rt = NewRect("Label", parent);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? TextColor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (height > 0f) Layout(rt, -1f, height);
            return t;
        }

        public static Button AddButton(Transform parent, string label, Action onClick, float width = 360f, float height = 60f, int fontSize = 30)
        {
            RectTransform rt = NewRect(label + " Button", parent);
            Layout(rt, width, height);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = Color.white;
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;

            ColorBlock cb = b.colors;
            cb.normalColor = ButtonColor;
            cb.highlightedColor = Accent;
            cb.selectedColor = new Color(0.8f, 0.5f, 0.2f);
            cb.pressedColor = new Color(0.7f, 0.4f, 0.12f);
            cb.disabledColor = new Color(0.12f, 0.13f, 0.17f, 0.6f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.06f;
            b.colors = cb;

            Text t = AddLabel(rt, label, fontSize);
            Stretch(t.rectTransform);
            b.onClick.AddListener(() => { if (onClick != null) onClick(); });
            return b;
        }

        public static void SetButtonText(Button b, string text)
        {
            if (b == null) return;
            Text t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static Button AddToggleButton(Transform parent, string label, bool value, Action<bool> onChanged, float width = 360f)
        {
            bool state = value;
            Button b = null;
            b = AddButton(parent, label, () =>
            {
                state = !state;
                SetButtonText(b, label + ": " + (state ? "ON" : "OFF"));
                if (onChanged != null) onChanged(state);
            }, width);
            SetButtonText(b, label + ": " + (state ? "ON" : "OFF"));
            return b;
        }

        public static Slider AddSlider(Transform parent, string label, float min, float max, float value, Action<float> onChanged)
        {
            RectTransform row = AddRow(parent, 56f, 16f);
            Text l = AddLabel(row, label, 26, TextAnchor.MiddleLeft);
            Layout(l.rectTransform, 220f, 56f);

            RectTransform sRt = NewRect("Slider", row);
            Layout(sRt, 380f, 40f);
            Slider slider = sRt.gameObject.AddComponent<Slider>();

            RectTransform bg = AddPanel(sRt, "Background", new Color(0.1f, 0.11f, 0.15f, 1f));
            bg.anchorMin = new Vector2(0f, 0.3f);
            bg.anchorMax = new Vector2(1f, 0.7f);
            bg.offsetMin = bg.offsetMax = Vector2.zero;

            RectTransform fillArea = NewRect("Fill Area", sRt);
            fillArea.anchorMin = new Vector2(0f, 0.3f);
            fillArea.anchorMax = new Vector2(1f, 0.7f);
            fillArea.offsetMin = new Vector2(5f, 0f);
            fillArea.offsetMax = new Vector2(-15f, 0f);
            RectTransform fill = AddPanel(fillArea, "Fill", Accent);
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.sizeDelta = new Vector2(10f, 0f);

            RectTransform handleArea = NewRect("Handle Slide Area", sRt);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            RectTransform handle = AddPanel(handleArea, "Handle", Color.white);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(22f, 0f);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v => { if (onChanged != null) onChanged(v); });
            return slider;
        }

        public static InputField AddInput(Transform parent, string initial, string placeholder, int maxChars,
                                          float width = 360f, float height = 56f)
        {
            RectTransform rt = NewRect("Input", parent);
            Layout(rt, width, height);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.1f, 0.11f, 0.15f, 1f);
            InputField field = rt.gameObject.AddComponent<InputField>();

            Text text = AddLabel(rt, "", 28, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(14f, 4f);
            text.rectTransform.offsetMax = new Vector2(-14f, -4f);
            text.supportRichText = false;

            Text ph = AddLabel(rt, placeholder, 28, TextAnchor.MiddleLeft, Dim);
            Stretch(ph.rectTransform);
            ph.rectTransform.offsetMin = new Vector2(14f, 4f);
            ph.rectTransform.offsetMax = new Vector2(-14f, -4f);
            ph.fontStyle = FontStyle.Italic;

            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = img;
            field.characterLimit = maxChars;
            field.text = initial;
            return field;
        }

        public static ProgressBarUI AddProgressBar(Transform parent, float width, float height)
        {
            RectTransform bg = AddPanel(parent, "ProgressBar", new Color(0.1f, 0.11f, 0.15f, 1f));
            Layout(bg, width, height);
            RectTransform fill = AddPanel(bg, "Fill", Accent);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Text label = AddLabel(bg, "0%", 26);
            Stretch(label.rectTransform);
            return new ProgressBarUI { Fill = fill, Label = label };
        }

        public static SwatchUI AddSwatch(Transform parent, Color color, Action onClick)
        {
            RectTransform rt = NewRect("Swatch", parent);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            b.colors = cb;
            b.onClick.AddListener(() => { if (onClick != null) onClick(); });
            Outline outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(4f, -4f);
            outline.enabled = false;
            return new SwatchUI { Button = b, Outline = outline };
        }

        public static CyclerUI AddCycler(Transform parent, string label, string[] options, int index, Action<int> onChange)
        {
            var cycler = new CyclerUI(options, index, onChange);
            RectTransform row = AddRow(parent, 52f, 8f);
            Text l = AddLabel(row, label, 24, TextAnchor.MiddleLeft);
            Layout(l.rectTransform, 110f, 52f);
            cycler.Prev = AddButton(row, "<", () => cycler.Step(-1), 52f, 52f, 28);
            cycler.Value = AddLabel(row, options.Length > 0 ? options[cycler.Index] : "-", 24);
            Layout(cycler.Value.rectTransform, 150f, 52f);
            cycler.Next = AddButton(row, ">", () => cycler.Step(1), 52f, 52f, 28);
            return cycler;
        }
    }
}
