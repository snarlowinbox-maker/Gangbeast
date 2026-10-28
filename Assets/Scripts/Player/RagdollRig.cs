using System.Collections.Generic;
using UnityEngine;

namespace BeefCity.Ragdoll
{
    /// <summary>
    /// Root component of a built ragdoll. Holds every limb and offers rig-wide operations
    /// (motor strength for knockouts, tinting for the costume room, teleporting for respawns).
    /// </summary>
    [DisallowMultipleComponent]
    public class RagdollRig : MonoBehaviour
    {
        [SerializeField] RagdollLimb[] limbs = new RagdollLimb[0];
        [SerializeField] Renderer[] tintRenderers = new Renderer[0];
        [SerializeField] Color bodyColor = Color.white;
        [SerializeField, Range(0f, 1f)] float motorStrength = 1f;

        static MaterialPropertyBlock _mpb;
        static MaterialPropertyBlock Mpb => _mpb ??= new MaterialPropertyBlock();

        public IReadOnlyList<RagdollLimb> Limbs => limbs;
        public float MotorStrength => motorStrength;
        public Color BodyColor => bodyColor;
        public float TotalMass { get; private set; }
        public bool IsBuilt => Get(LimbId.Pelvis) != null;

        void Awake()
        {
            RecalculateMass();
        }

        public void Initialize(RagdollLimb[] builtLimbs, Renderer[] renderersToTint)
        {
            limbs = builtLimbs;
            tintRenderers = renderersToTint;
            RecalculateMass();
        }

        public RagdollLimb Get(LimbId id)
        {
            int i = (int)id;
            return limbs != null && i >= 0 && i < limbs.Length ? limbs[i] : null;
        }

        public Rigidbody Body(LimbId id)
        {
            RagdollLimb l = Get(id);
            return l != null ? l.Body : null;
        }

        public void RecalculateMass()
        {
            float total = 0f;
            if (limbs != null)
                foreach (RagdollLimb l in limbs)
                    if (l != null && l.Body != null) total += l.Body.mass;
            TotalMass = total;
        }

        public Vector3 CenterOfMass
        {
            get
            {
                Vector3 sum = Vector3.zero;
                float mass = 0f;
                foreach (RagdollLimb l in limbs)
                {
                    if (l == null || l.Body == null) continue;
                    sum += l.Body.worldCenterOfMass * l.Body.mass;
                    mass += l.Body.mass;
                }
                return mass > 0f ? sum / mass : transform.position;
            }
        }

        /// <summary>1 = full strength, 0 = fully limp (knocked out). Hook for knockout / revival.</summary>
        public void SetMotorStrength(float strength01)
        {
            motorStrength = Mathf.Clamp01(strength01);
            foreach (RagdollLimb l in limbs)
                if (l != null) l.SetDriveStrength(motorStrength);
        }

        /// <summary>Tints the body with a MaterialPropertyBlock (no material instances created).</summary>
        public void SetColor(Color color)
        {
            bodyColor = color;
            foreach (Renderer r in tintRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(Mpb);
                Mpb.SetColor("_BaseColor", color); // URP
                Mpb.SetColor("_Color", color);     // Built-in fallback
                r.SetPropertyBlock(Mpb);
            }
        }

        /// <summary>Freezes the ragdoll in its rest pose (costume-room display) or releases it back to physics.</summary>
        public void SetKinematic(bool kinematic)
        {
            foreach (RagdollLimb l in limbs)
            {
                if (l == null || l.Body == null) continue;
                l.Body.isKinematic = kinematic;
                l.Body.interpolation = kinematic ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            }
        }

        /// <summary>Moves the whole ragdoll so the pelvis ends up at the target position and kills all velocity.</summary>
        public void Teleport(Vector3 pelvisTarget)
        {
            Rigidbody pelvis = Body(LimbId.Pelvis);
            if (pelvis == null) return;
            Vector3 delta = pelvisTarget - pelvis.position;
            foreach (RagdollLimb l in limbs)
            {
                if (l == null || l.Body == null) continue;
                l.Body.position += delta;
                l.Body.SetVelocity(Vector3.zero);
                l.Body.angularVelocity = Vector3.zero;
            }
        }
    }
}
