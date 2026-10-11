using PoEController.Input;
using PoEController.Tutorials;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// On a timed tutorial box (IMGUI over the game's NGUI box): a gold reading-time bar along its top and bottom
    /// edges that shrinks from both ends toward the middle, and "L3 Read / B Dismiss" just below it.
    /// </summary>
    internal sealed class TutorialView
    {
        private const float RefHeight = 1080f;
        private static readonly Color Track = new Color(0.05f, 0.04f, 0.03f, 0.75f);
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f, 1f);
        private static readonly Color GoldEdge = new Color(0.55f, 0.4f, 0.15f, 1f);

        private readonly PromptBar m_Text;

        public TutorialView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(GlyphFamily family, bool showDismiss, float scale)
        {
            if (TutorialFlow.Mode != TutorialFlow.State.Timed || Event.current.type != EventType.Repaint
                || !TutorialFlow.TryGetBoxRect(out Rect box))
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            float t = Mathf.Max(3f, 5f * s);
            float gap = 4f * s;
            float frac = TutorialFlow.Fraction;
            Bar(new Rect(box.x, box.y - gap - t, box.width, t), frac);
            Bar(new Rect(box.x, box.yMax + gap, box.width, t), frac);

            // Hints under the box, or above it when it sits at the bottom of the screen.
            float lineH = m_Text.PromptLineHeight;
            float spacing = 24f * s;
            float wRead = m_Text.MeasurePrompt(family, "lstick_click", "Read");
            float wDismiss = showDismiss ? m_Text.MeasurePrompt(family, "east", "Dismiss") : 0f;
            float total = wRead + (showDismiss ? spacing + wDismiss : 0f);
            float midY = box.yMax + gap + t + gap + lineH * 0.75f;
            if (midY + lineH > Screen.height - 8f)
            {
                midY = box.y - gap - t - gap - lineH * 0.75f;
            }
            float x = Mathf.Clamp(box.center.x - total * 0.5f, 8f, Mathf.Max(8f, Screen.width - 8f - total));
            m_Text.DrawHudStrip(new Rect(x - lineH * 0.6f, midY - lineH * 0.75f, total + lineH * 1.2f, lineH * 1.5f));
            m_Text.DrawPromptAt(family, x, midY, "lstick_click", "Read");
            if (showDismiss)
            {
                m_Text.DrawPromptAt(family, x + wRead + spacing, midY, "east", "Dismiss");
            }
        }

        /// <summary>Dark track with the gold remainder centred on it, so it closes in from both ends.</summary>
        private static void Bar(Rect r, float frac)
        {
            Fill(r, Track);
            float w = r.width * frac;
            if (w < 1f)
            {
                return;
            }
            Rect fill = new Rect(r.center.x - w * 0.5f, r.y, w, r.height);
            Fill(fill, Gold);
            // Darker lower edge: the beveled look of the game's gold trim.
            Fill(new Rect(fill.x, fill.yMax - Mathf.Max(1f, r.height * 0.3f), fill.width, Mathf.Max(1f, r.height * 0.3f)), GoldEdge);
        }

        private static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
