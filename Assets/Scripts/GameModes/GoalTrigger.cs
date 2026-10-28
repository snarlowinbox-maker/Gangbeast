using UnityEngine;

namespace BeefCity.Core
{
    /// <summary>Put on a trigger collider. When the arena's ball enters, the scoring team is reported.</summary>
    public class GoalTrigger : MonoBehaviour
    {
        public ArenaInfo Arena;
        public int ScoringTeam;

        void OnTriggerEnter(Collider other)
        {
            if (Arena == null || Arena.ball == null) return;
            if (other.attachedRigidbody == Arena.ball) Arena.RaiseGoal(ScoringTeam);
        }
    }
}
