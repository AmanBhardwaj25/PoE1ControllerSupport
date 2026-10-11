using System;
using System.Collections.Generic;
using HarmonyLib;

namespace PoEController.Context
{
    /// <summary>
    /// Keeps a stack of open UIHudWindows in the order they were shown, so the topmost one decides the
    /// active control scheme. Fed by Harmony postfixes on UIWindowManager.WindowShown/WindowHidden.
    /// </summary>
    [Compat.Feature(Compat.Features.ScreenTracking)]
    public static class ScreenTracker
    {
        private static readonly List<UIHudWindow> s_Stack = new List<UIHudWindow>();

        /// <summary>Raised with the new top window (null = in-world, no window).</summary>
        public static event Action<UIHudWindow> TopChanged;

        public static UIHudWindow Top
        {
            get
            {
                Prune();
                return s_Stack.Count > 0 ? s_Stack[s_Stack.Count - 1] : null;
            }
        }

        public static IReadOnlyList<UIHudWindow> Stack
        {
            get
            {
                Prune();
                return s_Stack;
            }
        }

        private static void OnShown(UIHudWindow window)
        {
            UIHudWindow before = Top;
            s_Stack.Remove(window);
            s_Stack.Add(window);
            RaiseIfChanged(before);
        }

        private static void OnHidden(UIHudWindow window)
        {
            UIHudWindow before = Top;
            s_Stack.Remove(window);
            RaiseIfChanged(before);
        }

        private static void RaiseIfChanged(UIHudWindow before)
        {
            UIHudWindow after = Top;
            if (before == after)
            {
                return;
            }
            try
            {
                TopChanged?.Invoke(after);
            }
            catch (Exception e)
            {
                // Runs inside the game's window code; an exception here must not break showing/hiding windows.
                Plugin.Log.LogError($"Screen change handler failed: {e}");
            }
        }

        private static void Prune()
        {
            // Windows are destroyed on level unload without a WindowHidden call.
            for (int i = s_Stack.Count - 1; i >= 0; i--)
            {
                if (s_Stack[i] == null || !s_Stack[i].WindowActive())
                {
                    s_Stack.RemoveAt(i);
                }
            }
        }

        [HarmonyPatch(typeof(UIWindowManager), nameof(UIWindowManager.WindowShown))]
        private static class WindowShownPatch
        {
            private static void Postfix(UIHudWindow window) => OnShown(window);
        }

        [HarmonyPatch(typeof(UIWindowManager), nameof(UIWindowManager.WindowHidden))]
        private static class WindowHiddenPatch
        {
            private static void Postfix(UIHudWindow window) => OnHidden(window);
        }
    }
}
