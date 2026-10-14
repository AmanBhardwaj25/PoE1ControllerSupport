using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// NGUI reads the pad by itself through Unity's legacy input, next to the mod: <c>submitKey1</c> =
    /// JoystickButton0 clicks NGUI's selected object, <c>cancelKey1</c> = JoystickButton1 deselects it (a selected
    /// text field loses focus) and <c>useController</c> moves its selection with the "Vertical" / "Horizontal" axes.
    /// Those button numbers are the Xbox layout (A, B); under DirectInput a PlayStation pad reports Square as 0 and
    /// Cross as 1, other pads their own numbers. While the pad is the active device all three are switched off on
    /// every UICamera (the mod reads the pad itself, through SDL); keyboard keys (Return, Escape, arrows) stay on.
    /// The game's own values are given back on keyboard / mouse and on unload.
    /// </summary>
    internal static class NguiPadInput
    {
        private struct Original
        {
            public KeyCode Submit;
            public KeyCode Cancel;
            public bool Controller;
        }

        private static readonly FieldInfo s_List = AccessTools.Field(typeof(UICamera), "mList");
        private static readonly Dictionary<UICamera, Original> s_Originals = new Dictionary<UICamera, Original>();
        private static bool s_Off;
        private static bool s_Failed;

        /// <summary>Every frame: <paramref name="padActive"/> = the pad is the device in use.</summary>
        public static void Update(bool padActive)
        {
            if (s_Failed)
            {
                return;
            }
            try
            {
                if (padActive)
                {
                    SwitchOff();
                }
                else if (s_Off)
                {
                    Restore();
                }
            }
            catch (Exception e)
            {
                s_Failed = true;
                Plugin.Log.LogWarning($"NGUI pad input: left to the game ({e.Message})");
                try { Restore(); } catch (Exception) { }
            }
        }

        /// <summary>Gives NGUI its own pad handling back (keyboard / mouse, plugin unload or failure).</summary>
        public static void Restore()
        {
            foreach (KeyValuePair<UICamera, Original> kv in s_Originals)
            {
                if (kv.Key == null)
                {
                    continue;
                }
                kv.Key.submitKey1 = kv.Value.Submit;
                kv.Key.cancelKey1 = kv.Value.Cancel;
                kv.Key.useController = kv.Value.Controller;
            }
            if (s_Off)
            {
                Plugin.Log.LogInfo("NGUI pad input: given back to the game");
            }
            s_Originals.Clear();
            s_Off = false;
        }

        private static void SwitchOff()
        {
            if (!(s_List?.GetValue(null) is List<UICamera> cameras))
            {
                return;
            }
            bool changed = false;
            foreach (UICamera cam in cameras)
            {
                if (cam == null)
                {
                    continue;
                }
                if (!s_Originals.ContainsKey(cam))
                {
                    // A UICamera seen for the first time (start-up, a new scene): keep the game's values.
                    s_Originals[cam] = new Original { Submit = cam.submitKey1, Cancel = cam.cancelKey1, Controller = cam.useController };
                }
                if (cam.submitKey1 != KeyCode.None || cam.cancelKey1 != KeyCode.None || cam.useController)
                {
                    cam.submitKey1 = KeyCode.None;
                    cam.cancelKey1 = KeyCode.None;
                    cam.useController = false;
                    changed = true;
                }
            }
            if (changed && !s_Off)
            {
                Plugin.Log.LogInfo($"NGUI pad input: switched off on {cameras.Count} UI camera(s) while the controller is in use");
            }
            s_Off = true;
        }
    }
}
