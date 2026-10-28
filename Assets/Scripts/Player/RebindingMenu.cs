using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using BeefCity.UI;

namespace BeefCity.Controls
{
    /// <summary>
    /// In-game rebinding overlay. Opens with Tab (keyboard) or Start (gamepad), or from Settings > Controls.
    /// Every action in the Player map can be remapped for either control scheme; overrides are saved in the
    /// profile and pushed to all joined local players immediately.
    /// </summary>
    public class RebindingMenu : MonoBehaviour
    {
        public static RebindingMenu Instance { get; private set; }

        Canvas _canvas;
        RectTransform _list;
        Text _hint;
        Button _keyboardTab, _gamepadTab;
        int _scheme; // 0 = keyboard & mouse, 1 = gamepad
        InputActionRebindingExtensions.RebindingOperation _op;

        public bool IsOpen => _canvas != null && _canvas.gameObject.activeSelf;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            BuildUI();
        }

        void OnDestroy()
        {
            if (_op != null) { _op.Dispose(); _op = null; }
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (_op != null) return; // swallow the menu button while waiting for a new binding
            LocalPlayerManager lpm = LocalPlayerManager.Instance;
            if (lpm == null) return;
            if (lpm.Template.FindAction("Menu", true).WasPressedThisFrame()) Toggle();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            UIKit.EnsureEventSystem();
            _canvas.gameObject.SetActive(true);
            _hint.text = "Click Rebind, then press the new control.  Esc cancels.";
            Populate();
        }

        public void Close()
        {
            CancelRebind();
            _canvas.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ UI

        void BuildUI()
        {
            _canvas = UIKit.CreateCanvas("RebindingCanvas", 200);
            _canvas.transform.SetParent(transform, false);

            RectTransform dim = UIKit.AddPanel(_canvas.transform, "Dim", new Color(0f, 0f, 0f, 0.72f));
            UIKit.Stretch(dim);

            RectTransform panel = UIKit.AddPanel(dim, "Panel", UIKit.PanelColor);
            UIKit.Anchor(panel, new Vector2(0.25f, 0.05f), new Vector2(0.75f, 0.95f));
            UIKit.Column(panel, 8f, 24, TextAnchor.UpperCenter);

            UIKit.AddLabel(panel, "CONTROLS", 46, TextAnchor.MiddleCenter, UIKit.Accent, 60f);
            RectTransform tabs = UIKit.AddRow(panel, 52f, 12f);
            _keyboardTab = UIKit.AddButton(tabs, "KEYBOARD & MOUSE", () => SetScheme(0), 330f, 52f, 24);
            _gamepadTab = UIKit.AddButton(tabs, "GAMEPAD", () => SetScheme(1), 330f, 52f, 24);

            _hint = UIKit.AddLabel(panel, "", 22, TextAnchor.MiddleCenter, UIKit.Dim, 34f);

            _list = UIKit.NewRect("List", panel);
            UIKit.Layout(_list, 700f, 640f);
            UIKit.Column(_list, 6f, 0, TextAnchor.UpperCenter);

            RectTransform bottom = UIKit.AddRow(panel, 56f, 12f);
            UIKit.AddButton(bottom, "RESET TO DEFAULTS", ResetDefaults, 330f, 56f, 24);
            UIKit.AddButton(bottom, "CLOSE", Close, 330f, 56f, 24);

            _canvas.gameObject.SetActive(false);
        }

        void SetScheme(int scheme)
        {
            CancelRebind();
            _scheme = scheme;
            Populate();
        }

        void Populate()
        {
            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                Transform child = _list.GetChild(i);
                child.SetParent(null);
                Destroy(child.gameObject);
            }

            LocalPlayerManager lpm = LocalPlayerManager.Instance;
            if (lpm == null) return;
            string group = _scheme == 0 ? BeefCityControls.KeyboardScheme : BeefCityControls.GamepadScheme;

            foreach (string actionName in BeefCityControls.ActionNames)
            {
                InputAction action = lpm.Template.FindAction(actionName, true);
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding binding = action.bindings[i];
                    if (binding.isComposite || !InGroup(binding, group)) continue;

                    string label = binding.isPartOfComposite ? actionName + "  (" + binding.name + ")" : actionName;
                    AddRow(action, i, label);
                }
            }
        }

        static bool InGroup(InputBinding binding, string group)
        {
            return !string.IsNullOrEmpty(binding.groups) && (";" + binding.groups + ";").Contains(";" + group + ";");
        }

        void AddRow(InputAction action, int bindingIndex, string label)
        {
            RectTransform row = UIKit.AddRow(_list, 46f, 12f, TextAnchor.MiddleLeft);
            Text name = UIKit.AddLabel(row, label, 26, TextAnchor.MiddleLeft);
            UIKit.Layout(name.rectTransform, 300f, 46f);
            Text value = UIKit.AddLabel(row, action.GetBindingDisplayString(bindingIndex), 26, TextAnchor.MiddleLeft, UIKit.Accent);
            UIKit.Layout(value.rectTransform, 220f, 46f);
            UIKit.AddButton(row, "REBIND", () => StartRebind(action, bindingIndex, value, label), 150f, 44f, 22);
        }

        // ------------------------------------------------------------------ rebinding

        void StartRebind(InputAction action, int bindingIndex, Text value, string label)
        {
            CancelRebind();
            _hint.text = "Press a control for " + label + "  (Esc to cancel)";
            value.text = "...";

            bool wasEnabled = action.enabled;
            action.Disable(); // an action must be disabled while it is being rebound

            InputActionRebindingExtensions.RebindingOperation op = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f);

            if (_scheme == 0)
            {
                op.WithControlsExcluding("<Gamepad>")
                  .WithControlsExcluding("<Mouse>/position")
                  .WithControlsExcluding("<Mouse>/delta")
                  .WithControlsExcluding("<Mouse>/scroll")
                  .WithControlsExcluding("<Pointer>/position")
                  .WithControlsExcluding("<Pointer>/delta");
            }
            else
            {
                op.WithControlsHavingToMatchPath("<Gamepad>");
            }

            op.OnComplete(o =>
            {
                Finish(action, wasEnabled, value, bindingIndex);
                _hint.text = "Saved.";
            });
            op.OnCancel(o =>
            {
                Finish(action, wasEnabled, value, bindingIndex);
                _hint.text = "Cancelled.";
            });
            _op = op;
            op.Start();
        }

        void Finish(InputAction action, bool wasEnabled, Text value, int bindingIndex)
        {
            if (_op != null) { _op.Dispose(); _op = null; }
            if (wasEnabled) action.Enable();
            if (value != null) value.text = action.GetBindingDisplayString(bindingIndex);

            LocalPlayerManager lpm = LocalPlayerManager.Instance;
            if (lpm != null) lpm.ApplyOverrides(lpm.Template.SaveBindingOverridesAsJson());
        }

        void CancelRebind()
        {
            if (_op == null) return;
            _op.Cancel(); // triggers OnCancel, which restores the action and refreshes the row
        }

        void ResetDefaults()
        {
            CancelRebind();
            LocalPlayerManager lpm = LocalPlayerManager.Instance;
            if (lpm != null) lpm.ApplyOverrides("");
            _hint.text = "Controls reset to defaults.";
            Populate();
        }
    }
}
