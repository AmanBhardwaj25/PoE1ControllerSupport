using System.Text;
using PoEController.Context;
using PoEController.Input;
using UnityEngine;

namespace PoEController
{
    /// <summary>IMGUI overlay (F7) for checking controller detection, glyphs, device switching and screen tracking.</summary>
    internal static class DebugOverlay
    {
        private const float GlyphSize = 32f;
        private static readonly StringBuilder s_Sb = new StringBuilder();
        private static GUIStyle s_Style;

        public static void Draw(Plugin plugin)
        {
            if (s_Style == null)
            {
                s_Style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            }
            Gamepad pad = plugin.Pad;

            s_Sb.Length = 0;
            s_Sb.AppendLine("<b>PoE Controller " + Plugin.Version + "</b>");
            s_Sb.AppendLine(pad.Connected ? $"Pad: {pad.Name} ({pad.Type})" : "Pad: <color=orange>none connected</color>");
            s_Sb.AppendLine($"Glyphs: {plugin.ActiveGlyphFamily}   Device: <b>{plugin.Mode.Active}</b>");
            s_Sb.AppendLine($"LS {pad.LeftStick.x:+0.00;-0.00} {pad.LeftStick.y:+0.00;-0.00}   RS {pad.RightStick.x:+0.00;-0.00} {pad.RightStick.y:+0.00;-0.00}   LT {pad.LeftTrigger:0.00} RT {pad.RightTrigger:0.00}");
            var router = plugin.Router;
            s_Sb.AppendLine($"Scheme: <b>{(router.Active != null ? router.Active.Name : "none")}</b>   Paused: {router.Context.Paused}   Combat: {router.Context.InCombat}");
            s_Sb.Append("Screens: ");
            var stack = ScreenTracker.Stack;
            if (stack.Count == 0)
            {
                s_Sb.Append("World");
            }
            for (int i = 0; i < stack.Count; i++)
            {
                s_Sb.Append(i == stack.Count - 1 ? "<b>" + stack[i].GetType().Name + "</b>" : stack[i].GetType().Name + " > ");
            }

            Rect box = new Rect(10f, 10f, 560f, 160f);
            GUI.Box(box, GUIContent.none);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 8f, box.y + 6f, box.width - 16f, 120f), s_Sb.ToString(), s_Style);

            // Held buttons, drawn with the active glyph set.
            float x = box.x + 8f;
            for (int i = 0; i < Gamepad.ButtonCount; i++)
            {
                PadButton b = (PadButton)i;
                if (!pad.GetButton(b))
                {
                    continue;
                }
                Texture2D tex = plugin.Glyphs.Get(plugin.ActiveGlyphFamily, b);
                Rect r = new Rect(x, box.yMax - GlyphSize - 6f, GlyphSize, GlyphSize);
                if (tex != null)
                {
                    GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
                }
                else
                {
                    GUI.Label(r, b.ToString(), s_Style);
                }
                x += GlyphSize + 4f;
            }
        }
    }
}
