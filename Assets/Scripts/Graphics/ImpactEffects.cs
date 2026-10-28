using UnityEngine;
using UnityEngine.Rendering;

namespace BeefCity.Graphics
{
    /// <summary>
    /// Dust, sparks and confetti built entirely in code (ParticleSystem modules + procedural soft-circle textures),
    /// plus the one-call hooks combat and the match use: Impact, StrikeHit, Confetti.
    /// Effects are short-lived and destroy themselves; light rate limiting keeps big pile-ups cheap.
    /// </summary>
    public static class ImpactEffects
    {
        static Material _softAlpha, _softAdditive, _squareAlpha;
        static float _lastDust, _lastSparks, _lastDecal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _softAlpha = _softAdditive = _squareAlpha = null;
            _lastDust = _lastSparks = _lastDecal = 0f;
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>A body hit scenery at the given speed: dust, a scuff mark and, if hard, sparks.</summary>
        public static void Impact(Vector3 point, Vector3 normal, float speed)
        {
            if (Application.isBatchMode) return;
            float k = Mathf.Clamp01(speed / 12f);
            Dust(point, normal, 0.4f + k);
            if (speed >= 5f && Time.time - _lastDecal > 0.25f)
            {
                _lastDecal = Time.time;
                ImpactDecals.Spawn(DecalKind.Scuff, point, normal, Mathf.Lerp(0.5f, 1.2f, k));
            }
            if (speed >= 7f) Sparks(point, normal, 8 + Mathf.RoundToInt(k * 10f));
        }

        /// <summary>A punch, kick or headbutt connected.</summary>
        public static void StrikeHit(Vector3 point, Vector3 normal, float strength)
        {
            if (Application.isBatchMode) return;
            Sparks(point, normal, 6);
            Dust(point, normal, 0.35f + Mathf.Clamp01(strength / 60f) * 0.5f);
        }

        // ------------------------------------------------------------------ effects

        public static void Dust(Vector3 point, Vector3 normal, float scale)
        {
            if (Application.isBatchMode || Time.time - _lastDust < 0.05f) return;
            _lastDust = Time.time;

            ParticleSystem ps = NewSystem("Dust", point, Quaternion.LookRotation(SafeNormal(normal)), SoftAlpha());
            ParticleSystem.MainModule main = ps.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.75f * scale);
            main.startColor = new Color(0.78f, 0.74f, 0.68f, 0.5f);
            main.gravityModifier = -0.04f;

            Burst(ps, Mathf.RoundToInt(Mathf.Lerp(4f, 12f, Mathf.Clamp01(scale / 1.4f))));
            Cone(ps, 35f, 0.12f);
            Fade(ps, 0.6f);

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1f)));
            ps.Play();
        }

        public static void Sparks(Vector3 point, Vector3 normal, int count)
        {
            if (Application.isBatchMode || Time.time - _lastSparks < 0.04f) return;
            _lastSparks = Time.time;

            ParticleSystem ps = NewSystem("Sparks", point, Quaternion.LookRotation(SafeNormal(normal)), SoftAdditive());
            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.gravityModifier = 1.4f;

            var warm = new Gradient();
            warm.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(warm);

            Burst(ps, count);
            Cone(ps, 40f, 0.05f);
            Fade(ps, 1f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 1.6f;
            ps.Play();
        }

        /// <summary>Round/match win and goals: a burst of colored paper falling over the given point.</summary>
        public static void Confetti(Vector3 center, float spread = 8f)
        {
            if (Application.isBatchMode) return;

            ParticleSystem ps = NewSystem("Confetti", center, Quaternion.identity, SquareAlpha());
            ParticleSystem.MainModule main = ps.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.35f;

            var rainbow = new Gradient { mode = GradientMode.Fixed };
            rainbow.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.25f, 0.25f), 0.00f), new GradientColorKey(new Color(0.98f, 0.6f, 0.15f), 0.17f),
                    new GradientColorKey(new Color(0.98f, 0.9f, 0.25f), 0.34f), new GradientColorKey(new Color(0.3f, 0.85f, 0.4f), 0.51f),
                    new GradientColorKey(new Color(0.25f, 0.55f, 0.98f), 0.68f), new GradientColorKey(new Color(0.75f, 0.35f, 0.95f), 0.85f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(rainbow) { mode = ParticleSystemGradientMode.RandomColor };

            Burst(ps, 140);

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(spread, 0.5f, spread);

            ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(2.5f);
            limit.dampen = 0.15f;

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.8f;

            Fade(ps, 1f);
            ps.Play();
        }

        // ------------------------------------------------------------------ module helpers

        static ParticleSystem NewSystem(string name, Vector3 position, Quaternion rotation, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // AddComponent starts it with defaults

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        static void Burst(ParticleSystem ps, int count)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(count, 1, 500)) });
        }

        static void Cone(ParticleSystem ps, float angle, float radius)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        static void Fade(ParticleSystem ps, float startAlpha)
        {
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha, 0.6f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(fade);
        }

        static Vector3 SafeNormal(Vector3 n) => n.sqrMagnitude < 0.01f ? Vector3.up : n.normalized;

        // ------------------------------------------------------------------ materials + textures

        static Material SoftAlpha() => _softAlpha != null ? _softAlpha : (_softAlpha = MakeMaterial(MakeSoftCircle(), false));
        static Material SoftAdditive() => _softAdditive != null ? _softAdditive : (_softAdditive = MakeMaterial(MakeSoftCircle(), true));
        static Material SquareAlpha() => _squareAlpha != null ? _squareAlpha : (_squareAlpha = MakeMaterial(MakeSquare(), false));

        static Material MakeMaterial(Texture2D texture, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { hideFlags = HideFlags.DontSave };

            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);

            if (m.HasProperty("_Surface")) // URP particles: switch to a transparent surface
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", additive ? 2f : 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            return m;
        }

        static Texture2D MakeSoftCircle()
        {
            const int size = 64;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(Mathf.Sqrt(u * u + v * v)));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return Finish(px, size, "SoftCircle");
        }

        static Texture2D MakeSquare()
        {
            const int size = 8;
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            return Finish(px, size, "Square");
        }

        static Texture2D Finish(Color[] px, int size, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
