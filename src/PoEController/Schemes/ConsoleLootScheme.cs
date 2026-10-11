using System.Collections.Generic;
using System.Linq;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Loot window in Console style (after the console version), on top of the cursor menu: A takes the
    /// item under the cursor (a click on a loot item sends it to the receiver — the game's own ClickSendsTo), X takes
    /// everything (TAKE_ALL), Y examines (right-click), LB / RB pick who receives (UILootManager.SelectPartyMember),
    /// R3 sends to the stash instead (SelectStash) and back, B closes.
    /// </summary>
    public sealed class ConsoleLootScheme : MenuScheme, IItemTooltipHint
    {
        public ConsoleLootScheme(params Binding[] bindings)
            : base("Loot (Console)", bindings)
        {
        }

        private static bool On => Compat.Features.On(Compat.Features.ConsoleMenus);

        protected override bool RightClickOnX => !On;

        public string ItemTooltipHint => On ? "{north} for details." : null;

        /// <summary>The first focus lands on the container's first item (top-left), not the Take All button.</summary>
        protected override Vector2 StartNear =>
            Compat.Features.Get(Compat.Features.ConsoleMenus, FirstLootItem, (Vector2?)null) ?? base.StartNear;

        /// <summary>Screen point (Unity coordinates) of the loot grid's top-left item, or null when it shows none.</summary>
        private static Vector2? FirstLootItem()
        {
            UILootManager loot = UILootManager.Instance;
            Camera ui = InGameUILayout.NGUICamera;
            if (loot == null || ui == null || loot.ItemGrid == null)
            {
                return null;
            }
            Vector2? best = null;
            foreach (UIInventoryGridItem slot in loot.ItemGrid.GetComponentsInChildren<UIInventoryGridItem>(false))
            {
                if (slot.Empty || slot.InvItem == null)
                {
                    continue;
                }
                Vector3 p = ui.WorldToScreenPoint(slot.transform.position);
                // Top row first (larger y is higher on screen), then leftmost; rows within a few pixels count as one.
                if (best == null || p.y > best.Value.y + 4f || (Mathf.Abs(p.y - best.Value.y) <= 4f && p.x < best.Value.x))
                {
                    best = new Vector2(p.x, p.y);
                }
            }
            return best;
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            if (!On)
            {
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => HandleConsole(pad));
        }

        private void HandleConsole(Gamepad pad)
        {
            UILootManager loot = UILootManager.Instance;
            if (loot == null || !loot.WindowActive())
            {
                return;
            }
            // Y = one right-click: the item card (or a grimoire's spells), as the game's right-click. MenuScheme lets
            // the button go again next frame.
            if (pad.GetButtonDown(PadButton.North) && HoveredItem(loot) != null)
            {
                MouseEmulator.SetRight(true);
            }
            if (pad.GetButtonDown(PadButton.LeftShoulder))
            {
                StepReceiver(loot, -1);
            }
            if (pad.GetButtonDown(PadButton.RightShoulder))
            {
                StepReceiver(loot, 1);
            }
            if (pad.GetButtonDown(PadButton.RightStick))
            {
                if (loot.SelectedStash)
                {
                    loot.SelectPartyMember(Party().FirstOrDefault(m => m.GetComponent<CharacterStats>() == loot.SelectedCharacter) ?? Party().FirstOrDefault());
                }
                else
                {
                    loot.SelectStash();
                }
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            }
        }

        private static List<PartyMemberAI> Party() =>
            PartyMemberAI.OnlyPrimaryPartyMembers.Where(m => m != null).OrderBy(m => m.Slot).ToList();

        private static void StepReceiver(UILootManager loot, int dir)
        {
            List<PartyMemberAI> party = Party();
            if (party.Count == 0)
            {
                return;
            }
            int i = party.FindIndex(m => m.GetComponent<CharacterStats>() == loot.SelectedCharacter);
            // From the stash (or nobody), LB/RB go back to the last receiver, else the first / last member.
            int next = loot.SelectedStash || i < 0 ? (i >= 0 ? i : (dir > 0 ? 0 : party.Count - 1)) : ((i + dir) % party.Count + party.Count) % party.Count;
            loot.SelectPartyMember(party[next]);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        /// <summary>The item slot under the cursor inside the loot window (either grid), or null.</summary>
        private static UIInventoryGridItem HoveredItem(UILootManager loot)
        {
            Collider c = FocusNavigator.ColliderAt(MouseEmulator.Position);
            UIInventoryGridItem item = c != null ? c.GetComponentInParent<UIInventoryGridItem>() : null;
            return item != null && !item.Empty && item.InvItem != null && item.transform.IsChildOf(loot.transform) ? item : null;
        }

        protected override void AddNavigationPrompts(List<Prompt> into)
        {
            UILootManager loot = UILootManager.Instance;
            if (!On || loot == null)
            {
                base.AddNavigationPrompts(into);
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () =>
            {
                AddNavigatePrompt(into);
                UIInventoryGridItem item = HoveredItem(loot);
                bool fromLoot = item != null && (object)item.Owner == loot.ItemGrid;
                into.Add(new Prompt("south", fromLoot ? "Take" : "Select"));
                if (item != null)
                {
                    into.Add(new Prompt("north", "Examine"));
                }
                string receiver = loot.SelectedStash ? "Stash" : (loot.SelectedCharacter != null ? loot.SelectedCharacter.Name() : "?");
                into.Add(new Prompt(new[] { "lb", "rb" }, "To: " + receiver));
                into.Add(new Prompt("rstick_click", loot.SelectedStash ? "To party" : "To stash"));
            });
        }
    }
}
