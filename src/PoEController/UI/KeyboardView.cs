using PoEController.TextEntry;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Draws the on-screen keyboard (IMGUI): a framed panel just above the prompt bar with what the box is for, the
    /// text so far (so it reads even when the panel covers the box), and the key grid.
    /// </summary>
    internal sealed class KeyboardView
    {
        private const float RefHeight = 1080f;
        private const int MaxPreviewLines = 4;
        private static readonly Color KeyFill = new Color(0.08f, 0.07f, 0.06f, 0.85f);
        private static readonly Color KeyFillFocus = new Color(0.32f, 0.25f, 0.12f, 0.95f);
        private static readonly Color TextFill = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color TitleColor = new Color(0.85f, 0.8f, 0.68f);
        private static readonly Color SpecialColor = new Color(0.85f, 0.8f, 0.68f);
        private static readonly Color OnColor = new Color(0.45f, 0.85f, 0.45f);

        private readonly PromptBar m_Text;

        public KeyboardView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(float scale)
        {
            TextTarget target = OnScreenKeyboard.Target;
            if (target == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            float pad = 28f * s;
            float gap = 6f * s;
            float keyH = 58f * s;
            float titleH = target.Title != null ? 30f * s : 0f;
            float textH = (target.Multiline ? 110f : 48f) * s;
            Key[][] rows = OnScreenKeyboard.Rows;

            float panelW = Mathf.Min(980f * s, Screen.width - 32f);
            float panelH = pad * 2f + titleH + textH + gap * 2f + rows.Length * (keyH + gap);
            float bottom = Screen.height - m_Text.MenuBarBottomSpace - 12f * s;
            Rect panel = new Rect((Screen.width - panelW) * 0.5f, bottom - panelH, panelW, panelH);
            m_Text.DrawPanel(panel);

            float x0 = panel.x + pad;
            float innerW = panelW - pad * 2f;
            float y = panel.y + pad;

            if (target.Title != null)
            {
                m_Text.DrawText(new Rect(x0, y, innerW, titleH), target.Title, 0.9f, TextAnchor.MiddleLeft, TitleColor);
                y += titleH;
            }

            Rect textRect = new Rect(x0, y + gap, innerW, textH);
            Fill(textRect, TextFill);
            DrawPreview(new Rect(textRect.x + 10f * s, textRect.y + 4f * s, textRect.width - 20f * s, textRect.height - 8f * s), target);
            y = textRect.yMax + gap * 2f;

            float unit = innerW / OnScreenKeyboard.RowUnits;
            for (int r = 0; r < rows.Length; r++)
            {
                float x = x0;
                for (int c = 0; c < rows[r].Length; c++)
                {
                    Key k = rows[r][c];
                    Rect kr = new Rect(x + gap * 0.5f, y, k.Width * unit - gap, keyH);
                    bool focus = r == OnScreenKeyboard.Row && c == OnScreenKeyboard.Col;
                    Fill(kr, focus ? KeyFillFocus : KeyFill);
                    DrawKey(kr, k);
                    if (focus)
                    {
                        m_Text.DrawFocusFrame(kr);
                    }
                    x += k.Width * unit;
                }
                y += keyH + gap;
            }
        }

        private void DrawKey(Rect r, Key k)
        {
            switch (k.Kind)
            {
                case KeyKind.Char:
                    m_Text.DrawText(r, OnScreenKeyboard.Shown(k).ToString(), 1.25f, TextAnchor.MiddleCenter, null);
                    break;
                case KeyKind.Shift:
                    ShiftState sh = OnScreenKeyboard.Shift;
                    m_Text.DrawText(r, sh == ShiftState.Locked ? "CAPS" : "Shift", 0.95f, TextAnchor.MiddleCenter, sh == ShiftState.Off ? SpecialColor : OnColor);
                    if (sh == ShiftState.Locked)
                    {
                        m_Text.DrawFrame(r, OnColor, 2f);
                    }
                    break;
                case KeyKind.Layout:
                    m_Text.DrawText(r, OnScreenKeyboard.SymbolsShown ? "ABC" : "#?é", 0.95f, TextAnchor.MiddleCenter, SpecialColor);
                    break;
                case KeyKind.Space:
                    m_Text.DrawText(r, "Space", 0.95f, TextAnchor.MiddleCenter, SpecialColor);
                    break;
                case KeyKind.NewLine:
                    m_Text.DrawText(r, "New line", 0.8f, TextAnchor.MiddleCenter, SpecialColor);
                    break;
                case KeyKind.Delete:
                    m_Text.DrawText(r, "Delete", 0.95f, TextAnchor.MiddleCenter, SpecialColor);
                    break;
                case KeyKind.Done:
                    m_Text.DrawText(r, "Done", 1.05f, TextAnchor.MiddleCenter, null);
                    break;
            }
        }

        /// <summary>The end of the text with a caret; older text scrolls off to the left (or top).</summary>
        private void DrawPreview(Rect r, TextTarget target)
        {
            string text = target.Text ?? string.Empty;
            if (target.Password)
            {
                text = new string('*', text.Length);
            }
            if (target.Multiline)
            {
                // Long lines wrap; keep the tail short enough that wrapped lines stay inside the box.
                if (text.Length > 320)
                {
                    text = "…" + text.Substring(text.Length - 320);
                }
                string[] lines = text.Split('\n');
                int first = Mathf.Max(0, lines.Length - MaxPreviewLines);
                text = string.Join("\n", lines, first, lines.Length - first);
                m_Text.DrawText(r, text + "|", 1f, TextAnchor.LowerLeft, null);
                return;
            }
            const float size = 1.15f;
            string shown = text + "|";
            int start = 0;
            while (start < text.Length && m_Text.MeasureText(shown, size) > r.width)
            {
                start++;
                shown = "…" + text.Substring(start) + "|";
            }
            m_Text.DrawText(r, shown, size, TextAnchor.MiddleLeft, null);
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
