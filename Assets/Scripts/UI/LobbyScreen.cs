using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using BeefCity.Controls;
using BeefCity.Core;
using BeefCity.Networking;

namespace BeefCity.UI
{
    /// <summary>
    /// Three panels in one screen: offline choices (local / host / join), the local-players panel, and the online room
    /// (lobby code, private/public, roster, ready, start).
    /// </summary>
    public sealed class LobbyScreen : MenuScreen
    {
        enum Panel { Offline, Local, Room }

        RectTransform _offline, _local, _room;
        CyclerUI _mode, _stage, _roomMode, _roomStage;
        InputField _code;
        Text _message, _localList, _codeLabel, _roster;
        Button _privacy, _ready, _start;
        bool _readyState;
        float _refreshT;

        static readonly string[] ModeNames = Enum.GetNames(typeof(GameModeId));

        protected override string Title => "Lobby";

        protected override void Build(RectTransform root)
        {
            RectTransform bg = UIKit.AddPanel(root, "Bg", new Color(0.03f, 0.04f, 0.07f, 0.94f));
            UIKit.Stretch(bg);

            _offline = NewPanel(root, "Offline");
            UIKit.AddLabel(_offline, "PLAY", 56, TextAnchor.MiddleCenter, UIKit.Accent, 80f);
            _mode = UIKit.AddCycler(_offline, "Mode", ModeNames, 0, i => Game.Settings.mode = (GameModeId)i);
            _stage = UIKit.AddCycler(_offline, "Stage", StageRegistry.Names, 0, i => Game.Settings.stageId = StageRegistry.Ids[i]);
            UIKit.AddSpacer(_offline, 8f);
            UIKit.AddButton(_offline, "LOCAL GAME", OpenLocal, 560f, 66f, 32);
            UIKit.AddButton(_offline, "HOST CUSTOM GAME", HostGame, 560f, 66f, 32);
            RectTransform joinRow = UIKit.AddRow(_offline, 60f, 10f);
            _code = UIKit.AddInput(joinRow, "", "Lobby code", 20, 330f, 60f);
            UIKit.AddButton(joinRow, "JOIN", JoinGame, 210f, 60f, 28);
            UIKit.AddButton(_offline, "BACK", () => Game.Show(ScreenId.Main), 560f, 56f, 28);

            _local = NewPanel(root, "Local");
            UIKit.AddLabel(_local, "LOCAL GAME", 56, TextAnchor.MiddleCenter, UIKit.Accent, 80f);
            UIKit.AddLabel(_local, "Press any button on a controller (or click / press a key) to join. Up to 8 players.", 26,
                           TextAnchor.MiddleCenter, UIKit.Dim, 80f);
            _localList = UIKit.AddLabel(_local, "", 30, TextAnchor.UpperCenter, UIKit.TextColor, 330f);
            UIKit.Layout(_localList.rectTransform, 700f, 330f);
            UIKit.AddButton(_local, "START MATCH", () => Game.StartLocalMatch(), 560f, 66f, 32);
            UIKit.AddButton(_local, "REMOVE ALL PLAYERS", () =>
            {
                if (LocalPlayerManager.Instance != null) LocalPlayerManager.Instance.Clear();
            }, 560f, 52f, 24);
            UIKit.AddButton(_local, "BACK", () => ShowPanel(Panel.Offline), 560f, 56f, 28);

            _room = NewPanel(root, "Room");
            _codeLabel = UIKit.AddLabel(_room, "CODE", 52, TextAnchor.MiddleCenter, UIKit.Accent, 70f);
            _privacy = UIKit.AddButton(_room, "PRIVATE (O)", () =>
            {
                NetworkLobbyManager l = NetworkLobbyManager.Instance;
                if (l != null) l.RequestSetPrivate(!l.IsPrivate);
            }, 360f, 52f, 26);
            _roomMode = UIKit.AddCycler(_room, "Mode", ModeNames, 0, _ => SendSettings());
            _roomStage = UIKit.AddCycler(_room, "Stage", StageRegistry.Names, 0, _ => SendSettings());
            _roster = UIKit.AddLabel(_room, "", 30, TextAnchor.UpperCenter, UIKit.TextColor, 300f);
            UIKit.Layout(_roster.rectTransform, 700f, 300f);
            RectTransform row = UIKit.AddRow(_room, 66f, 12f);
            _ready = UIKit.AddButton(row, "READY", ToggleReady, 270f, 66f, 30);
            _start = UIKit.AddButton(row, "START", () =>
            {
                if (NetworkLobbyManager.Instance != null) NetworkLobbyManager.Instance.RequestStartMatch();
            }, 270f, 66f, 30);
            UIKit.AddButton(_room, "LEAVE", () =>
            {
                if (NetworkLobbyManager.Instance != null) NetworkLobbyManager.Instance.Leave();
                ShowPanel(Panel.Offline);
            }, 560f, 56f, 28);

            _message = UIKit.AddLabel(root, "", 26, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.4f));
            UIKit.Anchor(_message.rectTransform, new Vector2(0.2f, 0.02f), new Vector2(0.8f, 0.09f));
        }

        static RectTransform NewPanel(RectTransform root, string name)
        {
            RectTransform p = UIKit.AddPanel(root, name, UIKit.PanelColor);
            UIKit.Anchor(p, new Vector2(0.27f, 0.08f), new Vector2(0.73f, 0.94f));
            UIKit.Column(p, 14f, 26, TextAnchor.UpperCenter);
            return p;
        }

        // ------------------------------------------------------------------ show / hide

