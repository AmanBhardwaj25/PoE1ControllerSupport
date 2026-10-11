using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// The prompt bar's backgrounds, generated in code (0.39.0; replaces the Switch port's two bar textures, which
    /// could not be shipped): the menu bar is a dark rounded panel with a thin gold border (9-sliced with
    /// <see cref="MenuBorder"/>), the HUD strip a soft dark band that fades out towards every edge.
    /// </summary>
    internal static class BarArt
    {
        private const int MenuW = 128;
        private const int MenuH = 96;
        /// <summary>9-slice border of <see cref="MenuBar"/> (left, right, top, bottom), as the old texture's.</summary>
        public static RectOffset MenuBorder => new RectOffset(24, 24, 20, 20);

        private static readonly Color Panel = new Color(0.065f, 0.055f, 0.045f, 0.94f);
        private static readonly Color Gold = new Color(0.74f, 0.61f, 0.38f, 1f);

        private static Texture2D s_Menu;
        private static Texture2D s_Hud;

        public static Texture2D MenuBar => s_Menu != null ? s_Menu : (s_Menu = BuildMenu());
        public static Texture2D HudStrip => s_Hud != null ? s_Hud : (s_Hud = BuildHud(256, 64));

        private static Texture2D New(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        /// <summary>Signed distance to a rounded rectangle inset by <paramref name="inset"/> (negative inside).</summary>
        private static float RoundedBox(float x, float y, int w, int h, float inset, float radius)
        {
            float hx = w * 0.5f - inset;
            float hy = h * 0.5f - inset;
            float qx = Mathf.Abs(x - w * 0.5f) - (hx - radius);
            float qy = Mathf.Abs(y - h * 0.5f) - (hy - radius);
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static Texture2D BuildMenu()
        {
            Texture2D tex = New(MenuW, MenuH);
            Color[] px = new Color[MenuW * MenuH];
            for (int y = 0; y < MenuH; y++)
            {
                for (int x = 0; x < MenuW; x++)
                {
                    float fx = x + 0.5f;
                    float fy = y + 0.5f;
                    // Soft shadow under the panel, the panel, then a gold line 4 px inside its edge.
                    float outer = RoundedBox(fx, fy, MenuW, MenuH, 3f, 11f);
                    float shadow = Mathf.Clamp01(1f - Mathf.Max(0f, outer) / 3f) * 0.45f;
                    Color c = new Color(0f, 0f, 0f, shadow);
                    float fill = Mathf.Clamp01(0.5f - outer);
                    c = Color.Lerp(c, Panel, fill);
                    c.a = Mathf.Max(shadow, Panel.a * fill);
                    float line = RoundedBox(fx, fy, MenuW, MenuH, 7f, 7f);
                    float l = Mathf.Clamp01(1.3f - Mathf.Abs(line)) * fill;
                    c = Color.Lerp(c, Gold, l);
                    px[y * MenuW + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildHud(int w, int h)
        {
            Texture2D tex = New(w, h);
            Color[] px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float vy = 1f - Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
                for (int x = 0; x < w; x++)
                {
                    float vx = 1f - Mathf.Abs((x + 0.5f) / w - 0.5f) * 2f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(vx * 3f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(vy * 1.6f));
                    px[y * w + x] = new Color(0f, 0f, 0f, 0.62f * a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
