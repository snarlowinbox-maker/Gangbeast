using System;
using System.Collections.Generic;
using UnityEngine;
using BeefCity.Graphics;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeefCity.Ragdoll
{
    public enum MoveId
    {
        None,
        Backflip, BodySlam, ChargedHeadbutt, DropSlide, FlipKick, FlyingDropKick, Handstand,
        KnockoutHeadbutt, LeapUpClimb, Lift, Powerslide, Sit, SlideTackle, StandingDropKick,
        SuperDropKick, SuperPunch, SwingUpClimb, Taunt, ThrowFoe, ZombieWaddle
    }

    /// <summary>
    /// Phase 1 combat for the active ragdoll: punch, left/right grab, headbutt (tap / hold-to-charge), kick,
    /// knockout + revival, stun, ring-out detection and the throw flow. 100% physics: no health, no stability meter.
    /// The advanced moves live in RagdollCombat.Moves.cs, their input combinations in MoveCombos.cs.
    /// Phase 3 will call the public input methods (ArmButton, KickPressed, ...) from the Input System.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollRig))]
    [RequireComponent(typeof(ActiveRagdollMotor))]
    public partial class RagdollCombat : MonoBehaviour
    {
        /// <summary>Raised once when a head or torso leaves the arena (or a hazard calls NotifyRingOut).</summary>
        public static event Action<RagdollCombat> RingedOut;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { RingedOut = null; }

        sealed class Strike
        {
            public float T = -1f, Duration, Impulse, Stun;
            public bool Knockout;
            public readonly HashSet<object> Hit = new HashSet<object>();
            public bool Active => T >= 0f;
            public float P => Duration > 0f ? T / Duration : 0f;
            public bool InWindow => Active && P >= 0.25f && P <= 0.85f; // the part of a swing that can hurt
        }

        sealed class Arm
        {
            public bool Left, Held, GrabMode;
            public float HeldTime;
            public FixedJoint Joint;
            public Rigidbody Grabbed;   // null while Joint != null means "anchored to scenery"
            public Rigidbody Hand;
            public readonly Strike Punch = new Strike();
        }

        [Header("Test input (replaced by the Input System in Phase 3)")]
        public bool useBuiltInTestInput = true;

        [Header("Strikes")]
        public float punchImpulse = 22f;
        public float superPunchImpulse = 60f;
        public float kickImpulse = 20f;
        public float headbuttImpulse = 26f;
        public float stunSeconds = 0.8f;
        [Range(0f, 1f)] public float stunStrength = 0.35f;
        [Tooltip("Headbutt/crouch button: shorter than this = headbutt, longer = crouch and charge.")]
        public float tapThreshold = 0.2f;
        public float headbuttChargeTime = 1f;

        [Header("Grab and throw")]
        public float grabHoldDelay = 0.15f;
        public float grabBreakForce = 4000f;
        public float throwSpeed = 9f;
        public float throwUp = 4.5f;
        [Range(0f, 1f)] public float liftAssist = 0.75f;

        [Header("Knockout and revival")]
        [Tooltip("Relative impact speed (m/s) of head/torso/pelvis against scenery that knocks you out.")]
        public float koImpactSpeed = 8.5f;
        public float koDuration = 3f;
        [Range(0f, 1f)] public float wakeStrength = 0.45f;
        public float recoverTime = 1.2f;

        [Header("Ring-out")]
        public float ringOutY = -8f;

        public event Action KnockedOut;
        public event Action Revived;

        public bool IsKnockedOut => _ko;
        public bool IsStunned => Time.time < _stunUntil;
        public bool IsCrouching => _crouch;
        public bool IsLifting => _lifting;
        public bool HasRungOut => _ringedOut;

        RagdollRig _rig;
        ActiveRagdollMotor _motor;
        Rigidbody _pelvis, _torso, _head;
        readonly HashSet<Collider> _own = new HashSet<Collider>();
        readonly Arm _armL = new Arm { Left = true };
        readonly Arm _armR = new Arm();
        readonly Strike _kick = new Strike();
        readonly Strike _headbutt = new Strike();

        bool _ready, _ko, _ringedOut, _crouch, _hbHeld, _hbConsumed, _lifting, _kickLeft;
        float _koUntil, _stunUntil, _invulnUntil, _recoverT, _appliedStrength = 1f, _hbTime, _lastCharge, _baseStand, _nextFx;

        float Charge01 => _hbHeld && _hbTime > tapThreshold
            ? Mathf.Clamp01((_hbTime - tapThreshold) / Mathf.Max(0.1f, headbuttChargeTime))
            : 0f;

        bool HoldingBothArms => _armL.Joint != null && _armR.Joint != null;
        bool HoldingStaticWithBothArms => HoldingBothArms && _armL.Grabbed == null && _armR.Grabbed == null;
        Rigidbody HeldBody => _armL.Grabbed != null ? _armL.Grabbed : _armR.Grabbed;
        bool CanLift => HoldingBothArms && HeldBody != null;

        /// <summary>Enemies share a faction so they never hurt each other. -1 (players) hurts everyone.</summary>
        public int Faction = -1;
        /// <summary>Below 1 while guarding (shield carriers): scales incoming strike force and blocks stuns/knockouts.</summary>
        [HideInInspector] public float DefenseMultiplier = 1f;
        public bool IsAttacking => _kick.Active || _headbutt.Active || _armL.Punch.Active || _armR.Punch.Active;
        public bool HoldingAny => _armL.Joint != null || _armR.Joint != null;
        public bool HoldingBoth => HoldingBothArms;
        public Rigidbody Held => HeldBody;

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            _motor = GetComponent<ActiveRagdollMotor>();
            _motor.AfterPose += OnAfterPose;
            if (useBuiltInTestInput) _motor.testInputJump = false; // combat resolves Space (jump vs. climb vs. backflip)
        }

        void OnDisable()
        {
            if (_motor != null) _motor.AfterPose -= OnAfterPose;
        }

        void Update()
        {
            if (useBuiltInTestInput) ReadTestInput();
        }

        bool EnsureCache()
        {
            if (_ready) return true;
            if (_rig == null) _rig = GetComponent<RagdollRig>();
            if (_motor == null) _motor = GetComponent<ActiveRagdollMotor>();
            if (_rig == null || !_rig.IsBuilt) return false;

            _pelvis = _rig.Body(LimbId.Pelvis);
            _torso = _rig.Body(LimbId.Torso);
            _head = _rig.Body(LimbId.Head);
            _armL.Hand = _rig.Body(LimbId.HandL);
            _armR.Hand = _rig.Body(LimbId.HandR);

            _own.Clear();
            foreach (RagdollLimb l in _rig.Limbs)
            {
                if (l == null) continue;
                foreach (Collider c in l.GetComponents<Collider>()) _own.Add(c);
                LimbImpactRelay relay = l.GetComponent<LimbImpactRelay>();
                if (relay == null) relay = l.gameObject.AddComponent<LimbImpactRelay>();
                relay.Owner = this;
                relay.Limb = l.Id;
            }

            _baseStand = _motor.standHeight;
            _recoverT = recoverTime;
            _invulnUntil = Time.time + 1f; // ignore spawn-time depenetration
            _moves = BuildMoves();
            _ready = true;
            return true;
        }

        /// <summary>Called by the motor right after it set the walking pose, so overrides here win this physics step.</summary>
        void OnAfterPose()
        {
            if (!EnsureCache()) return;
            float dt = Time.fixedDeltaTime;
            TickState(dt);
            TickMove(dt);
            ApplyOverlayPoses();
            CheckRingOut();
        }

        // ---------------------------------------------------------------- input API (Phase 3 wires real controls to these)

        /// <summary>Left/right arm button (LMB/RMB, LB/RB). Tap = punch, hold = reach and grab.</summary>
        public void ArmButton(bool left, bool pressed)
        {
            if (!EnsureCache()) return;
            Arm a = left ? _armL : _armR;

            if (pressed)
            {
                if (_ko) return;
                a.Held = true;
                a.HeldTime = 0f;
                a.GrabMode = false;
                if (MoveActive) return;
                if (MoveCombos.TryResolve(left ? MoveButton.PunchL : MoveButton.PunchR, BuildContext(), out MoveId id)
                    && Perform(id, left)) return;
                FirePunch(left, false);
            }
            else
            {
                a.Held = false;
                a.GrabMode = false;
                if (_lifting) ThrowLifted(); // release while lifting = throw (needs the grabs, so before ReleaseArm)
                ReleaseArm(a);
            }
        }

        public void KickPressed()
        {
            if (!EnsureCache() || _ko) return;
            if (MoveActive) return;
            if (MoveCombos.TryResolve(MoveButton.Kick, BuildContext(), out MoveId id) && Perform(id)) return;
            FireKick();
        }

        /// <summary>Headbutt/crouch button. Tap = headbutt, hold = crouch (and charge), release = charged headbutt.</summary>
        public void HeadbuttButton(bool pressed)
        {
            if (!EnsureCache()) return;
            if (pressed)
            {
                if (_hbHeld || _ko) return;
                _hbHeld = true;
                _hbTime = 0f;
                _hbConsumed = false;
                if (!MoveActive
                    && MoveCombos.TryResolve(MoveButton.Crouch, BuildContext(), out MoveId id)
                    && Perform(id))
                    _hbConsumed = true; // a slide / handstand used this press
                return;
            }

            if (!_hbHeld) return;
            _hbHeld = false;
            float held = _hbTime;
            bool consumed = _hbConsumed;
            _hbConsumed = false;
            if (consumed || _ko || MoveActive) return;

            float charge = held < tapThreshold ? 0f : Mathf.Clamp01((held - tapThreshold) / Mathf.Max(0.1f, headbuttChargeTime));
            _lastCharge = charge;
            ComboContext ctx = BuildContext();
            ctx.HeadbuttCharge = charge;
            if (MoveCombos.TryResolve(MoveButton.Headbutt, ctx, out MoveId move) && Perform(move)) return;
            FireHeadbutt(charge, false);
        }

        public void TauntPressed()
        {
            if (!EnsureCache() || _ko) return;
            if (MoveActive) return;
            if (MoveCombos.TryResolve(MoveButton.Taunt, BuildContext(), out MoveId id)) Perform(id);
        }

        public void JumpPressed()
        {
            if (!EnsureCache())
            {
                if (_motor != null) _motor.RequestJump();
                return;
            }
            if (_ko) return;
            if (_move != null && _move.Infinite && _move.Id != MoveId.Lift) { CancelMove(); return; } // stand up from sit / waddle / handstand
            if (MoveActive) return;
            if (MoveCombos.TryResolve(MoveButton.Jump, BuildContext(), out MoveId id) && Perform(id)) return;
            _motor.RequestJump();
        }

        ComboContext BuildContext()
        {
            Vector3 v = _pelvis.GetVelocity();
            return new ComboContext
            {
                Grounded = _motor.IsGrounded,
                Crouching = _crouch,
                ArmsHoldStatic = HoldingStaticWithBothArms,
                ArmsHoldBody = CanLift,
                ArmsReaching = _armL.GrabMode && _armR.GrabMode && _armL.Joint == null && _armR.Joint == null,
                Speed = new Vector2(v.x, v.z).magnitude,
                HeadbuttCharge = Charge01
            };
        }

        // ---------------------------------------------------------------- per-step state

        void TickState(float dt)
        {
            TickArm(_armL, dt);
            TickArm(_armR, dt);
            TickStrike(_kick, dt);
            TickStrike(_headbutt, dt);

            if (_hbHeld) _hbTime += dt;
            bool wantCrouch = _hbHeld && !_hbConsumed && _hbTime >= tapThreshold && !_ko;
            if (wantCrouch != _crouch) SetCrouch(wantCrouch);

            if (_ko && Time.time >= _koUntil) Revive();
            if (!_ko && _recoverT < recoverTime) _recoverT += dt;

            UpdateMotorStrength();
            UpdateMotorScales();
        }

        void TickArm(Arm a, float dt)
        {
            TickStrike(a.Punch, dt);
            if (!a.Held) return;
            a.HeldTime += dt;
            if (!a.GrabMode && a.HeldTime >= grabHoldDelay && !a.Punch.Active) a.GrabMode = true;
        }

        static void TickStrike(Strike s, float dt)
        {
            if (!s.Active) return;
            s.T += dt;
            if (s.T >= s.Duration) s.T = -1f;
        }

        void UpdateMotorStrength()
        {
            float s = 1f;
            if (_ko) s = 0f;
            else if (_recoverT < recoverTime) s = Mathf.Lerp(wakeStrength, 1f, _recoverT / Mathf.Max(0.01f, recoverTime));
            if (IsStunned) s = Mathf.Min(s, stunStrength);
            if (Mathf.Abs(s - _appliedStrength) > 0.01f)
            {
                _rig.SetMotorStrength(s);
                _appliedStrength = s;
            }
        }

        void UpdateMotorScales()
        {
            if (MoveActive) return; // moves set their own scales
            bool reaching = _armL.GrabMode && _armR.GrabMode && _armL.Joint == null && _armR.Joint == null;
            float speed = 1f;
            if (_crouch) speed *= 0.4f;   // crawl
            if (reaching) speed *= 0.55f; // zombie waddle
            _motor.moveSpeedScale = speed;
        }

        void SetCrouch(bool on)
        {
            _crouch = on;
            _motor.standHeight = _baseStand * (on ? 0.62f : 1f);
        }

        // ---------------------------------------------------------------- strikes

        static void StartStrike(Strike s, float duration, float impulse, float stun, bool knockout)
        {
            s.T = 0f;
            s.Duration = duration;
            s.Impulse = impulse;
            s.Stun = stun;
            s.Knockout = knockout;
            s.Hit.Clear();
        }

        void FirePunch(bool left, bool super)
        {
            if (_ko) return;
            Arm a = left ? _armL : _armR;
            if (a.Joint != null || a.Punch.Active) return; // hands that are holding something cannot punch
            StartStrike(a.Punch, super ? 0.42f : 0.30f, super ? superPunchImpulse : punchImpulse, super ? stunSeconds : 0f, false);
            LungeUpper(_motor.Facing * (super ? 3.5f : 0.8f));
        }

        void FireKick()
        {
            if (_ko || _kick.Active) return;
            _kickLeft = !_kickLeft;
            StartStrike(_kick, 0.34f, kickImpulse, stunSeconds * 0.5f, false);
            LungeUpper(_motor.Facing * 0.6f);
        }

        void FireHeadbutt(float charge, bool knockout)
        {
            if (_ko || _headbutt.Active) return;
            float impulse = Mathf.Lerp(headbuttImpulse, headbuttImpulse * 2.5f, charge);
            StartStrike(_headbutt, 0.45f, impulse, stunSeconds * (1f + charge), knockout);
            LungeUpper(_motor.Facing * Mathf.Lerp(2.5f, 6.5f, charge) + Vector3.up * 0.8f);
        }

        void LungeUpper(Vector3 velocityChange)
        {
            _pelvis.AddForce(velocityChange, ForceMode.VelocityChange);
            _torso.AddForce(velocityChange, ForceMode.VelocityChange);
            _head.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        Strike StrikeFor(LimbId limb)
        {
            switch (limb)
            {
                case LimbId.HandL: return _armL.Punch;
                case LimbId.HandR: return _armR.Punch;
                case LimbId.FootL:
                case LimbId.FootR: return _kick;
                case LimbId.Head: return _headbutt;
                default: return null;
            }
        }

        void ApplyStrikeHit(Strike s, Collision c)
        {
            Rigidbody target = c.rigidbody;
            if (target == null || c.contactCount == 0 || _own.Contains(c.collider)) return;

            RagdollRig targetRig = target.GetComponentInParent<RagdollRig>();
            RagdollCombat other = targetRig != null ? targetRig.GetComponent<RagdollCombat>() : null;
            if (other != null && Faction >= 0 && other.Faction == Faction) return; // no friendly fire between enemies

            object key = targetRig != null ? (object)targetRig : target;
            if (!s.Hit.Add(key)) return; // one hit per strike per target

            float guard = other != null ? other.DefenseMultiplier : 1f;
            Vector3 dir = (_motor.Facing + Vector3.up * 0.2f).normalized;
            target.AddForceAtPosition(dir * (s.Impulse * guard), c.GetContact(0).point, ForceMode.Impulse);
            ImpactEffects.StrikeHit(c.GetContact(0).point, c.GetContact(0).normal, s.Impulse);
            if (targetRig == null) return;

            Rigidbody pelvis = targetRig.Body(LimbId.Pelvis);
            if (pelvis != null && pelvis != target) pelvis.AddForce(dir * (s.Impulse * 0.5f * guard), ForceMode.Impulse);

            if (other == null) return;
            if (s.Knockout && guard > 0.9f) other.KnockOut(koDuration);
            else if (s.Stun > 0f && guard > 0.5f) other.Stun(s.Stun);
        }

        // ---------------------------------------------------------------- collisions

        /// <summary>Called by LimbImpactRelay for every contact on every limb.</summary>
        public void HandleCollision(LimbId limb, Collision c, bool stay)
        {
            if (!_ready || c.contactCount == 0) return;

            // Knockout: a hard hit to the head / torso / pelvis. Hits from other ragdolls need to be harder, because
            // punches and kicks already have their own effects and should not knock people out by themselves.
            if (!stay && !_ko && Time.time >= _invulnUntil
                && (limb == LimbId.Head || limb == LimbId.Torso || limb == LimbId.Pelvis))
            {
                float needed = c.collider.GetComponentInParent<RagdollRig>() != null ? koImpactSpeed * 1.3f : koImpactSpeed;
                if (c.relativeVelocity.magnitude >= needed) KnockOut(koDuration);
            }

            // Impact effects: dust, scuff decal and sparks when a body slams into scenery (props and players excluded).
            if (!stay && c.rigidbody == null && Time.time >= _nextFx)
            {
                float speed = c.relativeVelocity.magnitude;
                if (speed >= 3.5f)
                {
                    _nextFx = Time.time + 0.12f;
                    ContactPoint cp = c.GetContact(0);
                    ImpactEffects.Impact(cp.point, cp.normal, speed);
                }
            }

            Strike s = StrikeFor(limb);
            if (s != null && s.InWindow) ApplyStrikeHit(s, c);

            if (limb == LimbId.HandL) TryGrab(_armL, c);
            else if (limb == LimbId.HandR) TryGrab(_armR, c);
        }

        public void HandleJointBreak(LimbId limb)
        {
            Arm a = limb == LimbId.HandL ? _armL : limb == LimbId.HandR ? _armR : null;
            if (a == null) return;
            a.Joint = null;
            a.Grabbed = null;
            _lifting = false;
        }

        // ---------------------------------------------------------------- grabbing

        void TryGrab(Arm a, Collision c)
        {
            if (!a.GrabMode || a.Joint != null || _ko || a.Hand == null) return;
            if (_own.Contains(c.collider)) return;

            Rigidbody other = c.rigidbody; // null = scenery (ledges, rails, walls)
            ContactPoint cp = c.GetContact(0);

            FixedJoint j = a.Hand.gameObject.AddComponent<FixedJoint>();
            j.connectedBody = other;                       // null anchors the hand to the world
            j.anchor = a.Hand.transform.InverseTransformPoint(cp.point);
            j.breakForce = grabBreakForce;
            j.breakTorque = grabBreakForce;
            j.enableCollision = false;
            a.Joint = j;
            a.Grabbed = other;
        }

        void ReleaseArm(Arm a)
        {
            if (a.Joint != null) Destroy(a.Joint);
            a.Joint = null;
            a.Grabbed = null;
        }

        void ReleaseGrabs()
        {
            ReleaseArm(_armL);
            ReleaseArm(_armR);
            _lifting = false;
        }

        void ThrowLifted()
        {
            if (!_lifting) return;
            _lifting = false;
            Rigidbody body = HeldBody;
            ReleaseArm(_armL);
            ReleaseArm(_armR);
            if (_move != null && _move.Id == MoveId.Lift) FinishMove();
            if (body == null) return;

            Vector3 aim = _motor.MoveDirection.sqrMagnitude > 0.01f ? _motor.MoveDirection.normalized : _motor.Facing;
            ThrowBody(body, aim * throwSpeed + Vector3.up * throwUp);
        }

        void ThrowBody(Rigidbody body, Vector3 velocity)
        {
            RagdollRig rig = body.GetComponentInParent<RagdollRig>();
            if (rig == null)
            {
                body.AddForce(velocity, ForceMode.VelocityChange);
                return;
            }
            foreach (RagdollLimb l in rig.Limbs)
                if (l != null) l.Body.AddForce(velocity, ForceMode.VelocityChange);
            RagdollCombat other = rig.GetComponent<RagdollCombat>();
            if (other != null) other.Stun(stunSeconds);
        }

        // ---------------------------------------------------------------- knockout, stun, ring-out

        public void KnockOut(float seconds)
        {
            if (!_ready) return;
            bool already = _ko;
            _ko = true;
            _koUntil = Mathf.Max(_koUntil, Time.time + seconds);

            ReleaseGrabs();
            _armL.Held = _armR.Held = false;
            _armL.GrabMode = _armR.GrabMode = false;
            _hbHeld = false;
            _hbConsumed = false;
            CancelMove();
            SetCrouch(false);
            _kick.T = _headbutt.T = _armL.Punch.T = _armR.Punch.T = -1f;
            _recoverT = 0f;

            if (!already) KnockedOut?.Invoke();
        }

        void Revive()
        {
            _ko = false;
            _recoverT = 0f; // ramp from wakeStrength back to full strength: awake, but not instantly at 100%
            _invulnUntil = Time.time + 1.2f;
            Revived?.Invoke();
        }

        public void Stun(float seconds)
        {
            _stunUntil = Mathf.Max(_stunUntil, Time.time + seconds);
        }

        void CheckRingOut()
        {
            if (_ringedOut) return;
            if (_head.position.y < ringOutY || _torso.position.y < ringOutY) NotifyRingOut();
        }

        /// <summary>Hazards (grinders, furnace, water) call this when they claim a player.</summary>
        public void NotifyRingOut()
        {
            if (_ringedOut) return;
            _ringedOut = true;
            ReleaseGrabs();
            RingedOut?.Invoke(this);
        }

        /// <summary>Call after teleporting the ragdoll back into the arena.</summary>
        public void ResetAfterRespawn()
        {
            _ringedOut = false;
            _ko = false;
            _recoverT = recoverTime;
            _stunUntil = 0f;
            _invulnUntil = Time.time + 1.5f;
            ReleaseGrabs();
            CancelMove();
        }

        // ---------------------------------------------------------------- poses (strikes, grab reach, crouch)

        static Quaternion Euler(float x, float y = 0f, float z = 0f) => Quaternion.Euler(x, y, z);

        void Pose(LimbId id, Quaternion localToParent)
        {
            RagdollLimb l = _rig.Get(id);
            if (l != null) l.SetTargetLocalRotation(localToParent);
        }

        void ApplyOverlayPoses()
        {
            if (MoveActive) return; // advanced moves author their own pose

            float lean = 0f, twist = 0f;
            bool torsoOverride = false;

            if (_crouch)
            {
                Pose(LimbId.UpperLegL, Euler(-55f, 0f, -5f));
                Pose(LimbId.UpperLegR, Euler(-55f, 0f, 5f));
                Pose(LimbId.LowerLegL, Euler(95f));
                Pose(LimbId.LowerLegR, Euler(95f));
                lean = 25f - Charge01 * 55f; // lean back while charging a headbutt
                torsoOverride = true;
            }

            if (_headbutt.Active)
            {
                float p = _headbutt.P;
                lean = p < 0.3f ? Mathf.Lerp(0f, -25f, p / 0.3f)
                     : p < 0.7f ? 55f
                     : Mathf.Lerp(55f, 0f, (p - 0.7f) / 0.3f);
                Pose(LimbId.Head, Euler(p >= 0.3f && p < 0.7f ? 30f : 0f));
                torsoOverride = true;
            }

            if (_kick.Active)
            {
                float p = _kick.P;
                float pitch = p < 0.25f ? Mathf.Lerp(0f, 35f, p / 0.25f)
                            : p < 0.65f ? -85f
                            : Mathf.Lerp(-85f, 0f, (p - 0.65f) / 0.35f);
                Pose(_kickLeft ? LimbId.UpperLegL : LimbId.UpperLegR, Euler(pitch));
                Pose(_kickLeft ? LimbId.LowerLegL : LimbId.LowerLegR, Euler(p < 0.25f ? 60f : 5f));
                lean -= 10f;
                torsoOverride = true;
            }

            torsoOverride |= OverlayArm(_armL, ref twist);
            torsoOverride |= OverlayArm(_armR, ref twist);

            if (torsoOverride) Pose(LimbId.Torso, Euler(lean, twist));
        }

        bool OverlayArm(Arm a, ref float twist)
        {
            LimbId upper = a.Left ? LimbId.UpperArmL : LimbId.UpperArmR;
            LimbId lower = a.Left ? LimbId.LowerArmL : LimbId.LowerArmR;
            float side = a.Left ? -1f : 1f;

            if (a.Punch.Active)
            {
                float p = a.Punch.P;
                float pitch = p < 0.25f ? Mathf.Lerp(0f, 50f, p / 0.25f)      // wind up (arm back)
                            : p < 0.6f ? -95f                                  // strike (arm forward)
                            : Mathf.Lerp(-95f, 0f, (p - 0.6f) / 0.4f);
                Pose(upper, Euler(pitch, 0f, side * 10f));
                Pose(lower, Euler(p >= 0.25f && p < 0.6f ? -5f : -60f));
                twist += (a.Left ? 1f : -1f) * (p < 0.25f ? -15f : 25f);
                return true;
            }

            if (a.GrabMode)
            {
                Pose(upper, Euler(-88f, 0f, side * 8f)); // reach forward, whether or not something is held yet
                Pose(lower, Euler(-8f));
                return false;
            }
            return false;
        }

        // ---------------------------------------------------------------- built-in test input

        void ReadTestInput()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            Keyboard kb = Keyboard.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame) ArmButton(true, true);
                if (m.leftButton.wasReleasedThisFrame) ArmButton(true, false);
                if (m.rightButton.wasPressedThisFrame) ArmButton(false, true);
                if (m.rightButton.wasReleasedThisFrame) ArmButton(false, false);
            }
            if (kb != null)
            {
                if (kb.mKey.wasPressedThisFrame) KickPressed();
                if (kb.leftCtrlKey.wasPressedThisFrame) HeadbuttButton(true);
                if (kb.leftCtrlKey.wasReleasedThisFrame) HeadbuttButton(false);
                if (kb.leftShiftKey.wasPressedThisFrame) TauntPressed();
                if (kb.spaceKey.wasPressedThisFrame) JumpPressed();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0)) ArmButton(true, true);
            if (Input.GetMouseButtonUp(0)) ArmButton(true, false);
            if (Input.GetMouseButtonDown(1)) ArmButton(false, true);
            if (Input.GetMouseButtonUp(1)) ArmButton(false, false);
            if (Input.GetKeyDown(KeyCode.M)) KickPressed();
            if (Input.GetKeyDown(KeyCode.LeftControl)) HeadbuttButton(true);
            if (Input.GetKeyUp(KeyCode.LeftControl)) HeadbuttButton(false);
            if (Input.GetKeyDown(KeyCode.LeftShift)) TauntPressed();
            if (Input.GetKeyDown(KeyCode.Space)) JumpPressed();
#endif
        }
    }
}
