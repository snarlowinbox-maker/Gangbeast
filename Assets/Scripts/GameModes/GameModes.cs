using System;
using System.Collections.Generic;
using UnityEngine;
using BeefCity.Customization;
using BeefCity.Ragdoll;

namespace BeefCity.Core
{
    public enum GameModeId { Melee, Gang, Waves, Soccer }

    [Serializable]
    public class MatchSettings
    {
        public GameModeId mode = GameModeId.Melee;
        public string stageId = DefaultArena.Id;
        public int roundsToWin = 3;
        public int goalsToWin = 5;
        public int teamCount = 2;
        public float respawnDelay = 3f;
    }

    /// <summary>One participant of a match (human, local guest or dummy).</summary>
    public class MatchPlayer
    {
        public int slot;
        public ulong clientId;
        public string name;
        public CostumeData costume;
        public int team;
        public bool isLocal;
        public int localIndex = -1;   // index into LocalPlayerManager.Players, -1 = no dedicated input device
        public bool isDummy;          // spawned by secret commands, never counts for win conditions
        public RagdollRig rig;
        public RagdollCombat combat;
        public bool alive;
        public float respawnAt;
    }

    /// <summary>
    /// What a stage builder hands back to the match: where to spawn, where the world ends, and (for Soccer) the ball
    /// and goals. Phase 5 stages fill this in exactly like DefaultArena does.
    /// </summary>
    public sealed class ArenaInfo
    {
        public GameObject root;
        public readonly List<Transform> spawns = new List<Transform>();
        public List<Transform>[] teamSpawns;     // optional, indexed by team
        public float ringOutY = -8f;
        public Vector3 center;
        public float radius = 12f;
        public Rigidbody ball;
        public Transform ballSpawn;

        public event Action<int> GoalScored;     // argument = team that scored
        public void RaiseGoal(int scoringTeam) => GoalScored?.Invoke(scoringTeam);
    }

    /// <summary>Stages register themselves here; lobby menus list them and the match builds them by id.</summary>
    public static class StageRegistry
    {
        public delegate ArenaInfo Builder(MatchSettings settings);

        sealed class Entry
        {
            public string Id, Name;
            public Builder Build;
        }

        static readonly List<Entry> Entries = new List<Entry>();

        static StageRegistry()
        {
            DefaultArena.Register();
        }

        public static void Register(string id, string displayName, Builder builder)
        {
            foreach (Entry e in Entries)
            {
                if (e.Id != id) continue;
                e.Name = displayName;
                e.Build = builder;
                return;
            }
            Entries.Add(new Entry { Id = id, Name = displayName, Build = builder });
        }

        public static string[] Ids
        {
            get
            {
                var a = new string[Entries.Count];
                for (int i = 0; i < a.Length; i++) a[i] = Entries[i].Id;
                return a;
            }
        }

        public static string[] Names
        {
            get
            {
                var a = new string[Entries.Count];
                for (int i = 0; i < a.Length; i++) a[i] = Entries[i].Name;
                return a;
            }
        }

        public static string DisplayName(string id)
        {
            foreach (Entry e in Entries) if (e.Id == id) return e.Name;
            return id;
        }

        public static ArenaInfo Build(string id, MatchSettings settings)
        {
            // Soccer needs goals and a ball, so it always uses the pitch.
            if (settings.mode == GameModeId.Soccer) return DefaultArena.Build(settings);
            foreach (Entry e in Entries) if (e.Id == id) return e.Build(settings);
            Debug.LogWarning($"[Stages] Unknown stage '{id}', using the default arena.");
            return DefaultArena.Build(settings);
        }
    }
}
