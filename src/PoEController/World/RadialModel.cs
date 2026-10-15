using UnityEngine;

namespace PoEController.World
{
    /// <summary>One slot of a Console-style radial, as <see cref="UI.RadialView"/> draws it.</summary>
    internal struct RadialSlot
    {
        public string Name;
        /// <summary>Icon file in the plugin's radial folder (without ".png"); used when <see cref="Icon"/> is null.</summary>
        public string IconName;
        /// <summary>A game texture (ability / item icon).</summary>
        public Texture Icon;
        public bool Usable;
        public bool Active;
        public string Badge;
        public string Status;
        /// <summary>Empty slot: frame only, no icon or text.</summary>
        public bool Blank;
        /// <summary>A spell / ability / item: its description is shown beside the radial (<see cref="RadialInfo"/>).</summary>
        public AbilityWheel.Entry Info;
    }

    /// <summary>What <see cref="UI.RadialView"/> needs from a radial (General Radial on LT, Combat Radial on RT).</summary>
    internal interface IRadialModel
    {
        bool IsOpen { get; }
        int Count { get; }
        RadialSlot GetSlot(int index);
        /// <summary>-1 = nothing highlighted.</summary>
        int Highlight { get; }
        CharacterStats Owner { get; }
        /// <summary>Under the portrait; null = the owner's name.</summary>
        string Title { get; }
        /// <summary>Red slot frames (Combat Radial) instead of gold.</summary>
        bool Combat { get; }

        /// <summary>Spread the entries around the ring's holes (party ring) instead of filling them from the top.</summary>
        bool SpreadOnHoles { get; }
    }

    /// <summary>
    /// Slot positions shared by drawing and stick picking: up to 12 entries sit in the ring's 12 holes (clockwise
    /// from the top, 30° apart, any unused holes stay empty); more than 12 spread evenly.
    /// </summary>
    internal static class RadialLayout
    {
        public const int Holes = 12;

        public static float Degrees(int index, int count, bool spreadOnHoles = false)
        {
            if (spreadOnHoles && count > 0 && count <= Holes)
            {
                // Evenly around, snapped to the nearest hole: 4 → 12/3/6/9 o'clock, 5 → 0, 2, 5, 7, 10.
                return Mathf.Round(index * (float)Holes / count) % Holes * (360f / Holes);
            }
            return index * Step(count);
        }

        private static float Step(int count) => count <= Holes ? 360f / Holes : 360f / count;

        /// <summary>The entry the stick points at (nearest used slot), or -1 for no entries.</summary>
        public static int Pick(Vector2 stick, int count, bool spreadOnHoles = false)
        {
            if (count <= 0)
            {
                return -1;
            }
            float angle = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg;
            if (angle < 0f)
            {
                angle += 360f;
            }
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(angle, Degrees(i, count, spreadOnHoles)));
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
