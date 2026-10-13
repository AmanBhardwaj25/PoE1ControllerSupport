using System.Collections.Generic;
using UnityEngine;

namespace PoEController.Input
{
    /// <summary>
    /// The controller's lights: the DualSense lightbar, player LEDs and mute light (one DS5 effect packet), or the
    /// DualShock 4 lightbar. Every frame it mixes, in order: the base colour (set from the game state), the low
    /// endurance heartbeat, a spell being prepared (breathing between that spell's colours), the end-turn surge,
    /// then the flashes of hits and spells on top (each moving through its spell's colours and fading out). Colours
    /// come from the spell art (pattern files' "lights", the spell index); this class only plays them. Sends at most
    /// ~30 times a second and only when something changed; restores the controller's defaults when it stops.
    /// </summary>
    public static class LightPlayer
    {
        private struct Flash
        {
            public Color[] Colors;
            public float Start;
            public float Length;
            public float Strength;
        }

        private const int MaxFlashes = 8;
        private const float SendInterval = 1f / 30f;
        /// <summary>Re-send the unchanged state this often (another program or a reconnect may have changed the lights).</summary>
        private const float RefreshInterval = 1f;
        /// <summary>LED brightness is far from linear: this curve keeps dim colours dim.</summary>
        private const float Gamma = 1.6f;

        private static readonly List<Flash> s_Flashes = new List<Flash>();
        private static readonly byte[] s_Packet = new byte[47];

        /// <summary>The resting colour and brightness (0..1) for the current game state.</summary>
        public static Color BaseColor = new Color(0.78f, 0.56f, 0.25f);
        public static float BaseLevel = 0.25f;
        /// <summary>Breathing of the base colour, breaths per second (0 = steady).</summary>
        public static float BaseBreath;
        /// <summary>Main character's endurance is low: a slow red heartbeat through the base colour.</summary>
        public static bool LowEndurance;
        /// <summary>Player LEDs (DualSense): how many of the five are lit, 0..5.</summary>
        public static int PlayerLights = 1;
        /// <summary>DualSense adaptive trigger effects (<see cref="DualSenseTriggers"/>); null = free.</summary>
        public static byte[] RightTrigger;
        public static byte[] LeftTrigger;

        private static readonly byte[] s_TriggerOff = DualSenseTriggers.Off();
        private static byte[] s_SentRight;
        private static byte[] s_SentLeft;
        private static bool s_SentLightsOn;

        private static Color[] s_Prepare;
        private static float s_PrepareStart;
        private static float s_Surge;
        private static float s_SurgeSet;

        private static System.IntPtr s_SentTo;
        private static Color32 s_Sent;
        private static int s_SentLights = -1;
        private static int s_SentMic = -1;
        private static float s_LastSend;
        private static bool s_Active;

        /// <summary>A hit or spell: moves through <paramref name="colors"/> over <paramref name="seconds"/>, then fades.</summary>
        public static void Play(Color[] colors, float seconds, float strength = 1f, float delay = 0f)
        {
            if (colors == null || colors.Length == 0 || strength <= 0f)
            {
                return;
            }
            s_Flashes.Add(new Flash { Colors = colors, Start = Time.unscaledTime + Mathf.Max(0f, delay), Length = Mathf.Clamp(seconds, 0.25f, 2.5f), Strength = Mathf.Clamp01(strength) });
            while (s_Flashes.Count > MaxFlashes)
            {
                s_Flashes.RemoveAt(0);
            }
        }

        /// <summary>A spell is being prepared (turn-based long cast): breathe between its colours until <see cref="EndPrepare"/>.</summary>
        public static void Prepare(Color[] colors)
        {
            s_Prepare = colors != null && colors.Length > 0 ? colors : null;
            s_PrepareStart = Time.unscaledTime;
        }

        public static void EndPrepare() => s_Prepare = null;

        /// <summary>The end-turn hold (0..1), refreshed every frame while held; switches itself off when not refreshed.</summary>
        public static void Surge(float level)
        {
            s_Surge = Mathf.Clamp01(level);
            s_SurgeSet = Time.unscaledTime;
        }

