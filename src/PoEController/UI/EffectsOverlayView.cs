using System.Collections.Generic;
using PoEController.World;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Console style, hold Y (the game keeps running). Two views:
    /// <list type="bullet">
    /// <item><b>Icons</b> (on hold): every party member's effect icons stacked upward from the top of their portrait,
    /// each with a draining time bar (green helpful / red hostile).</item>
    /// <item><b>Details</b> (after LB/RB while Y is held): the highlighted character's effects in one panel just above
    /// the portrait bar instead.</item>
    /// </list>
    /// Details panel, modelled on the Switch's Active Effects panel (screenshot #11): per
    /// effect a framed icon, the name, and below it what it does in the character sheet's wording (red when hostile),
    /// with the time left ("12.0 sec left") or "Until rest" / "Until combat ends" / "Active", and a slim bar that
    /// drains with the time left. LB/RB while holding Y changes the selection
    /// and the panel follows. The game's own small status icons beside each portrait stay as they are.
    /// Rows from <see cref="ActiveEffectsPanel.BuildRows"/>, refreshed twice a second while shown.
    /// </summary>
    internal sealed class EffectsOverlayView
    {
        private const float RefHeight = 1080f;
        private const float RefreshInterval = 0.5f;
        private static readonly Color Panel = new Color(0.02f, 0.02f, 0.02f, 0.82f);
        private static readonly Color Gold = new Color(0.78f, 0.64f, 0.36f, 0.95f);
        private static readonly Color IconBox = new Color(0.12f, 0.11f, 0.1f, 1f);
        private static readonly Color Hostile = new Color(0.92f, 0.32f, 0.3f);
        private static readonly Color Helpful = new Color(0.45f, 0.85f, 0.45f);
        private static readonly Color Body = new Color(0.9f, 0.86f, 0.77f);
        private static readonly Color Dim = new Color(0.68f, 0.64f, 0.56f);
        private static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.12f);

        private const int MaxIcons = 10;

        private readonly PromptBar m_Text;
        private List<ActiveEffectsPanel.Row> m_Rows = new List<ActiveEffectsPanel.Row>();
        private PartyMemberAI m_RowsFor;
        private float m_NextRefresh;
        private readonly Dictionary<PartyMemberAI, List<ActiveEffectsPanel.Row>> m_AllRows = new Dictionary<PartyMemberAI, List<ActiveEffectsPanel.Row>>();
        private float m_NextAllRefresh;

        public EffectsOverlayView(PromptBar text)
        {
            m_Text = text;
        }

        /// <param name="details">True after LB/RB while Y is held: the highlighted character's panel instead of icons.</param>
        public void Draw(bool visible, bool details, float scale)
        {
            if (!visible)
            {
                m_NextRefresh = 0f;
                m_NextAllRefresh = 0f;
                return;
            }
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (!details)
            {
                DrawAllIcons(Screen.height / RefHeight * scale);
                return;
            }
            UIPartyPortraitBar bar = UIPartyPortraitBar.Instance;
            Camera ui = InGameUILayout.NGUICamera;
            GameObject leader = DirectMovement.SelectedLeader();
            PartyMemberAI who = leader != null ? leader.GetComponent<PartyMemberAI>() : null;
            if (bar == null || ui == null || who == null || !bar.gameObject.activeInHierarchy || !TryBarTop(bar, ui, out float barLeft, out float barTop))
            {
                return;
            }
            if (who != m_RowsFor || Time.unscaledTime >= m_NextRefresh)
            {
                m_NextRefresh = Time.unscaledTime + RefreshInterval;
                m_RowsFor = who;
                CharacterStats stats = who.GetComponent<CharacterStats>();
                m_Rows = stats != null ? ActiveEffectsPanel.BuildRows(stats) : new List<ActiveEffectsPanel.Row>();
            }
            DrawPanel(who, m_Rows, barLeft, barTop, Screen.height / RefHeight * scale);
        }

        // ---- Icons view: every portrait ----

        private void DrawAllIcons(float s)
        {
            UIPartyPortraitBar bar = UIPartyPortraitBar.Instance;
            Camera ui = InGameUILayout.NGUICamera;
            if (bar == null || ui == null || !bar.gameObject.activeInHierarchy)
            {
                return;
            }
            if (Time.unscaledTime >= m_NextAllRefresh)
            {
                m_NextAllRefresh = Time.unscaledTime + RefreshInterval;
                m_AllRows.Clear();
            }
            foreach (PartyMemberAI m in PartyMemberAI.OnlyPrimaryPartyMembers)
            {
                if (m == null || !TryPortraitRect(bar, ui, m, out Rect r))
                {
                    continue;
                }
                if (!m_AllRows.TryGetValue(m, out List<ActiveEffectsPanel.Row> rows))
                {
                    CharacterStats stats = m.GetComponent<CharacterStats>();
                    rows = stats != null ? ActiveEffectsPanel.BuildRows(stats) : new List<ActiveEffectsPanel.Row>();
                    m_AllRows[m] = rows;
                }
                if (rows.Count > 0)
                {
                    DrawIcons(rows, r, s);
                }
            }
        }

        private static bool TryPortraitRect(UIPartyPortraitBar bar, Camera ui, PartyMemberAI m, out Rect r)
        {
            r = default;
            UIPartyPortrait portrait = bar.GetPortraitFor(m);
            if (portrait == null || portrait.PartyMemberAI != m || !portrait.gameObject.activeInHierarchy)
            {
                return false;
            }
            Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(portrait.transform);
            Vector3 min = ui.WorldToScreenPoint(b.min);
            Vector3 max = ui.WorldToScreenPoint(b.max);
            r = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Screen.height - Mathf.Max(min.y, max.y),
                Mathf.Max(min.x, max.x), Screen.height - Mathf.Min(min.y, max.y));
            return r.width >= 8f;
        }

        /// <summary>Icons two per row stacked upward from the portrait, each with its own draining bar.</summary>
        private void DrawIcons(List<ActiveEffectsPanel.Row> rows, Rect portrait, float s)
        {
            const int perRow = 2;
            float gap = Mathf.Max(2f, portrait.width * 0.04f);
            float icon = (portrait.width - gap * (perRow + 1)) / perRow;
            float barH = Mathf.Max(3f, icon * 0.1f);
            float cell = icon + barH + gap * 1.5f;
            int shown = Mathf.Min(rows.Count, MaxIcons);
            int rowCount = (shown + perRow - 1) / perRow;
            Rect backing = new Rect(portrait.x, portrait.y - rowCount * cell - gap, portrait.width, rowCount * cell + gap);
            Color old = GUI.color;
            GUI.color = Panel;
            GUI.DrawTexture(backing, Texture2D.whiteTexture);
            GUI.color = old;
            for (int i = 0; i < shown; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                float x = portrait.x + gap + col * (icon + gap);
                float y = portrait.y - (row + 1) * cell;
                Rect ir = new Rect(x, y, icon, icon);
                if (i == MaxIcons - 1 && rows.Count > MaxIcons)
                {
                    m_Text.DrawText(ir, "+" + (rows.Count - MaxIcons + 1), 0.9f, TextAnchor.MiddleCenter, null);
                    continue;
                }
                ActiveEffectsPanel.Row e = rows[i];
                if (e.Icon != null)
                {
                    GUI.DrawTexture(ir, e.Icon, ScaleMode.ScaleToFit);
                }
                DrawBar(new Rect(x, y + icon + gap * 0.5f, icon, barH), e.Fraction < 0f ? 1f : e.Fraction, e.Hostile ? Hostile : Helpful);
            }
        }

        // ---- Details view: the highlighted character ----

        /// <summary>Left edge and top edge (GUI coordinates) of the visible portraits.</summary>
        private static bool TryBarTop(UIPartyPortraitBar bar, Camera ui, out float left, out float top)
        {
            left = float.MaxValue;
            top = float.MaxValue;
            foreach (PartyMemberAI m in PartyMemberAI.OnlyPrimaryPartyMembers)
            {
                UIPartyPortrait portrait = m != null ? bar.GetPortraitFor(m) : null;
                if (portrait == null || portrait.PartyMemberAI != m || !portrait.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(portrait.transform);
                Vector3 min = ui.WorldToScreenPoint(b.min);
                Vector3 max = ui.WorldToScreenPoint(b.max);
                left = Mathf.Min(left, Mathf.Min(min.x, max.x));
                top = Mathf.Min(top, Screen.height - Mathf.Max(min.y, max.y));
            }
            return left < float.MaxValue;
        }

        private void DrawPanel(PartyMemberAI who, List<ActiveEffectsPanel.Row> rows, float barLeft, float barTop, float s)
        {
            float w = Mathf.Min(500f * s, Screen.width - 32f);
            float pad = 14f * s;
            float icon = 48f * s;
            float gap = 12f * s;
            float textX = pad + icon + gap;
            float textW = w - textX - pad;
            float maxH = barTop - 40f * s;
            CharacterStats stats = who.GetComponent<CharacterStats>();

            // Measure the entries (name line + wrapped description + bar) to fit the panel above the portraits.
            float titleH = 30f * s;
            float height = pad + titleH;
            int shown = 0;
            var heights = new List<float>();
            foreach (ActiveEffectsPanel.Row e in rows)
            {
                float nameH = 28f * s;
                float descH = m_Text.MeasureWrappedHeight(Describe(e), 0.74f, textW);
                float h = Mathf.Max(icon, nameH + descH) + (e.Fraction >= 0f ? 6f * s : 0f) + gap;
                if (height + h + pad + 24f * s > maxH && shown > 0)
                {
                    break;
                }
                heights.Add(h);
                height += h;
                shown++;
            }
            bool more = shown < rows.Count;
            if (rows.Count == 0)
            {
                height += 28f * s;
            }
            if (more)
            {
                height += 22f * s;
            }
            height += pad;

            float x = Mathf.Clamp(barLeft, 16f, Screen.width - w - 16f);
            float y = Mathf.Max(16f, barTop - height - 6f * s);
            Rect panel = new Rect(x, y, w, height);
            Color old = GUI.color;
            GUI.color = Panel;
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = old;
            m_Text.DrawFrame(panel, Gold, Mathf.Max(1f, 1.5f * s));

            float cy = y + pad;
            m_Text.DrawText(new Rect(x + pad, cy, w - pad * 2f, titleH), (stats != null ? stats.Name() : "") + "  -  Active Effects", 0.85f, TextAnchor.MiddleLeft, Gold);
            cy += titleH;
            if (rows.Count == 0)
            {
                m_Text.DrawText(new Rect(x + pad, cy, w - pad * 2f, 26f * s), "No active effects", 0.8f, TextAnchor.MiddleLeft, Dim);
            }
            for (int i = 0; i < shown; i++)
            {
                ActiveEffectsPanel.Row e = rows[i];
                Rect box = new Rect(x + pad, cy, icon, icon);
                GUI.color = IconBox;
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = old;
                m_Text.DrawFrame(box, new Color(0.35f, 0.33f, 0.3f), Mathf.Max(1f, 1f * s));
                if (e.Icon != null)
                {
                    GUI.DrawTexture(new Rect(box.x + icon * 0.15f, box.y + icon * 0.15f, icon * 0.7f, icon * 0.7f), e.Icon, ScaleMode.ScaleToFit);
                }
                float tx = x + textX;
                m_Text.DrawText(new Rect(tx, cy, textW, 28f * s), e.Name, 1f, TextAnchor.MiddleLeft, Body);
                string desc = Describe(e);
                float descH = m_Text.MeasureWrappedHeight(desc, 0.74f, textW);
                m_Text.DrawText(new Rect(tx, cy + 28f * s, textW, descH + 2f * s), desc, 0.74f, TextAnchor.UpperLeft, e.Hostile ? Hostile : Body);
                if (e.Fraction >= 0f)
                {
                    DrawBar(new Rect(tx, cy + 28f * s + descH + 3f * s, textW, Mathf.Max(2f, 3f * s)), e.Fraction, e.Hostile ? Hostile : Helpful);
                }
                cy += heights[i];
            }
            if (more)
            {
                m_Text.DrawText(new Rect(x + pad, cy, w - pad * 2f, 20f * s), $"+{rows.Count - shown} more (LT > Info > Active effects)", 0.62f, TextAnchor.MiddleLeft, Dim);
            }
        }

        /// <summary>What it does, plus how long it stays.</summary>
        private static string Describe(ActiveEffectsPanel.Row e)
        {
            string what = string.IsNullOrEmpty(e.Text) ? e.Short : e.Text;
            string when = e.Time != null ? e.Time + " left" : e.Until;
            return string.IsNullOrEmpty(when) ? what : what + "  (" + when + ")";
        }

        private static void DrawBar(Rect r, float fraction, Color color)
        {
            Color old = GUI.color;
            GUI.color = BarTrack;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
