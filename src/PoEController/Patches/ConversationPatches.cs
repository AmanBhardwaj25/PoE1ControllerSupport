using System;
using HarmonyLib;
using PoEController.Schemes;

namespace PoEController.Patches
{
    /// <summary>
    /// The conversation window writes the player's responses (DrawResponses, or DrawResponsesDebug with localization
    /// debugging) only when the dialogue moves to a new node: the cursor goes onto the new responses then
    /// (<see cref="ConversationScheme.ResponsesDrawn"/>).
    /// </summary>
    [Compat.Feature(Compat.Features.ConversationFocus)]
    internal static class ConversationPatches
    {
        [HarmonyPatch(typeof(UIConversationManager), "DrawResponses")]
        private static class Draw
        {
            private static void Postfix() => Signal();
        }

        [HarmonyPatch(typeof(UIConversationManager), "DrawResponsesDebug")]
        private static class DrawDebug
        {
            private static void Postfix() => Signal();
        }

        private static void Signal()
        {
            try
            {
                ConversationScheme.ResponsesDrawn();
            }
            catch (Exception e)
            {
                Compat.Features.Off(Compat.Features.ConversationFocus, e.Message);
            }
        }
    }
}
