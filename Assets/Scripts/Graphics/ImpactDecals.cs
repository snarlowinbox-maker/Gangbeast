using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BeefCity.Graphics
{
    public enum DecalKind { Scuff, Smudge, Scorch }

    /// <summary>
    /// Impact marks using URP's Decal Projector. Textures are painted with Texture2D.SetPixels. Needs the URP Decal
    /// Renderer Feature (added by BeefCity > Setup URP). Without it, small dark discs are used instead so impacts
    /// are still visible. At most 64 decals exist at once; the oldest is recycled and every decal fades out.
    /// </summary>
    public class ImpactDecals : MonoBehaviour
    {
        const int MaxDecals = 64;
        const float Life = 25f;
        const float FadeTime = 3f;

        sealed class Entry
        {
            public GameObject Go;
            public DecalProjector Projector; // null when using the fallback disc
            public float Born;
            public Vector3 BaseScale;
        }

        static ImpactDecals _instance;
        static readonly Dictionary<DecalKind, Material> Materials = new Dictionary<DecalKind, Material>();
        static Shader _decalShader;
        static bool _shaderSearched;

        readonly List<Entry> _entries = new List<Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            Materials.Clear();
            _decalShader = null;
            _shaderSearched = false;
        }

        public static void Spawn(DecalKind kind, Vector3 point, Vector3 normal, float size = 0.8f)
        {
            if (Application.isBatchMode) return;
            if (_instance == null)
            {
                var go = new GameObject("ImpactDecals");
                _instance = go.AddComponent<ImpactDecals>();
            }
            _instance.Add(kind, point, normal, size);
        }

        void Add(DecalKind kind, Vector3 point, Vector3 normal, float size)
        {
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            normal.Normalize();

            if (_entries.Count >= MaxDecals) Remove(0);

            var e = new Entry { Born = Time.time };
            e.Go = new GameObject("Decal_" + kind);
            e.Go.transform.SetParent(transform, false);
            e.Go.transform.position = point + normal * 0.05f;

            Shader shader = FindDecalShader();
            if (shader != null)
            {
                // Projectors look along +Z, so face into the surface and spin randomly.
                Vector3 up = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
                e.Go.transform.rotation = Quaternion.LookRotation(-normal, up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                DecalProjector projector = e.Go.AddComponent<DecalProjector>();
                projector.material = GetMaterial(kind, shader);
                projector.size = new Vector3(size, size, 0.4f);
                projector.drawDistance = 45f;
                projector.fadeScale = 0.9f;
                projector.fadeFactor = 1f;
                e.Projector = projector;
            }
            else
            {
                // Fallback: a thin dark disc lying on the surface.
                e.Go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.GetComponent<Collider>().enabled = false;
                Destroy(disc.GetComponent<Collider>());
                disc.transform.SetParent(e.Go.transform, false);
                e.BaseScale = new Vector3(size, 0.004f, size * (kind == DecalKind.Smudge ? 0.45f : 1f));
                disc.transform.localScale = e.BaseScale;
                ProceduralMaterialGenerator.Apply(disc.GetComponent<Renderer>(), SurfaceType.Rubber,
                    kind == DecalKind.Scorch ? new Color(0.05f, 0.04f, 0.03f) : new Color(0.12f, 0.12f, 0.13f), Vector2.one);
            }
            _entries.Add(e);
        }

        void Update()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry e = _entries[i];
                float age = Time.time - e.Born;
                if (age >= Life) { Remove(i); continue; }

                float fade = 1f - Mathf.InverseLerp(Life - FadeTime, Life, age);
                if (e.Projector != null) e.Projector.fadeFactor = fade;
                else if (e.Go.transform.childCount > 0) e.Go.transform.GetChild(0).localScale = new Vector3(e.BaseScale.x * fade, e.BaseScale.y, e.BaseScale.z * fade);
            }
        }

        void Remove(int index)
        {
            Destroy(_entries[index].Go);
            _entries.RemoveAt(index);
        }

        // ------------------------------------------------------------------ materials + textures

        static Shader FindDecalShader()
        {
            if (!_shaderSearched)
            {
                _shaderSearched = true;
                _decalShader = Shader.Find("Shader Graphs/Decal");
            }
            return _decalShader;
        }

        static Material GetMaterial(DecalKind kind, Shader shader)
        {
            if (Materials.TryGetValue(kind, out Material cached) && cached != null) return cached;

            var m = new Material(shader) { name = "Decal_" + kind, hideFlags = HideFlags.DontSave };
            Texture2D tex = MakeTexture(kind);
            // The decal shader's texture property name differs between URP versions, so set whichever exists.
            foreach (string prop in new[] { "Base_Map", "_BaseMap", "_BaseColorMap", "_MainTex" })
                if (m.HasProperty(prop)) m.SetTexture(prop, tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            Materials[kind] = m;
            return m;
        }

        static Texture2D MakeTexture(DecalKind kind)
        {
            const int size = 128;
            var px = new Color[size * size];
            float offset = (int)kind * 17.3f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float noise = Mathf.PerlinNoise(x * 0.07f + offset, y * 0.07f + offset);
                    Color c;
                    float a;

                    switch (kind)
                    {
                        case DecalKind.Smudge:
                        {
                            float r = Mathf.Sqrt(u * u * 0.4f + v * v * 2.4f);
                            float streak = Mathf.PerlinNoise(x * 0.015f + offset, y * 0.35f + offset);
                            a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1.05f - r + (streak - 0.5f) * 0.8f)) * 0.7f;
                            c = new Color(0.12f, 0.10f, 0.08f);
                            break;
                        }
                        case DecalKind.Scorch:
                        {
                            float r = Mathf.Sqrt(u * u + v * v);
                            a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1.05f - r + (noise - 0.5f) * 0.6f)) * 0.9f;
                            c = Color.Lerp(new Color(0.02f, 0.02f, 0.02f), new Color(0.9f, 0.35f, 0.05f), Mathf.SmoothStep(0.7f, 1f, r));
                            break;
                        }
                        default: // Scuff
                        {
                            float r = Mathf.Sqrt(u * u + v * v);
                            a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1.1f - r * 1.15f + (noise - 0.5f) * 0.9f)) * 0.8f;
                            c = new Color(0.08f, 0.08f, 0.09f);
                            break;
                        }
                    }
                    c.a = a;
                    px[y * size + x] = c;
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Decal_" + kind,
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
