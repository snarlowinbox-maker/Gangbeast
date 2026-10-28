using UnityEngine;
using UnityEngine.InputSystem;

namespace BeefCity.Controls
{
    /// <summary>
    /// The game's InputActions asset, created in code (no .inputactions file needed).
    /// Keyboard/Mouse and Gamepad control schemes, one "Player" action map:
    ///   Move  WASD / Left Stick      Jump  Space / A          Kick  M / X
    ///   LeftArm  Left Mouse / LB     RightArm  Right Mouse / RB
    ///   Headbutt (tap) and Crouch (hold)  Ctrl / B           Taunt  Shift / Y
    ///   Menu  Tab / Start (opens the rebinding menu)
    /// Climb and Throwing Foes are combinations of these (both arm buttons + jump / taunt), handled by RagdollCombat.
    /// </summary>
    public static class BeefCityControls
    {
        public const string KeyboardScheme = "Keyboard&Mouse";
        public const string GamepadScheme = "Gamepad";
        public const string MapName = "Player";

        public static readonly string[] ActionNames =
            { "Move", "Jump", "Kick", "LeftArm", "RightArm", "Headbutt", "Taunt", "Menu" };

        public static InputActionAsset Create()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "BeefCityControls";
            InputActionMap map = asset.AddActionMap(MapName);

            InputAction move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", KeyboardScheme)
                .With("Down", "<Keyboard>/s", KeyboardScheme)
                .With("Left", "<Keyboard>/a", KeyboardScheme)
                .With("Right", "<Keyboard>/d", KeyboardScheme);
            move.AddBinding("<Gamepad>/leftStick", groups: GamepadScheme, processors: "stickDeadzone");

            AddButton(map, "Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            AddButton(map, "Kick", "<Keyboard>/m", "<Gamepad>/buttonWest");
            AddButton(map, "LeftArm", "<Mouse>/leftButton", "<Gamepad>/leftShoulder");
            AddButton(map, "RightArm", "<Mouse>/rightButton", "<Gamepad>/rightShoulder");
            AddButton(map, "Headbutt", "<Keyboard>/leftCtrl", "<Gamepad>/buttonEast");
            AddButton(map, "Taunt", "<Keyboard>/leftShift", "<Gamepad>/buttonNorth");
            AddButton(map, "Menu", "<Keyboard>/tab", "<Gamepad>/start");

            asset.AddControlScheme(KeyboardScheme)
                 .WithBindingGroup(KeyboardScheme)
                 .WithRequiredDevice("<Keyboard>")
                 .WithRequiredDevice("<Mouse>");
            asset.AddControlScheme(GamepadScheme)
                 .WithBindingGroup(GamepadScheme)
                 .WithRequiredDevice("<Gamepad>");

            return asset;
        }

        static void AddButton(InputActionMap map, string name, string keyboardOrMouse, string gamepad)
        {
            InputAction a = map.AddAction(name, InputActionType.Button);
            a.AddBinding(keyboardOrMouse, groups: KeyboardScheme);
            a.AddBinding(gamepad, groups: GamepadScheme);
        }
    }
}
