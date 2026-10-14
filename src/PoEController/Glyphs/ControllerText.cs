using System.Collections.Generic;

namespace PoEController.Glyphs
{
    /// <summary>
    /// Controller versions of the game's keyboard/mouse instructions (English only; other languages keep the game's
    /// text). Tokens like <c>{south}</c> or <c>{lb+rb}</c> become controller glyphs. Every instruction here names a
    /// binding the mod really has — no glyph may promise a button that does something else. The two control styles
    /// put actions on different buttons: the texts name the action and each style's table (<see cref="Layout"/>)
    /// says which button that is there.
    /// </summary>
    internal static class ControllerText
    {
        /// <summary>UI strings (gui.stringtable) by ID.</summary>
        private static readonly Dictionary<int, string> s_Gui = new Dictionary<int, string>
        {
            // Item / weapon / ability tooltips. In menus X is the right click.
            { 1796, "{west} for details." },
            { 1712, "Custom Formation - {west} to modify." },
            // Area map: A held drags, the stick moves.
            { 1725, "Hold {south} and move {lstick} to navigate" },
        };

        /// <summary>
        /// What one control style calls things: which pad button does a game action (<see cref="Controls"/>, also the
        /// "[key]" hints, see <see cref="GlyphFor"/>) and the wording for what works differently there
        /// (<see cref="Phrases"/>). The texts below name actions, never buttons, so each style's buttons live here once.
        /// </summary>
        private sealed class Layout
        {
            public Dictionary<MappedControl, string> Controls;
            public Dictionary<string, string> Phrases;
        }

        /// <summary>PC layout (<see cref="Schemes.WorldScheme"/>): RT pause, X scouting, Y ability wheel, View + LB / RB screens.</summary>
        private static readonly Layout s_Pc = new Layout
        {
            Controls = new Dictionary<MappedControl, string>
            {
                { MappedControl.PAUSE, "rt" },
                { MappedControl.PASS_TURN, "rt" },
                { MappedControl.WAIT_TURN, "dpad_up" },
                { MappedControl.STEALTH_TOGGLE, "west" },
                { MappedControl.INVENTORY, "back" },
                { MappedControl.OPTIONS, "start" },
                { MappedControl.NEXT_COMPANION, "rb" },
                { MappedControl.PREVIOUS_COMPANION, "lb" },
                { MappedControl.SELECT_ALL, "lb+rb" },
            },
            Phrases = new Dictionary<string, string>
            {
                { "screens", "{back}, then {lb}/{rb} to switch screens" },
                { "areamap", "{back}, then {lb}/{rb} to switch screens" },
                { "pick_members", "or press {dpad_down} to pick portraits directly" },
                { "exact_move", "hold {lt} for a cursor" },
                { "next_target", " ({dpad_lr} picks another nearby target)" },
                { "next_object", ", and {dpad_lr} steps to the next one" },
                { "wheel", "ability wheel ({north})" },
                { "wheel_switch", "{lb}/{rb} inside it switches" },
                { "weapon_switch", "Switch weapons" },
            },
        };

        /// <summary>
        /// Console style (<see cref="Schemes.ConsoleWorldScheme"/>): X pause, L3 scouting, Y move pointer, RT Combat
        /// Radial, LT General Radial (screens), View area map, LB / RB held = party ring, no next-target button.
        /// </summary>
        private static readonly Layout s_Console = new Layout
        {
            Controls = new Dictionary<MappedControl, string>
            {
                { MappedControl.PAUSE, "west" },
                // Ending the turn is a hold of X there; a delayed turn is a Combat Radial slot.
                { MappedControl.PASS_TURN, "west" },
                { MappedControl.STEALTH_TOGGLE, "lstick_click" },
                { MappedControl.AREAMAP, "back" },
                { MappedControl.OPTIONS, "start" },
                { MappedControl.NEXT_COMPANION, "rb" },
                { MappedControl.PREVIOUS_COMPANION, "lb" },
                { MappedControl.SELECT_ALL, "lb+rb" },
            },
            Phrases = new Dictionary<string, string>
            {
                { "screens", "{lt}, the General Radial" },
                { "areamap", "{back}" },
                { "pick_members", "or hold {lb} or {rb} for the party ring" },
                { "exact_move", "press {north} for the move pointer" },
                { "next_target", "" },
                { "next_object", "" },
                { "wheel", "Combat Radial ({rt})" },
                { "wheel_switch", "{lb}/{rb} switches" },
                { "weapon_switch", "Switch weapon set" },
            },
        };

        private static Layout Active =>
            Plugin.Instance != null && Plugin.Instance.Style == ControlStyle.Console ? s_Console : s_Pc;

