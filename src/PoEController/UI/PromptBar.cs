using System;
using System.Collections.Generic;
using System.IO;
using PoEController.Glyphs;
using PoEController.Input;
using PoEController.Schemes;
using UnityEngine;

namespace PoEController.UI
{
    public enum BarPosition
    {
        Top,
        Bottom,
    }

    /// <summary>
    /// Draws the active scheme's prompts as a centred bar of glyphs + labels (IMGUI, on top of the game UI).
    /// Laid out at 1080p and scaled with screen height.
    /// </summary>
    public sealed class PromptBar
    {
        private const float RefHeight = 1080f;
        private const float BarHeight = 54f;
        private const float GlyphHeight = 34f;
        private const float FontSize = 20f;
        private const float GlyphGap = 2f;
        private const float LabelGap = 8f;
        /// <summary>The in-world prompt (A over the target) relative to the prompt bar: glyph, text and bar alike.</summary>
        private const float WorldPromptSize = 0.75f;
        /// <summary>Space between wrapped rows of prompts.</summary>
        private const float RowGap = 4f;
        private const float ItemGap = 28f;
        private const float SidePadding = 40f;
        private const float EdgeMargin = 12f;
        private static readonly Color TextColor = new Color(0.93f, 0.88f, 0.76f);
        private static readonly Color FocusColor = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        private readonly GlyphLibrary m_Glyphs;
        private readonly Texture2D m_MenuBar;
        private readonly Texture2D m_HudBar;
        private readonly string[] m_FontPreference;
        private readonly List<float> m_ItemWidths = new List<float>();
        /// <summary>Wrapped rows: x = first item, y = end (exclusive), z = content width.</summary>
        private readonly List<Vector3> m_Rows = new List<Vector3>();
        private int m_RowCount = 1;
        private readonly GUIContent m_Content = new GUIContent();
        private GUIStyle m_LabelStyle;
        private GUIStyle m_MenuBarStyle;
        private int m_StyledFontSize = -1;
        private float m_NextFontSearch;
        private int m_LastFontCount = -1;
        private const float FontSearchInterval = 3f;

        public float Scale = 1f;
        public BarPosition HudPosition = BarPosition.Top;
        public BarPosition MenuPosition = BarPosition.Bottom;

