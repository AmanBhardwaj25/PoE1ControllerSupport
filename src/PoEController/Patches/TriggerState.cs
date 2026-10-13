using System.Collections.Generic;
using PoEController.Input;
using UnityEngine;

namespace PoEController.Patches
{
    /// <summary>
    /// The DualSense adaptive triggers, read from the game every frame. Four feels, so each one is easy to tell apart:
    /// <list type="bullet">
    /// <item><b>Ready</b>: resistance along the whole press, shaped by the weapon in hand.</item>
    /// <item><b>Strained</b>: debuffs (the red status icons) or low endurance / health: heavy, heavier the worse.</item>
    /// <item><b>Empowered</b>: buffs and nothing straining: a light hum while pressed.</item>
    /// <item><b>Locked</b>: stunned, paralyzed, petrified, prone, stuck: rigid.</item>
    /// </list>
    /// In a turn-based fight only RT counts (it opens the Combat Radial, once a turn): the acting party member's
    /// feel, slack once the action is used, a vibration while a long cast is prepared (frequency by spell bucket),
    /// slack while charmed / dominated. LT and enemy turns / real-time fights: free. Out of combat both triggers
    /// (LT opens the menus, the inventory) show the worst-off party member: strained (debuffs, health under half),
    /// else empowered (a buff still running), else free.
    /// </summary>
    internal static class TriggerState
    {
        private const float StrainBelow = 0.5f;

        private static string s_LastState;
        private static readonly HashSet<string> s_SeenAfflictions = new HashSet<string>();

        private enum Hold
        {
            None,
            Locked,
            Slack,
        }

        public static void Update()
        {
            byte[] right = null;
            byte[] left = null;
            string what = "free";
            GameObject who = null;
            bool inCombat = !GameState.IsLoading && GameState.InCombat;
            TacticalModeManager tm = TacticalModeManager.Instance;
            if (inCombat)
            {
                if (tm != null && TacticalModeManager.IsInTacticalCombat() && tm.CurrentEvent is TacticalTurnEvent turn
                    && turn.Actor is PartyMemberAI member && member != null)
                {
                    who = member.gameObject;
                    right = TurnFeel(member, out what);
                }
            }
            else
            {
                who = WorstOff(out int strain, out bool buffed);
                if (strain > 0)
                {
                    right = left = Strained(strain);
                    what = $"strained {strain} (both)";
                }
                else if (buffed)
                {
                    right = left = Empowered();
                    what = "empowered (both)";
                }
            }
            LightPlayer.RightTrigger = right;
            LightPlayer.LeftTrigger = left;
            string state = $"{(who != null ? who.name : "-")}: {what}";
            if (state != s_LastState)
            {
                s_LastState = state;
                Plugin.Log.LogInfo("Triggers: " + state);
            }
        }

        /// <summary>RT on a party member's turn.</summary>
        private static byte[] TurnFeel(PartyMemberAI member, out string what)
        {
            GameObject go = member.gameObject;
            switch (HoldOf(go))
            {
                case Hold.Locked:
                    what = "RT locked";
                    return DualSenseTriggers.Feedback(0, 8);
                case Hold.Slack:
                    what = "RT slack (not in control)";
                    return null;
            }
            if (!member.AvailableAction)
            {
                what = "RT slack (action used)";
                return null;
            }
            if (RumblePatches.Preparing != null && RumblePatches.Preparing == member)
            {
                int hz = Frequency(RumblePatches.PrepareBucket);
                what = $"RT preparing {RumblePatches.PrepareBucket} ({hz} Hz)";
                return DualSenseTriggers.Vibration(2, 5, hz);
            }
            int strain = Strain(go, true, out bool buffed);
            if (strain > 0)
            {
                what = $"RT strained {strain}";
                return Strained(strain);
            }
            if (buffed)
            {
                what = "RT empowered";
                return Empowered();
            }
            byte[] ready = WeaponFeel(go, out string weapon);
            what = "RT ready, " + weapon;
            return ready;
        }

