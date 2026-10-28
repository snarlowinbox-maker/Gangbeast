using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using BeefCity.Core;
using BeefCity.Customization;

namespace BeefCity.Controls
{
    /// <summary>One person playing on this machine (a keyboard+mouse user or one gamepad).</summary>
    public class LocalPlayer
    {
        public PlayerInput Input;
        public int Index;
        public string Name;
        public CostumeData Costume;
        public string Scheme => Input != null ? Input.currentControlScheme : "";
    }

    /// <summary>
    /// Local multiplayer for up to 8 players using Unity's PlayerInputManager. Joining is only open while the
    /// local-game lobby panel is visible: the next button press on an unused device creates a player.
    /// Also owns the shared InputActions template that the rebinding menu edits.
    /// </summary>
    public class LocalPlayerManager : MonoBehaviour
    {
        public const int MaxPlayers = 8;
        public static LocalPlayerManager Instance { get; private set; }

        public InputActionAsset Template { get; private set; }
        public readonly List<LocalPlayer> Players = new List<LocalPlayer>();
        public event Action Changed;

        PlayerInputManager _pim;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            Template = BeefCityControls.Create();
            string overrides = SaveSystem.Data.bindingOverrides;
            if (!string.IsNullOrEmpty(overrides)) Template.LoadBindingOverridesFromJson(overrides);
            Template.FindAction("Menu", true).Enable(); // the rebinding menu listens to this on the template

            // The "prefab" PlayerInputManager clones for every joining player. Built inactive, in code.
            var prefab = new GameObject("PlayerInputPrefab");
            prefab.SetActive(false);
            prefab.transform.SetParent(transform, false);
            PlayerInput template = prefab.AddComponent<PlayerInput>();
            template.actions = Template;
            template.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            template.defaultActionMap = BeefCityControls.MapName;
            template.neverAutoSwitchControlSchemes = true;

            var managerGo = new GameObject("PlayerInputManager");
            managerGo.SetActive(false);
            managerGo.transform.SetParent(transform, false);
            _pim = managerGo.AddComponent<PlayerInputManager>();
            _pim.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            _pim.joinBehavior = PlayerJoinBehavior.JoinPlayersWhenButtonIsPressed;
            _pim.maxPlayerCount = MaxPlayers;
            _pim.playerPrefab = prefab;
            managerGo.SetActive(true);
            _pim.DisableJoining();
            _pim.onPlayerJoined += OnJoined;
            _pim.onPlayerLeft += OnLeft;
        }

        void OnDestroy()
        {
            if (_pim != null)
            {
                _pim.onPlayerJoined -= OnJoined;
                _pim.onPlayerLeft -= OnLeft;
            }
            if (Instance == this) Instance = null;
        }

        public void EnableJoining()
        {
            if (_pim != null && Players.Count < MaxPlayers) _pim.EnableJoining();
        }

        public void DisableJoining()
        {
            if (_pim != null) _pim.DisableJoining();
        }

        void OnJoined(PlayerInput pi)
        {
            pi.transform.SetParent(transform, false); // survives scene changes (it is cloned into the active scene)

            int index = Players.Count;
            Players.Add(new LocalPlayer
            {
                Input = pi,
                Index = index,
                Name = index == 0 ? SaveSystem.Data.playerName : "Player " + (index + 1),
                Costume = index == 0 ? SaveSystem.Data.costume : CostumeData.FromSlot(index)
            });

            string overrides = SaveSystem.Data.bindingOverrides;
            if (!string.IsNullOrEmpty(overrides)) pi.actions.LoadBindingOverridesFromJson(overrides);

            if (Players.Count >= MaxPlayers) DisableJoining();
            Changed?.Invoke();
        }

        void OnLeft(PlayerInput pi)
        {
            Players.RemoveAll(p => p.Input == pi);
            for (int i = 0; i < Players.Count; i++) Players[i].Index = i;
            Changed?.Invoke();
        }

        /// <summary>Removes every joined player (lobby "remove all" button).</summary>
        public void Clear()
        {
            foreach (LocalPlayer p in Players.ToArray())
                if (p.Input != null) Destroy(p.Input.gameObject);
            Players.Clear();
            Changed?.Invoke();
        }

        /// <summary>Pushes new binding overrides to the template and to every joined player's own action copy.</summary>
        public void ApplyOverrides(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Template.RemoveAllBindingOverrides();
                foreach (LocalPlayer p in Players) p.Input.actions.RemoveAllBindingOverrides();
            }
            else
            {
                Template.LoadBindingOverridesFromJson(json);
                foreach (LocalPlayer p in Players) p.Input.actions.LoadBindingOverridesFromJson(json);
            }
            SaveSystem.Data.bindingOverrides = json ?? "";
            SaveSystem.Save();
        }
    }
}