        public override void OnShow()
        {
            _message.text = "";
            _mode.SetIndex((int)Game.Settings.mode, false);
            _stage.SetIndex(Mathf.Max(0, Array.IndexOf(StageRegistry.Ids, Game.Settings.stageId)), false);

            NetworkLobbyManager l = NetworkLobbyManager.Instance;
            if (l != null)
            {
                l.OnLobbyChanged += RefreshRoom;
                l.OnStateChanged += OnState;
                l.OnError += ShowMessage;
                l.OnDisconnected += ShowMessage;
            }
            if (LocalPlayerManager.Instance != null) LocalPlayerManager.Instance.Changed += RefreshLocal;

            ShowPanel(l != null && l.State == LobbyState.InLobby ? Panel.Room : Panel.Offline);
        }

        public override void OnHide()
        {
            NetworkLobbyManager l = NetworkLobbyManager.Instance;
            if (l != null)
            {
                l.OnLobbyChanged -= RefreshRoom;
                l.OnStateChanged -= OnState;
                l.OnError -= ShowMessage;
                l.OnDisconnected -= ShowMessage;
            }
            if (LocalPlayerManager.Instance != null)
            {
                LocalPlayerManager.Instance.Changed -= RefreshLocal;
                LocalPlayerManager.Instance.DisableJoining();
            }
        }

        public override void Tick(float dt)
        {
            _refreshT += dt;
            if (_refreshT < 0.4f) return;
            _refreshT = 0f;
            if (_local.gameObject.activeSelf) RefreshLocal();
        }

        void ShowPanel(Panel panel)
        {
            _offline.gameObject.SetActive(panel == Panel.Offline);
            _local.gameObject.SetActive(panel == Panel.Local);
            _room.gameObject.SetActive(panel == Panel.Room);

            LocalPlayerManager lpm = LocalPlayerManager.Instance;
            if (lpm != null)
            {
                if (panel == Panel.Local) lpm.EnableJoining();
                else lpm.DisableJoining();
            }

            if (panel == Panel.Local) RefreshLocal();
            if (panel == Panel.Room)
            {
                _readyState = false;
                UIKit.SetButtonText(_ready, "READY");
                RefreshRoom();
            }
        }

        // ------------------------------------------------------------------ actions

        void OpenLocal()
        {
            _message.text = "";
            ShowPanel(Panel.Local);
        }

        void HostGame()
        {
            NetworkLobbyManager l = NetworkLobbyManager.Instance;
            if (l == null) { ShowMessage("No NetworkLobbyManager in the Boot scene."); return; }
            if (l.HostCustomGame(SaveSystem.Data.playerName, true)) _message.text = "Starting host...";
        }

        void JoinGame()
        {
            NetworkLobbyManager l = NetworkLobbyManager.Instance;
            if (l == null) { ShowMessage("No NetworkLobbyManager in the Boot scene."); return; }
            if (l.JoinByCode(_code.text, SaveSystem.Data.playerName)) _message.text = "Connecting...";
        }

        void ToggleReady()
        {
            _readyState = !_readyState;
            UIKit.SetButtonText(_ready, _readyState ? "NOT READY" : "READY");
            if (NetworkLobbyManager.Instance != null) NetworkLobbyManager.Instance.SetReady(_readyState);
        }

        void SendSettings()
        {
            NetworkLobbyManager l = NetworkLobbyManager.Instance;
            if (l == null || !l.IsLeader) return;
            l.RequestSetSettings(StageRegistry.Ids[_roomStage.Index], ModeNames[_roomMode.Index]);
        }

        void OnState(LobbyState state)
        {
            if (state == LobbyState.InLobby)
            {
                ShowPanel(Panel.Room);
                _message.text = "";
                NetworkLobbyManager l = NetworkLobbyManager.Instance;
                if (l != null && l.IsLeader)
                    l.RequestSetSettings(Game.Settings.stageId, Game.Settings.mode.ToString());
            }
            else if (state == LobbyState.Offline)
            {
                ShowPanel(Panel.Offline);
            }
        }

        void ShowMessage(string text)
        {
            _message.text = text;
        }

        // ------------------------------------------------------------------ refresh

        void RefreshLocal()
        {
            LocalPlayerManager lpm = LocalPlayerManager.Instance;
            var sb = new StringBuilder();
            if (lpm == null || lpm.Players.Count == 0)
            {
                sb.Append("No one has joined yet.\nStarting now plays solo with the keyboard and mouse.");
            }
            else
            {
                foreach (LocalPlayer p in lpm.Players)
                    sb.AppendLine("P" + (p.Index + 1) + "   " + p.Name + "   (" + p.Scheme + ")");
            }
            _localList.text = sb.ToString();
        }

        void RefreshRoom()
        {
            NetworkLobbyManager l = NetworkLobbyManager.Instance;
            if (l == null || !_room.gameObject.activeSelf) return;

            _codeLabel.text = "CODE   " + l.LobbyCode;
            UIKit.SetButtonText(_privacy, (l.IsPrivate ? "PRIVATE" : "PUBLIC") + "  (O)");
            _privacy.interactable = l.IsLeader;

            _roomMode.SetIndex(Mathf.Max(0, Array.IndexOf(ModeNames, l.ModeId)), false);
            _roomStage.SetIndex(Mathf.Max(0, Array.IndexOf(StageRegistry.Ids, l.MapId)), false);
            _roomMode.SetInteractable(l.IsLeader);
            _roomStage.SetInteractable(l.IsLeader);
            _start.gameObject.SetActive(l.IsLeader);

            var sb = new StringBuilder();
            foreach (LobbyPlayer p in l.Roster)
                sb.AppendLine((p.IsLeader ? "* " : "   ") + p.Name + "   " + (p.Ready ? "READY" : "..."));
            _roster.text = sb.ToString();
        }
    }
}
