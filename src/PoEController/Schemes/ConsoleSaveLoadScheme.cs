using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Save / load in Console style, on top of the cursor menu: A on a save row loads it (or saves over it / into the new
    /// slot), as double-clicking the row does; hold X deletes the save under the cursor through its own Delete (the
    /// game's "are you sure?" still comes); Y opens / closes the playthrough the cursor is in (as clicking its header).
    /// The row's buttons and name field stay reachable with the cursor.
    /// </summary>
    public sealed class ConsoleSaveLoadScheme : MenuScheme
    {
        private static readonly MethodInfo s_Use = AccessTools.Method(typeof(UISaveLoadSave), "OnButtonClicked");
        private static readonly MethodInfo s_Delete = AccessTools.Method(typeof(UISaveLoadSave), "OnDeleteClicked");

        private readonly HoldButton m_X = new HoldButton(PadButton.West);

        public ConsoleSaveLoadScheme(params Binding[] bindings)
            : base("Save / load (Console)", bindings)
        {
        }

        private static bool On => Compat.Features.On(Compat.Features.ConsoleMenus);

        protected override bool RightClickOnX => !On;

        /// <summary>A on a save row itself (not its buttons or name field) is handled here: load / save.</summary>
        protected override bool ClickOnA => !(On && RowBody() != null);

        public override void Activate()
        {
            base.Activate();
            m_X.Reset();
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            if (!On || UISaveLoadManager.Instance == null || !UISaveLoadManager.Instance.WindowActive())
            {
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => HandleList(pad, dt));
        }

        private void HandleList(Gamepad pad, float dt)
        {
            UISaveLoadSave row = RowAt();
            UISaveLoadSave body = RowBody();
            if (pad.GetButtonDown(PadButton.South) && body != null)
            {
                Plugin.Log.LogInfo("Save / load: A on a save row");
                s_Use.Invoke(body, new object[] { body.gameObject });
            }

            bool deletable = row != null && !row.IsNew && row.MetaData != null;
            HoldButton.Result x = m_X.Update(pad, dt, deletable);
            if (x == HoldButton.Result.Held)
            {
                Plugin.Log.LogInfo("Save / load: delete (hold X)");
                s_Delete.Invoke(row, new object[] { row.gameObject });
            }
            else if (x == HoldButton.Result.Tapped && deletable)
            {
                UI.Toast.Show("Hold to delete");
            }

            if (pad.GetButtonDown(PadButton.North))
            {
                UISaveLoadPlaythroughGroup group = GroupAt();
                if (group != null)
                {
                    bool closing = !group.Collapsed;
                    group.Collapsed = !group.Collapsed;
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                    if (closing && row != null && group.ExpandHitbox != null)
                    {
                        // Its saves are hidden now: the cursor onto the group's header.
                        MoveOnto(group.ExpandHitbox.GetComponent<Collider>());
                    }
                }
                else
                {
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                }
            }
        }

        private static Collider Focus() => FocusNavigator.ColliderAt(MouseEmulator.Position);

        /// <summary>The save row the cursor is anywhere in (its body, buttons or name field).</summary>
        private static UISaveLoadSave RowAt()
        {
            Collider c = Focus();
            return c != null ? c.GetComponentInParent<UISaveLoadSave>() : null;
        }

        /// <summary>The save row whose body (not a button or the name field) the cursor is on.</summary>
        private static UISaveLoadSave RowBody()
        {
            Collider c = Focus();
            UISaveLoadSave row = c != null ? c.GetComponentInParent<UISaveLoadSave>() : null;
            return row != null && row.Collider != null && c.gameObject == row.Collider ? row : null;
        }

        /// <summary>The playthrough group the cursor is in: on its header or on one of its saves.</summary>
        private static UISaveLoadPlaythroughGroup GroupAt()
        {
            Collider c = Focus();
            return c != null ? c.GetComponentInParent<UISaveLoadPlaythroughGroup>() : null;
        }

        private static void MoveOnto(Collider c)
        {
            Camera cam = c != null ? NGUITools.FindCameraForLayer(c.gameObject.layer) : null;
            if (cam != null)
            {
                Vector3 p = cam.WorldToScreenPoint(c.bounds.center);
                MouseEmulator.MoveTo(new Vector2(p.x, p.y));
            }
        }

        protected override void AddNavigationPrompts(List<Prompt> into)
        {
            if (!On || UISaveLoadManager.Instance == null)
            {
                base.AddNavigationPrompts(into);
                return;
            }
            AddNavigatePrompt(into);
            UISaveLoadSave body = Compat.Features.Get(Compat.Features.ConsoleMenus, RowBody, null);
            UISaveLoadSave row = Compat.Features.Get(Compat.Features.ConsoleMenus, RowAt, null);
            if (body != null)
            {
                into.Add(new Prompt("south", UISaveLoadManager.Instance.SaveMode ? "Save" : "Load"));
            }
            else
            {
                into.Add(new Prompt("south", "Select"));
            }
            if (row != null && !row.IsNew && row.MetaData != null)
            {
                into.Add(new Prompt("west", "Delete (hold)", m_X.Progress));
            }
            if (Compat.Features.Get(Compat.Features.ConsoleMenus, GroupAt, null) != null)
            {
                into.Add(new Prompt("north", "Show / hide saves"));
            }
            into.Add(new Prompt("rstick_ud", "Scroll"));
        }
    }
}
