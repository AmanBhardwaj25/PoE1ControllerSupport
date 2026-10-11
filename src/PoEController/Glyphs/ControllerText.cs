using System.Collections.Generic;

namespace PoEController.Glyphs
{
    /// <summary>
    /// Controller versions of the game's keyboard/mouse instructions (English only; other languages keep the game's
    /// text). Tokens like <c>{south}</c> or <c>{lb+rb}</c> become controller glyphs. Every instruction here names a
    /// binding the mod really has — no glyph may promise a button that does something else.
    /// </summary>
    internal static class ControllerText
    {
        private const string Screens = "{back}, then {lb}/{rb} to switch screens";

        /// <summary>UI strings (gui.stringtable) by ID.</summary>
        private static readonly Dictionary<int, string> s_Gui = new Dictionary<int, string>
        {
            // Item / weapon / ability tooltips. In menus X is the right click.
            { 1796, "{west} for details." },
            { 1712, "Custom Formation - {west} to modify." },
            // Area map: A held drags, the stick moves.
            { 1725, "Hold {south} and move {lstick} to navigate" },
        };

        /// <summary>Tutorial texts (tutorial.stringtable) by ID — whole paragraphs, the rest of the wording kept.</summary>
        private static readonly Dictionary<int, string> s_Tutorials = new Dictionary<int, string>
        {
            {
                2, "To select a party member, press {lb} or {rb} to step through the party, or press {dpad_down} to pick "
                 + "portraits directly ({south} adds or removes a member, {north} selects only that one). To select the "
                 + "whole party, press {lb} and {rb} together."
            },
            {
                3, "To move selected characters, push {lstick} the way you want to go; all selected characters follow in "
                 + "formation. For an exact spot, hold {lt} for a cursor and press {south} where it is a circle of four "
                 + "wedges. If you see a red circle with a slash through it, you cannot walk on that part of the map."
            },
            {
                4, "Most people you encounter in the world are neutral or friendly. Walk up to them and press {south} when "
                 + "the prompt appears over them to interact ({dpad_lr} picks another nearby target). Many characters will "
                 + "simply greet you and go on their way, but others will have larger conversations to explore. The "
                 + "merchant, Heodan, has a conversation that opens a store with equipment for sale."
            },
            {
                8, "Exploration is key in Pillars of Eternity. As you make your way through the Eastern Reach, open the Area "
                 + "Map (" + Screens + ") to see what parts of the map you've already been to and what's left to explore."
            },
            {
                9, "As you receive quests, your Journal will update with relevant information. If you ever get stuck, open "
                 + "it up (" + Screens + ") to review your notes."
            },
            {
                10, "Pillars of Eternity combat uses a pauseable real-time system. Because you will often manage more than "
                  + "one character at a time, it's a good idea to pause the game ({rt}), issue orders, and resume real-time "
                  + "({rt} again) to see the orders play out. The Options menu ({start}) also contains many choices for "
                  + "automatic pausing when certain conditions are met (e.g., the start of combat)."
            },
            {
                23, "The Eastern Reach is a dangerous place full of lurking enemies and carefully-concealed secrets. Enable "
                  + "Scouting Mode ({west}) to move stealthily and search for hidden objects like traps and secret doors."
            },
            {
                28, "Wizards have the ability to learn a staggering number of spells, but they are always limited by the "
                  + "capacity of their grimoires. At any given time, a grimoire may only contain four spells of each level. "
                  + "Press {south} on a spell in the right panel to remove it from the grimoire. Add spells to empty slots "
                  + "by pressing {south} on the spell in the left panel."
            },
            {
                30, "Press {south} on a chant to select it. Phrases chosen with {south} in the left panel will be added to "
                  + "the selected chant. If you wish to remove a phrase, press {south} on the phrase in the chant."
            },
            {
                31, "You can switch to new weapon sets with Switch weapons in a character's ability wheel ({north})."
            },
            {
                32, "When multiple party members are selected, the action bar is hidden. The ability wheel ({north}) always "
                  + "shows one character's options; {lb}/{rb} inside it switches to another character."
            },
            {
                34, "Your character has earned enough experience to advance a level. Open the character sheet ("
                  + Screens + ") and choose Level Up."
            },
            {
                35, "After leveling a character up, you may want to review their statistics. Open their character sheet ("
                  + Screens + ") to see their attributes, skills, abilities, and talents in greater detail."
            },
            {
                46, "Your stronghold is now available! You can manage your stronghold at any time through the castle icon, "
                  + "or with " + Screens + "."
            },
            {
                47, "Interactive objects near you are highlighted as you move: {south} uses the one with the prompt, and "
                  + "{dpad_lr} steps to the next one."
            },
        };

