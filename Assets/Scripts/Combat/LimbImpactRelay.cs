using UnityEngine;

namespace BeefCity.Ragdoll
{
    /// <summary>
    /// Forwards physics callbacks from a limb's GameObject (where Unity delivers them) to the owning RagdollCombat.
    /// Added automatically at runtime; you never need to place this by hand.
    /// </summary>
    [DisallowMultipleComponent]
    public class LimbImpactRelay : MonoBehaviour
    {
        [HideInInspector] public RagdollCombat Owner;
        [HideInInspector] public LimbId Limb;

        void OnCollisionEnter(Collision collision)
        {
            if (Owner != null) Owner.HandleCollision(Limb, collision, false);
        }

        void OnCollisionStay(Collision collision)
        {
            if (Owner != null) Owner.HandleCollision(Limb, collision, true);
        }

        // Unity sends this to the GameObject that owns the joint when its breakForce is exceeded (a grab tearing loose).
        void OnJointBreak(float breakForce)
        {
            if (Owner != null) Owner.HandleJointBreak(Limb);
        }
    }
}
