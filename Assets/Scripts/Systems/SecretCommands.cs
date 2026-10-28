using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using BeefCity.Core;
using BeefCity.Networking;

namespace BeefCity.Controls
{
    /// <summary>
    /// Secret keyboard commands. Only active during a match, and only for the host when playing offline or in a
    /// private lobby.
    ///   1 attractive force   2 pushing force            (at the mouse cursor)
    ///   3 small balls  4 ball  5 box  6 dumpster  7 boneloaf
    ///   9 enemy AI                                       (a controller-less dummy until the Phase 6 AI exists)
    ///   Shift+1-8 or Ctrl+1-8  spawn / move that player slot to the cursor
    ///   -  slow motion   +  fast motion   0  realtime
    ///   F1 day/night prototype     Backspace hitbox debugger
    /// </summary>
    public class SecretCommands : MonoBehaviour
    {
        /// <summary>Phase 6's enemy spawner subscribes to this. With no subscriber a dummy ragdoll is spawned instead.</summary>
        public static event Action<Vector3> EnemySpawnRequested;

        static float _baseFixedDelta = -1f;
        HitboxDebugger _hitboxes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            EnemySpawnRequested = null;
            _baseFixedDelta = -1f;
        }

        public static bool Allowed
        {
            get
            {
                if (MatchManager.Current == null) return false;
                NetworkManager nm = NetworkManager.Singleton;
                bool online = nm != null && nm.IsListening;
                if (!online) return true; // offline: always allowed
                NetworkLobbyManager lobby = NetworkLobbyManager.Instance;
                return nm.IsServer && lobby != null && lobby.IsPrivate; // online: host of a private lobby only
            }
        }

        /// <summary>Puts time and lighting back to normal (called when a match ends).</summary>
        public static void ResetTime()
        {
            Time.timeScale = 1f;
            if (_baseFixedDelta > 0f) Time.fixedDeltaTime = _baseFixedDelta;
            DayNight.Reset();
        }

        void Awake()
        {
            if (_baseFixedDelta < 0f) _baseFixedDelta = Time.fixedDeltaTime;
            _hitboxes = gameObject.AddComponent<HitboxDebugger>();
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !Allowed) return;
            if (RebindingMenu.Instance != null && RebindingMenu.Instance.IsOpen) return;

            MatchManager match = MatchManager.Current;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;

            for (int n = 1; n <= 9; n++)
            {
                if (!kb[Digit(n)].wasPressedThisFrame) continue;
                if (!CursorPoint(out Vector3 point)) continue;

                if ((shift || ctrl) && n <= 8)
                {
                    match.SpawnPlayerAt(n - 1, point + Vector3.up * 0.1f);
                    continue;
                }

                switch (n)
                {
                    case 1: ForceField.Spawn(point + Vector3.up * 0.5f, true); break;
                    case 2: ForceField.Spawn(point + Vector3.up * 0.5f, false); break;
                    case 3: case 4: case 5: case 6: case 7:
                        PropFactory.Spawn((PropType)(n - 3), point + Vector3.up * 1.5f);
                        break;
                    case 9: SpawnEnemy(point + Vector3.up * 0.1f, match); break;
                }
            }

            // Time manipulation
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) SetTime(0.3f, true);
            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) SetTime(2.5f, false);
            if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) SetTime(1f, false);

            if (kb.f1Key.wasPressedThisFrame) DayNight.Toggle();
            if (kb.backspaceKey.wasPressedThisFrame && _hitboxes != null) _hitboxes.Show = !_hitboxes.Show;
        }

        static Key Digit(int n) => n == 0 ? Key.Digit0 : (Key)((int)Key.Digit1 + n - 1);

        static void SetTime(float scale, bool scaleFixedStep)
        {
            Time.timeScale = scale;
            // Slow motion also shrinks the physics step so it stays smooth; fast motion keeps the normal step.
            Time.fixedDeltaTime = scaleFixedStep ? _baseFixedDelta * scale : _baseFixedDelta;
        }

        static void SpawnEnemy(Vector3 position, MatchManager match)
        {
            if (EnemySpawnRequested != null) EnemySpawnRequested(position);
            else match.SpawnDummy(position, new Color(0.85f, 0.2f, 0.2f));
        }

        static bool CursorPoint(out Vector3 point)
        {
            point = Vector3.zero;
            Camera cam = Camera.main;
            Mouse mouse = Mouse.current;
            if (cam == null || mouse == null) return false;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                return true;
            }
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }
            return false;
        }
    }
}
