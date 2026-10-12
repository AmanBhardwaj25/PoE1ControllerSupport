using System;
using System.Collections.Generic;

namespace PoEController.Compat
{
    /// <summary>
    /// The mod's parts that can fail on their own. If one breaks — a game member missing at startup
    /// (<see cref="CompatibilityCheck"/>), a patch that can't be applied, or an exception while running — only that
    /// part turns off (one log line saying why) and everything else keeps working. Call sites run feature code through
    /// <see cref="Run"/> so even an error while the code is being compiled (a renamed game member) is caught there:
    /// the referencing code must sit in the lambda / a separate method, not in the caller itself.
    /// </summary>
    internal static class Features
    {
        // Core: without these the controller can't do much, but the rest still runs.
        public const string GameActions = "Game actions (pad presses the game's controls)";
        public const string ScreenTracking = "Screen tracking (menus)";
        public const string Camera = "Right-stick camera";
        // Extras.
        public const string Keyboard = "On-screen keyboard";
        public const string TextGlyphs = "Glyphs in game text";
        public const string ButtonGlyphs = "Glyphs on game buttons";
        public const string QuickTooltips = "Quick tooltips";
        public const string LinkStops = "Highlighted-word stops";
        public const string Tutorials = "Tutorial handling";
        public const string ItemMenu = "Inventory item menu";
        public const string InspectActions = "Item card buttons";
        public const string Compare = "Item comparison";
        public const string Settings = "Controller settings panel";
        public const string HiddenCursorHover = "No hover from the hidden cursor";
        public const string StopParty = "B stops the party (Console style)";
        public const string GeneralRadial = "General Radial on LT (Console style)";
        public const string CombatRadial = "Combat Radial on RT (Console style)";
        public const string ConsoleHud = "Trimmed HUD (Console style)";
        public const string AimMarkers = "Aiming cone (Console style)";
        public const string PartyRadial = "Party ring on LB / RB hold (Console style)";
        public const string ConsoleMenus = "Console-style menu buttons (inventory, loot)";
        public const string ActiveEffects = "Active Effects panel (Console style)";
        public const string CombatLog = "Readable combat log (Console style)";
        public const string WorldMap = "World map stepping (Console style)";
        public const string FocusFrameRule = "No focus frame on game buttons (Console style)";
        public const string LinkColors = "Brighter tooltip words";
        public const string ConversationScroll = "Scroll conversations with RS";
        public const string SlotCount = "Stack count out from under the cursor (Console style)";

        private static readonly Dictionary<string, string> s_Off = new Dictionary<string, string>();

        public static bool On(string feature) => !s_Off.ContainsKey(feature);

        /// <summary>Turns <paramref name="feature"/> off (logged once).</summary>
        public static void Off(string feature, string reason)
        {
            if (s_Off.ContainsKey(feature))
            {
                return;
            }
            s_Off[feature] = reason;
            Plugin.Log.LogWarning($"Feature off: {feature} — {reason}. Everything else keeps working.");
        }

        /// <summary>Runs <paramref name="action"/> if the feature is on; any exception turns the feature off.</summary>
        public static void Run(string feature, Action action)
        {
            if (!On(feature))
            {
                return;
            }
            try
            {
                action();
            }
            catch (Exception e)
            {
                Off(feature, "error: " + e);
            }
        }

        /// <summary>As <see cref="Run"/>, with a result (<paramref name="fallback"/> when off or failed).</summary>
        public static T Get<T>(string feature, Func<T> func, T fallback)
        {
            if (!On(feature))
            {
                return fallback;
            }
            try
            {
                return func();
            }
            catch (Exception e)
            {
                Off(feature, "error: " + e);
                return fallback;
            }
        }

        /// <summary>For the startup report: every feature that is off, with why.</summary>
        public static IEnumerable<KeyValuePair<string, string>> OffList => s_Off;
    }

    /// <summary>Which <see cref="Features"/> entry a Harmony patch class belongs to (it turns off if the patch can't be applied).</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class FeatureAttribute : Attribute
    {
        public FeatureAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
