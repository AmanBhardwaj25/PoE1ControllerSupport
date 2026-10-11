using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;
using PoEController.Settings;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// While the controller settings panel is open (<see cref="SettingsPanel"/>): d-pad / LS up-down choose, d-pad
    /// left-right change, A flips on/off and cycles choices, B closes. NGUI's own pad-A click is muted meanwhile.
    /// </summary>
    public sealed class SettingsScheme : ControlScheme
    {
        private const float StickPress = 0.6f;
        private const float StickRelease = 0.3f;

        private bool m_StickLatched;

        public override string Name => "Controller settings";

        public override void Activate()
        {
            MouseEmulator.ReleaseAll();
            SubmitGuard.Mute();
            m_StickLatched = true;
        }

        public override void Deactivate()
        {
            SubmitGuard.Restore(force: true);
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            SubmitGuard.Mute();
            if (pad.GetButtonDown(PadButton.East))
            {
                SettingsPanel.Close();
                pad.SuppressHeldButtons();
                return;
            }
            int move = 0;
            if (pad.GetButtonRepeat(PadButton.DpadUp))
            {
                move = -1;
            }
            else if (pad.GetButtonRepeat(PadButton.DpadDown))
            {
                move = 1;
            }
            float y = pad.LeftStick.y;
            if (!m_StickLatched && Mathf.Abs(y) > StickPress)
            {
                m_StickLatched = true;
                move = y > 0f ? -1 : 1;
            }
            else if (Mathf.Abs(y) < StickRelease)
            {
                m_StickLatched = false;
            }
            if (move != 0)
            {
                SettingsPanel.MoveFocus(move);
            }
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                SettingsPanel.Change(-1);
            }
            else if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                SettingsPanel.Change(1);
            }
            else if (pad.GetButtonDown(PadButton.South))
            {
                SettingsPanel.Activate();
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            into.Add(new Prompt("dpad_ud", "Choose"));
            into.Add(new Prompt("dpad_lr", "Change"));
            if (SettingsPanel.CanActivate)
            {
                into.Add(new Prompt("south", "Toggle"));
            }
            into.Add(new Prompt("east", "Close"));
        }
    }
}