        /// <summary>
        /// Every frame. <paramref name="lightsAllowed"/> / <paramref name="triggersAllowed"/> = that setting is on and the
        /// pad is the active device. Triggers exist on the DualSense only.
        /// </summary>
        public static void Update(Gamepad pad, bool lightsAllowed, bool triggersAllowed)
        {
            if (pad == null)
            {
                return;
            }
            bool dualSense = pad.IsDualSense;
            bool ds4 = pad.IsDualShock4;
            if ((!lightsAllowed && !triggersAllowed) || (!dualSense && !ds4) || (ds4 && !lightsAllowed))
            {
                Stop(pad);
                return;
            }
            float now = Time.unscaledTime;
            if (s_Surge > 0f && now - s_SurgeSet > 0.1f)
            {
                s_Surge = 0f;
            }

            // Base: the game state's colour, breathing if asked, with the low-endurance heartbeat.
            float level = BaseLevel;
            if (BaseBreath > 0f)
            {
                level *= 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(now * BaseBreath * 2f * Mathf.PI));
            }
            Color c = BaseColor * level;
            if (LowEndurance)
            {
                // lub-dub every 1.2 s
                float t = Mathf.Repeat(now, 1.2f);
                float beat = Mathf.Max(Pulse(t, 0f, 0.18f), 0.7f * Pulse(t, 0.25f, 0.18f));
                c = Color.Lerp(c, new Color(1f, 0.05f, 0.02f), beat * 0.85f);
            }

            // Preparing a spell: breathe through its colours (about one breath a second).
            if (s_Prepare != null)
            {
                float t = now - s_PrepareStart;
                Color p = Sample(s_Prepare, Mathf.Repeat(t * 0.25f, 1f), true);
                float breath = 0.35f + 0.65f * (0.5f - 0.5f * Mathf.Cos(t * 1.1f * 2f * Mathf.PI));
                c = Color.Lerp(c, p * breath, 0.9f);
            }

            // End turn: brighter and whiter as the hold fills.
            if (s_Surge > 0f)
            {
                c = Color.Lerp(c, Color.white, s_Surge * 0.7f) * (0.4f + 0.6f * s_Surge) + c * (1f - s_Surge) * 0.3f;
            }

            // Flashes on top, oldest first so the newest wins.
            for (int i = 0; i < s_Flashes.Count; i++)
            {
                Flash f = s_Flashes[i];
                float t = now - f.Start;
                if (t >= f.Length)
                {
                    s_Flashes.RemoveAt(i--);
                    continue;
                }
                if (t < 0f)
                {
                    continue;
                }
                float p = t / f.Length;
                // in fast, hold, then fade out over the last 45 %
                float env = Mathf.Min(1f, t / 0.05f) * (p < 0.55f ? 1f : 1f - (p - 0.55f) / 0.45f);
                c = Color.Lerp(c, Sample(f.Colors, Mathf.Min(1f, p / 0.8f), false), env * f.Strength);
            }

            Color32 out32 = new Color32(Byte(c.r), Byte(c.g), Byte(c.b), 255);
            int lights = Mathf.Clamp(PlayerLights, 0, 5);
            int mic = s_Prepare != null ? 2 : 0; // 0 off, 1 on, 2 pulsing
            // Triggers off = the "off" effect is still sent, so they are freed when the setting goes off.
            byte[] right = triggersAllowed && dualSense && RightTrigger != null ? RightTrigger : s_TriggerOff;
            byte[] left = triggersAllowed && dualSense && LeftTrigger != null ? LeftTrigger : s_TriggerOff;
            bool changed = !s_Active || s_SentTo != pad.Handle || !Same(out32, s_Sent) || lights != s_SentLights || mic != s_SentMic
                || lightsAllowed != s_SentLightsOn || !DualSenseTriggers.Same(right, s_SentRight) || !DualSenseTriggers.Same(left, s_SentLeft);
            if ((changed && now - s_LastSend >= SendInterval) || now - s_LastSend >= RefreshInterval)
            {
                if (dualSense && (!s_Active || s_SentTo != pad.Handle))
                {
                    pad.SendEffect(ReleaseLightbar());
                }
                bool ok = dualSense ? pad.SendEffect(Packet(out32, lights, mic, lightsAllowed, right, left)) : pad.SetLightbar(out32.r, out32.g, out32.b);
                if (ok)
                {
                    s_SentTo = pad.Handle;
                    s_Sent = out32;
                    s_SentLights = lights;
                    s_SentMic = mic;
                    s_SentLightsOn = lightsAllowed;
                    s_SentRight = right;
                    s_SentLeft = left;
                    s_Active = true;
                }
                s_LastSend = now;
            }
        }

