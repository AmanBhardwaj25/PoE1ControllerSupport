using HarmonyLib;
using PoEController.Input;

namespace PoEController.Patches
{
    /// <summary>
    /// In world mode on the controller the OS cursor is hidden and parked mid-screen, but the game still works out
    /// which character it is over (<c>GameInput.Update</c> → <c>GameCursor.CharacterUnderCursor</c>) and highlights
    /// that character's portrait / endurance. With the follow camera the party walks through the screen centre all
    /// the time, so the highlight flickered between them. While the mod has the cursor hidden, the result is cleared
    /// right after the game computes it (cleared, never faked).
    /// </summary>
    [HarmonyPatch(typeof(GameInput), "Update")]
    [Compat.Feature(Compat.Features.HiddenCursorHover)]
    internal static class HoverPatches
    {
        private static void Postfix()
        {
            try
            {
                if (DeviceTransitions.CursorHiddenByMod)
                {
                    GameCursor.CharacterUnderCursor = null;
                }
            }
            catch (System.Exception e)
            {
                Compat.Features.Off(Compat.Features.HiddenCursorHover, "error: " + e);
            }
        }
    }
}
