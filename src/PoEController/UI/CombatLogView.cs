using PoEController.World;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Draws <see cref="CombatLogPanel"/>: a gold panel "Combat log", lines in the game's colours drawn upward from
    /// <see cref="CombatLogPanel.Bottom"/> (newest at the bottom), children indented, detailed text when toggled;
    /// "more above / below" hints. Sizes follow the screen height.
    /// </summary>
    internal sealed class CombatLogView
    {
        private const float RefHeight = 1080f;
        private static readonly Color Dim = new Color(0.7f, 0.66f, 0.58f);

        private readonly PromptBar m_Text;

        public CombatLogView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(CombatLogPanel log, float scale)
        {
            if (!log.IsOpen || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            float w = Mathf.Min(900f * s, Screen.width - 32f);
            float h = Mathf.Min(700f * s, Screen.height - m_Text.MenuBarBottomSpace - 100f * s);
            Rect r = new Rect((Screen.width - w) * 0.5f, Mathf.Max(16f, (Screen.height - h) * 0.45f), w, h);
            m_Text.DrawPanel(r);
            float pad = 26f * s;
            float x = r.x + pad;
            float inner = r.width - pad * 2f;
            m_Text.DrawText(new Rect(x, r.y + pad, inner, 40f * s), log.Details ? "Combat log (details)" : "Combat log", 1.1f, TextAnchor.MiddleCenter, null);
            float top = r.y + pad + 52f * s;
            float y = r.yMax - pad;
            if (log.Lines.Count == 0)
            {
                m_Text.DrawText(new Rect(x, top, inner, 36f * s), "Nothing logged yet", 0.9f, TextAnchor.MiddleCenter, Dim);
                return;
            }
            if (log.Bottom < log.Lines.Count - 1)
            {
                m_Text.DrawText(new Rect(x, y - 2f * s, inner, 18f * s), "more below", 0.7f, TextAnchor.MiddleCenter, Dim);
                y -= 20f * s;
            }
            int i = log.Bottom;
            for (; i >= 0; i--)
            {
                CombatLogPanel.Line line = log.Lines[i];
                string text = log.Details && line.Verbose != null ? line.Verbose : line.Text;
                float indent = line.Child ? 28f * s : 0f;
                float lh = m_Text.MeasureWrappedHeight(text, 0.8f, inner - indent) + 4f * s;
                if (y - lh < top && i < log.Bottom)
                {
                    break;
                }
                y -= lh;
                m_Text.DrawText(new Rect(x + indent, y, inner - indent, lh), text, 0.8f, TextAnchor.UpperLeft, line.Color);
            }
            if (i >= 0)
            {
                m_Text.DrawText(new Rect(x, top - 20f * s, inner, 18f * s), "more above", 0.7f, TextAnchor.MiddleCenter, Dim);
            }
        }
    }
}
