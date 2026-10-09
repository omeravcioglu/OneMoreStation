using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>Tiny procedural sound effects (no audio assets needed).</summary>
    public static class Sfx
    {
        static AudioSource source;
        static AudioClip click, build, error, chime, reveal, coin;
        public static float volume = 0.8f;
        public static bool enabled = true;

        const int Rate = 44100;
        /// <summary>Overall level of the effects, set with MetroHeadless.AudioCheck (peaks -5 to -16 dBFS at default settings).</summary>
        const float Level = 1.9f;

        /// <summary>The play volume for an effects setting of 0..1.</summary>
        public static float VolumeFor(float setting) => Mathf.Clamp01(setting);

        /// <summary>Every effect: name, samples, and the volume it plays at (before the settings volume).</summary>
        public static List<(string name, float[] samples, float vol)> Bank()
        {
            return new List<(string, float[], float)>
            {
                ("click", ToneData(0.035f, f => 1800f, 0.25f * Level, 60f), 0.5f),
                ("build", ChordData(0.32f, new[] { 523.25f, 659.25f, 783.99f }, 0.06f, 0.22f * Level), 0.9f),
                ("error", ToneData(0.22f, f => 220f - 60f * f, 0.25f * Level, 10f), 0.7f),
                ("chime", ChordData(0.5f, new[] { 880f, 1318.5f }, 0.09f, 0.18f * Level), 0.6f),
                ("reveal", ChordData(0.9f, new[] { 392f, 523.25f, 659.25f, 1046.5f }, 0.11f, 0.16f * Level), 0.8f),
                ("coin", ChordData(0.25f, new[] { 1318.5f, 1760f }, 0.05f, 0.16f * Level), 0.6f)
            };
        }

        public static int SampleRate => Rate;

        public static void Init(GameObject host)
        {
            if (source != null) return;
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            var clips = new Dictionary<string, AudioClip>();
            foreach (var (name, samples, _) in Bank())
            {
                var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
                clip.SetData(samples, 0);
                clips[name] = clip;
            }
            click = clips["click"];
            build = clips["build"];
            error = clips["error"];
            chime = clips["chime"];
            reveal = clips["reveal"];
            coin = clips["coin"];
        }

        static float[] ToneData(float dur, System.Func<float, float> freq, float amp, float decay)
        {
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = freq(t / dur);
                phase += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 400f);
                data[i] = Mathf.Sin(phase) * amp * env;
            }
            return data;
        }

        static float[] ChordData(float dur, float[] notes, float stagger, float amp)
        {
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                float start = k * stagger;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate - start;
                    if (t < 0f) continue;
                    float env = Mathf.Exp(-t * 6f) * Mathf.Min(1f, t * 300f);
                    data[i] += (Mathf.Sin(2f * Mathf.PI * notes[k] * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * notes[k] * t)) * amp * env;
                }
            }
            return data;
        }

        static void Play(AudioClip c, float vol = 1f)
        {
            if (!enabled || source == null || c == null) return;
            source.PlayOneShot(c, volume * vol);
        }

        public static void Click() => Play(click, 0.5f);
        public static void Build() => Play(build, 0.9f);
        public static void Error() => Play(error, 0.7f);
        public static void Chime() => Play(chime, 0.6f);
        public static void Reveal() => Play(reveal, 0.8f);
        public static void Coin() => Play(coin, 0.6f);
    }
}
