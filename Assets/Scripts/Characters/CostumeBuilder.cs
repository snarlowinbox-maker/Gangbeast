using UnityEngine;
using BeefCity.Graphics;
using BeefCity.Ragdoll;

namespace BeefCity.Customization
{
    /// <summary>
    /// Applies a CostumeData to a ragdoll: body tint plus accessories made of primitives.
    /// Accessories have no colliders, so they add no mass or physics behaviour.
    /// </summary>
    public static class CostumeBuilder
    {
        const string RootName = "Accessories";

        public static void Apply(RagdollRig rig, CostumeData c)
        {
            if (rig == null || c == null) return;
            rig.SetColor(c.bodyColor);

            RagdollLimb head = rig.Get(LimbId.Head);
            RagdollLimb torso = rig.Get(LimbId.Torso);
            if (head == null || torso == null) return;

            Clear(head.transform);
            Clear(torso.transform);

            SphereCollider hc = head.GetComponent<SphereCollider>();
            float u = hc != null ? hc.radius / 0.16f : 1f; // 1 unit = head radius / 0.16 (the rig's scale)

            Transform headRoot = NewRoot(head.transform);
            Transform torsoRoot = NewRoot(torso.transform);

            BuildHat(headRoot, c.hat, c.accentColor, u);
            BuildNose(headRoot, c.nose, c.accentColor, u);
            BuildExtra(headRoot, torsoRoot, c.extra, c.accentColor, u);
        }

        static void Clear(Transform parent)
        {
            Transform t = parent.Find(RootName);
            if (t == null) return;
            t.SetParent(null);
            Object.Destroy(t.gameObject);
        }

        static Transform NewRoot(Transform parent)
        {
            var go = new GameObject(RootName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static GameObject Prim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color color,
                               SurfaceType surface = SurfaceType.Plastic, Vector3? euler = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false; // never touches physics, even for the frame before Destroy takes effect
                Object.Destroy(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
            ProceduralMaterialGenerator.Apply(go.GetComponent<Renderer>(), surface, color, Vector2.one);
            return go;
        }

        // Cylinder primitive: height 2, radius 0.5 -> scale = (diameter, height / 2, diameter)
        static void Cyl(Transform p, Vector3 pos, float dia, float height, Color col, float u, SurfaceType s = SurfaceType.Plastic)
        {
            Prim(p, PrimitiveType.Cylinder, pos * u, new Vector3(dia, height * 0.5f, dia) * u, col, s);
        }

        static void BuildHat(Transform root, int hat, Color accent, float u)
        {
            Color dark = new Color(0.08f, 0.08f, 0.1f);
            Color orange = new Color(1f, 0.45f, 0.05f);
            switch (hat)
            {
                case 1: // top hat
                    Cyl(root, new Vector3(0f, 0.155f, 0f), 0.34f, 0.02f, dark, u);
                    Cyl(root, new Vector3(0f, 0.255f, 0f), 0.20f, 0.18f, dark, u);
                    Cyl(root, new Vector3(0f, 0.20f, 0f), 0.205f, 0.035f, accent, u);
                    break;
                case 2: // party hat (stacked disks make the cone)
                    for (int i = 0; i < 5; i++)
                        Cyl(root, new Vector3(0f, 0.19f + i * 0.05f, 0f), 0.22f * (1f - i * 0.2f), 0.05f,
                            i % 2 == 0 ? accent : Color.white, u);
                    break;
                case 3: // cap
                    Prim(root, PrimitiveType.Sphere, new Vector3(0f, 0.12f, 0f) * u, new Vector3(0.34f, 0.2f, 0.34f) * u, accent);
                    Prim(root, PrimitiveType.Cube, new Vector3(0f, 0.10f, 0.17f) * u, new Vector3(0.2f, 0.015f, 0.14f) * u, accent);
                    break;
                case 4: // crown
                    Color gold = new Color(0.95f, 0.8f, 0.15f);
                    Cyl(root, new Vector3(0f, 0.19f, 0f), 0.27f, 0.07f, gold, u, SurfaceType.Metal);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 72f * Mathf.Deg2Rad;
                        Prim(root, PrimitiveType.Cube, new Vector3(Mathf.Sin(a) * 0.12f, 0.26f, Mathf.Cos(a) * 0.12f) * u,
                             new Vector3(0.04f, 0.08f, 0.04f) * u, gold, SurfaceType.Metal);
                    }
                    break;
                case 5: // traffic cone
                    float[] dia = { 0.26f, 0.22f, 0.18f, 0.14f, 0.10f };
                    for (int i = 0; i < dia.Length; i++)
                        Cyl(root, new Vector3(0f, 0.20f + i * 0.07f, 0f), dia[i], 0.07f, i == 1 || i == 3 ? Color.white : orange, u);
                    break;
            }
        }

        static void BuildNose(Transform root, int nose, Color accent, float u)
        {
            switch (nose)
            {
                case 1: // round
                    Prim(root, PrimitiveType.Sphere, new Vector3(0f, -0.01f, 0.17f) * u, Vector3.one * (0.07f * u), accent);
                    break;
                case 2: // long
                    Prim(root, PrimitiveType.Capsule, new Vector3(0f, -0.01f, 0.21f) * u, new Vector3(0.05f, 0.09f, 0.05f) * u,
                         accent, SurfaceType.Plastic, new Vector3(90f, 0f, 0f));
                    break;
                case 3: // pointy (three shrinking disks)
                    for (int i = 0; i < 3; i++)
                        Prim(root, PrimitiveType.Cylinder, new Vector3(0f, -0.01f, 0.17f + i * 0.04f) * u,
                             new Vector3(0.09f - i * 0.03f, 0.025f, 0.09f - i * 0.03f) * u, accent, SurfaceType.Plastic,
                             new Vector3(90f, 0f, 0f));
                    break;
                case 4: // clown
                    Prim(root, PrimitiveType.Sphere, new Vector3(0f, -0.01f, 0.18f) * u, Vector3.one * (0.10f * u), new Color(0.9f, 0.1f, 0.1f));
                    break;
            }
        }

        static void BuildExtra(Transform headRoot, Transform torsoRoot, int extra, Color accent, float u)
        {
            Color dark = new Color(0.08f, 0.08f, 0.1f);
            switch (extra)
            {
                case 1: // backpack
                    Prim(torsoRoot, PrimitiveType.Cube, new Vector3(0f, 0.03f, -0.19f) * u, new Vector3(0.26f, 0.30f, 0.14f) * u, accent);
                    for (int i = 0; i < 2; i++)
                        Prim(torsoRoot, PrimitiveType.Cube, new Vector3(i == 0 ? -0.1f : 0.1f, 0.10f, -0.10f) * u,
                             new Vector3(0.03f, 0.25f, 0.02f) * u, dark);
                    break;
                case 2: // cape
                    Prim(torsoRoot, PrimitiveType.Cube, new Vector3(0f, -0.04f, -0.14f) * u, new Vector3(0.34f, 0.5f, 0.02f) * u, accent);
                    break;
                case 3: // antenna
                    Prim(headRoot, PrimitiveType.Capsule, new Vector3(0f, 0.22f, 0f) * u, new Vector3(0.015f, 0.07f, 0.015f) * u,
                         new Color(0.5f, 0.5f, 0.55f), SurfaceType.Metal);
                    Prim(headRoot, PrimitiveType.Sphere, new Vector3(0f, 0.31f, 0f) * u, Vector3.one * (0.05f * u), accent);
                    break;
            }
        }
    }
}
