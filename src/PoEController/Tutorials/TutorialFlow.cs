using System;
using System.Reflection;
using HarmonyLib;
using PoEController.Input;
using PoEController.Schemes;
using UnityEngine;

namespace PoEController.Tutorials
{
    /// <summary>
    /// The game's tutorial pop-up (<see cref="UITutorialBox"/>, a HUD panel, not a window) on the controller.
    /// <list type="bullet">
    /// <item>Tutorials the game pauses for → <b>Focused</b> at once (<see cref="TutorialScheme"/> has the controls).</item>
    /// <item>The rest → <b>Timed</b>: play goes on, the box closes by itself after its reading time (2 s + words at
    /// <see cref="WordsPerMinute"/>, at least 5 s; the countdown stops while paused or in a menu). L3 = read it
    /// (pause + Focused), B = dismiss (only where B does nothing else).</item>
    /// <item>No tutorial up: hold L3 for <see cref="RecallHold"/> s = show the last one again (Focused).</item>
    /// </list>
    /// Focused pauses the game if it isn't (never in turn-based combat) and resumes when the box is gone; tutorials
    /// the game paused for itself stay paused afterwards, as in the unmodded game. Closing goes through the box's own
    /// <c>Hide</c>, so follow-up tutorials still chain. Only acts in controller mode; mouse/keyboard play is unchanged.
    /// </summary>
    internal static class TutorialFlow
    {
        public enum State
        {
            None,
            /// <summary>Up, but appeared while on mouse/keyboard: left alone until the controller is used.</summary>
            Vanilla,
            Timed,
            Focused,
        }

        private const float NoticeSeconds = 2f;
        private const float MinSeconds = 5f;

        private static readonly FieldInfo s_Current = AccessTools.Field(typeof(UITutorialBox), "m_CurrentTutorial");

        public static bool TimedEnabled = true;
        public static float WordsPerMinute = 238f;
        public static float RecallHold = 1f;

        private static int s_Index = -1;
        private static int s_LastIndex = -1;
        private static bool s_PausedByUs;
        private static bool s_Recalling;
        private static bool s_RecallFired;
        private static bool s_Broken => !Compat.Features.On(Compat.Features.Tutorials);

        public static State Mode { get; private set; }
        public static float Duration { get; private set; }
        public static float Remaining { get; private set; }

        /// <summary>Share of the reading time left, 1 → 0.</summary>
        public static float Fraction => Duration > 0f ? Mathf.Clamp01(Remaining / Duration) : 0f;

        /// <summary>The full tutorial box is up (the small minimized tab doesn't count; that's the player's choice).</summary>
        public static bool IsShowing()
        {
            if (s_Broken)
            {
                return false;
            }
            UITutorialBox box = UITutorialBox.Instance;
            if (box == null || !box.isActiveAndEnabled)
            {
                return false;
            }
            UIPanel maximized = Maximized(box);
            GameObject minimized = Minimized(box);
            return maximized != null && maximized.alpha > 0.01f && minimized != null && !minimized.activeSelf;
        }

        // The box's parts, read by name (Compat.GameRef): a changed game means "not showing", never an exception.
        internal static UIPanel Maximized(UITutorialBox box) => Compat.GameRef.Get<UIPanel>(box, "Maximized");

        internal static GameObject Minimized(UITutorialBox box) => Compat.GameRef.Object(box, "Minimized");

        /// <summary>The close button that is showing (the box has two).</summary>
        internal static GameObject CloseButton(UITutorialBox box)
        {
            GameObject close = Compat.GameRef.Object(box, "CloseButton");
            return close != null && close.activeInHierarchy ? close : Compat.GameRef.Object(box, "CloseButton2");
        }

