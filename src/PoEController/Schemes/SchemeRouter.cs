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
        private readonly List<Prompt> m_Prompts = new List<Prompt>();

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

            ControlScheme next = Pick(ctx);
            if (next != Active)
            {
                Active?.Deactivate();
                Active = next;
                VirtualControls.Reset();
                pad.SuppressHeldButtons();
                Active?.Activate();
                Changed?.Invoke(next);
            }

            if (Active != null && pad.Connected && Application.isFocused)
            {
                Active.HandleInput(ctx, pad, dt);
            }

            m_Prompts.Clear();
            UI.ButtonHints.Clear();
            Active?.GetPrompts(ctx, m_Prompts);
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
            Active.Deactivate();
            VirtualControls.Reset();
            Active.Activate();
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
