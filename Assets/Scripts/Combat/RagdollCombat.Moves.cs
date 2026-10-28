using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeefCity.Ragdoll
{
    /// <summary>
    /// Advanced moves. Each one is a small script of impulses, spins around the center of mass and joint-target poses
    /// (no animation clips). Trigger them through the input methods + MoveCombos, or call Perform(MoveId) directly.
    /// Numbers are first-pass; the flips, climbs and handstand in particular need tuning against your stage geometry.
    /// </summary>
    public partial class RagdollCombat
    {
        sealed class Move
        {
            public MoveId Id;
            public float Duration;
            public bool Infinite;                 // runs until cancelled (sit, zombie waddle, lift)
            public bool RequireGrounded, RequireAir;
            public float Upright = 1f, UprightSign = 1f, SpeedScale = 1f;
            public bool Locomotion = true, RideHeight = true;
            public Func<float, float> UprightAt;  // optional: upright strength over normalized time
            public Action OnStart, OnEnd;
            public Action<float, float> OnUpdate; // (normalized time, or seconds for Infinite moves; dt)
        }

        Dictionary<MoveId, Move> _moves;
        Move _move;
        float _moveT;

        public bool MoveActive => _move != null;
        public MoveId CurrentMove => _move != null ? _move.Id : MoveId.None;

        // ---------------------------------------------------------------- running moves

        /// <summary>Starts a move if the situation allows it. Infinite moves toggle off when performed again.</summary>
        public bool Perform(MoveId id, bool leftSide = false)
        {
            if (!EnsureCache() || _ko || id == MoveId.None) return false;

            switch (id) // moves that reuse the basic strikes / throw
            {
                case MoveId.ChargedHeadbutt: FireHeadbutt(Mathf.Clamp(_lastCharge, 0.5f, 0.99f), false); return true;
                case MoveId.KnockoutHeadbutt: FireHeadbutt(1f, true); return true;
                case MoveId.SuperPunch: FirePunch(leftSide, true); return true;
                case MoveId.ThrowFoe:
                    if (!_lifting) return false;
                    ThrowLifted();
                    return true;
            }

            if (!_moves.TryGetValue(id, out Move m)) return false;
            if (_move != null)
            {
                if (!_move.Infinite) return false;
                bool same = _move.Id == id;
                CancelMove();
                if (same) return true; // toggled off
            }

            if (m.RequireGrounded && !_motor.IsGrounded) return false;
            if (m.RequireAir && _motor.IsGrounded) return false;
            if (id == MoveId.Lift && !CanLift) return false;
            if ((id == MoveId.LeapUpClimb || id == MoveId.SwingUpClimb) && !HoldingStaticWithBothArms) return false;

            _move = m;
            _moveT = 0f;
            _motor.uprightScale = m.Upright;
            _motor.uprightSign = m.UprightSign;
            _motor.locomotionEnabled = m.Locomotion;
            _motor.rideHeightEnabled = m.RideHeight;
            _motor.moveSpeedScale = m.SpeedScale;
            m.OnStart?.Invoke();
            return true;
        }

        void TickMove(float dt)
        {
            if (_move == null) return;
            _moveT += dt;
            float t = _move.Infinite ? _moveT : Mathf.Clamp01(_moveT / _move.Duration);
            if (_move.UprightAt != null) _motor.uprightScale = _move.UprightAt(t);
            _move.OnUpdate?.Invoke(t, dt);
            if (_move != null && !_move.Infinite && _moveT >= _move.Duration) FinishMove();
        }

        void FinishMove()
        {
            Move m = _move;
            if (m == null) return;
            _move = null;
            m.OnEnd?.Invoke();
            _motor.uprightScale = 1f;
            _motor.uprightSign = 1f;
            _motor.locomotionEnabled = true;
            _motor.rideHeightEnabled = true;
            _motor.moveSpeedScale = 1f;
        }

        public void CancelMove() => FinishMove();

        // ---------------------------------------------------------------- helpers

        Vector3 RightAxis => Vector3.Cross(Vector3.up, _motor.Facing).normalized;

        /// <summary>Adds the same velocity change to every limb (a clean "jump").</summary>
        void LaunchAll(Vector3 velocityChange)
        {
            foreach (RagdollLimb l in _rig.Limbs)
                if (l != null) l.Body.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        /// <summary>Spins the whole body around its center of mass (flips). Positive right-axis = forward flip.</summary>
        void SpinAroundCom(Vector3 angularVelocity)
        {
            Vector3 com = _rig.CenterOfMass;
            foreach (RagdollLimb l in _rig.Limbs)
            {
                if (l == null) continue;
                Rigidbody rb = l.Body;
                rb.angularVelocity += angularVelocity;
                rb.AddForce(Vector3.Cross(angularVelocity, rb.worldCenterOfMass - com), ForceMode.VelocityChange);
            }
        }

        void PoseBoth(LimbId left, LimbId right, float pitch, float roll = 0f)
        {
            Pose(left, Euler(pitch, 0f, -roll)); // roll is mirrored so positive = outward on both sides
            Pose(right, Euler(pitch, 0f, roll));
        }

        void ArmsPose(float pitch, float roll, float elbow)
        {
            PoseBoth(LimbId.UpperArmL, LimbId.UpperArmR, pitch, roll);
            PoseBoth(LimbId.LowerArmL, LimbId.LowerArmR, elbow);
        }

        void LegsPose(float upper, float lower, float splay = 0f)
        {
            PoseBoth(LimbId.UpperLegL, LimbId.UpperLegR, upper, splay);
            PoseBoth(LimbId.LowerLegL, LimbId.LowerLegR, lower);
        }

        void Tuck(float k)
        {
            LegsPose(-85f * k, 115f * k);
            ArmsPose(-50f * k, 20f, -60f * k);
            Pose(LimbId.Torso, Euler(30f * k));
        }

        void SlidePose()
        {
            LegsPose(-85f, 8f);
            ArmsPose(25f, 25f, -20f);
            Pose(LimbId.Torso, Euler(-35f));
            Pose(LimbId.Head, Euler(-20f));
        }

        void AssistLift(Rigidbody body)
        {
            RagdollRig rig = body.GetComponentInParent<RagdollRig>();
            if (rig == null)
            {
                body.AddForce(-Physics.gravity * (body.mass * liftAssist), ForceMode.Force);
                return;
            }
            foreach (RagdollLimb l in rig.Limbs)
                if (l != null) l.Body.AddForce(-Physics.gravity * (l.Body.mass * liftAssist), ForceMode.Force);
        }

        void Shockwave(float radius, float impulse, float stun)
        {
            Vector3 center = _pelvis.position;
            Collider[] cols = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore);
            var stunned = new HashSet<RagdollCombat>();
            foreach (Collider col in cols)
            {
                if (_own.Contains(col)) continue;
                Rigidbody rb = col.attachedRigidbody;
                if (rb == null) continue;
                rb.AddExplosionForce(impulse, center, radius, 0.4f, ForceMode.Impulse);
                RagdollCombat other = rb.GetComponentInParent<RagdollCombat>();
                if (other != null && stunned.Add(other)) other.Stun(stun);
            }
        }

        // ---------------------------------------------------------------- move factories

        Move SlideMove(MoveId id, float speed, float duration, float hitImpulse, bool steer)
        {
            return new Move
            {
                Id = id, Duration = duration, RequireGrounded = true,
                Upright = 0.25f, Locomotion = false, RideHeight = false,
                UprightAt = t => Mathf.Lerp(0.25f, 1f, Mathf.InverseLerp(0.7f, 1f, t)),
                OnStart = () =>
                {
                    LaunchAll(_motor.Facing * speed);
                    if (hitImpulse > 0f) StartStrike(_kick, duration, hitImpulse, stunSeconds, false); // feet are the weapon
                },
                OnUpdate = (t, dt) =>
                {
                    SlidePose();
                    if (steer) _pelvis.AddForce(_motor.Facing * (_rig.TotalMass * 6f), ForceMode.Force);
                }
            };
        }

        Move DropKick(MoveId id, float up, float forward, float impulse, float duration, bool needGround)
        {
            return new Move
            {
                Id = id, Duration = duration, RequireGrounded = needGround,
                Upright = 0.25f, Locomotion = false, RideHeight = false,
                UprightAt = t => Mathf.Lerp(0.25f, 1f, Mathf.InverseLerp(0.75f, 1f, t)),
                OnStart = () =>
                {
                    LaunchAll(Vector3.up * up + _motor.Facing * forward);
                    StartStrike(_kick, duration * 0.9f, impulse, stunSeconds * 1.2f, false);
                },
                OnUpdate = (t, dt) =>
                {
                    LegsPose(-88f, 0f);
                    ArmsPose(30f, 20f, -20f);
                    Pose(LimbId.Torso, Euler(-30f));
                }
            };
        }

        Move ClimbMove(MoveId id, bool leap)
        {
            bool released = false;
            return new Move
            {
                Id = id, Duration = leap ? 0.95f : 1.2f,
                Upright = 0.6f, Locomotion = false, RideHeight = false,
                OnStart = () =>
                {
                    released = false;
                    if (leap) LaunchAll(Vector3.up * 3f);
                },
                OnUpdate = (t, dt) =>
                {
                    if (!released)
                    {
                        if (!HoldingStaticWithBothArms) { FinishMove(); return; }

                        float scramble = Mathf.Sin(_moveT * 18f);
                        ArmsPose(70f, 10f, -125f);                       // elbows bend, pulling the body up
                        Pose(LimbId.UpperLegL, Euler(-50f + scramble * 30f));
                        Pose(LimbId.UpperLegR, Euler(-50f - scramble * 30f));
                        PoseBoth(LimbId.LowerLegL, LimbId.LowerLegR, 80f);

                        float up = leap ? 21f : (t < 0.35f ? 0f : 24f);   // swing-up swings first, then pulls
                        float fwd = leap ? 2f : (t < 0.35f ? 10f : 6f);
                        _pelvis.AddForce((Vector3.up * up + _motor.Facing * fwd) * _rig.TotalMass, ForceMode.Force);

                        if (t > 0.55f)
                        {
                            released = true;
                            ReleaseArm(_armL);
                            ReleaseArm(_armR);
                            LaunchAll(_motor.Facing * 2.2f + Vector3.up * 1.5f);
                        }
                    }
                }
            };
        }

        Dictionary<MoveId, Move> BuildMoves()
        {
            var d = new Dictionary<MoveId, Move>();
            float fullTurn = Mathf.PI * 2f;

            d[MoveId.Backflip] = new Move
            {
                Id = MoveId.Backflip, Duration = 1f, RequireGrounded = true,
                Upright = 0f, Locomotion = false, RideHeight = false,
                UprightAt = t => t < 0.75f ? 0f : Mathf.InverseLerp(0.75f, 1f, t),
                OnStart = () =>
                {
                    LaunchAll(Vector3.up * 6f - _motor.Facing * 1.5f);
                    SpinAroundCom(-RightAxis * (fullTurn / 0.7f)); // negative = backward
                },
                OnUpdate = (t, dt) => Tuck(Mathf.Sin(Mathf.Clamp01(t / 0.7f) * Mathf.PI))
            };

            d[MoveId.FlipKick] = new Move
            {
                Id = MoveId.FlipKick, Duration = 1f,
                Upright = 0f, Locomotion = false, RideHeight = false,
                UprightAt = t => t < 0.8f ? 0f : Mathf.InverseLerp(0.8f, 1f, t),
                OnStart = () =>
                {
                    LaunchAll(Vector3.up * (_motor.IsGrounded ? 5.5f : 2.5f) + _motor.Facing * 3f);
                    SpinAroundCom(RightAxis * (fullTurn / 0.8f)); // forward flip
                    StartStrike(_kick, 0.7f, 30f, stunSeconds, false);
                },
                OnUpdate = (t, dt) =>
                {
                    float p = Mathf.Clamp01(t / 0.8f);
                    if (p < 0.45f) Tuck(Mathf.Sin(p / 0.45f * Mathf.PI * 0.5f));
                    else LegsPose(-70f, 0f); // legs shoot out for the kick
                }
            };

            bool slammed = false;
            d[MoveId.BodySlam] = new Move
            {
                Id = MoveId.BodySlam, Duration = 1.6f,
                Upright = 0f, Locomotion = false, RideHeight = false,
                OnStart = () =>
                {
                    slammed = false;
                    if (_motor.IsGrounded) LaunchAll(Vector3.up * 4.5f);
                },
                OnUpdate = (t, dt) =>
                {
                    if (!slammed && _moveT > 0.3f)
                    {
                        slammed = true;
                        LaunchAll(Vector3.down * 7f + _motor.Facing * 2f);
                        SpinAroundCom(RightAxis * 4f); // belly-flop forward
                    }
                    if (!slammed) ArmsPose(-170f, 10f, -10f);
                    else
                    {
                        ArmsPose(-60f, 70f, -5f);
                        LegsPose(0f, 0f, 15f);
                    }
                    if (slammed && _moveT > 0.45f && _motor.IsGrounded)
                    {
                        Shockwave(2.6f, 18f, stunSeconds);
                        FinishMove();
                    }
                }
            };

            d[MoveId.DropSlide] = SlideMove(MoveId.DropSlide, 4.5f, 0.9f, 0f, false);
            d[MoveId.Powerslide] = SlideMove(MoveId.Powerslide, 6.5f, 1.3f, 0f, true);
            d[MoveId.SlideTackle] = SlideMove(MoveId.SlideTackle, 6f, 0.8f, 30f, false);

            d[MoveId.FlyingDropKick] = DropKick(MoveId.FlyingDropKick, 3.8f, 5.2f, 26f, 0.9f, false);
            d[MoveId.StandingDropKick] = DropKick(MoveId.StandingDropKick, 3.2f, 2.6f, 24f, 0.9f, true);
            d[MoveId.SuperDropKick] = DropKick(MoveId.SuperDropKick, 4.5f, 8f, 48f, 1f, false);

            d[MoveId.LeapUpClimb] = ClimbMove(MoveId.LeapUpClimb, true);
            d[MoveId.SwingUpClimb] = ClimbMove(MoveId.SwingUpClimb, false);

            // Experimental: half-flip onto the hands, then balance upside down. Expect to tune the arm drive strength.
            d[MoveId.Handstand] = new Move
            {
                Id = MoveId.Handstand, Duration = 3.2f, RequireGrounded = true,
                Upright = 0f, Locomotion = false, RideHeight = false,
                UprightAt = t => t < 0.2f ? 0f : (t < 0.85f ? 1f : Mathf.Lerp(1f, 0f, Mathf.InverseLerp(0.85f, 1f, t))),
                OnStart = () =>
                {
                    LaunchAll(Vector3.up * 2.5f);
                    SpinAroundCom(RightAxis * (Mathf.PI / 0.5f)); // half a forward flip
                },
                OnUpdate = (t, dt) =>
                {
                    _motor.uprightSign = (_moveT > 0.6f && _moveT < 2.7f) ? -1f : 1f;
                    ArmsPose(-175f, 10f, -3f);
                    LegsPose(0f, 0f, 8f);
                }
            };

            d[MoveId.Sit] = new Move
            {
                Id = MoveId.Sit, Infinite = true, RequireGrounded = true,
                Upright = 0.4f, Locomotion = false, RideHeight = false,
                OnUpdate = (t, dt) =>
                {
                    LegsPose(-90f, 0f);
                    ArmsPose(-10f, 25f, -10f);
                    Pose(LimbId.Torso, Euler(5f));
                }
            };

            d[MoveId.Taunt] = new Move
            {
                Id = MoveId.Taunt, Duration = 1.5f, Locomotion = false,
                OnStart = () => LaunchAll(Vector3.up * 2.2f),
                OnUpdate = (t, dt) =>
                {
                    float wave = Mathf.Sin(_moveT * 14f) * 25f;
                    PoseBoth(LimbId.UpperArmL, LimbId.UpperArmR, -150f, 40f + wave);
                    PoseBoth(LimbId.LowerArmL, LimbId.LowerArmR, -30f);
                }
            };

            d[MoveId.ZombieWaddle] = new Move
            {
                Id = MoveId.ZombieWaddle, Infinite = true, SpeedScale = 0.5f,
                OnUpdate = (t, dt) =>
                {
                    ArmsPose(-90f, 5f, -5f);
                    Pose(LimbId.Torso, Euler(12f));
                }
            };

            // Lift is the first half of "Throwing Foes": hold both grabs on a body, press taunt, aim with the move
            // stick, then release a grab button to throw (see ThrowLifted).
            d[MoveId.Lift] = new Move
            {
                Id = MoveId.Lift, Infinite = true, SpeedScale = 0.6f,
                OnStart = () => _lifting = true,
                OnEnd = () => _lifting = false,
                OnUpdate = (t, dt) =>
                {
                    Rigidbody body = HeldBody;
                    if (body == null || !HoldingBothArms) { FinishMove(); return; }
                    AssistLift(body);
                    ArmsPose(-165f, 15f, -20f);
                    Pose(LimbId.Torso, Euler(-8f));
                }
            };

            return d;
        }
    }
}