        /// <summary>Heavy along the whole press: strain 1..4 = strength 5..8.</summary>
        private static byte[] Strained(int strain)
        {
            int s = Mathf.Clamp(4 + strain, 5, 8);
            return DualSenseTriggers.Ramp(s, s);
        }

        /// <summary>A light hum while pressed.</summary>
        private static byte[] Empowered() => DualSenseTriggers.Vibration(1, 2, 60);

        /// <summary>
        /// 0 = fine, 1..4 = how strained: one point per visible debuff (max 2), plus endurance (in a fight) or health
        /// under half (+1) and under a quarter (+2). <paramref name="buffed"/> = a visible buff is running.
        /// </summary>
        private static int Strain(GameObject character, bool inCombat, out bool buffed)
        {
            buffed = false;
            CharacterStats stats = character != null ? character.GetComponent<CharacterStats>() : null;
            if (stats == null)
            {
                return 0;
            }
            int debuffs = 0;
            foreach (StatusEffect e in stats.ActiveStatusEffects)
            {
                if (!Counts(e))
                {
                    continue;
                }
                if (e.Params.IsHostile)
                {
                    debuffs++;
                }
                else
                {
                    buffed = true;
                }
            }
            int strain = Mathf.Min(2, debuffs);
            Health h = character.GetComponent<Health>();
            if (h != null)
            {
                float share = inCombat
                    ? (h.MaxStamina > 0f ? h.CurrentStamina / h.MaxStamina : 1f)
                    : (h.MaxHealth > 0f ? h.CurrentHealth / h.MaxHealth : 1f);
                if (share < StrainBelow)
                {
                    strain += share < StrainBelow / 2f ? 2 : 1;
                }
            }
            return Mathf.Min(4, strain);
        }

        /// <summary>
        /// A buff / debuff the player would recognise: shown in the UI, does something, not from a passive ability or
        /// an aura (those are always on), and temporary (timed, or until the fight ends or the next rest).
        /// </summary>
        private static bool Counts(StatusEffect e)
        {
            if (e == null || !e.Applied || e.Params == null || e.Params.HideFromUi || e.IsFromAura
                || e.Params.AffectsStat == StatusEffect.ModifiedStat.NoEffect)
            {
                return false;
            }
            if (e.AfflictionOrigin != null && e.AfflictionOrigin.HideFromUI)
            {
                return false;
            }
            if (e.AbilityOrigin != null && e.AbilityOrigin.Passive)
            {
                return false;
            }
            return e.Duration > 0f || e.LastsUntilCombatEnds || e.LastsUntilRest;
        }

        /// <summary>Out of combat: the party member with the most strain (ties: the first), and whether anyone has a buff.</summary>
        private static GameObject WorstOff(out int strain, out bool buffed)
        {
            strain = 0;
            buffed = false;
            GameObject worst = null;
            foreach (PartyMemberAI p in PartyMemberAI.OnlyPrimaryPartyMembers)
            {
                if (p == null)
                {
                    continue;
                }
                int s = Strain(p.gameObject, false, out bool b);
                buffed |= b;
                if (s > strain)
                {
                    strain = s;
                    worst = p.gameObject;
                }
            }
            return worst;
        }

