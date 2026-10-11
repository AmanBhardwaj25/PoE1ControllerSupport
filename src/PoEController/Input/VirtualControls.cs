using System;

namespace PoEController.Input
{
    /// <summary>
    /// Lets the controller "press" the game's own rebindable actions (MappedControl). Patches on
    /// GameInput.GetControl*(MappedControl…) OR these states into the game's answers, so every feature driven this
    /// way runs through exactly the same code (and the same checks) as the bound keyboard key.
    ///
    /// Per frame: <see cref="BeginFrame"/>, then schemes call <see cref="Hold"/> for each action whose button is
    /// down, then <see cref="EndFrame"/> derives press/release edges, hold time and repeat.
    /// </summary>
    public static class VirtualControls
    {
        private static readonly int Count = (int)MappedControl.COUNT;
        private static readonly bool[] s_Held = new bool[Count];
        private static readonly bool[] s_PrevHeld = new bool[Count];
        private static readonly bool[] s_Down = new bool[Count];
        private static readonly bool[] s_Up = new bool[Count];
        private static readonly bool[] s_HandledDown = new bool[Count];
        private static readonly bool[] s_HandledUp = new bool[Count];
        private static readonly float[] s_HeldTime = new float[Count];
        private static readonly float[] s_RepeatTimer = new float[Count];
        private static readonly bool[] s_Repeat = new bool[Count];

        public static void BeginFrame()
        {
            Array.Copy(s_Held, s_PrevHeld, Count);
            Array.Clear(s_Held, 0, Count);
        }

        /// <summary>Marks an action as held for this frame.</summary>
        public static void Hold(MappedControl control)
        {
            s_Held[(int)control] = true;
        }

        /// <summary>
        /// Drops all held actions without producing release edges (call between BeginFrame and EndFrame). Used on
        /// scheme changes so an action held in the old context doesn't "release" into the new one.
        /// </summary>
        public static void Reset()
        {
            Array.Clear(s_Held, 0, Count);
            Array.Clear(s_PrevHeld, 0, Count);
        }

        public static void EndFrame(float unscaledDeltaTime)
        {
            float keyRepeatDelay = GameInput.Instance != null ? GameInput.Instance.KeyRepeatDelay : 0.25f;
            float keyRepeatTime = GameInput.Instance != null ? GameInput.Instance.KeyRepeatTime : 1f / 15f;
            for (int i = 0; i < Count; i++)
            {
                s_Down[i] = s_Held[i] && !s_PrevHeld[i];
                s_Up[i] = !s_Held[i] && s_PrevHeld[i];
                s_HandledDown[i] = false;
                s_HandledUp[i] = false;
                s_Repeat[i] = false;
                if (!s_Held[i])
                {
                    s_HeldTime[i] = 0f;
                    s_RepeatTimer[i] = 0f;
                    continue;
                }
                s_HeldTime[i] += unscaledDeltaTime;
                if (s_HeldTime[i] >= keyRepeatDelay)
                {
                    s_RepeatTimer[i] += unscaledDeltaTime;
                    if (s_RepeatTimer[i] >= keyRepeatTime)
                    {
                        s_RepeatTimer[i] = 0f;
                        s_Repeat[i] = true;
                    }
                }
            }
        }

        // Queries mirroring GameInput's semantics. 'handle' consumes the edge so later readers don't also act on it.

        internal static bool Down(MappedControl c, bool handle)
        {
            int i = (int)c;
            if (!InRange(i) || !s_Down[i] || s_HandledDown[i])
            {
                return false;
            }
            if (handle)
            {
                s_HandledDown[i] = true;
            }
            return true;
        }

        internal static bool Up(MappedControl c, bool handle)
        {
            int i = (int)c;
            if (!InRange(i) || !s_Up[i] || s_HandledUp[i])
            {
                return false;
            }
            if (handle)
            {
                s_HandledUp[i] = true;
            }
            return true;
        }

        internal static bool DownWithRepeat(MappedControl c, bool handle)
        {
            int i = (int)c;
            if (!InRange(i) || s_HandledDown[i] || !(s_Down[i] || s_Repeat[i]))
            {
                return false;
            }
            if (handle)
            {
                s_HandledDown[i] = true;
            }
            return true;
        }

        internal static bool Held(MappedControl c)
        {
            int i = (int)c;
            return InRange(i) && s_Held[i];
        }

        internal static bool HeldLong(MappedControl c)
        {
            int i = (int)c;
            float holdTime = GameInput.Instance != null ? GameInput.Instance.HoldTime : 0.08f;
            return InRange(i) && s_Held[i] && s_HeldTime[i] >= holdTime;
        }

        private static bool InRange(int i) => i >= 0 && i < Count;
    }
}
