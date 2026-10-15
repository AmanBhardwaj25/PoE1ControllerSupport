using System;

namespace PoEController.World
{
    /// <summary>Turn-based questions asked every frame, made safe to ask at any moment.</summary>
    internal static class TurnChecks
    {
        /// <summary>
        /// The game's own "may this character delay their turn" (what enables the HUD's Delay button). The game's check
        /// reads the character's current AI state without a null check, and a character between states has none (an
        /// empty state stack, e.g. right as a turn changes hands). The HUD button only logs that and carries on; asked
        /// from here it must not throw, so those moments simply read as "can't delay".
        /// </summary>
        public static bool CanDelayTurn(AIController actor)
        {
            TacticalModeManager tm = TacticalModeManager.Instance;
            if (actor == null || tm == null || actor.StateManager == null || actor.StateManager.CurrentState == null || actor.CharacterStats == null)
            {
                return false;
            }
            try
            {
                return tm.GetCanDelayTurn(actor);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
