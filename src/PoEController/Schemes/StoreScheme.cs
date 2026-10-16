using System.Collections.Generic;
using PoEController.Compare;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// Shops (<see cref="UIStoreManager"/>): the cursor menu, plus Y on equipment = quick compare with what the
    /// selected companion has equipped (<see cref="CompareSession.QuickCompare"/>). Nothing selected / nothing to
    /// compare → the game's error sound, nothing opens.
    /// </summary>
    public sealed class StoreScheme : MenuScheme
    {
        private Equippable m_Hovered;
        private bool m_HoveredIsStore;

        public StoreScheme(params Binding[] bindings)
            : base("Store", bindings)
        {
        }

        /// <summary>Y quick-compares the item under the cursor (for the tooltip hint).</summary>
        internal bool HoveredCanCompare => m_Hovered != null;

        public override void Activate()
        {
            base.Activate();
            m_Hovered = null;
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            // B on the stash view: back to the party first (before the window's own B, which closes the shop).
            if (!InTooltipMode)
            {
                StoreStash.HandleBack(pad);
            }
            base.HandleInput(ctx, pad, dt);
            m_Hovered = null;
            Compat.Features.Run(Compat.Features.Compare, UpdateHovered);
            if (m_Hovered != null && pad.GetButtonDown(PadButton.North))
            {
                bool ok = false;
                Equippable item = m_Hovered;
                bool isStore = m_HoveredIsStore;
                CompareSession.Safe(() => ok = CompareSession.QuickCompare(item, isStore), "Quick compare");
                Plugin.Log.LogInfo($"Quick compare {m_Hovered.name}: {(ok ? "shown" : "nothing to compare")}");
                if (!ok)
                {
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                }
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            base.GetPrompts(ctx, into);
            StoreStash.RelabelBack(into);
            if (m_Hovered != null)
            {
                into.Add(new Prompt("north", "Quick compare"));
            }
        }

        private void UpdateHovered()
        {
            m_Hovered = null;
            UIStoreManager store = UIStoreManager.Instance;
            Collider c = FocusNavigator.ColliderAt(MouseEmulator.Position);
            UIInventoryGridItem slot = c != null ? c.GetComponentInParent<UIInventoryGridItem>() : null;
            if (store == null || slot == null || slot.InvItem == null || !slot.transform.IsChildOf(store.transform))
            {
                return;
            }
            if (CompareSession.Comparable(slot.InvItem.baseItem))
            {
                m_Hovered = (Equippable)slot.InvItem.baseItem;
                m_HoveredIsStore = slot.Owner != null && slot.Owner.IsStore;
            }
        }
    }
}
