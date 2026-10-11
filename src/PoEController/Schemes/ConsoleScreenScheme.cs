using System;
using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;

namespace PoEController.Schemes
{
    /// <summary>
    /// The full-screen menus in Console style (character sheet, journal, grimoire, area map, party, formations,
    /// stronghold; the console version's layout), on top of the cursor menu (d-pad snap, focus frame, tooltips): A select,
    /// Y details (one right-click: ability / item card), LB / RB the screen's own top row (characters or tabs, when
    /// it has one), LT / RT previous / next screen, RS scroll, B close — the same meaning as in the inventory.
    /// </summary>
    public sealed class ConsoleScreenScheme : MenuScheme
    {
        private readonly string m_ShoulderLabel;
        private readonly Action<int> m_Step;

        /// <param name="shoulderLabel">What LB / RB switch ("Character", "Tab"); null = the screen has no top row.</param>
        /// <param name="step">Called with -1 / +1 for LB / RB (runs under feature ConsoleMenus).</param>
        public ConsoleScreenScheme(string name, string shoulderLabel, Action<int> step, params Binding[] bindings)
            : base(name, bindings)
        {
            m_ShoulderLabel = shoulderLabel;
            m_Step = step;
        }

        private static bool On => Compat.Features.On(Compat.Features.ConsoleMenus);

        protected override bool RightClickOnX => !On;

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            if (!On)
            {
                return;
            }
            // Y = one right-click (MenuScheme lets the button go next frame).
            if (pad.GetButtonDown(PadButton.North))
            {
                MouseEmulator.SetRight(true);
            }
            if (m_Step != null)
            {
                if (pad.GetButtonDown(PadButton.LeftShoulder))
                {
                    Compat.Features.Run(Compat.Features.ConsoleMenus, () => m_Step(-1));
                }
                if (pad.GetButtonDown(PadButton.RightShoulder))
                {
                    Compat.Features.Run(Compat.Features.ConsoleMenus, () => m_Step(1));
                }
            }
        }

        protected override void AddNavigationPrompts(List<Prompt> into)
        {
            if (!On)
            {
                base.AddNavigationPrompts(into);
                return;
            }
            AddNavigatePrompt(into);
            into.Add(new Prompt("south", "Select"));
            into.Add(new Prompt("north", "Details"));
            if (m_ShoulderLabel != null && m_Step != null)
            {
                into.Add(new Prompt(new[] { "lb", "rb" }, m_ShoulderLabel));
            }
            into.Add(new Prompt("rstick_ud", "Scroll"));
        }

        // ---- LB / RB per screen (the game's own previous / next calls) ----

        /// <summary>Character sheet: UICharacterSheetManager.OnNext / OnPrevious.</summary>
        public static void StepCharacterSheet(int dir)
        {
            UICharacterSheetManager sheet = UICharacterSheetManager.Instance;
            PartyMemberAI current = sheet != null && sheet.SelectedCharacter != null ? sheet.SelectedCharacter.GetComponent<PartyMemberAI>() : null;
            if (current == null)
            {
                return;
            }
            sheet.SelectCharacter(dir > 0 ? PartyHelper.SeekNextPartyMember(current) : PartyHelper.SeekPreviousPartyMember(current));
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        /// <summary>Grimoire: UIGrimoireManager.OnNext / OnPrevious.</summary>
        public static void StepGrimoire(int dir)
        {
            UIGrimoireManager grim = UIGrimoireManager.Instance;
            PartyMemberAI current = grim != null && grim.SelectedCharacter != null ? grim.SelectedCharacter.GetComponent<PartyMemberAI>() : null;
            if (current == null)
            {
                return;
            }
            grim.SelectCharacter(dir > 0 ? PartyHelper.SeekNextPartyMember(current) : PartyHelper.SeekPreviousPartyMember(current));
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        /// <summary>Journal: the four tabs (quests, biography, cyclopedia, notes) as NEXT_TAB cycles them, both ways.</summary>
        public static void StepJournalTab(int dir)
        {
            UIJournalManager journal = UIJournalManager.Instance;
            if (journal == null || !Compat.GameRef.TryGet(journal, "m_CurrentScreen", out UIJournalManager.JournalScreen current))
            {
                return;
            }
            int count = (int)UIJournalManager.JournalScreen.Count;
            int next = (((int)current + dir) % count + count) % count;
            journal.ChangeScreen((UIJournalManager.JournalScreen)next);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }
    }
}
