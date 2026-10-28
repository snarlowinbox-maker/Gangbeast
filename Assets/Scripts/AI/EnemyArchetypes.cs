using UnityEngine;
using BeefCity.Core;
using BeefCity.Customization;
using BeefCity.Graphics;
using BeefCity.Ragdoll;
using BeefCity.Stages;

namespace BeefCity.AI
{
    public enum EnemyKind { RookieRumbler, HeavyHitter, Grabber, Dodger, ShieldCarrier }

    /// <summary>Everything that makes one enemy type different. Difficulty comes from behavior, not hit points.</summary>
    public sealed class EnemyArchetype
    {
        public EnemyKind Kind;
        public string Name;
        public Color Body;
        public CostumeData Costume;
        public float Scale = 1f, MassScale = 1f;
        public float MoveSpeed = 3f;
        public float AttackRange = 1.7f;
        public float CooldownMultiplier = 1f;
        public float PunchPower = 1f;
        public float SenseRange = 32f;
        public float Cost = 1f;           // spent from the wave budget
        public float Weight = 1f;         // how often the director picks it
        public bool CanGrab;              // grabs players and throws them toward the nearest edge
        public bool UsesProps;            // picks up props and throws them at players
        public bool Dodges;               // sidesteps and hops away from incoming attacks, hit-and-run
        public bool HasShield;            // crouches behind a shield, then bashes
        public float VoicePitch = 1f;
    }

    public static class EnemyArchetypes
    {
        static readonly EnemyArchetype[] All =
        {
            new EnemyArchetype
            {
                Kind = EnemyKind.RookieRumbler, Name = "Rookie Rumbler", Body = new Color(0.95f, 0.5f, 0.15f),
                Costume = new CostumeData { hat = 3, nose = 1, extra = 0, accentColor = Color.white },
                MoveSpeed = 3.0f, AttackRange = 1.7f, Cost = 1f, Weight = 3f, UsesProps = true, VoicePitch = 1f
            },
            new EnemyArchetype
            {
                Kind = EnemyKind.HeavyHitter, Name = "Heavy Hitter", Body = new Color(0.55f, 0.1f, 0.12f),
                Costume = new CostumeData { hat = 5, nose = 3, extra = 1, accentColor = new Color(0.1f, 0.1f, 0.12f) },
                Scale = 1.25f, MassScale = 1.7f, MoveSpeed = 2.4f, AttackRange = 1.7f, PunchPower = 1.6f, CooldownMultiplier = 1.2f,
                Cost = 4f, Weight = 1f, VoicePitch = 0.7f
            },
            new EnemyArchetype
            {
                Kind = EnemyKind.Grabber, Name = "Grabber", Body = new Color(0.25f, 0.7f, 0.3f),
                Costume = new CostumeData { hat = 2, nose = 2, extra = 2, accentColor = new Color(0.98f, 0.85f, 0.2f) },
                MoveSpeed = 3.3f, AttackRange = 1.5f, Cost = 2.5f, Weight = 2f, CanGrab = true, VoicePitch = 1.05f
            },
            new EnemyArchetype
            {
                Kind = EnemyKind.Dodger, Name = "Dodger", Body = new Color(0.2f, 0.8f, 0.9f),
                Costume = new CostumeData { hat = 0, nose = 4, extra = 3, accentColor = new Color(0.9f, 0.2f, 0.2f) },
                Scale = 0.92f, MoveSpeed = 4.1f, AttackRange = 1.6f, CooldownMultiplier = 0.8f, Cost = 2f, Weight = 2f, Dodges = true, VoicePitch = 1.4f
            },
            new EnemyArchetype
            {
                Kind = EnemyKind.ShieldCarrier, Name = "Shield Carrier", Body = new Color(0.5f, 0.52f, 0.58f),
                Costume = new CostumeData { hat = 1, nose = 0, extra = 0, accentColor = new Color(0.2f, 0.5f, 0.95f) },
                MoveSpeed = 2.6f, AttackRange = 1.7f, Cost = 3f, Weight = 1.5f, HasShield = true, VoicePitch = 0.9f
            }
        };

        public static EnemyArchetype Get(EnemyKind kind) => All[(int)kind];
        public static int Count => All.Length;
    }

    /// <summary>Builds an enemy ragdoll (same body as players) and attaches the AI brain.</summary>
    public static class EnemyFactory
    {
        public static EnemyBrain Spawn(EnemyKind kind, Vector3 position, float aggression)
        {
            EnemyArchetype a = EnemyArchetypes.Get(kind);

            var go = new GameObject("Enemy_" + a.Name.Replace(" ", ""));
            go.transform.position = position;

            ProceduralRagdollBuilder builder = go.AddComponent<ProceduralRagdollBuilder>();
            builder.Configure(a.Body, true, true);
            builder.ConfigureScale(a.Scale, a.MassScale);
            RagdollRig rig = builder.Build();

            CostumeData look = a.Costume.Clone();
            look.bodyColor = a.Body;
            CostumeBuilder.Apply(rig, look);

            var motor = rig.GetComponent<ActiveRagdollMotor>();
            motor.useBuiltInTestInput = false;
            motor.moveSpeed = a.MoveSpeed;

            var combat = rig.GetComponent<RagdollCombat>();
            combat.useBuiltInTestInput = false;
            combat.Faction = 1;
            combat.punchImpulse *= a.PunchPower;
            combat.superPunchImpulse *= a.PunchPower;
            combat.headbuttImpulse *= a.PunchPower;
            combat.kickImpulse *= a.PunchPower;
            if (MatchManager.Current != null) combat.ringOutY = MatchManager.Current.Arena.ringOutY;

            if (a.HasShield) AttachShield(rig, a.Scale);

            EnemyBrain brain = go.AddComponent<EnemyBrain>();
            brain.Init(a, aggression);
            return brain;
        }

        static void AttachShield(RagdollRig rig, float scale)
        {
            RagdollLimb torso = rig.Get(LimbId.Torso);
            if (torso == null) return;

            GameObject shield = StageKit.BoxOn(torso.gameObject, "Shield",
                new Vector3(0f, 0.02f * scale, 0.42f * scale), new Vector3(1.05f * scale, 1.25f * scale, 0.1f * scale),
                SurfaceType.Metal, new Color(0.55f, 0.6f, 0.72f), 1f, true);
            StageKit.BoxOn(torso.gameObject, "ShieldBoss", new Vector3(0f, 0.02f * scale, 0.5f * scale),
                new Vector3(0.3f * scale, 0.3f * scale, 0.08f * scale), SurfaceType.Metal, new Color(0.9f, 0.75f, 0.2f), 1f, false);

            // The shield belongs to the torso body; keep it from fighting the enemy's own arms and legs.
            Collider shieldCollider = shield.GetComponent<Collider>();
            foreach (Collider c in rig.GetComponentsInChildren<Collider>())
                if (c != shieldCollider) Physics.IgnoreCollision(shieldCollider, c, true);
        }
    }
}
