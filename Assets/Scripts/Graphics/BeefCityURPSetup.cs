#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeefCity.EditorTools
{
    /// <summary>
    /// One-click URP setup: adds Screen Space Ambient Occlusion and Decal renderer features to the active
    /// renderer(s) and turns on the depth texture. Run it once: menu BeefCity > Setup URP.
    /// Bloom, color grading, tonemapping and vignette need no setup; they are created in code at runtime.
    /// </summary>
    public static class BeefCityURPSetup
    {
        [MenuItem("BeefCity/Setup URP (Ambient Occlusion + Decals)")]
        public static void Run()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                EditorUtility.DisplayDialog("BeefCity URP setup",
                    "No URP asset is active.\n\nCreate one (Assets > Create > Rendering > URP Asset) and assign it in " +
                    "Project Settings > Graphics (and Quality), then run this again.", "OK");
                return;
            }

            int added = 0;
            try
            {
                var assetSo = new SerializedObject(asset);
                SerializedProperty depth = assetSo.FindProperty("m_RequireDepthTexture");
                if (depth != null) depth.boolValue = true;
                assetSo.ApplyModifiedProperties();

                SerializedProperty list = assetSo.FindProperty("m_RendererDataList");
                for (int i = 0; list != null && i < list.arraySize; i++)
                {
                    var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                    if (data == null) continue;
                    if (AddFeature<ScreenSpaceAmbientOcclusion>(data, "Screen Space Ambient Occlusion")) added++;
                    if (AddFeature<DecalRendererFeature>(data, "Decals")) added++;
                }
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BeefCity URP setup] Automatic setup failed (" + e.Message + "). Add the features by hand: " +
                                 "select your Renderer asset > Add Renderer Feature > Screen Space Ambient Occlusion and Decal.");
                return;
            }

            EditorUtility.DisplayDialog("BeefCity URP setup",
                added > 0 ? "Added " + added + " renderer feature(s) and enabled the depth texture."
                          : "Ambient Occlusion and Decals were already set up.", "OK");
        }

        static bool AddFeature<T>(ScriptableRendererData data, string featureName) where T : ScriptableRendererFeature
        {
            foreach (ScriptableRendererFeature existing in data.rendererFeatures)
                if (existing is T) return false;

            T feature = ScriptableObject.CreateInstance<T>();
            feature.name = featureName;
            AssetDatabase.AddObjectToAsset(feature, data);
            feature.Create();

            // Same bookkeeping Unity's own "Add Renderer Feature" button performs.
            var so = new SerializedObject(data);
            so.Update();
            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            return true;
        }
    }
}
#endif
