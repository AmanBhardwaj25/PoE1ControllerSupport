using System;
using System.Collections.Generic;
using PoEController.Input;
using PoEController.Schemes;

namespace PoEController.Menus
{
    /// <summary>
    /// The stash (or crafting) view on the player side of a shop. The game only offers Close there, which leaves
    /// the whole shop (and asks to cancel a pending trade). On the controller, B first goes back to the party view
    /// (what the window's own Party button does, <c>UIStoreViewingButton</c>), like B leaving the stash over the
    /// inventory; B again closes the shop.
    /// </summary>
    internal static class StoreStash
    {
        private const string BackLabel = "Party";

        /// <summary>The shop is up with the stash / crafting side showing instead of the party.</summary>
        public static bool Showing
        {
            get
            {
                try
                {
                    UIStoreManager store = UIStoreManager.Instance;
                    return store != null && store.WindowActive() && store.Page == UIStorePageType.Store
                        && store.StorePage != null && store.StorePage.ViewingPage != PartyInventoryType.Party;
                }
                catch (Exception)
                {
                    return false; // a changed game: B closes as the game has it
                }
            }
        }

        /// <summary>B on the stash view: back to the party view. True = B was used here (don't pass it to the game).</summary>
        public static bool HandleBack(Gamepad pad)
        {
            if (!pad.GetButtonDown(PadButton.East) || !Showing)
            {
                return false;
            }
            UIStoreManager.Instance.StorePage.ChangePlayerViewing(PartyInventoryType.Party);
            // B is still down: kept from the next frame so it doesn't also close the shop.
            pad.SuppressHeldButtons();
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo("Store: stash view closed (B), back to the party");
            return true;
        }

        /// <summary>The B prompt says where B goes on the stash view.</summary>
        public static void RelabelBack(List<Prompt> into)
        {
            if (!Showing)
            {
                return;
            }
            for (int i = 0; i < into.Count; i++)
            {
                if (into[i].Glyphs != null && into[i].Glyphs.Length == 1 && into[i].Glyphs[0] == "east")
                {
                    into[i] = new Prompt("east", BackLabel);
                }
            }
        }
    }
}
