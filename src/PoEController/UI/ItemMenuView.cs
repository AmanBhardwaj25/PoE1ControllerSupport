using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Draws the inventory item menu (IMGUI): a gold-framed panel beside the item with its name and one row per
    /// action; unavailable actions are dimmed with the reason on the right.
    /// </summary>
    internal sealed class ItemMenuView
    {
        private const float RefHeight = 1080f;
        private static readonly Color RowFillFocus = new Color(0.32f, 0.25f, 0.12f, 0.95f);
        private static readonly Color TitleColor = new Color(0.95f, 0.85f, 0.6f);
        private static readonly Color TextColor = new Color(0.92f, 0.88f, 0.8f);
        private static readonly Color DisabledColor = new Color(0.55f, 0.52f, 0.48f);
        private static readonly Color ReasonColor = new Color(0.75f, 0.45f, 0.38f);

        private readonly PromptBar m_Text;

        public ItemMenuView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(IListMenu menu, float scale)
        {
            if (!menu.MenuOpen || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            float pad = 22f * s;
            float rowH = 42f * s;
            float titleH = 40f * s;
            float gap = 18f * s;
            const float LabelSize = 0.9f;
            const float ReasonSize = 0.75f;

            // Width: widest row (label + reason) or the title.
            float w = m_Text.MeasureText(menu.Title ?? "", 1f);
            for (int i = 0; i < menu.Count; i++)
            {
                string label = menu.LabelAt(i);
                string disabled = menu.DisabledAt(i);
                float rw = m_Text.MeasureText(label, LabelSize) + 16f * s;
                if (disabled != null)
                {
                    rw += gap + m_Text.MeasureText(disabled, ReasonSize);
                }
                w = Mathf.Max(w, rw);
            }
            float panelW = Mathf.Min(w + pad * 2f, Screen.width - 32f);
            float panelH = pad * 2f + titleH + menu.Count * rowH;

            // Beside the item: right if it fits, else left; vertically centred on it, kept on screen and above the bar.
            Rect a = menu.Anchor;
            float x = a.xMax + 12f * s;
            if (x + panelW > Screen.width - 16f)
            {
                x = a.xMin - 12f * s - panelW;
            }
            x = Mathf.Clamp(x, 16f, Mathf.Max(16f, Screen.width - 16f - panelW));
            float bottomLimit = Screen.height - m_Text.MenuBarBottomSpace - 8f * s;
            float y = Mathf.Clamp(a.center.y - panelH * 0.5f, 16f, Mathf.Max(16f, bottomLimit - panelH));
            Rect panel = new Rect(x, y, panelW, panelH);
            m_Text.DrawPanel(panel);

            float innerX = panel.x + pad;
            float innerW = panelW - pad * 2f;
            float rowY = panel.y + pad;
            m_Text.DrawText(new Rect(innerX, rowY, innerW, titleH), menu.Title ?? "", 1f, TextAnchor.MiddleLeft, TitleColor);
            rowY += titleH;

            for (int i = 0; i < menu.Count; i++)
            {
                string label = menu.LabelAt(i);
                string disabled = menu.DisabledAt(i);
                Rect row = new Rect(innerX, rowY, innerW, rowH);
                bool focus = i == menu.Focus;
                if (focus)
                {
                    Fill(row, RowFillFocus);
                }
                Rect text = new Rect(row.x + 8f * s, row.y, row.width - 16f * s, row.height);
                m_Text.DrawText(text, label, LabelSize, TextAnchor.MiddleLeft, disabled == null ? TextColor : DisabledColor);
                if (disabled != null)
                {
                    m_Text.DrawText(text, disabled, ReasonSize, TextAnchor.MiddleRight, ReasonColor);
                }
                rowY += rowH;
            }
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
