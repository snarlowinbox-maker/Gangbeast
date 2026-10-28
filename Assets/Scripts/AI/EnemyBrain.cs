using System;
using System.Collections.Generic;
using UnityEngine;
using BeefCity.Core;
using BeefCity.Ragdoll;
using BeefCity.Stages;
using Random = UnityEngine.Random;

namespace BeefCity.AI
{
    public enum EnemyState { Idle, Patrol, Search, Approach, Attack, Grab, Defend, Stagger, Retreat, UseObject }

    /// <summary>
    /// The AI for one enemy ragdoll. It does not move the body directly: it feeds the same motor and combat inputs a
    /// player would (world-space move direction, punch, kick, headbutt, grab, lift, throw). A behavior tree picks what
    /// to do every ~0.1-0.3 s; movement is applied every frame along a NavMesh / A* path with edge and hazard avoidance.
    /// Priority: Stagger, Retreat, Defend, Grab-and-throw, Use object, Attack, Approach, Search, Patrol, Idle.
    /// </summary>
    public sealed class EnemyBrain : MonoBehaviour
    {
        public static event Action<EnemyBrain> Eliminated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Eliminated = null; }

        struct Scheduled
        {
            public float Time;
            public Action Action;
        }

        public EnemyArchetype A { get; private set; }
        public float Aggression { get; set; } = 0.3f;
        public EnemyState State { get; private set; }
        public RagdollCombat Combat { get; private set; }
        public ActiveRagdollMotor Motor { get; private set; }
        public Rigidbody Pelvis { get; private set; }

        public bool HasTarget { get; private set; }
        public bool HasLastKnown { get; private set; }
        public Vector3 LastKnown { get; private set; }
        public float TargetDistance { get; private set; }
        public Rigidbody TargetPelvis { get; private set; }
        public RagdollCombat TargetCombat { get; private set; }
        public Rigidbody NearbyProp { get; private set; }

        BTNode<EnemyBrain> _tree;
        AudioSource _voice;
        MatchPlayer _target;
        bool _ready, _wasStaggered;
        float _nextThink, _lastSeenAt, _nextPropScan, _voiceBusyUntil, _nextTauntAt;
        float _retreatUntil, _guardUntil, _guardCooldownUntil, _dodgeUntil, _dodgeCooldownUntil, _dodgeSign = 1f;
        float _grabCooldownUntil, _propCooldownUntil, _rangeEnterAt = -1f, _searchArrivedAt = -1f, _patrolWaitUntil;
        float _slotAngle, _slotSign = 1f;
        bool _hasPatrol;
        Vector3 _patrolPoint;

        // movement request (applied every frame by Locomote)
        bool _hasGoal, _direct;
        Vector3 _goal, _directDir;
        float _speed = 1f;
        Vector3? _faceDir;
        readonly List<Vector3> _corners = new List<Vector3>();
        int _cornerIndex;
        Vector3 _pathGoal;
        float _nextPathAt;

        readonly List<Scheduled> _scheduled = new List<Scheduled>();
        readonly Collider[] _overlap = new Collider[16];
        readonly Collider[] _propBuffer = new Collider[64];

        // ------------------------------------------------------------------ setup

        public void Init(EnemyArchetype archetype, float aggression)
        {
            A = archetype;
            Aggression = aggression;

            RagdollRig rig = GetComponent<RagdollRig>();
            Combat = rig.GetComponent<RagdollCombat>();
            Motor = rig.GetComponent<ActiveRagdollMotor>();
            Pelvis = rig.Body(LimbId.Pelvis);

            Rigidbody head = rig.Body(LimbId.Head);
            if (head != null)
            {
                _voice = head.gameObject.AddComponent<AudioSource>();
                _voice.playOnAwake = false;
                _voice.spatialBlend = 1f;
                _voice.minDistance = 4f;
                _voice.maxDistance = 45f;
                _voice.rolloffMode = AudioRolloffMode.Linear;
            }

            _slotAngle = Random.Range(0f, 360f);
            _slotSign = Random.value < 0.5f ? -1f : 1f;
            _nextTauntAt = Time.time + Random.Range(5f, 10f);
            _tree = BuildTree();
            RagdollCombat.RingedOut += OnRingedOut;
            Voice(VoiceKind.Taunt);
            _ready = true;
        }

