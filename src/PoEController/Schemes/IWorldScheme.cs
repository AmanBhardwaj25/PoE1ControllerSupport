using PoEController.World;

namespace PoEController.Schemes
{
    /// <summary>
    /// What the rest of the mod needs from an in-world scheme (PC layout <see cref="WorldScheme"/>, Console style
    /// <see cref="ConsoleWorldScheme"/>): drawing the wheel / A prompt / party-select frame, hiding the cursor,
    /// whether B is free for dismissing a tutorial tip.
    /// </summary>
    internal interface IWorldScheme
    {
        InteractionTargeting Targeting { get; }
        PartySelect PartySelect { get; }
        AbilityWheel Wheel { get; }

        /// <summary>The controller is driving the visible world cursor (the game cursor must stay shown).</summary>
        bool CursorActive { get; }

        /// <summary>B has no other job right now (a timed tutorial tip may take it).</summary>
        bool EastIsFree { get; }
    }
}
