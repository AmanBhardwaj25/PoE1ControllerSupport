using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// A short message from the mod drawn on top of everything (a refusal such as "Not unlocked yet", or "Hold to
    /// trade"). The game's own message line sits at the bottom of the HUD, under full-screen windows such as the world
    /// map, so a message posted there from inside a window can go unseen.
    /// </summary>
    internal static class Toast
    {
        private const float Seconds = 2.5f;
        private const float FadeSeconds = 0.4f;
        private static readonly Color Background = new Color(0f, 0f, 0f, 0.8f);

        private static string s_Text;
        private static float s_Until;

        public static void Show(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            s_Text = text;
            s_Until = Time.unscaledTime + Seconds;
            Plugin.Log.LogInfo("Message: " + text);
        }

        /// <summary>Drawn last in OnGUI, so nothing covers it.</summary>
        public static void Draw(PromptBar bar)
        {
            if (s_Text == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float left = s_Until - Time.unscaledTime;
            if (left <= 0f)
            {
                s_Text = null;
                return;
            }
            float alpha = Mathf.Clamp01(left / FadeSeconds);
            float s = Screen.height / 1080f * bar.Scale;
            float w = bar.MeasureText(s_Text, 1f) + 48f * s;
            float h = 50f * s;
            Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.18f, w, h);
            Color old = GUI.color;
            GUI.color = new Color(Background.r, Background.g, Background.b, Background.a * alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            bar.DrawText(r, s_Text, 1f, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, alpha));
            GUI.color = old;
        }
    }
}
