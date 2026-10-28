using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BeefCity.Core;
using BeefCity.Graphics;
using BeefCity.Ragdoll;

#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
#else
using PhysMat = UnityEngine.PhysicMaterial;
#endif

namespace BeefCity.Stages
{
    // These components are added to stage objects at runtime by StageKit / the stage builders.
    // Movers use kinematic rigidbodies so players are carried by friction and pushed by collisions.

    /// <summary>Global hazard speed multiplier. The Waves director raises it as waves progress (1 = normal).</summary>
    public static class HazardScale
    {
        public static float Value = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Value = 1f; }
    }

    /// <summary>Trigger volume that eliminates players (grinders, furnace, water, fans, trains) and clears away props.</summary>
    public class KillZone : MonoBehaviour
    {
        public bool AnyLimb;            // false: only head / torso / pelvis count
        public bool DestroyProps = true;

        void OnTriggerEnter(Collider other) { Check(other); }
        void OnTriggerStay(Collider other) { Check(other); }

        void Check(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb == null) return;

            RagdollLimb limb = rb.GetComponent<RagdollLimb>();
            if (limb != null)
            {
                if (!AnyLimb && limb.Id != LimbId.Head && limb.Id != LimbId.Torso && limb.Id != LimbId.Pelvis) return;
                RagdollCombat combat = limb.Rig != null ? limb.Rig.GetComponent<RagdollCombat>() : null;
                if (combat != null) combat.NotifyRingOut();
                return;
            }
            if (DestroyProps && !rb.isKinematic && rb.GetComponentInParent<RagdollRig>() == null)
                Destroy(rb.gameObject, 0.3f);
        }
    }

    /// <summary>Back-and-forth movement with optional dwell time and a gentle rocking rotation.</summary>
    public class MovingPlatform : MonoBehaviour
    {
        public Vector3 Travel;          // world offset of the far end
        public float Period = 6f;       // seconds for one trip
        public float Pause = 0.5f;      // dwell at each end
        public float StartOffset;       // seconds into the cycle at spawn
        public Vector3 RockAxis = Vector3.forward;
        public float RockDegrees;
        public float RockPeriod = 5f;

        Rigidbody _rb;
        Vector3 _startPos;
        Quaternion _startRot;
        float _t;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _startPos = transform.position;
            _startRot = transform.rotation;
            _t = StartOffset;
        }

        void FixedUpdate()
        {
            if (_rb == null) return;
            _t += Time.fixedDeltaTime * HazardScale.Value;
            float x = Mathf.PingPong(_t, Period + Pause * 2f);
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - Pause) / Mathf.Max(0.01f, Period)));
            _rb.MovePosition(_startPos + Travel * e);
            if (RockDegrees != 0f)
                _rb.MoveRotation(_startRot * Quaternion.AngleAxis(RockDegrees * Mathf.Sin(_t * Mathf.PI * 2f / RockPeriod), RockAxis));
        }
    }

    /// <summary>Constant rotation. Uses the rigidbody if there is one (so it carries players), else the transform.</summary>
    public class Rotator : MonoBehaviour
    {
        public Vector3 Axis = Vector3.up;
        public float DegreesPerSecond = 30f;
        Rigidbody _rb;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        void FixedUpdate()
        {
            if (_rb != null) _rb.MoveRotation(_rb.rotation * Quaternion.AngleAxis(DegreesPerSecond * HazardScale.Value * Time.fixedDeltaTime, Axis));
        }

        void Update()
        {
            if (_rb == null) transform.Rotate(Axis, DegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }

    /// <summary>Moves a kinematic body around a circle (Ferris wheel gondola hangers).</summary>
    public class OrbitMover : MonoBehaviour
    {
        public Vector3 Center;
        public float Radius = 9f;
        public float DegreesPerSecond = 12f;
        public float StartAngle;
        public Vector3 PlaneRight = Vector3.right;
        public Vector3 PlaneUp = Vector3.up;

        Rigidbody _rb;
        float _angle;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _angle = StartAngle;
        }

        void FixedUpdate()
        {
            _angle += DegreesPerSecond * Time.fixedDeltaTime;
            float r = _angle * Mathf.Deg2Rad;
            _rb.MovePosition(Center + (PlaneRight * Mathf.Cos(r) + PlaneUp * Mathf.Sin(r)) * Radius);
        }
    }

    /// <summary>Endless looping movement (road markings, overhead signs on the Trucks stage).</summary>
    public class Scroller : MonoBehaviour
    {
        public Vector3 Velocity = new Vector3(0f, 0f, -12f);
        public float HalfLength = 35f;

        Rigidbody _rb;
        Vector3 _start;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            _start = transform.position;
        }

        void FixedUpdate()
        {
            Vector3 dir = Velocity.normalized;
            Vector3 p = (_rb != null ? _rb.position : transform.position) + Velocity * Time.fixedDeltaTime;
            float offset = Vector3.Dot(p - _start, dir);
            bool wrapped = false;
            if (offset > HalfLength) { p -= dir * (2f * HalfLength); wrapped = true; }
            else if (offset < -HalfLength) { p += dir * (2f * HalfLength); wrapped = true; }

            if (_rb == null) transform.position = p;
            else if (wrapped) { _rb.position = p; transform.position = p; }
            else _rb.MovePosition(p);
        }
    }

    /// <summary>Swinging arm / wrecking ball: rotates a kinematic body back and forth around its own pivot.</summary>
    public class Pendulum : MonoBehaviour
    {
        public Vector3 Axis = Vector3.forward;
        public float Degrees = 55f;
        public float Period = 4f;

        Rigidbody _rb;
        Quaternion _startRot;
        float _t;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _startRot = transform.rotation;
        }

        void FixedUpdate()
        {
            _t += Time.fixedDeltaTime * HazardScale.Value;
            _rb.MoveRotation(_startRot * Quaternion.AngleAxis(Degrees * Mathf.Sin(_t * Mathf.PI * 2f / Period), Axis));
        }
    }

    /// <summary>Makes a dynamic body (a hanging container) tilt back and forth around a fixed world axis.</summary>
    public class Rocker : MonoBehaviour
    {
        public Vector3 Axis = Vector3.right;
        public float Degrees = 25f;
        public float Period = 8f;
        public float Stiffness = 6f;
        public float Damping = 2f;

        Rigidbody _rb;
        float _t;

        void Awake() { _rb = GetComponent<Rigidbody>(); }

        void FixedUpdate()
        {
            if (_rb == null) return;
            _t += Time.fixedDeltaTime;
            float target = Degrees * Mathf.Sin(_t * Mathf.PI * 2f / Period) * Mathf.Deg2Rad;
            float angle = Vector3.SignedAngle(Vector3.up, transform.up, Axis) * Mathf.Deg2Rad;
            float w = Vector3.Dot(_rb.angularVelocity, Axis);
            _rb.AddTorque(Axis * (Stiffness * (target - angle) - Damping * w), ForceMode.Acceleration);
        }
    }

    /// <summary>
    /// Anything players can wear down by hitting, hanging on or standing on: cables, loose boards, bridges.
    /// When it breaks it removes its joints (cables let go) and/or becomes a falling rigidbody (boards drop).
    /// </summary>
    public class Breakable : MonoBehaviour
    {
        public float Health = 8f;
        public bool FallWhenBroken;
        public float FallMass = 12f;
        public Joint[] Joints = new Joint[0];
        public GameObject[] Hide = new GameObject[0];
        public bool IsBroken { get; private set; }
        public event Action<Breakable> Broken;

        void OnCollisionEnter(Collision c)
        {
            if (!IsBroken && FromRagdoll(c)) Damage(c.relativeVelocity.magnitude * 0.35f);
        }

        void OnCollisionStay(Collision c)
        {
            if (!IsBroken && FromRagdoll(c)) Damage(Time.fixedDeltaTime * 0.35f); // standing or hanging wears it down
        }

        static bool FromRagdoll(Collision c) => c.collider.GetComponentInParent<RagdollRig>() != null;

        public void Damage(float amount)
        {
            Health -= amount;
            if (Health <= 0f) Break();
        }

        public void Break()
        {
            if (IsBroken) return;
            IsBroken = true;

            foreach (Joint j in Joints) if (j != null) Destroy(j);
            foreach (GameObject g in Hide) if (g != null && g != gameObject) Destroy(g);

            if (FallWhenBroken)
            {
                Rigidbody rb = GetComponent<Rigidbody>();
                if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = false;
                rb.mass = FallMass;
            }
            else if (Hide.Length > 0 || Joints.Length > 0)
            {
                // A cable: the cable object itself disappears once it has let go.
                Destroy(gameObject, 0.05f);
            }
            if (Broken != null) Broken(this);
        }
    }

    /// <summary>Stretches a visual cable between a fixed anchor and a point on a moving body.</summary>
    public class CableVisual : MonoBehaviour
    {
        public Vector3 Anchor;
        public Transform Body;
        public Vector3 LocalAnchor;

        void LateUpdate()
        {
            if (Body == null) { Destroy(gameObject); return; }
            Vector3 d = Body.TransformPoint(LocalAnchor) - Anchor;
            float len = d.magnitude;
            if (len < 0.01f) return;
            transform.position = Anchor + d * 0.5f;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, d);
            transform.localScale = new Vector3(0.14f, len * 0.5f, 0.14f);
        }
    }

    /// <summary>Fan over a hole: alternates between an updraft and a vacuum. The blades only kill during the vacuum.</summary>
    public class FanZone : MonoBehaviour
    {
        public float Period = 7f;
        public float UpdraftSeconds = 3.5f;
        public float Offset;
        public float UpdraftAcceleration = 16f;
        public float VacuumAcceleration = 22f;
        public KillZone Blades;
        public Rotator Rotor;
        public bool Vacuum { get; private set; }

        void Update()
        {
            Vacuum = (Time.time + Offset) % Period >= UpdraftSeconds;
            if (Blades != null) Blades.enabled = Vacuum;
            if (Rotor != null) Rotor.DegreesPerSecond = Vacuum ? 900f : 360f;
        }

        void OnTriggerStay(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic) return;
            Vector3 toCenter = transform.position - rb.worldCenterOfMass;
            toCenter.y = 0f;
            Vector3 acc = Vacuum
                ? Vector3.down * VacuumAcceleration + toCenter.normalized * (VacuumAcceleration * 0.5f)
                : Vector3.up * UpdraftAcceleration;
            rb.AddForce(acc, ForceMode.Acceleration);
        }
    }

    /// <summary>Surface that drags anything on it along a direction.</summary>
    public class ConveyorBelt : MonoBehaviour
    {
        public Vector3 Direction = Vector3.forward;
        public float Speed = 2.5f;
        public float Acceleration = 14f;

        void OnCollisionStay(Collision c)
        {
            Rigidbody rb = c.rigidbody;
            if (rb == null || rb.isKinematic) return;
            Vector3 dir = Direction.normalized;
            if (Vector3.Dot(rb.GetVelocity(), dir) < Speed) rb.AddForce(dir * Acceleration, ForceMode.Acceleration);
        }
    }

    /// <summary>Periodically drops props (or anything the Factory builds) at its position.</summary>
    public class ObjectSpawner : MonoBehaviour
    {
        public float Interval = 4f;
        public int MaxAlive = 6;
        public float Lifetime = 45f;
        public Vector3 LaunchVelocity;
        public Func<Vector3, GameObject> Factory;

        readonly List<GameObject> _alive = new List<GameObject>();
        float _next;

        void Start() { _next = Time.time + 2f; }

        void Update()
        {
            if (MatchManager.Current != null && MatchManager.Current.CurrentPhase != MatchManager.Phase.Playing) return;
            if (Time.time < _next) return;
            _next = Time.time + Interval / HazardScale.Value * UnityEngine.Random.Range(0.7f, 1.3f);

            _alive.RemoveAll(g => g == null);
            if (_alive.Count >= MaxAlive) return;

            GameObject go = Factory != null
                ? Factory(transform.position)
                : PropFactory.Spawn((PropType)UnityEngine.Random.Range(1, 5), transform.position);
            if (go == null) return;
            _alive.Add(go);
            Destroy(go, Lifetime);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null && LaunchVelocity != Vector3.zero) rb.AddForce(LaunchVelocity, ForceMode.VelocityChange);
        }
    }

    /// <summary>Chutes stage: marks a spot on the floor, then drops a heavy blob of pink goo on it.</summary>
    public class GooDropper : MonoBehaviour
    {
        public Vector3[] Points = new Vector3[0];
        public float Interval = 2.2f;
        public float Height = 12f;
        public float Telegraph = 1.2f;

        float _next;
        static PhysMat _gooMat;

        void Start() { _next = Time.time + 3f; }

        void Update()
        {
            if (Points.Length == 0) return;
            if (MatchManager.Current != null && MatchManager.Current.CurrentPhase != MatchManager.Phase.Playing) return;
            if (Time.time < _next) return;
            _next = Time.time + Interval / HazardScale.Value * UnityEngine.Random.Range(0.6f, 1.2f);
            StartCoroutine(Drop(Points[UnityEngine.Random.Range(0, Points.Length)]));
        }

        IEnumerator Drop(Vector3 point)
        {
            // Warning mark on the floor.
            GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mark.GetComponent<Collider>().enabled = false;
            Destroy(mark.GetComponent<Collider>());
            mark.transform.position = point + Vector3.up * 0.04f;
            mark.transform.localScale = new Vector3(2.4f, 0.02f, 2.4f);
            ProceduralMaterialGenerator.Apply(mark.GetComponent<Renderer>(), SurfaceType.Plastic, new Color(1f, 0.25f, 0.5f), Vector2.one);
            yield return new WaitForSeconds(Telegraph);
            Destroy(mark);

            GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blob.name = "PinkGoo";
            blob.transform.position = point + Vector3.up * Height;
            blob.transform.localScale = Vector3.one * 1.8f;
            ProceduralMaterialGenerator.Apply(blob.GetComponent<Renderer>(), SurfaceType.Plastic, new Color(1f, 0.4f, 0.7f), Vector2.one);
            if (_gooMat == null)
                _gooMat = new PhysMat("Goo") { bounciness = 0f, dynamicFriction = 1.2f, staticFriction = 1.2f, hideFlags = HideFlags.DontSave };
            blob.GetComponent<Collider>().sharedMaterial = _gooMat;
            Rigidbody rb = blob.AddComponent<Rigidbody>();
            rb.mass = 35f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Destroy(blob, 12f);
        }
    }

    /// <summary>Tower stage: a staircase that collapses a few seconds after someone first steps on it.</summary>
    public class CollapsingStairs : MonoBehaviour
    {
        public List<Transform> Steps = new List<Transform>();
        public float Delay = 7f;
        bool _armed;

        void OnTriggerEnter(Collider other)
        {
            if (_armed || other.GetComponent<RagdollLimb>() == null) return;
            _armed = true;
            StartCoroutine(Collapse());
        }

        IEnumerator Collapse()
        {
            yield return new WaitForSeconds(Delay);
            foreach (Transform step in Steps)
            {
                if (step == null) continue;
                Rigidbody rb = step.gameObject.AddComponent<Rigidbody>();
                rb.mass = 20f;
                yield return new WaitForSeconds(0.18f);
            }
        }
    }

    /// <summary>Subway stage: a train that periodically rushes through. Touching it is a ring-out.</summary>
    public class PassingTrain : MonoBehaviour
    {
        public Vector3 From;
        public Vector3 To;
        public float Speed = 28f;
        public float Interval = 9f;
        public float InitialDelay = 4f;

        Rigidbody _rb;
        Vector3 _dir;
        float _next;
        bool _running;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _dir = (To - From).normalized;
            _rb.position = From;
            _next = Time.time + InitialDelay;
        }

        void FixedUpdate()
        {
            if (!_running)
            {
                if (Time.time >= _next) _running = true;
                return;
            }
            Vector3 p = _rb.position + _dir * (Speed * Time.fixedDeltaTime);
            _rb.MovePosition(p);
            if (Vector3.Dot(p - To, _dir) >= 0f)
            {
                _running = false;
                _next = Time.time + Interval / HazardScale.Value;
                _rb.position = From;
            }
        }
    }
}
