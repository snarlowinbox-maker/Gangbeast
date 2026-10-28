using UnityEngine;
using UnityEngine.Rendering;

namespace BeefCity.Core
{
    /// <summary>Prototype day/night switch: flips the sun, ambient light and sky color. No baking, instant.</summary>
    public static class DayNight
    {
        static bool _night;

        public static bool IsNight => _night;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _night = false; }

        public static void Toggle()
        {
            _night = !_night;
            Apply();
        }

        public static void Reset()
        {
            if (!_night) return;
            _night = false;
            Apply();
        }

        static void Apply()
        {
            Light sun = FindSun();
            if (sun != null)
            {
                sun.color = _night ? new Color(0.45f, 0.55f, 1f) : new Color(1f, 0.96f, 0.88f);
                sun.intensity = _night ? 0.3f : 1.1f;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = _night ? new Color(0.06f, 0.08f, 0.16f) : new Color(0.45f, 0.5f, 0.6f);

            Camera cam = Camera.main;
            if (cam != null && cam.clearFlags == CameraClearFlags.SolidColor)
                cam.backgroundColor = _night ? new Color(0.02f, 0.03f, 0.07f) : new Color(0.35f, 0.5f, 0.7f);
        }

        static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.gameObject.activeInHierarchy) return l;
            return null;
        }
    }
}
