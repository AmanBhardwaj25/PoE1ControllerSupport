using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// A dropdown list that is open (options resolution / language, inventory sort, rest bonus, AI script, auto
    /// attack...). With the mouse, a click anywhere closes it; on the controller, B closes it before B means "close the
    /// window". Closed with the dropdown's own box click handler (the call its arrow / box makes).
    /// </summary>
    internal static class OpenDropdowns
    {
        private const float RecheckSeconds = 0.2f;

        private static readonly FieldInfo s_Open = AccessTools.Field(typeof(UIDropdownMenu), "m_DropdownOpen");
        private static readonly MethodInfo s_Toggle = AccessTools.Method(typeof(UIDropdownMenu), "OnBoxClicked");

        private static UIDropdownMenu s_Cached;
        private static float s_CheckedAt = -100f;

        /// <summary>An open dropdown list in a visible window, or null. Looked up a few times a second, not every frame.</summary>
        public static UIDropdownMenu Find(bool fresh = false)
        {
            if (!Compat.Features.On(Compat.Features.MenuBack) || s_Open == null || s_Toggle == null)
            {
                return null;
            }
            if (!fresh && Time.unscaledTime - s_CheckedAt < RecheckSeconds)
            {
                return s_Cached != null && IsOpen(s_Cached) ? s_Cached : null;
            }
            s_CheckedAt = Time.unscaledTime;
            s_Cached = Compat.Features.Get(Compat.Features.MenuBack, () =>
            {
                foreach (UIDropdownMenu d in UnityEngine.Object.FindObjectsOfType<UIDropdownMenu>())
                {
                    if (IsOpen(d))
                    {
                        return d;
                    }
                }
                return null;
            }, null);
            return s_Cached;
        }

        private static bool IsOpen(UIDropdownMenu d) => d != null && d.isActiveAndEnabled && (bool)s_Open.GetValue(d);

        /// <summary>B: closes an open list. True = B was used here (the caller keeps it from the window).</summary>
        public static bool CloseOne()
        {
            UIDropdownMenu d = Find(fresh: true);
            if (d == null)
            {
                return false;
            }
            Compat.Features.Run(Compat.Features.MenuBack, () => s_Toggle.Invoke(d, new object[] { d.gameObject }));
            s_Cached = null;
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Dropdown '{d.name}' closed (B)");
            return true;
        }
    }
}
