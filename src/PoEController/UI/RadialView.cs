using PoEController.World;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Draws Console style's radials (General, Combat, party ring; layout like the Switch, screenshot #20): the
    /// 12-socket ring, a round slot frame per entry (gold, red-bronze in the Combat Radial), the icon, a glow on the
    /// highlighted / active slot, the character's portrait in the middle and the highlighted entry's name on a bar
    /// beside it. 0.39.0: frames generated in code (<see cref="RadialArt"/>), icons from the running game
    /// (<see cref="GameIcons"/>) — the Switch art is no longer used or shipped.
    /// </summary>
    internal sealed class RadialView
    {
        private const float RefHeight = 1080f;
        /// <summary>Ring slot centres sit at this fraction of the ring texture's half-size.</summary>
        private const float SlotRadius = RadialArt.SlotRadius;
        /// <summary>Icon size as a fraction of the frame: the game's square icon tiles fit inside the frame's dark disc.</summary>
        private const float IconInFrame = 0.57f;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color GlowColor = new Color(1f, 0.85f, 0.5f, 0.9f);
        private static readonly Color ActiveGlow = new Color(0.45f, 0.9f, 0.45f, 0.8f);
        private static readonly Color LabelBg = new Color(0f, 0f, 0f, 0.7f);
        private static readonly Color StatusColor = new Color(0.85f, 0.8f, 0.68f);

        private readonly PromptBar m_Text;

        public RadialView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(IRadialModel radial, float scale)
        {
            if (!radial.IsOpen || radial.Count == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float size = Mathf.Min(760f * s, Screen.height * 0.86f);
            float slotR = size * 0.5f * SlotRadius;
            float frameD = size * RadialArt.SlotFraction;
            float iconD = frameD * IconInFrame;

            Color old = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), RadialArt.Ring);
            GUI.color = old;

            DrawCentre(radial, c, size * 0.2f, s);

            int n = radial.Count;
            Texture2D frame = radial.Combat ? RadialArt.RedFrame : RadialArt.GoldFrame;
            for (int i = 0; i < n; i++)
            {
                float deg = RadialLayout.Degrees(i, n, radial.SpreadOnHoles);
                bool hl = i == radial.Highlight;
                DrawSlot(radial.GetSlot(i), SlotCentre(c, slotR, deg), frame, hl ? frameD * 1.12f : frameD, hl ? iconD * 1.12f : iconD, hl, s);
            }

            int h = radial.Highlight;
            if (h >= 0 && h < n)
            {
                RadialSlot hs = radial.GetSlot(h);
                DrawLabel(hs, SlotCentre(c, slotR, RadialLayout.Degrees(h, n, radial.SpreadOnHoles)), c, frameD * 0.65f, s);
                if (hs.Info != null)
                {
                    RadialInfo.DrawPanel(m_Text, new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), hs.Name,
                        RadialInfo.Text(hs.Info, radial.Owner), s);
                }
            }
            GUI.color = old;
        }

        private static Vector2 SlotCentre(Vector2 c, float radius, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
        }

        private void DrawCentre(IRadialModel radial, Vector2 c, float d, float s)
        {
            Texture2D portrait = radial.Owner != null ? CharacterStats.SmallPortrait(radial.Owner.gameObject) : null;
            Rect r = new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d);
            if (portrait != null)
            {
                GUI.DrawTexture(r, portrait, ScaleMode.ScaleAndCrop);
                m_Text.DrawFrame(r, new Color(0.78f, 0.64f, 0.36f), 2f * s);
            }
            string title = radial.Title ?? (radial.Owner != null ? radial.Owner.Name() : "");
            m_Text.DrawText(new Rect(c.x - d * 1.5f, r.yMax + 6f * s, d * 3f, 32f * s), title, 1f, TextAnchor.UpperCenter, null);
        }

        private void DrawSlot(RadialSlot e, Vector2 p, Texture2D frame, float frameD, float iconD, bool hl, float s)
        {
            Color old = GUI.color;
            if (hl || e.Active)
            {
                GUI.color = hl ? GlowColor : ActiveGlow;
                float g = frameD * 1.6f;
                GUI.DrawTexture(new Rect(p.x - g * 0.5f, p.y - g * 0.5f, g, g), RadialArt.Glow);
            }
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(p.x - frameD * 0.5f, p.y - frameD * 0.5f, frameD, frameD), frame);
            GUI.color = old;

            Rect ir = new Rect(p.x - iconD * 0.5f, p.y - iconD * 0.5f, iconD, iconD);
            if (!e.Usable)
            {
                GUI.color = Dim;
            }
            if (e.Icon != null)
            {
                GUI.DrawTexture(ir, e.Icon, ScaleMode.ScaleToFit);
            }
            else if (GameIcons.TryGet(e.IconName, out Texture gameIcon, out Rect uv))
            {
                GUI.DrawTextureWithTexCoords(ir, gameIcon, uv);
            }
            else if (!e.Blank)
            {
                m_Text.DrawText(ir, e.Name, 0.7f, TextAnchor.MiddleCenter, null);
            }
            GUI.color = old;
            if (!string.IsNullOrEmpty(e.Badge))
            {
                m_Text.DrawText(new Rect(ir.x, ir.yMax - 24f * s, ir.width + 6f * s, 26f * s), e.Badge, 0.85f, TextAnchor.LowerRight, null);
            }
        }

        /// <summary>Name (+ status) on a dark bar beside the highlighted slot, on the side towards the middle.</summary>
        private void DrawLabel(RadialSlot e, Vector2 p, Vector2 c, float gap, float s)
        {
            float w = 300f * s;
            float h = string.IsNullOrEmpty(e.Status) ? 40f * s : 64f * s;
            Rect r;
            TextAnchor anchor;
            if (p.x < c.x - 20f * s)
            {
                r = new Rect(p.x + gap, p.y - h * 0.5f, w, h);
                anchor = TextAnchor.MiddleLeft;
            }
            else if (p.x > c.x + 20f * s)
            {
                r = new Rect(p.x - gap - w, p.y - h * 0.5f, w, h);
                anchor = TextAnchor.MiddleRight;
            }
            else
            {
                float y = p.y < c.y ? p.y + gap : p.y - gap - h;
                r = new Rect(p.x - w * 0.5f, y, w, h);
                anchor = TextAnchor.MiddleCenter;
            }
            Color old = GUI.color;
            GUI.color = LabelBg;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.DrawTexture(new Rect(r.x, r.yMax - 3f * s, r.width, 3f * s), RadialArt.Divider, ScaleMode.StretchToFill);
            Rect inner = new Rect(r.x + 12f * s, r.y + 4f * s, r.width - 24f * s, 32f * s);
            m_Text.DrawText(inner, e.Name, 1.05f, anchor, null);
            if (!string.IsNullOrEmpty(e.Status))
            {
                m_Text.DrawText(new Rect(inner.x, inner.yMax, inner.width, 24f * s), e.Status, 0.8f, anchor, StatusColor);
            }
        }
    }
}
