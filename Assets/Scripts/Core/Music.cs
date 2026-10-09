using System;
using UnityEngine;

namespace Metro
{
    public enum MusicMood { Menu, Morning, Rush, Day, Evening, Night }

    /// <summary>
    /// Plays the generative soundtrack (<see cref="MusicSynth"/>) on the audio thread.
    /// No audio files: nothing to license, and it never repeats exactly.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        public static MusicPlayer I { get; private set; }

        MusicSynth synth;
        AudioSource source;

        public static void Create(GameObject host)
        {
            if (I != null) return;
            var go = new GameObject("Music");
            go.transform.SetParent(host.transform, false);
            go.AddComponent<MusicPlayer>();
        }

        void Awake()
        {
            I = this;
            int sampleRate = AudioSettings.outputSampleRate;
            synth = new MusicSynth(sampleRate > 0 ? sampleRate : 48000);
            source = gameObject.AddComponent<AudioSource>();
            // A silent looping clip keeps the DSP chain (and OnAudioFilterRead) running.
            source.clip = AudioClip.Create("music-carrier", synth.SampleRate, 2, synth.SampleRate, false);
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 0;
            source.Play();
        }

        /// <summary>Music volume 0..1 (the master volume is applied by the listener).</summary>
        public void SetVolume(float v) => synth.SetVolume(v);

        public void SetMood(MusicMood m) => synth.targetMood = (int)m;

        public static MusicMood MoodFor(GameClock c, bool menu)
        {
            if (menu) return MusicMood.Menu;
            switch (c.Period)
            {
                case DayPeriod.EarlyMorning: return MusicMood.Morning;
                case DayPeriod.MorningRush:
                case DayPeriod.EveningRush: return MusicMood.Rush;
                case DayPeriod.Midday: return MusicMood.Day;
                case DayPeriod.Evening: return MusicMood.Evening;
                default: return MusicMood.Night;
            }
        }

