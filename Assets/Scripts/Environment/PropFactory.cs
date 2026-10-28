using UnityEngine;
using BeefCity.Graphics;
using BeefCity.Ragdoll;

#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
#else
using PhysMat = UnityEngine.PhysicMaterial;
#endif

namespace BeefCity.Core
{
    public enum PropType { SmallBalls, Ball, Box, Dumpster, Boneloaf }

    /// <summary>
    /// Builds grabbable physics props out of primitives. Used by the secret commands now and by the admin panel's
    /// "give item" later.
    /// </summary>
    public static class PropFactory
    {
        public static GameObject Spawn(PropType type, Vector3 position)
        {
            switch (type)
            {
                case PropType.SmallBalls: return SmallBalls(position);
                case PropType.Ball: return Ball(position);
                case PropType.Box: return Box(position);
                case PropType.Dumpster: return Dumpster(position);
                default: return Boneloaf(position);
            }
        }

        static GameObject SmallBalls(Vector3 position)
        {
            var root = new GameObject("SmallBalls");
            root.transform.position = position;
            Color[] colors = { new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.5f, 0.95f), new Color(0.98f, 0.85f, 0.2f),
                               new Color(0.3f, 0.8f, 0.35f), new Color(0.95f, 0.45f, 0.7f), new Color(0.98f, 0.55f, 0.18f) };
            for (int i = 0; i < colors.Length; i++)
            {
                GameObject b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                b.name = "SmallBall";
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0f, 0.6f), Random.Range(-0.4f, 0.4f));
                b.transform.localScale = Vector3.one * 0.24f;
                Tint(b, SurfaceType.Plastic, colors[i]);
                b.GetComponent<Collider>().sharedMaterial = Bouncy(0.7f);
                Body(b, 0.2f, true);
            }
            return root;
        }

        static GameObject Ball(Vector3 position)
        {
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            b.name = "Ball";
            b.transform.position = position;
            b.transform.localScale = Vector3.one * 0.7f;
            Tint(b, SurfaceType.Plastic, new Color(0.95f, 0.35f, 0.25f));
            b.GetComponent<Collider>().sharedMaterial = Bouncy(0.6f);
            Body(b, 1.2f, true);
            return b;
        }

        static GameObject Box(Vector3 position)
        {
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = "Box";
            b.transform.position = position;
            b.transform.localScale = Vector3.one * 0.8f;
            Tint(b, SurfaceType.Wood, new Color(1f, 0.9f, 0.75f));
            Body(b, 8f, false);
            return b;
        }

        static GameObject Dumpster(Vector3 position)
        {
            GameObject d = GameObject.CreatePrimitive(PrimitiveType.Cube);
            d.name = "Dumpster";
            d.transform.position = position;
            d.transform.localScale = new Vector3(1.8f, 1f, 1f);
            Tint(d, SurfaceType.Metal, new Color(0.25f, 0.55f, 0.3f));
            Body(d, 60f, false);

            // The lid is visual only so it cannot snag on things.
            Visual(d.transform, PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(1.05f, 0.1f, 1.1f),
                   SurfaceType.Metal, new Color(0.15f, 0.35f, 0.2f));
            return d;
        }

        static GameObject Boneloaf(Vector3 position)
        {
            var root = new GameObject("Boneloaf");
            root.transform.position = position;
            Color crust = new Color(0.85f, 0.7f, 0.45f);

            Visual(root.transform, PrimitiveType.Capsule, Vector3.zero, new Vector3(0.5f, 0.8f, 0.5f), SurfaceType.Plastic, crust,
                   new Vector3(0f, 0f, 90f));
            for (int end = -1; end <= 1; end += 2)
                for (int side = -1; side <= 1; side += 2)
                    Visual(root.transform, PrimitiveType.Sphere, new Vector3(end * 0.85f, 0f, side * 0.14f), Vector3.one * 0.32f,
                           SurfaceType.Plastic, new Color(0.95f, 0.92f, 0.85f));

            CapsuleCollider cc = root.AddComponent<CapsuleCollider>();
            cc.direction = 0; // along X
            cc.radius = 0.25f;
            cc.height = 1.6f;
            for (int end = -1; end <= 1; end += 2)
            {
                SphereCollider sc = root.AddComponent<SphereCollider>();
                sc.center = new Vector3(end * 0.85f, 0f, 0f);
                sc.radius = 0.3f;
            }
            Body(root, 10f, false);
            return root;
        }

        // ------------------------------------------------------------------ helpers

        static void Body(GameObject go, float mass, bool continuous)
        {
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = continuous ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            rb.SetDamping(0.05f, 0.1f);
        }

        static void Tint(GameObject go, SurfaceType surface, Color color)
        {
            ProceduralMaterialGenerator.Apply(go.GetComponent<Renderer>(), surface, color, Vector2.one);
        }

        static void Visual(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, SurfaceType surface, Color color,
                           Vector3? euler = null)
        {
            GameObject v = GameObject.CreatePrimitive(type);
            v.name = "Visual";
            Collider c = v.GetComponent<Collider>();
            c.enabled = false;
            Object.Destroy(c);
            v.transform.SetParent(parent, false);
            v.transform.localPosition = pos;
            v.transform.localScale = scale;
            if (euler.HasValue) v.transform.localRotation = Quaternion.Euler(euler.Value);
            Tint(v, surface, color);
        }

        static PhysMat Bouncy(float bounciness)
        {
            return new PhysMat("Bouncy")
            {
                bounciness = bounciness,
                dynamicFriction = 0.4f,
                staticFriction = 0.4f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                hideFlags = HideFlags.DontSave
            };
        }
    }
}
