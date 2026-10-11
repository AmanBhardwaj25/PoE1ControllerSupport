using System;
using System.Collections.Generic;
using PoEController.Input;
using PoEController.UI;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// The one popup list (only one is ever open): actions of an inspect window, the compare choices, items to compare
    /// with. The owning scheme calls <see cref="HandleInput"/> first each frame; while open it takes all input:
    /// d-pad / LS up-down choose, A runs the entry (a dimmed one shows its reason instead), B backs out.
    /// NGUI's own pad-A click is muted meanwhile (<see cref="SubmitGuard"/>).
    /// </summary>
    internal static class PopupMenu
    {
        public sealed class Entry
        {
            public string Label;
            /// <summary>Null when usable; otherwise why not (shown dimmed, and as a message on A).</summary>
            public string Disabled = null;
            public Action Run;
            /// <summary>The game button this action stands for, if any (gets the pad glyph drawn beside it).</summary>
            public GameObject Target;
        }

        private sealed class View : IListMenu
        {
            public bool MenuOpen => s_Open;
            public string Title => s_Title;
            public Rect Anchor => s_Anchor;
            public int Focus => s_Focus;
            public int Count => s_Entries.Count;
            public string LabelAt(int i) => s_Entries[i].Label;
            public string DisabledAt(int i) => s_Entries[i].Disabled;
        }

        private const float StickPress = 0.6f;
        private const float StickRelease = 0.3f;

        private static readonly List<Entry> s_Entries = new List<Entry>();
        private static bool s_Open;
        private static string s_Title;
        private static Rect s_Anchor;
        private static int s_Focus;
        private static bool s_StickLatched;
        private static Action s_OnBack;
        private static UIWidget s_MessageAnchor;

        public static IListMenu Menu { get; } = new View();

        public static bool IsOpen => s_Open;

        /// <param name="anchor">GUI rect the list sits beside.</param>
        /// <param name="onBack">Run when B closes it (null = just close).</param>
        /// <param name="messageAnchor">Where the game shows the reason for a dimmed entry (may be null).</param>
        public static void Open(string title, Rect anchor, IEnumerable<Entry> entries, Action onBack, UIWidget messageAnchor)
        {
            s_Entries.Clear();
            s_Entries.AddRange(entries);
            if (s_Entries.Count == 0)
            {
                return;
            }
            s_Title = title;
            s_Anchor = anchor;
            s_OnBack = onBack;
            s_MessageAnchor = messageAnchor;
            s_Focus = 0;
            for (int i = 0; i < s_Entries.Count; i++)
            {
                if (s_Entries[i].Disabled == null)
                {
                    s_Focus = i;
                    break;
                }
            }
            s_StickLatched = true;
            s_Open = true;
            MouseEmulator.ReleaseAll();
            SubmitGuard.Mute();
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        /// <summary>Closes without running anything (scheme change, window gone).</summary>
        public static void Close()
        {
            s_Open = false;
            s_Entries.Clear();
            s_OnBack = null;
        }

        /// <summary>Returns true while the popup is open (it has taken this frame's input).</summary>
        public static bool HandleInput(Gamepad pad)
        {
            if (!s_Open)
            {
                return false;
            }
            if (pad.GetButtonDown(PadButton.East))
            {
                Action back = s_OnBack;
                Finish(pad);
                back?.Invoke();
                return true;
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
            if (!s_StickLatched && Mathf.Abs(y) > StickPress)
            {
                s_StickLatched = true;
                move = y > 0f ? -1 : 1;
            }
            else if (Mathf.Abs(y) < StickRelease)
            {
                s_StickLatched = false;
            }
            if (move != 0)
            {
                s_Focus = (s_Focus + move + s_Entries.Count) % s_Entries.Count;
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                Entry e = s_Entries[s_Focus];
                if (e.Disabled != null)
                {
                    if (s_MessageAnchor != null && UIGlobalInventory.Instance != null)
                    {
                        UIGlobalInventory.Instance.PostMessage(e.Disabled, s_MessageAnchor);
                    }
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                    return true;
                }
                Finish(pad);
                Plugin.Log.LogInfo($"Popup: {e.Label}");
                Compare.CompareSession.Safe(e.Run, "Popup '" + e.Label + "'");
            }
            return true;
        }

        private static void Finish(Gamepad pad)
        {
            Close();
            // The button that closed it must not reach the window once released.
            pad.SuppressHeldButtons();
        }
    }
}
