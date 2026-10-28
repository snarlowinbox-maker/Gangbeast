using System.Collections.Generic;
using UnityEngine;

namespace BeefCity.AI
{
    public enum VoiceKind { Grunt, Hurt, Yell, Taunt }

    /// <summary>
    /// Enemy "vocals" with no audio files: each clip is a pitch-swept harmonic series shaped by vowel formants,
    /// with a little breath noise and an attack/decay envelope. Clips are generated once and cached.
    /// </summary>
    public static class ProceduralVoice
    {
        const int SampleRate = 22050;

        static readonly Dictionary<(VoiceKind, int, int), AudioClip> Cache = new Dictionary<(VoiceKind, int, int), AudioClip>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); }

        // Vowel formant pairs (Hz): "ah", "oh", "eh"
        static readonly float[,] Formants = { { 700f, 1200f }, { 500f, 800f }, { 550f, 1800f } };

        public static AudioClip Get(VoiceKind kind, float pitch, int variant)
        {
            variant = Mathf.Abs(variant) % 3;
            var key = (kind, Mathf.RoundToInt(pitch * 10f), variant);
            if (Cache.TryGetValue(key, out AudioClip clip) && clip != null) return clip;

            clip = Synthesize(kind, pitch, variant);
            Cache[key] = clip;
            return clip;
        }

        public static void Play(AudioSource source, VoiceKind kind, float pitch, float volume = 0.8f)
        {
            if (source == null) return;
            source.PlayOneShot(Get(kind, pitch, Random.Range(0, 3)), volume);
        }

        static AudioClip Synthesize(VoiceKind kind, float pitch, int variant)
        {
            float duration, f0Start, f0End, vibrato, decay;
            switch (kind)
            {
                case VoiceKind.Hurt:  duration = 0.38f; f0Start = 230f; f0End = 140f; vibrato = 0.02f; decay = 4.5f; break;
                case VoiceKind.Yell:  duration = 0.65f; f0Start = 150f; f0End = 270f; vibrato = 0.03f; decay = 2.2f; break;
                case VoiceKind.Taunt: duration = 0.55f; f0Start = 130f; f0End = 175f; vibrato = 0.06f; decay = 3.0f; break;
                default:              duration = 0.24f; f0Start = 125f; f0End = 90f;  vibrato = 0.01f; decay = 7.0f; break; // Grunt
            }

            var rng = new System.Random((int)kind * 131 + variant * 17 + Mathf.RoundToInt(pitch * 100f));
            float detune = 1f + ((float)rng.NextDouble() - 0.5f) * 0.16f;
            float f1 = Formants[variant, 0], f2 = Formants[variant, 1];

            int count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            float phase = 0f;
            float peak = 0.0001f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float u = t / duration;
                float f0 = Mathf.Lerp(f0Start, f0End, u) * pitch * detune * (1f + vibrato * Mathf.Sin(t * 38f));
                phase += f0 / SampleRate;
                phase -= Mathf.Floor(phase);

                float s = 0f;
                for (int n = 1; n <= 20; n++)
                {
                    float hz = n * f0;
                    if (hz > SampleRate * 0.45f) break;
                    float a = Gaussian(hz, f1, 160f) + 0.6f * Gaussian(hz, f2, 230f) + 0.04f;
                    s += a / n * Mathf.Sin(phase * Mathf.PI * 2f * n);
                }
                s += ((float)rng.NextDouble() * 2f - 1f) * 0.05f; // breath

                float attack = Mathf.Clamp01(t / 0.012f);
                float envelope = attack * Mathf.Exp(-decay * u);
                data[i] = s * envelope;
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }

            float gain = 0.8f / peak;
            for (int i = 0; i < count; i++) data[i] *= gain;

            AudioClip clip = AudioClip.Create("Voice_" + kind + "_" + variant, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Gaussian(float x, float center, float width)
        {
            float d = (x - center) / width;
            return Mathf.Exp(-d * d);
        }
    }
}
