using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// The rest box in Console style (camp, inn, player house: <see cref="UIRestConfirmBox"/>), in two levels.
    /// <list type="bullet">
    /// <item>Main: the party's bonus section is one stop beside Rest / Stash / Cancel. Hold X = Rest (the box's own Rest
    /// button), Y = Stash (when the box has it), B = Cancel, A on the section or LB / RB = edit bonuses.</item>
    /// <item>Edit bonuses: one character at a time (LB / RB or d-pad left / right; the others are darkened). A opens their
    /// bonus list, the d-pad picks, A chooses (the game applies it at once); the game's own tooltip shows what a bonus
    /// does. B = back to the main level. Hold X = Rest works here too.</item>
    /// </list>
    /// A rest without a bonus section (inn, player house): LB / RB say there are no bonuses.
    /// </summary>
    public sealed class ConsoleRestScheme : MenuScheme
    {
        private static readonly MethodInfo s_OnRest = AccessTools.Method(typeof(UIRestConfirmBox), "OnRest");
        private static readonly MethodInfo s_OnStash = AccessTools.Method(typeof(UIRestConfirmBox), "OnStash");
        private static readonly MethodInfo s_OnCancel = AccessTools.Method(typeof(UIRestConfirmBox), "OnCloseWindow");
        private static readonly Color Darken = new Color(0f, 0f, 0f, 0.62f);

        private readonly HoldButton m_X = new HoldButton(PadButton.West);
        private readonly List<UIRestBoxPartyMember> m_Rows = new List<UIRestBoxPartyMember>();
        private bool m_Edit;
        private int m_Index;

        public ConsoleRestScheme()
            : base("Rest (Console)")
        {
        }

        private static bool On => Compat.Features.On(Compat.Features.ConsoleMenus);

        private static UIRestConfirmBox Box => UIRestConfirmBox.Instance;

        protected override bool RightClickOnX => !On;

        /// <summary>A on the bonus section (main level) enters edit mode instead of clicking.</summary>
        protected override bool ClickOnA => !(On && !m_Edit && CursorOnSection());

        public override void Activate()
        {
            base.Activate();
            m_X.Reset();
            m_Edit = false;
            m_Index = 0;
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            UIRestConfirmBox box = Box;
            if (!On || box == null || !box.WindowActive())
            {
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => HandleBox(box, pad, dt));
        }

        private void HandleBox(UIRestConfirmBox box, Gamepad pad, float dt)
        {
            RefreshRows(box);
            if (m_Edit && m_Rows.Count == 0)
            {
                m_Edit = false;
            }

            HoldButton.Result x = m_X.Update(pad, dt, box.RestButton != null && box.RestButton.gameObject.activeInHierarchy);
            if (x == HoldButton.Result.Held)
            {
                Plugin.Log.LogInfo("Rest: rest (hold X)");
                s_OnRest.Invoke(box, new object[] { box.gameObject });
                return;
            }
            if (x == HoldButton.Result.Tapped)
            {
                UI.Toast.Show("Hold to rest");
            }

            if (pad.GetButtonDown(PadButton.North) && HasStash(box))
            {
                Plugin.Log.LogInfo("Rest: stash (Y)");
                s_OnStash.Invoke(box, new object[] { box.gameObject });
                return;
            }

            bool lb = pad.GetButtonDown(PadButton.LeftShoulder);
            bool rb = pad.GetButtonDown(PadButton.RightShoulder);
            if (!m_Edit)
            {
                if (pad.GetButtonDown(PadButton.East))
                {
                    s_OnCancel.Invoke(box, new object[] { box.gameObject });
                    return;
                }
                if (lb || rb || (pad.GetButtonDown(PadButton.South) && CursorOnSection()))
                {
                    EnterEdit(lb && !rb ? m_Rows.Count - 1 : 0, pad);
                }
                return;
            }

            // Edit mode. B: an open list is closed by the menu itself first (B is then held back); else back to the main level.
            if (pad.GetButtonDown(PadButton.East))
            {
                m_Edit = false;
                pad.SuppressHeldButtons();
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                Plugin.Log.LogInfo("Rest: bonuses done (B)");
                return;
            }
            if (lb || rb)
            {
                Step(rb ? 1 : -1);
            }
        }

        /// <summary>Edit mode on, on row <paramref name="index"/>; no bonus section here = the "no bonuses" message.</summary>
        private void EnterEdit(int index, Gamepad pad)
        {
            if (m_Rows.Count == 0)
            {
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                UI.Toast.Show("No bonuses available for this rest");
                return;
            }
            m_Edit = true;
            m_Index = Mathf.Clamp(index, 0, m_Rows.Count - 1);
            pad.SuppressHeldButtons();
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Rest: edit bonuses ({m_Rows.Count} character(s))");
            FocusRow();
        }

        /// <summary>The previous / next character (wraps); an open bonus list is closed first.</summary>
        private void Step(int dir)
        {
            if (m_Rows.Count == 0)
            {
                return;
            }
            OpenDropdowns.CloseOne();
            m_Index = (m_Index + dir + m_Rows.Count) % m_Rows.Count;
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            FocusRow();
        }

        /// <summary>The cursor onto the current character's bonus box.</summary>
        private void FocusRow()
        {
            UIRestBoxPartyMember row = m_Index < m_Rows.Count ? m_Rows[m_Index] : null;
            Collider c = row != null && row.DropdownTriggerButton != null ? row.DropdownTriggerButton.GetComponent<Collider>() : null;
            if (c == null && row != null)
            {
                c = row.GetComponentInChildren<Collider>();
            }
            Camera cam = c != null ? NGUITools.FindCameraForLayer(c.gameObject.layer) : null;
            if (cam != null)
            {
                Vector3 p = cam.WorldToScreenPoint(c.bounds.center);
                MouseEmulator.MoveTo(new Vector2(p.x, p.y));
            }
        }

        /// <summary>D-pad / LS left / right switch character in edit mode.</summary>
        protected override bool ConsumeDirection(Vector2 dir)
        {
            if (!On || !m_Edit || Mathf.Abs(dir.x) < 0.5f || OpenDropdowns.Find() != null)
            {
                return false;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => Step(dir.x > 0f ? 1 : -1));
            return true;
        }

        /// <summary>Edit mode: only the current character's row (and its open list) are stops.</summary>
        protected override bool SkipStop(Collider c)
        {
            if (!On || !m_Edit || m_Index >= m_Rows.Count || m_Rows[m_Index] == null)
            {
                return false;
            }
            return !c.transform.IsChildOf(m_Rows[m_Index].transform);
        }

        /// <summary>Main level: the whole bonus section is one stop.</summary>
        protected override Transform GroupAs(Collider c)
        {
            UIRestConfirmBox box = Box;
            if (!On || m_Edit || box == null || box.PartyMemberGrid == null || !box.PartyMemberGrid.gameObject.activeInHierarchy)
            {
                return null;
            }
            return c.transform.IsChildOf(box.PartyMemberGrid.transform) ? box.PartyMemberGrid.transform : null;
        }

        /// <summary>The box opens with the cursor on the bonus section (camp); elsewhere the nearest button.</summary>
        protected override bool PreferAsStart(Collider c)
        {
            UIRestConfirmBox box = Box;
            return On && box != null && box.PartyMemberGrid != null && box.PartyMemberGrid.gameObject.activeInHierarchy
                && c.transform.IsChildOf(box.PartyMemberGrid.transform);
        }

        private bool CursorOnSection()
        {
            UIRestConfirmBox box = Box;
            if (box == null || box.PartyMemberGrid == null || !box.PartyMemberGrid.gameObject.activeInHierarchy)
            {
                return false;
            }
            Collider c = FocusNavigator.ColliderAt(MouseEmulator.Position);
            return c != null && c.transform.IsChildOf(box.PartyMemberGrid.transform);
        }

        private static bool HasStash(UIRestConfirmBox box) => box.StashButton != null && box.StashButton.gameObject.activeInHierarchy;

        /// <summary>The characters who can pick a bonus here, left to right (none without a bonus section).</summary>
        private void RefreshRows(UIRestConfirmBox box)
        {
            m_Rows.Clear();
            if (box.CampingOnlyContent == null || !box.CampingOnlyContent.activeInHierarchy || box.PartyMemberGrid == null)
            {
                return;
            }
            m_Rows.AddRange(box.PartyMemberGrid.GetComponentsInChildren<UIRestBoxPartyMember>(false)
                .Where(r => r != null && r.SelectedCharacter != null && r.SelectedCharacter.CalculateSkill(CharacterStats.SkillType.Survival) > 0)
                .OrderBy(r => r.transform.position.x).ThenByDescending(r => r.transform.position.y));
            if (m_Index >= m_Rows.Count)
            {
                m_Index = Mathf.Max(0, m_Rows.Count - 1);
            }
        }

        /// <summary>Edit mode: the rows of the characters not being edited, darkened (drawn under the prompt bar).</summary>
        public void DrawDimmed()
        {
            if (!On || !m_Edit || Event.current.type != EventType.Repaint || Box == null || Box.PartyMemberGrid == null)
            {
                return;
            }
            UIRestBoxPartyMember current = m_Index < m_Rows.Count ? m_Rows[m_Index] : null;
            Color old = GUI.color;
            GUI.color = Darken;
            foreach (UIRestBoxPartyMember row in Box.PartyMemberGrid.GetComponentsInChildren<UIRestBoxPartyMember>(false))
            {
                if (row != current && UI.WorldTextAvoid.TryScreenRect(row.transform, out Rect r))
                {
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                }
            }
            GUI.color = old;
        }

        protected override void AddNavigationPrompts(List<Prompt> into)
        {
            UIRestConfirmBox box = Box;
            if (!On || box == null)
            {
                base.AddNavigationPrompts(into);
                return;
            }
            AddNavigatePrompt(into);
            if (m_Edit)
            {
                into.Add(new Prompt(new[] { "lb", "rb" }, "Character"));
                into.Add(new Prompt("south", "Choose bonus"));
            }
            else
            {
                into.Add(new Prompt("south", "Select"));
                into.Add(new Prompt(new[] { "lb", "rb" }, "Edit bonuses"));
            }
            into.Add(new Prompt("west", "Rest (hold)", m_X.Progress));
            if (HasStash(box))
            {
                into.Add(new Prompt("north", "Stash"));
            }
            into.Add(new Prompt("east", m_Edit ? "Done" : "Cancel"));
        }
    }
}
