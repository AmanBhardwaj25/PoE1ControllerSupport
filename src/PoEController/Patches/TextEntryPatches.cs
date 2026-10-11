using System;
using System.Reflection;
using HarmonyLib;
using PoEController.Input;
using PoEController.TextEntry;

namespace PoEController.Patches
{
    /// <summary>
    /// The game asks for an on-screen keyboard (Steam's) wherever a text box gets focus: <c>UIInput.OnClick</c>, the
    /// <c>UIInput.selected</c> setter (name entry, text prompts), and the journal's note text. While the controller
    /// is in use we open our own keyboard there instead, so every copy of the game (Steam, GOG, …) gets the same
    /// one; with mouse/keyboard the game's own call runs unchanged. SteamManager is internal, so it is found by name.
    /// </summary>
    [Compat.Feature(Compat.Features.Keyboard)]
    internal static class TextEntryPatches
    {
        private static readonly Type s_SteamManager = AccessTools.TypeByName("SteamManager");

        // Set while the player clicks the journal's note text. The journal also asks for a keyboard every time a
        // note is merely shown; we only open on the click, so reading notes doesn't pop up a keyboard.
        private static bool s_JournalClick;

        private static bool UsingPad
        {
            get
            {
                Plugin p = Plugin.Instance;
                return p != null && p.isActiveAndEnabled && p.Mode != null && p.Mode.Active == InputDevice.Gamepad
                    && p.Pad != null && p.Pad.Connected;
            }
        }

        [HarmonyPatch]
        private static class ForInput
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(s_SteamManager, "TryShowGamepadTextInput", new[] { typeof(UIInput), typeof(bool), typeof(string) });

            private static bool Prefix(UIInput sender, ref bool __result)
            {
                try
                {
                    if (!UsingPad)
                    {
                        return true;
                    }
                    OnScreenKeyboard.OpenFor(sender);
                    __result = false;
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Keyboard request failed: {e}");
                    return true;
                }
            }
        }

        [HarmonyPatch]
        private static class ForCallback
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(s_SteamManager, "TryShowGamepadTextInput",
                    new[] { typeof(string), typeof(uint), typeof(bool), typeof(bool), typeof(string), typeof(Action<string>) });

            private static bool Prefix(string defaultText, uint maxChars, bool multiline, string pchDesc, Action<string> callback, ref bool __result)
            {
                try
                {
                    if (!UsingPad)
                    {
                        return true;
                    }
                    if (callback != null && s_JournalClick)
                    {
                        OnScreenKeyboard.OpenBuffer(defaultText, maxChars, multiline, pchDesc, callback);
                    }
                    __result = false;
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Keyboard request failed: {e}");
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(UIJournalContentText), "OnChildClick")]
        private static class JournalClick
        {
            private static void Prefix() => s_JournalClick = true;

            private static void Finalizer() => s_JournalClick = false;
        }

        /// <summary>
        /// NGUI also treats the pad's A as a click on the focused object, and a click inside a focused text box moves
        /// its caret to the mouse position. Typing always goes at the end, so ignore clicks on the box being typed into.
        /// </summary>
        [HarmonyPatch(typeof(UIInput), "OnClick")]
        private static class InputClick
        {
            private static bool Prefix(UIInput __instance)
            {
                try
                {
                    return !(UsingPad && OnScreenKeyboard.IsEditing(__instance));
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }
    }
}
