using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PoEController.Input;
using PoEController.Menus;
using OEIFormats.FlowCharts.Conversations;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Conversations and scripted interactions: the window's own bindings (d-pad choose, A select / continue) plus
    /// RS = scroll the text back and forth and R3 = back to the newest line, as on the console version. Scrolling
    /// moves the window's own scroll panel (<c>ContentPanel</c> / <c>InteractionContentPanel</c>), kept inside its
    /// bounds the way a mouse drag is. Feature <c>ConversationScroll</c>; off = the bindings only.
    /// <para>
    /// The cursor sits on a response (feature <c>ConversationFocus</c>): new responses put it on the first one that
    /// can be chosen, the d-pad moves it up / down (wrapping). The game highlights and picks the response under the
    /// mouse, so cursor, highlight and A always agree; the game's own d-pad selection (which the mouse cancels) isn't used.
    /// </para>
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

        private static bool FocusOn => Compat.Features.On(Compat.Features.ConversationFocus);

        private static readonly FieldInfo s_Outstanding = AccessTools.Field(typeof(UIConversationManager), "m_OutstandingResponses");
        private static readonly FieldInfo s_MouseSelection = AccessTools.Field(typeof(UIConversationManager), "m_MouseSelection");

        private static readonly FieldInfo s_FlowChart = AccessTools.Field(typeof(UIConversationManager), "m_ActiveFlowChart");
        private static readonly MethodInfo s_Conversation = AccessTools.PropertyGetter(typeof(UIConversationManager), "conversation");
        private static readonly MethodInfo s_MarkedAsRead = AccessTools.Method(typeof(UIConversationManager), "MarkedAsRead", new[] { typeof(PlayerResponseNode) });

        /// <summary>When the window last wrote a new set of responses (a new dialogue node), see <see cref="ResponsesDrawn"/>.</summary>
        private static float s_DrawnTime = -1f;
        private static bool s_Pending;
        private bool m_HasResponses;

        /// <summary>The window wrote the responses of a new dialogue node (patch on its DrawResponses).</summary>
        public static void ResponsesDrawn()
        {
            s_Pending = true;
            s_DrawnTime = Time.unscaledTime;
        }

        public override void Activate()
        {
            base.Activate();
            m_HasResponses = false;
        }

        protected override bool TakesOver(PadButton button) =>
            m_HasResponses && FocusOn && (button == PadButton.DpadUp || button == PadButton.DpadDown);

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            if (FocusOn)
            {
                Compat.Features.Run(Compat.Features.ConversationFocus, () => FocusResponses(pad));
            }
            else
            {
                m_HasResponses = false;
            }
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

        private void FocusResponses(Gamepad pad)
        {
            UIConversationManager conv = UIConversationManager.Instance;
            UIConsoleText list = TextList(conv);
            int count = conv != null && s_Outstanding != null ? (int)s_Outstanding.GetValue(conv) : 0;
            m_HasResponses = list != null && count > 0 && list.ParagraphCount >= count;
            if (!m_HasResponses)
            {
                return;
            }
            int first = list.ParagraphCount - count;
            // New responses: placed once the window has laid them out and scrolled them in, then left to the player.
            if (s_Pending)
            {
                if (Time.unscaledTime - s_DrawnTime < 0.1f)
                {
                    return;
                }
                s_Pending = false;
                int target = StartResponse(conv, count);
                Plugin.Log.LogInfo(PlaceOn(list, first + target)
                    ? $"Conversation: cursor on response {target + 1} of {count}"
                    : $"Conversation: response {target + 1} of {count} not found on screen");
                return;
            }
            int step = pad.GetButtonDown(PadButton.DpadDown) ? 1 : pad.GetButtonDown(PadButton.DpadUp) ? -1 : 0;
            if (step == 0)
            {
                return;
            }
            int current = s_MouseSelection != null ? (int)s_MouseSelection.GetValue(conv) : -1;
            int next = current < 0 || current >= count ? (step > 0 ? 0 : count - 1) : (current + step + count) % count;
            if (PlaceOn(list, first + next))
            {
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            }
        }

        /// <summary>
        /// Where the cursor starts: the first response that can be chosen and hasn't been read yet, else the first that
        /// can be chosen, else 0. Both are the game's own rules (PassesConditionalsEx greys out the rest, MarkedAsRead
        /// colours the questions already asked; responses without read tracking, such as leaving, count as unread).
        /// Responses past the node list (the interaction's own end line) count as open and unread.
        /// </summary>
        private static int StartResponse(UIConversationManager conv, int count)
        {
            Conversation c = s_Conversation != null ? s_Conversation.Invoke(conv, null) as Conversation : null;
            FlowChartPlayer player = s_FlowChart != null ? s_FlowChart.GetValue(conv) as FlowChartPlayer : null;
            if (c == null || player == null)
            {
                return 0;
            }
            List<PlayerResponseNode> nodes = c.GetResponseNodes(player);
            int firstOpen = -1;
            for (int r = 0; r < count; r++)
            {
                if (nodes == null || r >= nodes.Count)
                {
                    return firstOpen >= 0 ? firstOpen : r;
                }
                if (!c.PassesConditionalsEx(nodes[r], player))
                {
                    continue;
                }
                if (s_MarkedAsRead == null || !(bool)s_MarkedAsRead.Invoke(conv, new object[] { nodes[r] }))
                {
                    return r;
                }
                if (firstOpen < 0)
                {
                    firstOpen = r;
                }
            }
            return firstOpen >= 0 ? firstOpen : 0;
        }

        private static UIConsoleText TextList(UIConversationManager conv)
        {
            if (conv == null)
            {
                return null;
            }
            return conv.ConversationWindow != null && conv.ConversationWindow.activeInHierarchy
                ? conv.ConversationTextList
                : conv.InteractionTextList;
        }

        /// <summary>
        /// The cursor onto that paragraph: its place is found with the list's own <c>LineAt</c> (what the game uses for
        /// the mouse), scanning down the start of the lines inside the visible part. Not in view: scrolled to the newest
        /// lines (responses are last) and looked for again. False = not found.
        /// </summary>
        private static bool PlaceOn(UIConsoleText list, int paragraph)
        {
            if (FindParagraph(list, paragraph, out Vector2 at))
            {
                MouseEmulator.MoveTo(at);
                return true;
            }
            UIDraggablePanel drag = Panel();
            if (drag != null)
            {
                drag.SetDragAmount(0f, 1f, false);
                drag.RestrictWithinBounds(true);
            }
            if (FindParagraph(list, paragraph, out at))
            {
                MouseEmulator.MoveTo(at);
                return true;
            }
            return false;
        }

        private static bool FindParagraph(UIConsoleText list, int paragraph, out Vector2 at)
        {
            at = default;
            Camera cam = InGameUILayout.NGUICamera;
            UIPanel panel = NGUITools.FindInParents<UIPanel>(list.gameObject);
            if (cam == null || panel == null || !list.gameObject.activeInHierarchy)
            {
                return false;
            }
            // The visible part of the list (the panel's clip), on screen.
            float top = Screen.height, bottom = 0f;
            if (panel.clipping != UIDrawCall.Clipping.None)
            {
                Vector4 clip = panel.clipRange;
                Transform pt = panel.transform;
                top = cam.WorldToScreenPoint(pt.TransformPoint(new Vector3(clip.x, clip.y + clip.w * 0.5f, 0f))).y;
                bottom = cam.WorldToScreenPoint(pt.TransformPoint(new Vector3(clip.x, clip.y - clip.w * 0.5f, 0f))).y;
            }
            // Just inside the start of the lines (the response number).
            float x = cam.WorldToScreenPoint(list.transform.TransformPoint(new Vector3(12f, 0f, 0f))).x;
            float lo = float.MaxValue, hi = float.MinValue;
            for (float y = Mathf.Max(top, bottom) - 2f; y > Mathf.Min(top, bottom) + 2f; y -= 2f)
            {
                if (list.LineAt(new Vector3(x, y, 0f)) == paragraph)
                {
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                }
            }
            if (hi < lo)
            {
                return false;
            }
            at = new Vector2(x + 6f, (lo + hi) * 0.5f);
            return true;
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
