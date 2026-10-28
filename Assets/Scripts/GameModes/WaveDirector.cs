using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BeefCity.Controls;
using BeefCity.Core;
using BeefCity.Ragdoll;
using BeefCity.Stages;
using Random = UnityEngine.Random;

namespace BeefCity.AI
{
    /// <summary>
    /// Everything the director can tune. Saved as wave_config.json in the persistent data folder the first time the
    /// game runs; edit the file to rebalance waves without recompiling. There is deliberately no enemy health: waves
    /// get harder through who shows up, how many, how aggressively they play, and how fast the stage hazards run.
    /// </summary>
    [Serializable]
    public class WaveConfig
    {
        [Header("Numbers")]
        public float baseBudget = 4f;            // points to spend on wave 1
        public float budgetPerWave = 2.5f;       // extra points each wave
        public int maxAliveBase = 6;             // enemies allowed at once on wave 1
        public float maxAlivePerWave = 0.8f;     // extra simultaneous enemies per wave
        public int maxAliveCap = 18;
        public float extraPerPlayer = 0.5f;      // +50% enemies per additional human player
        public float spawnInterval = 1.2f;
        public float intermission = 4.5f;
        public float firstWaveDelay = 6f;

        [Header("Composition (first wave each type can appear)")]
        public int unlockGrabber = 2;
        public int unlockDodger = 3;
        public int unlockShield = 4;
        public int unlockHeavy = 5;

        [Header("Aggression: reaction time, attack rate, swarming, throw accuracy, grab frequency")]
        public float aggressionStart = 0.2f;
        public float aggressionEnd = 1f;
        public int aggressionWaves = 12;         // waves until aggression reaches its end value

        [Header("Hazard intensity: speed of stage hazards")]
        public float hazardStart = 1f;
        public float hazardEnd = 1.8f;
        public int hazardWaves = 14;
        public int hazardSpikeEvery = 3;         // every n-th wave the hazards run extra fast
        public float hazardSpike = 1.25f;

        static string FilePath => Path.Combine(Application.persistentDataPath, "wave_config.json");

