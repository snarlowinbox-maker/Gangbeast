using System.Collections.Generic;
using UnityEngine;
using BeefCity.Core;
using BeefCity.Graphics;

namespace BeefCity.Stages
{
    /// <summary>Stages 9-16.</summary>
    public static class StagesB
    {
        static Color C(float r, float g, float b) => new Color(r, g, b);

        // ------------------------------------------------------------------ 9. Tower

        public static ArenaInfo Tower(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Tower", -16f, Vector3.zero, 12f, out Transform r);
            const int n = 32;
            const float outer = 13f, inner = 6f, width = outer - inner, mid = (outer + inner) * 0.5f;
            float chord = 2f * mid * Mathf.Sin(Mathf.PI / n) * 1.06f;

            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                StageKit.Box(r, "Floor", radial * mid + Vector3.down * 0.3f, new Vector3(chord, 0.6f, width), SurfaceType.Concrete, C(0.62f, 0.62f, 0.66f), 3f,
                             true, Quaternion.LookRotation(radial).eulerAngles);

                Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                float half = chord * 0.5f;
                if (k % 8 != 0) // rail around the central hole, with gaps
                    StageKit.Rail(r, radial * (inner + 0.2f) - tangent * half, radial * (inner + 0.2f) + tangent * half, 0.9f);
                if (k != 0 && k != 1 && k != n - 1 && k % 3 != 0) // outer rail, open where the staircase leaves
                    StageKit.Rail(r, radial * (outer - 0.2f) - tangent * half, radial * (outer - 0.2f) + tangent * half, 0.9f);
            }

            // Staircase down from the outer edge (east side)
            var stairs = new List<Transform>();
            for (int i = 0; i < 10; i++)
            {
                float top = -0.5f * (i + 1);
                GameObject step = StageKit.Box(r, "Step", new Vector3(13.5f + i, top - 0.25f, 0f), new Vector3(1f, 0.5f, 3f), SurfaceType.Concrete, C(0.7f, 0.7f, 0.74f), 2f);
                stairs.Add(step.transform);
            }
            var trigger = new GameObject("StairsTrigger");
            trigger.transform.SetParent(r, false);
            trigger.transform.localPosition = new Vector3(18f, -2f, 0f);
            BoxCollider bc = trigger.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(11f, 5f, 3.4f);
            CollapsingStairs cs = trigger.AddComponent<CollapsingStairs>();
            cs.Steps = stairs;
            cs.Delay = 7f;

            // Lower level at the bottom of the stairs
            StageKit.Box(r, "Basement", new Vector3(25f, -5.75f, 0f), new Vector3(9f, 0.5f, 9f), SurfaceType.Metal, C(0.45f, 0.5f, 0.55f), 3f);
            for (int i = 0; i < 3; i++)
                PropFactory.Spawn(i % 2 == 0 ? PropType.Box : PropType.Ball, new Vector3(24f + i * 1.5f, -4.5f, i - 1f)).transform.SetParent(r, true);

