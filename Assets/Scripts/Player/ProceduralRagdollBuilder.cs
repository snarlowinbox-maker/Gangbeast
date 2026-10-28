using System.Collections.Generic;
using UnityEngine;
using BeefCity.Graphics;

#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
#else
using PhysMat = UnityEngine.PhysicMaterial;
#endif

namespace BeefCity.Ragdoll
{
    /// <summary>
    /// Builds a complete active ragdoll (15 rigidbodies, ConfigurableJoints with slerp motors)
    /// out of Unity primitives. No models, no textures, no Animator.
    /// Attach to an empty GameObject standing on the ground (the object's position = the feet).
    /// Keep the object's scale at (1,1,1) and use the Scale field instead.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProceduralRagdollBuilder : MonoBehaviour
    {
        public const float DefaultStandHeight = 0.98f; // pelvis height above the feet at scale 1

        enum Shape { Box, Capsule, Sphere }

        sealed class Spec
        {
            public LimbId Id, Parent;
            public bool IsRoot, Hinge;
            public Shape Shape;
            public Vector3 Center, Dims, Anchor;
            public float Mass, TwistLow, TwistHigh, SwingY, SwingZ, Strength;
        }

        [Header("Build")]
        [SerializeField] bool buildOnStart = true;
        [SerializeField] bool addMotor = true;
        [SerializeField] bool addCombat = true;
        [SerializeField, Min(0.1f)] float scale = 1f;
        [SerializeField, Min(0.1f)] float massScale = 1f;

        [Header("Look")]
        [SerializeField] Color bodyColor = new Color(0.98f, 0.55f, 0.18f);
        [Tooltip("Optional. Leave empty to use a procedural plastic material.")]
        [SerializeField] Material bodyMaterial;
        [Tooltip("Optional. Used for the eyes. Leave empty for procedural rubber.")]
        [SerializeField] Material detailMaterial;

        [Header("Joint Motors (tune these first)")]
        [SerializeField] float jointSpring = 600f;
        [SerializeField] float jointDamper = 40f;

        [Header("Rigidbodies")]
        [SerializeField] float linearDamping = 0.05f;
        [SerializeField] float angularDamping = 0.6f;
        [SerializeField] int solverIterations = 12;
        [SerializeField] int solverVelocityIterations = 6;

        [Header("Surface")]
        [SerializeField] float friction = 0.9f;

        public RagdollRig Rig => GetComponent<RagdollRig>();

        void Start()
        {
            if (buildOnStart && (Rig == null || !Rig.IsBuilt)) Build();
        }

        /// <summary>Sets up the next Build() from code (used by the match spawner and the costume room).</summary>
        public void Configure(Color color, bool withMotor, bool withCombat)
        {
            bodyColor = color;
            addMotor = withMotor;
            addCombat = withCombat && withMotor;
            buildOnStart = false;
        }

        /// <summary>Sets body size and mass for the next Build() (heavy enemies, small dodgers).</summary>
        public void ConfigureScale(float newScale, float newMassScale)
        {
            scale = Mathf.Max(0.1f, newScale);
            massScale = Mathf.Max(0.1f, newMassScale);
        }

        [ContextMenu("Build Ragdoll")]
        public RagdollRig Build()
        {
            Clear();

            if (Vector3.Distance(transform.lossyScale, Vector3.one) > 0.001f)
                Debug.LogWarning("[RagdollBuilder] Keep this object's scale at (1,1,1); use the Scale field instead.", this);

            List<Spec> specs = CreateSpecs();
            RagdollRig rig = GetComponent<RagdollRig>();
            if (rig == null) rig = gameObject.AddComponent<RagdollRig>();

            var limbs = new RagdollLimb[specs.Count];
            var tint = new List<Renderer>();
            var colliders = new List<Collider>(specs.Count);
            PhysMat phys = CreatePhysicsMaterial();
            Material body = bodyMaterial != null ? bodyMaterial : ProceduralMaterialGenerator.GetMaterial(SurfaceType.Plastic);
            Material detail = detailMaterial != null ? detailMaterial : ProceduralMaterialGenerator.GetMaterial(SurfaceType.Rubber);

            foreach (Spec s in specs)
            {
                var go = new GameObject(s.Id.ToString());
                go.transform.SetParent(transform, false);
                go.transform.localPosition = s.Center * scale;
                go.transform.localRotation = Quaternion.identity;

                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = s.Mass * massScale;
                rb.SetDamping(linearDamping, angularDamping);
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = solverIterations;
                rb.solverVelocityIterations = solverVelocityIterations;
                rb.maxAngularVelocity = 40f;
                rb.maxDepenetrationVelocity = 4f;

                colliders.Add(AddShape(go, s, phys, body, tint));

                ConfigurableJoint joint = null;
                Rigidbody parentRb = null;
                if (!s.IsRoot)
                {
                    RagdollLimb parentLimb = limbs[(int)s.Parent];
                    parentRb = parentLimb.Body;
                    joint = ConfigureJoint(go, s, parentRb, parentLimb.transform);
                }

                if (s.Id == LimbId.Head) AddEyes(go.transform, detail);

                RagdollLimb limb = go.AddComponent<RagdollLimb>();
                limb.Initialize(s.Id, rig, rb, joint, parentRb, jointSpring * s.Strength, jointDamper * s.Strength);
                limbs[(int)s.Id] = limb;
            }

            // A ragdoll never collides with itself.
            for (int i = 0; i < colliders.Count; i++)
                for (int j = i + 1; j < colliders.Count; j++)
                    Physics.IgnoreCollision(colliders[i], colliders[j], true);

            rig.Initialize(limbs, tint.ToArray());
            rig.SetColor(bodyColor);
            rig.SetMotorStrength(1f);

            ActiveRagdollMotor motor = GetComponent<ActiveRagdollMotor>();
            if (motor == null && addMotor) motor = gameObject.AddComponent<ActiveRagdollMotor>();
            if (motor != null)
            {
                motor.standHeight = DefaultStandHeight * scale;
                motor.Rebind();
            }

            if (addCombat && GetComponent<RagdollCombat>() == null) gameObject.AddComponent<RagdollCombat>();

            return rig;
        }

        [ContextMenu("Clear Ragdoll")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<RagdollLimb>() != null) SafeDestroy(child.gameObject);
            }
        }

        // ------------------------------------------------------------------ specs

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static Spec Root(LimbId id, Shape shape, Vector3 center, Vector3 dims, float mass, float strength) =>
            new Spec { Id = id, Parent = id, IsRoot = true, Shape = shape, Center = center, Dims = dims, Mass = mass, Strength = strength };

        static Spec Ball(LimbId id, LimbId parent, Shape shape, Vector3 center, Vector3 dims, float mass,
                         Vector3 anchor, float twist, float swingY, float swingZ, float strength) =>
            new Spec
            {
                Id = id, Parent = parent, Shape = shape, Center = center, Dims = dims, Mass = mass, Anchor = anchor,
                TwistLow = -twist, TwistHigh = twist, SwingY = swingY, SwingZ = swingZ, Strength = strength
            };

        static Spec HingeSpec(LimbId id, LimbId parent, Shape shape, Vector3 center, Vector3 dims, float mass,
                              Vector3 anchor, float low, float high, float strength) =>
            new Spec
            {
                Id = id, Parent = parent, Shape = shape, Center = center, Dims = dims, Mass = mass, Anchor = anchor,
                Hinge = true, TwistLow = low, TwistHigh = high, Strength = strength
            };

        // Units are meters at scale 1, feet at y = 0, character faces +Z, left = -X.
        // Capsule dims: x = radius, y = total height. Sphere dims: x = radius. Box dims = size.
        // Parents must come before children in this list.
        static List<Spec> CreateSpecs()
        {
            var list = new List<Spec>(15);
            list.Add(Root(LimbId.Pelvis, Shape.Box, V(0f, 0.98f, 0f), V(0.34f, 0.20f, 0.22f), 6f, 1f));
            list.Add(Ball(LimbId.Torso, LimbId.Pelvis, Shape.Box, V(0f, 1.30f, 0f), V(0.38f, 0.46f, 0.24f), 10f,
                          V(0f, 1.08f, 0f), 35f, 30f, 25f, 1.6f));
            list.Add(Ball(LimbId.Head, LimbId.Torso, Shape.Sphere, V(0f, 1.72f, 0f), V(0.16f, 0f, 0f), 4f,
                          V(0f, 1.56f, 0f), 50f, 40f, 30f, 0.7f));

            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float sx = left ? -1f : 1f;
                LimbId upperArm = left ? LimbId.UpperArmL : LimbId.UpperArmR;
                LimbId lowerArm = left ? LimbId.LowerArmL : LimbId.LowerArmR;
                LimbId hand = left ? LimbId.HandL : LimbId.HandR;
                LimbId upperLeg = left ? LimbId.UpperLegL : LimbId.UpperLegR;
                LimbId lowerLeg = left ? LimbId.LowerLegL : LimbId.LowerLegR;
                LimbId foot = left ? LimbId.FootL : LimbId.FootR;

                list.Add(Ball(upperArm, LimbId.Torso, Shape.Capsule, V(sx * 0.29f, 1.33f, 0f), V(0.055f, 0.30f, 0f), 1.6f,
                              V(sx * 0.29f, 1.48f, 0f), 40f, 80f, 70f, 0.5f));
                // Elbow bends forward = negative rotation about X.
                list.Add(HingeSpec(lowerArm, upperArm, Shape.Capsule, V(sx * 0.29f, 1.04f, 0f), V(0.05f, 0.28f, 0f), 1.2f,
                                   V(sx * 0.29f, 1.18f, 0f), -130f, 5f, 0.4f));
                list.Add(Ball(hand, lowerArm, Shape.Sphere, V(sx * 0.29f, 0.835f, 0f), V(0.065f, 0f, 0f), 0.8f,
                              V(sx * 0.29f, 0.90f, 0f), 30f, 40f, 40f, 0.15f));

                list.Add(Ball(upperLeg, LimbId.Pelvis, Shape.Capsule, V(sx * 0.10f, 0.69f, 0f), V(0.075f, 0.42f, 0f), 3.2f,
                              V(sx * 0.10f, 0.90f, 0f), 25f, 85f, 30f, 1f));
                // Knee bends backward = positive rotation about X.
                list.Add(HingeSpec(lowerLeg, upperLeg, Shape.Capsule, V(sx * 0.10f, 0.28f, 0f), V(0.065f, 0.40f, 0f), 2.4f,
                                   V(sx * 0.10f, 0.48f, 0f), -5f, 135f, 0.8f));
                list.Add(Ball(foot, lowerLeg, Shape.Box, V(sx * 0.10f, 0.04f, 0.05f), V(0.15f, 0.08f, 0.28f), 1.2f,
                              V(sx * 0.10f, 0.08f, 0f), 15f, 35f, 15f, 0.4f));
            }
            return list;
        }

        // ------------------------------------------------------------------ construction helpers

        Collider AddShape(GameObject go, Spec s, PhysMat phys, Material mat, List<Renderer> tint)
        {
            Collider col;
            PrimitiveType prim;
            Vector3 visualScale;

            switch (s.Shape)
            {
                case Shape.Capsule:
                {
                    var c = go.AddComponent<CapsuleCollider>();
                    c.direction = 1;
                    c.radius = s.Dims.x * scale;
                    c.height = s.Dims.y * scale;
                    col = c;
                    prim = PrimitiveType.Capsule;
                    visualScale = new Vector3(s.Dims.x * 2f, s.Dims.y * 0.5f, s.Dims.x * 2f) * scale;
                    break;
                }
                case Shape.Sphere:
                {
                    var c = go.AddComponent<SphereCollider>();
                    c.radius = s.Dims.x * scale;
                    col = c;
                    prim = PrimitiveType.Sphere;
                    visualScale = Vector3.one * (s.Dims.x * 2f * scale);
                    break;
                }
                default:
                {
                    var c = go.AddComponent<BoxCollider>();
                    c.size = s.Dims * scale;
                    col = c;
                    prim = PrimitiveType.Cube;
                    visualScale = s.Dims * scale;
                    break;
                }
            }

            col.sharedMaterial = phys;
            tint.Add(MakeVisual(go.transform, prim, Vector3.zero, visualScale, mat));
            return col;
        }

        ConfigurableJoint ConfigureJoint(GameObject go, Spec s, Rigidbody parentRb, Transform parentTransform)
        {
            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;
            joint.autoConfigureConnectedAnchor = false;

            Vector3 anchorWorld = transform.TransformPoint(s.Anchor * scale);
            joint.anchor = go.transform.InverseTransformPoint(anchorWorld);
            joint.connectedAnchor = parentTransform.InverseTransformPoint(anchorWorld);

            // Ball joints twist around the bone (Y); hinges rotate around X.
            joint.axis = s.Hinge ? Vector3.right : Vector3.up;
            joint.secondaryAxis = s.Hinge ? Vector3.up : Vector3.right;

            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = s.Hinge ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            joint.angularZMotion = s.Hinge ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;

            joint.lowAngularXLimit = new SoftJointLimit { limit = Mathf.Min(0f, s.TwistLow) };
            joint.highAngularXLimit = new SoftJointLimit { limit = Mathf.Max(0f, s.TwistHigh) };
            joint.angularYLimit = new SoftJointLimit { limit = s.SwingY };
            joint.angularZLimit = new SoftJointLimit { limit = s.SwingZ };

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.1f * scale;
            return joint;
        }

        void AddEyes(Transform head, Material mat)
        {
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                MakeVisual(head, PrimitiveType.Sphere, new Vector3(sx * 0.065f, 0.03f, 0.14f) * scale,
                           Vector3.one * (0.07f * scale), mat);
            }
        }

        Renderer MakeVisual(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject v = GameObject.CreatePrimitive(type);
            v.name = "Visual";
            Collider c = v.GetComponent<Collider>();
            if (c != null) SafeDestroy(c);
            v.transform.SetParent(parent, false);
            v.transform.localPosition = localPos;
            v.transform.localRotation = Quaternion.identity;
            v.transform.localScale = localScale;
            Renderer r = v.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        PhysMat CreatePhysicsMaterial()
        {
            var m = new PhysMat("RagdollSurface")
            {
                dynamicFriction = friction,
                staticFriction = friction + 0.1f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum,
                hideFlags = HideFlags.DontSave
            };
            return m;
        }

        static void SafeDestroy(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