        void OnDestroy()
        {
            RagdollCombat.RingedOut -= OnRingedOut;
        }

        void OnRingedOut(RagdollCombat c)
        {
            if (c != Combat) return;
            _ready = false;
            if (Eliminated != null) Eliminated(this);
            Destroy(gameObject, 1.5f);
        }

        // ------------------------------------------------------------------ behavior tree

        BTNode<EnemyBrain> BuildTree()
        {
            return new Selector<EnemyBrain>(
                // 1. Stagger: knocked out or stunned, nothing else matters
                new Sequence<EnemyBrain>(
                    new Condition<EnemyBrain>(b => b.IsStaggered),
                    new Do<EnemyBrain>(b => b.DoStagger())),
                // 2. Retreat: hit-and-run after an attack
                new Sequence<EnemyBrain>(
                    new Condition<EnemyBrain>(b => b.WantsRetreat),
                    new Do<EnemyBrain>(b => b.DoRetreat())),
                // 3. Defend: shield guard / dodge
                new Sequence<EnemyBrain>(
                    new Condition<EnemyBrain>(b => b.WantsDefend),
                    new Do<EnemyBrain>(b => b.DoDefend(), () => EndGuard(false))),
                // 4. Grab a player and throw them toward the nearest edge or hazard
                new GrabPlan(false),
                // 5. Pick up a prop and throw it at a player
                new GrabPlan(true),
                // 6. Attack when in range (rate limited)
                new Sequence<EnemyBrain>(
                    new Condition<EnemyBrain>(b => b.InAttackRange),
                    new Cooldown<EnemyBrain>(b => b.AttackCooldown(), new Do<EnemyBrain>(b => b.DoAttack()))),
                // 7. Approach / swarm
                new Sequence<EnemyBrain>(
                    new Condition<EnemyBrain>(b => b.HasTarget),
                    new Do<EnemyBrain>(b => b.DoApproach())),
                // 8. Search where the target was last seen
                new Sequence<EnemyBrain>(
                    new Condition<EnemyBrain>(b => b.HasLastKnown),
                    new Do<EnemyBrain>(b => b.DoSearch())),
                // 9. Patrol the arena
                new Sequence<EnemyBrain>(
                    new Condition<EnemyBrain>(b => b._hasPatrol),
                    new Do<EnemyBrain>(b => b.DoPatrol())),
                // 10. Idle
                new Do<EnemyBrain>(b => b.DoIdle()));
        }

        // conditions
        bool IsStaggered => Combat.IsKnockedOut || Combat.IsStunned;
        bool WantsRetreat => HasTarget && Time.time < _retreatUntil;
        bool InAttackRange => HasTarget && !Combat.MoveActive && TargetDistance <= A.AttackRange * A.Scale + 0.2f;

        bool WantsDefend
        {
            get
            {
                if (!HasTarget || Combat.HoldingAny) return false;
                if (A.HasShield) return _guardUntil > 0f || (Time.time >= _guardCooldownUntil && TargetDistance < 5f);
                if (A.Dodges)
                {
                    if (Time.time < _dodgeUntil) return true;
                    return Time.time >= _dodgeCooldownUntil && TargetDistance < 3.2f && TargetCombat != null
                           && TargetCombat.IsAttacking && Random.value < Mathf.Lerp(0.4f, 0.9f, Aggression);
                }
                return false;
            }
        }

        float AttackCooldown() => Mathf.Lerp(1.9f, 0.55f, Aggression) * A.CooldownMultiplier * Random.Range(0.8f, 1.2f);

        // ------------------------------------------------------------------ actions

        NodeStatus DoStagger()
        {
            SetState(EnemyState.Stagger);
            Stop();
            if (!_wasStaggered)
            {
                _wasStaggered = true;
                Voice(VoiceKind.Hurt);
            }
            return NodeStatus.Running;
        }

        NodeStatus DoRetreat()
        {
            SetState(EnemyState.Retreat);
            Vector3 away = Pelvis.position - TargetPelvis.position;
            away.y = 0f;
            MoveDirect(away.sqrMagnitude > 0.01f ? away.normalized : -Motor.Facing, 1f);
            FacePoint(TargetPelvis.position);
            return NodeStatus.Running;
        }

