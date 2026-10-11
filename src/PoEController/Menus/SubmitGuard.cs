using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// NGUI reads pad A through Unity's legacy input as a "submit" click on its selected object (often the item slot
    /// or button clicked last). While one of our popups uses A, that click must not reach the game, so the one key is
    /// switched off and given back once it is up (so NGUI never sees a lone release).
    /// </summary>
    internal static class SubmitGuard
    {
        private static UICamera s_MutedOn;
        private static KeyCode s_Saved;

        public static void Mute()
        {
            UICamera handler = UICamera.eventHandler;
            if (s_MutedOn != null || handler == null)
            {
                return;
            }
            s_MutedOn = handler;
            s_Saved = handler.submitKey1;
            handler.submitKey1 = KeyCode.None;
        }

        /// <param name="force">Give it back even while the key is still held (scheme going away).</param>
        public static void Restore(bool force)
        {
            if (s_MutedOn == null)
            {
                return;
            }
            if (!force && s_Saved != KeyCode.None && UnityEngine.Input.GetKey(s_Saved))
            {
                return;
            }
            s_MutedOn.submitKey1 = s_Saved;
            s_MutedOn = null;
        }
    }
}
