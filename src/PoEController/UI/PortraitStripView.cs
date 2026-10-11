using PoEController.Compare;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// The companion picker of a comparison (IMGUI): the party's portraits in a row above the prompt bar, names under
    /// them, the current one in the gold focus frame; companions that can't be compared are dimmed with the reason.
    /// </summary>
    internal sealed class PortraitStripView
    {
        private const float RefHeight = 1080f;
        private static readonly Color TitleColor = new Color(0.95f, 0.85f, 0.6f);
        private static readonly Color NameColor = new Color(0.92f, 0.88f, 0.8f);
        private static readonly Color ReasonColor = new Color(0.75f, 0.45f, 0.38f);

        private readonly PromptBar m_Text;

        public PortraitStripView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(float scale)
        {
            if (CompareSession.Mode != CompareSession.Stage.Picking || Event.current.type != EventType.Repaint)
            {
                return;
            }
            int n = CompareSession.Members.Count;
            if (n == 0)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            float pad = 20f * s;
            float portraitW = 76f * s;
            float portraitH = 96f * s;
            float cellW = Mathf.Max(portraitW, 120f * s);
            float gap = 14f * s;
            float titleH = 36f * s;
            float nameH = 28f * s;
            float reasonH = 22f * s;

            float panelW = Mathf.Min(pad * 2f + n * cellW + (n - 1) * gap, Screen.width - 32f);
            float panelH = pad * 2f + titleH + portraitH + nameH + reasonH;
            float bottom = Screen.height - m_Text.MenuBarBottomSpace - 12f * s;
            Rect panel = new Rect((Screen.width - panelW) * 0.5f, bottom - panelH, panelW, panelH);
            m_Text.DrawPanel(panel);

            string item = CompareSession.Item != null ? CompareSession.Item.Name : "";
            m_Text.DrawText(new Rect(panel.x + pad, panel.y + pad, panel.width - pad * 2f, titleH), "Compare " + item + " with…", 0.9f, TextAnchor.MiddleCenter, TitleColor);

            float x = panel.x + pad;
            float y = panel.y + pad + titleH;
            for (int i = 0; i < n; i++)
            {
                PartyMemberAI m = CompareSession.Members[i];
                string why = CompareSession.MemberStatus[i];
                Rect cell = new Rect(x, y, cellW, portraitH + nameH + reasonH);
                Rect portrait = new Rect(cell.center.x - portraitW * 0.5f, y, portraitW, portraitH);
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, why == null ? 1f : 0.4f);
                Texture2D tex = m != null ? CharacterStats.SmallPortrait(m.gameObject) : null;
                if (tex != null)
                {
                    GUI.DrawTexture(portrait, tex, ScaleMode.ScaleAndCrop);
                }
                GUI.color = old;
                if (i == CompareSession.MemberIndex)
                {
                    m_Text.DrawFocusFrame(portrait);
                }
                string name = m != null ? CharacterStats.Name(m) : "";
                m_Text.DrawText(new Rect(cell.x, portrait.yMax + 2f * s, cell.width, nameH), name, 0.75f, TextAnchor.MiddleCenter, NameColor);
                if (why != null)
                {
                    m_Text.DrawText(new Rect(cell.x, portrait.yMax + nameH, cell.width, reasonH), why, 0.6f, TextAnchor.MiddleCenter, ReasonColor);
                }
                x += cellW + gap;
            }
        }
    }
}