        public PromptBar(GlyphLibrary glyphs, string fontPreference)
        {
            m_Glyphs = glyphs;
            // 0.39.0: generated in code (BarArt) instead of the Switch port's bar textures.
            m_MenuBar = BarArt.MenuBar;
            m_HudBar = BarArt.HudStrip;
            m_FontPreference = fontPreference.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public void Draw(GlyphFamily family, BarStyle style, IReadOnlyList<Prompt> prompts)
        {
            if (prompts.Count == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * Scale;
            EnsureStyles(Mathf.RoundToInt(FontSize * s));

            // Measure.
            m_ItemWidths.Clear();
            float total = 0f;
            for (int i = 0; i < prompts.Count; i++)
            {
                float w = 0f;
                string[] glyphs = prompts[i].Glyphs;
                for (int g = 0; g < glyphs.Length; g++)
                {
                    w += GlyphWidth(m_Glyphs.Get(family, glyphs[g]), s) + (g > 0 ? GlyphGap * s : 0f);
                }
                m_Content.text = prompts[i].Label;
                w += LabelGap * s + m_LabelStyle.CalcSize(m_Content).x;
                m_ItemWidths.Add(w);
                total += w + (i > 0 ? ItemGap * s : 0f);
            }

            // Too many prompts for one bar: wrap into several rows (each centred), first row nearest the edge text reads from.
            float maxContent = Screen.width - (SidePadding * 2f + EdgeMargin * 2f) * s;
            m_Rows.Clear();
            int rowStart = 0;
            float rowWidth = 0f;
            for (int i = 0; i < prompts.Count; i++)
            {
                float add = m_ItemWidths[i] + (i > rowStart ? ItemGap * s : 0f);
                if (i > rowStart && rowWidth + add > maxContent)
                {
                    m_Rows.Add(new Vector3(rowStart, i, rowWidth));
                    rowStart = i;
                    rowWidth = m_ItemWidths[i];
                }
                else
                {
                    rowWidth += add;
                }
            }
            m_Rows.Add(new Vector3(rowStart, prompts.Count, rowWidth));
            m_RowCount = m_Rows.Count;

            float barH = BarHeight * s;
            float rowGap = RowGap * s;
            BarPosition pos = style == BarStyle.Hud ? HudPosition : MenuPosition;
            float blockH = m_Rows.Count * barH + (m_Rows.Count - 1) * rowGap;
            float top = pos == BarPosition.Top ? EdgeMargin * s : Screen.height - blockH - EdgeMargin * s;
            for (int r = 0; r < m_Rows.Count; r++)
            {
                int from = (int)m_Rows[r].x;
                int to = (int)m_Rows[r].y;
                float content = m_Rows[r].z;
                float barW = Mathf.Min(content + SidePadding * 2f * s, Screen.width);
                Rect bar = new Rect((Screen.width - barW) * 0.5f, top + r * (barH + rowGap), barW, barH);
                DrawBarBackground(style, bar);
                DrawItems(family, prompts, from, to, bar.x + (barW - content) * 0.5f, bar.y + barH * 0.5f, s);
            }
        }

        private void DrawBarBackground(BarStyle style, Rect bar)
        {
            if (style == BarStyle.Menu && m_MenuBarStyle != null)
            {
                m_MenuBarStyle.Draw(bar, false, false, false, false);
            }
            else if (m_HudBar != null)
            {
                // The HUD strip is a soft shadow; overdraw it wider than the content so the fade sits outside.
                GUI.DrawTexture(new Rect(bar.x - bar.width * 0.1f, bar.y - bar.height * 0.25f, bar.width * 1.2f, bar.height * 1.5f), m_HudBar, ScaleMode.StretchToFill);
            }
        }

        private void DrawItems(GlyphFamily family, IReadOnlyList<Prompt> prompts, int from, int to, float x, float midY, float s)
        {
            for (int i = from; i < to; i++)
            {
                string[] glyphs = prompts[i].Glyphs;
                for (int g = 0; g < glyphs.Length; g++)
                {
                    Texture2D tex = m_Glyphs.Get(family, glyphs[g]);
                    float gw = GlyphWidth(tex, s);
                    float gh = GlyphHeight * s;
                    if (tex != null)
                    {
                        GUI.DrawTexture(new Rect(x, midY - gh * 0.5f, gw, gh), tex, ScaleMode.ScaleToFit);
                    }
                    if (g == 0 && prompts[i].Progress > 0f)
                    {
                        DrawHoldRing(new Vector2(x + gw * 0.5f, midY), gh * 0.62f, prompts[i].Progress, s);
                    }
                    x += gw + GlyphGap * s;
                }
                x += LabelGap * s - GlyphGap * s;
                m_Content.text = prompts[i].Label;
                Vector2 size = m_LabelStyle.CalcSize(m_Content);
                Rect label = new Rect(x, midY - size.y * 0.5f, size.x + 2f, size.y);
                DrawShadowedLabel(label, m_Content);
                x += size.x + ItemGap * s;
            }
        }

        private const int HoldRingDots = 16;
        private static readonly Color HoldRingOff = new Color(0f, 0f, 0f, 0.55f);
        private Texture2D m_Dot;

        /// <summary>A hold in progress: a ring of dots around the glyph, lit clockwise from the top.</summary>
        private void DrawHoldRing(Vector2 centre, float radius, float progress, float s)
        {
            if (m_Dot == null)
            {
                m_Dot = MakeDot(32);
            }
            int lit = Mathf.CeilToInt(Mathf.Clamp01(progress) * HoldRingDots);
            float d = 7f * s;
            Color old = GUI.color;
            for (int k = 0; k < HoldRingDots; k++)
            {
                float a = k * Mathf.PI * 2f / HoldRingDots;
                Vector2 p = centre + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                GUI.color = k < lit ? FocusColor : HoldRingOff;
                GUI.DrawTexture(new Rect(p.x - d * 0.5f, p.y - d * 0.5f, d, d), m_Dot);
            }
            GUI.color = old;
        }

        private static Texture2D MakeDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - dist)));
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// The in-world prompt (A over the target) for a point in the world: centred on it, its bottom edge where the
        /// prompt has always sat (the bar reaches a quarter glyph below the point). False when the point is behind the camera.
        /// </summary>
        public bool TryWorldPromptPlace(Vector3 world, out float centreX, out float bottomY)
        {
            centreX = bottomY = 0f;
            Camera cam = Camera.main;
            if (cam == null)
            {
                return false;
            }
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
            {
                return false;
            }
            centreX = sp.x;
            bottomY = Screen.height - sp.y + GlyphHeight * WorldScale * 0.25f;
            return true;
        }

