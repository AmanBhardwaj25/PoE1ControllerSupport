using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Inventory window: the normal cursor menu, plus Y on an item = a small menu of "Equip on …", "Send to …" and
    /// "Send to stash" (see <see cref="ItemActions"/>), so items never have to be dragged with the stick.
    /// </summary>
    public class InventoryScheme : MenuScheme, UI.IListMenu
    {
        /// <summary>How long an "equip on another member" may wait for the window to switch to them.</summary>
        private const float PendingTimeout = 0.5f;
        private const float StickPress = 0.6f;
        private const float StickRelease = 0.3f;

        private readonly List<ItemActions.Entry> m_Entries = new List<ItemActions.Entry>();
        private readonly List<ItemActions.Entry> m_Scratch = new List<ItemActions.Entry>();

        private static bool s_Broken => !Compat.Features.On(Compat.Features.ItemMenu);

        private UIInventoryGridItem m_Hovered;
        private InventoryItem m_HoveredItem;
        private bool m_HoveredHasActions;

        private UIInventoryGridItem m_Source;
        private InventoryItem m_SourceItem;
        private bool m_StickLatched;

        private ItemActions.Entry m_Pending;
        private UIInventoryGridItem m_PendingSource;
        private InventoryItem m_PendingItem;
        private float m_PendingTime;

        private readonly bool m_AlsoInventory;

        /// <param name="alsoInventory">True for a window that opens over the inventory (stash): the inventory's party rows stay reachable.</param>
        public InventoryScheme(string name, bool alsoInventory, params Binding[] bindings)
            : base(name, bindings)
        {
            m_AlsoInventory = alsoInventory;
        }

        protected override Transform ExtraNavigationRoot =>
            m_AlsoInventory && UIInventoryManager.Instance != null && UIInventoryManager.Instance.WindowActive() ? UIInventoryManager.Instance.transform : null;

        public bool MenuOpen { get; private set; }

        int UI.IListMenu.Count => m_Entries.Count;
        string UI.IListMenu.LabelAt(int i) => m_Entries[i].Label;
        string UI.IListMenu.DisabledAt(int i) => m_Entries[i].Disabled;

        /// <summary>Y opens the item menu for the item under the cursor right now.</summary>
        internal bool HoveredHasActions => m_HoveredHasActions && !MenuOpen && m_Pending == null;
        internal IReadOnlyList<ItemActions.Entry> Entries => m_Entries;
        public int Focus { get; private set; }
        public string Title { get; private set; }
        /// <summary>The item's slot on screen (GUI coordinates) when the menu opened; the menu sits beside it.</summary>
        public Rect Anchor { get; private set; }

        public override void Activate()
        {
            base.Activate();
            MenuOpen = false;
            m_Pending = null;
            m_Hovered = null;
            m_HoveredItem = null;
            m_HoveredHasActions = false;
        }

        public override void Deactivate()
        {
            MenuOpen = false;
            m_Pending = null;
            RestoreSubmit(force: true);
            base.Deactivate();
        }

        // ---- Hooks for Console style's inventory (ConsoleInventoryScheme); PC layout uses the defaults ----

        /// <summary>The item slot under the cursor (null over anything else, or while an item is being carried).</summary>
        protected UIInventoryGridItem Hovered => m_Hovered;

        /// <summary>The item menu is open or an action is waiting for the window.</summary>
        protected bool Busy => MenuOpen || m_Pending != null;

        /// <summary>Extra item menu entries (Console adds Examine first).</summary>
        private protected virtual void AddExtraEntries(UIInventoryGridItem source, List<ItemActions.Entry> into)
        {
        }

        /// <summary>True when the scheme mutes NGUI's pad submit for its whole lifetime (Console: A is ours).</summary>
        protected virtual bool KeepSubmitMuted => false;

        /// <summary>The Y prompt's word.</summary>
        protected virtual string MenuLabel => "Item options";

        /// <summary>Runs an item action now, or keeps it pending if the window must switch character first.</summary>
        private protected void RunAction(ItemActions.Entry e, UIInventoryGridItem source)
        {
            Plugin.Log.LogInfo($"Item action: {e.Label} ({source.InvItem.baseItem.name})");
            if (!ItemActions.Run(e, source))
            {
                m_Pending = e;
                m_PendingSource = source;
                m_PendingItem = source.InvItem;
                m_PendingTime = 0f;
            }
        }

        /// <summary>The item's actions with the scheme's extras, as the menu would list them.</summary>
        private protected void BuildEntries(UIInventoryGridItem source, List<ItemActions.Entry> into)
        {
            ItemActions.Build(source, into);
            AddExtraEntries(source, into);
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            if (!MenuOpen && !KeepSubmitMuted)
            {
                RestoreSubmit(force: false);
            }
            if (s_Broken)
            {
                base.HandleInput(ctx, pad, dt);
                return;
            }
            try
            {
                if (m_Pending != null)
                {
                    RunPending(dt);
                    return;
                }
                if (MenuOpen)
                {
                    HandleMenu(pad);
                    return;
                }
            }
            catch (System.Exception e)
            {
                Fail(e, pad);
                return;
            }
            base.HandleInput(ctx, pad, dt);
            try
            {
                UpdateHovered();
                if (m_HoveredHasActions && pad.GetButtonDown(PadButton.North))
                {
                    OpenMenu();
                }
            }
            catch (System.Exception e)
            {
                Fail(e, pad);
            }
        }

        /// <summary>The item menu is an extra: on any error it closes, turns itself off and the window works as before.</summary>
        private void Fail(System.Exception e, Gamepad pad)
        {
            Compat.Features.Off(Compat.Features.ItemMenu, "error: " + e);
            m_Pending = null;
            m_HoveredHasActions = false;
            if (MenuOpen)
            {
                CloseMenu(pad);
            }
            RestoreSubmit(force: true);
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            if (MenuOpen)
            {
                into.Add(new Prompt("dpad_ud", "Choose"));
                into.Add(new Prompt("south", "Select"));
                into.Add(new Prompt("east", "Back"));
                return;
            }
            base.GetPrompts(ctx, into);
            if (m_HoveredHasActions)
            {
                into.Add(new Prompt("north", MenuLabel));
            }
        }

        private void UpdateHovered()
        {
            UIInventoryGridItem item = ItemActions.ItemAt(MouseEmulator.Position);
            InventoryItem inv = item != null ? item.InvItem : null;
            if (item == m_Hovered && inv == m_HoveredItem)
            {
                return;
            }
            m_Hovered = item;
            m_HoveredItem = inv;
            m_HoveredHasActions = false;
            if (item != null)
            {
                BuildEntries(item, m_Scratch);
                m_HoveredHasActions = m_Scratch.Count > 0;
            }
        }

        private void OpenMenu()
        {
            BuildEntries(m_Hovered, m_Entries);
            if (m_Entries.Count == 0)
            {
                return;
            }
            m_Source = m_Hovered;
            m_SourceItem = m_HoveredItem;
            Title = m_SourceItem.baseItem.Name;
            Focus = 0;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].Disabled == null)
                {
                    Focus = i;
                    break;
                }
            }
            Anchor = TryGetFocusRect(out Rect r) ? r : new Rect(MouseEmulator.Position.x, Screen.height - MouseEmulator.Position.y, 1f, 1f);
            MouseEmulator.ReleaseAll();
            MuteSubmit();
            m_StickLatched = true; // ignore a stick already pushed when Y was pressed
            MenuOpen = true;
            if (GlobalAudioPlayer.Instance != null)
            {
                GlobalAudioPlayer.Instance.Play(UIAudioList.UIAudioType.ButtonUp);
            }
        }

        private static void MuteSubmit() => SubmitGuard.Mute();

        private static void RestoreSubmit(bool force) => SubmitGuard.Restore(force);

        private void CloseMenu(Gamepad pad)
        {
            MenuOpen = false;
            // The A/B/Y that closed the menu must not reach the window once released.
            pad.SuppressHeldButtons();
            m_Hovered = null;
            m_HoveredItem = null;
        }

        private void HandleMenu(Gamepad pad)
        {
            m_Source = ItemActions.Find(m_SourceItem, m_Source);
            if (m_Source == null || UIInventoryManager.Instance == null || !UIInventoryManager.Instance.WindowActive())
            {
                CloseMenu(pad);
                return;
            }
            if (pad.GetButtonDown(PadButton.East) || pad.GetButtonDown(PadButton.North))
            {
                CloseMenu(pad);
                return;
            }

            int move = 0;
            if (pad.GetButtonRepeat(PadButton.DpadUp))
            {
                move = -1;
            }
            else if (pad.GetButtonRepeat(PadButton.DpadDown))
            {
                move = 1;
            }
            float y = pad.LeftStick.y;
            if (!m_StickLatched && Mathf.Abs(y) > StickPress)
            {
                m_StickLatched = true;
                move = y > 0f ? -1 : 1;
            }
            else if (Mathf.Abs(y) < StickRelease)
            {
                m_StickLatched = false;
            }
            if (move != 0)
            {
                Focus = (Focus + move + m_Entries.Count) % m_Entries.Count;
            }

            if (pad.GetButtonDown(PadButton.South))
            {
                ItemActions.Entry e = m_Entries[Focus];
                if (e.Disabled != null)
                {
                    UIGlobalInventory.Instance.PostMessage(e.Disabled, m_Source.Icon);
                    return;
                }
                CloseMenu(pad);
                Plugin.Log.LogInfo($"Item menu: {e.Label} ({m_SourceItem.baseItem.name})");
                if (!ItemActions.Run(e, m_Source))
                {
                    m_Pending = e;
                    m_PendingSource = m_Source;
                    m_PendingItem = m_SourceItem;
                    m_PendingTime = 0f;
                }
            }
        }

        private void RunPending(float dt)
        {
            m_PendingTime += dt;
            m_PendingSource = ItemActions.Find(m_PendingItem, m_PendingSource);
            if (m_PendingSource == null)
            {
                Plugin.Log.LogWarning("Item menu: item moved before the action could finish");
                m_Pending = null;
                return;
            }
            if (ItemActions.Run(m_Pending, m_PendingSource))
            {
                m_Pending = null;
            }
            else if (m_PendingTime > PendingTimeout)
            {
                Plugin.Log.LogWarning($"Item menu: '{m_Pending.Label}' gave up (the inventory didn't switch character, or the slot didn't clear)");
                m_Pending = null;
            }
        }
    }
}
