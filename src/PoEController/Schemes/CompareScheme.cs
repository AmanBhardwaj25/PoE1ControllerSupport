using System.Collections.Generic;
using PoEController.Compare;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// While a comparison is open (<see cref="CompareSession"/>; the router picks this over the inspect windows).
    /// Picking: d-pad ←/→ (or LB/RB) changes companion with the comparison updating live, A keeps it, B cancels.
    /// Viewing: B closes the comparison, X picks another companion / item, RS scrolls the comparison text.
    /// </summary>
    public sealed class CompareScheme : ControlScheme
    {
        private const float ScrollNotchesPerSecond = 14f;

        private float m_ScrollAccumulator;

        public override string Name => "Compare";

        public override void Activate()
        {
            MouseEmulator.ReleaseAll();
            SubmitGuard.Mute();
            m_ScrollAccumulator = 0f;
        }

        public override void Deactivate()
        {
            PopupMenu.Close();
            SubmitGuard.Restore(force: true);
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            if (PopupMenu.HandleInput(pad))
            {
                return;
            }
            SubmitGuard.Mute();
            CompareSession.Safe(() => HandleSession(pad, dt), "Compare input");
        }

        private void HandleSession(Gamepad pad, float dt)
        {
            switch (CompareSession.Mode)
            {
                case CompareSession.Stage.Picking:
                    if (pad.GetButtonRepeat(PadButton.DpadLeft) || pad.GetButtonDown(PadButton.LeftShoulder))
                    {
                        CompareSession.Step(-1);
                    }
                    else if (pad.GetButtonRepeat(PadButton.DpadRight) || pad.GetButtonDown(PadButton.RightShoulder))
                    {
                        CompareSession.Step(1);
                    }
                    else if (pad.GetButtonDown(PadButton.South))
                    {
                        CompareSession.Keep();
                    }
                    else if (pad.GetButtonDown(PadButton.East))
                    {
                        CompareSession.End();
                    }
                    break;
                case CompareSession.Stage.Viewing:
                    if (pad.GetButtonDown(PadButton.East))
                    {
                        CompareSession.End();
                    }
                    else if (pad.GetButtonDown(PadButton.West))
                    {
                        if (CompareSession.FromInventory)
                        {
                            CompareSession.StartChoosing(CompareSession.Item, CompareSession.Source);
                        }
                        else
                        {
                            CompareSession.PickAgain();
                        }
                    }
                    Scroll(pad.RightStick.y, dt);
                    break;
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            if (PopupMenu.IsOpen)
            {
                InspectScheme.AddPopupPrompts(into);
                return;
            }
            if (CompareSession.Mode == CompareSession.Stage.Picking)
            {
                into.Add(new Prompt("dpad_lr", "Companion"));
                into.Add(new Prompt("south", "Keep"));
                into.Add(new Prompt("east", "Cancel"));
            }
            else
            {
                into.Add(new Prompt("west", CompareSession.ChangeLabel));
                into.Add(new Prompt("rstick_ud", "Scroll"));
                into.Add(new Prompt("east", "Close"));
            }
        }

        /// <summary>The inspect text scrolls with the mouse wheel over it: point at the comparison window and turn.</summary>
        private void Scroll(float stickY, float dt)
        {
            UIItemInspectManager w = CompareSession.FirstWindow;
            if (Mathf.Abs(stickY) < 0.01f || w == null || !w.WindowActive())
            {
                m_ScrollAccumulator = 0f;
                return;
            }
            Rect r = CompareMenus.WindowRect(w);
            MouseEmulator.MoveTo(new Vector2(r.center.x, Screen.height - r.center.y));
            m_ScrollAccumulator += stickY * ScrollNotchesPerSecond * dt;
            int notches = (int)m_ScrollAccumulator;
            if (notches != 0)
            {
                m_ScrollAccumulator -= notches;
                MouseEmulator.Scroll(notches);
            }
        }
    }
}
