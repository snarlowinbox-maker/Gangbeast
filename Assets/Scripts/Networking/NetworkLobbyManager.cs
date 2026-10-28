using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeefCity.Networking
{
    public enum LobbyState { Offline, Connecting, InLobby, InMatch }

    public struct LobbyPlayer
    {
        public ulong ClientId;
        public string Name;
        public bool Ready;
        public bool IsLeader;
    }

    /// <summary>
    /// Host-authoritative lobby on Netcode for GameObjects + Unity Transport.
    /// - Host (or dedicated server) owns the roster; clients only send requests.
    /// - "Lobby Code" = host IPv4 + port + secret key, encoded in Crockford base32 (e.g. K3M9-2QXA-7FBD4).
    /// - Private lobbies require the key from the code; public lobbies accept anyone who reaches the port.
    /// - No cloud services needed. For internet play: set Address Override to your public IP and forward the UDP port,
    ///   or run a dedicated server. (A Relay / Lobby-service backend can replace the transport part later.)
    /// Requires a NetworkManager + UnityTransport in the scene; Player Prefab can stay empty.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkLobbyManager : MonoBehaviour
    {
        public static NetworkLobbyManager Instance { get; private set; }

        const string MsgRoster = "bc.roster";
        const string MsgReady = "bc.ready";
        const string MsgStart = "bc.start";
        const string MsgSettings = "bc.settings";
        const string MsgPrivate = "bc.private";
        const string MsgError = "bc.error";
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32
        public const ulong NoLeader = ulong.MaxValue;

        [Header("Network")]
        [SerializeField] ushort defaultPort = 7777;
        [SerializeField, Range(2, 8)] int maxPlayers = 8;
        [Tooltip("Public IPv4 or DNS name friends use to reach you. Leave empty for LAN play.")]
        [SerializeField] string addressOverride = "";
        [SerializeField] int protocolVersion = 1;
        [SerializeField] bool autoStartDedicatedServerInBatchMode = true;

        [Header("Defaults")]
        [Tooltip("Map id doubles as the scene name to load, so it must be in Build Settings.")]
        [SerializeField] string defaultMap = "Gondola";
        [SerializeField] string defaultMode = "Melee";
        [Tooltip("Scene (in Build Settings) loaded additively when a match starts. Stages are built in code inside it.")]
        [SerializeField] string matchSceneName = "Match";

        [Header("Input / Debug")]
        [SerializeField] bool enableTogglePrivateKey = true;
        [SerializeField] bool debugGui = true;

        /// <summary>UI sets this to true while a text field has focus so 'O' does not toggle privacy.</summary>
        public bool InputBlocked { get; set; }

        public LobbyState State { get; private set; } = LobbyState.Offline;
        public bool IsPrivate { get; private set; } = true;
        public string LobbyCode { get; private set; } = "";
        public string MapId { get; private set; }
        public string ModeId { get; private set; }
        public ulong LeaderClientId { get; private set; } = NoLeader;
        public int MaxPlayers => maxPlayers;
        public IReadOnlyList<LobbyPlayer> Roster => _roster;

        public bool IsLeader
        {
            get
            {
                NetworkManager nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening && LeaderClientId != NoLeader && LeaderClientId == nm.LocalClientId;
            }
        }

        public event Action<LobbyState> OnStateChanged;
        public event Action OnLobbyChanged;
        public event Action<string> OnError;
        public event Action<string> OnDisconnected;
        public event Action<string, string> OnMatchStarting; // mapId, modeId

        // Server-side authoritative data
        readonly Dictionary<ulong, LobbyPlayer> _players = new Dictionary<ulong, LobbyPlayer>();
        readonly Dictionary<ulong, string> _pendingNames = new Dictionary<ulong, string>();
        ushort _lobbyKey;
        bool _matchStarted;

        // Replicated view (server fills it directly, clients receive it)
        readonly List<LobbyPlayer> _roster = new List<LobbyPlayer>();

        bool _subscribed;
        string _dbgName = "Player", _dbgCode = "";

        [Serializable]
        sealed class ConnectPayload
        {
            public string name;
            public int key;
            public int version;
        }

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            MapId = defaultMap;
            ModeId = defaultMode;
        }

        void Start()
        {
            if (autoStartDedicatedServerInBatchMode && Application.isBatchMode)
            {
                ushort port = ushort.TryParse(GetArg("-port"), out ushort p) ? p : defaultPort;
                string address = GetArg("-address");
                if (!string.IsNullOrEmpty(address)) addressOverride = address;
                if (int.TryParse(GetArg("-maxplayers"), out int mp)) maxPlayers = Mathf.Clamp(mp, 2, 8);
                StartDedicatedServer(port, HasArg("-private"));
            }
        }

        void Update()
        {
            if (!enableTogglePrivateKey || InputBlocked || State != LobbyState.InLobby || !IsLeader) return;
            if (TogglePressed()) RequestSetPrivate(!IsPrivate);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Unsubscribe();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Creates a "Custom Game": starts a host and generates a lobby code.</summary>
        public bool HostCustomGame(string playerName, bool startPrivate = true, ushort port = 0)
        {
            if (State != LobbyState.Offline) return Fail("You are already in a lobby.");
            if (!Prepare(out NetworkManager nm, out UnityTransport utp)) return false;

            ushort p = port == 0 ? defaultPort : port;
            ResetLocalState();
            IsPrivate = startPrivate;
            _lobbyKey = RandomKey();
            LobbyCode = EncodeLobbyCode(ResolveAdvertisedAddress(addressOverride), p, _lobbyKey);

            utp.SetConnectionData("127.0.0.1", p, "0.0.0.0"); // local client connects to loopback, server listens everywhere
            nm.NetworkConfig.ConnectionData = BuildPayload(playerName, _lobbyKey);

            SetState(LobbyState.Connecting);
            if (!nm.StartHost())
            {
                ResetLocalState();
                SetState(LobbyState.Offline);
                return Fail("Could not start the host. Is the port already in use?");
            }
            RegisterServerHandlers(nm);
            return true;
        }

        /// <summary>Starts a headless server (no local player). The first player to join becomes the leader.</summary>
        public bool StartDedicatedServer(ushort port, bool startPrivate)
        {
            if (State != LobbyState.Offline) return Fail("Already running.");
            if (!Prepare(out NetworkManager nm, out UnityTransport utp)) return false;

            ResetLocalState();
            IsPrivate = startPrivate;
            _lobbyKey = RandomKey();
            LobbyCode = EncodeLobbyCode(ResolveAdvertisedAddress(addressOverride), port, _lobbyKey);

            utp.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            if (!nm.StartServer())
            {
                ResetLocalState();
                return Fail("Could not start the dedicated server.");
            }
            RegisterServerHandlers(nm);
            SetState(LobbyState.InLobby);
            Debug.Log($"[Lobby] Dedicated server up on UDP {port}. Lobby code: {LobbyCode} ({(IsPrivate ? "private" : "public")})");
            return true;
        }

        public bool JoinByCode(string code, string playerName)
        {
            if (State != LobbyState.Offline) return Fail("You are already in a lobby.");
            if (!TryDecodeLobbyCode(code, out string ip, out ushort port, out ushort key))
                return Fail("That lobby code is not valid.");
            if (!Prepare(out NetworkManager nm, out UnityTransport utp)) return false;

            ResetLocalState();
            utp.SetConnectionData(ip, port);
            nm.NetworkConfig.ConnectionData = BuildPayload(playerName, key);

            SetState(LobbyState.Connecting);
            if (!nm.StartClient())
            {
                SetState(LobbyState.Offline);
                return Fail("Could not start the client.");
            }
            RegisterClientHandlers(nm);
            return true;
        }

        public void Leave()
        {
            NetworkManager nm = NetworkManager.Singleton;
            ResetLocalState();
            SetState(LobbyState.Offline); // set first so shutdown callbacks are ignored
            if (nm != null && nm.IsListening) nm.Shutdown();
            OnLobbyChanged?.Invoke();
        }

        public void SetReady(bool ready)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || State != LobbyState.InLobby) return;
            if (nm.IsServer) SetReadyServer(nm.LocalClientId, ready);
            else SendBoolToServer(MsgReady, ready);
        }

        /// <summary>Leader only. Toggles Private/Public (also bound to the 'O' key).</summary>
        public void RequestSetPrivate(bool value)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !IsLeader) return;
            if (nm.IsServer) SetPrivateServer(value);
            else SendBoolToServer(MsgPrivate, value);
        }

        /// <summary>Leader only. Picks the mode and map (map id = scene name).</summary>
        public void RequestSetSettings(string mapId, string modeId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !IsLeader) return;
            if (nm.IsServer) SetSettingsServer(mapId, modeId);
            else SendSettingsToServer(mapId, modeId);
        }

        /// <summary>Leader only. Loads the selected map for everyone once all other players are ready.</summary>
        public void RequestStartMatch()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !IsLeader || State != LobbyState.InLobby) return;
            if (nm.IsServer)
            {
                if (!TryStartMatch(out string error)) OnError?.Invoke(error);
            }
            else SendBoolToServer(MsgStart, true);
        }

        /// <summary>Server only. Admin tools (Phase 7) call this to kick a player by client id.</summary>
        public void Kick(ulong clientId, string reason = "You were removed from the lobby.")
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || clientId == NetworkManager.ServerClientId) return;
            nm.DisconnectClient(clientId, reason);
        }

        public string MatchSceneName => matchSceneName;

        /// <summary>Server only. After a match: reopen the lobby, clear ready flags and tell everyone.</summary>
        public void ReturnToLobby()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            _matchStarted = false;
            var ids = new List<ulong>(_players.Keys);
            foreach (ulong id in ids)
            {
                LobbyPlayer p = _players[id];
                p.Ready = false;
                _players[id] = p;
            }
            SetState(LobbyState.InLobby);
            BroadcastRoster();
        }

        // ------------------------------------------------------------------ setup helpers

        bool Prepare(out NetworkManager nm, out UnityTransport utp)
        {
            nm = NetworkManager.Singleton;
            utp = null;
            if (nm == null) return Fail("No NetworkManager in the scene.");
            utp = nm.GetComponent<UnityTransport>();
            if (utp == null) return Fail("The NetworkManager needs a UnityTransport component.");

            nm.NetworkConfig.ConnectionApproval = true; // must be identical on host and clients
            nm.ConnectionApprovalCallback = OnApproval;
            Subscribe(nm);
            return true;
        }

        void Subscribe(NetworkManager nm)
        {
            if (_subscribed) return;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnClientConnected;
                nm.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            _subscribed = false;
        }

        void RegisterServerHandlers(NetworkManager nm)
        {
            CustomMessagingManager cm = nm.CustomMessagingManager;
            cm.RegisterNamedMessageHandler(MsgReady, OnReadyMessage);
            cm.RegisterNamedMessageHandler(MsgStart, OnStartMessage);
            cm.RegisterNamedMessageHandler(MsgSettings, OnSettingsMessage);
            cm.RegisterNamedMessageHandler(MsgPrivate, OnPrivateMessage);
        }

        void RegisterClientHandlers(NetworkManager nm)
        {
            CustomMessagingManager cm = nm.CustomMessagingManager;
            cm.RegisterNamedMessageHandler(MsgRoster, OnRosterMessage);
            cm.RegisterNamedMessageHandler(MsgError, OnErrorMessage);
        }

        void ResetLocalState()
        {
            _players.Clear();
            _pendingNames.Clear();
            _roster.Clear();
            _matchStarted = false;
            LeaderClientId = NoLeader;
            LobbyCode = "";
            MapId = defaultMap;
            ModeId = defaultMode;
        }

        void SetState(LobbyState s)
        {
            if (State == s) return;
            State = s;
            OnStateChanged?.Invoke(s);
        }

        bool Fail(string message)
        {
            Debug.LogWarning("[Lobby] " + message);
            OnError?.Invoke(message);
            return false;
        }

        // ------------------------------------------------------------------ connection approval (server)

        void OnApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false; // player ragdolls are spawned when the match starts
            response.Pending = false;

            ConnectPayload payload = ParsePayload(request.Payload);
            if (payload == null) { Reject(response, "Invalid connection data."); return; }
            if (payload.version != protocolVersion) { Reject(response, "Game version mismatch."); return; }

            bool isLocalHost = request.ClientNetworkId == NetworkManager.ServerClientId;
            if (!isLocalHost)
            {
                NetworkManager nm = NetworkManager.Singleton;
                if (_matchStarted) { Reject(response, "A match is already in progress."); return; }
                if (nm.ConnectedClientsIds.Count >= maxPlayers) { Reject(response, "The lobby is full."); return; }
                if (IsPrivate && payload.key != _lobbyKey) { Reject(response, "Wrong lobby code."); return; }
            }

            _pendingNames[request.ClientNetworkId] = SanitizeName(payload.name, request.ClientNetworkId);
            response.Approved = true;
        }

        static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.Reason = reason;
        }

        // ------------------------------------------------------------------ connect / disconnect events

        void OnClientConnected(ulong clientId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return; // clients enter the lobby when the first roster arrives

            string name = _pendingNames.TryGetValue(clientId, out string n) ? n : "Player " + clientId;
            _pendingNames.Remove(clientId);
            _players[clientId] = new LobbyPlayer { ClientId = clientId, Name = name, Ready = false };
            if (LeaderClientId == NoLeader) LeaderClientId = clientId;
            if (State == LobbyState.Connecting) SetState(LobbyState.InLobby); // host finished starting
            BroadcastRoster();
        }

        void OnClientDisconnected(ulong clientId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || State == LobbyState.Offline) return;

            if (nm.IsServer)
            {
                _pendingNames.Remove(clientId);
                if (_players.Remove(clientId))
                {
                    if (clientId == LeaderClientId) LeaderClientId = LowestPlayerId();
                    if (_players.Count == 0) _matchStarted = false;
                    BroadcastRoster();
                }
                return;
            }

            // Client side: we lost the connection or were rejected.
            string reason = nm.DisconnectReason;
            bool wasConnecting = State == LobbyState.Connecting;
            ResetLocalState();
            SetState(LobbyState.Offline);
            if (nm.IsListening) nm.Shutdown();

            if (string.IsNullOrEmpty(reason))
                reason = wasConnecting
                    ? "Could not reach the lobby. Check the code and that the host's UDP port is open."
                    : "Disconnected from the lobby.";
            OnDisconnected?.Invoke(reason);
            OnLobbyChanged?.Invoke();
        }

        ulong LowestPlayerId()
        {
            ulong best = NoLeader;
            foreach (ulong id in _players.Keys) if (id < best) best = id;
            return best;
        }

        // ------------------------------------------------------------------ server-side state changes

        void SetReadyServer(ulong clientId, bool ready)
        {
            if (_matchStarted || !_players.TryGetValue(clientId, out LobbyPlayer p)) return;
            p.Ready = ready;
            _players[clientId] = p;
            BroadcastRoster();
        }

        void SetPrivateServer(bool value)
        {
            IsPrivate = value;
            BroadcastRoster();
        }

        void SetSettingsServer(string mapId, string modeId)
        {
            if (_matchStarted) return;
            mapId = (mapId ?? "").Trim();
            modeId = (modeId ?? "").Trim();
            if (mapId.Length == 0 || mapId.Length > 32 || modeId.Length == 0 || modeId.Length > 32) return;
            MapId = mapId;
            ModeId = modeId;
            BroadcastRoster();
        }

        bool TryStartMatch(out string error)
        {
            error = null;
            NetworkManager nm = NetworkManager.Singleton;
            if (_matchStarted) { error = "The match has already started."; return false; }
            foreach (LobbyPlayer p in _players.Values)
                if (p.ClientId != LeaderClientId && !p.Ready) { error = "Not everyone is ready."; return false; }
            if (!Application.CanStreamedLevelBeLoaded(matchSceneName))
            { error = $"Scene '{matchSceneName}' is not in the build settings."; return false; }

            SceneEventProgressStatus status = nm.SceneManager.LoadScene(matchSceneName, LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            { error = "Could not start loading the map (" + status + ")."; return false; }

            _matchStarted = true;
            SetState(LobbyState.InMatch);
            BroadcastRoster();
            OnMatchStarting?.Invoke(MapId, ModeId);
            return true;
        }

        // ------------------------------------------------------------------ roster replication

        void BroadcastRoster()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            // Update the server's own view first.
            _roster.Clear();
            foreach (LobbyPlayer p in _players.Values)
            {
                LobbyPlayer q = p;
                q.IsLeader = p.ClientId == LeaderClientId;
                _roster.Add(q);
            }
            _roster.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
            OnLobbyChanged?.Invoke();

            using (var w = new FastBufferWriter(1024, Allocator.Temp, 4096))
            {
                w.WriteValueSafe(LobbyCode);
                w.WriteValueSafe(IsPrivate);
                w.WriteValueSafe(MapId);
                w.WriteValueSafe(ModeId);
                w.WriteValueSafe(_matchStarted);
                w.WriteValueSafe(LeaderClientId);
                w.WriteValueSafe(_roster.Count);
                foreach (LobbyPlayer p in _roster)
                {
                    w.WriteValueSafe(p.ClientId);
                    w.WriteValueSafe(p.Name);
                    w.WriteValueSafe(p.Ready);
                }

                foreach (ulong id in nm.ConnectedClientsIds)
                {
                    if (id == NetworkManager.ServerClientId) continue; // the host already has the data
                    nm.CustomMessagingManager.SendNamedMessage(MsgRoster, id, w, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        void OnRosterMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string code);
            reader.ReadValueSafe(out bool isPrivate);
            reader.ReadValueSafe(out string map);
            reader.ReadValueSafe(out string mode);
            reader.ReadValueSafe(out bool matchStarted);
            reader.ReadValueSafe(out ulong leader);
            reader.ReadValueSafe(out int count);

            _roster.Clear();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ulong id);
                reader.ReadValueSafe(out string name);
                reader.ReadValueSafe(out bool ready);
                _roster.Add(new LobbyPlayer { ClientId = id, Name = name, Ready = ready, IsLeader = id == leader });
            }

            LobbyCode = code;
            IsPrivate = isPrivate;
            MapId = map;
            ModeId = mode;
            LeaderClientId = leader;
            SetState(matchStarted ? LobbyState.InMatch : LobbyState.InLobby);
            OnLobbyChanged?.Invoke();
        }

        void OnErrorMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            OnError?.Invoke(text);
        }

        // ------------------------------------------------------------------ client -> server messages

        void OnReadyMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool ready);
            SetReadyServer(sender, ready);
        }

        void OnPrivateMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool value);
            if (sender == LeaderClientId) SetPrivateServer(value);
        }

        void OnSettingsMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string map);
            reader.ReadValueSafe(out string mode);
            if (sender == LeaderClientId) SetSettingsServer(map, mode);
        }

        void OnStartMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool _);
            if (sender != LeaderClientId) return;
            if (!TryStartMatch(out string error)) SendError(sender, error);
        }

        void SendBoolToServer(string message, bool value)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient) return;
            using (var w = new FastBufferWriter(8, Allocator.Temp))
            {
                w.WriteValueSafe(value);
                nm.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        void SendSettingsToServer(string mapId, string modeId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient) return;
            using (var w = new FastBufferWriter(256, Allocator.Temp, 1024))
            {
                w.WriteValueSafe(mapId ?? "");
                w.WriteValueSafe(modeId ?? "");
                nm.CustomMessagingManager.SendNamedMessage(MsgSettings, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        void SendError(ulong clientId, string text)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            using (var w = new FastBufferWriter(256, Allocator.Temp, 1024))
            {
                w.WriteValueSafe(text);
                nm.CustomMessagingManager.SendNamedMessage(MsgError, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        // ------------------------------------------------------------------ lobby codes

        static string EncodeLobbyCode(IPAddress ip, ushort port, ushort key)
        {
            byte[] b = ip.GetAddressBytes();
            ulong v = ((ulong)b[0] << 56) | ((ulong)b[1] << 48) | ((ulong)b[2] << 40) | ((ulong)b[3] << 32)
                      | ((ulong)port << 16) | key;
            var sb = new StringBuilder(15);
            for (int i = 0; i < 13; i++)
            {
                int idx = (int)((v >> (5 * (12 - i))) & 31UL);
                sb.Append(Alphabet[idx]);
                if (i == 3 || i == 7) sb.Append('-');
            }
            return sb.ToString();
        }

        static bool TryDecodeLobbyCode(string code, out string ip, out ushort port, out ushort key)
        {
            ip = null;
            port = 0;
            key = 0;
            if (string.IsNullOrWhiteSpace(code)) return false;

            ulong v = 0;
            int count = 0;
            foreach (char raw in code)
            {
                if (raw == '-' || char.IsWhiteSpace(raw)) continue;
                char ch = char.ToUpperInvariant(raw);
                if (ch == 'O') ch = '0';
                else if (ch == 'I' || ch == 'L') ch = '1';
                int idx = Alphabet.IndexOf(ch);
                if (idx < 0) return false;
                if (count == 0 && idx > 15) return false; // first symbol only carries 4 bits
                v = (v << 5) | (uint)idx;
                count++;
            }
            if (count != 13) return false;

            ip = $"{(v >> 56) & 255UL}.{(v >> 48) & 255UL}.{(v >> 40) & 255UL}.{(v >> 32) & 255UL}";
            port = (ushort)((v >> 16) & 0xFFFFUL);
            key = (ushort)(v & 0xFFFFUL);
            return port != 0;
        }

        static IPAddress ResolveAdvertisedAddress(string overrideAddress)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(overrideAddress))
                {
                    string a = overrideAddress.Trim();
                    if (IPAddress.TryParse(a, out IPAddress parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
                        return parsed;
                    foreach (IPAddress resolved in Dns.GetHostAddresses(a))
                        if (resolved.AddressFamily == AddressFamily.InterNetwork) return resolved;
                }

                // LAN address: "connecting" a UDP socket sends nothing but reveals the outgoing interface.
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("8.8.8.8", 65530);
                    if (s.LocalEndPoint is IPEndPoint ep) return ep.Address;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Lobby] Could not determine host address: " + e.Message);
            }
            return IPAddress.Loopback;
        }

        static ushort RandomKey()
        {
            var bytes = new byte[2];
            using (var rng = RandomNumberGenerator.Create())
            {
                do { rng.GetBytes(bytes); } while (bytes[0] == 0 && bytes[1] == 0);
            }
            return (ushort)((bytes[0] << 8) | bytes[1]);
        }

        // ------------------------------------------------------------------ payload + misc

        static byte[] BuildPayload(string playerName, ushort key)
        {
            var payload = new ConnectPayload { name = playerName, key = key, version = Instance != null ? Instance.protocolVersion : 1 };
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        }

        static ConnectPayload ParsePayload(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 1024) return null;
            try { return JsonUtility.FromJson<ConnectPayload>(Encoding.UTF8.GetString(bytes)); }
            catch { return null; }
        }

        static string SanitizeName(string raw, ulong clientId)
        {
            var sb = new StringBuilder();
            foreach (char c in raw ?? "")
            {
                if (char.IsControl(c)) continue;
                sb.Append(c);
                if (sb.Length >= 16) break;
            }
            string name = sb.ToString().Trim();
            return name.Length == 0 ? "Player " + clientId : name;
        }

        static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.oKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.O);
#else
            return false;
#endif
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        static bool HasArg(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------------ temporary debug panel (until Phase 2 UI exists)

        void OnGUI()
        {
            if (!debugGui) return;
            GUILayout.BeginArea(new Rect(10, 10, 340, 440), GUI.skin.box);
            GUILayout.Label("Beef City Lobby (debug)  -  " + State);

            if (State == LobbyState.Offline)
            {
                _dbgName = GUILayout.TextField(_dbgName, 16);
                if (GUILayout.Button("Host Custom Game (private)")) HostCustomGame(_dbgName, true);
                GUILayout.Space(8);
                _dbgCode = GUILayout.TextField(_dbgCode, 20);
                if (GUILayout.Button("Join by Code")) JoinByCode(_dbgCode, _dbgName);
            }
            else
            {
                GUILayout.Label($"Code: {LobbyCode}   [{(IsPrivate ? "PRIVATE" : "PUBLIC")}]");
                GUILayout.Label($"Mode: {ModeId}   Map: {MapId}");
                if (IsLeader) GUILayout.Label("You are the leader. Press O to toggle private/public.");
                foreach (LobbyPlayer p in _roster)
                    GUILayout.Label($"{(p.IsLeader ? "[L] " : "     ")}{p.Name}  {(p.Ready ? "READY" : "...")}");

                if (State == LobbyState.InLobby)
                {
                    if (GUILayout.Button("Ready")) SetReady(true);
                    if (GUILayout.Button("Not ready")) SetReady(false);
                    if (IsLeader && GUILayout.Button("Start match")) RequestStartMatch();
                }
                if (GUILayout.Button("Leave")) Leave();
            }
            GUILayout.EndArea();
        }
    }
}
