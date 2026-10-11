using System.Collections.Generic;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// "Equip on …" / "Send to …" / "Send to stash" for one inventory slot, so the controller never has to drag.
    /// Every action is the call the game's own UI makes: equip = what double-click does (<c>Activate</c>: the
    /// equipment slot's <c>TrySwap</c>), send = what unequipping does (an empty slot in that member's row,
    /// <c>TrySwap</c>), stash = what shift-click does. The game's validity checks and error messages still apply.
    /// </summary>
    internal static class ItemActions
    {
        public enum Kind
        {
            Equip,
            SendTo,
            Stash,
            /// <summary>Open the item card (what right-click does). Only Console style's Actions menu adds it.</summary>
            Examine,
            /// <summary>Use a consumable on the selected character (drop on the paper doll). Console only.</summary>
            Use,
            /// <summary>Put a consumable into the selected character's quick slots (double-click). Console only.</summary>
            QuickSlot,
            /// <summary>Split a stack: the game's own amount dialog, then the part is carried (double-click). Console only.</summary>
            Split,
        }

        public sealed class Entry
        {
            public Kind Kind;
            public PartyMemberAI Member;
            public string Label;
            /// <summary>Null when usable; otherwise a short reason shown dimmed next to the label.</summary>
            public string Disabled;
        }

        /// <summary>The slot under the cursor, if it holds an item in the open inventory window.</summary>
        public static UIInventoryGridItem ItemAt(Vector2 screenPos)
        {
            UIInventoryManager inv = UIInventoryManager.Instance;
            if (inv == null || !inv.WindowActive() || UIGlobalInventory.Instance == null || UIGlobalInventory.Instance.DraggingItem)
            {
                return null;
            }
            Collider c = FocusNavigator.ColliderAt(screenPos);
            UIInventoryGridItem item = c != null ? c.GetComponentInParent<UIInventoryGridItem>() : null;
            if (item == null || item.Empty || item.InvItem.baseItem == null || item.Owner == null || item.Owner.IsStore || item.Locked)
            {
                return null;
            }
            return InWindows(item.transform) ? item : null;
        }

        public static void Build(UIInventoryGridItem source, List<Entry> into)
        {
            into.Clear();
            UIInventoryManager inv = UIInventoryManager.Instance;
            // The slot may have been emptied this frame (item just equipped / moved): nothing to offer.
            if (source == null || source.Empty || source.InvItem == null || source.InvItem.baseItem == null || inv == null)
            {
                return;
            }
            CharacterStats selected = inv.SelectedCharacter;
            PartyMemberAI selectedMember = selected != null ? selected.GetComponent<PartyMemberAI>() : null;
            bool equipped = source.EquipmentSlot != Equippable.EquipmentSlot.None;
            bool quick = source.Owner == inv.QuickItemsGrid;
            StashInventory stash = Stash();
            bool inStash = stash != null && source.Owner is UIInventoryItemGrid g && g.Inventory == stash;
            // Party row items belong to that member; equipped and quick items to the selected one.
            // (The stash grid's owner object is the player character, but its items are nobody's pack.)
            PartyMemberAI owner = equipped || quick ? selectedMember : inStash ? null : OwnerOf(source);

            string blocked = null;
            if (!UIInventoryGridItem.ItemTakeValid(source, out string takeError))
            {
                blocked = string.IsNullOrEmpty(takeError) ? "Can't move this now" : takeError;
            }

            // Equip: the equipment panel only shows the selected member, so the item has to come from somewhere that
            // stays put when we switch member (a party row or the stash).
            Equippable equippable = source.InvItem.baseItem as Equippable;
            if (equippable != null && !equipped && !quick && equippable.GetPreferredSlot() != Equippable.EquipmentSlot.None)
            {
                foreach (PartyMemberAI m in OrderedParty(selectedMember))
                {
                    into.Add(new Entry
                    {
                        Kind = Kind.Equip,
                        Member = m,
                        Label = "Equip on " + CharacterStats.Name(m),
                        Disabled = blocked ?? WhyNotEquip(equippable, m),
                    });
                }
            }

            foreach (PartyMemberAI m in OrderedParty(selectedMember))
            {
                if (m == owner && !equipped && !quick)
                {
                    continue; // already in this member's pack
                }
                string label = m == owner && equipped ? "Unequip"
                    : m == owner && quick ? "Move to " + CharacterStats.Name(m) + "'s pack"
                    : "Send to " + CharacterStats.Name(m);
                UIInventoryPlayerRow row = inv.RowPanel != null ? inv.RowPanel.RowForPartyMember(m) : null;
                into.Add(new Entry
                {
                    Kind = Kind.SendTo,
                    Member = m,
                    Label = label,
                    Disabled = blocked ?? (row == null ? "Not available" : row.ItemGrid.FirstEmptyGridItem() == null ? "Pack is full" : null),
                });
            }

            if (stash != null && !inStash)
            {
                string why = blocked;
                if (why == null && !UIInventoryGridItem.ItemPlayerTakeValid(source.InvItem, source, out string stashError))
                {
                    why = string.IsNullOrEmpty(stashError) ? "Can't stash this now" : stashError;
                }
                into.Add(new Entry { Kind = Kind.Stash, Label = "Send to stash", Disabled = why });
            }
        }

        /// <summary>
        /// Runs <paramref name="e"/>. Returns false if it must wait a frame (equip on another member: the window is
        /// switched to them first so the equipment panel shows their slots); call again next frame.
        /// </summary>
        public static bool Run(Entry e, UIInventoryGridItem source)
        {
            UIInventoryManager inv = UIInventoryManager.Instance;
            switch (e.Kind)
            {
                case Kind.SendTo:
                {
                    UIInventoryPlayerRow row = inv.RowPanel.RowForPartyMember(e.Member);
                    UIInventoryGridItem target = row != null ? row.ItemGrid.FirstEmptyGridItem() : null;
                    if (target != null)
                    {
                        target.TrySwap(source, splitStack: false);
                    }
                    return true;
                }
                case Kind.Equip:
                {
                    CharacterStats stats = e.Member.GetComponent<CharacterStats>();
                    if (inv.SelectedCharacter != stats)
                    {
                        inv.SelectPartyMember(e.Member);
                        return false;
                    }
                    Equipment eq = e.Member.GetComponent<Equipment>();
                    if (eq == null || inv.Equipment.SelectedEquipment != eq)
                    {
                        return false; // the panel hasn't caught up yet
                    }
                    Equippable equippable = source.InvItem.baseItem as Equippable;
                    UIInventoryGridItem slot = equippable != null ? inv.Equipment.GetGridItemForSlot(equippable.GetPreferredSlot()) : null;
                    if (slot != null && slot.gameObject.activeSelf && ClearForEquip(slot, equippable, source, e.Member))
                    {
                        slot.TrySwap(source, splitStack: false);
                    }
                    return true;
                }
                case Kind.Examine:
                {
                    // What a right-click does (UIInventoryGridItem.OnRightClick): a grimoire opens its spell book,
                    // anything else its item card (UIInventoryGridItem.Inspect).
                    Grimoire grimoire = source.InvItem.baseItem.GetComponent<Grimoire>();
                    if (grimoire != null)
                    {
                        OpenGrimoire(grimoire, source);
                    }
                    else
                    {
                        UIItemInspectManager.Examine(source.InvItem.BaseItem, source.Owner.OwnerGameObject);
                    }
                    return true;
                }
                case Kind.Use:
                {
                    // UIInventoryManager.OnDropPaperdoll, for an item that isn't being carried (its EndUse takes the
                    // used one out of whichever inventory stores it).
                    Consumable consumable = source.InvItem.baseItem as Consumable;
                    CharacterStats selected = inv.SelectedCharacter;
                    if (consumable == null || selected == null || GameState.InCombat || consumable.GetComponent<AttackBase>() != null)
                    {
                        return true;
                    }
                    if (consumable.CanUse(selected))
                    {
                        Item baseItem = consumable;
                        consumable.UseImmediately(selected.gameObject);
                        GlobalAudioPlayer.Instance.Play(baseItem, GlobalAudioPlayer.UIInventoryAction.UseItem);
                    }
                    else
                    {
                        UIGlobalInventory.Instance.PostMessage(consumable.WhyCannotUse(selected), source.Icon);
                    }
                    return true;
                }
                case Kind.QuickSlot:
                {
                    // UIInventoryGridItem.Activate's consumable branch.
                    if (UIInventoryGridItem.ItemTransferValid(source.InvItem, source, inv.QuickItemsGrid, out string err))
                    {
                        int left = inv.QuickItemsGrid.Add(source.InvItem.baseItem, source.InvItem.stackSize, null);
                        if (source.InvItem.baseItem == null || left <= 0)
                        {
                            source.RemoveItem();
                        }
                        else
                        {
                            source.InvItem.SetStackSize(left);
                        }
                    }
                    else
                    {
                        UIGlobalInventory.Instance.PostMessage(err, source.Icon);
                    }
                    return true;
                }
                case Kind.Split:
                {
                    // Double-click on a stack = the game's amount dialog, then the part is carried on the cursor.
                    source.Activate();
                    return true;
                }
                case Kind.Stash:
                {
                    // Shift-click: UIInventoryGridItem.DoClick.
                    if (UIInventoryGridItem.ItemPlayerTakeValid(source.InvItem, source, out string error))
                    {
                        StashInventory stash = Stash();
                        if (stash != null && !Contains(stash, source.InvItem))
                        {
                            InventoryItem item = source.RemoveItem();
                            if (!stash.PutItem(item))
                            {
                                source.PutItem(item);
                            }
                        }
                    }
                    else
                    {
                        UIGlobalInventory.Instance.PostMessage(error, source.Icon);
                    }
                    return true;
                }
            }
            return true;
        }

        /// <summary>
        /// Before equipping <paramref name="item"/> into <paramref name="slot"/> on <paramref name="member"/>: what is
        /// in the way (that slot, and the other hand when either weapon is two-handed) goes into the member's own pack,
        /// the way unequipping does, instead of swapping into wherever the new item came from. False (with a message)
        /// if their pack can't take it all; then nothing has moved.
        /// </summary>
        private static bool ClearForEquip(UIInventoryGridItem slot, Equippable item, UIInventoryGridItem source, PartyMemberAI member)
        {
            var inTheWay = new List<UIInventoryGridItem>(2);
            if (!slot.Empty)
            {
                inTheWay.Add(slot);
            }
            UIInventoryGridItem buddy = slot.WeaponSetBuddy;
            if (buddy != null && !buddy.Empty && (item.BothPrimaryAndSecondarySlot
                || (buddy.InvItem.baseItem is Equippable other && other.BothPrimaryAndSecondarySlot)))
            {
                inTheWay.Add(buddy);
            }
            if (inTheWay.Count == 0)
            {
                return true;
            }
            UIInventoryPlayerRow row = UIInventoryManager.Instance.RowPanel.RowForPartyMember(member);
            BaseInventory pack = row != null ? row.ItemGrid.Inventory : null;
            if (pack == null)
            {
                return false;
            }
            if (pack.GetFreeSpace() < inTheWay.Count)
            {
                // Coming from their own pack, a lone item in the way can simply trade places with it (still their pack).
                bool ownPack = source.Owner.OwnerGameObject == member.gameObject && !IsStash(source);
                if (ownPack && inTheWay.Count == 1 && inTheWay[0] == slot)
                {
                    return true;
                }
                UIGlobalInventory.Instance.PostMessage(
                    CharacterStats.Name(member) + " has a full inventory. Please make space before equipping this item.", source.Icon);
                return false;
            }
            foreach (UIInventoryGridItem g in inTheWay)
            {
                UIInventoryGridItem empty = row.ItemGrid.FirstEmptyGridItem();
                // TrySwap shows the game's own message if it refuses (e.g. an item that can't be unequipped).
                if (empty == null || !empty.TrySwap(g, splitStack: false))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Console style's Actions entries for one slot, by item category only (type / game components, never names,
        /// so items added by other mods fall into the right group): Examine (grimoire: Open grimoire) for everything;
        /// consumables: Use on the selected character (when the game would let the paper doll use it: outside combat,
        /// no target needed) and Add to quick slots; stacks: Split stack. Each runs the game's own call for it.
        /// </summary>
        public static void AddCategoryEntries(UIInventoryGridItem source, List<Entry> into)
        {
            UIInventoryManager inv = UIInventoryManager.Instance;
            if (source == null || source.Empty || source.InvItem == null || source.InvItem.baseItem == null || inv == null)
            {
                return;
            }
            Item item = source.InvItem.baseItem;
            var extra = new List<Entry>
            {
                new Entry { Kind = Kind.Examine, Label = IsGrimoire(source) ? "Open grimoire" : "Examine" },
            };
            string blocked = UIInventoryGridItem.ItemTakeValid(source, out string takeError) ? null : (string.IsNullOrEmpty(takeError) ? "Can't move this now" : takeError);
            bool quick = source.Owner == inv.QuickItemsGrid;
            if (item is Consumable consumable)
            {
                CharacterStats selected = inv.SelectedCharacter;
                // Targeted consumables (scrolls, traps, figurines) can only be used from the quick slots in the world.
                if (consumable.GetComponent<AttackBase>() == null && selected != null)
                {
                    string why = GameState.InCombat ? "Not during combat"
                        : !consumable.CanUse(selected) ? (consumable.WhyCannotUse(selected) ?? "Can't use now")
                        : null;
                    extra.Add(new Entry { Kind = Kind.Use, Label = "Use on " + selected.Name(), Disabled = string.IsNullOrEmpty(why) ? null : why });
                }
                if (!quick)
                {
                    string why = blocked;
                    if (why == null && !UIInventoryGridItem.ItemTransferValid(source.InvItem, source, inv.QuickItemsGrid, out string err))
                    {
                        why = string.IsNullOrEmpty(err) ? "Quick slots are full" : err;
                    }
                    extra.Add(new Entry { Kind = Kind.QuickSlot, Label = "Add to quick slots", Disabled = why });
                }
            }
            if (item.MaxStackSize > 1 && source.InvItem.stackSize > 1 && blocked == null)
            {
                extra.Add(new Entry { Kind = Kind.Split, Label = $"Split stack ({source.InvItem.stackSize})" });
            }
            into.InsertRange(0, extra);
        }

        /// <summary>True for a grimoire (Console's Actions entry reads "Open grimoire" instead of "Examine").</summary>
        public static bool IsGrimoire(UIInventoryGridItem g) =>
            g != null && g.InvItem != null && g.InvItem.baseItem != null && g.InvItem.baseItem.GetComponent<Grimoire>() != null;

        /// <summary>The grimoire branch of UIInventoryGridItem.OnRightClick, call for call.</summary>
        private static void OpenGrimoire(Grimoire grimoire, UIInventoryGridItem source)
        {
            PartyMemberAI holder = source.Owner.OwnerGameObject != null ? source.Owner.OwnerGameObject.GetComponent<PartyMemberAI>() : null;
            if (holder == null)
            {
                holder = UIAbilityBar.GetSelectedAIForBars();
            }
            if (holder == null)
            {
                holder = GameState.s_playerCharacter.GetComponent<PartyMemberAI>();
            }
            if (holder == null)
            {
                return;
            }
            UIWindowManager.Instance.SuspendFor(UIGrimoireManager.Instance);
            UIGrimoireManager.Instance.SelectCharacter(holder);
            UIGrimoireManager.Instance.LoadGrimoire(grimoire, !source.Owner.IsStore);
            UIGrimoireManager.Instance.ShowWindow();
        }

        private static bool IsStash(UIInventoryGridItem g)
        {
            StashInventory stash = Stash();
            return stash != null && g.Owner is UIInventoryItemGrid grid && grid.Inventory == stash;
        }

        /// <summary>The slot now holding <paramref name="item"/> in the inventory window (slots can be reloaded when the member changes).</summary>
        public static UIInventoryGridItem Find(InventoryItem item, UIInventoryGridItem hint)
        {
            if (hint != null && hint.InvItem == item)
            {
                return hint;
            }
            UIInventoryManager inv = UIInventoryManager.Instance;
            if (inv == null || item == null)
            {
                return null;
            }
            UIInventoryGridItem found = FindIn(inv.transform, item);
            UIStashManager stash = UIStashManager.Instance;
            if (found == null && stash != null && stash.WindowActive())
            {
                found = FindIn(stash.transform, item);
            }
            return found;
        }

        private static UIInventoryGridItem FindIn(Transform root, InventoryItem item)
        {
            foreach (UIInventoryGridItem g in root.GetComponentsInChildren<UIInventoryGridItem>(false))
            {
                if (g.InvItem == item)
                {
                    return g;
                }
            }
            return null;
        }

        /// <summary>Inside the inventory window, or the stash window opened over it.</summary>
        private static bool InWindows(Transform t)
        {
            if (t.IsChildOf(UIInventoryManager.Instance.transform))
            {
                return true;
            }
            UIStashManager stash = UIStashManager.Instance;
            return stash != null && stash.WindowActive() && t.IsChildOf(stash.transform);
        }

        private static IEnumerable<PartyMemberAI> OrderedParty(PartyMemberAI first)
        {
            if (first != null)
            {
                yield return first;
            }
            foreach (PartyMemberAI m in PartyMemberAI.OnlyPrimaryPartyMembers)
            {
                if (m != null && m != first)
                {
                    yield return m;
                }
            }
        }

        private static PartyMemberAI OwnerOf(UIInventoryGridItem source)
        {
            GameObject go = source.Owner.OwnerGameObject;
            return go != null ? go.GetComponent<PartyMemberAI>() : null;
        }

        private static string WhyNotEquip(Equippable e, PartyMemberAI m)
        {
            switch (e.WhyCantEquip(m.gameObject))
            {
                case Equippable.CantEquipReason.None:
                    return null;
                case Equippable.CantEquipReason.ClassMismatch:
                    return "Wrong class";
                case Equippable.CantEquipReason.SoulboundToOther:
                    return "Soulbound to someone else";
                case Equippable.CantEquipReason.EquipmentLocked:
                    return "Equipment locked";
                default:
                    return "Can't equip";
            }
        }

        private static StashInventory Stash()
        {
            return GameState.s_playerCharacter != null ? GameState.s_playerCharacter.GetComponent<StashInventory>() : null;
        }

        private static bool Contains(BaseInventory inv, InventoryItem item)
        {
            foreach (InventoryItem i in inv)
            {
                if (i == item)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
