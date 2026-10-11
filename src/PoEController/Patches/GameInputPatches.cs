using HarmonyLib;
using PoEController.Input;

namespace PoEController.Patches
{
    /// <summary>
    /// Postfixes on GameInput's MappedControl queries: if the keyboard didn't trigger the action, report whether the
    /// controller did. Only the MappedControl overloads are patched (they loop over bindings, so Mono won't inline
    /// them); the tiny one-argument wrappers forward to these.
    /// </summary>
    [HarmonyPatch]
    [Compat.Feature(Compat.Features.GameActions)]
    internal static class GameInputPatches
    {
        private static bool Allowed => !GameInput.DisableInput;

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControlUp), new[] { typeof(MappedControl), typeof(bool) })]
        [HarmonyPostfix]
        private static void ControlUp(MappedControl control, bool handle, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.Up(control, handle);
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControlDown), new[] { typeof(MappedControl), typeof(bool) })]
        [HarmonyPostfix]
        private static void ControlDown(MappedControl control, bool handle, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.Down(control, handle);
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControlDownWithRepeat), new[] { typeof(MappedControl), typeof(bool) })]
        [HarmonyPostfix]
        private static void ControlDownWithRepeat(MappedControl control, bool handle, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.DownWithRepeat(control, handle);
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControlUpWithoutModifiers), new[] { typeof(MappedControl) })]
        [HarmonyPostfix]
        private static void ControlUpNoMods(MappedControl control, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.Up(control, handle: false);
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControlDownWithoutModifiers), new[] { typeof(MappedControl) })]
        [HarmonyPostfix]
        private static void ControlDownNoMods(MappedControl control, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.Down(control, handle: false);
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControlDownWithoutModifiersWithRepeat), new[] { typeof(MappedControl) })]
        [HarmonyPostfix]
        private static void ControlDownNoModsRepeat(MappedControl control, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.DownWithRepeat(control, handle: false);
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControlHeld), new[] { typeof(MappedControl) })]
        [HarmonyPostfix]
        private static void ControlHeld(MappedControl control, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.HeldLong(control);
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControl), new[] { typeof(MappedControl) })]
        [HarmonyPostfix]
        private static void Control(MappedControl control, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.Held(control);
            }
        }

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetControl), new[] { typeof(MappedControl), typeof(bool), typeof(bool) })]
        [HarmonyPostfix]
        private static void ControlEx(MappedControl control, ref bool __result)
        {
            if (!__result && Allowed)
            {
                __result = VirtualControls.Held(control);
            }
        }
    }
}
