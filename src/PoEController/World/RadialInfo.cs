using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PoEController.Compat;
using PoEController.UI;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// What a spell, ability or item in the Combat Radial / ability wheel does: the game's own hover tooltip text
    /// (what the ability bar shows a mouse player), shown in a panel beside the radial, and the game's full card
    /// (what right-click on the ability bar opens) for Examine.
    /// </summary>
    internal static class RadialInfo
    {
        private const float RefreshSeconds = 0.5f;
        private static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.78f);
        private static readonly Color BodyColor = new Color(0.92f, 0.89f, 0.8f);
        private static readonly Regex SpriteTag = new Regex("<([A-Za-z0-9_]+)>");
        private static readonly Regex BlankLines = new Regex("\n{3,}");

        /// <summary>Beside the radials (config Prompts.RadialDetails).</summary>
        public static bool ShowPanel = true;

        private static Dictionary<string, string> s_SpriteWords;
        private static object s_CachedFor;
        private static GameObject s_CachedOwner;
        private static float s_CachedAt;
        private static string s_Cached;

        /// <summary>The game object whose description this entry shows: the ability, or the item; null for anything else.</summary>
        private static MonoBehaviour Target(AbilityWheel.Entry e)
        {
            if (e == null)
            {
                return null;
            }
            if (e.Ability != null)
            {
                return e.Ability;
            }
            return e.Item != null ? e.Item.baseItem : null;
        }

        public static bool CanExamine(AbilityWheel.Entry e) => Features.On(Features.RadialInfo) && Target(e) != null;

        /// <summary>The game's card for the entry, as right-click on the ability bar opens it.</summary>
        public static void Examine(AbilityWheel.Entry e, CharacterStats owner)
        {
            Features.Run(Features.RadialInfo, () =>
            {
                if (e.Ability != null)
                {
                    UIItemInspectManager.Examine(e.Ability);
                }
                else if (e.Item != null && e.Item.baseItem != null)
                {
                    UIItemInspectManager.Examine(e.Item.baseItem, owner != null ? owner.gameObject : null);
                }
            });
        }

        /// <summary>The entry's tooltip text, made readable outside the game's labels; null when there is none.</summary>
        public static string Text(AbilityWheel.Entry e, CharacterStats owner)
        {
            MonoBehaviour target = Target(e);
            if (target == null || !Features.On(Features.RadialInfo))
            {
                return null;
            }
            GameObject who = owner != null ? owner.gameObject : null;
            // Built fresh at most twice a second (the text includes accuracy and uses left), not every frame.
            if (ReferenceEquals(target, s_CachedFor) && who == s_CachedOwner && Time.unscaledTime - s_CachedAt < RefreshSeconds)
            {
                return s_Cached;
            }
            s_CachedFor = target;
            s_CachedOwner = who;
            s_CachedAt = Time.unscaledTime;
            s_Cached = Features.Get(Features.RadialInfo, () => Clean(((ITooltipContent)target).GetTooltipContent(who)), null);
            return s_Cached;
        }

        /// <summary>
        /// The game's tooltip markup in plain words: colour / link codes removed (the game's own stripping), the
        /// defence and damage icons it draws inline written as their names, and the "Right-click for details" line
        /// dropped (the prompt bar shows the Examine button instead).
        /// </summary>
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            text = NGUITools.StripSymbols(text).Replace("\r", "");
            string rightClick = GUIUtils.GetText(1796);
            if (!string.IsNullOrEmpty(rightClick))
            {
                text = text.Replace(rightClick, "");
            }
            Dictionary<string, string> words = SpriteWords();
            text = SpriteTag.Replace(text, m => words.TryGetValue(m.Groups[1].Value, out string w) ? w + " " : "");
            text = BlankLines.Replace(text, "\n\n").Trim();
            return text.Length > 0 ? text : null;
        }

        private static Dictionary<string, string> SpriteWords()
        {
            if (s_SpriteWords != null)
            {
                return s_SpriteWords;
            }
            var map = new Dictionary<string, string>();
            foreach (CharacterStats.DefenseType d in Enum.GetValues(typeof(CharacterStats.DefenseType)))
            {
                Add(map, () => UIPartyMemberStatIconGetter.GetDefenseTypeSprite(d), () => GUIUtils.GetDefenseTypeString(d));
            }
            foreach (DamagePacket.DamageType d in Enum.GetValues(typeof(DamagePacket.DamageType)))
            {
                Add(map, () => UIPartyMemberStatIconGetter.GetDamageTypeSprite(d), () => GUIUtils.GetDamageTypeString(d));
            }
            s_SpriteWords = map;
            return map;
        }

        private static void Add(Dictionary<string, string> map, Func<string> sprite, Func<string> word)
        {
            try
            {
                string s = sprite();
                string w = word();
                if (!string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(w) && !map.ContainsKey(s))
                {
                    map[s] = w;
                }
            }
            catch (Exception)
            {
                // Values without a sprite or a name (None, Count...) just aren't mapped.
            }
        }

        /// <summary>
        /// The panel: entry name, then the text, wrapped, in a dark box beside the radial, on whichever side of
        /// <paramref name="radial"/> has more room, top-aligned with it and cut off at the screen's bottom edge.
        /// </summary>
        public static void DrawPanel(PromptBar text, Rect radial, string name, string body, float s)
        {
            if (!ShowPanel || string.IsNullOrEmpty(body) || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float margin = 16f * s;
            float gap = 20f * s;
            float right = Screen.width - radial.xMax - gap - margin;
            float left = radial.xMin - gap - margin;
            float room = Mathf.Max(right, left);
            float w = Mathf.Min(460f * s, room);
            if (w < 200f * s)
            {
                return; // no room beside the radial at this aspect ratio
            }
            float x = right >= left ? radial.xMax + gap : radial.xMin - gap - w;
            float pad = 14f * s;
            float innerW = w - pad * 2f;
            float nameH = 34f * s;
            float bodyH = text.MeasureWrappedHeight(body, 0.78f, innerW);
            float h = Mathf.Min(pad * 2f + nameH + 6f * s + bodyH, Screen.height - margin * 2f);
            float y = Mathf.Clamp(radial.y, margin, Screen.height - margin - h);
            Rect r = new Rect(x, y, w, h);

            Color old = GUI.color;
            GUI.color = PanelBg;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.DrawTexture(new Rect(r.x, r.y + pad + nameH, r.width, 3f * s), RadialArt.Divider, ScaleMode.StretchToFill);
            text.DrawText(new Rect(r.x + pad, r.y + pad, innerW, nameH), name, 1.05f, TextAnchor.MiddleLeft, null);
            GUI.BeginClip(new Rect(r.x + pad, r.y + pad + nameH + 6f * s, innerW, r.height - pad * 2f - nameH - 6f * s));
            text.DrawText(new Rect(0f, 0f, innerW, bodyH), body, 0.78f, TextAnchor.UpperLeft, BodyColor);
            GUI.EndClip();
        }
    }
}
