using PoEController.World;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>Draws the ability wheel (IMGUI): a dark disc, entry icons in a ring, details in the middle.</summary>
    internal sealed class WheelView
    {
        private const float RefHeight = 1080f;
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color ActiveColor = new Color(0.45f, 0.85f, 0.45f);
        private static readonly Color StatusColor = new Color(0.85f, 0.8f, 0.68f);

        private readonly PromptBar m_Text;
        private Texture2D m_Disc;

        public WheelView(PromptBar text)
        {
            m_Text = text;
        }

        public void Draw(AbilityWheel wheel, float scale)
        {
            if (!wheel.IsOpen || wheel.Shown == null || wheel.Shown.Count == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float ring = 250f * s;
            float icon = 84f * s;
            float discR = ring + icon * 0.9f;

            GUI.DrawTexture(new Rect(c.x - discR, c.y - discR, discR * 2f, discR * 2f), Disc());

            // Title above the disc.
            string title = wheel.Owner != null ? wheel.Owner.Name() : "";
            if (wheel.InGroup)
            {
                title += "  -  " + wheel.GroupName;
            }
            m_Text.DrawText(new Rect(c.x - discR, c.y - discR - 44f * s, discR * 2f, 40f * s), title, 1.2f, TextAnchor.MiddleCenter, null);

            int n = wheel.Shown.Count;
            for (int i = 0; i < n; i++)
            {
                AbilityWheel.Entry e = wheel.Shown[i];
                float a = i * Mathf.PI * 2f / n;
                Vector2 p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * ring;
                bool hl = i == wheel.Highlight;
                float size = hl ? icon * 1.15f : icon;
                Rect r = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);

                Color old = GUI.color;
                if (!e.Usable)
                {
                    GUI.color = Dim;
                }
                if (e.Icon != null)
                {
                    GUI.DrawTexture(r, e.Icon, ScaleMode.ScaleToFit);
                }
                else
                {
                    m_Text.DrawText(r, Abbrev(e.Name), 0.9f, TextAnchor.MiddleCenter, null);
                }
                GUI.color = old;

                if (e.Active)
                {
                    m_Text.DrawFrame(r, ActiveColor, 2f * s);
                }
                if (hl)
                {
                    m_Text.DrawFocusFrame(r);
                }
                if (!string.IsNullOrEmpty(e.Badge))
                {
                    m_Text.DrawText(new Rect(r.x, r.yMax - 26f * s, r.width - 4f * s, 26f * s), e.Badge, 0.85f, TextAnchor.LowerRight, null);
                }
            }

            // Details of the highlighted entry in the middle.
            AbilityWheel.Entry cur = wheel.Current;
            if (cur != null)
            {
                float w = ring * 1.35f;
                m_Text.DrawText(new Rect(c.x - w * 0.5f, c.y - 50f * s, w, 60f * s), cur.Name, 1.25f, TextAnchor.LowerCenter, null);
                if (!string.IsNullOrEmpty(cur.Status))
                {
                    m_Text.DrawText(new Rect(c.x - w * 0.5f, c.y + 14f * s, w, 70f * s), cur.Status, 0.9f, TextAnchor.UpperCenter, StatusColor);
                }
                RadialInfo.DrawPanel(m_Text, new Rect(c.x - discR, c.y - discR, discR * 2f, discR * 2f), cur.Name, RadialInfo.Text(cur, wheel.Owner), s);
            }
        }

        private static string Abbrev(string name)
        {
            return string.IsNullOrEmpty(name) ? "?" : (name.Length <= 10 ? name : name.Substring(0, 9) + ".");
        }

        /// <summary>Soft-edged translucent disc, made once at runtime.</summary>
        private Texture2D Disc()
        {
            if (m_Disc != null)
            {
                return m_Disc;
            }
            const int size = 256;
            m_Disc = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            Color32[] px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                    float alpha = d >= 1f ? 0f : (d > 0.9f ? (1f - d) / 0.1f : 1f) * 0.78f;
                    px[y * size + x] = new Color32(12, 10, 8, (byte)(alpha * 255f));
                }
            }
            m_Disc.SetPixels32(px);
            m_Disc.Apply(false, true);
            return m_Disc;
        }
    }
}