        /// <summary>
        /// RT for the weapon the character attacks with: resistance along the whole press, shaped per weapon.
        /// <paramref name="what"/> names it for the log.
        /// </summary>
        private static byte[] WeaponFeel(GameObject character, out string what)
        {
            Equipment equipment = character != null ? character.GetComponent<Equipment>() : null;
            AttackBase attack = equipment != null ? equipment.PrimaryAttack : null;
            if (attack is AttackFirearm firearm && firearm.RequiresReload)
            {
                what = "reloading (rigid)";
                return DualSenseTriggers.Feedback(0, 8);
            }
            Weapon weapon = attack != null ? attack.GetComponent<Weapon>() : null;
            switch (weapon != null ? weapon.WeaponType : WeaponSpecializationData.WeaponType.Unarmed)
            {
                case WeaponSpecializationData.WeaponType.HuntingBow:
                case WeaponSpecializationData.WeaponType.WarBow:
                    what = "bow (harder the further you pull)";
                    return DualSenseTriggers.Ramp(1, 7);
                case WeaponSpecializationData.WeaponType.Pistol:
                case WeaponSpecializationData.WeaponType.Arquebus:
                case WeaponSpecializationData.WeaponType.Blunderbuss:
                    what = "firearm (stiff)";
                    return DualSenseTriggers.Ramp(6, 6);
                case WeaponSpecializationData.WeaponType.Crossbow:
                case WeaponSpecializationData.WeaponType.Arbalest:
                    what = "crossbow (stiff, harder at the bottom)";
                    return DualSenseTriggers.Ramp(5, 7);
                case WeaponSpecializationData.WeaponType.Dagger:
                case WeaponSpecializationData.WeaponType.Stiletto:
                    what = "dagger (light)";
                    return DualSenseTriggers.Ramp(2, 2);
                case WeaponSpecializationData.WeaponType.Wand:
                case WeaponSpecializationData.WeaponType.Rod:
                case WeaponSpecializationData.WeaponType.Sceptre:
                case WeaponSpecializationData.WeaponType.Unarmed:
                    what = weapon == null ? "no weapon (medium)" : "implement (medium)";
                    return DualSenseTriggers.Ramp(4, 4);
            }
            if (equipment.TwoHandedWeapon)
            {
                what = "two-handed (heavy)";
                return DualSenseTriggers.Ramp(6, 7);
            }
            what = "one-handed (medium)";
            return DualSenseTriggers.Ramp(4, 4);
        }

        /// <summary>Afflictions that take the character out of your hands: locked (can't act) or slack (not in control).</summary>
        private static Hold HoldOf(GameObject character)
        {
            CharacterStats stats = character != null ? character.GetComponent<CharacterStats>() : null;
            if (stats == null)
            {
                return Hold.None;
            }
            if (stats.HasStatusEffectOfType(StatusEffect.ModifiedStat.Stunned) || stats.HasStatusEffectOfType(StatusEffect.ModifiedStat.KnockedDown)
                || Has(stats, AfflictionData.Paralyzed) || Has(stats, AfflictionData.Petrified))
            {
                return Hold.Locked;
            }
            if (Has(stats, AfflictionData.Charmed) || Has(stats, AfflictionData.Dominated))
            {
                return Hold.Slack;
            }
            foreach (StatusEffect e in stats.ActiveStatusEffects)
            {
                global::Affliction a = e != null && e.Applied ? e.AfflictionOrigin : null;
                if (a == null)
                {
                    continue;
                }
                string key = (a.Tag ?? "") + " " + a.name;
                if (s_SeenAfflictions.Add(key))
                {
                    Plugin.Log.LogInfo($"Triggers: affliction seen on {character.name}: tag '{a.Tag}', object '{a.name}'");
                }
                string k = key.ToLowerInvariant();
                if (k.Contains("stuck") || k.Contains("paralyz") || k.Contains("petrif") || k.Contains("stun") || k.Contains("prone"))
                {
                    return Hold.Locked;
                }
                if (k.Contains("charm") || k.Contains("dominat") || k.Contains("confus"))
                {
                    return Hold.Slack;
                }
            }
            return Hold.None;
        }

        private static bool Has(CharacterStats stats, global::Affliction affliction) =>
            affliction != null && stats.HasStatusEffectFromAffliction(affliction);

        /// <summary>Trigger vibration (Hz) while a spell of this bucket is prepared.</summary>
        private static int Frequency(string bucket)
        {
            switch (bucket)
            {
                case "lightning": return 45;
                case "beams": return 40;
                case "frost_burst":
                case "frost_lingering": return 32;
                case "acid_decay": return 25;
                case "fire_blast":
                case "fire_stream":
                case "fire_lingering": return 18;
                case "radiant_holy": return 14;
                case "force_kinetic": return 12;
                case "chanter": return 10;
                case "curses_debuffs": return 9;
                case "earth_knockdown": return 8;
                case "summoning": return 7;
                case "mind": return 6;
                case "healing_buffs": return 5;
                default: return 10;
            }
        }
    }
}