        public static WaveConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonUtility.FromJson<WaveConfig>(File.ReadAllText(FilePath));
                    if (loaded != null) return loaded;
                }
                var fresh = new WaveConfig();
                File.WriteAllText(FilePath, JsonUtility.ToJson(fresh, true)); // leave an editable copy behind
                return fresh;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Waves] Could not read wave_config.json, using defaults: " + e.Message);
                return new WaveConfig();
            }
        }

        public float AggressionAt(int wave) =>
            Mathf.Lerp(aggressionStart, aggressionEnd, Mathf.Clamp01((wave - 1) / (float)Mathf.Max(1, aggressionWaves)));

        public float HazardAt(int wave)
        {
            float h = Mathf.Lerp(hazardStart, hazardEnd, Mathf.Clamp01((wave - 1) / (float)Mathf.Max(1, hazardWaves)));
            if (hazardSpikeEvery > 0 && wave % hazardSpikeEvery == 0) h *= hazardSpike;
            return h;
        }

        public int FirstWave(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Grabber: return unlockGrabber;
                case EnemyKind.Dodger: return unlockDodger;
                case EnemyKind.ShieldCarrier: return unlockShield;
                case EnemyKind.HeavyHitter: return unlockHeavy;
                default: return 1;
            }
        }
    }

    /// <summary>
    /// Co-op survival director for Waves mode. Each wave it spends a point budget on enemy types that have unlocked,
    /// spawns them in a trickle at the spawn points farthest from the players, and starts the next wave when the last
    /// enemy is gone (ring-outs only). Between waves it raises aggression and hazard intensity.
    /// </summary>
    public sealed class WaveDirector : IWaveDirector
    {
        enum Phase { Intermission, Active }

        readonly WaveConfig _c;
        readonly Queue<EnemyKind> _queue = new Queue<EnemyKind>();
        readonly List<EnemyBrain> _alive = new List<EnemyBrain>();
        MatchManager _match;
        Phase _phase;
        float _timer, _nextSpawn;
        int _wave, _defeated;

        public WaveDirector(WaveConfig config) { _c = config; }

        public string StatusText
        {
            get
            {
                if (_phase == Phase.Intermission)
                    return _wave == 0 ? "Get ready" : "Wave " + _wave + " cleared - next in " + Mathf.CeilToInt(Mathf.Max(0f, _timer)) + "s";
                return "Wave " + _wave + "   Enemies left " + (_queue.Count + _alive.Count) + "   Defeated " + _defeated;
            }
        }

        public void Begin(MatchManager match)
        {
            _match = match;
            _phase = Phase.Intermission;
            _timer = _c.firstWaveDelay;
            _wave = 0;
            _defeated = 0;
            HazardScale.Value = _c.hazardStart;
            EnemyBrain.Eliminated += OnEliminated;
            ArenaNavigation.Build(match.Arena);
        }

        public void End()
        {
            EnemyBrain.Eliminated -= OnEliminated;
            foreach (EnemyBrain e in _alive)
                if (e != null) UnityEngine.Object.Destroy(e.gameObject);
            _alive.Clear();
            _queue.Clear();
            HazardScale.Value = 1f;
            ArenaNavigation.DisposeCurrent();
        }

        public void Tick(float dt)
        {
            _alive.RemoveAll(e => e == null);

            if (_phase == Phase.Intermission)
            {
                _timer -= dt;
                if (_timer <= 0f) StartWave();
                return;
            }

            if (_queue.Count > 0 && _alive.Count < MaxAlive() && Time.time >= _nextSpawn)
            {
                SpawnOne();
                _nextSpawn = Time.time + _c.spawnInterval * Random.Range(0.7f, 1.3f);
            }

            if (_queue.Count == 0 && _alive.Count == 0)
            {
                _phase = Phase.Intermission;
                _timer = _c.intermission;
            }
        }

        // ------------------------------------------------------------------ waves

        int HumanCount()
        {
            int n = 0;
            foreach (MatchPlayer p in _match.Players) if (!p.isDummy) n++;
            return Mathf.Max(1, n);
        }

        float CoopFactor() => 1f + _c.extraPerPlayer * (HumanCount() - 1);

        int MaxAlive()
        {
            float n = (_c.maxAliveBase + _c.maxAlivePerWave * (_wave - 1)) * CoopFactor();
            return Mathf.Clamp(Mathf.RoundToInt(n), 1, Mathf.RoundToInt(_c.maxAliveCap * CoopFactor()));
        }

        void StartWave()
        {
            _wave++;
            HazardScale.Value = _c.HazardAt(_wave);
            Compose();
            _phase = Phase.Active;
            _nextSpawn = Time.time;
        }

        /// <summary>Spends the wave budget on unlocked enemy types, weighted by how common each type is.</summary>
        void Compose()
        {
            _queue.Clear();
            float budget = (_c.baseBudget + _c.budgetPerWave * (_wave - 1)) * CoopFactor();

            var pool = new List<EnemyKind>();
            for (int i = 0; i < EnemyArchetypes.Count; i++)
            {
                var kind = (EnemyKind)i;
                if (_wave >= _c.FirstWave(kind)) pool.Add(kind);
            }

            int guard = 0;
            while (budget > 0f && guard++ < 200)
            {
                float total = 0f;
                foreach (EnemyKind k in pool)
                {
                    EnemyArchetype a = EnemyArchetypes.Get(k);
                    if (a.Cost <= budget) total += a.Weight;
                }
                if (total <= 0f) break;

                float roll = Random.value * total;
                foreach (EnemyKind k in pool)
                {
                    EnemyArchetype a = EnemyArchetypes.Get(k);
                    if (a.Cost > budget) continue;
                    roll -= a.Weight;
                    if (roll > 0f) continue;
                    _queue.Enqueue(k);
                    budget -= a.Cost;
                    break;
                }
            }
            if (_queue.Count == 0) _queue.Enqueue(EnemyKind.RookieRumbler);
        }

        void SpawnOne()
        {
            EnemyKind kind = _queue.Dequeue();
            Vector3 pos = PickSpawnPoint();
            EnemyBrain brain = EnemyFactory.Spawn(kind, pos, _c.AggressionAt(_wave));
            _alive.Add(brain);
        }

        /// <summary>One of the spawn points farthest from every living player (so enemies arrive, not appear on top of you).</summary>
        Vector3 PickSpawnPoint()
        {
            List<Transform> spawns = _match.Arena.spawns;
            if (spawns.Count == 0) return _match.Arena.center + Vector3.up * 0.1f;

            var scored = new List<KeyValuePair<float, Transform>>();
            foreach (Transform s in spawns)
            {
                float nearest = float.MaxValue;
                foreach (MatchPlayer p in _match.Players)
                {
                    if (p.isDummy || !p.alive || p.rig == null) continue;
                    Rigidbody pel = p.rig.Body(LimbId.Pelvis);
                    if (pel != null) nearest = Mathf.Min(nearest, Vector3.Distance(s.position, pel.position));
                }
                scored.Add(new KeyValuePair<float, Transform>(nearest, s));
            }
            scored.Sort((a, b) => b.Key.CompareTo(a.Key)); // farthest first

            Transform pick = scored[Random.Range(0, Mathf.Min(3, scored.Count))].Value;
            return pick.position + new Vector3(Random.Range(-1.2f, 1.2f), 0.05f, Random.Range(-1.2f, 1.2f));
        }

        void OnEliminated(EnemyBrain brain)
        {
            _alive.Remove(brain);
            _defeated++;
        }
    }

    /// <summary>Registers the director with Waves mode and wires up the secret "9 = enemy" command.</summary>
    static class WaveDirectorBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            WaveDirectorHook.Factory = () => new WaveDirector(WaveConfig.Load());
            SecretCommands.EnemySpawnRequested += SpawnRandomEnemy;
        }

        static void SpawnRandomEnemy(Vector3 position)
        {
            MatchManager match = MatchManager.Current;
            if (match == null) return;

            // Outside Waves mode nobody built the navigation yet; do it on first use.
            if (ArenaNavigation.Current == null) ArenaNavigation.Build(match.Arena);

            float total = 0f;
            for (int i = 0; i < EnemyArchetypes.Count; i++) total += EnemyArchetypes.Get((EnemyKind)i).Weight;
            float roll = Random.value * total;
            EnemyKind kind = EnemyKind.RookieRumbler;
            for (int i = 0; i < EnemyArchetypes.Count; i++)
            {
                roll -= EnemyArchetypes.Get((EnemyKind)i).Weight;
                if (roll <= 0f) { kind = (EnemyKind)i; break; }
            }
            EnemyFactory.Spawn(kind, position, 0.6f);
        }
    }
}
