using System;
using HarmonyLib;
using UnityEngine;

namespace PoEController.Patches
{
    /// <summary>Config Input.Vibration: how much the controller vibrates.</summary>
    public enum VibrationMode
    {
        /// <summary>Never.</summary>
        Off,
        /// <summary>Exactly the console version: only when the game shakes the camera.</summary>
        Essential,
        /// <summary>Essential plus party damage, critical hits, knock-outs and deaths.</summary>
        Extended,
    }

    /// <summary>
    /// Controller rumble. <b>Essential</b> is the console version's: the only place it vibrates is the camera shake
    /// (<c>CameraControl.ScreenShake(duration, strength)</c>), with the shake's own strength as motor strength and its
    /// duration as length, and only when the game accepts the shake (a weaker one during a stronger one is ignored).
    /// The game shakes the camera from the Scripts.ScreenShake script command (scripted scenes and conversations),
    /// attacks whose data asks for it, critical-hit kills, animation events (stomps, slams) and two cutscenes.
    /// <b>Extended</b> adds party damage (main character medium, companions light), critical hits dealt or taken
    /// (heavy), knock-outs and deaths, using the console version's own presets (strength 1/5, 1/3, 1; length 1/10,
    /// 1/5, 1/3 s). A weaker rumble never cuts off a stronger one still running. Misses and damage over time stay
    /// quiet. Config Input.Vibration and Input.VibrationStrength (a multiplier).
    /// </summary>
    [Compat.Feature(Compat.Features.Rumble)]
    internal static class RumblePatches
    {
        public static VibrationMode Mode = VibrationMode.Essential;
        public static float StrengthMultiplier = 1f;

        // The console version's presets (ControllerEffectsManager.VibrationIntensity / VibrationDuration).
        private const float Light = 1f / 5f;
        private const float Medium = 1f / 3f;
        private const float Heavy = 1f;
        private const float Short = 1f / 10f;
        private const float MediumLength = 1f / 5f;
        private const float Long = 1f / 3f;

        private static float s_Strength;
        private static float s_Until;

        private static bool OnPad
        {
            get
            {
                Plugin plugin = Plugin.Instance;
                return plugin != null && plugin.Mode != null && plugin.Mode.Active == Input.InputDevice.Gamepad && plugin.Pad != null;
            }
        }

        /// <summary>Starts a rumble unless a stronger one is still running.</summary>
        private static void Request(float strength, float seconds, string reason)
        {
            if (Mode == VibrationMode.Off || !OnPad || strength <= 0f || seconds <= 0f)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (now < s_Until && strength < s_Strength)
            {
                return;
            }
            float motor = Mathf.Clamp01(strength * StrengthMultiplier);
            if (motor <= 0f)
            {
                return;
            }
            s_Strength = strength;
            s_Until = now + seconds;
            Plugin.Instance.Pad.Rumble(motor, motor, seconds);
            Plugin.Log.LogInfo($"Rumble ({reason}): strength {strength:0.###} (motor {motor:0.###}) for {seconds:0.##} s");
        }

        private static void Fail(Exception e)
        {
            Compat.Features.Off(Compat.Features.Rumble, e.Message);
            Mode = VibrationMode.Off;
        }

        [HarmonyPatch(typeof(CameraControl), nameof(CameraControl.ScreenShake), typeof(float), typeof(float))]
        private static class Shake
        {
            private static void Postfix(CameraControl __instance, float duration, float strength)
            {
                try
                {
                    if (Mode == VibrationMode.Off)
                    {
                        return;
                    }
                    // Accepted = the game now runs exactly this shake (it keeps a stronger one that is still running).
                    if (Compat.GameRef.TryGet(__instance, "m_screenShakeStrength", out float current) && current == strength)
                    {
                        Request(strength, duration, "camera shake");
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        /// <summary>Extended: every attack hit (Health.DoDamage returns the damage done).</summary>
        [HarmonyPatch(typeof(Health), nameof(Health.DoDamage), typeof(DamageInfo), typeof(GameObject))]
        private static class Hit
        {
            private static void Postfix(Health __instance, DamageInfo damage, GameObject attacker, float __result)
            {
                try
                {
                    if (Mode != VibrationMode.Extended || damage == null || damage.IsMiss || __result <= 0f || __instance == null)
                    {
                        return;
                    }
                    GameObject target = __instance.gameObject;
                    bool targetInParty = PartyHelper.IsPartyMember(target);
                    bool attackerInParty = attacker != null && PartyHelper.IsPartyMember(attacker);
                    if (damage.IsCriticalHit && (targetInParty || attackerInParty))
                    {
                        Request(Heavy, Long, targetInParty ? "party critically hit" : "party critical hit");
                    }
                    else if (targetInParty)
                    {
                        bool main = GameState.s_playerCharacter != null && target == GameState.s_playerCharacter.gameObject;
                        if (main)
                        {
                            Request(Medium, MediumLength, "main character hit");
                        }
                        else
                        {
                            Request(Light, Short, "companion hit");
                        }
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        [HarmonyPatch(typeof(Health), nameof(Health.HandleUnconscious), typeof(GameObject))]
        private static class KnockedOut
        {
            private static void Postfix(Health __instance)
            {
                try
                {
                    if (Mode == VibrationMode.Extended && __instance != null && PartyHelper.IsPartyMember(__instance.gameObject))
                    {
                        Request(Heavy, 0.5f, "party member knocked out");
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        [HarmonyPatch(typeof(Health), nameof(Health.HandleDeath), typeof(GameObject))]
        private static class Died
        {
            private static void Postfix(Health __instance)
            {
                try
                {
                    if (Mode == VibrationMode.Extended && __instance != null && PartyHelper.IsPartyMember(__instance.gameObject))
                    {
                        Request(Heavy, 0.8f, "party member died");
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }
    }
}
