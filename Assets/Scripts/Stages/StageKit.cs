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
    /// <summary>Small building blocks so each stage is a short list of "put this here" calls.</summary>
    public static class StageKit
    {
        public static Color C(float r, float g, float b) => new Color(r, g, b);

        // ------------------------------------------------------------------ stage frame

        public static ArenaInfo Begin(string name, float ringOutY, Vector3 center, float radius, out Transform root)
        {
            var go = new GameObject("Stage_" + name);
            root = go.transform;
            EnsureLight(root);
            return new ArenaInfo { root = go, ringOutY = ringOutY, center = center, radius = radius };
        }

        static void EnsureLight(Transform root)
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.gameObject.activeInHierarchy) return;

            var go = new GameObject("Sun");
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);
        }

        // ------------------------------------------------------------------ geometry

        public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size,
                                     SurfaceType surface = SurfaceType.Concrete, Color? tint = null, float tile = 2f,
                                     bool collider = true, Vector3? euler = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) DropCollider(go);
            Place(go, parent, pos, size, euler);
            Paint(go, surface, tint ?? Color.white, tile);
            return go;
        }

        /// <summary>
        /// A box attached to a (possibly non-uniformly scaled) parent, positioned and sized in WORLD units relative to
        /// the parent's center, so rails, ribs and cabins come out the size you typed. Avoid rotating these.
        /// </summary>
        public static GameObject BoxOn(GameObject parent, string name, Vector3 worldOffset, Vector3 worldSize,
                                       SurfaceType surface = SurfaceType.Concrete, Color? tint = null, float tile = 2f,
                                       bool collider = true, Vector3? euler = null)
        {
            Vector3 s = parent.transform.lossyScale;
            Vector3 localPos = new Vector3(worldOffset.x / s.x, worldOffset.y / s.y, worldOffset.z / s.z);
            Vector3 localSize = new Vector3(worldSize.x / s.x, worldSize.y / s.y, worldSize.z / s.z);
            return Box(parent.transform, name, localPos, localSize, surface, tint, tile, collider, euler);
        }

        /// <summary>Cylinder with a proper convex mesh collider (Unity's primitive uses a capsule, which is wrong for discs).</summary>
        public static GameObject Cyl(Transform parent, string name, Vector3 pos, float diameter, float height,
                                     SurfaceType surface = SurfaceType.Concrete, Color? tint = null, bool collider = true,
                                     Vector3? euler = null, float tile = 2f)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            DropCollider(go);
            if (collider)
            {
                MeshCollider mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                mc.convex = true;
            }
            Place(go, parent, pos, new Vector3(diameter, height * 0.5f, diameter), euler);
            Paint(go, surface, tint ?? Color.white, tile);
            return go;
        }

        public static GameObject Ball(Transform parent, string name, Vector3 pos, float diameter,
                                      SurfaceType surface = SurfaceType.Plastic, Color? tint = null, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            if (!collider) DropCollider(go);
            Place(go, parent, pos, Vector3.one * diameter, null);
            Paint(go, surface, tint ?? Color.white, 2f);
            return go;
        }

        static void DropCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c == null) return;
            c.enabled = false;
            Object.Destroy(c);
        }

        static void Place(GameObject go, Transform parent, Vector3 pos, Vector3 scale, Vector3? euler)
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
        }

        static void Paint(GameObject go, SurfaceType surface, Color tint, float tile)
        {
            ProceduralMaterialGenerator.Apply(go.GetComponent<Renderer>(), surface, tint,
                ProceduralMaterialGenerator.AutoTiling(go.transform.lossyScale, tile));
        }

        public static Rigidbody Dynamic(GameObject go, float mass, float linearDamping = 0.2f, float angularDamping = 0.5f)
        {
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.solverIterations = 16;
            rb.SetDamping(linearDamping, angularDamping);
            return rb;
        }

        public static Rigidbody Kinematic(GameObject go)
        {
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return rb;
        }

        public static void Bouncy(GameObject go, float bounciness)
        {
            Collider c = go.GetComponent<Collider>();
            if (c == null) return;
            c.sharedMaterial = new PhysMat("Bouncy")
            {
                bounciness = bounciness,
                dynamicFriction = 0.3f,
                staticFriction = 0.3f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                hideFlags = HideFlags.DontSave
            };
        }

        // ------------------------------------------------------------------ rails and kill zones

        /// <summary>Two-bar guard rail with posts between two points (a to b), at the given height.</summary>
        public static void Rail(Transform parent, Vector3 a, Vector3 b, float height = 1f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.1f) return;
            Quaternion rot = Quaternion.LookRotation(d.normalized);
            Color rail = new Color(0.95f, 0.8f, 0.15f);
            Box(parent, "RailTop", (a + b) * 0.5f + Vector3.up * height, new Vector3(0.12f, 0.12f, len), SurfaceType.Metal, rail, 1f, true, rot.eulerAngles);
            Box(parent, "RailMid", (a + b) * 0.5f + Vector3.up * height * 0.5f, new Vector3(0.1f, 0.1f, len), SurfaceType.Metal, rail, 1f, true, rot.eulerAngles);
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 3f) + 1);
            for (int i = 0; i < posts; i++)
                Box(parent, "RailPost", Vector3.Lerp(a, b, i / (float)(posts - 1)) + Vector3.up * height * 0.5f,
                    new Vector3(0.14f, height, 0.14f), SurfaceType.Metal, rail, 1f, true);
        }

        public static KillZone Kill(Transform parent, string name, Vector3 center, Vector3 size, bool anyLimb = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = size;
            KillZone kz = go.AddComponent<KillZone>();
            kz.AnyLimb = anyLimb;
            return kz;
        }

        /// <summary>Sea / pit: a blue slab for looks plus a kill trigger just under the surface.</summary>
        public static void Water(Transform parent, Vector3 center, Vector2 size, float surfaceY)
        {
            Box(parent, "Water", new Vector3(center.x, surfaceY - 0.5f, center.z), new Vector3(size.x, 1f, size.y),
                SurfaceType.Plastic, C(0.12f, 0.38f, 0.7f), 6f, false);
            Kill(parent, "WaterKill", new Vector3(center.x, surfaceY - 0.3f, center.z), new Vector3(size.x, 1.6f, size.y), true);
        }

        // ------------------------------------------------------------------ cables

        /// <summary>
        /// Hangs a dynamic body from a fixed anchor point with a breakable cable. The joint locks the attachment point
        /// but leaves rotation free, so one broken cable tilts the body and two let it fall.
        /// </summary>
        public static Breakable Cable(Transform parent, Vector3 anchorWorld, Rigidbody body, Vector3 bodyLocalAnchor, float hp = 8f)
        {
            ConfigurableJoint joint = body.gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = bodyLocalAnchor;
            joint.connectedAnchor = anchorWorld; // no connected body: world space
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;

            GameObject cable = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cable.name = "Cable";
            cable.transform.SetParent(parent, false);
            ProceduralMaterialGenerator.Apply(cable.GetComponent<Renderer>(), SurfaceType.Metal, C(0.35f, 0.36f, 0.4f), Vector2.one);
            CableVisual cv = cable.AddComponent<CableVisual>();
            cv.Anchor = anchorWorld;
            cv.Body = body.transform;
            cv.LocalAnchor = bodyLocalAnchor;

            Breakable br = cable.AddComponent<Breakable>();
            br.Health = hp;
            br.Joints = new Joint[] { joint };
            br.Hide = new[] { cable };
            return br;
        }

        // ------------------------------------------------------------------ spawns

        public static Transform Spawn(ArenaInfo info, Transform parent, Vector3 pos, float yaw)
        {
            var go = new GameObject("Spawn");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            info.spawns.Add(go.transform);
            return go.transform;
        }

        public static void SpawnCircle(ArenaInfo info, Transform parent, Vector3 center, float radius, float y, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                Vector3 pos = new Vector3(center.x + Mathf.Sin(a) * radius, y, center.z + Mathf.Cos(a) * radius);
                Vector3 toCenter = new Vector3(center.x - pos.x, 0f, center.z - pos.z);
                Spawn(info, parent, pos, toCenter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCenter).eulerAngles.y : 0f);
            }
        }

        public static void SpawnLine(ArenaInfo info, Transform parent, Vector3 from, Vector3 to, int count, float yaw)
        {
            for (int i = 0; i < count; i++)
                Spawn(info, parent, Vector3.Lerp(from, to, count == 1 ? 0.5f : i / (float)(count - 1)), yaw);
        }

        /// <summary>Splits the spawn points into team slices (by angle around the center) for Gang mode.</summary>
        public static ArenaInfo Finish(ArenaInfo info, MatchSettings s)
        {
            int teams = Mathf.Max(2, s.teamCount);
            info.teamSpawns = new List<Transform>[teams];
            for (int t = 0; t < teams; t++) info.teamSpawns[t] = new List<Transform>();

            var sorted = new List<Transform>(info.spawns);
            sorted.Sort((a, b) => Angle(a, info.center).CompareTo(Angle(b, info.center)));
            for (int i = 0; i < sorted.Count; i++)
                info.teamSpawns[Mathf.Min(teams - 1, i * teams / Mathf.Max(1, sorted.Count))].Add(sorted[i]);
            return info;
        }

        static float Angle(Transform t, Vector3 center) => Mathf.Atan2(t.position.z - center.z, t.position.x - center.x);

        // ------------------------------------------------------------------ scenery

        /// <summary>Distant city towers for context (visual only).</summary>
        public static void Skyline(Transform parent, float radius, int count, int seed, float minTop, float maxTop)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count + (float)rng.NextDouble() * 0.2f;
                float top = minTop + (float)rng.NextDouble() * (maxTop - minTop);
                float w = 5f + (float)rng.NextDouble() * 7f;
                float bottom = -90f;
                float tint = 0.3f + (float)rng.NextDouble() * 0.2f;
                Box(parent, "Tower", new Vector3(Mathf.Cos(a) * radius, (top + bottom) * 0.5f, Mathf.Sin(a) * radius),
                    new Vector3(w, top - bottom, w), i % 3 == 0 ? SurfaceType.Metal : SurfaceType.Concrete,
                    C(tint, tint, tint * 1.15f), 6f, false);
            }
        }

        public static GameObject Car(Vector3 position, Color color)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Car";
            body.transform.position = position;
            body.transform.localScale = new Vector3(1.8f, 0.8f, 3.8f);
            Paint(body, SurfaceType.Metal, color, 2f);
            BoxOn(body, "Cabin", new Vector3(0f, 0.7f, -0.2f), new Vector3(1.5f, 0.6f, 2f), SurfaceType.Plastic, C(0.7f, 0.85f, 0.95f), 1f, false);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.95f : 0.95f;
                float z = i < 2 ? -1.2f : 1.2f;
                BoxOn(body, "Wheel", new Vector3(x, -0.35f, z), new Vector3(0.25f, 0.6f, 0.6f), SurfaceType.Rubber, Color.white, 1f, false);
            }
            Dynamic(body, 350f, 0.1f, 0.3f);
            return body;
        }
    }
}
