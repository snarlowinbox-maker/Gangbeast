using System.Collections.Generic;
using UnityEngine;
using BeefCity.Graphics;

#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
#else
using PhysMat = UnityEngine.PhysicMaterial;
#endif

namespace BeefCity.Core
{
    /// <summary>
    /// "Proving Ground": a flat concrete platform with hazard-stripe edges. In Soccer mode it becomes a pitch with
    /// goals and a ball. Everything is built from primitives and procedural materials.
    /// </summary>
    public static class DefaultArena
    {
        public const string Id = "proving_ground";

        public static void Register() => StageRegistry.Register(Id, "Proving Ground", Build);

        public static ArenaInfo Build(MatchSettings s)
        {
            var info = new ArenaInfo();
            var root = new GameObject("Arena_ProvingGround");
            info.root = root;
            bool soccer = s.mode == GameModeId.Soccer;

            float width = soccer ? 18f : 26f;
            float length = soccer ? 34f : 26f;

            EnsureLight(root.transform);

            // Floor (keeps its box collider)
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(width, 1f, length);
            AddSurface(floor, SurfaceType.Concrete, new Color(0.85f, 0.85f, 0.88f), 3f);

            // Hazard-stripe border (visual only)
            Edge(root.transform, new Vector3(0f, 0.02f, length * 0.5f - 0.25f), new Vector3(width, 0.04f, 0.5f));
            Edge(root.transform, new Vector3(0f, 0.02f, -length * 0.5f + 0.25f), new Vector3(width, 0.04f, 0.5f));
            Edge(root.transform, new Vector3(width * 0.5f - 0.25f, 0.02f, 0f), new Vector3(0.5f, 0.04f, length));
            Edge(root.transform, new Vector3(-width * 0.5f + 0.25f, 0.02f, 0f), new Vector3(0.5f, 0.04f, length));

            info.ringOutY = -8f;
            info.center = Vector3.zero;
            info.radius = Mathf.Min(width, length) * 0.5f;

            if (soccer) BuildPitch(info, root.transform, width, length);
            BuildSpawns(info, root.transform, s, soccer, width, length);
            return info;
        }

        static void BuildSpawns(ArenaInfo info, Transform root, MatchSettings s, bool soccer, float width, float length)
        {
            int teams = Mathf.Max(2, s.teamCount);
            info.teamSpawns = new List<Transform>[teams];
            for (int t = 0; t < teams; t++) info.teamSpawns[t] = new List<Transform>();

            if (soccer)
            {
                for (int t = 0; t < 2; t++)
                {
                    float z = t == 0 ? -length * 0.27f : length * 0.27f;
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i - 1.5f) * 3f;
                        Transform sp = Spawn(root, new Vector3(x, 0.05f, z), t == 0 ? 0f : 180f);
                        info.teamSpawns[t].Add(sp);
                        info.spawns.Add(sp);
                    }
                }
                return;
            }

