using System.Collections.Generic;
using UnityEngine;

namespace PoEController.Input
{
    /// <summary>
    /// Plays vibration layers (from the pattern files, <see cref="HapticLibrary"/>) on the controller's two motors
    /// (low = the big, heavy one; high = the small, fast one) by setting their strength every frame: an attack ramp,
    /// an exponential fade, optional pulsing and flicker (smoothed noise), a start delay and repeats. Everything
    /// running mixes by taking the stronger value per motor, so a blast and a sword hit at the same moment both come
    /// through. The controller is told the current strength with a short timeout each frame, so a vibration always
    /// stops even if updates stop.
    /// </summary>
    public static class RumblePlayer
    {
        private struct Instance
        {
            public HapticLayer Layer;
            public float Start;
            public float Strength;
            public float Length;
            public int Seed;
            public int Tag;
            /// <summary>Time factor: 0.6 = the whole layer (delay, ramp, fade, pulses, length) runs in 60 % of the time.</summary>
            public float Pace;
        }

        /// <summary>How long the controller keeps the last strength if updates stop (a frame hitch, a pause).</summary>
        private const uint KeepAliveMs = 120;
        private const int MaxRunning = 48;

        private static readonly List<Instance> s_Active = new List<Instance>();
        private static float s_SentLow;
        private static float s_SentHigh;
        private static float s_LastSend;
        private static int s_Seed;

        /// <summary>The user's multiplier (config Input.VibrationStrength).</summary>
        public static float Multiplier = 1f;

        /// <summary>
        /// Starts every layer (and its repeats) at <paramref name="strength"/> (0..1, before the multiplier). A layer
        /// with length 0 lasts <paramref name="untilSeconds"/> (the cast build-up: until the attack lands); without
        /// one it is skipped. <paramref name="extraDelay"/> shifts the whole set (the test bench plays phases in a row).
        /// The strength goes through a square root (felt strength is far from linear, and motors barely move below
        /// about 0.15): a 0.05 hit plays at 0.22, a 0.5 hit at 0.71. <paramref name="linear"/> = use it as given.
        /// <paramref name="pace"/> &lt; 1 plays the patterns faster and shorter (real-time fights).
        /// </summary>
        public static void Play(HapticLayer[] layers, float strength, float untilSeconds = 0f, float extraDelay = 0f, bool linear = false, int tag = 0,
            float pace = 1f)
        {
            pace = Mathf.Clamp(pace, 0.1f, 1f);
            if (layers == null || strength <= 0f)
            {
                return;
            }
            float now = Time.unscaledTime;
            foreach (HapticLayer l in layers)
            {
                float length = l != null && l.length > 0f ? l.length * pace : untilSeconds;
                if (l == null || l.amp <= 0f || length <= 0f)
                {
                    continue;
                }
                float s = linear ? Mathf.Clamp01(strength) : Mathf.Sqrt(Mathf.Clamp01(strength));
                int repeats = Mathf.Clamp(l.repeat, 0, 32);
                for (int i = 0; i <= repeats; i++)
                {
                    s_Active.Add(new Instance { Layer = l, Start = now + extraDelay + (l.delay + i * Mathf.Max(0f, l.every)) * pace, Strength = s, Length = length, Seed = ++s_Seed, Tag = tag, Pace = pace });
                    s *= Mathf.Clamp01(l.falloff);
                }
            }
            // A burst of area hits in one frame: keep the list bounded, the strongest win anyway.
            while (s_Active.Count > MaxRunning)
            {
                // Oldest first, but never a running loop (it is stopped by its tag).
                int oldest = s_Active.FindIndex(a => a.Tag == 0);
                s_Active.RemoveAt(oldest >= 0 ? oldest : 0);
            }
        }

        /// <summary>A plain steady vibration on both motors (the game's camera shake).</summary>
        public static void PlaySteady(float strength, float seconds)
        {
            Play(new[] { new HapticLayer { motor = "both", amp = 1f, fade = 1000f, length = seconds } }, strength, linear: true);
        }

