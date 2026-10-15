using System.Collections.Generic;
using System.Linq;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Inventory (and the stash over it) in Console style (after the console version), on top of the
    /// cursor menu (d-pad snapping, focus frame, tooltips stay):
    /// <list type="bullet">
    /// <item>A tap on an item: Equip on the selected character, or Unequip (quick item: move to their pack) — the item
    /// menu's own actions; anything else: pick it up. Hold A: pick up (carry with the cursor); A again puts it down.</item>
    /// <item>X: Move to stash; on a stash item, Move to the selected character's pack.</item>
    /// <item>Y: Actions — Examine + the item menu (equip on / send to / stash).</item>
    /// <item>LB / RB: previous / next character. LT / RT: previous / next screen. R3: open / close the stash (where
    /// the game allows it). B: close.</item>
    /// </list>
    /// NGUI's pad submit stays muted while this is active, since A never means "click whatever NGUI selected last".
    /// Everything game-facing runs under feature <see cref="Compat.Features.ConsoleMenus"/>; if that turns off, the
    /// window falls back to the plain cursor (A / X = left / right click).
    /// </summary>
    public sealed class ConsoleInventoryScheme : InventoryScheme, IItemTooltipHint
    {
        private const float HoldToPickUp = 0.35f;

        private readonly List<ItemActions.Entry> m_Actions = new List<ItemActions.Entry>();
        private readonly bool m_IsStash;
        private bool m_AArmed;
        private float m_AHeld;
        private bool m_AWaitRelease;
        private bool m_ClickPulse;

        // Multi-select (L3 tap, Baldur's Gate 3 style).
        private const float L3HoldForCursor = 0.4f;
        private const float MarkRefreshInterval = 0.25f;
        private readonly List<InventoryItem> m_Marked = new List<InventoryItem>();
        private bool m_Multi;
        private bool m_L3Down;
        private float m_L3Time;
        private float m_MarkRefresh;

        public ConsoleInventoryScheme(string name, bool isStash, params Binding[] bindings)
            : base(name, isStash, bindings)
        {
            m_IsStash = isStash;
        }

        private static bool On => Compat.Features.On(Compat.Features.ConsoleMenus);

        private static bool Dragging => UIGlobalInventory.Instance != null && UIGlobalInventory.Instance.DraggingItem;

        /// <summary>The hovered slot if it still holds an item (an action earlier this frame can empty it).</summary>
        private UIInventoryGridItem Item
        {
            get
            {
                UIInventoryGridItem slot = Hovered;
                return slot != null && !slot.Empty && slot.InvItem != null && slot.InvItem.baseItem != null ? slot : null;
            }
        }

        // A is the mouse button only while carrying an item (to put it down), once the press that picked it up is over.
        protected override bool ClickOnA => !On || (Dragging && !m_AWaitRelease);

        protected override bool RightClickOnX => !On;

        protected override bool KeepSubmitMuted => On;

        protected override string MenuLabel => "Actions";

        // L3: tap = multi-select, hold = free cursor (handled here).
        protected override bool L3TogglesCursor => !On;

        public string ItemTooltipHint => On ? "{north} for actions." : null;

        private protected override void AddExtraEntries(UIInventoryGridItem source, List<ItemActions.Entry> into)
        {
            if (On)
            {
                ItemActions.AddCategoryEntries(source, into);
                // The stash window (R3 is tooltips): where the game lets the party reach it (camp, inn, town ...).
                if (!m_IsStash && StashAllowed())
                {
                    into.Add(new ItemActions.Entry { Kind = ItemActions.Kind.OpenStash, Label = "Open stash" });
                }
            }
        }

        public override void Activate()
        {
            base.Activate();
            m_AArmed = false;
            m_AWaitRelease = false;
            m_ClickPulse = false;
            m_L3Down = false;
            if (On)
            {
                SubmitGuard.Mute();
            }
        }

        public override void Deactivate()
        {
            Compat.Features.Run(Compat.Features.ConsoleMenus, ExitMulti);
            m_Multi = false;
            m_Marked.Clear();
            base.Deactivate();
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            if (Busy || !On)
            {
                base.HandleInput(ctx, pad, dt);
                return;
            }
            // The multi-select actions list takes all input while open.
            if (PopupMenu.HandleInput(pad))
            {
                return;
            }
            // In multi-select, B / Y / X are ours: handle them before the window's bindings and the item menu see them.
            if (m_Multi && Compat.Features.Get(Compat.Features.ConsoleMenus, () => MultiButtons(pad), false))
            {
                pad.SuppressHeldButtons();
                return;
            }
            base.HandleInput(ctx, pad, dt);
            if (Busy)
            {
                return; // Y just opened the Actions menu
            }
            m_Root = NavigationRoot(ctx);
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => HandleConsole(pad, dt));
        }

        /// <summary>The window the d-pad works in this frame (for tooltip mode from the L3 hold).</summary>
        private Transform m_Root;

        private void HandleConsole(Gamepad pad, float dt)
        {
            // A click pulse: down last frame, MenuScheme lets go this frame.
            if (m_ClickPulse)
            {
                m_ClickPulse = false;
            }
            if (!pad.GetButton(PadButton.South))
            {
                m_AWaitRelease = false;
            }
            HandleL3(pad, dt);
            if (m_Multi)
            {
                RefreshMarks(dt);
                if (pad.GetButtonDown(PadButton.South) && Item != null && !Dragging)
                {
                    ToggleMark(Item);
                }
                if (pad.GetButtonDown(PadButton.LeftShoulder))
                {
                    StepCharacter(-1);
                }
                if (pad.GetButtonDown(PadButton.RightShoulder))
                {
                    StepCharacter(1);
                }
                return;
            }
            HandleA(pad, dt);

            if (pad.GetButtonDown(PadButton.West) && !Dragging)
            {
                MoveAction();
            }
            if (pad.GetButtonDown(PadButton.LeftShoulder))
            {
                StepCharacter(-1);
            }
            if (pad.GetButtonDown(PadButton.RightShoulder))
            {
                StepCharacter(1);
            }
        }

        /// <summary>Tap = the item's main action (on release); hold = pick it up; carrying = plain clicks (MenuScheme).</summary>
        private void HandleA(Gamepad pad, float dt)
        {
            if (Dragging)
            {
                m_AArmed = false;
                return;
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                m_AArmed = true;
                m_AHeld = 0f;
            }
            if (!m_AArmed)
            {
                return;
            }
            if (pad.GetButton(PadButton.South))
            {
                m_AHeld += dt;
                if (m_AHeld >= HoldToPickUp && Item != null)
                {
                    m_AArmed = false;
                    m_AWaitRelease = true;
                    Click();
                    Plugin.Log.LogInfo("Inventory: pick up (hold A)");
                }
                return;
            }
            m_AArmed = false;
            UIInventoryGridItem item = Item;
            if (item == null)
            {
                Click(); // buttons, tabs, empty slots: a normal click
                return;
            }
            ItemActions.Entry main = MainAction(item);
            if (main == null)
            {
                m_AWaitRelease = true;
                Click(); // not equippable: pick it up
                return;
            }
            if (main.Disabled != null)
            {
                UIGlobalInventory.Instance.PostMessage(main.Disabled, item.Icon);
                return;
            }
            RunAction(main, item);
        }

        private void Click()
        {
            MouseEmulator.SetLeft(true);
            m_ClickPulse = true;
        }

        /// <summary>Equip on the selected character, Unequip, or Move to their pack (quick items).</summary>
        private ItemActions.Entry MainAction(UIInventoryGridItem slot)
        {
            BuildEntries(slot, m_Actions);
            PartyMemberAI selected = SelectedMember();
            bool equipped = slot.EquipmentSlot != Equippable.EquipmentSlot.None;
            bool quick = UIInventoryManager.Instance != null && slot.Owner == UIInventoryManager.Instance.QuickItemsGrid;
            ItemActions.Kind kind = equipped || quick ? ItemActions.Kind.SendTo : ItemActions.Kind.Equip;
            return m_Actions.FirstOrDefault(e => e.Kind == kind && e.Member == selected);
        }

        private static PartyMemberAI SelectedMember()
        {
            CharacterStats s = UIInventoryManager.Instance != null ? UIInventoryManager.Instance.SelectedCharacter : null;
            return s != null ? s.GetComponent<PartyMemberAI>() : null;
        }

        private static bool InStash(UIInventoryGridItem slot)
        {
            StashInventory stash = GameState.s_playerCharacter != null ? GameState.s_playerCharacter.GetComponent<StashInventory>() : null;
            return stash != null && slot.Owner is UIInventoryItemGrid g && g.Inventory == stash;
        }

        /// <summary>X: Move to stash, or (stash item) to the selected character's pack.</summary>
        private ItemActions.Entry MoveEntry(UIInventoryGridItem slot)
        {
            BuildEntries(slot, m_Actions);
            if (InStash(slot))
            {
                PartyMemberAI selected = SelectedMember();
                return m_Actions.FirstOrDefault(e => e.Kind == ItemActions.Kind.SendTo && e.Member == selected);
            }
            return m_Actions.FirstOrDefault(e => e.Kind == ItemActions.Kind.Stash);
        }

        private void MoveAction()
        {
            UIInventoryGridItem slot = Item;
            ItemActions.Entry move = slot != null ? MoveEntry(slot) : null;
            if (move == null)
            {
                return;
            }
            if (move.Disabled != null)
            {
                UIGlobalInventory.Instance.PostMessage(move.Disabled, slot.Icon);
                return;
            }
            RunAction(move, slot);
        }

        // ---- Multi-select ----

        /// <summary>L3 tap = multi-select on / off; L3 held 0.4 s = free cursor on / off.</summary>
        private void HandleL3(Gamepad pad, float dt)
        {
            if (pad.GetButtonDown(PadButton.LeftStick))
            {
                m_L3Down = true;
                m_L3Time = 0f;
            }
            if (!m_L3Down)
            {
                return;
            }
            if (pad.GetButton(PadButton.LeftStick))
            {
                m_L3Time += dt;
                if (m_L3Time >= L3HoldForCursor)
                {
                    m_L3Down = false;
                    ToggleFreeCursor();
                }
                return;
            }
            m_L3Down = false;
            if (m_Multi)
            {
                ExitMulti();
            }
            else if (!Dragging)
            {
                EnterMulti();
            }
        }

        private void EnterMulti()
        {
            m_Multi = true;
            m_Marked.Clear();
            UIGlobalInventory.Instance.UnselectAll();
            if (Item != null)
            {
                ToggleMark(Item);
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo("Inventory: multi-select on");
        }

        /// <summary>Leaves multi-select: marks cleared, the cursor stays on the item it points at.</summary>
        private void ExitMulti()
        {
            if (!m_Multi)
            {
                return;
            }
            m_Multi = false;
            m_Marked.Clear();
            if (UIGlobalInventory.Instance != null)
            {
                UIGlobalInventory.Instance.UnselectAll();
            }
            Plugin.Log.LogInfo("Inventory: multi-select off");
        }

        /// <summary>A in multi-select: mark / unmark (the game's own selection highlight).</summary>
        private void ToggleMark(UIInventoryGridItem slot)
        {
            InventoryItem item = slot.InvItem;
            if (m_Marked.Remove(item))
            {
                UIGlobalInventory.Instance.Unselect(slot);
            }
            else
            {
                m_Marked.Add(item);
                UIGlobalInventory.Instance.Select(slot, multi: true);
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        /// <summary>Slots reload (character switch, items moving): keep the highlight on the marked items, drop vanished ones.</summary>
        private void RefreshMarks(float dt)
        {
            m_MarkRefresh -= dt;
            if (m_MarkRefresh > 0f)
            {
                return;
            }
            m_MarkRefresh = MarkRefreshInterval;
            for (int i = m_Marked.Count - 1; i >= 0; i--)
            {
                UIInventoryGridItem slot = ItemActions.Find(m_Marked[i], null);
                if (slot == null || slot.InvItem != m_Marked[i])
                {
                    // Not shown right now (another character's equipment) or gone: keep it only if it still exists somewhere visible later.
                    if (m_Marked[i] == null || m_Marked[i].baseItem == null)
                    {
                        m_Marked.RemoveAt(i);
                    }
                    continue;
                }
                if (!slot.Selected)
                {
                    UIGlobalInventory.Instance.Select(slot, multi: true);
                }
            }
        }

        /// <summary>B cancels, Y opens the actions list, X sends everything marked to the stash. True = used.</summary>
        private bool MultiButtons(Gamepad pad)
        {
            if (pad.GetButtonDown(PadButton.East))
            {
                ExitMulti();
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                return true;
            }
            if (pad.GetButtonDown(PadButton.North))
            {
                if (m_Marked.Count > 0)
                {
                    OpenMultiMenu();
                }
                else
                {
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                }
                return true;
            }
            if (pad.GetButtonDown(PadButton.West) && m_Marked.Count > 0 && HasStash())
            {
                RunBatch("Sent to stash", l => l.Find(e => e.Kind == ItemActions.Kind.Stash));
                return true;
            }
            return false;
        }

        private static bool HasStash() => GameState.s_playerCharacter != null && GameState.s_playerCharacter.GetComponent<StashInventory>() != null;

        private void OpenMultiMenu()
        {
            var entries = new List<PopupMenu.Entry>();
            foreach (PartyMemberAI m in PartyMemberAI.OnlyPrimaryPartyMembers.Where(x => x != null).OrderBy(x => x.Slot))
            {
                PartyMemberAI member = m;
                entries.Add(new PopupMenu.Entry
                {
                    Label = "Send to " + CharacterStats.Name(member),
                    Run = () => RunBatch("Sent to " + CharacterStats.Name(member), l => l.Find(e => e.Kind == ItemActions.Kind.SendTo && e.Member == member)),
                });
            }
            if (HasStash())
            {
                entries.Add(new PopupMenu.Entry { Label = "Send to stash", Run = () => RunBatch("Sent to stash", l => l.Find(e => e.Kind == ItemActions.Kind.Stash)) });
            }
            if (m_Marked.Any(i => i != null && i.baseItem is Consumable))
            {
                entries.Add(new PopupMenu.Entry { Label = "Add to quick slots", Run = () => RunBatch("Added to quick slots", l => l.Find(e => e.Kind == ItemActions.Kind.QuickSlot)) });
            }
            entries.Add(new PopupMenu.Entry { Label = "Unmark all", Run = ExitMulti });
            Rect anchor = TryGetFocusRect(out Rect r) ? r : new Rect(MouseEmulator.Position.x, Screen.height - MouseEmulator.Position.y, 1f, 1f);
            UIWidget messageAnchor = Item != null ? Item.Icon : null;
            PopupMenu.Open(m_Marked.Count == 1 ? "1 item" : m_Marked.Count + " items", anchor, entries, null, messageAnchor);
        }

        /// <summary>
        /// Runs one action on every marked item (each item's own action entry, so the game's checks apply item by item),
        /// then posts a short summary of what really moved. Items that didn't move, or only in part, stay marked (and
        /// multi-select stays on); when everything moved, multi-select ends.
        /// </summary>
        private void RunBatch(string verb, System.Func<List<ItemActions.Entry>, ItemActions.Entry> pick)
        {
            int done = 0;
            int partly = 0;
            int failed = 0;
            string reason = null;
            UIWidget anchor = null;
            var list = new List<ItemActions.Entry>();
            var keep = new List<InventoryItem>();
            foreach (InventoryItem item in m_Marked.ToList())
            {
                UIInventoryGridItem slot = ItemActions.Find(item, null);
                if (slot == null || slot.Empty)
                {
                    failed++;
                    if (item != null && item.baseItem != null)
                    {
                        keep.Add(item);
                    }
                    continue;
                }
                anchor = anchor ?? slot.Icon;
                ItemActions.Build(slot, list);
                ItemActions.AddCategoryEntries(slot, list);
                ItemActions.Entry e = pick(list);
                if (e == null || e.Disabled != null)
                {
                    failed++;
                    keep.Add(item);
                    reason = reason ?? e?.Disabled;
                    continue;
                }
                switch (ItemActions.Run(e, slot))
                {
                    case ItemActions.Outcome.Done:
                        done++;
                        break;
                    case ItemActions.Outcome.Partly:
                        partly++;
                        keep.Add(item);
                        break;
                    default:
                        failed++;
                        keep.Add(item);
                        break;
                }
            }
            if (keep.Count == 0)
            {
                ExitMulti();
            }
            else
            {
                m_Marked.Clear();
                m_Marked.AddRange(keep);
                UIGlobalInventory.Instance.UnselectAll();
                m_MarkRefresh = 0f; // highlight the kept ones again right away
            }
            string text = $"{verb}: {done} item{(done == 1 ? "" : "s")}"
                + (partly > 0 ? $", {partly} only in part (no room for the rest)" : "")
                + (failed > 0 ? $" ({failed} stayed{(reason != null ? ": " + reason : "")})" : "")
                + (keep.Count > 0 ? ". Those are still marked." : "");
            Plugin.Log.LogInfo("Inventory: " + text);
            if (anchor != null && UIGlobalInventory.Instance != null)
            {
                UIGlobalInventory.Instance.PostMessage(text, anchor);
            }
        }

        private static void StepCharacter(int dir)
        {
            UIInventoryManager inv = UIInventoryManager.Instance;
            if (inv == null || Dragging)
            {
                return;
            }
            List<PartyMemberAI> party = PartyMemberAI.OnlyPrimaryPartyMembers.Where(m => m != null).OrderBy(m => m.Slot).ToList();
            if (party.Count < 2)
            {
                return;
            }
            int i = party.IndexOf(SelectedMember());
            PartyMemberAI next = party[((i < 0 ? 0 : i + dir) + party.Count) % party.Count];
            inv.SelectPartyMember(next);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        /// <summary>The stash can be opened here (UIInventoryItemZone's own rule).</summary>
        private static bool StashAllowed()
        {
            UIInventoryManager inv = UIInventoryManager.Instance;
            GameState gs = GameState.Instance;
            return UIStashManager.Instance != null && inv != null
                && (RestZone.PartyInRestZone || inv.StashAccess || gs == null || gs.CurrentMap == null || gs.CurrentMap.GetCanAccessStash()
                    || GameState.Option.GetOption(GameOption.BoolOption.DONT_RESTRICT_STASH));
        }

        protected override void AddNavigationPrompts(List<Prompt> into)
        {
            if (!On)
            {
                base.AddNavigationPrompts(into);
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => AddConsolePrompts(into));
        }

        private void AddConsolePrompts(List<Prompt> into)
        {
            AddNavigatePrompt(into);
            if (m_Multi)
            {
                UIInventoryGridItem hovered = Item;
                if (hovered != null)
                {
                    into.Add(new Prompt("south", m_Marked.Contains(hovered.InvItem) ? "Unmark" : "Mark"));
                }
                if (m_Marked.Count > 0)
                {
                    into.Add(new Prompt("north", $"Actions ({m_Marked.Count})"));
                    if (HasStash())
                    {
                        into.Add(new Prompt("west", $"Move {m_Marked.Count} to stash"));
                    }
                }
                into.Add(new Prompt(new[] { "lb", "rb" }, "Character"));
                into.Add(new Prompt(new[] { "east", "lstick_click" }, "Cancel multi-select"));
                return;
            }
            if (Dragging)
            {
                into.Add(new Prompt("south", "Put down"));
                return;
            }
            UIInventoryGridItem slot = Item;
            if (slot != null)
            {
                ItemActions.Entry main = MainAction(slot);
                if (main != null && main.Disabled == null)
                {
                    into.Add(new Prompt("south", main.Kind == ItemActions.Kind.Equip ? "Equip" : main.Label.StartsWith("Unequip") ? "Unequip" : "Move to pack"));
                    into.Add(new Prompt("south", "Hold: pick up"));
                }
                else
                {
                    into.Add(new Prompt("south", "Pick up"));
                }
                ItemActions.Entry move = MoveEntry(slot);
                if (move != null && move.Disabled == null)
                {
                    into.Add(new Prompt("west", InStash(slot) ? "Move to pack" : "Move to stash"));
                }
            }
            else
            {
                into.Add(new Prompt("south", "Select"));
            }
            into.Add(new Prompt(new[] { "lb", "rb" }, "Character"));
            into.Add(new Prompt("lstick_click", "Multi-select"));
            into.Add(new Prompt("lstick_click", CursorIsFree ? "Hold: lock cursor" : "Hold: free cursor"));
            into.Add(new Prompt("rstick_click", "Tooltips"));
        }
    }

    /// <summary>A screen that words the item tooltip's "Right-click for details." line itself (null = default).</summary>
    public interface IItemTooltipHint
    {
        string ItemTooltipHint { get; }
    }
}
