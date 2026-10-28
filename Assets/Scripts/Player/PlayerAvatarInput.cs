using UnityEngine;
using UnityEngine.InputSystem;
using BeefCity.Ragdoll;

namespace BeefCity.Controls
{
    /// <summary>
    /// Sits on a player's ragdoll and forwards that player's InputActions to the motor (movement) and combat
    /// (everything else). Buttons are looked up by name, so rebinding never touches this class.
    /// </summary>
    public class PlayerAvatarInput : MonoBehaviour
    {
        static readonly string[] ButtonActions = { "Jump", "Kick", "LeftArm", "RightArm", "Headbutt", "Taunt" };

        /// <summary>Set by the match during the countdown.</summary>
        public bool Locked;

        PlayerInput _input;
        InputAction _move;
        ActiveRagdollMotor _motor;
        RagdollCombat _combat;
        bool _bound;

        public void Bind(PlayerInput input)
        {
            Unbind();
            _input = input;
            _motor = GetComponent<ActiveRagdollMotor>();
            _combat = GetComponent<RagdollCombat>();

            InputActionAsset actions = input.actions;
            _move = actions["Move"];
            foreach (string name in ButtonActions)
            {
                InputAction a = actions[name];
                a.started += OnStarted;
                a.canceled += OnCanceled;
            }
            _bound = true;
        }

        void Unbind()
        {
            if (!_bound || _input == null) { _bound = false; return; }
            foreach (string name in ButtonActions)
            {
                InputAction a = _input.actions[name];
                a.started -= OnStarted;
                a.canceled -= OnCanceled;
            }
            _bound = false;
        }

        void OnDestroy()
        {
            Unbind();
        }

        void Update()
        {
            if (!_bound || _motor == null) return;
            _motor.SetMoveInput(Locked ? Vector2.zero : _move.ReadValue<Vector2>());
        }

        void OnStarted(InputAction.CallbackContext ctx)
        {
            if (Locked || _combat == null) return;
            switch (ctx.action.name)
            {
                case "Jump": _combat.JumpPressed(); break;
                case "Kick": _combat.KickPressed(); break;
                case "LeftArm": _combat.ArmButton(true, true); break;
                case "RightArm": _combat.ArmButton(false, true); break;
                case "Headbutt": _combat.HeadbuttButton(true); break;
                case "Taunt": _combat.TauntPressed(); break;
            }
        }

        void OnCanceled(InputAction.CallbackContext ctx)
        {
            if (_combat == null) return;
            // Releases are always delivered, even while locked, so nothing gets stuck held.
            switch (ctx.action.name)
            {
                case "LeftArm": _combat.ArmButton(true, false); break;
                case "RightArm": _combat.ArmButton(false, false); break;
                case "Headbutt": _combat.HeadbuttButton(false); break;
            }
        }
    }
}
