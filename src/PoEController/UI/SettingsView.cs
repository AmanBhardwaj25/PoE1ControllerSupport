using PoEController.Settings;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Draws the controller settings panel (IMGUI): a gold-framed panel in the middle of the screen with section
    /// headers, one row per setting (name left, value right, < > hints on the focused one), a scrolling window around
    /// the focus, and the focused setting's description at the bottom.
    /// </summary>
    internal sealed class SettingsView
    {
        private const float RefHeight = 1080f;
        private const int VisibleRows = 13;
        private static readonly Color TitleColor = new Color(0.95f, 0.85f, 0.6f);
        private static readonly Color HeaderColor = new Color(0.85f, 0.7f, 0.4f);
        private static readonly Color WarningColor = new Color(1f, 0.55f, 0.35f);
        private static readonly Color TextColor = new Color(0.92f, 0.88f, 0.8f);
        private static readonly Color ValueColor = new Color(0.98f, 0.9f, 0.65f);
        private static readonly Color DescColor = new Color(0.8f, 0.77f, 0.7f);
        private static readonly Color RowFillFocus = new Color(0.32f, 0.25f, 0.12f, 0.95f);

        private readonly PromptBar m_Text;

        public SettingsView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(float scale)
        {
            if (!SettingsPanel.IsOpen || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            float pad = 26f * s;
            float rowH = 40f * s;
            float titleH = 46f * s;
            float descH = 74f * s;
            float panelW = Mathf.Min(820f * s, Screen.width - 32f);
            int rows = Mathf.Min(VisibleRows, SettingsPanel.Rows.Count);
            float panelH = pad * 2f + titleH + rows * rowH + descH;
            float bottom = Screen.height - m_Text.MenuBarBottomSpace - 12f * s;
            Rect panel = new Rect((Screen.width - panelW) * 0.5f, Mathf.Max(16f, Mathf.Min((Screen.height - panelH) * 0.5f, bottom - panelH)), panelW, panelH);
            m_Text.DrawPanel(panel);

            float x = panel.x + pad;
            float w = panelW - pad * 2f;
            float y = panel.y + pad;
            m_Text.DrawText(new Rect(x, y, w, titleH), "Controller settings", 1.1f, TextAnchor.MiddleCenter, TitleColor);
            y += titleH;

            // Keep the focused row inside the visible window.
            int first = Mathf.Clamp(SettingsPanel.Focus - rows / 2, 0, Mathf.Max(0, SettingsPanel.Rows.Count - rows));
            for (int i = first; i < first + rows; i++)
            {
                SettingsPanel.Row row = SettingsPanel.Rows[i];
                Rect r = new Rect(x, y, w, rowH);
                if (row.Warning)
                {
                    m_Text.DrawText(new Rect(r.x, r.y + 4f * s, r.width, r.height - 4f * s), row.Label, 0.68f, TextAnchor.MiddleCenter, WarningColor);
                }
                else if (row.Entry == null)
                {
                    m_Text.DrawText(new Rect(r.x, r.y + 6f * s, r.width, r.height - 6f * s), row.Label, 0.8f, TextAnchor.MiddleLeft, HeaderColor);
                }
                else
                {
                    bool focus = i == SettingsPanel.Focus;
                    if (focus)
                    {
                        Fill(r, RowFillFocus);
                    }
                    Rect inner = new Rect(r.x + 18f * s, r.y, r.width - 30f * s, r.height);
                    m_Text.DrawText(inner, row.Label, 0.85f, TextAnchor.MiddleLeft, TextColor);
                    string value = SettingsPanel.ValueText(row.Entry);
                    m_Text.DrawText(inner, focus ? "<  " + value + "  >" : value, 0.85f, TextAnchor.MiddleRight, ValueColor);
                }
                y += rowH;
            }

            string desc = SettingsPanel.Description(SettingsPanel.FocusedEntry);
            m_Text.DrawText(new Rect(x, y + 8f * s, w, descH - 8f * s), desc, 0.7f, TextAnchor.UpperLeft, DescColor);
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