        /// <summary>
        /// Tutorial texts (tutorial.stringtable) by ID, whole paragraphs with the rest of the wording kept. <c>{@NAME}</c>
        /// is the button of game action NAME in the active style, <c>{@phrase}</c> that style's wording (see
        /// <see cref="Layout"/>), <c>{name}</c> a fixed glyph (the same in both styles).
        /// </summary>
        private static readonly Dictionary<int, string> s_Tutorials = new Dictionary<int, string>
        {
            {
                2, "To select a party member, press {@PREVIOUS_COMPANION} or {@NEXT_COMPANION} to step through the party, "
                 + "{@pick_members} ({south} adds or removes a member, {north} selects only that one). To select the "
                 + "whole party, press {@SELECT_ALL} together."
            },
            {
                3, "To move selected characters, push {lstick} the way you want to go; all selected characters follow in "
                 + "formation. For an exact spot, {@exact_move} and press {south} where it is a circle of four "
                 + "wedges. If you see a red circle with a slash through it, you cannot walk on that part of the map."
            },
            {
                4, "Most people you encounter in the world are neutral or friendly. Walk up to them and press {south} when "
                 + "the prompt appears over them to interact{@next_target}. Many characters will simply greet you and go "
                 + "on their way, but others will have larger conversations to explore. The merchant, Heodan, has a "
                 + "conversation that opens a store with equipment for sale."
            },
            {
                8, "Exploration is key in Pillars of Eternity. As you make your way through the Eastern Reach, open the Area "
                 + "Map ({@areamap}) to see what parts of the map you've already been to and what's left to explore."
            },
            {
                9, "As you receive quests, your Journal will update with relevant information. If you ever get stuck, open "
                 + "it up ({@screens}) to review your notes."
            },
            {
                10, "Pillars of Eternity combat uses a pauseable real-time system. Because you will often manage more than "
                  + "one character at a time, it's a good idea to pause the game ({@PAUSE}), issue orders, and resume "
                  + "real-time ({@PAUSE} again) to see the orders play out. The Options menu ({@OPTIONS}) also contains "
                  + "many choices for automatic pausing when certain conditions are met (e.g., the start of combat)."
            },
            {
                23, "The Eastern Reach is a dangerous place full of lurking enemies and carefully-concealed secrets. Enable "
                  + "Scouting Mode ({@STEALTH_TOGGLE}) to move stealthily and search for hidden objects like traps and "
                  + "secret doors."
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
                31, "You can switch to new weapon sets with {@weapon_switch} in a character's {@wheel}."
            },
            {
                32, "When multiple party members are selected, the action bar is hidden. The {@wheel} always shows one "
                  + "character's options; {@wheel_switch} to another character."
            },
            {
                34, "Your character has earned enough experience to advance a level. Open the character sheet "
                  + "({@screens}) and choose Level Up."
            },
            {
                35, "After leveling a character up, you may want to review their statistics. Open their character sheet "
                  + "({@screens}) to see their attributes, skills, abilities, and talents in greater detail."
            },
            {
                46, "Your stronghold is now available! You can manage your stronghold at any time through the castle icon, "
                  + "or with {@screens}."
            },
            {
                47, "Interactive objects near you are highlighted as you move: {south} uses the one with the "
                  + "prompt{@next_object}."
            },
        };

        /// <summary>The controller glyph for a game action the mod presses from the pad in the active style, if any.</summary>
        public static string GlyphFor(MappedControl control) =>
            Active.Controls.TryGetValue(control, out string glyph) ? glyph : null;

        /// <summary>
        /// <c>{@NAME}</c> / <c>{@phrase}</c> → the active style's button / wording (a name it doesn't know stays as it
        /// is, so a missing entry shows up in the text instead of silently naming a wrong button).
        /// </summary>
        private static string ApplyLayout(string template)
        {
            Layout layout = Active;
            var sb = new System.Text.StringBuilder(template.Length + 32);
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf("{@", i, System.StringComparison.Ordinal);
                int close = open >= 0 ? template.IndexOf('}', open) : -1;
                if (open < 0 || close < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }
                sb.Append(template, i, open - i);
                string name = template.Substring(open + 2, close - open - 2);
                if (layout.Phrases.TryGetValue(name, out string phrase))
                {
                    sb.Append(phrase);
                }
                else if (System.Enum.TryParse(name, out MappedControl control) && layout.Controls.TryGetValue(control, out string glyph))
                {
                    sb.Append('{').Append(glyph).Append('}');
                }
                else
                {
                    sb.Append(name);
                }
                i = close + 1;
            }
            return sb.ToString();
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
            text = TextGlyphs.Rewrite(Expand(ApplyLayout(template)), original);
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
