using System.Collections.Generic;
using UnityEngine;

namespace BeefCity.Core
{
    /// <summary>Classic party-brawler camera: looks down at an angle and zooms out to keep everyone in frame.</summary>
    [RequireComponent(typeof(Camera))]
    public class PartyCamera : MonoBehaviour
    {
        /// <summary>The match fills this every frame with the positions that must stay visible.</summary>
        public readonly List<Vector3> Points = new List<Vector3>();

        public float pitch = 55f;
        public float minDistance = 12f;
        public float maxDistance = 40f;
        public float smoothTime = 0.35f;

        Vector3 _velocity;
        Vector3 _focus = Vector3.up;

        void LateUpdate()
        {
            if (Points.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (Vector3 p in Points) sum += p;
                Vector3 center = sum / Points.Count;

                float spread = 0f;
                foreach (Vector3 p in Points) spread = Mathf.Max(spread, Vector3.Distance(p, center));

                _focus = Vector3.Lerp(_focus, center, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                float distance = Mathf.Clamp(minDistance + spread * 1.1f, minDistance, maxDistance);
                // Behind (-z) and above the group, looking down at it.
                Vector3 desired = _focus + new Vector3(0f, Mathf.Sin(pitch * Mathf.Deg2Rad), -Mathf.Cos(pitch * Mathf.Deg2Rad)) * distance;
                transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            }
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(_focus - transform.position), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
        }
    }
}
