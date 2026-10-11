using PoEController.World;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Draws <see cref="ActiveEffectsPanel"/>: a gold panel (the prompt bar's 9-slice) with "Name — Active Effects",
    /// then per effect its icon, name (red = hostile, green = beneficial), time left on the right and the
    /// character-sheet description wrapped below; rows from <see cref="ActiveEffectsPanel.Scroll"/> down as far as
    /// they fit, with "more above / below" hints. Sizes follow the screen height.
    /// </summary>
    internal sealed class ActiveEffectsView
    {
        private const float RefHeight = 1080f;
        private static readonly Color Hostile = new Color(0.95f, 0.45f, 0.4f);
        private static readonly Color Helpful = new Color(0.55f, 0.9f, 0.5f);
        private static readonly Color Body = new Color(0.86f, 0.82f, 0.72f);
        private static readonly Color Dim = new Color(0.7f, 0.66f, 0.58f);

        private readonly PromptBar m_Text;

        public ActiveEffectsView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(ActiveEffectsPanel panel, float scale)
        {
            if (!panel.IsOpen || panel.Owner == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            float w = Mathf.Min(760f * s, Screen.width - 32f);
            float h = Mathf.Min(640f * s, Screen.height - m_Text.MenuBarBottomSpace - 120f * s);
            Rect r = new Rect((Screen.width - w) * 0.5f, Mathf.Max(16f, (Screen.height - h) * 0.45f), w, h);
            m_Text.DrawPanel(r);

            float pad = 26f * s;
            float x = r.x + pad;
            float inner = r.width - pad * 2f;
            float y = r.y + pad;
            m_Text.DrawText(new Rect(x, y, inner, 40f * s), panel.Owner.Name() + "  -  Active Effects", 1.1f, TextAnchor.MiddleCenter, null);
            y += 52f * s;
            float bottom = r.yMax - pad;

            if (panel.Rows.Count == 0)
            {
                m_Text.DrawText(new Rect(x, y, inner, 36f * s), "No active effects", 0.9f, TextAnchor.MiddleCenter, Dim);
                return;
            }
            if (panel.Scroll > 0)
            {
                m_Text.DrawText(new Rect(x, y - 18f * s, inner, 18f * s), "more above", 0.7f, TextAnchor.MiddleCenter, Dim);
            }

            float icon = 40f * s;
            float textX = x + icon + 14f * s;
            float textW = inner - icon - 14f * s;
            int i = panel.Scroll;
            for (; i < panel.Rows.Count; i++)
            {
                ActiveEffectsPanel.Row row = panel.Rows[i];
                float descH = m_Text.MeasureWrappedHeight(row.Text, 0.75f, textW);
                float rowH = Mathf.Max(icon, 30f * s + descH) + 14f * s;
                if (y + rowH > bottom && i > panel.Scroll)
                {
                    break;
                }
                if (row.Icon != null)
                {
                    GUI.DrawTexture(new Rect(x, y, icon, icon), row.Icon, ScaleMode.ScaleToFit);
                }
                m_Text.DrawText(new Rect(textX, y, textW, 30f * s), row.Name, 0.95f, TextAnchor.MiddleLeft, row.Hostile ? Hostile : Helpful);
                if (row.Time != null)
                {
                    m_Text.DrawText(new Rect(textX, y, textW, 30f * s), row.Time, 0.85f, TextAnchor.MiddleRight, Dim);
                }
                m_Text.DrawText(new Rect(textX, y + 30f * s, textW, descH + 4f * s), row.Text, 0.75f, TextAnchor.UpperLeft, Body);
                y += rowH;
            }
            if (i < panel.Rows.Count)
            {
                m_Text.DrawText(new Rect(x, bottom - 4f * s, inner, 18f * s), "more below", 0.7f, TextAnchor.MiddleCenter, Dim);
            }
        }
    }
}
