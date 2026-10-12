using System.Collections.Generic;
using PoEController.Input;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Conversations and scripted interactions: the window's own bindings (d-pad choose, A select / continue) plus
    /// RS = scroll the text back and forth and R3 = back to the newest line, as on the console version. Scrolling
    /// moves the window's own scroll panel (<c>ContentPanel</c> / <c>InteractionContentPanel</c>), kept inside its
    /// bounds the way a mouse drag is. Feature <c>ConversationScroll</c>; off = the bindings only.
    /// </summary>
    public sealed class ConversationScheme : BindingScheme
    {
        /// <summary>Scroll speed at full stick, in the panel's own units per second (the UI is about 1200 units tall).</summary>
        private const float ScrollSpeed = 900f;
        private const float Deadzone = 0.2f;

        public ConversationScheme(params Binding[] bindings)
            : base("Conversation", bindings)
        {
        }

        private static bool On => Compat.Features.On(Compat.Features.ConversationScroll);

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            if (!On)
            {
                return;
            }
            float y = pad.RightStick.y;
            if (Mathf.Abs(y) > Deadzone)
            {
                // Stick down = later text: the content moves up.
                float amount = -y * ScrollSpeed * dt;
                Compat.Features.Run(Compat.Features.ConversationScroll, () => Scroll(amount));
            }
            if (pad.GetButtonDown(PadButton.RightStick))
            {
                Compat.Features.Run(Compat.Features.ConversationScroll, ScrollToNewest);
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            base.GetPrompts(ctx, into);
            if (On)
            {
                into.Add(new Prompt("rstick_ud", "Scroll"));
                into.Add(new Prompt("rstick_click", "Newest"));
            }
        }

        /// <summary>The scroll panel of whichever window is showing (conversation or scripted interaction).</summary>
        private static UIDraggablePanel Panel()
        {
            UIConversationManager conv = UIConversationManager.Instance;
            if (conv == null)
            {
                return null;
            }
            UIPanel panel = conv.ConversationWindow != null && conv.ConversationWindow.activeInHierarchy
                ? conv.ContentPanel
                : conv.InteractionContentPanel;
            return panel != null ? panel.GetComponent<UIDraggablePanel>() : null;
        }

        private static void Scroll(float amount)
        {
            UIDraggablePanel drag = Panel();
            if (drag == null)
            {
                return;
            }
            drag.DisableSpring();
            drag.MoveRelative(new Vector3(0f, amount, 0f));
            drag.RestrictWithinBounds(true);
        }

        private static void ScrollToNewest()
        {
            UIDraggablePanel drag = Panel();
            if (drag == null)
            {
                return;
            }
            drag.SetDragAmount(0f, 1f, false);
            drag.RestrictWithinBounds(true);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }
    }
}
