using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeefCity.Graphics
{
    /// <summary>
    /// Builds the "premium look" Volume in code: Bloom, Color Adjustments (grading), ACES tonemapping and a light
    /// vignette. Ambient Occlusion is a URP Renderer Feature rather than a Volume effect, so it is added to the
    /// renderer by the editor menu BeefCity > Setup URP (see Editor/BeefCityURPSetup.cs).
    /// Requires the Universal Render Pipeline.
    /// </summary>
    public static class PostProcessingSetup
    {
        static Volume _volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _volume = null; }

        /// <summary>Creates the global volume once. Safe to call from anywhere, any number of times.</summary>
        public static void Ensure()
        {
            if (_volume != null || Application.isBatchMode) return;

            var go = new GameObject("BeefCityGlobalVolume");
            Object.DontDestroyOnLoad(go);

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.65f);

            ColorAdjustments grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(0.15f);
            grade.contrast.Override(14f);
            grade.saturation.Override(16f);

            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);

            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.sharedProfile = profile;
        }

        /// <summary>Turns post-processing, depth texture (for AO and decals) and SMAA on for a camera.</summary>
        public static void ConfigureCamera(Camera camera)
        {
            if (camera == null || Application.isBatchMode) return;
            Ensure();
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.requiresDepthTexture = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }

        /// <summary>Quality switch for Settings: turns the whole volume on or off.</summary>
        public static void SetEnabled(bool enabled)
        {
            Ensure();
            if (_volume != null) _volume.enabled = enabled;
        }
    }
}