        void OnAudioFilterRead(float[] data, int channels) => synth?.Fill(data, channels);
    }

    /// <summary>
    /// The soundtrack synthesiser: a soft pad, a walking bass, a plucked arpeggio, a shaker at rush
    /// hour and a stereo delay. The chords follow the time of day. Plain C#, so it can also render
    /// offline (the audio level check in MetroHeadless).
    /// </summary>
    public sealed class MusicSynth
    {
        // Written on the main thread, read on the audio thread.
        public volatile float targetGain;
        public volatile int targetMood;

        readonly int sampleRate;
        public int SampleRate => sampleRate;

        // ---- synth state (audio thread only)
        const int TableSize = 4096;
        static readonly float[] Sine = BuildSine();
        double sampleInBeat;
        int step = -1, bar, mood = -1, chordIndex;
        float gain;
        readonly double[] padPhase = new double[8];
        readonly float[] padFreq = new float[8];
        float padEnv, padEnvTarget = 1f;
        int padCount;
        double bassPhase;
        float bassFreq, bassEnv;
        readonly double[] arpPhase = new double[6];
        readonly float[] arpFreq = new float[6], arpEnv = new float[6], arpPan = new float[6];
        int arpNext;
        float shakerEnv, noisePrev;
        uint noise = 22222;
        float[] delayL, delayR;
        int delayPos, delayLen;
        float lpL, lpR;
        System.Random rnd = new System.Random(7);

        sealed class MoodDef
        {
            public float bpm, arpChance, shaker, bassLevel, padLevel, arpLevel;
            public bool sixteenths;
            public int[][] chords;   // MIDI notes
        }

        static readonly MoodDef[] Moods =
        {
            // Menu: C major, lush and slow.
            new MoodDef { bpm = 76, arpChance = 0.45f, shaker = 0f, bassLevel = 0.8f, padLevel = 1f, arpLevel = 0.9f,
                chords = new[] { new[] { 48, 52, 55, 59, 64 }, new[] { 45, 48, 52, 55, 60 }, new[] { 41, 45, 48, 52, 57 }, new[] { 43, 47, 50, 52, 59 } } },
            // Morning: D major, bright.
            new MoodDef { bpm = 84, arpChance = 0.55f, shaker = 0.5f, bassLevel = 0.9f, padLevel = 0.85f, arpLevel = 1f,
                chords = new[] { new[] { 50, 54, 57, 61, 64 }, new[] { 47, 50, 54, 57, 62 }, new[] { 43, 47, 50, 54, 59 }, new[] { 45, 49, 52, 54, 61 } } },
            // Rush: E dorian, busier.
            new MoodDef { bpm = 96, arpChance = 0.62f, shaker = 1f, bassLevel = 1f, padLevel = 0.7f, arpLevel = 1f, sixteenths = true,
                chords = new[] { new[] { 40, 47, 50, 54, 57 }, new[] { 48, 52, 55, 59, 62 }, new[] { 50, 54, 57, 59, 64 }, new[] { 47, 50, 54, 57, 61 } } },
            // Day: F major, easy.
            new MoodDef { bpm = 82, arpChance = 0.5f, shaker = 0.25f, bassLevel = 0.85f, padLevel = 0.9f, arpLevel = 0.9f,
                chords = new[] { new[] { 41, 45, 48, 52, 57 }, new[] { 50, 53, 57, 60, 64 }, new[] { 46, 50, 53, 57, 62 }, new[] { 48, 52, 55, 57, 64 } } },
            // Evening: A minor, warm.
            new MoodDef { bpm = 78, arpChance = 0.42f, shaker = 0.15f, bassLevel = 0.85f, padLevel = 1f, arpLevel = 0.85f,
                chords = new[] { new[] { 45, 48, 52, 55, 59 }, new[] { 41, 45, 48, 52, 57 }, new[] { 48, 52, 55, 59, 62 }, new[] { 40, 43, 47, 50, 55 } } },
            // Night: D minor, sparse.
            new MoodDef { bpm = 66, arpChance = 0.25f, shaker = 0f, bassLevel = 0.6f, padLevel = 1f, arpLevel = 0.7f,
                chords = new[] { new[] { 50, 53, 57, 60, 64 }, new[] { 46, 50, 53, 57, 62 }, new[] { 41, 45, 48, 52, 55 }, new[] { 45, 48, 52, 55, 60 } } }
        };

        static float[] BuildSine()
        {
            var t = new float[TableSize + 1];
            for (int i = 0; i <= TableSize; i++) t[i] = Mathf.Sin(i / (float)TableSize * Mathf.PI * 2f);
            return t;
        }

        static float Sin01(double phase)
        {
            double p = phase - Math.Floor(phase);
            double x = p * TableSize;
            int i = (int)x;
            // p can round to exactly 1.0, and a corrupted phase must never take the audio thread down.
            if ((uint)i >= TableSize) return 0f;
            float f = (float)(x - i);
            return Sine[i] + (Sine[i + 1] - Sine[i]) * f;
        }

        static float Midi(int n) => 440f * Mathf.Pow(2f, (n - 69) / 12f);

        public MusicSynth(int sampleRate)
        {
            this.sampleRate = Math.Max(8000, sampleRate);
            delayLen = Math.Max(1, (int)(this.sampleRate * 0.54f));
            delayL = new float[delayLen];
            delayR = new float[delayLen];
        }

        /// <summary>Music volume 0..1 (the master volume is applied by the listener).</summary>
        public void SetVolume(float v) => targetGain = Mathf.Clamp01(v) * MusicGain;

        /// <summary>
        /// Output gain at full music volume. Set with MetroHeadless.AudioCheck: at the default music
        /// volume (0.55) the mix sits around -21 dBFS RMS with peaks near -10 dBFS.
        /// </summary>
        public const float MusicGain = 3.6f;

        /// <summary>Fills an interleaved buffer (audio thread).</summary>
        public void Fill(float[] data, int channels)
        {
            if (channels < 1) return;
            int frames = data.Length / channels;
            try
            {
                for (int f = 0; f < frames; f++)
                {
                    Render(out float l, out float r);
                    int o = f * channels;
                    data[o] = l;
                    if (channels > 1) data[o + 1] = r;
                    for (int c = 2; c < channels; c++) data[o + c] = 0f;
                }
            }
            catch (Exception)
            {
                // Never let the synth take the audio thread down: go quiet for this buffer and reset.
                Array.Clear(data, 0, data.Length);
                step = -1;
                mood = -1;
            }
        }

        public void Render(out float outL, out float outR)
        {
            var m = Moods[Mathf.Clamp(mood < 0 ? targetMood : mood, 0, Moods.Length - 1)];
            double samplesPerStep = sampleRate * 60.0 / m.bpm / (m.sixteenths ? 4.0 : 2.0);
            sampleInBeat += 1.0;
            if (step < 0 || sampleInBeat >= samplesPerStep)
            {
                sampleInBeat = 0;
                Step();
                m = Moods[mood];
            }
            gain += (targetGain - gain) * 0.00005f;
            float dt = 1f / sampleRate;

            // Pad: chord tones, two slightly detuned voices each, soft harmonics.
            padEnv += (padEnvTarget - padEnv) * 0.00012f;
            float pad = 0f;
            for (int i = 0; i < padCount; i++)
            {
                padPhase[i] += padFreq[i] * dt;
                double ph = padPhase[i];
                pad += Sin01(ph) + 0.22f * Sin01(ph * 2.0) + 0.1f * Sin01(ph * 3.0) + 0.7f * Sin01(ph * 1.0035);
            }
            pad *= 0.022f * padEnv * m.padLevel;

            // Bass.
            bassPhase += bassFreq * dt;
            bassEnv *= 0.99993f;
            float bass = (Sin01(bassPhase) + 0.18f * Sin01(bassPhase * 3.0)) * bassEnv * 0.12f * m.bassLevel;

            // Plucked arpeggio voices.
            float arpL = 0f, arpR = 0f;
            for (int i = 0; i < arpEnv.Length; i++)
            {
                if (arpEnv[i] < 0.0005f) continue;
                arpPhase[i] += arpFreq[i] * dt;
                float v = (Sin01(arpPhase[i]) + 0.3f * Sin01(arpPhase[i] * 2.0) + 0.08f * Sin01(arpPhase[i] * 4.0)) * arpEnv[i];
                arpEnv[i] *= 0.99986f;
                arpL += v * (1f - arpPan[i]);
                arpR += v * arpPan[i];
            }
            float arpGain = 0.055f * m.arpLevel;
            arpL *= arpGain;
            arpR *= arpGain;

            // Shaker: high-passed noise bursts.
            noise ^= noise << 13;
            noise ^= noise >> 17;
            noise ^= noise << 5;
            float n = (noise & 0xFFFF) / 32768f - 1f;
            float hp = n - noisePrev;
            noisePrev = n;
            shakerEnv *= 0.9985f;
            float shaker = hp * shakerEnv * 0.018f * m.shaker;

            // Stereo delay on pad and arp.
            float inL = pad + arpL, inR = pad + arpR;
            float dl = delayL[delayPos], dr = delayR[delayPos];
            delayL[delayPos] = inL + dr * 0.33f;
            delayR[delayPos] = inR + dl * 0.33f;
            delayPos = (delayPos + 1) % delayLen;

            float l = inL + dl * 0.28f + bass + shaker;
            float r = inR + dr * 0.28f + bass + shaker * 0.8f;
            // Gentle low-pass and soft clip.
            lpL += (l - lpL) * 0.35f;
            lpR += (r - lpR) * 0.35f;
            outL = Soft(lpL * gain);
            outR = Soft(lpR * gain);
        }

        static float Soft(float x) => x / (1f + Mathf.Abs(x));

        void Step()
        {
            step++;
            var mPrev = mood;
            int stepsPerBar = mood >= 0 && Moods[mood].sixteenths ? 16 : 8;
            if (step >= stepsPerBar || mood < 0)
            {
                step = 0;
                bar++;
                // Change mood only at the start of a two-bar phrase so the music doesn't lurch.
                if (mood < 0 || (bar % 2 == 0 && mood != targetMood)) mood = targetMood;
                stepsPerBar = Moods[mood].sixteenths ? 16 : 8;
                if (bar % 2 == 0 || mPrev != mood) NewChord();
            }
            var m = Moods[mood];
            int[] chord = m.chords[chordIndex];
            // Bass on beats 1 and 3 (and a pickup in busy moods).
            int beatStep = m.sixteenths ? 4 : 2;
            if (step % (beatStep * 2) == 0 || (m.sixteenths && step == 14))
            {
                int root = chord[0];
                while (root > 45) root -= 12;
                bassFreq = Midi(root - (step == 0 ? 0 : (rnd.NextDouble() < 0.3 ? -5 : 0)));
                bassEnv = 1f;
            }
            if (rnd.NextDouble() < m.arpChance)
            {
                int note = chord[1 + rnd.Next(chord.Length - 1)] + 12 * (rnd.NextDouble() < 0.4 ? 2 : 1);
                int v = arpNext;
                arpNext = (arpNext + 1) % arpEnv.Length;
                arpFreq[v] = Midi(note);
                arpPhase[v] = 0;
                arpEnv[v] = 0.6f + (float)rnd.NextDouble() * 0.4f;
                arpPan[v] = 0.25f + (float)rnd.NextDouble() * 0.5f;
            }
            if (step % beatStep == beatStep / 2) shakerEnv = 1f;
        }

        void NewChord()
        {
            var m = Moods[mood];
            chordIndex = (bar / 2) % m.chords.Length;
            int[] chord = m.chords[chordIndex];
            padCount = Mathf.Min(chord.Length, padPhase.Length);
            for (int i = 0; i < padCount; i++) padFreq[i] = Midi(chord[i] + 12);
            padEnv = Mathf.Min(padEnv, 0.6f);
            padEnvTarget = 1f;
        }
    }
}
