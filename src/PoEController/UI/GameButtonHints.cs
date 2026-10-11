using System;
using System.Collections.Generic;
using PoEController.Compat;
using PoEController.Input;
using PoEController.Schemes;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Which game buttons get a pad glyph (<see cref="ButtonHints"/>): a window's own buttons for the bindings the
    /// active scheme really has (close = B, message box A/B, loot Take All = Y, conversation Continue = A), and the
    /// HUD's hotkey buttons (<c>UITriggerButtonByHotkey</c> / <c>UIActionBarOnClick</c>) for the game actions the world
    /// controls press in the current mode.
    /// </summary>
    internal static class GameButtonHints
    {
        private const float HudRescanSeconds = 2f;
        private const float CloseRescanSeconds = 1f;

        private struct HudButton
        {
            public GameObject Target;
            public MappedControl Control;
        }

        private static readonly List<HudButton> s_Hud = new List<HudButton>();
        private static float s_HudScanTime = -100f;

        private static UIHudWindow s_CloseWindow;
        private static GameObject s_CloseButton;
        private static float s_CloseScanTime = -100f;

        /// <summary>Glyphs on the top window's buttons, for the bindings <paramref name="bindings"/> of the active scheme.</summary>
        public static void ForWindow(UIHudWindow window, IReadOnlyList<Binding> bindings)
        {
            if (window == null || bindings == null)
            {
                return;
            }
            // Game UI fields are read by name (GameRef): a renamed field means no glyph, never an error.
            if (window is UIMessageBox)
            {
                Component[] buttons = GameRef.Get<Component[]>(window, "Buttons");
                if (buttons != null)
                {
                    if (Binds(bindings, PadButton.South, MappedControl.MB_CONFIRM) && buttons.Length > 0 && buttons[0] != null)
                    {
                        ButtonHints.Add(buttons[0].gameObject, "south");
                    }
                    if (Binds(bindings, PadButton.East, MappedControl.CLOSE_WINDOW) && buttons.Length > 1 && buttons[1] != null)
                    {
                        ButtonHints.Add(buttons[1].gameObject, "east");
                    }
                }
                return;
            }
            if (window is UIConversationManager && Binds(bindings, PadButton.South, MappedControl.CONV_CONTINUE))
            {
                ButtonHints.Add(GameRef.Object(window, "ContinueButton"), "south");
            }
            if (window is UILootManager)
            {
                if (Binds(bindings, PadButton.North, MappedControl.TAKE_ALL))
                {
                    ButtonHints.Add(GameRef.Object(window, "TakeAllButton"), "north");
                }
                else if (Binds(bindings, PadButton.West, MappedControl.TAKE_ALL))
                {
                    // Console style: X takes all.
                    ButtonHints.Add(GameRef.Object(window, "TakeAllButton"), "west");
                }
                GameObject close = GameRef.Object(window, "CloseButton");
                if (close != null && Binds(bindings, PadButton.East, MappedControl.CLOSE_WINDOW))
                {
                    ButtonHints.Add(close, "east");
                    return;
                }
            }
            if (Binds(bindings, PadButton.East, MappedControl.CLOSE_WINDOW))
            {
                ButtonHints.Add(CloseButtonOf(window), "east");
            }
        }

        /// <summary>Glyphs on the HUD buttons whose game action the pad presses right now (pairs: action → glyph).</summary>
        public static void ForHud(params KeyValuePair<MappedControl, string>[] actions)
        {
            Features.Run(Features.ButtonGlyphs, () => ForHudUnsafe(actions));
        }

        private static void ForHudUnsafe(KeyValuePair<MappedControl, string>[] actions)
        {
            if (Time.unscaledTime - s_HudScanTime > HudRescanSeconds)
            {
                ScanHud();
            }
            foreach (HudButton b in s_Hud)
            {
                if (b.Target == null || !b.Target.activeInHierarchy)
                {
                    continue;
                }
                foreach (KeyValuePair<MappedControl, string> a in actions)
                {
                    if (a.Key == b.Control)
                    {
                        ButtonHints.Add(b.Target, a.Value, ButtonHints.Placement.Badge);
                        break;
                    }
                }
            }
        }

        public static KeyValuePair<MappedControl, string> Pair(MappedControl control, string glyph) => new KeyValuePair<MappedControl, string>(control, glyph);

        private static bool Binds(IReadOnlyList<Binding> bindings, PadButton button, MappedControl control)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Button == button && bindings[i].Control == control)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ScanHud()
        {
            s_HudScanTime = Time.unscaledTime;
            s_Hud.Clear();
            foreach (UITriggerButtonByHotkey t in UnityEngine.Object.FindObjectsOfType<UITriggerButtonByHotkey>())
            {
                if (GameRef.TryGet(t, "MappedControl", out MappedControl c) && c != MappedControl.NONE)
                {
                    s_Hud.Add(new HudButton { Target = t.gameObject, Control = c });
                }
            }
            foreach (UIActionBarOnClick a in UnityEngine.Object.FindObjectsOfType<UIActionBarOnClick>())
            {
                if (GameRef.TryGet(a, "Hotkey", out MappedControl c) && c != MappedControl.NONE)
                {
                    s_Hud.Add(new HudButton { Target = a.gameObject, Control = c });
                }
            }
        }

        /// <summary>
        /// The window's close button: windows share no close-button component, so this is the first active child with
        /// a collider whose name contains "close" (cached per window). A miss only means no glyph.
        /// </summary>
        private static GameObject CloseButtonOf(UIHudWindow window)
        {
            if (window != s_CloseWindow || (s_CloseButton == null && Time.unscaledTime - s_CloseScanTime > CloseRescanSeconds)
                || (s_CloseButton != null && !s_CloseButton.activeInHierarchy))
            {
                s_CloseWindow = window;
                s_CloseScanTime = Time.unscaledTime;
                s_CloseButton = null;
                foreach (Collider c in window.GetComponentsInChildren<Collider>(false))
                {
                    if (c.enabled && c.gameObject.name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        s_CloseButton = c.gameObject;
                        break;
                    }
                }
            }
            return s_CloseButton;
        }
    }
}
