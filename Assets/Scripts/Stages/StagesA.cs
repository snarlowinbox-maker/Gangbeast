using System.Collections.Generic;
using UnityEngine;
using BeefCity.Core;
using BeefCity.Graphics;

namespace BeefCity.Stages
{
    /// <summary>Stages 1-8. Each builder returns an ArenaInfo; geometry is all primitives + procedural surfaces.</summary>
    public static class StagesA
    {
        static Color C(float r, float g, float b) => new Color(r, g, b);

        // ------------------------------------------------------------------ 1. Gondola

        public static ArenaInfo Gondola(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Gondola", -30f, new Vector3(0f, 10f, 0f), 11f, out Transform r);
            StageKit.Box(r, "Facade", new Vector3(0f, 8f, 5f), new Vector3(44f, 52f, 2f), SurfaceType.Concrete, C(0.6f, 0.63f, 0.7f), 4f);
            for (int i = 0; i < 7; i++)
                for (int j = 0; j < 5; j++)
                    StageKit.Box(r, "Window", new Vector3(-15f + i * 5f, -4f + j * 8f, 3.9f), new Vector3(3f, 4.5f, 0.2f),
                                 SurfaceType.Metal, C(0.5f, 0.65f, 0.8f), 2f, false);
            StageKit.Box(r, "RoofBeam", new Vector3(0f, 31f, 2f), new Vector3(30f, 1.2f, 3f), SurfaceType.Metal, C(0.5f, 0.5f, 0.55f), 3f);

            Rigidbody left = MakeGondola(r, new Vector3(-7f, 10f, 0f));
            Rigidbody right = MakeGondola(r, new Vector3(7f, 10f, 0f));

            // The plank joining them hangs on a hinge at each end.
            GameObject plank = StageKit.Box(r, "Plank", new Vector3(0f, 10.35f, 0f), new Vector3(6.4f, 0.25f, 1.3f), SurfaceType.Wood, Color.white, 2f);
            StageKit.Dynamic(plank, 20f);
            for (int i = 0; i < 2; i++)
            {
                HingeJoint h = plank.AddComponent<HingeJoint>();
                h.connectedBody = i == 0 ? left : right;
                h.autoConfigureConnectedAnchor = true;
                h.anchor = new Vector3(i == 0 ? -0.5f : 0.5f, 0f, 0f); // local units: +-0.5 = the plank's ends
                h.axis = Vector3.forward;
            }

            StageKit.SpawnLine(info, r, new Vector3(-9f, 10.5f, -0.6f), new Vector3(-5f, 10.5f, 0.6f), 4, 90f);
            StageKit.SpawnLine(info, r, new Vector3(5f, 10.5f, -0.6f), new Vector3(9f, 10.5f, 0.6f), 4, 270f);
            StageKit.Skyline(r, 70f, 14, 3, 15f, 45f);
            return StageKit.Finish(info, s);
        }

        static Rigidbody MakeGondola(Transform r, Vector3 center)
        {
            GameObject floor = StageKit.Box(r, "Gondola", center, new Vector3(8f, 0.4f, 3.4f), SurfaceType.Metal, C(0.85f, 0.85f, 0.3f), 2f);
            Color rail = C(0.7f, 0.7f, 0.75f);
            StageKit.BoxOn(floor, "RailBack", new Vector3(0f, 0.7f, 1.55f), new Vector3(8f, 1.0f, 0.12f), SurfaceType.Metal, rail, 1f);
            StageKit.BoxOn(floor, "RailL", new Vector3(-3.9f, 0.7f, 0f), new Vector3(0.12f, 1.0f, 3.4f), SurfaceType.Metal, rail, 1f);
            StageKit.BoxOn(floor, "RailR", new Vector3(3.9f, 0.7f, 0f), new Vector3(0.12f, 1.0f, 3.4f), SurfaceType.Metal, rail, 1f);
            Rigidbody rb = StageKit.Dynamic(floor, 260f, 0.6f, 1.2f);
            rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
            foreach (float x in new[] { -3.9f, 3.9f })
                // Anchor is in the floor's local units (scale 8 x 0.4 x 3.4): the top of the end rail, 1.2 m above the floor center.
                StageKit.Cable(r, new Vector3(center.x + x, 31f, 0f), rb, new Vector3(x / 8f, 3f, 0f), 8f);
            return rb;
        }

