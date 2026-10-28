using System.Collections.Generic;
using UnityEngine;

namespace BeefCity.Graphics
{
    public enum SurfaceType { Concrete, Metal, Plastic, Rubber, Wood, HazardStripes }

    /// <summary>
    /// Generates tileable albedo / normal / occlusion textures from code (Texture2D.SetPixels + Perlin noise)
    /// and wraps them in URP Lit materials. Per-object tint and tiling go through a MaterialPropertyBlock.
    /// Generation costs a few dozen ms per surface at 256px, so call PreWarm() on the loading screen.
    /// </summary>
    public static class ProceduralMaterialGenerator
    {
        public const int DefaultSize = 256;

        struct Profile
        {
            public float Metallic, Smoothness, NormalStrength, Grunge;
        }

        sealed class TextureSet
        {
            public Texture2D Albedo, Normal, Occlusion;
        }

        static readonly Dictionary<(SurfaceType, int, int), TextureSet> TextureCache =
            new Dictionary<(SurfaceType, int, int), TextureSet>();
        static readonly Dictionary<(SurfaceType, int, int), Material> MaterialCache =
            new Dictionary<(SurfaceType, int, int), Material>();

        static MaterialPropertyBlock _mpb;
        static MaterialPropertyBlock Mpb => _mpb ??= new MaterialPropertyBlock();

        // Keeps the caches valid when "Enter Play Mode Options > Reload Domain" is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            TextureCache.Clear();
            MaterialCache.Clear();
            _mpb = null;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Shared, white-tinted material for a surface type. Cached.</summary>
        public static Material GetMaterial(SurfaceType type, int size = DefaultSize, int seed = 1)
        {
            var key = (type, size, seed);
            if (MaterialCache.TryGetValue(key, out Material cached) && cached != null) return cached;

            TextureSet set = GetTextures(type, size, seed);
            Profile p = GetProfile(type);
            var mat = new Material(FindShader())
            {
                name = "Proc_" + type,
                hideFlags = HideFlags.DontSave
            };

            SetTexture(mat, "_BaseMap", "_MainTex", set.Albedo);
            SetTexture(mat, "_BumpMap", "_BumpMap", set.Normal);
            SetTexture(mat, "_OcclusionMap", "_OcclusionMap", set.Occlusion);
            SetFloat(mat, "_Metallic", p.Metallic);
            SetFloat(mat, "_Smoothness", p.Smoothness);   // URP
            SetFloat(mat, "_Glossiness", p.Smoothness);   // Built-in Standard
            SetFloat(mat, "_BumpScale", 1f);
            SetFloat(mat, "_OcclusionStrength", 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_OCCLUSIONMAP");

            MaterialCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// Assigns the surface material to a renderer and applies tint + UV tiling through a
        /// MaterialPropertyBlock (no per-object material instances).
        /// Note: renderers with a property block are excluded from SRP Batcher batching.
        /// </summary>
        public static void Apply(Renderer renderer, SurfaceType type, Color tint, Vector2 tiling,
                                 int size = DefaultSize, int seed = 1)
        {
            if (renderer == null) return;
            renderer.sharedMaterial = GetMaterial(type, size, seed);

            MaterialPropertyBlock mpb = Mpb;
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", tint);
            mpb.SetColor("_Color", tint);
            var st = new Vector4(tiling.x, tiling.y, 0f, 0f);
            mpb.SetVector("_BaseMap_ST", st);
            mpb.SetVector("_MainTex_ST", st);
            renderer.SetPropertyBlock(mpb);
        }

        /// <summary>Changes only the tint of a renderer that already has a procedural surface.</summary>
        public static void Tint(Renderer renderer, Color tint)
        {
            if (renderer == null) return;
            MaterialPropertyBlock mpb = Mpb;
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", tint);
            mpb.SetColor("_Color", tint);
            renderer.SetPropertyBlock(mpb);
        }

        /// <summary>Roughly keeps texel density constant on scaled primitives (one tile per tileSize meters).</summary>
        public static Vector2 AutoTiling(Vector3 lossyScale, float tileSize)
        {
            float a = Mathf.Abs(lossyScale.x), b = Mathf.Abs(lossyScale.y), c = Mathf.Abs(lossyScale.z);
            float max = Mathf.Max(a, Mathf.Max(b, c));
            float min = Mathf.Min(a, Mathf.Min(b, c));
            float mid = a + b + c - max - min;
            tileSize = Mathf.Max(0.05f, tileSize);
            return new Vector2(Mathf.Max(1f, max / tileSize), Mathf.Max(1f, mid / tileSize));
        }

        public static void PreWarm(int size, params SurfaceType[] types)
        {
            foreach (SurfaceType t in types) GetMaterial(t, size);
        }

        /// <summary>Grayscale grunge mask in the alpha channel; useful for decals and overlays.</summary>
        public static Texture2D CreateGrungeMask(int size = DefaultSize, int seed = 1)
        {
            var px = new Color[size * size];
            float off = seed * 37.17f % 500f;
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float g = Smooth(Fbm(u, v, 4f, 4f, 5, off), 0.40f, 0.85f);
                    px[y * size + x] = new Color(1f, 1f, 1f, g);
                }
            }
            return MakeTexture(px, size, true, "GrungeMask");
        }

