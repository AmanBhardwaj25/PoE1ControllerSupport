using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using PoEController.Schemes;
using UnityEngine;

namespace PoEController.Context
{
    /// <summary>
    /// Watches for the pad's controls not fitting the screen: the world's controls are running while one of the
    /// game's windows is open on screen (the mod lost track of it, so the pad drives the world behind the window and
    /// the window can't be used). Only reports it, in every build including release, so a player's log shows how
    /// often and where it happens; it does not try to recover (the player has the rescue cursor, hold View).
    /// </summary>
    internal static class StuckWatch
    {
        private const float CheckInterval = 0.25f;
        /// <summary>Must last this long before it is reported (windows open and close across a frame or two).</summary>
        private const float ReportAfter = 1f;
        /// <summary>Reports per session at most, so a case that repeats can't flood the log.</summary>
        private const int MaxReports = 20;

        private static readonly FieldInfo s_Windows = AccessTools.Field(typeof(UIWindowManager), "m_Windows");

        private static float s_CheckTimer;
        private static float s_StuckFor;
        private static bool s_Reported;
        private static string s_What;
        private static int s_Reports;
        private static bool s_Broken;

        /// <summary>Once per frame, after the router has picked the active scheme.</summary>
        public static void Update(ControlScheme active, float dt)
        {
            if (s_Broken)
            {
                return;
            }
            s_CheckTimer -= dt;
            if (s_CheckTimer > 0f)
            {
                if (s_StuckFor > 0f)
                {
                    s_StuckFor += dt;
                }
                return;
            }
            s_CheckTimer = CheckInterval;
            try
            {
                Check(active, dt);
            }
            catch (Exception e)
            {
                // A changed game: stop watching, never get in the way.
                s_Broken = true;
                Plugin.Log.Report($"Stuck-screen check off: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Check(ControlScheme active, float dt)
        {
            string open = active is IWorldScheme && !GameState.IsLoading ? OpenWindows() : null;
            if (open == null)
            {
                if (s_Reported)
                {
                    Plugin.Log.Report($"Stuck screen cleared after {s_StuckFor:0.0} s (was: {s_What}); controls now '{active?.Name ?? "none"}'");
                }
                s_StuckFor = 0f;
                s_Reported = false;
                return;
            }
            if (s_StuckFor <= 0f || open != s_What)
            {
                s_What = open;
                s_StuckFor = dt;
                s_Reported = false;
                return;
            }
            if (!s_Reported && s_StuckFor >= ReportAfter && s_Reports < MaxReports)
            {
                s_Reported = true;
                s_Reports++;
                var tracked = new List<string>();
                foreach (UIHudWindow w in ScreenTracker.Stack)
                {
                    tracked.Add(w.GetType().Name);
                }
                Plugin.Log.Report($"Possible stuck screen: controls are '{active.Name}' (world) but the game shows {open}; "
                    + $"windows the mod tracks: [{string.Join(", ", tracked.ToArray())}], mouse input available: {UIWindowManager.MouseInputAvailable}, "
                    + $"paused: {(TimeController.Instance != null && TimeController.Instance.Paused)}. The player can hold View for the rescue cursor."
                    + (s_Reports == MaxReports ? " (last report this session)" : ""));
            }
        }

        /// <summary>The game's windows that are open and in front right now, by type name; null for none.</summary>
        private static string OpenWindows()
        {
            UIWindowManager wm = UIWindowManager.Instance;
            if (wm == null || s_Windows == null || !(s_Windows.GetValue(wm) is List<UIHudWindow> windows))
            {
                return null;
            }
            StringBuilder sb = null;
            foreach (UIHudWindow w in windows)
            {
                if (w != null && w.WindowActive())
                {
                    sb = sb == null ? new StringBuilder() : sb.Append(", ");
                    sb.Append(w.GetType().Name);
                }
            }
            return sb?.ToString();
        }
    }
}