        private static float s_Surge;
        private static float s_SurgeSet;
        /// <summary>A surge not refreshed for this long switches itself off (the caller stopped holding it).</summary>
        private const float SurgeTimeout = 0.1f;

        /// <summary>
        /// A held, exact level on both motors (0..1, linear, then the multiplier), refreshed every frame by its caller
        /// (the end-turn hold). Mixed with everything else like any layer; 0 ends it at once.
        /// </summary>
        public static void Surge(float level)
        {
            s_Surge = Mathf.Clamp01(level);
            s_SurgeSet = Time.unscaledTime;
        }

        /// <summary>Stops the layers started with <paramref name="tag"/> (a looping preparation pulse).</summary>
        public static void Stop(int tag)
        {
            if (tag != 0)
            {
                s_Active.RemoveAll(a => a.Tag == tag);
            }
        }

        /// <summary>Stops everything at once (mode off, mouse taken over, plugin unload).</summary>
        public static void StopAll(Gamepad pad)
        {
            s_Active.Clear();
            s_Surge = 0f;
            if (pad != null && (s_SentLow > 0f || s_SentHigh > 0f))
            {
                pad.Rumble(0f, 0f, 0f);
            }
            s_SentLow = 0f;
            s_SentHigh = 0f;
        }

        /// <summary>Every frame: mixes the running layers and tells the controller.</summary>
        public static void Update(Gamepad pad, bool allowed)
        {
            if (pad == null)
            {
                return;
            }
            if (!allowed)
            {
                StopAll(pad);
                return;
            }
            float now = Time.unscaledTime;
            float low = 0f;
            float high = 0f;
            for (int i = s_Active.Count - 1; i >= 0; i--)
            {
                Instance a = s_Active[i];
                float t = now - a.Start;
                if (t >= a.Length)
                {
                    s_Active.RemoveAt(i);
                    continue;
                }
                if (t < 0f)
                {
                    continue;
                }
                float v = a.Layer.amp * Envelope(a.Layer, t / a.Pace, a.Seed) * a.Strength;
                string m = a.Layer.motor ?? "both";
                if (m != "high")
                {
                    low = Mathf.Max(low, v);
                }
                if (m != "low")
                {
                    high = Mathf.Max(high, v);
                }
            }
            if (s_Surge > 0f && now - s_SurgeSet > SurgeTimeout)
            {
                s_Surge = 0f;
            }
            low = Mathf.Max(low, s_Surge);
            high = Mathf.Max(high, s_Surge);
            low = Mathf.Clamp01(low * Multiplier);
            high = Mathf.Clamp01(high * Multiplier);
            bool changed = Mathf.Abs(low - s_SentLow) > 0.01f || Mathf.Abs(high - s_SentHigh) > 0.01f;
            bool running = low > 0f || high > 0f;
            if (changed || (running && now - s_LastSend > KeepAliveMs / 2000f))
            {
                pad.Rumble(low, high, running ? KeepAliveMs / 1000f : 0f);
                s_SentLow = low;
                s_SentHigh = high;
                s_LastSend = now;
            }
        }

        /// <summary>0..1 strength of a layer at time <paramref name="t"/> (attack ramp, fade, pulse, flicker).</summary>
        private static float Envelope(HapticLayer l, float t, int seed)
        {
            float e = l.attack > 0f && t < l.attack ? t / l.attack : Mathf.Exp(-(t - Mathf.Max(0f, l.attack)) / Mathf.Max(0.001f, l.fade));
            if (l.pulse > 0f && Mathf.Repeat(t * l.pulse, 1f) > Mathf.Clamp01(l.duty))
            {
                e *= 0.15f;
            }
            if (l.flicker > 0f)
            {
                e *= 1f - Mathf.Clamp01(l.flicker) * Mathf.PerlinNoise(t * 30f, seed * 0.731f);
            }
            return Mathf.Clamp01(e);
        }
    }
}
