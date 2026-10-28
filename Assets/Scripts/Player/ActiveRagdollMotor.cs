using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeefCity.Ragdoll
{
    /// <summary>
    /// Makes the ragdoll stand, balance and struggle forward using forces and joint motor targets only
    /// (no Animator). Phase 3 will feed SetMoveInput / RequestJump from the Input System;
    /// until then the built-in WASD + Space test input is on.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollRig))]
    public class ActiveRagdollMotor : MonoBehaviour
    {
        [Header("Test input (replaced by the Input System in Phase 3)")]
        public bool useBuiltInTestInput = true;

        [Header("Locomotion")]
        public float moveSpeed = 3.2f;
        public float acceleration = 10f;
        public float maxAcceleration = 16f;
        [Range(0f, 1f)] public float airControl = 0.15f;
        public float jumpSpeed = 5.5f;
        public float forwardLean = 10f;

        [Header("Balance (angular acceleration, rad/s^2)")]
        public float uprightGain = 70f;
        public float uprightDamping = 10f;
        public float yawGain = 45f;

        [Header("Standing")]
        public float standHeight = ProceduralRagdollBuilder.DefaultStandHeight;
        public float rideSpring = 110f;
        public float rideDamper = 17f;
        public float groundTolerance = 0.22f;
        public LayerMask groundMask = ~0;

        [Header("Procedural gait")]
        public float gaitFrequency = 1.3f;
        public float gaitBlendRate = 6f;
        public float legSwing = 38f;
        public float kneeBend = 55f;
        public float armSwing = 32f;
        public float legSplay = 5f;
        public float armSplay = 12f;
        public float elbowRest = 18f;

        public bool IsGrounded => _grounded;

        // Hooks used by RagdollCombat (strikes, flips, slides, handstands). Safe to ignore otherwise.
        [HideInInspector] public float uprightScale = 1f;      // 0 = let the body tumble
        [HideInInspector] public float uprightSign = 1f;       // -1 = balance upside down
        [HideInInspector] public bool locomotionEnabled = true;
        [HideInInspector] public bool rideHeightEnabled = true;
        [HideInInspector] public float moveSpeedScale = 1f;
        [HideInInspector] public bool testInputJump = true;    // combat takes over Space when it is present
        /// <summary>Invoked at the end of every motor FixedUpdate, after the walking pose is set. Override limb targets here.</summary>
        public System.Action AfterPose;
        public Vector3 Facing => _facing;
        public Vector3 MoveDirection => _moveDir;
        public float GroundDistance => _groundDistance;

        // AI control: world-space movement that ignores the camera. Call every frame while an AI drives this body.
        bool _worldActive, _worldHasFace;
        Vector3 _worldMove, _worldFace;

        public void SetMoveWorld(Vector3 direction, Vector3? face = null)
        {
            _worldActive = true;
            _worldMove = Vector3.ClampMagnitude(new Vector3(direction.x, 0f, direction.z), 1f);
            _worldHasFace = false;
            if (face.HasValue)
            {
                Vector3 f = new Vector3(face.Value.x, 0f, face.Value.z);
                if (f.sqrMagnitude > 0.0001f)
                {
                    _worldFace = f.normalized;
                    _worldHasFace = true;
                }
            }
        }

        RagdollRig _rig;
        Rigidbody _pelvis, _torso, _head, _footL, _footR;
        readonly HashSet<Collider> _own = new HashSet<Collider>();
        readonly RaycastHit[] _hits = new RaycastHit[16];

        bool _ready, _grounded, _jumpQueued;
        float _groundDistance, _jumpLockUntil, _phase, _gaitBlend, _air, _speed01;
        Vector2 _moveInput;
        Vector3 _moveDir, _facing = Vector3.forward;

        // ------------------------------------------------------------------ public API

        public void SetMoveInput(Vector2 input) => _moveInput = Vector2.ClampMagnitude(input, 1f);
        public void RequestJump() => _jumpQueued = true;

        /// <summary>Call after the rig has been rebuilt so cached references are refreshed.</summary>
        public void Rebind() => _ready = false;

        // ------------------------------------------------------------------ loop

        void Update()
        {
            if (useBuiltInTestInput) ReadTestInput();
        }

        void FixedUpdate()
        {
            if (!_ready)
            {
                _ready = TryInitialize();
                if (!_ready) return;
            }

            float dt = Time.fixedDeltaTime;
            float strength = _rig.MotorStrength;

            ComputeMoveDirection();
            UpdateGround();

            if (_jumpQueued)
            {
                _jumpQueued = false;
                if (_grounded && strength > 0.5f) DoJump();
            }

            ApplyUpright(strength * uprightScale);
            if (rideHeightEnabled) ApplyRideHeight(strength);
            if (locomotionEnabled) ApplyLocomotion(strength);
            UpdateGait(dt);
            ApplyPose(dt);
            AfterPose?.Invoke();
        }

        bool TryInitialize()
        {
            if (_rig == null) _rig = GetComponent<RagdollRig>();
            if (_rig == null || !_rig.IsBuilt) return false;

            _pelvis = _rig.Body(LimbId.Pelvis);
            _torso = _rig.Body(LimbId.Torso);
            _head = _rig.Body(LimbId.Head);
            _footL = _rig.Body(LimbId.FootL);
            _footR = _rig.Body(LimbId.FootR);

            _own.Clear();
            foreach (RagdollLimb l in _rig.Limbs)
                if (l != null)
                    foreach (Collider c in l.GetComponents<Collider>()) _own.Add(c);

            Vector3 fwd = Vector3.ProjectOnPlane(_pelvis.transform.forward, Vector3.up);
            _facing = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
            return true;
        }

        // ------------------------------------------------------------------ input

        void ReadTestInput()
        {
            Vector2 m = Vector2.zero;
            bool jump = false;
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                m.x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                m.y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                jump = testInputJump && kb.spaceKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            m.x = Input.GetAxisRaw("Horizontal");
            m.y = Input.GetAxisRaw("Vertical");
            jump = testInputJump && Input.GetKeyDown(KeyCode.Space);
#endif
            _moveInput = Vector2.ClampMagnitude(m, 1f);
            if (jump) _jumpQueued = true;
        }

        void ComputeMoveDirection()
        {
            if (_worldActive)
            {
                _moveDir = _worldMove;
                if (_worldHasFace) _facing = _worldFace;
                else if (_moveDir.sqrMagnitude > 0.01f) _facing = _moveDir.normalized;
                return;
            }

            Camera cam = Camera.main;
            Vector3 f = Vector3.forward, r = Vector3.right;
            if (cam != null)
            {
                f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                r = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            }
            _moveDir = f * _moveInput.y + r * _moveInput.x;
            if (_moveDir.sqrMagnitude > 1f) _moveDir.Normalize();
            if (_moveDir.sqrMagnitude > 0.01f) _facing = _moveDir.normalized;
        }

        // ------------------------------------------------------------------ ground + balance

        void UpdateGround()
        {
            _groundDistance = float.MaxValue;
            float reach = standHeight + groundTolerance;
            int n = Physics.RaycastNonAlloc(_pelvis.position, Vector3.down, _hits, reach, groundMask,
                                            QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (_own.Contains(h.collider)) continue; // the ray starts inside our own body
                if (h.distance < _groundDistance) _groundDistance = h.distance;
            }
            _grounded = _groundDistance <= reach && Time.time >= _jumpLockUntil;
        }

        void ApplyUpright(float s)
        {
            if (s <= 0.01f) return;
            Upright(_pelvis, s);
            Upright(_torso, 0.7f * s);
            Upright(_head, 0.35f * s);
            Yaw(_pelvis, s);
            Yaw(_torso, 0.6f * s);
        }

        void Upright(Rigidbody rb, float k)
        {
            Vector3 axis = Vector3.Cross(rb.transform.up, Vector3.up * uprightSign);
            Vector3 torque = axis * (uprightGain * k) - rb.angularVelocity * (uprightDamping * k);
            rb.AddTorque(torque, ForceMode.Acceleration);
        }

        void Yaw(Rigidbody rb, float k)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(rb.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) return;
            float err = Vector3.SignedAngle(fwd, _facing, Vector3.up) * Mathf.Deg2Rad;
            rb.AddTorque(Vector3.up * (yawGain * k * err), ForceMode.Acceleration);
        }

        void ApplyRideHeight(float s)
        {
            if (!_grounded || s <= 0.01f) return;
            float compression = standHeight - _groundDistance;
            float vy = _pelvis.GetVelocity().y;
            float acc = rideSpring * compression - rideDamper * vy;
            if (acc <= 0f) return; // only ever push up, never pull the body into the floor

            Vector3 force = Vector3.up * (acc * _rig.TotalMass * s);
            _pelvis.AddForce(force, ForceMode.Force);
            // Equal and opposite on the feet so the character does not launch itself.
            _footL.AddForce(-force * 0.5f, ForceMode.Force);
            _footR.AddForce(-force * 0.5f, ForceMode.Force);
        }

        void ApplyLocomotion(float s)
        {
            if (s <= 0.01f) return;
            Vector3 v = _pelvis.GetVelocity();
            Vector3 horizontal = new Vector3(v.x, 0f, v.z);
            Vector3 desired = _moveDir * (moveSpeed * moveSpeedScale);
            Vector3 acc = Vector3.ClampMagnitude((desired - horizontal) * acceleration, maxAcceleration);
            float control = (_grounded ? 1f : airControl) * s;
            _pelvis.AddForce(acc * (_rig.TotalMass * control), ForceMode.Force);
        }

        void DoJump()
        {
            _jumpLockUntil = Time.time + 0.25f;
            foreach (RagdollLimb l in _rig.Limbs)
                if (l != null) l.Body.AddForce(Vector3.up * jumpSpeed, ForceMode.VelocityChange);
        }

        // ------------------------------------------------------------------ procedural gait

        void UpdateGait(float dt)
        {
            Vector3 v = _pelvis.GetVelocity();
            float speed = new Vector2(v.x, v.z).magnitude;
            bool walking = _moveDir.sqrMagnitude > 0.01f && _grounded;

            _gaitBlend = Mathf.MoveTowards(_gaitBlend, walking ? 1f : 0f, dt * gaitBlendRate);
            _air = Mathf.MoveTowards(_air, _grounded ? 0f : 1f, dt * 5f);
            _speed01 = Mathf.Clamp01(speed / Mathf.Max(0.1f, moveSpeed));

            if (walking)
                _phase += dt * Mathf.PI * 2f * gaitFrequency * Mathf.Clamp(speed / Mathf.Max(0.1f, moveSpeed), 0.35f, 1.25f);
        }

        void ApplyPose(float dt)
        {
            float amp = _gaitBlend;
            float swingR = Mathf.Sin(_phase);
            float swingL = Mathf.Sin(_phase + Mathf.PI);
            float liftR = Mathf.Max(0f, Mathf.Cos(_phase));
            float liftL = Mathf.Max(0f, Mathf.Cos(_phase + Mathf.PI));
            float dangle = 25f * _air;

            // Legs: forward swing = negative pitch, knee flexes while the leg swings forward.
            Pose(LimbId.UpperLegR, Quaternion.Euler(-swingR * legSwing * amp, 0f, legSplay));
            Pose(LimbId.UpperLegL, Quaternion.Euler(-swingL * legSwing * amp, 0f, -legSplay));
            Pose(LimbId.LowerLegR, Quaternion.Euler(liftR * kneeBend * amp + dangle, 0f, 0f));
            Pose(LimbId.LowerLegL, Quaternion.Euler(liftL * kneeBend * amp + dangle, 0f, 0f));

            // Arms swing opposite to the legs; in the air they flail up and forward.
            float armPitchR = Mathf.Lerp(-swingL * armSwing * amp, -100f, _air);
            float armPitchL = Mathf.Lerp(-swingR * armSwing * amp, -100f, _air);
            float armRoll = Mathf.Lerp(armSplay, 35f, _air);
            Pose(LimbId.UpperArmR, Quaternion.Euler(armPitchR, 0f, armRoll));
            Pose(LimbId.UpperArmL, Quaternion.Euler(armPitchL, 0f, -armRoll));
            float elbow = -(elbowRest + 20f * _air);
            Pose(LimbId.LowerArmR, Quaternion.Euler(elbow, 0f, 0f));
            Pose(LimbId.LowerArmL, Quaternion.Euler(elbow, 0f, 0f));

            // Lean into the direction of travel.
            Pose(LimbId.Torso, Quaternion.Euler(forwardLean * _speed01 * amp, 0f, 0f));

            // Everything else stays at its rest pose.
            Pose(LimbId.Head, Quaternion.identity);
            Pose(LimbId.HandL, Quaternion.identity);
            Pose(LimbId.HandR, Quaternion.identity);
            Pose(LimbId.FootL, Quaternion.identity);
            Pose(LimbId.FootR, Quaternion.identity);
        }

        void Pose(LimbId id, Quaternion localToParent)
        {
            RagdollLimb l = _rig.Get(id);
            if (l != null) l.SetTargetLocalRotation(localToParent);
        }
    }
}
