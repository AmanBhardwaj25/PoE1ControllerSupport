using System;
using HarmonyLib;
using UnityEngine;

namespace PoEController.Patches
{
    /// <summary>
    /// Controller rumble, the way the console version does it: the only place it vibrates is the camera shake
    /// (<c>CameraControl.ScreenShake(duration, strength)</c>), with the shake's own strength as motor strength and its
    /// duration as length, and only when the game accepts the shake (a weaker one during a stronger one is ignored).
    /// The game shakes the camera from the Scripts.ScreenShake script command (scripted scenes and conversations),
    /// attacks whose data asks for it, critical-hit kills, animation events (stomps, slams) and two cutscenes, so
    /// the rumble follows exactly those. Config Input.Vibration and Input.VibrationStrength (a multiplier).
    /// </summary>
    [Compat.Feature(Compat.Features.Rumble)]
    internal static class RumblePatches
    {
        public static bool Enabled = true;
        public static float StrengthMultiplier = 1f;

        [HarmonyPatch(typeof(CameraControl), nameof(CameraControl.ScreenShake), typeof(float), typeof(float))]
        private static class Shake
        {
            private static void Postfix(CameraControl __instance, float duration, float strength)
            {
                try
                {
                    Plugin plugin = Plugin.Instance;
                    if (!Enabled || plugin == null || plugin.Mode == null || plugin.Mode.Active != Input.InputDevice.Gamepad
                        || plugin.Pad == null || duration <= 0f || strength <= 0f)
                    {
                        return;
                    }
                    // Accepted = the game now runs exactly this shake (it keeps a stronger one that is still running).
                    if (!Compat.GameRef.TryGet(__instance, "m_screenShakeStrength", out float current) || current != strength)
                    {
                        return;
                    }
                    float motor = Mathf.Clamp01(strength * StrengthMultiplier);
                    if (motor <= 0f)
                    {
                        return;
                    }
                    plugin.Pad.Rumble(motor, motor, duration);
                    Plugin.Log.LogInfo($"Rumble: strength {strength:0.###} (motor {motor:0.###}) for {duration:0.##} s");
                }
                catch (Exception e)
                {
                    Compat.Features.Off(Compat.Features.Rumble, e.Message);
                    Enabled = false;
                }
            }
        }
    }
}