            const int count = 8;
            float r = info.radius * 0.55f;
            for (int i = 0; i < count; i++)
            {
                float ang = i * Mathf.PI * 2f / count;
                Vector3 pos = new Vector3(Mathf.Sin(ang) * r, 0.05f, Mathf.Cos(ang) * r);
                Transform sp = Spawn(root, pos, Quaternion.LookRotation(-pos.normalized).eulerAngles.y);
                info.spawns.Add(sp);
                info.teamSpawns[(i * teams / count) % teams].Add(sp); // each team gets a slice of the circle
            }
        }

        static Transform Spawn(Transform root, Vector3 pos, float yaw)
        {
            var go = new GameObject("Spawn");
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        static void BuildPitch(ArenaInfo info, Transform root, float width, float length)
        {
            // Side walls keep the ball in play.
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -width * 0.5f : width * 0.5f;
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "SideWall";
                wall.transform.SetParent(root, false);
                wall.transform.localPosition = new Vector3(x, 1.5f, 0f);
                wall.transform.localScale = new Vector3(0.4f, 3f, length);
                AddSurface(wall, SurfaceType.Metal, new Color(0.7f, 0.75f, 0.8f), 3f);
            }

            // Goals: team 0 attacks +z, team 1 attacks -z.
            for (int i = 0; i < 2; i++)
            {
                float dir = i == 0 ? 1f : -1f;
                float mouth = dir * (length * 0.5f - 3f);
                float back = dir * (length * 0.5f - 0.8f);
                Color post = Color.white;

                Post(root, new Vector3(-3.2f, 1.5f, mouth), new Vector3(0.3f, 3f, 0.3f), post);
                Post(root, new Vector3(3.2f, 1.5f, mouth), new Vector3(0.3f, 3f, 0.3f), post);
                Post(root, new Vector3(0f, 3f, mouth), new Vector3(6.7f, 0.3f, 0.3f), post);

                GameObject net = GameObject.CreatePrimitive(PrimitiveType.Cube);
                net.name = "GoalBack";
                net.transform.SetParent(root, false);
                net.transform.localPosition = new Vector3(0f, 1.5f, back);
                net.transform.localScale = new Vector3(6.4f, 3f, 0.3f);
                AddSurface(net, SurfaceType.Metal, new Color(0.5f, 0.55f, 0.6f), 2f);

                var trig = new GameObject("GoalTrigger");
                trig.transform.SetParent(root, false);
                trig.transform.localPosition = new Vector3(0f, 1.5f, (mouth + back) * 0.5f);
                BoxCollider bc = trig.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = new Vector3(6f, 3f, Mathf.Abs(back - mouth));
                GoalTrigger gt = trig.AddComponent<GoalTrigger>();
                gt.Arena = info;
                gt.ScoringTeam = i; // the ball at the +z goal scores for team 0
            }

            // Ball
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "SoccerBall";
            ball.transform.SetParent(root, false);
            ball.transform.localPosition = new Vector3(0f, 1f, 0f);
            ball.transform.localScale = Vector3.one * 1.1f;
            AddSurface(ball, SurfaceType.Plastic, new Color(0.95f, 0.95f, 0.95f), 1f);
            Rigidbody rb = ball.AddComponent<Rigidbody>();
            rb.mass = 1.5f;
            rb.SetDampingCompat(0.05f, 0.2f);
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            ball.GetComponent<Collider>().sharedMaterial = new PhysMat("Ball")
            {
                bounciness = 0.6f,
                dynamicFriction = 0.4f,
                staticFriction = 0.4f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                hideFlags = HideFlags.DontSave
            };
            info.ball = rb;
            info.ballSpawn = Spawn(root, new Vector3(0f, 1f, 0f), 0f);
        }

        static void Post(Transform root, Vector3 pos, Vector3 scale, Color color)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "GoalPost";
            p.transform.SetParent(root, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            AddSurface(p, SurfaceType.Metal, color, 1f);
        }

        static void Edge(Transform root, Vector3 pos, Vector3 scale)
        {
            GameObject e = GameObject.CreatePrimitive(PrimitiveType.Cube);
            e.name = "Edge";
            Object.Destroy(e.GetComponent<Collider>());
            e.transform.SetParent(root, false);
            e.transform.localPosition = pos;
            e.transform.localScale = scale;
            AddSurface(e, SurfaceType.HazardStripes, Color.white, 2f);
        }

        static void AddSurface(GameObject go, SurfaceType type, Color tint, float tileSize)
        {
            ProceduralSurface ps = go.AddComponent<ProceduralSurface>();
            ps.type = type;
            ps.tint = tint;
            ps.tileSize = tileSize;
            ps.Apply();
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

        // Rigidbody damping got renamed in Unity 6; keep one call site.
        static void SetDampingCompat(this Rigidbody rb, float linear, float angular)
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
}
