using HarmonyLib;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// The A target's name card: when the pad's in-world target (the A "Talk" / "Attack" prompt) is a character,
    /// the game's own hover card shows over them, the one a mouse player gets by resting the cursor on a character
    /// (<c>UIMapTooltipManager.Show</c>: name and details, the same rules for who gets one). On the pad the mod hides
    /// and parks the cursor, so the game never sees a character under it and never shows that card by itself.
    /// The game takes a card down every frame unless the mouse is on that character; a prefix on its Hide keeps the
    /// A target's card up while it stays the target. Once it isn't, the game removes the card as usual.
    /// </summary>
    internal static class TargetNameTip
    {
        private const float RefreshSeconds = 0.25f;

        /// <summary>The character whose card is kept up, or null.</summary>
        private static GameObject s_Shown;
        /// <summary>The card the game returned for <see cref="s_Shown"/>.</summary>
        private static UIMapTooltip s_Card;
        private static float s_Refresh;

        /// <summary>
        /// Once per frame. <paramref name="targeting"/> = the world scheme's A targeting while the pad plays the world
        /// with the cursor hidden; null otherwise (the card goes, and the mouse's own hover takes over).
        /// </summary>
        public static void Update(InteractionTargeting targeting, float dt)
        {
            if (!Compat.Features.On(Compat.Features.TargetName))
            {
                s_Shown = null;
                return;
            }
            GameObject who = Compat.Features.Get(Compat.Features.TargetName, () => CardFor(targeting), null);
            if (who == null)
            {
                s_Shown = null;
                s_Card = null;
                return;
            }
            s_Refresh -= dt;
            if (who != s_Shown || s_Refresh <= 0f)
            {
                s_Shown = who;
                s_Refresh = RefreshSeconds;
                s_Card = Compat.Features.Get(Compat.Features.TargetName, () => UIMapTooltipManager.Instance.Show(who, byMouse: true, byAttack: false), null);
            }
        }

        /// <summary>
        /// Where the A target's card is on screen (GUI coordinates, origin top-left), so the A prompt can sit above it
        /// instead of on top of it. False when no card is up for <paramref name="target"/>.
        /// </summary>
        public static bool TryGetCardRect(GameObject target, out Rect rect)
        {
            rect = default;
            UIMapTooltip card = s_Card;
            if (target == null || target != s_Shown || card == null || !card.gameObject.activeInHierarchy || card.Target != target)
            {
                return false;
            }
            Camera ui = NGUITools.FindCameraForLayer(card.gameObject.layer);
            if (ui == null)
            {
                return false;
            }
            Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(card.transform);
            Vector3 min = ui.WorldToScreenPoint(b.min);
            Vector3 max = ui.WorldToScreenPoint(b.max);
            rect = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Screen.height - Mathf.Max(min.y, max.y),
                Mathf.Max(min.x, max.x), Screen.height - Mathf.Min(min.y, max.y));
            // Anything implausible (nothing measured, or a card the size of the screen): draw as before.
            return rect.width > 8f && rect.height > 8f && rect.width < Screen.width * 0.5f && rect.height < Screen.height * 0.5f;
        }

        /// <summary>The A target, if a mouse resting on it would get the hover card now (UIMapTooltipManager.Update's rules).</summary>
        private static GameObject CardFor(InteractionTargeting targeting)
        {
            InteractionTargeting.Candidate t = targeting?.Target;
            if (t == null || t.Go == null || targeting.Label == null || UIMapTooltipManager.Instance == null
                || InGameHUD.Instance == null || !InGameHUD.Instance.ShowHUD || !UIWindowManager.MouseInputAvailable)
            {
                return null;
            }
            // Talk: anyone you can speak to. Attack: a hostile out of combat (in combat the pointer does the aiming,
            // and the game's own hover card applies to it).
            if (t.Kind != InteractionTargeting.Kind.Talk && !(t.Kind == InteractionTargeting.Kind.Attack && !GameState.InCombat))
            {
                return null;
            }
            Faction faction = t.Go.GetComponent<Faction>();
            Health health = t.Go.GetComponent<Health>();
            if (faction == null || !faction.CanShowTooltip || health == null || health.ShowDead)
            {
                return null;
            }
            return t.Go;
        }

        [HarmonyPatch(typeof(UIMapTooltipManager), nameof(UIMapTooltipManager.Hide))]
        [Compat.Feature(Compat.Features.TargetName)]
        private static class KeepCardPatch
        {
            /// <summary>Skip hiding the A target's card (only that one, and only while it is the target).</summary>
            private static bool Prefix(GameObject target)
            {
                try
                {
                    return s_Shown == null || target != s_Shown;
                }
                catch (System.Exception)
                {
                    return true;
                }
            }
        }
    }
}
