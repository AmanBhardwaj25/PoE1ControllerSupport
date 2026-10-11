using System.Collections.Generic;
using PoEController.Input;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Reading a tutorial (<see cref="Tutorials.TutorialFlow"/> state Focused; the game is paused for it): the
    /// pointer moves onto the tutorial box like any menu — d-pad snaps between its close button and checkboxes
    /// (gold focus frame, tooltips, highlighted words), LS moves the cursor, A clicks, RS scrolls long text — and
    /// B closes it (the close button's own <c>Hide</c>, which also chains to a follow-up tutorial).
    /// </summary>
    public sealed class TutorialScheme : MenuScheme
    {
        public TutorialScheme()
            : base("Tutorial")
        {
        }

        protected override Transform NavigationRoot(in SchemeContext ctx)
        {
            UITutorialBox box = UITutorialBox.Instance;
            return box != null ? box.transform : null;
        }

        /// <summary>Start on the close button.</summary>
        protected override Vector2 StartNear
        {
            get
            {
                UITutorialBox box = UITutorialBox.Instance;
                Camera ui = InGameUILayout.NGUICamera;
                GameObject close = box != null ? Tutorials.TutorialFlow.CloseButton(box) : null;
                if (close == null || ui == null)
                {
                    return base.StartNear;
                }
                Vector3 p = ui.WorldToScreenPoint(close.transform.position);
                return new Vector2(p.x, p.y);
            }
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            UITutorialBox box = UITutorialBox.Instance;
            if (box != null && pad.GetButtonDown(PadButton.East))
            {
                box.Hide();
                return;
            }
            base.HandleInput(ctx, pad, dt);
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            base.GetPrompts(ctx, into);
            into.Add(new Prompt("east", "Close"));
            UITutorialBox box = UITutorialBox.Instance;
            if (box != null)
            {
                UI.ButtonHints.Add(Tutorials.TutorialFlow.CloseButton(box), "east");
            }
        }
    }
}