        public static void ReleaseAll()
        {
            foreach (var kv in MaterialCache) if (kv.Value != null) Object.Destroy(kv.Value);
            foreach (var kv in TextureCache)
            {
                if (kv.Value.Albedo != null) Object.Destroy(kv.Value.Albedo);
                if (kv.Value.Normal != null) Object.Destroy(kv.Value.Normal);
                if (kv.Value.Occlusion != null) Object.Destroy(kv.Value.Occlusion);
            }
            MaterialCache.Clear();
            TextureCache.Clear();
        }

        // ------------------------------------------------------------------ profiles + shader lookup

        static Profile GetProfile(SurfaceType t)
        {
            switch (t)
            {
                case SurfaceType.Concrete:
                    return new Profile { Metallic = 0f, Smoothness = 0.12f, NormalStrength = 1.2f, Grunge = 0.9f };
                case SurfaceType.Metal:
                    return new Profile { Metallic = 0.9f, Smoothness = 0.55f, NormalStrength = 0.6f, Grunge = 0.8f };
                case SurfaceType.Plastic:
                    return new Profile { Metallic = 0f, Smoothness = 0.6f, NormalStrength = 0.25f, Grunge = 0.15f };
                case SurfaceType.Rubber:
                    return new Profile { Metallic = 0f, Smoothness = 0.25f, NormalStrength = 0.8f, Grunge = 0.3f };
                case SurfaceType.Wood:
                    return new Profile { Metallic = 0f, Smoothness = 0.2f, NormalStrength = 0.7f, Grunge = 0.5f };
                default:
                    return new Profile { Metallic = 0f, Smoothness = 0.3f, NormalStrength = 0.5f, Grunge = 0.85f };
            }
        }

        static Shader FindShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            if (s == null) s = Shader.Find("Unlit/Texture");
            if (s == null) Debug.LogError("[ProceduralMaterialGenerator] No usable shader found. Add 'Universal Render Pipeline/Lit' to Project Settings > Graphics > Always Included Shaders.");
            return s;
        }

        static void SetTexture(Material m, string primary, string fallback, Texture t)
        {
            if (m.HasProperty(primary)) m.SetTexture(primary, t);
            else if (m.HasProperty(fallback)) m.SetTexture(fallback, t);
        }

        static void SetFloat(Material m, string name, float value)
        {
            if (m.HasProperty(name)) m.SetFloat(name, value);
        }

        // ------------------------------------------------------------------ texture generation