        /// <summary>Called by the router every frame, before it picks a scheme.</summary>
        /// <param name="padInput">Controller mode, pad connected, game focused, nothing else (keyboard, loading) owning the pad.</param>
        public static void Update(Gamepad pad, in SchemeContext ctx, float dt, bool padInput, bool onGamepad)
        {
            if (s_Broken)
            {
                return;
            }
            try
            {
                UpdateUnsafe(pad, ctx, dt, padInput, onGamepad);
            }
            catch (Exception e)
            {
                Mode = State.None;
                ReleasePause();
                Compat.Features.Off(Compat.Features.Tutorials, "error (tutorials left to the game): " + e);
            }
        }

        private static string s_LoggedRectSource;

        /// <summary>
        /// Screen rectangle (GUI coordinates) of the tutorial box: the hover area behind it (a collider with nothing
        /// drawn on it, so it is measured as a collider), else everything drawn in the full-size box.
        /// </summary>
        public static bool TryGetBoxRect(out Rect rect)
        {
            rect = default;
            UITutorialBox box = UITutorialBox.Instance;
            Camera ui = InGameUILayout.NGUICamera;
            if (box == null || ui == null)
            {
                return false;
            }
            string source = null;
            GameObject maximizedBg = Compat.GameRef.Object(box, "MaximizedBg");
            UIPanel maximized = Maximized(box);
            Collider bg = maximizedBg != null ? maximizedBg.GetComponent<Collider>() : null;
            if (bg != null && TryScreenRect(ui, bg.bounds, out rect))
            {
                source = "background collider";
            }
            else if (maximized != null && TryScreenRect(ui, NGUIMath.CalculateAbsoluteWidgetBounds(maximized.transform), out rect))
            {
                source = "box widgets";
            }
            if (source != null && source != s_LoggedRectSource)
            {
                s_LoggedRectSource = source;
                Plugin.Log.LogInfo($"Tutorial box measured from {source}: {rect}");
            }
            return source != null;
        }

        private static bool TryScreenRect(Camera ui, Bounds b, out Rect rect)
        {
            Vector3 min = ui.WorldToScreenPoint(b.min);
            Vector3 max = ui.WorldToScreenPoint(b.max);
            rect = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Screen.height - Mathf.Max(min.y, max.y),
                Mathf.Max(min.x, max.x), Screen.height - Mathf.Min(min.y, max.y));
            return rect.width > 8f && rect.height > 8f && rect.width < Screen.width * 0.95f && rect.height < Screen.height * 0.95f;
        }

        /// <summary>Frame in which B dismissed a tip (schemes that also use B skip it that frame).</summary>
        public static int EastUsedFrame { get; private set; } = -1;

        /// <summary>B dismisses a timed tutorial only where B has no other job.</summary>
        public static bool CanDismissWithB(in SchemeContext ctx, ControlScheme active)
        {
            return ctx.TopWindow == null && active is IWorldScheme world && world.EastIsFree;
        }

        private static void UpdateUnsafe(Gamepad pad, in SchemeContext ctx, float dt, bool padInput, bool onGamepad)
        {
            UITutorialBox box = UITutorialBox.Instance;
            if (box == null || !IsShowing())
            {
                if (Mode != State.None)
                {
                    Mode = State.None;
                    s_Index = -1;
                }
                ReleasePause();
                HandleRecall(box, pad, padInput);
                return;
            }

            int index = s_Current != null ? (int)s_Current.GetValue(box) : -1;
            if (index != s_Index || Mode == State.None)
            {
                // Still reading when a follow-up chains in → keep reading.
                bool keepFocus = Mode == State.Focused;
                s_Index = index;
                if (index >= 0)
                {
                    s_LastIndex = index;
                }
                if (s_Recalling || keepFocus || GamePausedFor(index) || (onGamepad && !TimedEnabled))
                {
                    Focus();
                }
                else if (onGamepad)
                {
                    StartTimer(box);
                }
                else
                {
                    Mode = State.Vanilla;
                }
                s_Recalling = false;
                Plugin.Log.LogInfo($"Tutorial {index} shown: {Mode}" + (Mode == State.Timed ? $" ({Duration:0.0} s)" : ""));
            }

            if (Mode == State.Vanilla && onGamepad)
            {
                StartTimer(box);
            }
            if (Mode != State.Timed)
            {
                return;
            }

            TimeController tc = TimeController.Instance;
            bool hold = (tc != null && tc.Paused) || ctx.TopWindow != null;
            if (!hold)
            {
                Remaining -= dt;
            }
            if (padInput && pad.GetButtonDown(PadButton.LeftStick))
            {
                Focus();
                Plugin.Log.LogInfo("Tutorial: reading (L3)");
                return;
            }
            if (padInput && pad.GetButtonDown(PadButton.East) && CanDismissWithB(ctx, Plugin.Instance.Router.Active))
            {
                Plugin.Log.LogInfo("Tutorial: dismissed (B)");
                EastUsedFrame = Time.frameCount;
                box.Hide();
                return;
            }
            if (Remaining <= 0f)
            {
                Plugin.Log.LogInfo("Tutorial: timed out");
                box.Hide();
            }
        }

