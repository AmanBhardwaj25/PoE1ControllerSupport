using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// The radials' frame art, generated in code (0.39.0; replaces the Switch port's ring / slot frames / glow /
    /// divider, which could not be shipped): a dark ring with gold edges and twelve sockets, a round slot frame (gold
    /// for the General Radial and the party ring, red-bronze for the Combat Radial), a soft glow and a gold divider.
    /// Built once at a fixed resolution and scaled when drawn; colours are baked in (draw with GUI.color white).
    /// </summary>
    internal static class RadialArt
    {
        /// <summary>Slot centres on the ring, as a fraction of its half-size (matches RadialView.SlotRadius).</summary>
        public const float SlotRadius = 0.852f;
        /// <summary>A slot frame's diameter as a fraction of the ring's size (matches RadialView).</summary>
        public const float SlotFraction = 0.135f;

        private static readonly Color Band = new Color(0.075f, 0.064f, 0.052f, 0.9f);
        private static readonly Color Edge = new Color(0.74f, 0.61f, 0.38f, 1f);
        private static readonly Color Socket = new Color(0.035f, 0.03f, 0.025f, 0.95f);
        private static readonly Color SocketEdge = new Color(0.45f, 0.37f, 0.23f, 0.9f);
        private static readonly Color Disc = new Color(0.07f, 0.06f, 0.05f, 0.96f);
        private static readonly Color GoldLight = new Color(0.93f, 0.79f, 0.5f, 1f);
        private static readonly Color GoldDark = new Color(0.5f, 0.38f, 0.2f, 1f);
        private static readonly Color RedLight = new Color(0.82f, 0.32f, 0.22f, 1f);
        private static readonly Color RedDark = new Color(0.36f, 0.08f, 0.06f, 1f);

        private static Texture2D s_Ring;
        private static Texture2D s_Gold;
        private static Texture2D s_Red;
        private static Texture2D s_Glow;
        private static Texture2D s_Divider;

        public static Texture2D Ring => s_Ring != null ? s_Ring : (s_Ring = BuildRing(1024));
        public static Texture2D GoldFrame => s_Gold != null ? s_Gold : (s_Gold = BuildFrame(256, GoldLight, GoldDark));
        public static Texture2D RedFrame => s_Red != null ? s_Red : (s_Red = BuildFrame(256, RedLight, RedDark));
        public static Texture2D Glow => s_Glow != null ? s_Glow : (s_Glow = BuildGlow(128));
        public static Texture2D Divider => s_Divider != null ? s_Divider : (s_Divider = BuildDivider(256, 8));

        private static Texture2D New(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        /// <summary>Coverage of a band [a, b] at distance d, anti-aliased over one pixel.</summary>
        private static float BandCover(float d, float a, float b) => Mathf.Clamp01(d - a + 0.5f) * Mathf.Clamp01(b - d + 0.5f);

        private static Color Over(Color dst, Color src, float a)
        {
            a *= src.a;
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

        private static Texture2D BuildRing(int size)
        {
            Texture2D tex = New(size, size);
            Color[] px = new Color[size * size];
            float half = size * 0.5f;
            float inner = half * 0.70f;
            float outer = half * 0.99f;
            float edge = Mathf.Max(1.5f, size / 400f);
            float socketR = half * SlotFraction * 0.92f;
            Vector2[] sockets = new Vector2[12];
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                sockets[i] = new Vector2(half + Mathf.Sin(a) * half * SlotRadius, half + Mathf.Cos(a) * half * SlotRadius);
            }
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Vector2.Distance(p, new Vector2(half, half));
                    Color col = new Color(0f, 0f, 0f, 0f);
                    float band = BandCover(d, inner, outer);
                    if (band > 0f)
                    {
                        // Slightly lighter towards the middle of the band.
                        float t = Mathf.InverseLerp(inner, outer, d);
                        Color b = Band * (1f + 0.25f * (1f - Mathf.Abs(t - 0.5f) * 2f));
                        b.a = Band.a;
                        col = Over(col, b, band);
                        for (int i = 0; i < 12; i++)
                        {
                            float sd = Vector2.Distance(p, sockets[i]);
                            if (sd < socketR + 2f)
                            {
                                col = Over(col, Socket, Mathf.Clamp01(socketR - sd + 0.5f) * band);
                                col = Over(col, SocketEdge, BandCover(sd, socketR - edge * 0.6f, socketR + edge * 0.6f) * band);
                            }
                        }
                    }
                    col = Over(col, Edge, BandCover(d, inner - edge, inner + edge));
                    col = Over(col, Edge, BandCover(d, outer - edge * 2f, outer));
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Round slot frame: dark disc, bevelled metal ring (light top-left, dark bottom-right), thin dark rim.</summary>
        private static Texture2D BuildFrame(int size, Color light, Color dark)
        {
            Texture2D tex = New(size, size);
            Color[] px = new Color[size * size];
            float half = size * 0.5f;
            float discR = half * 0.80f;
            float ringOut = half * 0.94f;
            float rimOut = half * 0.985f;
            Vector2 lightDir = new Vector2(-0.6f, 0.8f).normalized;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 v = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float d = v.magnitude;
                    Color col = new Color(0f, 0f, 0f, 0f);
                    col = Over(col, new Color(0f, 0f, 0f, 0.7f), BandCover(d, ringOut - 1f, rimOut));
                    float shade = d > 0.01f ? Vector2.Dot(v / d, lightDir) * 0.5f + 0.5f : 0.5f;
                    Color metal = Color.Lerp(dark, light, shade);
                    col = Over(col, metal, BandCover(d, discR, ringOut));
                    // A thin bright line on the ring's inner edge.
                    col = Over(col, Color.Lerp(metal, Color.white, 0.35f), BandCover(d, discR, discR + size / 128f) * 0.8f);
                    col = Over(col, Disc, Mathf.Clamp01(discR - d + 0.5f));
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildGlow(int size)
        {
            Texture2D tex = New(size, size);
            Color[] px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Gold line, brightest in the middle and fading out at both ends.</summary>
        private static Texture2D BuildDivider(int w, int h)
        {
            Texture2D tex = New(w, h);
            Color[] px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float vy = 1f - Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;
                for (int x = 0; x < w; x++)
                {
                    float vx = 1f - Mathf.Abs((x + 0.5f) / w - 0.5f) * 2f;
                    Color c = Color.Lerp(GoldDark, GoldLight, vx);
                    c.a = Mathf.Clamp01(vx * 1.6f) * Mathf.Clamp01(vy * 1.4f);
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