        /// <summary>The space the in-world prompt takes with its bottom edge at <paramref name="bottomY"/> (GUI coordinates).</summary>
        public Rect WorldPromptRect(GlyphFamily family, float centreX, float bottomY, string glyph, string label)
        {
            MeasureWorld(family, glyph, label, out float total, out float gh, out _, out _, out _);
            return new Rect(centreX - total * 0.5f - gh * 0.6f, bottomY - gh * 1.5f, total + gh * 1.2f, gh * 1.5f);
        }

        /// <summary>The in-world prompt centred on <paramref name="centreX"/>, its bottom edge at <paramref name="bottomY"/> (GUI coordinates).</summary>
        public void DrawWorldPromptAt(GlyphFamily family, float centreX, float bottomY, string glyph, string label)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            MeasureWorld(family, glyph, label, out float total, out float gh, out float gw, out Vector2 size, out Texture2D tex);
            float s = WorldScale;
            float x = centreX - total * 0.5f;
            float midY = bottomY - gh * 0.75f;
            if (m_HudBar != null)
            {
                GUI.DrawTexture(new Rect(x - gh * 0.6f, midY - gh * 0.75f, total + gh * 1.2f, gh * 1.5f), m_HudBar, ScaleMode.StretchToFill);
            }
            if (tex != null)
            {
                GUI.DrawTexture(new Rect(x, midY - gh * 0.5f, gw, gh), tex, ScaleMode.ScaleToFit);
            }
            if (size.x > 0f)
            {
                DrawShadowedLabel(new Rect(x + gw + LabelGap * s, midY - size.y * 0.5f, size.x + 2f, size.y), m_Content);
            }
        }

        /// <summary>Size factor of the in-world prompt (screen height, Prompts.Scale, and its own smaller size).</summary>
        private float WorldScale => Screen.height / RefHeight * Scale * WorldPromptSize;

        /// <summary>Glyph and label sizes of the in-world prompt; leaves the label in <see cref="m_Content"/> for drawing.</summary>
        private void MeasureWorld(GlyphFamily family, string glyph, string label, out float total, out float gh, out float gw, out Vector2 size, out Texture2D tex)
        {
            float s = WorldScale;
            EnsureStyles(Mathf.RoundToInt(FontSize * s));
            tex = m_Glyphs.Get(family, glyph);
            gw = GlyphWidth(tex, s);
            gh = GlyphHeight * s;
            // No label = glyph only (Prompts.TargetLabel).
            bool hasLabel = !string.IsNullOrEmpty(label);
            m_Content.text = hasLabel ? label : "";
            size = hasLabel ? m_LabelStyle.CalcSize(m_Content) : Vector2.zero;
            total = hasLabel ? gw + LabelGap * s + size.x : gw;
        }

        /// <summary>Width of a glyph + label pair as <see cref="DrawPromptAt"/> draws it.</summary>
        public float MeasurePrompt(GlyphFamily family, string glyph, string label)
        {
            float s = Screen.height / RefHeight * Scale;
            EnsureStyles(Mathf.RoundToInt(FontSize * s));
            m_Content.text = label;
            return GlyphWidth(m_Glyphs.Get(family, glyph), s) + LabelGap * s + m_LabelStyle.CalcSize(m_Content).x;
        }

        /// <summary>Glyph + label starting at <paramref name="x"/>, vertically centred on <paramref name="midY"/> (GUI coordinates).</summary>
        public void DrawPromptAt(GlyphFamily family, float x, float midY, string glyph, string label)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * Scale;
            EnsureStyles(Mathf.RoundToInt(FontSize * s));
            Texture2D tex = m_Glyphs.Get(family, glyph);
            float gw = GlyphWidth(tex, s);
            float gh = GlyphHeight * s;
            m_Content.text = label;
            Vector2 size = m_LabelStyle.CalcSize(m_Content);
            if (tex != null)
            {
                GUI.DrawTexture(new Rect(x, midY - gh * 0.5f, gw, gh), tex, ScaleMode.ScaleToFit);
            }
            DrawShadowedLabel(new Rect(x + gw + LabelGap * s, midY - size.y * 0.5f, size.x + 2f, size.y), m_Content);
        }

        /// <summary>One glyph, its left edge at <paramref name="x"/>, centred on <paramref name="midY"/> (GUI coordinates); returns its width.</summary>
        public float DrawGlyph(GlyphFamily family, float x, float midY, string glyph)
        {
            float s = Screen.height / RefHeight * Scale;
            Texture2D tex = m_Glyphs.Get(family, glyph);
            float gw = GlyphWidth(tex, s);
            float gh = GlyphHeight * s;
            if (tex != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(x, midY - gh * 0.5f, gw, gh), tex, ScaleMode.ScaleToFit);
            }
            return gw;
        }

        /// <summary>Soft dark strip behind on-screen prompts (the HUD bar art), GUI coordinates.</summary>
        public void DrawHudStrip(Rect r)
        {
            if (Event.current.type == EventType.Repaint && m_HudBar != null)
            {
                GUI.DrawTexture(r, m_HudBar, ScaleMode.StretchToFill);
            }
        }

        /// <summary>Height of one prompt line (glyph height) in pixels.</summary>
        public float PromptLineHeight => GlyphHeight * Screen.height / RefHeight * Scale;

        /// <summary>
        /// Focus frame around a screen rect (GUI coordinates), e.g. the focused party portrait: thin rounded pale-gold
        /// line with a soft glow (0.36.0; was a 4 px square gold box).
        /// </summary>
        public void DrawFocusFrame(Rect r)
        {
            FocusFrameArt.Draw(r, Screen.height / RefHeight * Scale);
        }

        /// <summary>Screen pixels the menu-style bar takes at the bottom edge (0 when it sits at the top).</summary>
        public float MenuBarBottomSpace => MenuPosition == BarPosition.Bottom
            ? (BarHeight * m_RowCount + RowGap * (m_RowCount - 1) + EdgeMargin) * Screen.height / RefHeight * Scale : 0f;

        /// <summary>A panel in the menu bar's gold-framed style (e.g. the on-screen keyboard).</summary>
        public void DrawPanel(Rect r)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            EnsureStyles(Mathf.RoundToInt(FontSize * Screen.height / RefHeight * Scale));
            if (m_MenuBarStyle != null)
            {
                m_MenuBarStyle.Draw(r, false, false, false, false);
            }
            else
            {
                Color old = GUI.color;
                GUI.color = new Color(0.05f, 0.04f, 0.03f, 0.9f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = old;
            }
        }

        /// <summary>Width of <paramref name="text"/> as <see cref="DrawText"/> would draw it on one line.</summary>
        /// <summary>Height of <paramref name="text"/> wrapped to <paramref name="width"/>, as <see cref="DrawText"/> draws it.</summary>
        public float MeasureWrappedHeight(string text, float sizeMultiplier, float width)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }
            float s = Screen.height / RefHeight * Scale;
            EnsureStyles(Mathf.RoundToInt(FontSize * s));
            int oldSize = m_LabelStyle.fontSize;
            bool oldWrap = m_LabelStyle.wordWrap;
            m_LabelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(oldSize * sizeMultiplier));
            m_LabelStyle.wordWrap = true;
            m_Content.text = text;
            float h = m_LabelStyle.CalcHeight(m_Content, width);
            m_LabelStyle.fontSize = oldSize;
            m_LabelStyle.wordWrap = oldWrap;
            return h;
        }

        public float MeasureText(string text, float sizeMultiplier)
        {
            float s = Screen.height / RefHeight * Scale;
            EnsureStyles(Mathf.RoundToInt(FontSize * s));
            int oldSize = m_LabelStyle.fontSize;
            m_LabelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(oldSize * sizeMultiplier));
            m_Content.text = text;
            float w = m_LabelStyle.CalcSize(m_Content).x;
            m_LabelStyle.fontSize = oldSize;
            return w;
        }

        /// <summary>Plain coloured outline (no shadow), e.g. an active mode on the ability wheel.</summary>
        public void DrawFrame(Rect r, Color color, float thickness)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            Color old = GUI.color;
            GUI.color = color;
            DrawBox(r, thickness);
            GUI.color = old;
        }

        /// <summary>Shadowed text in the prompt font, word-wrapped inside <paramref name="r"/>.</summary>
        public void DrawText(Rect r, string text, float sizeMultiplier, TextAnchor anchor, Color? color)
        {
            if (string.IsNullOrEmpty(text) || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * Scale;
            EnsureStyles(Mathf.RoundToInt(FontSize * s));
            int oldSize = m_LabelStyle.fontSize;
            TextAnchor oldAnchor = m_LabelStyle.alignment;
            bool oldWrap = m_LabelStyle.wordWrap;
            Color oldColor = m_LabelStyle.normal.textColor;
            m_LabelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(oldSize * sizeMultiplier));
            m_LabelStyle.alignment = anchor;
            m_LabelStyle.wordWrap = true;
            if (color.HasValue)
            {
                m_LabelStyle.normal.textColor = color.Value;
            }
            m_Content.text = text;
            DrawShadowedLabel(r, m_Content);
            m_LabelStyle.fontSize = oldSize;
            m_LabelStyle.alignment = oldAnchor;
            m_LabelStyle.wordWrap = oldWrap;
            m_LabelStyle.normal.textColor = oldColor;
        }

        private static void DrawBox(Rect r, float t)
        {
            Texture2D w = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), w);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), w);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), w);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), w);
        }

        private float GlyphWidth(Texture2D tex, float s)
        {
            float h = GlyphHeight * s;
            if (tex == null || tex.height == 0)
            {
                return h;
            }
            return h * tex.width / tex.height;
        }

        private void DrawShadowedLabel(Rect r, GUIContent content)
        {
            Color old = m_LabelStyle.normal.textColor;
            m_LabelStyle.normal.textColor = ShadowColor;
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), content, m_LabelStyle);
            m_LabelStyle.normal.textColor = old;
            GUI.Label(r, content, m_LabelStyle);
        }

        private void EnsureStyles(int fontSize)
        {
            if (m_LabelStyle == null)
            {
                m_LabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                };
                m_LabelStyle.normal.textColor = TextColor;
                if (m_MenuBar != null)
                {
                    m_MenuBarStyle = new GUIStyle { border = BarArt.MenuBorder };
                    m_MenuBarStyle.normal.background = m_MenuBar;
                }
            }
            // The game loads its fonts after the first frames we draw in; keep looking until one turns up.
            if (m_LabelStyle.font == null && Time.unscaledTime >= m_NextFontSearch)
            {
                m_NextFontSearch = Time.unscaledTime + FontSearchInterval;
                m_LabelStyle.font = FindGameFont();
            }
            if (m_StyledFontSize != fontSize)
            {
                m_LabelStyle.fontSize = fontSize;
                m_StyledFontSize = fontSize;
            }
        }

        private Font FindGameFont()
        {
            Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
            if (fonts.Length != m_LastFontCount)
            {
                m_LastFontCount = fonts.Length;
                var names = new List<string>();
                foreach (Font f in fonts)
                {
                    names.Add(f.name + (f.dynamic ? "" : " (bitmap)"));
                }
                Plugin.Log.LogInfo("Fonts available: " + string.Join(", ", names.ToArray()));
            }

            // Exact names only: the game also ships decorative faces (EspinosaNova-Ornaments, -CapitularIV) whose
            // names share a prefix with the text faces and render as symbols.
            foreach (string wanted in m_FontPreference)
            {
                foreach (Font f in fonts)
                {
                    // IMGUI can only resize dynamic fonts.
                    if (f.dynamic && string.Equals(f.name, wanted.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        Plugin.Log.LogInfo($"Prompt bar font: {f.name}");
                        return f;
                    }
                }
            }
            return null;
        }
    }
}