        // ------------------------------------------------------------------ 2. Grind

        public static ArenaInfo Grind(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Grind", -9f, Vector3.zero, 14f, out Transform r);
            const float R = 6f, W = 3f, offset = 6.5f;
            Vector3[] centers = { new Vector3(-offset, 0f, 0f), new Vector3(offset, 0f, 0f) };
            const int n = 28;

            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 c = centers[ring];
                Vector3 other = centers[1 - ring];
                float chord = 2f * R * Mathf.Sin(Mathf.PI / n) * 1.06f;
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2f / n;
                    Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 mid = c + radial * R;
                    StageKit.Box(r, "Walkway", mid + Vector3.down * 0.25f, new Vector3(chord, 0.5f, W), SurfaceType.Metal, C(0.55f, 0.58f, 0.62f), 2f,
                                 true, Quaternion.LookRotation(radial).eulerAngles);

                    bool nearCrossing = Vector3.Distance(mid, other) < R + 2.2f;
                    if (nearCrossing) continue;
                    Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                    if (k % 7 != 3) // outer rail, with a few gaps
                        StageKit.Rail(r, mid + radial * (W * 0.5f) - tangent * (chord * 0.5f), mid + radial * (W * 0.5f) + tangent * (chord * 0.5f), 0.9f);
                    if (k % 2 == 0) // inner rails only on some sections
                        StageKit.Rail(r, mid - radial * (W * 0.5f) - tangent * (chord * 0.5f), mid - radial * (W * 0.5f) + tangent * (chord * 0.5f), 0.9f);
                }

