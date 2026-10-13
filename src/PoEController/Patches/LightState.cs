using PoEController.Input;
using UnityEngine;

namespace PoEController.Patches
{
    /// <summary>
    /// What the controller lights rest on, read from the game every frame (the flashes of hits and spells are played
    /// from <see cref="RumblePatches"/>): out of a fight a dim, calm gold; in a fight a brighter gold; turn-based, the
    /// party's turn a steady gold with the player LEDs showing the acting party slot (1-5), an enemy's turn a dim red
    /// breathing with the player LEDs off; low endurance (<see cref="Focus"/>: the acting party member, else the main
    /// character) adds a red heartbeat.
    /// </summary>
    internal static class LightState
    {
        private static readonly Color Calm = new Color(0.78f, 0.56f, 0.25f);
        private static readonly Color Gold = new Color(1f, 0.69f, 0.25f);
        private static readonly Color Enemy = new Color(1f, 0.12f, 0.06f);
        /// <summary>Endurance share under which the heartbeat shows (in a fight), for <see cref="Focus"/>.</summary>
        private const float LowEndurance = 0.3f;

        public static void Update()
        {
            bool inCombat = !GameState.IsLoading && GameState.InCombat;
            float share = EnduranceShare(Focus());
            LightPlayer.LowEndurance = inCombat && share > 0f && share < LowEndurance;
            if (!inCombat)
            {
                Set(Calm, 0.22f, 0f, 1);
                return;
            }
            TacticalModeManager tm = TacticalModeManager.Instance;
            if (tm == null || !TacticalModeManager.IsInTacticalCombat())
            {
                Set(Gold, 0.45f, 0f, 1);
                return;
            }
            if (!(tm.CurrentEvent is TacticalTurnEvent turn) || turn.Actor == null)
            {
                return; // between turns: keep the last look
            }
            if (turn.Actor is PartyMemberAI member)
            {
                Set(Gold, 0.6f, 0f, Mathf.Clamp(member.Slot + 1, 1, 5));
            }
            else
            {
                Set(Enemy, 0.3f, 0.5f, 0);
            }
        }

        private static void Set(Color color, float level, float breath, int playerLights)
        {
            LightPlayer.BaseColor = color;
            LightPlayer.BaseLevel = level;
            LightPlayer.BaseBreath = breath;
            LightPlayer.PlayerLights = playerLights;
        }

        /// <summary>
        /// Whose state the lights and triggers show: turn-based, the party member whose turn it is; otherwise (enemy
        /// turns, real time) the main character.
        /// </summary>
        internal static GameObject Focus()
        {
            TacticalModeManager tm = TacticalModeManager.Instance;
            if (tm != null && TacticalModeManager.IsInTacticalCombat() && tm.CurrentEvent is TacticalTurnEvent turn
                && turn.Actor is PartyMemberAI member && member != null)
            {
                return member.gameObject;
            }
            return GameState.s_playerCharacter != null ? GameState.s_playerCharacter.gameObject : null;
        }

        /// <summary>Endurance as a share of its maximum (0 = unknown or down).</summary>
        internal static float EnduranceShare(GameObject character)
        {
            Health h = character != null ? character.GetComponent<Health>() : null;
            return h != null && h.MaxStamina > 0f && h.CurrentStamina > 0f ? h.CurrentStamina / h.MaxStamina : 0f;
        }
    }
}
