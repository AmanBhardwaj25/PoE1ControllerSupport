using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PoEController.Compare;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Shops and inns in Console style (after the console version), on top of the cursor menu: A select
    /// (buy / sell as with the mouse), Y details (one right-click: the item card), X quick compare on equipment (as
    /// PC layout's Y; Y means "details" everywhere in Console style), LB / RB the window's tabs (Store, Inn, Recruit,
    /// Respec — only those the vendor shows; set through the window's own <c>Page</c>, which its tab buttons change),
    /// B close.
    /// </summary>
    public sealed class ConsoleStoreScheme : MenuScheme, IItemTooltipHint
    {
        private static readonly UIStorePageType[] s_Order = { UIStorePageType.Store, UIStorePageType.Inn, UIStorePageType.Recruit, UIStorePageType.Respec };

        private static readonly MethodInfo s_DoTrade = AccessTools.Method(typeof(UIStoreStorePage), "OnDoTrade");

        private Equippable m_Compare;
        private bool m_CompareIsStore;
        private bool m_OverItem;
        /// <summary>X: tap = quick compare (on release), hold = Trade (as the window's Trade button).</summary>
        private readonly HoldButton m_X = new HoldButton(PadButton.West);
        private Equippable m_PressCompare;
        private bool m_PressCompareIsStore;

        public ConsoleStoreScheme(params Binding[] bindings)
            : base("Store (Console)", bindings)
        {
        }

        private static bool On => Compat.Features.On(Compat.Features.ConsoleMenus);

        protected override bool RightClickOnX => !On;

        public string ItemTooltipHint => !On ? null : (m_Compare != null ? "{north} for details.\n{west} to quick compare." : "{north} for details.");

        public override void Activate()
        {
            base.Activate();
            m_Compare = null;
            m_OverItem = false;
            m_X.Reset();
            m_PressCompare = null;
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            // B on the stash view: back to the party first (before the window's own B, which closes the shop).
            if (!InTooltipMode)
            {
                StoreStash.HandleBack(pad);
            }
            base.HandleInput(ctx, pad, dt);
            if (!On)
            {
                return;
            }
            Compat.Features.Run(Compat.Features.ConsoleMenus, () => HandleConsole(pad, dt));
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            base.GetPrompts(ctx, into);
            StoreStash.RelabelBack(into);
        }

        private void HandleConsole(Gamepad pad, float dt)
        {
            UIStoreManager store = UIStoreManager.Instance;
            if (store == null || !store.WindowActive())
            {
                return;
            }
            UpdateHovered(store);
            if (pad.GetButtonDown(PadButton.North) && m_OverItem)
            {
                MouseEmulator.SetRight(true); // one right-click; MenuScheme lets go next frame
            }
            bool pending = TradePending(store);
            HoldButton.Result x = m_X.Update(pad, dt, pending);
            if (m_X.PressedNow)
            {
                // What a tap compares: the item under the cursor when X went down.
                m_PressCompare = m_Compare;
                m_PressCompareIsStore = m_CompareIsStore;
            }
            if (x == HoldButton.Result.Held)
            {
                // The window's own Trade button (UIStoreStorePage.OnDoTrade): its checks and messages apply.
                Plugin.Log.LogInfo("Store: trade (hold X)");
                s_DoTrade.Invoke(store.StorePage, new object[] { store.StorePage.gameObject });
            }
            else if (x == HoldButton.Result.Tapped)
            {
                if (m_PressCompare != null)
                {
                    bool ok = false;
                    Equippable item = m_PressCompare;
                    bool isStore = m_PressCompareIsStore;
                    CompareSession.Safe(() => ok = CompareSession.QuickCompare(item, isStore), "Quick compare");
                    Plugin.Log.LogInfo($"Quick compare {item.name}: {(ok ? "shown" : "nothing to compare")}");
                    if (!ok)
                    {
                        GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                    }
                }
                else if (pending)
                {
                    UI.Toast.Show("Hold to trade");
                }
                m_PressCompare = null;
            }
            if (pad.GetButtonDown(PadButton.LeftShoulder))
            {
                StepTab(store, -1);
            }
            if (pad.GetButtonDown(PadButton.RightShoulder))
            {
                StepTab(store, 1);
            }
        }

        /// <summary>Something is in the buy or sell area of the store page (the Trade button would do something).</summary>
        private static bool TradePending(UIStoreManager store)
        {
            return store.Page == UIStorePageType.Store && store.StorePage != null && store.StorePage.TradeIsPending;
        }

        private void UpdateHovered(UIStoreManager store)
        {
            m_Compare = null;
            m_OverItem = false;
            Collider c = FocusNavigator.ColliderAt(MouseEmulator.Position);
            UIInventoryGridItem slot = c != null ? c.GetComponentInParent<UIInventoryGridItem>() : null;
            if (slot == null || slot.Empty || slot.InvItem == null || slot.InvItem.baseItem == null || !slot.transform.IsChildOf(store.transform))
            {
                return;
            }
            m_OverItem = true;
            if (Compat.Features.On(Compat.Features.Compare) && CompareSession.Comparable(slot.InvItem.baseItem))
            {
                m_Compare = (Equippable)slot.InvItem.baseItem;
                m_CompareIsStore = slot.Owner != null && slot.Owner.IsStore;
            }
        }

        private static UIMultiSpriteImageButton ButtonFor(UIStoreManager store, UIStorePageType page)
        {
            switch (page)
            {
                case UIStorePageType.Store: return store.StoreButton;
                case UIStorePageType.Inn: return store.InnButton;
                case UIStorePageType.Recruit: return store.RecruitButton;
                default: return store.RespecPartyButton;
            }
        }

        private static List<UIStorePageType> Tabs(UIStoreManager store)
        {
            var tabs = new List<UIStorePageType>();
            foreach (UIStorePageType p in s_Order)
            {
                UIMultiSpriteImageButton b = ButtonFor(store, p);
                if (b != null && b.gameObject.activeInHierarchy)
                {
                    tabs.Add(p);
                }
            }
            return tabs;
        }

        private static void StepTab(UIStoreManager store, int dir)
        {
            List<UIStorePageType> tabs = Tabs(store);
            if (tabs.Count < 2)
            {
                return;
            }
            int i = tabs.IndexOf(store.Page);
            store.Page = tabs[((i < 0 ? 0 : i + dir) % tabs.Count + tabs.Count) % tabs.Count];
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Store tab: {store.Page}");
        }

        protected override void AddNavigationPrompts(List<Prompt> into)
        {
            UIStoreManager store = UIStoreManager.Instance;
            if (!On || store == null)
            {
                base.AddNavigationPrompts(into);
                return;
            }
            AddNavigatePrompt(into);
            into.Add(new Prompt("south", "Select"));
            if (m_OverItem)
            {
                into.Add(new Prompt("north", "Details"));
            }
            if (m_Compare != null)
            {
                into.Add(new Prompt("west", "Quick compare"));
            }
            if (Compat.Features.Get(Compat.Features.ConsoleMenus, () => TradePending(store), false))
            {
                into.Add(new Prompt("west", "Trade (hold)", m_X.Progress));
            }
            if (Compat.Features.Get(Compat.Features.ConsoleMenus, () => Tabs(store).Count, 0) >= 2)
            {
                into.Add(new Prompt(new[] { "lb", "rb" }, "Tab"));
            }
            into.Add(new Prompt("rstick_ud", "Scroll"));
        }
    }
}