                // Grinder in the middle of each loop, and grinders on the outside.
                MakeGrinder(r, c + Vector3.down * 0.3f, 4.2f, ring == 0 ? 120f : -120f);
                MakeGrinder(r, c + new Vector3(ring == 0 ? -(R + W + 2.2f) : (R + W + 2.2f), -0.3f, 0f), 3.4f, ring == 0 ? -140f : 140f);
                MakeGrinder(r, c + new Vector3(0f, -0.3f, R + W + 2.2f), 3.4f, 140f);
                MakeGrinder(r, c + new Vector3(0f, -0.3f, -(R + W + 2.2f)), 3.4f, -140f);
            }

            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 c = centers[ring];
                for (int i = 0; i < 4; i++)
                {
                    float a = (ring == 0 ? 90f : -90f) + (i - 1.5f) * 50f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * R;
                    StageKit.Spawn(info, r, p + Vector3.up * 0.1f, 0f);
                }
            }
            StageKit.Skyline(r, 80f, 12, 5, 5f, 30f);
            return StageKit.Finish(info, s);
        }

        static void MakeGrinder(Transform r, Vector3 pos, float diameter, float degPerSec)
        {
            GameObject g = StageKit.Cyl(r, "Grinder", pos + Vector3.up * 1.2f, diameter, 2.6f, SurfaceType.Metal, C(0.4f, 0.4f, 0.45f));
            StageKit.Kinematic(g);
            Rotator rot = g.AddComponent<Rotator>();
            rot.DegreesPerSecond = degPerSec;
            for (int i = 0; i < 4; i++) // teeth: bars crossing the drum, spun 45 degrees apart
                StageKit.BoxOn(g, "Tooth", Vector3.zero, new Vector3(diameter * 1.25f, 2.2f, 0.45f), SurfaceType.Metal, C(0.8f, 0.3f, 0.2f), 1f, false,
                               new Vector3(0f, i * 45f, 0f));
            StageKit.Kill(r, "GrinderKill", pos + Vector3.up * 1.2f, new Vector3(diameter + 0.8f, 3f, diameter + 0.8f), true);
        }

        // ------------------------------------------------------------------ 3. Incinerator

        public static ArenaInfo Incinerator(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Incinerator", -9f, new Vector3(0f, 2f, 0f), 13f, out Transform r);
            StageKit.Box(r, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(26f, 1f, 22f), SurfaceType.Concrete, C(0.6f, 0.58f, 0.56f), 3f);
            // Upper level and ramp
            StageKit.Box(r, "Upper", new Vector3(-8f, 3.2f, -4f), new Vector3(9f, 0.6f, 10f), SurfaceType.Metal, C(0.5f, 0.52f, 0.56f), 2f);
            StageKit.Box(r, "Ramp", new Vector3(-8f, 1.5f, 4.9f), new Vector3(5f, 0.5f, 8.1f), SurfaceType.Metal, C(0.5f, 0.52f, 0.56f), 2f, true, new Vector3(24f, 0f, 0f));
            StageKit.Rail(r, new Vector3(-12.5f, 3.5f, -9f), new Vector3(-12.5f, 3.5f, 1f), 0.9f);
            StageKit.Rail(r, new Vector3(-12.5f, 3.5f, -9f), new Vector3(-3.5f, 3.5f, -9f), 0.9f);

            // Conveyor belt toward the furnace
            GameObject belt = StageKit.Box(r, "Belt", new Vector3(5f, 0.15f, 0f), new Vector3(4f, 0.3f, 20f), SurfaceType.Rubber, C(0.25f, 0.25f, 0.28f), 2f);
            ConveyorBelt cb = belt.AddComponent<ConveyorBelt>();
            cb.Direction = Vector3.forward;
            cb.Speed = 2.6f;

            // Furnace pit at the far end with short guard rails
            StageKit.Box(r, "FurnaceBack", new Vector3(5f, 2.5f, 12.4f), new Vector3(8f, 5f, 1f), SurfaceType.Concrete, C(0.35f, 0.3f, 0.3f), 3f);
            StageKit.Box(r, "Fire", new Vector3(5f, -0.6f, 11f), new Vector3(4f, 0.4f, 3f), SurfaceType.Plastic, C(1f, 0.45f, 0.08f), 1f, false);
            StageKit.Kill(r, "FurnaceKill", new Vector3(5f, 0.6f, 11f), new Vector3(4.2f, 2.2f, 3.2f), true);
            StageKit.Rail(r, new Vector3(3f, 0f, 9.4f), new Vector3(7f, 0f, 9.4f), 0.5f);
            var glow = new GameObject("FireLight");
            glow.transform.SetParent(r, false);
            glow.transform.localPosition = new Vector3(5f, 1.5f, 10f);
            Light l = glow.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.5f, 0.15f);
            l.intensity = 6f;
            l.range = 14f;

            // Objects ride the belt toward the fire
            var spawner = new GameObject("BeltSpawner");
            spawner.transform.SetParent(r, false);
            spawner.transform.localPosition = new Vector3(5f, 1.2f, -9f);
            ObjectSpawner os = spawner.AddComponent<ObjectSpawner>();
            os.Interval = 3.5f;
            os.MaxAlive = 6;

            StageKit.SpawnLine(info, r, new Vector3(-11f, 3.8f, -8f), new Vector3(-5f, 3.8f, -8f), 3, 0f);
            StageKit.SpawnLine(info, r, new Vector3(-10f, 0.1f, -6f), new Vector3(-2f, 0.1f, -6f), 3, 0f);
            StageKit.SpawnLine(info, r, new Vector3(-10f, 0.1f, 3f), new Vector3(-2f, 0.1f, 3f), 2, 90f);
            StageKit.Skyline(r, 70f, 10, 9, 5f, 25f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 4. Trucks

        public static ArenaInfo Trucks(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Trucks", -8f, new Vector3(0f, 3.2f, 0f), 12f, out Transform r);
            // Road is visual only so a fall is a real fall.
            StageKit.Box(r, "Road", new Vector3(0f, -1f, 0f), new Vector3(34f, 0.5f, 140f), SurfaceType.Concrete, C(0.25f, 0.25f, 0.28f), 6f, false);
            for (int i = 0; i < 6; i++)
            {
                GameObject dash = StageKit.Box(r, "LaneDash", new Vector3(0f, -0.7f, -35f + i * 14f), new Vector3(0.4f, 0.05f, 6f), SurfaceType.Plastic, Color.white, 1f, false);
                Scroller sc = dash.AddComponent<Scroller>();
                sc.Velocity = new Vector3(0f, 0f, -14f);
                sc.HalfLength = 42f;
            }

            for (int t = 0; t < 2; t++)
            {
                float x = t == 0 ? -5.2f : 5.2f;
                GameObject deck = StageKit.Box(r, "TruckDeck", new Vector3(x, 3f, 0f), new Vector3(4.4f, 0.4f, 15f), SurfaceType.Metal, t == 0 ? C(0.8f, 0.25f, 0.2f) : C(0.2f, 0.4f, 0.8f), 3f);
                StageKit.BoxOn(deck, "Cab", new Vector3(0f, 1.9f, 6.4f), new Vector3(4.2f, 3.4f, 2.2f), SurfaceType.Metal, C(0.8f, 0.8f, 0.85f), 2f);
                StageKit.BoxOn(deck, "Windshield", new Vector3(0f, 2.4f, 5.28f), new Vector3(3.6f, 1.3f, 0.06f), SurfaceType.Plastic, C(0.5f, 0.7f, 0.85f), 1f, false);
                StageKit.BoxOn(deck, "Chassis", new Vector3(0f, -1.0f, 0f), new Vector3(4.0f, 1.4f, 13f), SurfaceType.Rubber, C(0.15f, 0.15f, 0.17f), 2f, false);
                StageKit.Kinematic(deck);
                MovingPlatform mp = deck.AddComponent<MovingPlatform>();
                mp.Travel = new Vector3(t == 0 ? 0.6f : -0.6f, 0f, 0f);
                mp.Period = 4.5f;
                mp.Pause = 0f;
                mp.RockAxis = Vector3.forward;
                mp.RockDegrees = 1.4f;
                mp.RockPeriod = 5f + t;
            }

            // Overhead sign to duck under, and a low barrier to jump over, scrolling past forever.
            MakeGantry(r, 0f, true);
            MakeGantry(r, 35f, false);

            for (int i = 0; i < 4; i++)
            {
                StageKit.Spawn(info, r, new Vector3(-5.2f + (i % 2 == 0 ? -1f : 1f), 3.3f, -4f + (i / 2) * 4f), 0f);
                StageKit.Spawn(info, r, new Vector3(5.2f + (i % 2 == 0 ? -1f : 1f), 3.3f, -4f + (i / 2) * 4f), 0f);
            }
            return StageKit.Finish(info, s);
        }

        static void MakeGantry(Transform r, float z, bool overhead)
        {
            var root = new GameObject(overhead ? "SignGantry" : "Barrier");
            root.transform.SetParent(r, false);
            root.transform.localPosition = new Vector3(0f, 0f, z);
            if (overhead)
            {
                StageKit.Box(root.transform, "PostL", new Vector3(-11f, 3.6f, 0f), new Vector3(0.6f, 7f, 0.6f), SurfaceType.Metal, C(0.5f, 0.5f, 0.55f), 2f);
                StageKit.Box(root.transform, "PostR", new Vector3(11f, 3.6f, 0f), new Vector3(0.6f, 7f, 0.6f), SurfaceType.Metal, C(0.5f, 0.5f, 0.55f), 2f);
                StageKit.Box(root.transform, "Sign", new Vector3(0f, 5.45f, 0f), new Vector3(22f, 1.2f, 0.4f), SurfaceType.Metal, C(0.1f, 0.5f, 0.3f), 3f);
            }
            else
            {
                StageKit.Box(root.transform, "Jersey", new Vector3(0f, 3.75f, 0f), new Vector3(22f, 1.1f, 0.8f), SurfaceType.Concrete, C(0.85f, 0.85f, 0.85f), 2f);
            }
            StageKit.Kinematic(root);
            Scroller sc = root.AddComponent<Scroller>();
            sc.Velocity = new Vector3(0f, 0f, -12f);
            sc.HalfLength = 35f;
        }

        // ------------------------------------------------------------------ 5. Containers

        public static ArenaInfo Containers(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Containers", -14f, new Vector3(0f, 12f, 0f), 10f, out Transform r);
            StageKit.Box(r, "CraneBeam", new Vector3(0f, 30f, 0f), new Vector3(24f, 1.2f, 14f), SurfaceType.Metal, C(0.9f, 0.7f, 0.15f), 3f);
            Color[] colors = { C(0.75f, 0.2f, 0.15f), C(0.2f, 0.4f, 0.75f) };
            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? -4.2f : 4.2f;
                Vector3 pos = new Vector3(0f, 12f, z);
                GameObject box = StageKit.Box(r, "Container", pos, new Vector3(12f, 2.6f, 2.8f), SurfaceType.Metal, colors[i], 2f);
                for (int k = 0; k < 8; k++) // corrugation ribs (visual)
                    StageKit.Box(box.transform, "Rib", new Vector3(-0.45f + k * 0.128f, 0f, 0f), new Vector3(0.03f, 1.02f, 1.02f), SurfaceType.Metal, colors[i] * 0.85f, 1f, false);
                Rigidbody rb = StageKit.Dynamic(box, 700f, 0.5f, 1.5f);
                rb.centerOfMass = new Vector3(0f, -0.2f, 0f);
                foreach (float x in new[] { -6f, 6f }) // cables attach on the long axis, so the container can roll like a spit
                    StageKit.Cable(r, new Vector3(x, 29.5f, z), rb, new Vector3(x / 12f, 0f, 0f), 10f);
                Rocker rocker = box.AddComponent<Rocker>();
                rocker.Axis = Vector3.right;
                rocker.Degrees = 22f;
                rocker.Period = i == 0 ? 9f : 11f;
                rocker.Stiffness = 5f;
            }
            StageKit.SpawnLine(info, r, new Vector3(-4f, 13.5f, -4.2f), new Vector3(4f, 13.5f, -4.2f), 4, 0f);
            StageKit.SpawnLine(info, r, new Vector3(-4f, 13.5f, 4.2f), new Vector3(4f, 13.5f, 4.2f), 4, 180f);
            StageKit.Skyline(r, 70f, 12, 11, 8f, 38f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 6. Wheel

        public static ArenaInfo Wheel(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Wheel", -9f, new Vector3(0f, 6f, 0f), 12f, out Transform r);
            Vector3 hub = new Vector3(0f, 13f, 0f);
            const float radius = 9f;

            // Visual wheel
            GameObject wheelVisual = new GameObject("WheelVisual");
            wheelVisual.transform.SetParent(r, false);
            wheelVisual.transform.localPosition = hub;
            wheelVisual.AddComponent<Rotator>().Axis = Vector3.forward;
            wheelVisual.GetComponent<Rotator>().DegreesPerSecond = -12f;
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f;
                StageKit.Box(wheelVisual.transform, "Spoke", Vector3.zero, new Vector3(0.3f, radius * 2f, 0.3f), SurfaceType.Metal, C(0.85f, 0.85f, 0.9f), 2f, false, new Vector3(0f, 0f, a));
            }
            StageKit.Box(r, "SupportL", new Vector3(-3.5f, 5.5f, -2.5f), new Vector3(0.7f, 14f, 0.7f), SurfaceType.Metal, C(0.4f, 0.4f, 0.45f), 2f, false, new Vector3(0f, 0f, 14f));
            StageKit.Box(r, "SupportR", new Vector3(3.5f, 5.5f, -2.5f), new Vector3(0.7f, 14f, 0.7f), SurfaceType.Metal, C(0.4f, 0.4f, 0.45f), 2f, false, new Vector3(0f, 0f, -14f));

            const int count = 6;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 360f / count;
                Vector3 start = hub + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0f) * radius;

                var hanger = new GameObject("Hanger");
                hanger.transform.SetParent(r, false);
                hanger.transform.position = start;
                Rigidbody hangerBody = StageKit.Kinematic(hanger);
                OrbitMover orbit = hanger.AddComponent<OrbitMover>();
                orbit.Center = hub;
                orbit.Radius = radius;
                orbit.DegreesPerSecond = -12f;
                orbit.StartAngle = angle;

                // The gondola hangs below its pivot and tilts with the weight of whoever is inside.
                GameObject floor = StageKit.Box(r, "GondolaFloor", start + Vector3.down * 1.8f, new Vector3(4.6f, 0.3f, 3f), SurfaceType.Plastic, C(0.9f, 0.3f + 0.1f * i, 0.4f), 2f);
                StageKit.Box(floor.transform, "RailBack", new Vector3(0f, 2.2f, 0.46f), new Vector3(1f, 3f, 0.06f), SurfaceType.Metal, C(0.8f, 0.8f, 0.85f), 1f);
                StageKit.Box(floor.transform, "RailL", new Vector3(-0.47f, 2.2f, 0f), new Vector3(0.05f, 3f, 1f), SurfaceType.Metal, C(0.8f, 0.8f, 0.85f), 1f);
                StageKit.Box(floor.transform, "RailR", new Vector3(0.47f, 2.2f, 0f), new Vector3(0.05f, 3f, 1f), SurfaceType.Metal, C(0.8f, 0.8f, 0.85f), 1f);
                Rigidbody fb = StageKit.Dynamic(floor, 120f, 0.4f, 1f);
                HingeJoint hinge = floor.AddComponent<HingeJoint>();
                hinge.connectedBody = hangerBody;
                hinge.autoConfigureConnectedAnchor = true;
                hinge.anchor = new Vector3(0f, 6f, 0f); // local units: 6 x 0.3 scale = pivot 1.8 m above the floor
                hinge.axis = Vector3.forward;
                hinge.useSpring = true;
                hinge.spring = new JointSpring { spring = 500f, damper = 160f, targetPosition = 0f };
                hinge.useLimits = true;
                hinge.limits = new JointLimits { min = -35f, max = 35f };

                StageKit.Spawn(info, r, start + Vector3.down * 1.55f, 0f);
            }

            // Pier below with loose boards, over the water
            for (int i = 0; i < 26; i++)
            {
                GameObject plank = StageKit.Box(r, "PierBoard", new Vector3(-12.5f + i * 1f, -0.1f, 0f), new Vector3(0.96f, 0.25f, 4f), SurfaceType.Wood, Color.white, 2f);
                Breakable br = plank.AddComponent<Breakable>();
                br.Health = 5f;
                br.FallWhenBroken = true;
                br.FallMass = 8f;
            }
            StageKit.Water(r, new Vector3(0f, 0f, 0f), new Vector2(120f, 120f), -2.5f);
            StageKit.Spawn(info, r, new Vector3(-11f, 0.2f, 0f), 90f);
            StageKit.Spawn(info, r, new Vector3(11f, 0.2f, 0f), 270f);
            StageKit.Skyline(r, 80f, 10, 13, 5f, 30f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 7. Fans

        public static ArenaInfo Fans(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Fans", -10f, Vector3.zero, 13f, out Transform r);
            const float tile = 3f;
            for (int i = -4; i <= 4; i++)
            {
                for (int j = -4; j <= 4; j++)
                {
                    bool bigHole = Mathf.Abs(i) <= 1 && Mathf.Abs(j) <= 1;
                    bool smallHole = Mathf.Abs(i) == 3 && Mathf.Abs(j) == 3;
                    if (bigHole || smallHole) continue;
                    StageKit.Box(r, "Tile", new Vector3(i * tile, -0.4f, j * tile), new Vector3(tile, 0.8f, tile), SurfaceType.Metal, C(0.5f, 0.54f, 0.58f), 3f);
                }
            }

            MakeFan(r, Vector3.zero, 9f, 0f);
            // Breakable bridge over the largest fan
            for (int k = 0; k < 9; k++)
            {
                GameObject plank = StageKit.Box(r, "BridgePlank", new Vector3(-4f + k * 1f, -0.1f, 0f), new Vector3(0.96f, 0.25f, 1.8f), SurfaceType.Wood, Color.white, 2f);
                Breakable br = plank.AddComponent<Breakable>();
                br.Health = 6f;
                br.FallWhenBroken = true;
            }
            int fan = 1;
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    MakeFan(r, new Vector3(sx * 3 * tile, 0f, sz * 3 * tile), 3f, 1.7f * fan++);

            StageKit.SpawnCircle(info, r, Vector3.zero, 11f, 0.1f, 8);
            StageKit.Skyline(r, 80f, 10, 17, 5f, 25f);
            return StageKit.Finish(info, s);
        }

        static void MakeFan(Transform r, Vector3 center, float holeSize, float phase)
        {
            GameObject rotor = new GameObject("FanRotor");
            rotor.transform.SetParent(r, false);
            rotor.transform.localPosition = center + Vector3.down * 2f;
            Rotator rot = rotor.AddComponent<Rotator>();
            rot.Axis = Vector3.up;
            rot.DegreesPerSecond = 360f;
            for (int i = 0; i < 4; i++)
                StageKit.Box(rotor.transform, "Blade", Vector3.zero, new Vector3(holeSize * 0.95f, 0.1f, holeSize * 0.12f), SurfaceType.Metal, C(0.25f, 0.27f, 0.3f), 1f, false, new Vector3(0f, i * 45f, 0f));

            KillZone blades = StageKit.Kill(r, "FanBlades", center + Vector3.down * 1.9f, new Vector3(holeSize, 0.8f, holeSize), false);

            var zone = new GameObject("FanZone");
            zone.transform.SetParent(r, false);
            zone.transform.localPosition = center + Vector3.up * 1.5f;
            BoxCollider bc = zone.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(holeSize, 7f, holeSize);
            FanZone fz = zone.AddComponent<FanZone>();
            fz.Offset = phase;
            fz.Blades = blades;
            fz.Rotor = rot;
        }

        // ------------------------------------------------------------------ 8. Chutes

        public static ArenaInfo Chutes(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Chutes", -9f, Vector3.zero, 13f, out Transform r);
            StageKit.Box(r, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(28f, 1f, 28f), SurfaceType.Concrete, C(0.62f, 0.64f, 0.66f), 3f);

            var points = new List<Vector3>();
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f + 0.3f;
                Vector3 p = new Vector3(Mathf.Cos(a) * 6.5f, 0f, Mathf.Sin(a) * 6.5f);
                points.Add(p);
                StageKit.Cyl(r, "ChuteGrate", p + Vector3.up * 0.02f, 2.6f, 0.06f, SurfaceType.Metal, C(0.2f, 0.2f, 0.22f), false);
                StageKit.Cyl(r, "ChutePipe", p + Vector3.up * 12.5f, 2.4f, 2f, SurfaceType.Metal, C(0.5f, 0.2f, 0.4f), false);
            }
            points.Add(Vector3.zero);

            var dropper = new GameObject("GooDropper");
            dropper.transform.SetParent(r, false);
            GooDropper gd = dropper.AddComponent<GooDropper>();
            gd.Points = points.ToArray();

            StageKit.SpawnCircle(info, r, Vector3.zero, 10f, 0.1f, 8);
            StageKit.Skyline(r, 80f, 10, 19, 5f, 25f);
            return StageKit.Finish(info, s);
        }
    }
}
