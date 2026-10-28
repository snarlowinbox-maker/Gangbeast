using UnityEngine;
using BeefCity.Core;
using BeefCity.Graphics;

namespace BeefCity.Stages
{
    /// <summary>Stages 17-22 (new Beef City locations in the same spirit as the classic set).</summary>
    public static class StagesC
    {
        static Color C(float r, float g, float b) => new Color(r, g, b);

        // ------------------------------------------------------------------ 17. Crane

        public static ArenaInfo Crane(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Crane", -14f, new Vector3(0f, 12f, 0f), 13f, out Transform r);
            Color steel = C(0.95f, 0.75f, 0.1f);
            StageKit.Box(r, "CatwalkX", new Vector3(0f, 11.7f, 0f), new Vector3(28f, 0.6f, 7f), SurfaceType.Metal, steel, 3f);
            StageKit.Box(r, "CatwalkZ", new Vector3(0f, 11.69f, 0f), new Vector3(7f, 0.6f, 28f), SurfaceType.Metal, steel * 0.95f, 3f);

            // Crane mast and jib (visual), plus the wrecking ball on a swinging chain
            StageKit.Box(r, "Mast", new Vector3(0f, -10f, -16f), new Vector3(2f, 70f, 2f), SurfaceType.Metal, steel, 4f, false);
            StageKit.Box(r, "Jib", new Vector3(0f, 26.5f, -7f), new Vector3(1.4f, 1.2f, 19f), SurfaceType.Metal, steel, 3f, false);

            var pivot = new GameObject("WreckingBall");
            pivot.transform.SetParent(r, false);
            pivot.transform.localPosition = new Vector3(0f, 26f, 0f);
            StageKit.Box(pivot.transform, "Chain", new Vector3(0f, -6f, 0f), new Vector3(0.2f, 12f, 0.2f), SurfaceType.Metal, C(0.3f, 0.3f, 0.33f), 1f, false);
            StageKit.Ball(pivot.transform, "Ball", new Vector3(0f, -12.2f, 0f), 2.8f, SurfaceType.Metal, C(0.25f, 0.25f, 0.28f), true);
            StageKit.Kinematic(pivot);
            Pendulum p = pivot.AddComponent<Pendulum>();
            p.Axis = Vector3.forward;
            p.Degrees = 52f;
            p.Period = 4.2f;

            StageKit.SpawnCircle(info, r, Vector3.zero, 9f, 12.1f, 8);
            StageKit.Skyline(r, 85f, 12, 33, 5f, 45f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 18. Presses

        public static ArenaInfo Presses(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Presses", -9f, Vector3.zero, 13f, out Transform r);
            StageKit.Box(r, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(28f, 1f, 28f), SurfaceType.Concrete, C(0.45f, 0.47f, 0.5f), 3f);

            // Belts carry anything toward the presses
            foreach (float x in new[] { -7f, 7f })
            {
                GameObject belt = StageKit.Box(r, "Belt", new Vector3(x, 0.08f, 0f), new Vector3(3.4f, 0.16f, 26f), SurfaceType.Rubber, C(0.2f, 0.2f, 0.22f), 2f);
                ConveyorBelt cb = belt.AddComponent<ConveyorBelt>();
                cb.Direction = x < 0 ? Vector3.forward : Vector3.back;
                cb.Speed = 2.4f;
            }

            int index = 0;
            foreach (float x in new[] { -7f, 7f })
            {
                foreach (float z in new[] { -7f, 7f })
                {
                    GameObject slab = StageKit.Box(r, "Press", new Vector3(x, 5.6f, z), new Vector3(4f, 1.2f, 4f), SurfaceType.Metal, C(0.55f, 0.58f, 0.62f), 2f);
                    StageKit.BoxOn(slab, "Piston", new Vector3(0f, 4f, 0f), new Vector3(1f, 8f, 1f), SurfaceType.Metal, C(0.8f, 0.8f, 0.85f), 1f, false);
                    StageKit.BoxOn(slab, "Hazard", new Vector3(0f, -0.62f, 0f), new Vector3(4.02f, 0.05f, 4.02f), SurfaceType.HazardStripes, Color.white, 1f, false);
                    StageKit.Kinematic(slab);
                    MovingPlatform mp = slab.AddComponent<MovingPlatform>();
                    mp.Travel = new Vector3(0f, -4.5f, 0f);
                    mp.Period = 0.9f;
                    mp.Pause = 1.7f;
                    mp.StartOffset = index++ * 1.1f;
                    // Crush zone rides along under the slab (local size: world 4 x 1.6 x 4 divided by the slab's scale)
                    StageKit.Kill(slab.transform, "CrushZone", new Vector3(0f, -0.92f, 0f), new Vector3(1f, 1.33f, 1f), false);
                    foreach (float cx in new[] { -2.4f, 2.4f })
                        foreach (float cz in new[] { -2.4f, 2.4f })
                            StageKit.Box(r, "Frame", new Vector3(x + cx, 4f, z + cz), new Vector3(0.3f, 8f, 0.3f), SurfaceType.Metal, C(0.4f, 0.4f, 0.45f), 1f, false);
                }
            }

            foreach (float sx in new[] { -11f, 11f })
                foreach (float z in new[] { -3f, 3f })
                    StageKit.Spawn(info, r, new Vector3(sx, 0.1f, z), sx < 0 ? 90f : 270f);
            foreach (float sz in new[] { -11f, 11f })
                foreach (float x in new[] { -3f, 3f })
                    StageKit.Spawn(info, r, new Vector3(x, 0.1f, sz), sz < 0 ? 0f : 180f);
            StageKit.Skyline(r, 80f, 10, 35, 5f, 25f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 19. Carousel

        public static ArenaInfo Carousel(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Carousel", -9f, Vector3.zero, 11f, out Transform r);
            GameObject disc = StageKit.Cyl(r, "Disc", new Vector3(0f, -0.3f, 0f), 22f, 0.6f, SurfaceType.Plastic, C(0.95f, 0.85f, 0.7f), true, null, 4f);
            StageKit.Kinematic(disc);
            Rotator rot = disc.AddComponent<Rotator>();
            rot.Axis = Vector3.up;
            rot.DegreesPerSecond = 14f;

            StageKit.BoxOn(disc, "CenterPole", new Vector3(0f, 3.3f, 0f), new Vector3(1.6f, 6f, 1.6f), SurfaceType.Metal, C(0.9f, 0.8f, 0.3f), 1f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                Vector3 off = new Vector3(Mathf.Cos(a) * 7f, 0f, Mathf.Sin(a) * 7f);
                Color body = StageKit.C(0.9f, 0.3f + 0.12f * i, 0.4f);
                StageKit.BoxOn(disc, "Pole", off + Vector3.up * 2f, new Vector3(0.25f, 4f, 0.25f), SurfaceType.Metal, C(0.9f, 0.8f, 0.3f), 1f, false);
                StageKit.BoxOn(disc, "Horse", off + Vector3.up * 1.1f, new Vector3(1.4f, 1.1f, 0.6f), SurfaceType.Plastic, body, 1f);
            }

            var canopy = new GameObject("CanopyHolder");
            canopy.transform.SetParent(r, false);
            Rotator canopyRot = canopy.AddComponent<Rotator>();
            canopyRot.DegreesPerSecond = 14f;
            StageKit.Cyl(canopy.transform, "Canopy", new Vector3(0f, 7f, 0f), 24f, 0.5f, SurfaceType.Plastic, C(0.85f, 0.2f, 0.3f), false);

            StageKit.SpawnCircle(info, r, Vector3.zero, 8.5f, 0.1f, 8);
            StageKit.Skyline(r, 80f, 10, 37, 0f, 25f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 20. Elevator

        public static ArenaInfo Elevator(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Elevator", -14f, new Vector3(0f, 3f, 0f), 12f, out Transform r);
            StageKit.Box(r, "BackWall", new Vector3(0f, 8f, 12.5f), new Vector3(26f, 34f, 1f), SurfaceType.Concrete, C(0.45f, 0.47f, 0.52f), 4f);
            StageKit.Box(r, "SideWall", new Vector3(-12.5f, 8f, 0f), new Vector3(1f, 34f, 26f), SurfaceType.Concrete, C(0.42f, 0.44f, 0.5f), 4f);
            StageKit.Box(r, "Ledge", new Vector3(0f, 13.7f, 10f), new Vector3(12f, 0.6f, 4f), SurfaceType.Metal, C(0.6f, 0.62f, 0.66f), 2f);
            StageKit.Box(r, "MidLanding", new Vector3(0f, 3.2f, 0f), new Vector3(5f, 0.5f, 5f), SurfaceType.Metal, C(0.6f, 0.62f, 0.66f), 2f);

            int i = 0;
            foreach (float x in new[] { -6.5f, 6.5f })
            {
                foreach (float z in new[] { -6.5f, 6.5f })
                {
                    GameObject lift = StageKit.Box(r, "Lift", new Vector3(x, 0f, z), new Vector3(5.5f, 0.5f, 5.5f), SurfaceType.Metal, C(0.85f, 0.85f, 0.3f), 2f);
                    StageKit.Kinematic(lift);
                    MovingPlatform mp = lift.AddComponent<MovingPlatform>();
                    mp.Travel = new Vector3(0f, 8f, 0f);
                    mp.Period = 4f;
                    mp.Pause = 1.2f;
                    mp.StartOffset = i * 2.4f;
                    StageKit.Spawn(info, r, new Vector3(x - 1f, 0.3f, z), 0f);
                    StageKit.Spawn(info, r, new Vector3(x + 1f, 0.3f, z), 0f);
                    i++;
                }
            }
            StageKit.Skyline(r, 80f, 10, 39, 0f, 40f);
            return StageKit.Finish(info, s);
        }

        // ------------------------------------------------------------------ 21. Pier

        public static ArenaInfo Pier(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Pier", -9f, new Vector3(0f, 0f, 0f), 14f, out Transform r);
            StageKit.Water(r, Vector3.zero, new Vector2(160f, 160f), -2f);

            // Boardwalk along x, then a jetty along z; every board wears out under fighters.
            for (int i = 0; i < 28; i++) LooseBoard(r, new Vector3(-14f + i * 1f + 0.5f, -0.1f, 0f), new Vector3(0.96f, 0.25f, 5f));
            for (int i = 0; i < 12; i++) LooseBoard(r, new Vector3(13.5f, -0.1f, 3f + i * 1f), new Vector3(5f, 0.25f, 0.96f));

            for (int i = 0; i < 8; i++) // pilings
                StageKit.Cyl(r, "Piling", new Vector3(-12f + i * 4f, -4f, i % 2 == 0 ? -2.2f : 2.2f), 0.7f, 8f, SurfaceType.Wood, C(0.5f, 0.4f, 0.3f), false);

            // Boats cruising beside the pier
            for (int b = 0; b < 2; b++)
            {
                float z = b == 0 ? -8f : 8f;
                GameObject hull = StageKit.Box(r, "Boat", new Vector3(b == 0 ? -12f : 12f, -1.3f, z), new Vector3(7f, 1.4f, 2.8f), SurfaceType.Wood, C(0.8f, 0.9f, 1f), 2f);
                StageKit.BoxOn(hull, "Cabin", new Vector3(1.5f, 1.2f, 0f), new Vector3(2.2f, 1.6f, 2f), SurfaceType.Plastic, C(0.95f, 0.95f, 0.9f), 1f, false);
                StageKit.Kinematic(hull);
                MovingPlatform mp = hull.AddComponent<MovingPlatform>();
                mp.Travel = new Vector3(b == 0 ? 24f : -24f, 0f, 0f);
                mp.Period = 11f;
                mp.Pause = 1f;
                mp.RockAxis = Vector3.right;
                mp.RockDegrees = 2f;
            }

            StageKit.SpawnLine(info, r, new Vector3(-11f, 0.1f, -1.2f), new Vector3(9f, 0.1f, -1.2f), 4, 90f);
            StageKit.SpawnLine(info, r, new Vector3(-9f, 0.1f, 1.2f), new Vector3(11f, 0.1f, 1.2f), 4, 270f);
            StageKit.Skyline(r, 95f, 8, 41, -8f, 18f);
            return StageKit.Finish(info, s);
        }

        static void LooseBoard(Transform r, Vector3 pos, Vector3 size)
        {
            GameObject board = StageKit.Box(r, "Board", pos, size, SurfaceType.Wood, Color.white, 2f);
            Breakable br = board.AddComponent<Breakable>();
            br.Health = 6f;
            br.FallWhenBroken = true;
            br.FallMass = 8f;
        }

        // ------------------------------------------------------------------ 22. Parking

        public static ArenaInfo Parking(MatchSettings s)
        {
            ArenaInfo info = StageKit.Begin("Parking", -8f, new Vector3(0f, 2f, 0f), 14f, out Transform r);
            StageKit.Box(r, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 22f), SurfaceType.Concrete, C(0.4f, 0.4f, 0.43f), 3f);
            StageKit.Box(r, "UpperDeck", new Vector3(0f, 5.7f, 6f), new Vector3(30f, 0.6f, 10f), SurfaceType.Concrete, C(0.5f, 0.5f, 0.54f), 3f);
            foreach (float x in new[] { -14f, 0f, 14f })
                StageKit.Box(r, "Column", new Vector3(x, 2.85f, 6f), new Vector3(1f, 5.7f, 1f), SurfaceType.Concrete, C(0.55f, 0.55f, 0.58f), 2f);

            // Ramp up to the deck (rises toward +z)
            StageKit.Box(r, "Ramp", new Vector3(-12f, 3f, -5f), new Vector3(5f, 0.5f, 13.4f), SurfaceType.Concrete, C(0.45f, 0.45f, 0.48f), 3f, true, new Vector3(-26.6f, 0f, 0f));
            StageKit.Box(r, "DeckWall", new Vector3(0f, 6.45f, 11.2f), new Vector3(30f, 0.9f, 0.4f), SurfaceType.Concrete, C(0.6f, 0.6f, 0.62f), 2f);

            for (int i = 0; i < 6; i++) // painted bays
                StageKit.Box(r, "Bay", new Vector3(-9f + i * 3.6f, 0.03f, -7f), new Vector3(0.15f, 0.04f, 4f), SurfaceType.Plastic, Color.white, 1f, false);

            // Runaway cars roll down the ramp every few seconds
            var spawner = new GameObject("CarSpawner");
            spawner.transform.SetParent(r, false);
            spawner.transform.localPosition = new Vector3(-12f, 7.4f, 4f);
            ObjectSpawner os = spawner.AddComponent<ObjectSpawner>();
            os.Interval = 6f;
            os.MaxAlive = 3;
            os.Lifetime = 25f;
            os.LaunchVelocity = new Vector3(0f, 0f, -4f);
            Color[] paint = { C(0.85f, 0.2f, 0.2f), C(0.2f, 0.45f, 0.85f), C(0.9f, 0.8f, 0.2f), C(0.3f, 0.7f, 0.4f) };
            os.Factory = pos => StageKit.Car(pos, paint[Random.Range(0, paint.Length)]);

            StageKit.SpawnLine(info, r, new Vector3(2f, 6.1f, 6f), new Vector3(12f, 6.1f, 6f), 4, 90f);
            StageKit.SpawnLine(info, r, new Vector3(-6f, 0.1f, -4f), new Vector3(6f, 0.1f, -4f), 4, 0f);
            StageKit.Skyline(r, 80f, 12, 43, 5f, 35f);
            return StageKit.Finish(info, s);
        }
    }
}
