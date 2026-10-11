using System;
using HarmonyLib;
using UnityEngine;

namespace PoEController.Patches
{
    /// <summary>
    /// Brighter tooltip words (0.36.0: on the character sheet the words with a tooltip were hard to tell
    /// from the text around them). The game colours them from <c>UIGlobalColor</c>'s link sets, and its neutral
    /// colours sit close to — on dark panels even below — the normal text: parchment #353856 next to dark-brown text,
    /// wood (dark panels) #beb6ab next to #eddebf. Neutral links become blue, as the Switch port did for parchment
    /// (#0062c3 / hovered #0080ff, its own data) and a light blue on dark panels (ours). Buff / debuff words (green /
    /// red) stay the game's. Only the colour the game asks for changes — nothing is written to game data; a label
    /// picks the new colour up the next time the game sets its text. Config <c>Menus.BrightTooltipWords</c>.
    /// </summary>
    [Compat.Feature(Compat.Features.LinkColors)]
    internal static class LinkColorPatches
    {
        public static bool Enabled = true;

        private static readonly Color ParchmentNeutral = new Color32(0x00, 0x62, 0xc3, 0xff);
        private static readonly Color ParchmentNeutralHover = new Color32(0x00, 0x80, 0xff, 0xff);
        private static readonly Color WoodNeutral = new Color32(0x7f, 0xb2, 0xff, 0xff);
        private static readonly Color WoodNeutralHover = new Color32(0xb8, 0xd6, 0xff, 0xff);

        private static readonly System.Collections.Generic.HashSet<string> s_Logged = new System.Collections.Generic.HashSet<string>();

        /// <summary>Which link colour set each screen uses, once per screen and set (to check the colours fit their background).</summary>
        private static void LogOnce(UIGlobalColor.LinkStyle style)
        {
            string screen = Plugin.Instance != null && Plugin.Instance.Router.Active != null ? Plugin.Instance.Router.Active.Name : "none";
            if (s_Logged.Count < 100 && s_Logged.Add(screen + "|" + style))
            {
                Plugin.Log.LogInfo($"Tooltip words: '{style}' colours on screen '{screen}'");
            }
        }

        [HarmonyPatch(typeof(UIGlobalColor), nameof(UIGlobalColor.Get),
            typeof(UIGlobalColor.TextColor), typeof(UIGlobalColor.LinkStyle), typeof(bool), typeof(UIGlobalColor.LinkType))]
        private static class LinkColor
        {
            private static void Postfix(UIGlobalColor.TextColor color, UIGlobalColor.LinkStyle linkStyle, bool hovered,
                UIGlobalColor.LinkType linkType, ref Color __result)
            {
                try
                {
                    if (!Enabled || color != UIGlobalColor.TextColor.LINK || linkType != UIGlobalColor.LinkType.NEUTRAL)
                    {
                        return;
                    }
                    LogOnce(linkStyle);
                    if (linkStyle == UIGlobalColor.LinkStyle.PARCHMENT)
                    {
                        __result = hovered ? ParchmentNeutralHover : ParchmentNeutral;
                    }
                    else if (linkStyle == UIGlobalColor.LinkStyle.WOOD)
                    {
                        __result = hovered ? WoodNeutralHover : WoodNeutral;
                    }
                }
                catch (Exception e)
                {
                    Compat.Features.Off(Compat.Features.LinkColors, e.Message);
                    Enabled = false;
                }
            }
        }
    }
}