            StageKit.SpawnCircle(info, r, Vector3.zero, mid, 0.1f, 8);
            StageKit.Skyline(r, 85f, 12, 21, 5f, 40f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 10. Billboards

        public static ArenaInfo Billboards(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Billboards", -22f, new Vector3(0f, 14f, -2f), 14f, out Transform r);
            Color[] ads = { C(0.9f, 0.25f, 0.3f), C(0.2f, 0.55f, 0.9f), C(0.95f, 0.8f, 0.2f) };

            MakeBillboard(r, new Vector3(-13f, 14f, 0f), 10f, ads[0], false);
            MakeBillboard(r, new Vector3(13f, 14f, 0f), 10f, ads[2], false);
            GameObject mid = MakeBillboard(r, new Vector3(0f, 15f, 0f), 9f, ads[1], true);
            StageKit.Kinematic(mid);
            MovingPlatform mp = mid.AddComponent<MovingPlatform>();
            mp.Travel = new Vector3(0f, -3f, 0f);
            mp.Period = 5f;
            mp.Pause = 1f;
            MakeBillboard(r, new Vector3(0f, 14f, -6f), 7f, C(0.6f, 0.3f, 0.8f), false, false);

            // Sweeping sign arms over the outer billboards
            foreach (float x in new[] { -13f, 13f })
            {
                GameObject arm = StageKit.Box(r, "SweepArm", new Vector3(x, 15.4f, 0f), new Vector3(7f, 0.35f, 0.35f), SurfaceType.Metal, C(0.9f, 0.4f, 0.1f), 1f);
                StageKit.Kinematic(arm);
                Rotator rot = arm.AddComponent<Rotator>();
                rot.DegreesPerSecond = x < 0 ? 55f : -55f;
            }

            StageKit.SpawnLine(info, r, new Vector3(-16f, 14.4f, -0.5f), new Vector3(-10f, 14.4f, 0.5f), 3, 90f);
            StageKit.SpawnLine(info, r, new Vector3(10f, 14.4f, -0.5f), new Vector3(16f, 14.4f, 0.5f), 3, 270f);
            StageKit.SpawnLine(info, r, new Vector3(-2f, 14.4f, -6f), new Vector3(2f, 14.4f, -6f), 2, 0f);
            StageKit.Skyline(r, 75f, 14, 23, 10f, 50f);
            return StageKit.Finish(info, s);
        }

        static GameObject MakeBillboard(Transform r, Vector3 pos, float length, Color ad, bool carryFace, bool withFace = true)
        {
            GameObject deck = StageKit.Box(r, "BillboardDeck", pos, new Vector3(length, 0.6f, 3.2f), SurfaceType.Metal, C(0.5f, 0.52f, 0.56f), 2f);
            if (withFace)
            {
                StageKit.BoxOn(deck, "AdFace", new Vector3(0f, 2.9f, 1.55f), new Vector3(length, 5.2f, 0.3f), SurfaceType.Plastic, ad, 3f);
                StageKit.BoxOn(deck, "AdFrame", new Vector3(0f, 0.2f, 1.55f), new Vector3(length + 0.4f, 0.5f, 0.4f), SurfaceType.Metal, C(0.3f, 0.3f, 0.34f), 1f);
            }
            if (!carryFace) // fixed billboards stand on posts; the moving one hangs on its own
                for (int i = 0; i < 2; i++)
                    StageKit.Box(r, "Post", new Vector3(pos.x + (i == 0 ? -length * 0.4f : length * 0.4f), pos.y - 30f, pos.z + 1f), new Vector3(0.6f, 60f, 0.6f), SurfaceType.Metal, C(0.4f, 0.4f, 0.45f), 4f, false);
            return deck;
        }

        // ------------------------------------------------------------------ 11. Buoy

        public static ArenaInfo Buoy(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Buoy", -9f, Vector3.zero, 12f, out Transform r);
            StageKit.Water(r, Vector3.zero, new Vector2(140f, 140f), -2f);

            MakeBuoy(r, Vector3.zero, 9f, 2.5f, C(0.9f, 0.25f, 0.2f), true);
            MakeBuoy(r, new Vector3(-10.5f, 0f, 3f), 4.6f, 1.6f, C(0.95f, 0.8f, 0.2f), false);
            MakeBuoy(r, new Vector3(10.5f, 0f, -3f), 4.6f, 1.6f, C(0.95f, 0.8f, 0.2f), false);
            MakeBuoy(r, new Vector3(0f, 0f, -11f), 4.6f, 1.6f, C(0.2f, 0.5f, 0.9f), false);

            StageKit.SpawnCircle(info, r, Vector3.zero, 3.4f, 0.6f, 8);
            StageKit.Skyline(r, 90f, 8, 25, -5f, 18f);
            return StageKit.Finish(info, s);
        }

        static void MakeBuoy(Transform r, Vector3 pos, float diameter, float keelDepth, Color color, bool beacon)
        {
            GameObject b = StageKit.Cyl(r, "Buoy", pos, diameter, 1f, SurfaceType.Plastic, color, true, null, 3f);
            Rigidbody rb = StageKit.Dynamic(b, 300f + diameter * 20f, 1f, 2.5f);
            rb.centerOfMass = new Vector3(0f, -keelDepth, 0f); // heavy keel: it tilts under weight and rights itself

            ConfigurableJoint pivot = b.AddComponent<ConfigurableJoint>();
            pivot.autoConfigureConnectedAnchor = false;
            pivot.anchor = Vector3.zero;
            pivot.connectedAnchor = b.transform.position; // pinned to the world at its center, free to tilt
            pivot.xMotion = pivot.yMotion = pivot.zMotion = ConfigurableJointMotion.Locked;
            pivot.angularXMotion = pivot.angularYMotion = pivot.angularZMotion = ConfigurableJointMotion.Free;

            StageKit.BoxOn(b, "Band", new Vector3(0f, 0.15f, 0f), new Vector3(diameter * 0.98f, 0.1f, diameter * 0.98f), SurfaceType.Plastic, Color.white, 2f, false);
            if (beacon)
            {
                StageKit.BoxOn(b, "Mast", new Vector3(0f, 2.5f, 0f), new Vector3(0.3f, 4.5f, 0.3f), SurfaceType.Metal, C(0.5f, 0.5f, 0.55f), 1f, false);
                StageKit.BoxOn(b, "Lamp", new Vector3(0f, 5f, 0f), new Vector3(0.8f, 0.8f, 0.8f), SurfaceType.Plastic, C(1f, 0.9f, 0.4f), 1f, false);
            }
        }

        // ------------------------------------------------------------------ 12. Blimp

        public static ArenaInfo Blimp(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Blimp", -12f, new Vector3(0f, 20f, 0f), 10f, out Transform r);
            var blimp = new GameObject("Blimp");
            blimp.transform.SetParent(r, false);
            blimp.transform.localPosition = new Vector3(0f, 20f, 0f);

            StageKit.Box(blimp.transform, "Deck", new Vector3(0f, -0.3f, 0f), new Vector3(24f, 0.6f, 6f), SurfaceType.Metal, C(0.75f, 0.75f, 0.8f), 3f);
            StageKit.Ball(blimp.transform, "Envelope", new Vector3(0f, -4.4f, 0f), 1f, SurfaceType.Plastic, C(0.85f, 0.2f, 0.25f), false)
                .transform.localScale = new Vector3(34f, 8.4f, 9f);
            StageKit.Box(blimp.transform, "FinTop", new Vector3(-15f, -1.5f, 0f), new Vector3(3.5f, 4f, 0.25f), SurfaceType.Plastic, C(0.7f, 0.15f, 0.2f), 1f, false);
            StageKit.Box(blimp.transform, "FinSide", new Vector3(-15f, -4.4f, 0f), new Vector3(3.5f, 0.25f, 5.5f), SurfaceType.Plastic, C(0.7f, 0.15f, 0.2f), 1f, false);

            // Two stern propellers standing on the deck
            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? -2f : 2f;
                var prop = new GameObject("Propeller");
                prop.transform.SetParent(blimp.transform, false);
                prop.transform.localPosition = new Vector3(-9.8f, 2.3f, z);
                Rotator rot = prop.AddComponent<Rotator>();
                rot.Axis = Vector3.right;
                rot.DegreesPerSecond = 700f;
                for (int b = 0; b < 3; b++)
                    StageKit.Box(prop.transform, "Blade", Vector3.zero, new Vector3(0.12f, 4f, 0.5f), SurfaceType.Metal, C(0.3f, 0.3f, 0.35f), 1f, false, new Vector3(b * 60f, 0f, 0f));
                StageKit.Kill(blimp.transform, "PropKill", new Vector3(-9.8f, 2.3f, z), new Vector3(0.8f, 4.2f, 4.2f), false);
                StageKit.Box(blimp.transform, "PropGuardPost", new Vector3(-10.4f, 1f, z), new Vector3(0.5f, 2f, 0.5f), SurfaceType.Metal, C(0.4f, 0.4f, 0.45f), 1f);
            }

            StageKit.Kinematic(blimp);
            MovingPlatform mp = blimp.AddComponent<MovingPlatform>();
            mp.Travel = new Vector3(0f, 1.2f, 0f);
            mp.Period = 5f;
            mp.Pause = 0f;
            mp.RockAxis = Vector3.forward;
            mp.RockDegrees = 2.5f;
            mp.RockPeriod = 7f;

            StageKit.SpawnLine(info, r, new Vector3(-6f, 20.3f, -1.8f), new Vector3(9f, 20.3f, -1.8f), 4, 90f);
            StageKit.SpawnLine(info, r, new Vector3(-6f, 20.3f, 1.8f), new Vector3(9f, 20.3f, 1.8f), 4, 90f);
            StageKit.Skyline(r, 90f, 10, 27, -20f, 8f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 13. Girders

        public static ArenaInfo Girders(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Girders", -16f, new Vector3(0f, 12f, 0f), 12f, out Transform r);
            float[] lines = { -9f, -3f, 3f, 9f };
            Color steel = C(0.9f, 0.45f, 0.1f);
            foreach (float z in lines)
                StageKit.Box(r, "GirderX", new Vector3(0f, 12f, z), new Vector3(28f, 0.6f, 1.7f), SurfaceType.Metal, steel, 2f);
            foreach (float x in lines)
                StageKit.Box(r, "GirderZ", new Vector3(x, 12.02f, 0f), new Vector3(1.7f, 0.6f, 28f), SurfaceType.Metal, steel * 0.92f, 2f);
            foreach (float x in new[] { -13f, 13f })
                foreach (float z in new[] { -13f, 13f })
                    StageKit.Box(r, "Column", new Vector3(x, -30f, z), new Vector3(1.4f, 84f, 1.4f), SurfaceType.Metal, C(0.4f, 0.4f, 0.45f), 4f, false);

            // Two sliding girders that sweep across the grid
            for (int i = 0; i < 2; i++)
            {
                GameObject slider = StageKit.Box(r, "SlidingGirder", new Vector3(i == 0 ? -6f : 6f, 12.05f, -9f), new Vector3(7f, 0.6f, 1.7f), SurfaceType.Metal, C(0.95f, 0.85f, 0.2f), 2f);
                StageKit.Kinematic(slider);
                MovingPlatform mp = slider.AddComponent<MovingPlatform>();
                mp.Travel = new Vector3(0f, 0f, 18f);
                mp.Period = 6f;
                mp.Pause = 0.8f;
                mp.StartOffset = i * 3f;
            }

            Vector3[] spots = { new Vector3(-9, 12.3f, -9), new Vector3(9, 12.3f, -9), new Vector3(-9, 12.3f, 9), new Vector3(9, 12.3f, 9),
                                new Vector3(-3, 12.3f, -3), new Vector3(3, 12.3f, 3), new Vector3(-3, 12.3f, 3), new Vector3(3, 12.3f, -3) };
            foreach (Vector3 p in spots) StageKit.Spawn(info, r, p, 0f);
            StageKit.Skyline(r, 80f, 14, 29, 5f, 40f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 14. Subway

        public static ArenaInfo Subway(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Subway", -8f, Vector3.zero, 12f, out Transform r);
            StageKit.Box(r, "Platform", new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 36f), SurfaceType.Concrete, C(0.7f, 0.7f, 0.72f), 3f);
            foreach (float x in new[] { -4.6f, 4.6f })
                StageKit.Box(r, "SafetyStripe", new Vector3(x, 0.02f, 0f), new Vector3(0.7f, 0.04f, 36f), SurfaceType.HazardStripes, Color.white, 2f, false);
            foreach (float x in new[] { -8f, 8f })
            {
                StageKit.Box(r, "TrackBed", new Vector3(x, -1.8f, 0f), new Vector3(6f, 0.4f, 150f), SurfaceType.Concrete, C(0.3f, 0.3f, 0.32f), 4f);
                foreach (float dx in new[] { -1f, 1f })
                    StageKit.Box(r, "Rail", new Vector3(x + dx, -1.5f, 0f), new Vector3(0.15f, 0.2f, 150f), SurfaceType.Metal, C(0.6f, 0.6f, 0.65f), 4f, false);
            }
            foreach (float x in new[] { -12.5f, 12.5f })
                StageKit.Box(r, "TunnelWall", new Vector3(x, 2f, 0f), new Vector3(1f, 8f, 150f), SurfaceType.Concrete, C(0.5f, 0.5f, 0.54f), 4f);
            foreach (float z in new[] { -14f, -6f, 6f, 14f })
                StageKit.Box(r, "Pillar", new Vector3(0f, 2.5f, z), new Vector3(1f, 5f, 1f), SurfaceType.Concrete, C(0.6f, 0.6f, 0.64f), 2f);
            StageKit.Box(r, "Bench", new Vector3(-3.2f, 0.4f, 0f), new Vector3(0.7f, 0.8f, 4f), SurfaceType.Wood, Color.white, 2f);
            StageKit.Box(r, "Bench", new Vector3(3.2f, 0.4f, 0f), new Vector3(0.7f, 0.8f, 4f), SurfaceType.Wood, Color.white, 2f);

            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -8f : 8f;
                GameObject train = StageKit.Box(r, "Train", new Vector3(x, 0.3f, -75f), new Vector3(4.6f, 3.6f, 16f), SurfaceType.Metal, i == 0 ? C(0.8f, 0.8f, 0.85f) : C(0.85f, 0.75f, 0.3f), 3f);
                StageKit.BoxOn(train, "Windows", new Vector3(0f, 0.5f, 0f), new Vector3(4.7f, 0.9f, 15f), SurfaceType.Plastic, C(0.2f, 0.3f, 0.4f), 1f, false);
                StageKit.Kinematic(train);
                PassingTrain pt = train.AddComponent<PassingTrain>();
                pt.From = new Vector3(x, 0.3f, -75f);
                pt.To = new Vector3(x, 0.3f, 75f);
                pt.Speed = 30f;
                pt.Interval = 8f;
                pt.InitialDelay = 4f + i * 4f;
                StageKit.Kill(train.transform, "TrainKill", Vector3.zero, new Vector3(1.25f, 1.25f, 1.05f), false);
            }

            StageKit.SpawnLine(info, r, new Vector3(-2.5f, 0.1f, -13f), new Vector3(-2.5f, 0.1f, 13f), 4, 0f);
            StageKit.SpawnLine(info, r, new Vector3(2.5f, 0.1f, -13f), new Vector3(2.5f, 0.1f, 13f), 4, 0f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 15. Ring

        public static ArenaInfo Ring(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Ring", -9f, new Vector3(0f, 1.2f, 0f), 7f, out Transform r);
            StageKit.Box(r, "Canvas", new Vector3(0f, 0.6f, 0f), new Vector3(14f, 1.2f, 14f), SurfaceType.Plastic, C(0.9f, 0.9f, 0.85f), 4f);
            StageKit.Box(r, "Skirt", new Vector3(0f, 0.3f, 0f), new Vector3(14.6f, 0.9f, 14.6f), SurfaceType.Plastic, C(0.75f, 0.1f, 0.15f), 4f, false);

            foreach (float x in new[] { -7f, 7f })
                foreach (float z in new[] { -7f, 7f })
                    StageKit.Box(r, "Post", new Vector3(x, 2.4f, z), new Vector3(0.5f, 2.4f, 0.5f), SurfaceType.Metal, C(0.8f, 0.1f, 0.15f), 1f);

            // Elastic ropes: high bounciness sends fighters back in (unless they go over or under)
            Color rope = C(0.95f, 0.95f, 0.95f);
            for (int h = 0; h < 3; h++)
            {
                float y = 1.2f + 0.55f + h * 0.55f;
                foreach (float edge in new[] { -7f, 7f })
                {
                    StageKit.Bouncy(StageKit.Box(r, "RopeZ", new Vector3(0f, y, edge), new Vector3(14f, 0.14f, 0.14f), SurfaceType.Rubber, rope, 1f), 0.85f);
                    StageKit.Bouncy(StageKit.Box(r, "RopeX", new Vector3(edge, y, 0f), new Vector3(0.14f, 0.14f, 14f), SurfaceType.Rubber, rope, 1f), 0.85f);
                }
            }

            // The pit around the ring
            StageKit.Kill(r, "CrowdPit", new Vector3(0f, -3f, 0f), new Vector3(90f, 2f, 90f), true);
            StageKit.Box(r, "PitFloor", new Vector3(0f, -9f, 0f), new Vector3(90f, 1f, 90f), SurfaceType.Concrete, C(0.1f, 0.1f, 0.12f), 6f, false);

            var spot = new GameObject("Spotlight");
            spot.transform.SetParent(r, false);
            spot.transform.localPosition = new Vector3(0f, 16f, 0f);
            spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Light l = spot.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 70f;
            l.range = 30f;
            l.intensity = 12f;

            StageKit.SpawnCircle(info, r, Vector3.zero, 4.6f, 1.25f, 8);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 16. Roof

        public static ArenaInfo Roof(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Roof", -22f, Vector3.zero, 11f, out Transform r);
            StageKit.Box(r, "Roof", new Vector3(0f, -0.5f, 0f), new Vector3(22f, 1f, 22f), SurfaceType.Concrete, C(0.55f, 0.55f, 0.58f), 3f);

            // Low parapet with gaps (the east gap is the way back up from the lower roof)
            Color wall = C(0.7f, 0.68f, 0.66f);
            StageKit.Box(r, "ParapetS", new Vector3(0f, 0.45f, -10.75f), new Vector3(22f, 0.9f, 0.5f), SurfaceType.Concrete, wall, 2f);
            StageKit.Box(r, "ParapetW", new Vector3(-10.75f, 0.45f, 0f), new Vector3(0.5f, 0.9f, 22f), SurfaceType.Concrete, wall, 2f);
            foreach (float x in new[] { -6.5f, 6.5f })
                StageKit.Box(r, "ParapetN", new Vector3(x, 0.45f, 10.75f), new Vector3(9f, 0.9f, 0.5f), SurfaceType.Concrete, wall, 2f);
            foreach (float z in new[] { -6.5f, 6.5f })
                StageKit.Box(r, "ParapetE", new Vector3(10.75f, 0.45f, z), new Vector3(0.5f, 0.9f, 9f), SurfaceType.Concrete, wall, 2f);

            // Water tower
            foreach (float dx in new[] { -1.6f, 1.6f })
                foreach (float dz in new[] { -1.6f, 1.6f })
                    StageKit.Box(r, "TowerLeg", new Vector3(-6f + dx, 2f, -6f + dz), new Vector3(0.35f, 4f, 0.35f), SurfaceType.Metal, C(0.4f, 0.3f, 0.25f), 1f);
            StageKit.Cyl(r, "WaterTank", new Vector3(-6f, 5.2f, -6f), 5f, 3.4f, SurfaceType.Wood, C(0.9f, 0.8f, 0.7f));
            StageKit.Cyl(r, "TankRoof", new Vector3(-6f, 7.4f, -6f), 5.4f, 0.6f, SurfaceType.Metal, C(0.5f, 0.3f, 0.25f), false);

            // Vents, chimney and an antenna
            StageKit.Box(r, "AirCon", new Vector3(6f, 0.6f, 5f), new Vector3(2.2f, 1.2f, 1.6f), SurfaceType.Metal, C(0.7f, 0.72f, 0.75f), 1f);
            StageKit.Box(r, "AirCon", new Vector3(7f, 0.6f, -4f), new Vector3(2.2f, 1.2f, 1.6f), SurfaceType.Metal, C(0.7f, 0.72f, 0.75f), 1f);
            StageKit.Box(r, "Chimney", new Vector3(8f, 2.5f, 8f), new Vector3(1.6f, 5f, 1.6f), SurfaceType.Concrete, C(0.55f, 0.35f, 0.3f), 1f);
            StageKit.Box(r, "Antenna", new Vector3(-8f, 5f, 8f), new Vector3(0.15f, 10f, 0.15f), SurfaceType.Metal, C(0.6f, 0.6f, 0.65f), 1f, false);

            // The lower roof next door, four meters down
            StageKit.Box(r, "LowerRoof", new Vector3(17.5f, -4.5f, 0f), new Vector3(12f, 1f, 22f), SurfaceType.Concrete, C(0.5f, 0.5f, 0.54f), 3f);

            PropFactory.Spawn(PropType.Box, new Vector3(3f, 1f, 3f)).transform.SetParent(r, true);
            PropFactory.Spawn(PropType.Dumpster, new Vector3(-3f, 1f, 6f)).transform.SetParent(r, true);
            PropFactory.Spawn(PropType.Boneloaf, new Vector3(2f, 1f, -6f)).transform.SetParent(r, true);

            StageKit.SpawnCircle(info, r, Vector3.zero, 6.5f, 0.1f, 8);
            StageKit.Skyline(r, 90f, 14, 31, -10f, 60f);
            return StageKit.Finish(info, s);
        }
    }
}
