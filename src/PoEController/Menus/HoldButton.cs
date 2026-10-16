using PoEController.Input;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// One button that does two things in a menu: held for <see cref="Seconds"/> it commits the screen's main action
    /// (trade, rest, delete; the same gesture as ending a turn), a quick tap does its secondary action, on release so a
    /// hold never fires the tap too. <see cref="Progress"/> fills the prompt bar while it is held.
    /// </summary>
    internal sealed class HoldButton
    {
        public enum Result
        {
            None,
            /// <summary>Held long enough: commit (once per press).</summary>
            Held,
            /// <summary>Let go before the hold time.</summary>
            Tapped,
        }

        /// <summary>The same as ending a turn with a held X.</summary>
        public const float Seconds = 0.5f;

        private readonly PadButton m_Button;
        private bool m_Down;
        private bool m_Fired;
        private float m_Time;

        public HoldButton(PadButton button)
        {
            m_Button = button;
        }

        /// <summary>0..1 while held and not yet fired (for the prompt bar's fill), else 0.</summary>
        public float Progress => m_Down && !m_Fired ? Mathf.Clamp01(m_Time / Seconds) : 0f;

        /// <summary>Pressed this frame (to remember what the tap would act on).</summary>
        public bool PressedNow { get; private set; }

        /// <summary>Once per frame. <paramref name="canCommit"/> = the held action is possible now.</summary>
        public Result Update(Gamepad pad, float dt, bool canCommit)
        {
            PressedNow = pad.GetButtonDown(m_Button);
            if (PressedNow)
            {
                m_Down = true;
                m_Fired = false;
                m_Time = 0f;
            }
            if (!m_Down)
            {
                return Result.None;
            }
            if (pad.GetButton(m_Button))
            {
                m_Time += dt;
                if (!m_Fired && canCommit && m_Time >= Seconds)
                {
                    m_Fired = true;
                    return Result.Held;
                }
                return Result.None;
            }
            // Let go (or suppressed): a tap only if it was short and nothing fired.
            bool tapped = !m_Fired && m_Time < Seconds;
            Reset();
            return tapped ? Result.Tapped : Result.None;
        }

        public void Reset()
        {
            m_Down = false;
            m_Fired = false;
            m_Time = 0f;
        }
    }
}