        /// <summary>Back to the controller's own look (SDL's player-one blue, one player light, mute light off).</summary>
        public static void Stop(Gamepad pad)
        {
            s_Flashes.Clear();
            s_Prepare = null;
            s_Surge = 0f;
            if (!s_Active || pad == null)
            {
                return;
            }
            s_Active = false;
            s_SentTo = System.IntPtr.Zero;
            s_SentLights = -1;
            s_SentMic = -1;
            s_SentRight = null;
            s_SentLeft = null;
            if (pad.IsDualSense)
            {
                pad.SendEffect(Packet(new Color32(0, 0, 64, 255), 1, 0, true, s_TriggerOff, s_TriggerOff));
            }
            else if (pad.IsDualShock4)
            {
                pad.SetLightbar(0, 0, 64);
            }
        }

        /// <summary>
        /// Once per controller: frees the lightbar from the controller's own start-up animation, so it takes the colours
        /// sent to it (valid flag 2 bit 1 "lightbar setup" + setup value 2 "light out", as the Linux DualSense driver does).
        /// </summary>
        private static byte[] ReleaseLightbar()
        {
            System.Array.Clear(s_Packet, 0, s_Packet.Length);
            s_Packet[38] = 0x02;
            s_Packet[41] = 0x02;
            return s_Packet;
        }

        /// <summary>
        /// The DS5 effects state: the adaptive triggers (always, "off" when not in use) and, with <paramref name="lightsOn"/>,
        /// the lightbar, player LEDs and mute light. Rumble is never touched here.
        /// </summary>
        private static byte[] Packet(Color32 c, int playerLights, int mic, bool lightsOn, byte[] right, byte[] left)
        {
            System.Array.Clear(s_Packet, 0, s_Packet.Length);
            s_Packet[0] = 0x04 | 0x08; // enable: right trigger effect, left trigger effect
            System.Array.Copy(right, 0, s_Packet, 10, DualSenseTriggers.Size);
            System.Array.Copy(left, 0, s_Packet, 21, DualSenseTriggers.Size);
            if (!lightsOn)
            {
                return s_Packet;
            }
            s_Packet[1] = 0x01 | 0x04 | 0x10; // enable: mute light, lightbar colour, player lights
            s_Packet[8] = (byte)mic;
            s_Packet[43] = (byte)(PlayerBits(playerLights) | 0x20); // 0x20: switch at once (no fade-in)
            s_Packet[44] = c.r;
            s_Packet[45] = c.g;
            s_Packet[46] = c.b;
            return s_Packet;
        }

        /// <summary>The five player LEDs as the PS5 shows player numbers: 1 = centre, 2 = inner pair, ... 5 = all.</summary>
        private static int PlayerBits(int n)
        {
            switch (n)
            {
                case 0: return 0x00;
                case 1: return 0x04;
                case 2: return 0x0A;
                case 3: return 0x15;
                case 4: return 0x1B;
                default: return 0x1F;
            }
        }

        /// <summary>A colour along the palette at <paramref name="t"/> 0..1 (smooth); <paramref name="loop"/> wraps back to the first.</summary>
        private static Color Sample(Color[] colors, float t, bool loop)
        {
            if (colors.Length == 1)
            {
                return colors[0];
            }
            int n = loop ? colors.Length : colors.Length - 1;
            float x = Mathf.Clamp01(t) * n;
            int i = Mathf.Min((int)x, n - 1);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            return Color.Lerp(colors[i % colors.Length], colors[(i + 1) % colors.Length], f);
        }

        private static float Pulse(float t, float start, float length)
        {
            float x = (t - start) / length;
            return x < 0f || x > 1f ? 0f : Mathf.Sin(x * Mathf.PI);
        }

        private static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Pow(Mathf.Clamp01(v), Gamma) * 255f);

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;
    }
}