        static TextureSet GetTextures(SurfaceType type, int size, int seed)
        {
            var key = (type, size, seed);
            if (TextureCache.TryGetValue(key, out TextureSet cached) && cached.Albedo != null) return cached;
            TextureSet set = BuildTextures(type, size, seed);
            TextureCache[key] = set;
            return set;
        }

        static TextureSet BuildTextures(SurfaceType type, int size, int seed)
        {
            Profile p = GetProfile(type);
            int n = size * size;
            var albedo = new Color[n];
            var height = new float[n];
            var rng = new System.Random(seed * 7919 + (int)type * 104729);
            float off = seed * 37.17f % 500f + (int)type * 11.3f;

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    Shade(type, u, v, off, (float)rng.NextDouble(), p.Grunge, out Color c, out float h);
                    int i = y * size + x;
                    albedo[i] = c;
                    height[i] = h;
                }
            }

            // Normal + occlusion maps derived from the height field (wraps around so tiling stays seamless).
            var normal = new Color[n];
            var occlusion = new Color[n];
            float k = p.NormalStrength * size * 0.05f;
            for (int y = 0; y < size; y++)
            {
                int yd = (y - 1 + size) % size, yu = (y + 1) % size;
                for (int x = 0; x < size; x++)
                {
                    int xl = (x - 1 + size) % size, xr = (x + 1) % size;
                    int i = y * size + x;
                    float dx = (height[y * size + xr] - height[y * size + xl]) * k;
                    float dy = (height[yu * size + x] - height[yd * size + x]) * k;
                    Vector3 nv = new Vector3(-dx, -dy, 1f).normalized;
                    normal[i] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
                    float ao = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(height[i]));
                    occlusion[i] = new Color(ao, ao, ao, 1f);
                }
            }

            return new TextureSet
            {
                Albedo = MakeTexture(albedo, size, false, type + "_Albedo"),
                Normal = MakeTexture(normal, size, true, type + "_Normal"),
                Occlusion = MakeTexture(occlusion, size, true, type + "_AO")
            };
        }

        static Texture2D MakeTexture(Color[] pixels, int size, bool linear, string name)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave
            };
            t.SetPixels(pixels);
            t.Apply(true, true); // build mips, free the CPU copy
            return t;
        }

        static void Shade(SurfaceType t, float u, float v, float off, float rnd, float grunge,
                          out Color c, out float h)
        {
            float lum;
            switch (t)
            {
                case SurfaceType.Concrete:
                {
                    float broad = Fbm(u, v, 3f, 3f, 4, off);
                    float fine = Fbm(u, v, 28f, 28f, 3, off + 9f);
                    float pit = fine < 0.30f ? 1f : 0f;
                    h = broad * 0.45f + fine * 0.40f + rnd * 0.15f - pit * 0.25f;
                    lum = (0.46f + broad * 0.22f + rnd * 0.06f) * (1f - pit * 0.25f);
                    c = new Color(lum, lum * 0.985f, lum * 0.95f, 1f);
                    break;
                }
                case SurfaceType.Metal:
                {
                    float s1 = Tile(u, v, 2f, 96f, off);          // brushed streaks
                    float s2 = Tile(u, v, 5f, 240f, off + 3f);
                    float scratch = rnd > 0.985f ? 1f : 0f;
                    h = s1 * 0.5f + s2 * 0.35f + rnd * 0.05f;
                    lum = 0.60f + s1 * 0.16f + s2 * 0.10f + scratch * 0.12f;
                    c = new Color(lum * 0.92f, lum * 0.95f, lum, 1f);
                    break;
                }
                case SurfaceType.Plastic:
                {
                    float peel = Fbm(u, v, 48f, 48f, 2, off);      // subtle orange-peel
                    h = 0.5f + (peel - 0.5f) * 0.3f;
                    lum = 0.94f + (peel - 0.5f) * 0.08f;
                    c = new Color(lum, lum, lum, 1f);
                    break;
                }
                case SurfaceType.Rubber:
                {
                    float broad = Fbm(u, v, 10f, 10f, 3, off);
                    h = broad * 0.35f + rnd * 0.40f;
                    lum = 0.12f + broad * 0.06f + rnd * 0.03f;
                    c = new Color(lum, lum, lum * 1.06f, 1f);
                    break;
                }
                case SurfaceType.Wood:
                {
                    const float planks = 4f;
                    int plank = Mathf.Clamp(Mathf.FloorToInt(u * planks), 0, 3);
                    float pu = u * planks - plank;
                    float pr = Hash01(plank, off);
                    float warp = Fbm(u, v, 2f, 3f, 3, off + plank * 13f);
                    float rings = Mathf.Sin((pu * 10f + warp * 3.5f + pr * 6f) * Mathf.PI) * 0.5f + 0.5f;
                    float edge = Mathf.Min(pu, 1f - pu);
                    float gap = 1f - Mathf.InverseLerp(0f, 0.03f, edge);
                    h = 0.55f + rings * 0.2f - gap * 0.45f;
                    var dark = new Color(0.24f, 0.13f, 0.06f, 1f);
                    var light = new Color(0.58f, 0.38f, 0.19f, 1f);
                    c = Color.Lerp(dark, light, Mathf.Clamp01(rings * 0.55f + pr * 0.35f + warp * 0.10f));
                    c = Color.Lerp(c, new Color(0.05f, 0.03f, 0.02f, 1f), gap);
                    break;
                }
                default: // HazardStripes
                {
                    float d = (u + v) * 4f;
                    d -= Mathf.Floor(d);
                    bool yellow = d < 0.5f;
                    float wear = Fbm(u, v, 7f, 7f, 4, off + 5f);
                    h = (yellow ? 0.55f : 0.45f) + wear * 0.2f;
                    c = yellow ? new Color(0.96f, 0.74f, 0.04f, 1f) : new Color(0.05f, 0.05f, 0.055f, 1f);
                    if (wear > 0.72f)
                        c = Color.Lerp(c, new Color(0.35f, 0.35f, 0.36f, 1f), Smooth(wear, 0.72f, 0.85f));
                    break;
                }
            }

            // Perlin grunge pass shared by every surface.
            float gr = Smooth(Fbm(u, v, 5f, 5f, 4, off + 77f), 0.42f, 0.85f) * grunge;
            float dim = Mathf.Lerp(1f, 0.5f, gr);
            c = new Color(c.r * dim, c.g * dim, c.b * dim, 1f);
            h = Mathf.Clamp01(h - gr * 0.1f);
        }

        // ------------------------------------------------------------------ tileable noise

        /// <summary>Seamlessly tileable Perlin noise (blends four offset samples). u,v in [0,1).</summary>
        static float Tile(float u, float v, float fx, float fy, float off)
        {
            fx = Mathf.Max(1f, Mathf.Round(fx));
            fy = Mathf.Max(1f, Mathf.Round(fy));
            float x = u * fx, y = v * fy;
            float ox = off + fx + 10f, oy = off * 0.73f + fy + 10f; // keep Perlin inputs positive
            float a = Mathf.PerlinNoise(x + ox, y + oy);
            float b = Mathf.PerlinNoise(x - fx + ox, y + oy);
            float c = Mathf.PerlinNoise(x + ox, y - fy + oy);
            float d = Mathf.PerlinNoise(x - fx + ox, y - fy + oy);
            return Mathf.Clamp01(Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v));
        }

        static float Fbm(float u, float v, float fx, float fy, int octaves, float off)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Tile(u, v, fx, fy, off + i * 17.31f);
                norm += amp;
                amp *= 0.5f;
                fx *= 2f;
                fy *= 2f;
            }
            return sum / norm;
        }

        static float Smooth(float x, float a, float b) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        static float Hash01(int a, float off)
        {
            float x = Mathf.Sin(a * 127.1f + off * 311.7f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }
    }
}
