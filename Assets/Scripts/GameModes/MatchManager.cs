using System;
using System.Collections.Generic;
using UnityEngine;
using BeefCity.Controls;
using BeefCity.Customization;
using BeefCity.Graphics;
using BeefCity.Ragdoll;
using BeefCity.UI;

namespace BeefCity.Core
{
    /// <summary>Phase 6 plugs the wave AI in here: set WaveDirectorHook.Factory and Waves mode will use it.</summary>
    public interface IWaveDirector
    {
        string StatusText { get; }
        void Begin(MatchManager match);
        void Tick(float dt);
        void End();
    }

    public static class WaveDirectorHook
    {
        public static Func<IWaveDirector> Factory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Factory = null; }
    }

    /// <summary>
    /// Runs one match on the authority (offline, or the host / dedicated server): builds the stage, spawns the
    /// ragdolls, handles countdown, rounds, ring-out eliminations, scoring and the end of the match.
    /// Melee = free for all, Gang = teams, Waves = co-op survival, Soccer = goals.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        public enum Phase { Countdown, Playing, RoundEnd, MatchEnd }

        public static MatchManager Current { get; private set; }

        public static readonly Color[] TeamColors =
        {
            new Color(0.90f, 0.25f, 0.25f), new Color(0.25f, 0.45f, 0.95f),
            new Color(0.30f, 0.80f, 0.35f), new Color(0.98f, 0.85f, 0.20f),
            new Color(0.98f, 0.55f, 0.18f), new Color(0.60f, 0.30f, 0.85f)
        };
        static readonly string[] TeamNames = { "Red", "Blue", "Green", "Yellow", "Orange", "Purple" };

        public MatchSettings Settings { get; private set; }
        public ArenaInfo Arena { get; private set; }
        public readonly List<MatchPlayer> Players = new List<MatchPlayer>();
        public Phase CurrentPhase { get; private set; }
        public int Round { get; private set; }
        public bool Sandbox { get; private set; } // fewer than two human players: practice, no rounds

        readonly Dictionary<int, int> _scores = new Dictionary<int, int>();
        readonly HashSet<MatchPlayer> _testInput = new HashSet<MatchPlayer>();
        GameBootstrap _game;
        PartyCamera _camera;
        MatchHud _hud;
        IWaveDirector _waves;
        float _phaseT, _bannerClearAt, _matchTime;
        int _matchWinnerTeam = -1;
        bool _finished;

        // ------------------------------------------------------------------ setup

        public void Begin(MatchSettings settings, List<MatchPlayer> roster, GameBootstrap game)
        {
            Current = this;
            _game = game;
            Settings = settings;
            Players.AddRange(roster);

            Arena = StageRegistry.Build(settings.stageId, settings);
            AssignTeams();
            Sandbox = Players.FindAll(p => !p.isDummy).Count < 2 && settings.mode != GameModeId.Waves && settings.mode != GameModeId.Soccer;

            if (!Application.isBatchMode)
            {
                SetupCamera();
                _hud = new MatchHud(this);
            }

            if (settings.mode == GameModeId.Soccer && Arena.ball != null) Arena.GoalScored += OnGoal;
            if (settings.mode == GameModeId.Waves && WaveDirectorHook.Factory != null)
            {
                _waves = WaveDirectorHook.Factory();
                _waves.Begin(this);
            }

            RagdollCombat.RingedOut += OnRingedOut;
            StartRound();
        }

        void AssignTeams()
        {
            int teams = Mathf.Max(2, Settings.teamCount);
            foreach (MatchPlayer p in Players)
            {
                switch (Settings.mode)
                {
                    case GameModeId.Melee: p.team = p.slot; break;
                    case GameModeId.Waves: p.team = 0; break;
                    default: p.team = p.slot % (Settings.mode == GameModeId.Soccer ? 2 : teams); break;
                }
            }
        }

        void SetupCamera()
        {
            var go = new GameObject("PartyCamera");
            go.tag = "MainCamera";
            Camera cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.5f, 0.7f);
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            go.AddComponent<AudioListener>();
            _camera = go.AddComponent<PartyCamera>();
            PostProcessingSetup.ConfigureCamera(cam);
            go.transform.position = Arena.center + new Vector3(0f, 18f, -14f);
            go.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        }

        // ------------------------------------------------------------------ spawning

        Transform PickSpawn(MatchPlayer p)
        {
            bool teamMode = Settings.mode == GameModeId.Gang || Settings.mode == GameModeId.Soccer;
            if (teamMode && Arena.teamSpawns != null && Arena.teamSpawns.Length > 0)
            {
                List<Transform> list = Arena.teamSpawns[Mathf.Abs(p.team) % Arena.teamSpawns.Length];
                if (list.Count > 0)
                {
                    int index = 0;
                    foreach (MatchPlayer q in Players)
                    {
                        if (q == p) break;
                        if (q.team == p.team && !q.isDummy) index++;
                    }
                    return list[index % list.Count];
                }
            }
            return Arena.spawns.Count > 0 ? Arena.spawns[Mathf.Abs(p.slot) % Arena.spawns.Count] : null;
        }

        public MatchPlayer SpawnAvatar(MatchPlayer p, Vector3? at = null)
        {
            if (p.rig != null) Destroy(p.rig.gameObject);

            Transform sp = PickSpawn(p);
            Vector3 pos = at ?? (sp != null ? sp.position : Arena.center + Vector3.up * 0.05f);
            Quaternion rot = at.HasValue || sp == null ? Quaternion.identity : sp.rotation;

            var go = new GameObject("Player" + (p.slot + 1) + "_" + p.name);
            go.transform.SetPositionAndRotation(pos, rot);

            Color color = Settings.mode == GameModeId.Gang || Settings.mode == GameModeId.Soccer
                ? TeamColors[Mathf.Abs(p.team) % TeamColors.Length]
                : p.costume.bodyColor;

            ProceduralRagdollBuilder builder = go.AddComponent<ProceduralRagdollBuilder>();
            builder.Configure(color, true, true);
            RagdollRig rig = builder.Build();

            CostumeData look = p.costume.Clone();
            look.bodyColor = color;
            CostumeBuilder.Apply(rig, look);

            var combat = rig.GetComponent<RagdollCombat>();
            var motor = rig.GetComponent<ActiveRagdollMotor>();
            combat.ringOutY = Arena.ringOutY;
            motor.useBuiltInTestInput = false;
            combat.useBuiltInTestInput = false;

            p.rig = rig;
            p.combat = combat;
            p.alive = true;
            p.respawnAt = 0f;

            if (p.isLocal && !p.isDummy) AttachInput(p, motor, combat);
            return p;
        }

        void AttachInput(MatchPlayer p, ActiveRagdollMotor motor, RagdollCombat combat)
        {
            LocalPlayerManager lpm = LocalPlayerManager.Instance;
            if (lpm != null && p.localIndex >= 0 && p.localIndex < lpm.Players.Count)
            {
                var input = combat.gameObject.AddComponent<PlayerAvatarInput>();
                input.Bind(lpm.Players[p.localIndex].Input);
            }
            else if (p.localIndex <= 0)
            {
                // No joined controller: the first player falls back to the keyboard/mouse test input.
                motor.useBuiltInTestInput = true;
                combat.useBuiltInTestInput = true;
                motor.testInputJump = false;
                _testInput.Add(p);
            }
        }

        /// <summary>Secret-command helper: a ragdoll with no controller (also the stand-in for AI until Phase 6).</summary>
        public MatchPlayer SpawnDummy(Vector3 position, Color color)
        {
            var p = new MatchPlayer
            {
                slot = Players.Count,
                name = "Dummy",
                costume = CostumeData.CreateRandom(Players.Count + 7),
                team = 99,
                isDummy = true
            };
            p.costume.bodyColor = color;
            Players.Add(p);
            SpawnAvatar(p, position);
            return p;
        }

        /// <summary>Shift/Ctrl + 1-8: respawn that player slot at the cursor, or create a controller-less guest.</summary>
        public void SpawnPlayerAt(int slot, Vector3 position)
        {
            MatchPlayer existing = Players.Find(p => p.slot == slot && !p.isDummy);
            if (existing != null)
            {
                if (existing.rig != null && existing.alive) existing.rig.Teleport(position + Vector3.up * 1.1f);
                else SpawnAvatar(existing, position);
                return;
            }
            var guest = new MatchPlayer
            {
                slot = slot,
                name = "Guest " + (slot + 1),
                costume = CostumeData.FromSlot(slot),
                team = Settings.mode == GameModeId.Melee ? slot : slot % 2,
                isDummy = true
            };
            Players.Add(guest);
            SpawnAvatar(guest, position);
        }

        // ------------------------------------------------------------------ round flow

        /// <summary>Broken cables, fallen boards and collapsed stairs come back every round: the stage is rebuilt.</summary>
        void RebuildArena()
        {
            BeefCity.AI.ArenaNavigation.DisposeCurrent();
            if (Arena != null)
            {
                Arena.GoalScored -= OnGoal;
                if (Arena.root != null)
                {
                    Arena.root.SetActive(false); // colliders vanish immediately, the objects are destroyed at end of frame
                    Destroy(Arena.root);
                }
            }
            Arena = StageRegistry.Build(Settings.stageId, Settings);
            if (Settings.mode == GameModeId.Soccer && Arena.ball != null) Arena.GoalScored += OnGoal;
        }

        void StartRound()
        {
            if (Round > 0) RebuildArena();
            Round++;
            foreach (MatchPlayer p in Players.FindAll(x => !x.isDummy))
            {
                if (p.rig != null) Destroy(p.rig.gameObject);
                p.rig = null;
                SpawnAvatar(p);
            }
            ResetBall();
            CurrentPhase = Phase.Countdown;
            _phaseT = 3f;
            SetInputLocked(true);
        }

        void ResetBall()
        {
            if (Arena.ball == null || Arena.ballSpawn == null) return;
            Arena.ball.position = Arena.ballSpawn.position;
            Arena.ball.SetVelocity(Vector3.zero);
            Arena.ball.angularVelocity = Vector3.zero;
        }

        void Update()
        {
            if (_finished) return;
            float dt = Time.unscaledDeltaTime;
            UpdateCamera();
            UpdateRespawns();

            switch (CurrentPhase)
            {
                case Phase.Countdown:
                    _phaseT -= dt;
                    SetBanner(Mathf.Max(1, Mathf.CeilToInt(_phaseT)).ToString());
                    if (_phaseT <= 0f)
                    {
                        CurrentPhase = Phase.Playing;
                        SetInputLocked(false);
                        SetBanner(Sandbox ? "SANDBOX" : "FIGHT!");
                        _bannerClearAt = Time.unscaledTime + 1.2f;
                    }
                    break;

                case Phase.Playing:
                    _matchTime += dt;
                    if (_waves != null) _waves.Tick(Time.deltaTime);
                    CheckRoundEnd();
                    if (_bannerClearAt > 0f && Time.unscaledTime > _bannerClearAt)
                    {
                        SetBanner("");
                        _bannerClearAt = 0f;
                    }
                    break;

                case Phase.RoundEnd:
                    _phaseT -= dt;
                    if (_phaseT <= 0f)
                    {
                        if (_matchWinnerTeam >= 0)
                        {
                            CurrentPhase = Phase.MatchEnd;
                            _phaseT = 4f;
                            SetBanner(TeamLabel(_matchWinnerTeam) + " WINS THE MATCH!");
                        }
                        else StartRound();
                    }
                    break;

                case Phase.MatchEnd:
                    _phaseT -= dt;
                    if (_phaseT <= 0f) Finish(true);
                    break;
            }

            if (_hud != null) _hud.Refresh(ScoreText(), Hint());
        }

        void CheckRoundEnd()
        {
            List<MatchPlayer> humans = Players.FindAll(p => !p.isDummy);
            if (humans.Count == 0) return;

            switch (Settings.mode)
            {
                case GameModeId.Melee:
                {
                    if (Sandbox) return;
                    List<MatchPlayer> alive = humans.FindAll(p => p.alive);
                    if (alive.Count > 1) return;
                    EndRound(alive.Count == 1 ? alive[0].team : -1, alive.Count == 1 ? alive[0].name + " WINS THE ROUND!" : "DRAW!");
                    break;
                }
                case GameModeId.Gang:
                {
                    var aliveTeams = new HashSet<int>();
                    var allTeams = new HashSet<int>();
                    foreach (MatchPlayer p in humans)
                    {
                        allTeams.Add(p.team);
                        if (p.alive) aliveTeams.Add(p.team);
                    }
                    if (allTeams.Count < 2 || aliveTeams.Count > 1) return;
                    int winner = -1;
                    foreach (int t in aliveTeams) winner = t;
                    EndRound(winner, winner >= 0 ? TeamLabel(winner) + " TEAM WINS THE ROUND!" : "DRAW!");
                    break;
                }
                case GameModeId.Waves:
                {
                    if (humans.FindAll(p => p.alive).Count > 0) return;
                    CurrentPhase = Phase.MatchEnd;
                    _phaseT = 4f;
                    SetBanner("EVERYONE IS DOWN!");
                    break;
                }
            }
        }

        void EndRound(int winnerTeam, string banner)
        {
            if (winnerTeam >= 0)
            {
                _scores.TryGetValue(winnerTeam, out int s);
                _scores[winnerTeam] = s + 1;
                int needed = Settings.mode == GameModeId.Soccer ? Settings.goalsToWin : Settings.roundsToWin;
                if (_scores[winnerTeam] >= needed) _matchWinnerTeam = winnerTeam;
            }
            CurrentPhase = Phase.RoundEnd;
            _phaseT = 2.5f;
            SetBanner(banner);
            if (winnerTeam >= 0) ImpactEffects.Confetti(Arena.center + Vector3.up * 9f, _matchWinnerTeam >= 0 ? 14f : 8f);
        }

        void OnGoal(int scoringTeam)
        {
            if (CurrentPhase != Phase.Playing) return;
            EndRound(scoringTeam, "GOAL! " + TeamLabel(scoringTeam).ToUpperInvariant());
        }

        void OnRingedOut(RagdollCombat c)
        {
            MatchPlayer p = Players.Find(x => x.combat == c);
            if (p == null) return;
            p.alive = false;

            if (Settings.mode == GameModeId.Soccer || p.isDummy)
            {
                p.respawnAt = Time.time + Settings.respawnDelay; // keep the ragdoll, put it back later
                return;
            }
            GameObject go = p.rig.gameObject;
            p.rig = null;
            p.combat = null;
            Destroy(go, 2f);
        }

        void UpdateRespawns()
        {
            if (CurrentPhase != Phase.Playing) return;
            foreach (MatchPlayer p in Players)
            {
                if (p.alive || p.respawnAt <= 0f || Time.time < p.respawnAt || p.rig == null) continue;
                Transform sp = PickSpawn(p);
                Vector3 pos = sp != null ? sp.position : Arena.center;
                p.rig.Teleport(pos + Vector3.up * 1.1f);
                p.combat.ResetAfterRespawn();
                p.alive = true;
                p.respawnAt = 0f;
            }
        }

        void UpdateCamera()
        {
            if (_camera == null) return;
            _camera.Points.Clear();
            foreach (MatchPlayer p in Players)
            {
                if (!p.alive || p.rig == null) continue;
                Rigidbody pelvis = p.rig.Body(LimbId.Pelvis);
                if (pelvis != null) _camera.Points.Add(pelvis.position);
            }
            if (Arena.ball != null) _camera.Points.Add(Arena.ball.position);
            if (_camera.Points.Count == 0) _camera.Points.Add(Arena.center);
        }

        void SetInputLocked(bool locked)
        {
            foreach (MatchPlayer p in Players)
            {
                if (p.rig == null) continue;
                var input = p.rig.GetComponent<PlayerAvatarInput>();
                if (input != null) input.Locked = locked;
                if (_testInput.Contains(p))
                {
                    p.rig.GetComponent<ActiveRagdollMotor>().useBuiltInTestInput = !locked;
                    p.combat.useBuiltInTestInput = !locked;
                }
            }
        }

        // ------------------------------------------------------------------ text helpers

        string TeamLabel(int team)
        {
            if (Settings.mode == GameModeId.Melee)
            {
                MatchPlayer p = Players.Find(x => x.slot == team && !x.isDummy);
                return p != null ? p.name : "Player " + (team + 1);
            }
            return TeamNames[Mathf.Abs(team) % TeamNames.Length];
        }

        string ScoreText()
        {
            switch (Settings.mode)
            {
                case GameModeId.Waves:
                    return "WAVES  " + (_waves != null ? _waves.StatusText : "Survive!") + "   " + Mathf.FloorToInt(_matchTime) + "s";
                case GameModeId.Soccer:
                case GameModeId.Gang:
                {
                    int teams = Settings.mode == GameModeId.Soccer ? 2 : Mathf.Max(2, Settings.teamCount);
                    var parts = new List<string>();
                    for (int t = 0; t < teams; t++) { _scores.TryGetValue(t, out int s); parts.Add(TeamLabel(t) + " " + s); }
                    return string.Join("   -   ", parts);
                }
                default:
                {
                    if (Sandbox) return "SANDBOX";
                    var parts = new List<string>();
                    foreach (MatchPlayer p in Players)
                    {
                        if (p.isDummy) continue;
                        _scores.TryGetValue(p.team, out int s);
                        parts.Add(p.name + " " + s);
                    }
                    return string.Join("   |   ", parts);
                }
            }
        }

        string Hint()
        {
            return Sandbox ? "Practice arena. Secret commands: 1/2 forces, 3-7 props, 9 enemy, Shift+1-8 players, -/+/0 time, F1 day/night, Backspace hitboxes"
                           : "Last one standing wins. Ring-outs only.";
        }

        void SetBanner(string text)
        {
            if (_hud != null) _hud.SetBanner(text);
        }

        // ------------------------------------------------------------------ end

        public void Abort() => Finish(false);

        void Finish(bool completed)
        {
            if (_finished) return;
            _finished = true;

            if (completed && !Sandbox)
            {
                SaveData save = SaveSystem.Data;
                save.matches++;
                MatchPlayer local = Players.Find(p => p.isLocal && !p.isDummy);
                if (local != null && local.team == _matchWinnerTeam) save.wins++;
                SaveSystem.Save();
            }

            Cleanup();
            if (_game != null) _game.EndMatch();
        }

        void Cleanup()
        {
            RagdollCombat.RingedOut -= OnRingedOut;
            if (Arena != null) Arena.GoalScored -= OnGoal;
            if (_waves != null) _waves.End();
            BeefCity.AI.ArenaNavigation.DisposeCurrent();
            if (_hud != null) _hud.Destroy();
            if (_camera != null) Destroy(_camera.gameObject);
            foreach (MatchPlayer p in Players) if (p.rig != null) Destroy(p.rig.gameObject);
            if (Arena != null && Arena.root != null) Destroy(Arena.root);
            SecretCommands.ResetTime();
            if (Current == this) Current = null;
        }

        void OnDestroy()
        {
            if (!_finished) Cleanup();
        }
    }

    /// <summary>Score line, big center banner, hint line and a Leave button.</summary>
    public sealed class MatchHud
    {
        readonly Canvas _canvas;
        readonly UnityEngine.UI.Text _score, _banner, _hint;

        public MatchHud(MatchManager match)
        {
            UIKit.EnsureEventSystem();
            _canvas = UIKit.CreateCanvas("MatchHud", 50);

            RectTransform top = UIKit.AddPanel(_canvas.transform, "ScoreBar", new Color(0f, 0f, 0f, 0.45f));
            UIKit.Place(top, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(1100f, 70f));
            top.pivot = new Vector2(0.5f, 1f);
            _score = UIKit.AddLabel(top, "", 34);
            UIKit.Stretch(_score.rectTransform);

            _banner = UIKit.AddLabel(_canvas.transform, "", 120, TextAnchor.MiddleCenter, UIKit.Accent);
            UIKit.Anchor(_banner.rectTransform, new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.7f));
            _banner.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = Color.black;

            _hint = UIKit.AddLabel(_canvas.transform, "", 22, TextAnchor.MiddleCenter, UIKit.Dim);
            UIKit.Anchor(_hint.rectTransform, new Vector2(0.05f, 0f), new Vector2(0.95f, 0.06f));

            UnityEngine.UI.Button leave = UIKit.AddButton(_canvas.transform, "Leave Match", match.Abort, 220f, 50f, 24);
            UIKit.Place(leave.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(220f, 50f));
            leave.GetComponent<RectTransform>().pivot = new Vector2(1f, 1f);
        }

        public void Refresh(string score, string hint)
        {
            _score.text = score;
            _hint.text = hint;
        }

        public void SetBanner(string text) { _banner.text = text; }

        public void Destroy()
        {
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
        }
    }
}
