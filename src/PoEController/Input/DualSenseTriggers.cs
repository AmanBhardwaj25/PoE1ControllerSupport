namespace PoEController.Input
{
    /// <summary>
    /// The DualSense's built-in adaptive trigger effects as the 11-byte trigger parameter blocks of its output report
    /// (the controller's own factory effects, as documented by the community: Off, Feedback, Weapon, Vibration).
    /// Positions are the trigger's travel in 10 zones, 0 = at rest, 9 = fully pressed; strengths 1..8.
    /// </summary>
    public static class DualSenseTriggers
    {
        public const int Size = 11;

        /// <summary>No effect: the trigger moves freely.</summary>
        public static byte[] Off() => new byte[Size] { 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        /// <summary>Resistance from <paramref name="position"/> to the end of the travel, <paramref name="strength"/> 1..8.</summary>
        public static byte[] Feedback(int position, int strength)
        {
            if (position < 0 || position > 9 || strength < 1)
            {
                return Off();
            }
            uint force = (uint)(Clamp(strength, 1, 8) - 1) & 7;
            uint forceZones = 0;
            uint activeZones = 0;
            for (int i = position; i < 10; i++)
            {
                forceZones |= force << (3 * i);
                activeZones |= 1u << i;
            }
            return Zones(0x21, activeZones, forceZones, 0);
        }

        /// <summary>
        /// Resistance along the whole travel, changing from <paramref name="fromStrength"/> at the top to
        /// <paramref name="toStrength"/> at the bottom (1..8 each; equal = steady). Each of the 10 zones gets its own
        /// strength, so a bow can get harder the further it is drawn.
        /// </summary>
        public static byte[] Ramp(int fromStrength, int toStrength)
        {
            uint forceZones = 0;
            for (int i = 0; i < 10; i++)
            {
                int s = Clamp((int)System.Math.Round(fromStrength + (toStrength - fromStrength) * (i / 9.0)), 1, 8);
                forceZones |= (uint)((s - 1) & 7) << (3 * i);
            }
            return Zones(0x21, 0x3ff, forceZones, 0);
        }

        /// <summary>
        /// A click point like a gun's trigger: resistance from zone <paramref name="start"/> (2..7) that gives way at
        /// <paramref name="end"/> (start+1..8), <paramref name="strength"/> 1..8.
        /// </summary>
        public static byte[] Weapon(int start, int end, int strength)
        {
            start = Clamp(start, 2, 7);
            end = Clamp(end, start + 1, 8);
            uint zones = (1u << start) | (1u << end);
            var d = new byte[Size];
            d[0] = 0x25;
            d[1] = (byte)(zones & 0xff);
            d[2] = (byte)((zones >> 8) & 0xff);
            d[3] = (byte)(Clamp(strength, 1, 8) - 1);
            return d;
        }

        /// <summary>The trigger vibrates from <paramref name="position"/> on, <paramref name="amplitude"/> 1..8, at <paramref name="frequency"/> Hz (1..255).</summary>
        public static byte[] Vibration(int position, int amplitude, int frequency)
        {
            if (position < 0 || position > 9 || amplitude < 1 || frequency < 1)
            {
                return Off();
            }
            uint strength = (uint)(Clamp(amplitude, 1, 8) - 1) & 7;
            uint amplitudeZones = 0;
            uint activeZones = 0;
            for (int i = position; i < 10; i++)
            {
                amplitudeZones |= strength << (3 * i);
                activeZones |= 1u << i;
            }
            return Zones(0x26, activeZones, amplitudeZones, (byte)Clamp(frequency, 1, 255));
        }

        public static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return a == b;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static byte[] Zones(byte mode, uint active, uint values, byte frequency)
        {
            var d = new byte[Size];
            d[0] = mode;
            d[1] = (byte)(active & 0xff);
            d[2] = (byte)((active >> 8) & 0xff);
            d[3] = (byte)(values & 0xff);
            d[4] = (byte)((values >> 8) & 0xff);
            d[5] = (byte)((values >> 16) & 0xff);
            d[6] = (byte)((values >> 24) & 0xff);
            d[9] = frequency;
            return d;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    }
}
