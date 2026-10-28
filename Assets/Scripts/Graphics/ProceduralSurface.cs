using UnityEngine;

namespace BeefCity.Graphics
{
    /// <summary>
    /// Drop this on any primitive (or a parent of several) to give it a procedural surface.
    /// Uses ProceduralMaterialGenerator, so tint and tiling go through a MaterialPropertyBlock.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProceduralSurface : MonoBehaviour
    {
        public SurfaceType type = SurfaceType.Concrete;
        public Color tint = Color.white;
        [Min(0.05f)] public float tileSize = 2f;
        public bool autoTiling = true;
        public Vector2 manualTiling = Vector2.one;
        public int seed = 1;

        void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply")]
        public void Apply()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                Vector2 tiling = autoTiling
                    ? ProceduralMaterialGenerator.AutoTiling(r.transform.lossyScale, tileSize)
                    : manualTiling;
                ProceduralMaterialGenerator.Apply(r, type, tint, tiling, ProceduralMaterialGenerator.DefaultSize, seed);
            }
        }
    }
}
