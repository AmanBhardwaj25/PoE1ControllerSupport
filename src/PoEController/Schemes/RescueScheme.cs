using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// The way out when the pad's controls don't fit what is on screen (a window the mod lost track of, a screen whose
    /// controls stopped): hold View for a second, on any screen, and the pad becomes a plain mouse. The router puts
    /// it above every other scheme while it is on, so it works whatever the mod thinks the screen is. LS moves the
    /// pointer, A left click, X right click, RS scrolls, B is the game's Escape (close / back). Hold View again to
    /// leave; nothing else turns it off.
    /// </summary>
    public sealed class RescueScheme : ControlScheme
    {
        public const float HoldSeconds = 1f;
        private const float CursorSpeedAt1080 = 1400f;
        private const float ScrollNotchesPerSecond = 14f;

        private Vector2 m_Pos;
        private bool m_PosValid;
        private float m_ScrollAccumulator;

        /// <summary>The rescue cursor is on (toggled by holding View).</summary>
        public static bool On { get; private set; }

        public override string Name => "Rescue cursor";

        /// <summary>Hold View (alone) for <see cref="HoldSeconds"/>: rescue cursor on / off. Call once per frame, before picking a scheme.</summary>
        public static void UpdateToggle(Gamepad pad, string screen)
        {
            if (!pad.Connected || !pad.GetButton(PadButton.Back) || pad.GetButton(PadButton.Start)
                || pad.GetHeldTime(PadButton.Back) < HoldSeconds)
            {
                return;
            }
            // View is still down: kept from the next frame (in the world its release would open the map).
            pad.SuppressHeldButtons();
            On = !On;
            Plugin.Log.Report(On
                ? $"Rescue cursor on (hold View), on screen '{screen}'"
                : "Rescue cursor off (hold View)");
            if (UISystemMessager.Instance != null)
            {
                UISystemMessager.Instance.PostMessage(On
                    ? "Rescue cursor: the left stick moves the pointer. Hold View to leave."
                    : "Rescue cursor off", Color.white);
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        public override void Activate()
        {
            m_PosValid = false;
            m_ScrollAccumulator = 0f;
            MouseEmulator.ReleaseAll();
        }

        public override void Deactivate()
        {
            MouseEmulator.ReleaseAll();
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            Vector2 stick = pad.LeftStick;
            if (stick == Vector2.zero)
            {
                m_PosValid = false;
            }
            else
            {
                if (!m_PosValid)
                {
                    // Sub-pixel position of our own; the OS cursor only holds whole pixels.
                    m_Pos = MouseEmulator.Position;
                    m_PosValid = true;
                }
                // Squared response: precise near the centre, fast at full tilt (as the menus' free cursor).
                m_Pos += stick.normalized * CursorSpeedAt1080 * (Screen.height / 1080f) * stick.sqrMagnitude * dt;
                m_Pos.x = Mathf.Clamp(m_Pos.x, 0f, Screen.width - 1);
                m_Pos.y = Mathf.Clamp(m_Pos.y, 0f, Screen.height - 1);
                MouseEmulator.MoveTo(m_Pos);
            }

            float y = pad.RightStick.y;
            if (Mathf.Abs(y) < 0.2f)
            {
                m_ScrollAccumulator = 0f;
            }
            else
            {
                m_ScrollAccumulator += y * ScrollNotchesPerSecond * dt;
                int notches = (int)m_ScrollAccumulator;
                if (notches != 0)
                {
                    m_ScrollAccumulator -= notches;
                    MouseEmulator.Scroll(notches);
                }
            }

            MouseEmulator.SetLeft(pad.GetButton(PadButton.South));
            MouseEmulator.SetRight(pad.GetButton(PadButton.West));
            if (pad.GetButton(PadButton.East))
            {
                VirtualControls.Hold(MappedControl.CLOSE_WINDOW);
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            into.Add(new Prompt("lstick", "Pointer"));
            into.Add(new Prompt("south", "Click"));
            into.Add(new Prompt("west", "Right-click"));
            into.Add(new Prompt("rstick_ud", "Scroll"));
            into.Add(new Prompt("east", "Escape"));
            into.Add(new Prompt("back", "Hold: leave rescue cursor"));
        }
    }
}