        /// <summary>The controller glyph for a game action the mod presses from the pad, if any.</summary>
        public static string GlyphFor(MappedControl control)
        {
            switch (control)
            {
                case MappedControl.PAUSE: return "rt";
                case MappedControl.PASS_TURN: return "rt";
                case MappedControl.WAIT_TURN: return "dpad_up";
                case MappedControl.STEALTH_TOGGLE: return "west";
                case MappedControl.INVENTORY: return "back";
                case MappedControl.OPTIONS: return "start";
                case MappedControl.NEXT_COMPANION: return "rb";
                case MappedControl.PREVIOUS_COMPANION: return "lb";
                case MappedControl.SELECT_ALL: return "lb+rb";
                default: return null;
            }
        }

        /// <summary>Controller version of a game string, with glyph tokens; false to keep the game's text.</summary>
        public static bool TryGet(DatabaseString.StringTableType table, int id, string original, out string text)
        {
            text = null;
            string template;
            switch (table)
            {
                case DatabaseString.StringTableType.Gui:
                    if (!s_Gui.TryGetValue(id, out template))
                    {
                        return false;
                    }
                    break;
                case DatabaseString.StringTableType.Tutorial:
                    if (!s_Tutorials.TryGetValue(id, out template))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
            // Console-style screens word the "Right-click for details." line themselves.
            string hint = table == DatabaseString.StringTableType.Gui && id == 1796 ? ScreenItemHint() : null;
            if (hint != null)
            {
                template = hint;
            }
            // Item tooltips: Y = item menu, but only where it works for this item (inventory / stash).
            else if (table == DatabaseString.StringTableType.Gui && id == 1796 && ItemMenuAvailable())
            {
                template += "\n{north} for item options.";
            }
            // Shops: Y = quick compare with the selected companion's equipment.
            else if (table == DatabaseString.StringTableType.Gui && id == 1796 && QuickCompareAvailable())
            {
                template += "\n{north} to quick compare.";
            }
            text = TextGlyphs.Rewrite(Expand(template), original);
            return true;
        }

        private static string ScreenItemHint()
        {
            return Plugin.Instance != null && Plugin.Instance.Router != null && Plugin.Instance.Router.Active is Schemes.IItemTooltipHint h
                ? h.ItemTooltipHint
                : null;
        }

        private static bool ItemMenuAvailable()
        {
            return Plugin.Instance != null && Plugin.Instance.Router != null
                && Plugin.Instance.Router.Active is Schemes.InventoryScheme inv && inv.HoveredHasActions;
        }

        private static bool QuickCompareAvailable()
        {
            return Plugin.Instance != null && Plugin.Instance.Router != null
                && Plugin.Instance.Router.Active is Schemes.StoreScheme store && store.HoveredCanCompare;
        }

        /// <summary><c>{name}</c> → glyph token (fallback = the glyph name; the whole text falls back via <see cref="TextGlyphs.Rewrite"/>).</summary>
        private static string Expand(string template)
        {
            var sb = new System.Text.StringBuilder(template.Length + 32);
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                int close = open >= 0 ? template.IndexOf('}', open) : -1;
                if (open < 0 || close < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }
                sb.Append(template, i, open - i);
                string name = template.Substring(open + 1, close - open - 1);
                sb.Append(TextGlyphs.Token(name, name));
                i = close + 1;
            }
            return sb.ToString();
        }
    }
}
