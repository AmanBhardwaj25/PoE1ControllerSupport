using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// The focus frame's look (0.36.0: rounded to match the game's own corners): a thin
    /// pale-gold line with rounded corners, a dark rim so it reads on parchment, and a soft warm glow outside, close
    /// to the Switch port's focus marker. Drawn as a 9-sliced texture generated in code (no art file), rebuilt when
    /// the pixel size changes (screen height × prompt scale).
    /// </summary>
    internal static class FocusFrameArt
    {
        private static readonly Color LineColor = new Color32(236, 214, 160, 255);
        private static readonly Color GlowColor = new Color32(255, 214, 130, 255);
        private const float LineAt1080 = 1.6f;
        private const float RadiusAt1080 = 6f;
        private const float GlowAt1080 = 5f;
        /// <summary>Gap between the element's edge and the line.</summary>
        private const float PadAt1080 = 3f;

        private static Texture2D s_Texture;
        private static GUIStyle s_Style;
        private static int s_Key = -1;
        private static int s_Margin;
        private static int s_Border;

        /// <summary>Draws the frame around <paramref name="r"/> (GUI coordinates); <paramref name="s"/> = pixels per 1080p unit.</summary>
        public static void Draw(Rect r, float s)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            Ensure(s);
            float out_ = PadAt1080 * s + s_Margin;
            Rect f = new Rect(r.x - out_, r.y - out_, r.width + out_ * 2f, r.height + out_ * 2f);
            // 9-slicing needs at least both corners' room.
            float min = s_Border * 2f + 1f;
            if (f.width < min)
            {
                f.x -= (min - f.width) * 0.5f;
                f.width = min;
            }
            if (f.height < min)
            {
                f.y -= (min - f.height) * 0.5f;
                f.height = min;
            }
            Color old = GUI.color;
            GUI.color = Color.white;
            s_Style.Draw(f, false, false, false, false);
            GUI.color = old;
        }

        private static void Ensure(float s)
        {
            int key = Mathf.RoundToInt(s * 100f);
            if (key == s_Key && s_Texture != null)
            {
                return;
            }
            s_Key = key;
            if (s_Texture != null)
            {
                Object.Destroy(s_Texture);
            }
            float line = Mathf.Max(1.2f, LineAt1080 * s);
            float radius = Mathf.Max(3f, RadiusAt1080 * s);
            float glow = Mathf.Max(2f, GlowAt1080 * s);
            s_Margin = Mathf.CeilToInt(glow * 2f + 2f);
            int corner = s_Margin + Mathf.CeilToInt(radius) + 1;
            int size = corner * 2 + 4;
            s_Border = corner;
            s_Texture = Build(size, s_Margin, radius, line, glow);
            s_Style = new GUIStyle { border = new RectOffset(corner, corner, corner, corner) };
            s_Style.normal.background = s_Texture;
        }

        /// <summary>Straight-alpha texture: glow, then a dark rim, then the line, along a rounded rectangle inset by <paramref name="margin"/>.</summary>
        private static Texture2D Build(int size, int margin, float radius, float line, float glow)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Color[] px = new Color[size * size];
            float half = (size - margin * 2) * 0.5f;
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to the rounded rectangle's edge (negative inside).
                    float qx = Mathf.Abs(x + 0.5f - c) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - c) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float sd = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;

                    Color col = new Color(0f, 0f, 0f, 0f);
                    float g = sd > 0f ? 0.45f * Mathf.Exp(-(sd / glow) * (sd / glow)) : 0.12f * Mathf.Exp(-(sd / glow) * (sd / glow));
                    col = Over(col, GlowColor, g);
                    float rim = 0.5f * Mathf.Clamp01(line * 0.5f + 1.6f - Mathf.Abs(sd));
                    col = Over(col, Color.black, rim);
                    float l = Mathf.Clamp01(line * 0.5f + 0.5f - Mathf.Abs(sd));
                    col = Over(col, LineColor, l);
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Color Over(Color dst, Color src, float a)
        {
            if (a <= 0f)
            {
                return dst;
            }
            float outA = a + dst.a * (1f - a);
            if (outA <= 0f)
            {
                return new Color(0f, 0f, 0f, 0f);
            }
            Color rgb = (src * a + dst * dst.a * (1f - a)) / outA;
            rgb.a = outA;
            return rgb;
        }
    }
}
