using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// Brings the game's own hover tooltips up sooner on the controller. NGUI starts a timer (the Options tooltip
    /// delay) when the cursor rests on an element and shows the tooltip when it runs out; once our cursor has rested
    /// on the same element for <see cref="Delay"/> we only shorten a timer NGUI already started. NGUI still decides
    /// everything else (a click cancels it, moving away hides it, elements without a tooltip show nothing).
    /// </summary>
    internal static class TooltipAssist
    {
        private static readonly FieldInfo s_TooltipTime = AccessTools.Field(typeof(UICamera), "mTooltipTime");
        private static readonly FieldInfo s_Hover = AccessTools.Field(typeof(UICamera), "mHover");

        private static bool s_Broken => !Compat.Features.On(Compat.Features.QuickTooltips);
        private static GameObject s_Hovered;
        private static Vector2 s_CursorAt;
        private static float s_RestTime;
        private static bool s_Done;

        public static bool Enabled = true;
        public static float Delay = 0.4f;

        /// <summary>Call every frame while a cursor menu is active on the controller.</summary>
        public static void Update(Vector2 cursor, float dt)
        {
            if (!Enabled || s_Broken)
            {
                return;
            }
            if (s_TooltipTime == null || s_Hover == null)
            {
                Compat.Features.Off(Compat.Features.QuickTooltips, "NGUI tooltip fields not found");
                return;
            }
            GameObject hovered = s_Hover.GetValue(null) as GameObject;
            if (hovered != s_Hovered || (cursor - s_CursorAt).sqrMagnitude > 1f)
            {
                s_Hovered = hovered;
                s_CursorAt = cursor;
                s_RestTime = 0f;
                s_Done = false;
                return;
            }
            if (s_Done || hovered == null)
            {
                return;
            }
            s_RestTime += dt;
            if (s_RestTime < Delay)
            {
                return;
            }
            s_Done = true;
            UICamera handler = UICamera.eventHandler;
            if (handler == null)
            {
                return;
            }
            // 0 = no tooltip wanted (e.g. after a click); a time in the future = NGUI is waiting. Only shorten the wait.
            float due = (float)s_TooltipTime.GetValue(handler);
            if (due > Time.realtimeSinceStartup)
            {
                s_TooltipTime.SetValue(handler, 0.0001f);
            }
        }

        public static void Reset()
        {
            s_Hovered = null;
            s_RestTime = 0f;
            s_Done = false;
        }
    }
}
