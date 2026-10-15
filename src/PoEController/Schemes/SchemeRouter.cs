using System;
using System.Collections.Generic;
using PoEController.Compare;
using PoEController.Context;
using PoEController.Input;
using PoEController.TextEntry;
using PoEController.Tutorials;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>Picks the active control scheme each frame from the screen stack and game state, and runs it.</summary>
    public sealed class SchemeRouter
    {
        private const float RetrySeconds = 2f;
        private const int RepeatLogEvery = 50;

        private readonly List<Prompt> m_Prompts = new List<Prompt>();
        private readonly Dictionary<string, int> m_Errors = new Dictionary<string, int>();
        /// <summary>Schemes that threw, with the seconds they still sit out on this screen.</summary>
        private readonly Dictionary<ControlScheme, float> m_Benched = new Dictionary<ControlScheme, float>();
        /// <summary>The scheme the screen asked for when the first of them was benched (a new one = a new screen).</summary>
        private ControlScheme m_BenchedOn;
        /// <summary>The player has been told once this session.</summary>
        private bool m_Told;

        public ControlScheme Active { get; private set; }
        public SchemeContext Context { get; private set; }

        /// <summary>Prompts for the active scheme, rebuilt every frame.</summary>
        public IReadOnlyList<Prompt> Prompts => m_Prompts;

        public event Action<ControlScheme> Changed;

        /// <summary>Call between VirtualControls.BeginFrame and EndFrame.</summary>
        public void Update(Gamepad pad, float dt)
        {
            SchemeContext ctx = new SchemeContext
            {
                TopWindow = ScreenTracker.Top,
                Paused = TimeController.Instance != null && TimeController.Instance.Paused,
                InCombat = GameState.InCombat,
            };
            Context = ctx;
            bool onGamepad = Plugin.Instance.Mode.Active == InputDevice.Gamepad;
            bool padInput = onGamepad && pad.Connected && Application.isFocused && !GameState.IsLoading && !OnScreenKeyboard.IsOpen;
            TutorialFlow.Update(pad, ctx, dt, padInput, onGamepad);
            CompareSession.Safe(CompareSession.Validate, "Compare check");

            ControlScheme next;
            try
            {
                next = Pick(ctx);
            }
            catch (Exception e)
            {
                LogOnce("Picking the screen's controls", null, e);
                next = null;
            }
            // A scheme that threw sits out until the screen changes or a short while has passed (most failures are a
            // passing game state, e.g. a turn changing hands), with the plain menu controls standing in for a window's.
            if (m_Benched.Count > 0)
            {
                if (next != m_BenchedOn)
                {
                    m_Benched.Clear();
                }
                else
                {
                    foreach (ControlScheme s in new List<ControlScheme>(m_Benched.Keys))
                    {
                        if ((m_Benched[s] -= dt) <= 0f)
                        {
                            m_Benched.Remove(s);
                        }
                    }
                }
            }
            if (m_Benched.Count == 0)
            {
                m_BenchedOn = next;
            }
            if (next != null && m_Benched.ContainsKey(next))
            {
                next = ctx.TopWindow != null && !m_Benched.ContainsKey(SchemeTable.GenericMenu) ? SchemeTable.GenericMenu : null;
            }
            if (next != Active)
            {
                Switch(next, pad);
            }

            if (Active != null && pad.Connected && Application.isFocused)
            {
                try
                {
                    Active.HandleInput(ctx, pad, dt);
                }
                catch (Exception e)
                {
                    Fail("input", e);
                }
            }

            m_Prompts.Clear();
            UI.ButtonHints.Clear();
            if (Active != null)
            {
                try
                {
                    Active.GetPrompts(ctx, m_Prompts);
                }
                catch (Exception e)
                {
                    m_Prompts.Clear();
                    Fail("prompts", e);
                }
            }
            if (TutorialFlow.Mode == TutorialFlow.State.Timed)
            {
                m_Prompts.Add(new Prompt("lstick_click", "Read tip"));
                if (TutorialFlow.CanDismissWithB(ctx, Active))
                {
                    m_Prompts.Add(new Prompt("east", "Dismiss tip"));
                }
            }
        }

        /// <summary>
        /// Drops whatever the active scheme was in the middle of (held game actions, stick movement, emulated mouse
        /// buttons) and starts it fresh. Used when the input device changes or the controller goes away.
        /// </summary>
        public void Restart()
        {
            if (Active == null)
            {
                return;
            }
            try
            {
                Active.Deactivate();
                VirtualControls.Reset();
                Active.Activate();
            }
            catch (Exception e)
            {
                Fail("restart", e);
            }
        }

        private void Switch(ControlScheme next, Gamepad pad)
        {
            if (Active != null)
            {
                try
                {
                    Active.Deactivate();
                }
                catch (Exception e)
                {
                    // Leaving anyway; it gets a fresh Activate next time.
                    LogOnce("deactivate", Active, e);
                }
            }
            Active = next;
            VirtualControls.Reset();
            pad.SuppressHeldButtons();
            if (Active != null)
            {
                try
                {
                    Active.Activate();
                }
                catch (Exception e)
                {
                    Fail("activate", e);
                }
            }
            Changed?.Invoke(Active);
        }

        /// <summary>
        /// The active scheme threw: only it stops (cleaned up as far as it lets us, nothing left held), the rest of the
        /// mod carries on, and it is tried again after <see cref="RetrySeconds"/> or on the next screen.
        /// </summary>
        private void Fail(string stage, Exception e)
        {
            ControlScheme failed = Active;
            LogOnce(stage, failed, e);
            try
            {
                failed?.Deactivate();
            }
            catch (Exception)
            {
                // Already logged the cause; a half-done cleanup is all it allows.
            }
            Active = null;
            VirtualControls.Reset();
            try
            {
                Menus.MouseEmulator.ReleaseAll();
            }
            catch (Exception)
            {
            }
            if (failed != null)
            {
                m_Benched[failed] = RetrySeconds;
            }
            Changed?.Invoke(null);
            if (!m_Told && UISystemMessager.Instance != null)
            {
                m_Told = true;
                UISystemMessager.Instance.PostMessage("Controller: this screen's controls hit an error and are restarting", Color.white);
            }
        }

        /// <summary>Full details the first time, then a count every <see cref="RepeatLogEvery"/> repeats (a broken screen throws every frame).</summary>
        private void LogOnce(string stage, ControlScheme scheme, Exception e)
        {
            string key = $"{scheme?.Name}|{stage}|{e.GetType().Name}|{e.Message}";
            m_Errors.TryGetValue(key, out int seen);
            m_Errors[key] = ++seen;
            if (seen == 1)
            {
                Plugin.Log.LogError($"Controls '{scheme?.Name ?? "router"}' failed ({stage}); only they stop, retrying in {RetrySeconds} s or on the next screen: {e}");
            }
            else if (seen % RepeatLogEvery == 0)
            {
                Plugin.Log.LogWarning($"Controls '{scheme?.Name ?? "router"}' failed ({stage}) again ({seen} times): {e.GetType().Name}: {e.Message}");
            }
        }

        private static ControlScheme Pick(in SchemeContext ctx)
        {
            if (GameState.IsLoading)
            {
                return null;
            }
            OnScreenKeyboard.Validate();
            if (OnScreenKeyboard.IsOpen)
            {
                return SchemeTable.Keyboard;
            }
            if (Settings.SettingsPanel.IsOpen)
            {
                // Only for the controller: switching to mouse/keyboard or a loading screen closes it.
                if (Plugin.Instance.Mode.Active != InputDevice.Gamepad || !Compat.Features.On(Compat.Features.Settings))
                {
                    Settings.SettingsPanel.Close();
                }
                else
                {
                    return SchemeTable.Settings;
                }
            }
            // The tutorial pop-up sits over the world and any window; it has the focus until closed.
            if (TutorialFlow.Mode == TutorialFlow.State.Focused && TutorialFlow.IsShowing())
            {
                return SchemeTable.Tutorial;
            }
            // An open comparison owns the pad over its inspect windows until it is closed.
            if (CompareSession.Active && Compat.Features.On(Compat.Features.Compare))
            {
                return CompareSession.Mode == CompareSession.Stage.Choosing ? SchemeTable.CompareChoose : SchemeTable.Compare;
            }
            // Control style (settings panel): picks the world scheme and the window table. Keyboard, settings,
            // tutorial and compare above are shared by both styles.
            ControlStyle style = Plugin.Instance.Style;
            if (ctx.TopWindow != null)
            {
                // Walk up the type hierarchy so subclasses of a known window share its scheme.
                for (Type t = ctx.TopWindow.GetType(); t != null && t != typeof(UIHudWindow); t = t.BaseType)
                {
                    if (SchemeTable.TryGetWindow(style, t, out ControlScheme scheme))
                    {
                        return scheme;
                    }
                }
                return SchemeTable.GenericMenu;
            }
            if (UIMainMenuManager.Instance != null && UIMainMenuManager.Instance.isActiveAndEnabled)
            {
                return SchemeTable.MainMenu;
            }
            // Splash / intro screens: nothing to control yet.
            if (GameState.s_playerCharacter == null)
            {
                return null;
            }
            return SchemeTable.WorldFor(style);
        }
    }
}
