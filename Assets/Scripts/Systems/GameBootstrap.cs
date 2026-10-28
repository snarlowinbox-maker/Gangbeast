using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using BeefCity.Controls;
using BeefCity.Customization;
using BeefCity.Graphics;
using BeefCity.Networking;
using BeefCity.Ragdoll;
using BeefCity.UI;

namespace BeefCity.Core
{
    /// <summary>
    /// Entry point. Put this (plus NetworkManager + UnityTransport + NetworkLobbyManager) in the "Boot" scene.
    /// It is a normal MonoBehaviour, not static or abstract. It builds the menu world and UI from code, runs the flow
    /// Splash -> Main Menu -> Costume Room -> Lobby -> Loading -> Match, and loads the "Match" scene additively so the
    /// Boot scene (and the menus) stay alive for the whole session.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        [SerializeField] string matchSceneName = "Match";
        [SerializeField] float minLoadSeconds = 1.2f;

        public MatchSettings Settings { get; } = new MatchSettings();
        public SaveData Save => SaveSystem.Data;
        public ScreenId CurrentScreen { get; private set; } = ScreenId.None;
        public bool Headless => Application.isBatchMode;

        readonly Dictionary<ScreenId, MenuScreen> _screens = new Dictionary<ScreenId, MenuScreen>();
        MenuScreen _current;
        Canvas _canvas;
        CostumeRoomScreen _costume;
        LoadingScreen _loading;
        GameObject _menuWorld;
        RagdollRig _preview;
        Scene _bootScene, _matchScene;
        bool _localMatch, _inMatch, _lobbyHooked;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _bootScene = gameObject.scene;

            SaveSystem.Load();
            ApplySavedSettings();
            Settings.stageId = StageRegistry.Ids[0];

            if (Headless) return; // dedicated server: no menus, no controls, no camera

