using UnityEngine;

namespace BeefCity.Ragdoll
{
    /// <summary>Identifies each body part. The numeric order is also the array index inside RagdollRig.</summary>
    public enum LimbId
    {
        Pelvis = 0, Torso, Head,
        UpperArmL, LowerArmL, HandL,
        UpperArmR, LowerArmR, HandR,
        UpperLegL, LowerLegL, FootL,
        UpperLegR, LowerLegR, FootR
    }

    /// <summary>Small helpers so the project compiles on Unity 6 and older versions.</summary>
    public static class RigidbodyCompat
    {
        public static Vector3 GetVelocity(this Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(this Rigidbody rb, Vector3 value)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }

        public static void SetDamping(this Rigidbody rb, float linear, float angular)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = linear;
            rb.angularDamping = angular;
#else
            rb.drag = linear;
            rb.angularDrag = angular;
#endif
        }
    }

    /// <summary>
    /// One body part of the active ragdoll. Wraps the Rigidbody + ConfigurableJoint and exposes
    /// "set target pose" / "set drive strength" so gameplay code never touches joint math directly.
    /// </summary>
    [DisallowMultipleComponent]
    public class RagdollLimb : MonoBehaviour
    {
        public LimbId Id;
        public Rigidbody Body;
        public ConfigurableJoint Joint;      // null for the pelvis (root)
        public Rigidbody ParentBody;
        public RagdollRig Rig;
        public float BaseSpring;
        public float BaseDamper;
        public float MaxTorque = 100000f;

        [SerializeField] Quaternion restRotation = Quaternion.identity; // rotation relative to parent at build time
        Quaternion _jointSpace = Quaternion.identity;

        public bool IsRoot => Joint == null;

        public void Initialize(LimbId id, RagdollRig rig, Rigidbody body, ConfigurableJoint joint,
                               Rigidbody parentBody, float spring, float damper)
        {
            Id = id;
            Rig = rig;
            Body = body;
            Joint = joint;
            ParentBody = parentBody;
            BaseSpring = spring;
            BaseDamper = damper;
            restRotation = parentBody != null
                ? Quaternion.Inverse(parentBody.rotation) * body.rotation
                : Quaternion.identity;
            CacheJointSpace();
        }

        void Awake()
        {
            CacheJointSpace();
        }

        void CacheJointSpace()
        {
            if (Joint == null) return;
            // Same frame ConfigurableJoint builds internally from axis + secondaryAxis.
            Vector3 right = Joint.axis.normalized;
            Vector3 forward = Vector3.Cross(Joint.axis, Joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            _jointSpace = Quaternion.LookRotation(forward, up);
        }

        /// <summary>
        /// Sets the pose this limb's motor tries to reach, as a rotation relative to the parent body.
        /// Quaternion.identity = the standing rest pose.
        /// </summary>
        public void SetTargetLocalRotation(Quaternion target)
        {
            if (Joint == null) return;
            Joint.targetRotation = Quaternion.Inverse(_jointSpace) * Quaternion.Inverse(target) * restRotation * _jointSpace;
        }

        /// <summary>0 = limp (just a little damping), 1 = full motor strength.</summary>
        public void SetDriveStrength(float strength01)
        {
            if (Joint == null) return;
            float s = Mathf.Clamp01(strength01);
            Joint.slerpDrive = new JointDrive
            {
                positionSpring = BaseSpring * s,
                positionDamper = Mathf.Max(BaseDamper * s, BaseDamper * 0.08f),
                maximumForce = MaxTorque
            };
        }
    }
}
