using System;
using HarmonyLib;
using PoEController.Schemes;

namespace PoEController.Patches
{
    /// <summary>
    /// The game's own page changes inside one window: the menu cursor starts again on the new page's selected element
    /// (<see cref="MenuScheme.PageChanged"/>), like when a window opens. Character creation / level up show each step
    /// with UICharacterCreationController.Show; the options window switches tab with UIOptionsManager.ChangePage.
    /// </summary>
    [Compat.Feature(Compat.Features.MenuPageFocus)]
    internal static class MenuPagePatches
    {
        [HarmonyPatch(typeof(UICharacterCreationController), nameof(UICharacterCreationController.Show))]
        private static class CharacterCreationPage
        {
            private static void Postfix()
            {
                try
                {
                    MenuScheme.PageChanged();
                }
                catch (Exception e)
                {
                    Compat.Features.Off(Compat.Features.MenuPageFocus, e.Message);
                }
            }
        }

        [HarmonyPatch(typeof(UIOptionsManager), nameof(UIOptionsManager.ChangePage))]
        private static class OptionsPage
        {
            private static void Postfix()
            {
                try
                {
                    MenuScheme.PageChanged();
                }
                catch (Exception e)
                {
                    Compat.Features.Off(Compat.Features.MenuPageFocus, e.Message);
                }
            }
        }
    }
}