        private static void HandleRecall(UITutorialBox box, Gamepad pad, bool padInput)
        {
            if (!padInput || box == null || s_LastIndex < 0 || !pad.GetButton(PadButton.LeftStick))
            {
                s_RecallFired = false;
                return;
            }
            if (s_RecallFired || pad.GetHeldTime(PadButton.LeftStick) < RecallHold)
            {
                return;
            }
            s_RecallFired = true;
            TutorialManager tm = TutorialManager.Instance;
            if (tm == null || tm.Tutorials == null || s_LastIndex >= tm.Tutorials.Length)
            {
                return;
            }
            s_Recalling = true;
            box.ShowTutorial(s_LastIndex);
            // With "minimize tutorials" on it comes up as the small tab; press the box's own maximize button.
            GameObject minimized = Minimized(box);
            GameObject maximize = Compat.GameRef.Object(box, "MaximizeButton");
            if (minimized != null && minimized.activeSelf && maximize != null)
            {
                maximize.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
            }
            if (!IsShowing())
            {
                s_Recalling = false;
            }
            Plugin.Log.LogInfo($"Tutorial {s_LastIndex}: recalled (hold L3)");
        }

        /// <summary>The game paused for this tutorial itself (its own <c>Pauses</c> flag took effect).</summary>
        private static bool GamePausedFor(int index)
        {
            TutorialManager tm = TutorialManager.Instance;
            TimeController tc = TimeController.Instance;
            return tm != null && tc != null && tc.Paused && index >= 0 && tm.Tutorials != null
                && index < tm.Tutorials.Length && tm.Tutorials[index].Pauses;
        }

        private static void StartTimer(UITutorialBox box)
        {
            UILabel content = Compat.GameRef.Get<UILabel>(box, "ContentLabel");
            string text = content != null ? NGUITools.StripSymbols(content.text ?? "") : "";
            int words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
            Duration = Mathf.Max(MinSeconds, NoticeSeconds + words * 60f / Mathf.Max(60f, WordsPerMinute));
            Remaining = Duration;
            Mode = State.Timed;
        }

        private static void Focus()
        {
            Mode = State.Focused;
            Remaining = 0f;
            TimeController tc = TimeController.Instance;
            // Turn-based combat already waits for the player; pausing there can freeze the turn order.
            if (!s_PausedByUs && tc != null && !tc.Paused && !TacticalModeManager.IsInTacticalCombat())
            {
                tc.SafePaused = true;
                s_PausedByUs = tc.Paused;
            }
        }

        private static void ReleasePause()
        {
            if (!s_PausedByUs)
            {
                return;
            }
            s_PausedByUs = false;
            TimeController tc = TimeController.Instance;
            if (tc != null && tc.Paused)
            {
                tc.Paused = false;
            }
        }
    }
}
