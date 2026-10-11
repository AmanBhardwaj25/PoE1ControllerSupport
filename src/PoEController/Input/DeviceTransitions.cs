using PoEController.Menus;
using PoEController.Schemes;
using PoEController.World;
using UnityEngine;

namespace PoEController.Input
{
    /// <summary>
    /// Keeps the hand-over between mouse/keyboard and controller clean, including controllers that arrive late or
    /// vanish (unplugged, batteries, Bluetooth sleep while the player is away):
    /// <list type="bullet">
    /// <item>Every switch restarts the active control scheme, so nothing half-done carries over: stick movement stops,
    /// emulated mouse buttons are released, the game's actions are let go without firing their "on release" behaviour.</item>
    /// <item>Controller lost: back to mouse/keyboard at once, and (optionally) the game pauses if you were playing on
    /// the controller, so nothing happens while you're away. A message says what happened.</item>
    /// <item>Controller found: a message; buttons already held are ignored until released (Gamepad). The mode only
    /// switches once the controller is actually used.</item>
    /// <item>While playing in the world with the controller, the game's cursor is hidden (the same way cutscenes hide
    /// it) and parked mid-screen so it can't edge-scroll; it comes back the moment the mouse is touched.</item>
    /// </list>
    /// </summary>
    internal sealed class DeviceTransitions
    {
        private readonly Plugin m_Plugin;
        private GameCursor m_HiddenOn;

        /// <summary>The mod has hidden the game cursor (world mode on the controller); its hover is meaningless then.</summary>
        public static bool CursorHiddenByMod { get; private set; }

        public bool PauseOnDisconnect = true;

        public DeviceTransitions(Plugin plugin)
        {
            m_Plugin = plugin;
            plugin.Mode.Changed += OnModeChanged;
        }

        /// <summary>Call right after Gamepad.Update and before the mode tracker / router run.</summary>
        public void AfterPadUpdate()
        {
            Gamepad pad = m_Plugin.Pad;
            if (pad.DisconnectedThisFrame)
            {
                OnDisconnected();
            }
            else if (pad.ConnectedThisFrame)
            {
                Notify($"Controller connected: {pad.Name}");
            }
        }

        /// <summary>Call after the router has run, once per frame.</summary>
        public void UpdateCursor()
        {
            bool hide = m_Plugin.Mode.Active == InputDevice.Gamepad
                && m_Plugin.Pad.Connected
                && m_Plugin.Router.Active is IWorldScheme world
                && !world.CursorActive;
            SetCursorHidden(hide);
        }

        /// <summary>Gives the cursor back (plugin disabled or unloading).</summary>
        public void Shutdown()
        {
            SetCursorHidden(false);
        }

        private void OnDisconnected()
        {
            bool wasPlayingOnPad = m_Plugin.Mode.Active == InputDevice.Gamepad;
            m_Plugin.Mode.Set(InputDevice.KeyboardMouse);
            // Mode.Set only restarts the scheme when the mode actually changed.
            if (!wasPlayingOnPad)
            {
                m_Plugin.Router.Restart();
            }

            bool paused = false;
            if (wasPlayingOnPad && PauseOnDisconnect && CanPause())
            {
                TimeController.Instance.SafePaused = true;
                paused = TimeController.Instance.Paused;
            }
            Notify(paused ? "Controller disconnected - game paused" : "Controller disconnected");
        }

        private void OnModeChanged(InputDevice device)
        {
            m_Plugin.Router.Restart();
            if (device == InputDevice.KeyboardMouse)
            {
                // Buttons still held on the pad must not act once the mouse has taken over.
                m_Plugin.Pad.SuppressHeldButtons();
                CameraPan.Input = Vector2.zero;
            }
        }

        private static bool CanPause()
        {
            return TimeController.Instance != null
                && !TimeController.Instance.Paused
                && GameState.s_playerCharacter != null
                && !GameState.IsLoading
                && !GameState.IsCutsceneOrConversationRunning();
        }

        private void SetCursorHidden(bool hide)
        {
            GameCursor cursor = GameCursor.Instance;
            if (m_HiddenOn != null && (m_HiddenOn != cursor || !hide))
            {
                m_HiddenOn.SetShowCursor(this, state: true);
                m_HiddenOn = null;
            }
            if (hide && cursor != null && m_HiddenOn == null)
            {
                cursor.SetShowCursor(this, state: false);
                m_HiddenOn = cursor;
            }
            CursorHiddenByMod = m_HiddenOn != null;
        }

        private static void Notify(string message)
        {
            Plugin.Log.LogInfo(message);
            if (UISystemMessager.Instance != null)
            {
                UISystemMessager.Instance.PostMessage(message, Color.white);
            }
        }
    }
}
