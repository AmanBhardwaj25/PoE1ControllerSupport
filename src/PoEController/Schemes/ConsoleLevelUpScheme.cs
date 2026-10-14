using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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
    /// <item>Rows (an attribute, a skill, an appearance part, the portrait): the row is one stop; d-pad / LS left /
    /// right press its - / + button (the button's own click: the game checks the points).</item>
    /// <item>One-choice lists (gender, race, class, culture, background, voice, deity ...): moving onto an option
    /// chooses it, like the console. Spells, abilities, talents: moving shows the entry's description without choosing
    /// it; A chooses / unchooses.</item>
    /// <item>LT / RT: previous / next spell level or talent category (the tabs above the list, not d-pad stops); on a
    /// page without tabs they lower / raise the row under the cursor.</item>
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

        private static readonly MethodInfo s_IconClick = AccessTools.Method(typeof(UIColorSelectorLine), "OnIconClick");
        private static readonly FieldInfo s_DollAngle = AccessTools.Field(typeof(UICharacterCreationManager), "m_PaperdollAngle");

        /// <summary>RS left / right at full tilt turns the character this many degrees per second.</summary>
        private const float TurnSpeed = 180f;

        /// <summary>
        /// RS left / right turns the character, as a mouse drag over it does (the game's own angle and
        /// <c>PE_Paperdoll.LookAt</c>); RS up / down stays the description scroll.
        /// </summary>
        private static void TurnCharacter(Gamepad pad, float dt)
        {
            UICharacterCreationManager cc = UICharacterCreationManager.Instance;
            float x = pad.RightStick.x;
            if (cc == null || s_DollAngle == null || Mathf.Abs(x) < 0.25f || Mathf.Abs(x) < Mathf.Abs(pad.RightStick.y)
                || !CharacterShown(cc))
            {
                return;
            }
            float angle = (float)s_DollAngle.GetValue(cc) + x * TurnSpeed * dt;
            s_DollAngle.SetValue(cc, angle);
            PE_Paperdoll.LookAt(angle);
        }

        private static bool CharacterShown(UICharacterCreationManager cc) =>
            cc.CharacterRenderZone != null && cc.CharacterRenderZone.gameObject.activeInHierarchy && cc.CharacterRenderZone.alpha > 0.01f;

        /// <summary>The colour palette (hair / skin / primary / secondary) that is open, or null.</summary>
        private static UIColorSelectorLine OpenPalette()
        {
            UICharacterCreationManager cc = UICharacterCreationManager.Instance;
            if (cc == null)
            {
                return null;
            }
            foreach (UIColorSelectorLine line in cc.GetComponentsInChildren<UIColorSelectorLine>(false))
            {
                if (line.gameObject.activeInHierarchy)
                {
                    return line;
                }
            }
            return null;
        }

        private UIColorSelectorLine m_Palette;

        /// <summary>While a palette is open A is the scheme's (pick the colour), not the mouse button.</summary>
        protected override bool ClickOnA => !(On && OpenPalette() != null);

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            if (!On || InTooltipMode)
            {
                return;
            }
            UIColorSelectorLine palette = OpenPalette();
            if (palette != m_Palette)
            {
                m_Palette = palette;
                if (palette != null)
                {
                    Compat.Features.Run(Compat.Features.ConsoleMenus, () => PaletteOpened(palette));
                }
            }
            if (palette != null)
            {
                Compat.Features.Run(Compat.Features.ConsoleMenus, () => HandlePalette(palette, pad));
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => TurnCharacter(pad, dt));
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => HandleConsole(pad));
            if (MovedThisFrame)
            {
                Compat.Features.Run(Compat.Features.ConsoleMenus, OnNewFocus);
            }
        }

        /// <summary>A palette opened: the cursor onto the colour in use (the game's selection mark), else the first one.</summary>
        private static void PaletteOpened(UIColorSelectorLine palette)
        {
            Transform on = null;
            UIAnchor mark = palette.SelectionIcon != null ? palette.SelectionIcon.GetComponent<UIAnchor>() : null;
            if (mark != null && mark.widgetContainer != null && mark.widgetContainer.gameObject.activeInHierarchy)
            {
                on = mark.widgetContainer.transform;
            }
            else if (palette.RootIcon != null)
            {
                on = palette.RootIcon.transform;
            }
            MoveOnto(on != null ? on.GetComponentInChildren<Collider>() : null);
            Plugin.Log.LogInfo("Level up: colour palette open");
        }

        /// <summary>
        /// The palette: the d-pad moves between its colours (the only stops), A takes the one under the cursor (the
        /// game's own icon click) and closes, B closes; both go back to the colour button that opened it.
        /// </summary>
        private static void HandlePalette(UIColorSelectorLine palette, Gamepad pad)
        {
            if (pad.GetButtonDown(PadButton.South))
            {
                Collider hovered = FocusNavigator.ColliderAt(MouseEmulator.Position);
                // The swatch is the colour icon itself or holds the part under the cursor (a button piece inside it).
                Transform icon = hovered != null && palette.RootIcon != null ? hovered.transform : null;
                Transform row = palette.RootIcon != null ? palette.RootIcon.transform.parent : null;
                while (icon != null && icon.parent != row)
                {
                    icon = icon.parent;
                }
                if (icon != null && icon.GetComponent<UISprite>() != null && s_IconClick != null)
                {
                    s_IconClick.Invoke(palette, new object[] { icon.gameObject });
                    ClosePalette(palette, pad, "colour chosen");
                }
                else if (hovered != null && NGUITools.FindInParents<UIColorSelectorClose>(hovered.gameObject) != null)
                {
                    ClosePalette(palette, pad, "closed");
                }
                else
                {
                    Plugin.Log.LogInfo($"Level up: A in the palette on '{(hovered != null ? hovered.name : "nothing")}' (not a colour)");
                }
            }
            else if (pad.GetButtonDown(PadButton.East))
            {
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                ClosePalette(palette, pad, "closed (B)");
            }
        }

        private static void ClosePalette(UIColorSelectorLine palette, Gamepad pad, string why)
        {
            GameObject opener = palette.CurrentTarget;
            palette.Hide();
            // A / B are still down: kept from the next frame (A would click the colour button and open it again).
            pad.SuppressHeldButtons();
            UICharacterCreationColorSelector selector = opener != null ? opener.GetComponent<UICharacterCreationColorSelector>() : null;
            MoveOnto(selector != null && selector.Collider != null ? selector.Collider.GetComponent<Collider>() : null);
            Plugin.Log.LogInfo("Level up: colour palette " + why);
        }

        private static void MoveOnto(Collider c)
        {
            Camera cam = c != null ? NGUITools.FindCameraForLayer(c.gameObject.layer) : null;
            if (cam == null)
            {
                return;
            }
            Vector3 p = cam.WorldToScreenPoint(c.bounds.center);
            MouseEmulator.MoveTo(new Vector2(p.x, p.y));
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
            if (pad.GetButtonDown(PadButton.RightTrigger) && !StepTab(cc, 1))
            {
                Adjust(cc, 1);
            }
            if (pad.GetButtonDown(PadButton.LeftTrigger) && !StepTab(cc, -1))
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

        /// <summary>
        /// The step icons along the top jump to a page: LB / RB already move between the pages (and the console has no
        /// such jumps), so they are not d-pad stops. Previous / Next / Done / Exit show their glyphs (see the prompts),
        /// which takes them out of the d-pad too.
        /// </summary>
        protected override bool SkipStop(Collider c) =>
            On && (SkipForPalette(c) || c.GetComponent<UICharacterCreationControllerTrigger>() != null
                // Previous / Next / Done / Exit: LB / RB / Start / B (also before their glyphs are up, while the window fades in).
                || (NGUITools.FindInParents<UICharacterCreationNavControl>(c.gameObject) != null && !KeepStop(c))
                // Spell levels and talent categories: LT / RT.
                || c.GetComponent<UICharacterCreationAbilityLevelButton>() != null
                || c.GetComponent<UICharacterCreationTalentCategoryButton>() != null);

        /// <summary>A palette is open: only its own colours (and close button) are stops.</summary>
        private bool SkipForPalette(Collider c) => m_Palette != null && m_Palette.gameObject.activeInHierarchy && !c.transform.IsChildOf(m_Palette.transform);

        /// <summary>A - / + button of an attribute, skill or appearance row, or that row's own tooltip area: the row.</summary>
        protected override Transform GroupAs(Collider c)
        {
            if (!On)
            {
                return null;
            }
            if (IsSetter(c.gameObject))
            {
                return c.transform.parent;
            }
            Transform parent = c.transform.parent;
            if (parent != null && (c.GetComponent<UICharacterCreationAttributeToolTip>() != null || c.GetComponent<UICharacterCreationSkillTooltip>() != null))
            {
                if (HasSetterChild(c.transform))
                {
                    return c.transform;
                }
                if (HasSetterChild(parent))
                {
                    return parent;
                }
            }
            return null;
        }

        private static bool IsSetter(GameObject go) =>
            go.GetComponent<UICharacterCreationAttributeSetter>() != null || go.GetComponent<UICharacterCreationSkillSetter>() != null
            || go.GetComponent<UICharacterCreationAppearanceSetter>() != null;

        private static bool HasSetterChild(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                if (IsSetter(t.GetChild(i).gameObject))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Left / right on a row: its - / + button, the cursor stays on the row.</summary>
        protected override bool ConsumeDirection(Vector2 dir)
        {
            if (!On || Mathf.Abs(dir.x) <= Mathf.Abs(dir.y))
            {
                return false;
            }
            Transform row = FocusNavigator.RowAt(MouseEmulator.Position);
            if (row == null)
            {
                return false;
            }
            PressRow(row, dir.x > 0f ? 1 : -1);
            return true;
        }

        private static void PressRow(Transform row, int sign)
        {
            foreach (Component s in row.GetComponentsInChildren<MonoBehaviour>(false))
            {
                int adjustment;
                switch (s)
                {
                    case UICharacterCreationAttributeSetter a: adjustment = a.Adjustment; break;
                    case UICharacterCreationSkillSetter k: adjustment = k.Adjustment; break;
                    case UICharacterCreationAppearanceSetter ap: adjustment = ap.Adjustment; break;
                    default: continue;
                }
                if (System.Math.Sign(adjustment) == sign && s.transform.parent == row)
                {
                    s.gameObject.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                    return;
                }
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
        }

        /// <summary>The tabs above the list (spell levels, talent categories) that are showing, left to right.</summary>
        private static List<Component> Tabs(UICharacterCreationManager cc)
        {
            var tabs = new List<Component>();
            foreach (UICharacterCreationAbilityLevelButton b in cc.GetComponentsInChildren<UICharacterCreationAbilityLevelButton>(false))
            {
                Collider col = b.GetComponent<Collider>();
                if (col != null && col.enabled)
                {
                    tabs.Add(b);
                }
            }
            foreach (UICharacterCreationTalentCategoryButton b in cc.GetComponentsInChildren<UICharacterCreationTalentCategoryButton>(false))
            {
                Collider col = b.GetComponent<Collider>();
                if (col != null && col.enabled)
                {
                    tabs.Add(b);
                }
            }
            tabs.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            return tabs;
        }

        private static int CurrentTab(List<Component> tabs)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (ClickTargets.IsSelected(tabs[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>LT / RT: the previous / next tab (its own click). False = no tabs on this page.</summary>
        private static bool StepTab(UICharacterCreationManager cc, int step)
        {
            List<Component> tabs = Tabs(cc);
            if (tabs.Count < 2)
            {
                return false;
            }
            int now = CurrentTab(tabs);
            int next = Mathf.Clamp((now < 0 ? 0 : now) + step, 0, tabs.Count - 1);
            if (next == now)
            {
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                return true;
            }
            tabs[next].gameObject.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            MenuScheme.PageChanged();
            return true;
        }

        /// <summary>
        /// The cursor moved onto an option: a one-choice option is chosen (the console's way); a spell / ability /
        /// talent only shows its description in the panel (the game shows the last clicked one; choosing is A).
        /// </summary>
        private static void OnNewFocus()
        {
            UICharacterCreationManager cc = UICharacterCreationManager.Instance;
            // Where the cursor was just put: Unity reports the new mouse position only next frame.
            Collider c = FocusNavigator.ColliderAt(MouseEmulator.LastSetPosition);
            UICharacterCreationEnumSetter option = c != null ? c.GetComponent<UICharacterCreationEnumSetter>() : null;
            if (cc == null || option == null)
            {
                return;
            }
            switch (option.SetType)
            {
                case UICharacterCreationEnumSetter.EnumType.ABILITY:
                case UICharacterCreationEnumSetter.EnumType.ABILITY_MASTERY:
                case UICharacterCreationEnumSetter.EnumType.TALENTS:
                    Preview(cc, option);
                    break;
                default:
                    if (!ClickTargets.IsSelected(c))
                    {
                        option.gameObject.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                    }
                    break;
            }
        }

        private static readonly System.Reflection.MethodInfo s_Split = HarmonyLib.AccessTools.Method(typeof(UICharacterCreationStringGetter), "SplitTextBlock");

        /// <summary>The focused spell / ability / talent's name and description in the description panel, not chosen.</summary>
        private static void Preview(UICharacterCreationManager cc, UICharacterCreationEnumSetter option)
        {
            AbilityProgressionTable.UnlockableAbility ability = option.UnlockableAbility;
            if (ability == null || ability.Ability == null)
            {
                return;
            }
            UICharacterCreationStringGetter[] getters = cc.GetComponentsInChildren<UICharacterCreationStringGetter>(false);
            if (option.SetType != UICharacterCreationEnumSetter.EnumType.TALENTS)
            {
                // Spells / abilities show "the last clicked" entry: point it at the focused one and let the game write it.
                UICharacterCreationManager.AbilitySelectionState state = option.SetType == UICharacterCreationEnumSetter.EnumType.ABILITY
                    ? cc.GetCurrentAbilitySelectionState() : cc.GetCurrentSpellMasterySelectionState();
                if (state == null)
                {
                    return;
                }
                state.LastSelectedAbility = ability;
                foreach (UICharacterCreationStringGetter g in getters)
                {
                    g.SignalValueChanged(UICharacterCreationElement.ValueType.Ability);
                }
                return;
            }
            // Talents show the last chosen one: write the focused one's text the way the game does.
            string desc = AbilityProgressionTable.GetAbilityDesc(ability.Ability, cc.TargetCharacter, ability.ActivationObject);
            foreach (UICharacterCreationStringGetter g in getters)
            {
                switch (g.DataSource)
                {
                    case UICharacterCreationStringGetter.StringDataSource.TALENT:
                        g.SetText(AbilityProgressionTable.GetAbilityName(ability.Ability));
                        break;
                    case UICharacterCreationStringGetter.StringDataSource.TALENT_DESC:
                        g.SetText(Split(g, desc, 0));
                        break;
                    case UICharacterCreationStringGetter.StringDataSource.TALENT_DESC2:
                    {
                        var text = new System.Text.StringBuilder();
                        string second = Split(g, desc, 1);
                        if (!string.IsNullOrEmpty(second))
                        {
                            text.AppendLine(second);
                            GenericTalent talent = AbilityProgressionTable.GetGenericTalent(ability.Ability);
                            for (int m = 0; talent != null && m < 6; m++)
                            {
                                int adjust = talent.GetSkillAdjustment((CharacterStats.SkillType)m);
                                if (adjust > 0)
                                {
                                    text.AppendFormat("{0}: +{1}\n", GUIUtils.GetSkillTypeString((CharacterStats.SkillType)m), adjust);
                                }
                            }
                        }
                        g.SetText(text.ToString());
                        break;
                    }
                }
            }
        }

        private static string Split(UICharacterCreationStringGetter g, string block, int index) =>
            s_Split != null ? s_Split.Invoke(g, new object[] { block, index }) as string : block;

        /// <summary>
        /// A page with a list of choices (spells, talents, abilities, or the single choices) starts on its first
        /// option when nothing is chosen yet, not on whatever happens to be nearest (a spell-level tab, the list's edge).
        /// </summary>
        protected override bool PreferAsStart(Collider c)
        {
            if (!On)
            {
                return false;
            }
            UICharacterCreationNavControl done = LastPageDone();
            if (done != null)
            {
                return c.transform.IsChildOf(done.transform);
            }
            return c.GetComponent<UICharacterCreationEnumSetter>() != null;
        }

        /// <summary>The last page (Done can finish, no Next): Done is a d-pad stop there and the start.</summary>
        protected override bool KeepStop(Collider c)
        {
            UICharacterCreationNavControl done = On ? LastPageDone() : null;
            return done != null && c.transform.IsChildOf(done.transform);
        }

        /// <summary>Done, when this is the last page: Done can be pressed and there is no Next.</summary>
        private static UICharacterCreationNavControl LastPageDone()
        {
            UICharacterCreationManager cc = UICharacterCreationManager.Instance;
            if (cc == null || FindNav(cc, UICharacterCreationNavControl.NavType.ACCEPT) != null)
            {
                return null;
            }
            return FindNav(cc, UICharacterCreationNavControl.NavType.DONE);
        }

        /// <summary>The glyph of each page button on the button itself, like the console's L / R at the tab bar.</summary>
        private static void AddNavGlyphs(UICharacterCreationManager cc)
        {
            Glyph(FindNav(cc, UICharacterCreationNavControl.NavType.CANCEL), "lb");
            Glyph(FindNav(cc, UICharacterCreationNavControl.NavType.ACCEPT), "rb");
            Glyph(FindNav(cc, UICharacterCreationNavControl.NavType.DONE), "start");
            Glyph(FindNav(cc, UICharacterCreationNavControl.NavType.EXIT), "east");
            List<Component> tabs = Tabs(cc);
            int now = CurrentTab(tabs);
            if (tabs.Count >= 2 && now >= 0)
            {
                if (now > 0)
                {
                    UI.ButtonHints.Add(tabs[now - 1].gameObject, "lt");
                }
                if (now < tabs.Count - 1)
                {
                    UI.ButtonHints.Add(tabs[now + 1].gameObject, "rt");
                }
            }
        }

        private static void Glyph(UICharacterCreationNavControl nav, string glyph)
        {
            if (nav != null)
            {
                UI.ButtonHints.Add(nav.SpriteButton != null ? nav.SpriteButton.gameObject : nav.gameObject, glyph);
            }
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
                if (m_Palette != null && m_Palette.gameObject.activeInHierarchy)
                {
                    AddNavigatePrompt(into);
                    into.Add(new Prompt("south", "Choose colour"));
                    into.Add(new Prompt("east", "Back"));
                    return;
                }
                Compat.Features.Run(Compat.Features.ButtonGlyphs, () => AddNavGlyphs(cc));
                AddNavigatePrompt(into);
                into.Add(new Prompt("south", "Select"));
                if (CharacterShown(cc))
                {
                    into.Add(new Prompt("rstick_lr", "Turn"));
                }
                if (FocusNavigator.RowAt(MouseEmulator.Position) != null)
                {
                    into.Add(new Prompt("dpad_lr", "Value"));
                }
                List<Component> tabs = Tabs(cc);
                if (tabs.Count >= 2)
                {
                    bool spells = tabs[0] is UICharacterCreationAbilityLevelButton;
                    into.Add(new Prompt(new[] { "lt", "rt" }, spells ? "Spell level" : "Category"));
                }
                if (cc.CanGoBack())
                {
                    into.Add(new Prompt("lb", "Previous"));
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