            BuildMenuWorld();
            BuildUI();
            gameObject.AddComponent<LocalPlayerManager>();
            gameObject.AddComponent<RebindingMenu>();
            gameObject.AddComponent<SecretCommands>();
        }

        void Start()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded += OnSceneLoaded;

            NetworkLobbyManager lobby = NetworkLobbyManager.Instance;
            if (lobby != null) lobby.OnStateChanged += OnLobbyState;

            if (!Headless) Show(ScreenId.Splash);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            NetworkLobbyManager lobby = NetworkLobbyManager.Instance;
            if (lobby != null) lobby.OnStateChanged -= OnLobbyState;
            Instance = null;
        }

        void Update()
        {
            if (_current != null) _current.Tick(Time.unscaledDeltaTime);
        }

        void ApplySavedSettings()
        {
            SaveData s = SaveSystem.Data;
            AudioListener.volume = s.masterVolume;
            if (!Headless)
            {
                Screen.fullScreen = s.fullscreen;
                if (s.quality >= 0 && s.quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(s.quality, true);
            }
        }

        // ------------------------------------------------------------------ menu world + UI

        void BuildMenuWorld()
        {
            _menuWorld = new GameObject("MenuWorld");
            _menuWorld.transform.SetParent(transform, false);

            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(_menuWorld.transform, false);
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
            cam.fieldOfView = 40f;
            camGo.AddComponent<AudioListener>();
            PostProcessingSetup.ConfigureCamera(cam);
            camGo.transform.position = new Vector3(-0.8f, 1.15f, -4.4f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(-0.8f, 1.0f, 0f) - camGo.transform.position);

            var lightGo = new GameObject("MenuLight");
            lightGo.transform.SetParent(_menuWorld.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.5f);

            // Turntable
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Turntable";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(_menuWorld.transform, false);
            disc.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            disc.transform.localScale = new Vector3(2.4f, 0.05f, 2.4f);
            ProceduralMaterialGenerator.Apply(disc.GetComponent<Renderer>(), SurfaceType.Metal, new Color(0.8f, 0.8f, 0.85f), Vector2.one);

            // Beef City skyline: dark towers far behind the character
            var rng = new System.Random(7);
            for (int i = 0; i < 18; i++)
            {
                GameObject tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tower.name = "Tower";
                Destroy(tower.GetComponent<Collider>());
                tower.transform.SetParent(_menuWorld.transform, false);
                float h = 4f + (float)rng.NextDouble() * 12f;
                float w = 1.6f + (float)rng.NextDouble() * 2.2f;
                tower.transform.localPosition = new Vector3(-20f + i * 2.4f, h * 0.5f - 0.1f, 14f + (float)rng.NextDouble() * 8f);
                tower.transform.localScale = new Vector3(w, h, w);
                float tint = 0.25f + (float)rng.NextDouble() * 0.2f;
                ProceduralMaterialGenerator.Apply(tower.GetComponent<Renderer>(),
                    i % 3 == 0 ? SurfaceType.Metal : SurfaceType.Concrete, new Color(tint, tint, tint * 1.15f),
                    new Vector2(Mathf.Max(1f, w / 2f), Mathf.Max(1f, h / 2f)));
            }
        }

        void BuildUI()
        {
            UIKit.EnsureEventSystem();
            _canvas = UIKit.CreateCanvas("MenuCanvas", 10);
            _canvas.transform.SetParent(transform, false);

            _costume = new CostumeRoomScreen();
            _loading = new LoadingScreen();
            Register(ScreenId.Splash, new SplashScreen());
            Register(ScreenId.Main, new MainMenuScreen());
            Register(ScreenId.Settings, new SettingsScreen());
            Register(ScreenId.Costume, _costume);
            Register(ScreenId.Lobby, new LobbyScreen());
            Register(ScreenId.Loading, _loading);
        }

        void Register(ScreenId id, MenuScreen screen)
        {
            screen.Init(this, _canvas.transform);
            _screens[id] = screen;
        }

        public void Show(ScreenId id)
        {
            if (Headless) return;
            if (_current != null) _current.SetVisible(false);
            CurrentScreen = id;
            _current = id != ScreenId.None && _screens.TryGetValue(id, out MenuScreen s) ? s : null;
            if (_current != null) _current.SetVisible(true);
        }

        public void OpenCostume(ScreenId next, bool fromPlay)
        {
            _costume.SetNext(next, fromPlay);
            Show(ScreenId.Costume);
        }

        /// <summary>The 3D character shown in the costume room. Frozen in its rest pose and rotated by the screen.</summary>
        public RagdollRig ShowPreview(bool visible)
        {
            if (_preview == null)
            {
                var go = new GameObject("CostumePreview");
                go.transform.SetParent(_menuWorld.transform, false);
                var builder = go.AddComponent<ProceduralRagdollBuilder>();
                builder.Configure(SaveSystem.Data.costume.bodyColor, false, false);
                _preview = builder.Build();
                _preview.SetKinematic(true);
            }
            _preview.gameObject.SetActive(visible);
            return _preview;
        }

        void SetMenuVisible(bool visible)
        {
            if (_menuWorld != null) _menuWorld.SetActive(visible);
            if (_canvas != null) _canvas.gameObject.SetActive(visible);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ starting matches

        /// <summary>Offline match with everyone who joined on this machine (or just the keyboard player).</summary>
        public void StartLocalMatch()
        {
            if (_inMatch) return;
            _localMatch = true;
            Show(ScreenId.Loading);
            StartCoroutine(LoadLocalRoutine());
        }

        IEnumerator LoadLocalRoutine()
        {
            if (!Application.CanStreamedLevelBeLoaded(matchSceneName))
            {
                Debug.LogError($"[Bootstrap] Scene '{matchSceneName}' is not in Build Settings.");
                _localMatch = false;
                Show(ScreenId.Lobby);
                yield break;
            }

            _loading.SetStatus("Building Beef City...");
            AsyncOperation op = SceneManager.LoadSceneAsync(matchSceneName, LoadSceneMode.Additive);
            op.allowSceneActivation = false;

            float elapsed = 0f;
            while (op.progress < 0.9f || elapsed < minLoadSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                _loading.SetProgress(Mathf.Min(op.progress / 0.9f, elapsed / minLoadSeconds));
                yield return null;
            }
            _loading.SetProgress(1f);
            op.allowSceneActivation = true;
            yield return op;
        }

        void OnLobbyState(LobbyState state)
        {
            switch (state)
            {
                case LobbyState.InLobby:
                    HookNetworkScenes();
                    if (_inMatch) { _inMatch = false; CleanupMatchScene(); }
                    if (!Headless && CurrentScreen != ScreenId.Lobby)
                    {
                        SetMenuVisible(true);
                        Show(ScreenId.Lobby);
                    }
                    break;

                case LobbyState.InMatch:
                    if (_localMatch) break;
                    if (!Headless)
                    {
                        _loading.SetStatus("Loading match...");
                        Show(ScreenId.Loading);
                    }
                    break;

                case LobbyState.Offline:
                    if (_localMatch) break;
                    _inMatch = false;
                    CleanupMatchScene();
                    if (!Headless)
                    {
                        SetMenuVisible(true);
                        if (CurrentScreen == ScreenId.Loading || CurrentScreen == ScreenId.None) Show(ScreenId.Main);
                    }
                    break;
            }
        }

        void HookNetworkScenes()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.SceneManager == null) return;
            nm.SceneManager.OnSceneEvent -= OnNetworkSceneEvent;
            nm.SceneManager.OnSceneEvent += OnNetworkSceneEvent;
            _lobbyHooked = true;
        }

        void OnNetworkSceneEvent(SceneEvent e)
        {
            if (Headless || _loading == null || e.SceneName != matchSceneName) return;
            if (e.SceneEventType == SceneEventType.Load && e.AsyncOperation != null
                && e.ClientId == NetworkManager.Singleton.LocalClientId)
                StartCoroutine(TrackOperation(e.AsyncOperation));
        }

        IEnumerator TrackOperation(AsyncOperation op)
        {
            while (!op.isDone)
            {
                _loading.SetProgress(op.progress / 0.9f);
                yield return null;
            }
        }

        // ------------------------------------------------------------------ match scene loaded / ended

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != matchSceneName) return;
            _matchScene = scene;
            _inMatch = true;
            SceneManager.SetActiveScene(scene);

            NetworkManager nm = NetworkManager.Singleton;
            bool online = nm != null && nm.IsListening;
            bool authority = _localMatch || (online && nm.IsServer);

            if (!authority)
            {
                // Remote client: the host simulates the match. Live replication of the ragdolls is Phase 7.
                if (!Headless)
                {
                    _loading.SetProgress(1f);
                    _loading.SetStatus("Match is running on the host (online sync arrives with Phase 7).");
                }
                return;
            }

            SetMenuVisible(false);
            BeginMatch(scene);
        }

        void BeginMatch(Scene scene)
        {
            MatchSettings settings = Settings;
            List<MatchPlayer> roster;

            NetworkLobbyManager lobby = NetworkLobbyManager.Instance;
            if (_localMatch || lobby == null || lobby.State == LobbyState.Offline)
            {
                roster = BuildLocalRoster();
            }
            else
            {
                settings = new MatchSettings { stageId = lobby.MapId };
                if (System.Enum.TryParse(lobby.ModeId, true, out GameModeId mode)) settings.mode = mode;
                roster = BuildOnlineRoster(lobby);
            }

            var go = new GameObject("MatchManager");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<MatchManager>().Begin(settings, roster, this);
        }

        List<MatchPlayer> BuildLocalRoster()
        {
            var list = new List<MatchPlayer>();
            LocalPlayerManager lpm = LocalPlayerManager.Instance;
            if (lpm != null && lpm.Players.Count > 0)
            {
                for (int i = 0; i < lpm.Players.Count; i++)
                {
                    LocalPlayer lp = lpm.Players[i];
                    list.Add(new MatchPlayer { slot = i, name = lp.Name, costume = lp.Costume, isLocal = true, localIndex = i });
                }
            }
            else
            {
                list.Add(new MatchPlayer { slot = 0, name = Save.playerName, costume = Save.costume, isLocal = true, localIndex = -1 });
            }
            return list;
        }

        List<MatchPlayer> BuildOnlineRoster(NetworkLobbyManager lobby)
        {
            var list = new List<MatchPlayer>();
            NetworkManager nm = NetworkManager.Singleton;
            int slot = 0;
            foreach (LobbyPlayer p in lobby.Roster)
            {
                bool mine = nm.IsHost && p.ClientId == nm.LocalClientId;
                list.Add(new MatchPlayer
                {
                    slot = slot,
                    clientId = p.ClientId,
                    name = p.Name,
                    costume = mine ? Save.costume : CostumeData.FromSlot(slot),
                    isLocal = mine,
                    localIndex = mine ? -1 : -1
                });
                slot++;
            }
            return list;
        }

        /// <summary>Called by MatchManager when the match is over (or the player left).</summary>
        public void EndMatch()
        {
            Time.timeScale = 1f;
            NetworkManager nm = NetworkManager.Singleton;
            bool online = !_localMatch && nm != null && nm.IsListening && nm.IsServer;

            if (online)
            {
                if (_matchScene.IsValid() && _matchScene.isLoaded) nm.SceneManager.UnloadScene(_matchScene);
                _inMatch = false;
                if (NetworkLobbyManager.Instance != null) NetworkLobbyManager.Instance.ReturnToLobby();
            }
            else
            {
                _localMatch = false;
                _inMatch = false;
                CleanupMatchScene();
            }

            if (_bootScene.IsValid()) SceneManager.SetActiveScene(_bootScene);
            if (!Headless)
            {
                SetMenuVisible(true);
                Show(ScreenId.Lobby);
            }
        }

        void CleanupMatchScene()
        {
            if (_matchScene.IsValid() && _matchScene.isLoaded) SceneManager.UnloadSceneAsync(_matchScene);
            if (_bootScene.IsValid()) SceneManager.SetActiveScene(_bootScene);
        }
    }
}
