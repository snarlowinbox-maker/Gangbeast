using System.Collections.Generic;
using UnityEngine;
using BeefCity.Graphics;

namespace BeefCity.Core
{
    /// <summary>A short-lived area that pulls everything in (attract) or shoves everything away (push).</summary>
    public class ForceField : MonoBehaviour
    {
        public bool Attract = true;
        public float Radius = 7f;
        public float Strength = 45f;   // acceleration at the center, fades to zero at the edge
        public float Life = 2.5f;

        readonly Collider[] _buffer = new Collider[256];
        readonly HashSet<Rigidbody> _seen = new HashSet<Rigidbody>();
        Transform _visual;
        float _age;

        public static ForceField Spawn(Vector3 position, bool attract)
        {
            var go = new GameObject(attract ? "AttractiveForce" : "PushingForce");
            go.transform.position = position;
            ForceField f = go.AddComponent<ForceField>();
            f.Attract = attract;
            f.BuildVisual();
            return f;
        }

        void BuildVisual()
        {
            GameObject v = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            v.name = "Visual";
            v.GetComponent<Collider>().enabled = false;
            Destroy(v.GetComponent<Collider>());
            v.transform.SetParent(transform, false);
            ProceduralMaterialGenerator.Apply(v.GetComponent<Renderer>(), SurfaceType.Plastic,
                Attract ? new Color(0.3f, 0.5f, 1f) : new Color(1f, 0.55f, 0.15f), Vector2.one);
            _visual = v.transform;
        }

        void FixedUpdate()
        {
            _age += Time.fixedDeltaTime;
            _seen.Clear();
            int n = Physics.OverlapSphereNonAlloc(transform.position, Radius, _buffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Rigidbody rb = _buffer[i].attachedRigidbody;
                if (rb == null || rb.isKinematic || !_seen.Add(rb)) continue;

                Vector3 toCenter = transform.position - rb.worldCenterOfMass;
                float d = toCenter.magnitude;
                if (d < 0.1f) continue;
                float falloff = 1f - Mathf.Clamp01(d / Radius);
                Vector3 dir = toCenter / d * (Attract ? 1f : -1f);
                rb.AddForce(dir * (Strength * falloff), ForceMode.Acceleration);
            }
        }

        void Update()
        {
            if (_visual != null)
            {
                float pulse = 0.5f + Mathf.Sin(_age * 12f) * 0.1f;
                float fade = Mathf.Clamp01((Life - _age) / 0.5f);
                _visual.localScale = Vector3.one * (pulse * fade);
            }
            if (_age >= Life) Destroy(gameObject);
        }
    }
}
