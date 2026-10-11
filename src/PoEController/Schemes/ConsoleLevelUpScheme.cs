using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Level-up and character creation (<see cref="UICharacterCreationManager"/>, one step-by-step window) in Console
    /// style (after the console version), on top of the cursor menu:
    /// <list type="bullet">
    /// <item>RB next page / LB previous page — the window's own <c>PressOkay</c> / <c>PressBack</c>, only when the game
    /// allows (<c>ReadyForNextStage</c> / <c>CanGoBack</c>); otherwise the error sound.</item>
    /// <item>LT / RT lower / raise the skill or attribute row under the cursor: its −/+ button's own click (the game
    /// checks the points).</item>
    /// <item>Start = Done (finish), B = Quit (the window's Exit button, which asks first).</item>
    /// </list>
    /// </summary>
    public sealed class ConsoleLevelUpScheme : MenuScheme
    {
        public ConsoleLevelUpScheme(params Binding[] bindings)
            : base("Level up (Console)", bindings)
        {
        }

        private static bool On => Compat.Features.On(Compat.Features.ConsoleMenus);

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            if (!On)
            {
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => HandleConsole(pad));
        }

        private static void HandleConsole(Gamepad pad)
        {
            UICharacterCreationManager cc = UICharacterCreationManager.Instance;
            if (cc == null || !cc.WindowActive())
            {
                return;
            }
            if (pad.GetButtonDown(PadButton.RightShoulder))
            {
                if (CanNext(cc))
                {
                    cc.PressOkay();
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                    Plugin.Log.LogInfo("Level up: next page");
                }
                else
                {
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                }
            }
            if (pad.GetButtonDown(PadButton.LeftShoulder))
            {
                if (cc.CanGoBack())
                {
                    cc.PressBack();
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                    Plugin.Log.LogInfo("Level up: previous page");
                }
                else
                {
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                }
            }
            if (pad.GetButtonDown(PadButton.RightTrigger))
            {
                Adjust(cc, 1);
            }
            if (pad.GetButtonDown(PadButton.LeftTrigger))
            {
                Adjust(cc, -1);
            }
            if (pad.GetButtonDown(PadButton.Start))
            {
                PressNav(cc, UICharacterCreationNavControl.NavType.DONE);
            }
            if (pad.GetButtonDown(PadButton.East))
            {
                PressNav(cc, UICharacterCreationNavControl.NavType.EXIT);
            }
        }

        private static bool CanNext(UICharacterCreationManager cc)
        {
            UICharacterCreationController current = cc.GetCurrentController();
            return current != null && current.ReadyForNextStage() && FindNav(cc, UICharacterCreationNavControl.NavType.ACCEPT) != null;
        }

        /// <summary>The usable nav button of that kind, preferring the current page's own.</summary>
        private static UICharacterCreationNavControl FindNav(UICharacterCreationManager cc, UICharacterCreationNavControl.NavType type)
        {
            UICharacterCreationController current = cc.GetCurrentController();
            UICharacterCreationNavControl any = null;
            foreach (UICharacterCreationNavControl nav in cc.GetComponentsInChildren<UICharacterCreationNavControl>(false))
            {
                if (nav.Control != type || !nav.gameObject.activeInHierarchy || nav.SpriteButton == null || !nav.SpriteButton.enabled)
                {
                    continue;
                }
                if (current != null && nav.GetComponentInParent<UICharacterCreationController>() == current)
                {
                    return nav;
                }
                any = any ?? nav;
            }
            return any;
        }

        /// <summary>A click on the nav button (the game's own handler, incl. its confirmation boxes).</summary>
        private static void PressNav(UICharacterCreationManager cc, UICharacterCreationNavControl.NavType type)
        {
            UICharacterCreationNavControl nav = FindNav(cc, type);
            if (nav == null)
            {
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                return;
            }
            Plugin.Log.LogInfo($"Level up: {type}");
            nav.gameObject.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>The −/+ button pair of the skill or attribute row under the cursor (null = not on such a row).</summary>
        private static Component SetterFor(UICharacterCreationManager cc, int sign)
        {
            Collider hovered = FocusNavigator.ColliderAt(MouseEmulator.Position);
            if (hovered == null || !hovered.transform.IsChildOf(cc.transform))
            {
                return null;
            }
            Transform t = hovered.transform;
            foreach (UICharacterCreationSkillSetter s in cc.GetComponentsInChildren<UICharacterCreationSkillSetter>(false))
            {
                if (s.transform.parent != null && t.IsChildOf(s.transform.parent))
                {
                    return Matching(cc, s.Skill, sign);
                }
            }
            foreach (UICharacterCreationAttributeSetter a in cc.GetComponentsInChildren<UICharacterCreationAttributeSetter>(false))
            {
                if (a.transform.parent != null && t.IsChildOf(a.transform.parent))
                {
                    return MatchingAttribute(cc, a.Stat, sign);
                }
            }
            return null;
        }

        private static Component Matching(UICharacterCreationManager cc, CharacterStats.SkillType skill, int sign)
        {
            foreach (UICharacterCreationSkillSetter s in cc.GetComponentsInChildren<UICharacterCreationSkillSetter>(false))
            {
                if (s.Skill == skill && System.Math.Sign(s.Adjustment) == sign)
                {
                    return s;
                }
            }
            return null;
        }

        private static Component MatchingAttribute(UICharacterCreationManager cc, CharacterStats.AttributeScoreType stat, int sign)
        {
            foreach (UICharacterCreationAttributeSetter a in cc.GetComponentsInChildren<UICharacterCreationAttributeSetter>(false))
            {
                if (a.Stat == stat && System.Math.Sign(a.Adjustment) == sign)
                {
                    return a;
                }
            }
            return null;
        }

        private static void Adjust(UICharacterCreationManager cc, int sign)
        {
            Component setter = SetterFor(cc, sign);
            if (setter == null)
            {
                return;
            }
            // The button's own click: it checks the points / limits itself.
            setter.gameObject.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        protected override void AddNavigationPrompts(List<Prompt> into)
        {
            UICharacterCreationManager cc = UICharacterCreationManager.Instance;
            if (!On || cc == null)
            {
                base.AddNavigationPrompts(into);
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () =>
            {
                AddNavigatePrompt(into);
                into.Add(new Prompt("south", "Select"));
                if (SetterFor(cc, 1) != null || SetterFor(cc, -1) != null)
                {
                    into.Add(new Prompt(new[] { "lt", "rt" }, "Lower / Raise"));
                }
                if (cc.CanGoBack())
                {
                    into.Add(new Prompt("lb", "Back"));
                }
                if (CanNext(cc))
                {
                    into.Add(new Prompt("rb", "Next"));
                }
                if (FindNav(cc, UICharacterCreationNavControl.NavType.DONE) != null)
                {
                    into.Add(new Prompt("start", "Done"));
                }
                if (FindNav(cc, UICharacterCreationNavControl.NavType.EXIT) != null)
                {
                    into.Add(new Prompt("east", "Quit"));
                }
                into.Add(new Prompt("rstick_ud", "Scroll"));
            });
        }
    }
}