        NodeStatus DoDefend()
        {
            SetState(EnemyState.Defend);
            FacePoint(TargetPelvis.position);
            return A.HasShield ? DoGuard() : DoDodge();
        }

        NodeStatus DoGuard()
        {
            if (_guardUntil <= 0f)
            {
                _guardUntil = Time.time + Random.Range(1f, 2f);
                Combat.HeadbuttButton(true); // crouch behind the shield
                Combat.DefenseMultiplier = 0.3f;
            }
            MoveTo(TargetPelvis.position, 0.45f);
            if (Time.time >= _guardUntil)
            {
                EndGuard(true);
                return NodeStatus.Success;
            }
            return NodeStatus.Running;
        }

        /// <summary>Leaves the guard stance. With bash = true, releasing the crouch fires a charged headbutt.</summary>
        void EndGuard(bool bash)
        {
            if (_guardUntil <= 0f) return; // idempotent: also called as an interruption cleanup
            _guardUntil = 0f;
            _guardCooldownUntil = Time.time + Random.Range(2.5f, 4f);
            Combat.DefenseMultiplier = 1f;
            if (bash) Voice(VoiceKind.Grunt);
            Combat.HeadbuttButton(false);
        }

        NodeStatus DoDodge()
        {
            if (Time.time >= _dodgeUntil)
            {
                _dodgeUntil = Time.time + 0.45f;
                _dodgeCooldownUntil = Time.time + 1.2f;
                _dodgeSign = Random.value < 0.5f ? -1f : 1f;
                if (Random.value < 0.4f) Combat.JumpPressed();
            }
            Vector3 toTarget = TargetPelvis.position - Pelvis.position;
            toTarget.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized) * _dodgeSign;
            MoveDirect(side, 1f);
            return Time.time < _dodgeUntil ? NodeStatus.Running : NodeStatus.Success;
        }

        NodeStatus DoAttack()
        {
            SetState(EnemyState.Attack);
            Stop();
            FacePoint(TargetPelvis.position);

            // A short reaction delay when first getting in range; faster at high aggression.
            float reaction = Mathf.Lerp(0.8f, 0.15f, Aggression);
            if (_rangeEnterAt < 0f) _rangeEnterAt = Time.time;
            if (Time.time - _rangeEnterAt < reaction) return NodeStatus.Running;
            _rangeEnterAt = -1f;

            float r = Random.value;
            bool left = Random.value < 0.5f;
            if (A.Kind == EnemyKind.HeavyHitter)
            {
                if (r < 0.45f) Combat.Perform(MoveId.SuperPunch, left);
                else if (r < 0.8f) Combat.Perform(MoveId.ChargedHeadbutt);
                else if (Aggression > 0.6f) Combat.Perform(MoveId.KnockoutHeadbutt);
                else Combat.Perform(MoveId.SuperPunch, left);
            }
            else
            {
                if (r < 0.5f) Punch(left);
                else if (r < 0.8f) Combat.KickPressed();
                else TapHeadbutt();
            }

            Voice(VoiceKind.Grunt);
            if (A.Dodges) _retreatUntil = Time.time + Random.Range(0.8f, 1.6f); // hit and run
            return NodeStatus.Success;
        }

        void Punch(bool left)
        {
            Combat.ArmButton(left, true);
            Schedule(0.07f, () => Combat.ArmButton(left, false)); // released before the grab delay: a punch, not a grab
        }

        void TapHeadbutt()
        {
            Combat.HeadbuttButton(true);
            Schedule(0.08f, () => Combat.HeadbuttButton(false));
        }

        NodeStatus DoApproach()
        {
            SetState(EnemyState.Approach);
            _rangeEnterAt = -1f;
            Vector3 tp = TargetPelvis.position;
            Vector3 goal = tp;

            // Swarm: at higher aggression each enemy takes its own slot around the target and slowly circles it.
            if (Aggression > 0.45f && TargetDistance < 12f)
            {
                float ang = (_slotAngle + Time.time * 6f * _slotSign) * Mathf.Deg2Rad;
                goal = tp + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * (A.AttackRange * A.Scale * 0.9f);
            }
            MoveTo(goal, 1f);
            FacePoint(tp);

            if (Time.time >= _nextTauntAt)
            {
                _nextTauntAt = Time.time + Random.Range(6f, 12f);
                Voice(VoiceKind.Taunt);
            }
            return NodeStatus.Running;
        }

        NodeStatus DoSearch()
        {
            SetState(EnemyState.Search);
            Vector3 flat = LastKnown - Pelvis.position;
            flat.y = 0f;
            if (flat.magnitude > 1.6f)
            {
                _searchArrivedAt = -1f;
                MoveTo(LastKnown, 0.8f);
                return NodeStatus.Running;
            }

            Stop();
            if (_searchArrivedAt < 0f) _searchArrivedAt = Time.time;
            FaceDir(Quaternion.AngleAxis((Time.time - _searchArrivedAt) * 120f, Vector3.up) * Vector3.forward); // look around
            if (Time.time - _searchArrivedAt > 3f)
            {
                HasLastKnown = false;
                _searchArrivedAt = -1f;
                return NodeStatus.Success;
            }
            return NodeStatus.Running;
        }

        NodeStatus DoPatrol()
        {
            SetState(EnemyState.Patrol);
            if (Time.time < _patrolWaitUntil)
            {
                Stop();
                return NodeStatus.Running;
            }

            Vector3 flat = _patrolPoint - Pelvis.position;
            flat.y = 0f;
            if (_patrolPoint == Vector3.zero || flat.magnitude < 1.6f)
            {
                if (_patrolPoint != Vector3.zero) _patrolWaitUntil = Time.time + Random.Range(1f, 2.5f);
                _patrolPoint = PickPatrolPoint();
                return NodeStatus.Running;
            }
            MoveTo(_patrolPoint, 0.6f);
            return NodeStatus.Running;
        }

        NodeStatus DoIdle()
        {
            SetState(EnemyState.Idle);
            Stop();
            if (Time.time >= _nextTauntAt)
            {
                _nextTauntAt = Time.time + Random.Range(6f, 12f);
                Voice(VoiceKind.Taunt);
            }
            return NodeStatus.Success;
        }

        Vector3 PickPatrolPoint()
        {
            MatchManager m = MatchManager.Current;
            if (m == null || m.Arena.spawns.Count == 0) return Pelvis.position;
            Transform t = m.Arena.spawns[Random.Range(0, m.Arena.spawns.Count)];
            return t.position + new Vector3(Random.Range(-1.5f, 1.5f), 1f, Random.Range(-1.5f, 1.5f));
        }

        // ------------------------------------------------------------------ perception

        void Sense()
        {
            HasTarget = false;
            if (!IsStaggered) _wasStaggered = false;

            MatchManager match = MatchManager.Current;
            _hasPatrol = match != null && match.Arena != null && match.Arena.spawns.Count > 0;
            if (match == null) return;

            float best = float.MaxValue;
            MatchPlayer pick = null;
            foreach (MatchPlayer p in match.Players)
            {
                if (p.isDummy || !p.alive || p.rig == null) continue;
                Rigidbody pel = p.rig.Body(LimbId.Pelvis);
                if (pel == null) continue;
                float d = (pel.position - Pelvis.position).sqrMagnitude;
                if (p == _target) d *= 0.65f; // stick with the current target unless another is clearly closer
                if (d < best) { best = d; pick = p; }
            }

            if (pick != null)
            {
                Rigidbody pelvis = pick.rig.Body(LimbId.Pelvis);
                float dist = Vector3.Distance(pelvis.position, Pelvis.position);
                if (dist <= A.SenseRange)
                {
                    _target = pick;
                    TargetCombat = pick.combat;
                    TargetPelvis = pelvis;
                    TargetDistance = dist;
                    HasTarget = true;
                    LastKnown = pelvis.position;
                    HasLastKnown = true;
                    _lastSeenAt = Time.time;
                }
            }
            if (HasLastKnown && Time.time - _lastSeenAt > 14f) HasLastKnown = false;

            if (Time.time >= _nextPropScan)
            {
                _nextPropScan = Time.time + 1f;
                ScanForProp();
            }
        }

        void ScanForProp()
        {
            NearbyProp = null;
            if (!(A.UsesProps || Aggression > 0.5f)) return;

            int n = Physics.OverlapSphereNonAlloc(Pelvis.position, 9f, _propBuffer, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Rigidbody rb = _propBuffer[i].attachedRigidbody;
                if (rb == null || rb.isKinematic || rb.mass < 0.4f || rb.mass > 80f) continue;
                if (rb.GetComponentInParent<RagdollRig>() != null) continue;
                if (rb.GetComponent<Joint>() != null || rb.GetComponentInParent<Breakable>() != null) continue;
                float d = (rb.position - Pelvis.position).sqrMagnitude;
                if (d < best) { best = d; NearbyProp = rb; }
            }
        }

        // ------------------------------------------------------------------ movement (applied every frame)

        public void MoveTo(Vector3 goal, float speed = 1f)
        {
            _hasGoal = true;
            _direct = false;
            _goal = goal;
            _speed = speed;
        }

        public void MoveDirect(Vector3 direction, float speed = 1f)
        {
            _hasGoal = true;
            _direct = true;
            _directDir = direction;
            _speed = speed;
        }

        public void Stop() { _hasGoal = false; }
        public void FacePoint(Vector3 point) { _faceDir = point - Pelvis.position; }
        public void FaceDir(Vector3 dir) { _faceDir = dir; }
        public void SetState(EnemyState s) { State = s; }

        void Locomote()
        {
            if (Combat.IsKnockedOut || !_hasGoal)
            {
                Motor.SetMoveWorld(Vector3.zero, _faceDir);
                return;
            }

            Vector3 pos = Pelvis.position;
            Vector3 dir = _direct ? _directDir : PathDirection(pos);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                Motor.SetMoveWorld(Vector3.zero, _faceDir);
                return;
            }
            dir = AvoidDanger(pos, dir.normalized);
            Motor.SetMoveWorld(dir * _speed, _faceDir);
        }

        Vector3 PathDirection(Vector3 pos)
        {
            Vector3 straight = _goal - pos;
            straight.y = 0f;
            if (straight.magnitude < 5f) return straight; // close enough to steer directly

            ArenaNavigation nav = ArenaNavigation.Current;
            if (nav == null) return straight;

            if (_corners.Count == 0 || Time.time >= _nextPathAt || (_pathGoal - _goal).sqrMagnitude > 4f)
            {
                _nextPathAt = Time.time + 0.7f;
                if (nav.TryPath(pos, _goal, _corners))
                {
                    _pathGoal = _goal;
                    _cornerIndex = Mathf.Min(1, _corners.Count - 1);
                }
                else _corners.Clear();
            }
            if (_corners.Count == 0) return straight;

            while (_cornerIndex < _corners.Count - 1)
            {
                Vector3 d = _corners[_cornerIndex] - pos;
                d.y = 0f;
                if (d.magnitude > 1.1f) break;
                _cornerIndex++;
            }
            Vector3 next = _corners[Mathf.Clamp(_cornerIndex, 0, _corners.Count - 1)] - pos;
            next.y = 0f;
            return next;
        }

        Vector3 AvoidDanger(Vector3 pos, Vector3 dir)
        {
            if (IsSafeAt(pos + dir * 1.5f)) return dir;
            float[] turns = { 35f, -35f, 70f, -70f, 110f, -110f };
            foreach (float a in turns)
            {
                Vector3 d = Quaternion.AngleAxis(a, Vector3.up) * dir;
                if (IsSafeAt(pos + d * 1.5f)) return d * 0.8f;
            }
            return Vector3.zero;
        }

        /// <summary>Solid ground within a short drop, and no kill zone, at this point.</summary>
        bool IsSafeAt(Vector3 p)
        {
            if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3.6f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.point.y < Pelvis.position.y - 2.6f) return false;
            int n = Physics.OverlapSphereNonAlloc(hit.point + Vector3.up * 0.6f, 0.8f, _overlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
                if (_overlap[i].GetComponent<KillZone>() != null) return false;
            return true;
        }

        /// <summary>How far you can walk in a direction before reaching an edge or hazard.</summary>
        public float DistanceToDanger(Vector3 dir, float max = 16f)
        {
            dir.y = 0f;
            dir.Normalize();
            Vector3 pos = Pelvis.position;
            for (float d = 1.5f; d <= max; d += 1f)
                if (!IsSafeAt(pos + dir * d)) return d;
            return max + 1f;
        }

        /// <summary>The direction toward the nearest edge or hazard: where a Grabber wants to throw its victim.</summary>
        public Vector3 ThrowAim()
        {
            float bestDist = float.MaxValue;
            Vector3 best = Vector3.forward;
            for (int i = 0; i < 16; i++)
            {
                Vector3 d = Quaternion.AngleAxis(i * 22.5f, Vector3.up) * Vector3.forward;
                float dist = DistanceToDanger(d, 16f);
                if (dist < bestDist) { bestDist = dist; best = d; }
            }
            float jitter = (1f - Aggression) * 55f; // sloppy aim when the wave is easy
            return Quaternion.AngleAxis(Random.Range(-jitter, jitter), Vector3.up) * best;
        }

        public Vector3 AimAtTarget()
        {
            if (!HasTarget) return Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Vector3.forward;
            Vector3 d = TargetPelvis.position - Pelvis.position;
            d.y = 0f;
            return d.normalized;
        }

        // ------------------------------------------------------------------ grab-plan support

        public bool CanStartGrab(bool prop, out Rigidbody victim)
        {
            victim = null;
            if (Combat.IsKnockedOut || Combat.IsStunned || Combat.HoldingAny || Combat.MoveActive) return false;

            if (!prop)
            {
                if (!HasTarget || TargetDistance > 4.5f || Time.time < _grabCooldownUntil) return false;
                if (!A.CanGrab && !(Aggression > 0.6f && Random.value < 0.06f)) return false;
                victim = TargetPelvis;
                return victim != null;
            }

            if (!(A.UsesProps || Aggression > 0.5f)) return false;
            if (NearbyProp == null || Time.time < _propCooldownUntil) return false;
            if (HasTarget && TargetDistance < 2.2f) return false; // busy fighting
            if (Random.value > 0.08f) return false;               // do not grab a prop the moment one is nearby
            victim = NearbyProp;
            return true;
        }

        public void StartGrabCooldown(bool prop)
        {
            if (prop) _propCooldownUntil = Time.time + Random.Range(7f, 12f);
            else _grabCooldownUntil = Time.time + Mathf.Lerp(5f, 2f, Aggression);
        }

        public void CancelGrab()
        {
            Combat.ArmButton(true, false);
            Combat.ArmButton(false, false);
        }

        // ------------------------------------------------------------------ utilities

        void Schedule(float delay, Action action)
        {
            _scheduled.Add(new Scheduled { Time = Time.time + delay, Action = action });
        }

        void RunScheduled()
        {
            for (int i = _scheduled.Count - 1; i >= 0; i--)
            {
                if (Time.time < _scheduled[i].Time) continue;
                Action a = _scheduled[i].Action;
                _scheduled.RemoveAt(i);
                a();
            }
        }

        public void Voice(VoiceKind kind)
        {
            if (_voice == null) return;
            if (kind != VoiceKind.Hurt && Time.time < _voiceBusyUntil) return;
            AudioClip clip = ProceduralVoice.Get(kind, A.VoicePitch, Random.Range(0, 3));
            _voice.pitch = Random.Range(0.95f, 1.05f);
            _voice.PlayOneShot(clip, 0.8f);
            _voiceBusyUntil = Time.time + clip.length * 0.8f;
        }

        void Update()
        {
            if (!_ready || Pelvis == null) return;
            RunScheduled();
            if (Time.time >= _nextThink)
            {
                _nextThink = Time.time + Mathf.Lerp(0.3f, 0.12f, Aggression);
                Sense();
                _tree.Tick(this);
            }
            Locomote();
        }
    }

    /// <summary>
    /// Grab-and-throw as one behavior-tree node with its own phases: reach, lift, carry, release.
    /// Foe version: grab a player and carry them toward the nearest edge or hazard, then throw.
    /// Prop version ("use environmental objects"): pick up a nearby prop and throw it at a player.
    /// </summary>
    public sealed class GrabPlan : BTNode<EnemyBrain>
    {
        enum Phase { Idle, Reach, Lift, Carry, Release }

        readonly bool _prop;
        Phase _phase;
        Rigidbody _victim;
        float _t0, _lift, _carry, _releasedAt, _nextAim;
        Vector3 _aim;
        EnemyBrain _brain;

        public GrabPlan(bool prop) { _prop = prop; }

        public override NodeStatus Tick(EnemyBrain b)
        {
            _brain = b;
            switch (_phase)
            {
                case Phase.Idle:
                    if (!b.CanStartGrab(_prop, out _victim)) return NodeStatus.Failure;
                    _phase = Phase.Reach;
                    _t0 = Time.time;
                    b.SetState(_prop ? EnemyState.UseObject : EnemyState.Grab);
                    b.Combat.ArmButton(true, true);   // hold both arm buttons: reach out and grab
                    b.Combat.ArmButton(false, true);
                    return NodeStatus.Running;

                case Phase.Reach:
                {
                    if (_victim == null || b.Combat.IsKnockedOut) return Abort(b);
                    b.FacePoint(_victim.position);
                    Vector3 to = _victim.position - b.Pelvis.position;
                    to.y = 0f;
                    if (to.magnitude > 1.3f) b.MoveDirect(to.normalized, 0.8f);
                    else b.Stop();

                    if (b.Combat.HoldingBoth || (b.Combat.HoldingAny && Time.time - _t0 > 1f))
                    {
                        b.Combat.TauntPressed(); // taunt with both hands full = lift
                        _phase = Phase.Lift;
                        _lift = Time.time;
                    }
                    else if (Time.time - _t0 > 3.2f) return Abort(b);
                    return NodeStatus.Running;
                }

                case Phase.Lift:
                    b.Stop();
                    if (!b.Combat.HoldingAny) return Abort(b);
                    if (b.Combat.IsLifting)
                    {
                        _phase = Phase.Carry;
                        _carry = Time.time;
                        _aim = _prop ? b.AimAtTarget() : b.ThrowAim();
                        _nextAim = Time.time + 0.6f;
                        return NodeStatus.Running;
                    }
                    if (Time.time - _lift > 0.35f)
                    {
                        b.Combat.TauntPressed();
                        _lift = Time.time;
                    }
                    if (Time.time - _t0 > 5f) return Abort(b);
                    return NodeStatus.Running;

                case Phase.Carry:
                {
                    if (!b.Combat.IsLifting) return Abort(b); // lost the grip
                    if (_prop && Time.time > _nextAim)
                    {
                        _aim = b.AimAtTarget();
                        _nextAim = Time.time + 0.6f;
                    }
                    b.FaceDir(_aim);
                    b.MoveDirect(_aim, 0.55f);

                    float held = Time.time - _carry;
                    bool arrived = _prop
                        ? b.HasTarget && b.TargetDistance < 6f && held > 0.8f
                        : held > 0.6f && b.DistanceToDanger(_aim, 6f) < 4.2f;

                    if (arrived || held > 6.5f)
                    {
                        // Letting go of a grab button while lifting throws along the current move direction.
                        b.Combat.ArmButton(true, false);
                        b.Combat.ArmButton(false, false);
                        b.Voice(VoiceKind.Yell);
                        b.StartGrabCooldown(_prop);
                        _phase = Phase.Release;
                        _releasedAt = Time.time;
                    }
                    return NodeStatus.Running;
                }

                default: // Release
                    b.Stop();
                    if (Time.time - _releasedAt > 0.5f)
                    {
                        _phase = Phase.Idle;
                        return NodeStatus.Success;
                    }
                    return NodeStatus.Running;
            }
        }

        NodeStatus Abort(EnemyBrain b)
        {
            b.CancelGrab();
            b.StartGrabCooldown(_prop);
            _phase = Phase.Idle;
            return NodeStatus.Failure;
        }

        public override void Reset()
        {
            // Interrupted (stagger, defend...): let go of whatever is held.
            if (_brain != null && _phase != Phase.Idle && _phase != Phase.Release) _brain.CancelGrab();
            _phase = Phase.Idle;
        }
    }
}
