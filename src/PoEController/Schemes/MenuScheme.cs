using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// A menu screen driven through the real mouse cursor: d-pad snaps between clickable elements, the left stick
    /// moves the cursor freely, A/X are left/right click, the right stick scrolls. Extra bindings (close, tabs…)
    /// come from <see cref="BindingScheme"/>.
    /// </summary>
    public class MenuScheme : BindingScheme
    {
        private const float InitialSnapDelay = 0.15f;
        private const float CursorSpeedAt1080 = 1400f;
        private const float ScrollNotchesPerSecond = 14f;
        /// <summary>Locked cursor: LS past this snaps like the d-pad; held, it repeats after the delay at the interval.</summary>
        private const float StickSnapThreshold = 0.5f;
        private const float StickSnapDelay = 0.35f;
        private const float StickSnapInterval = 0.13f;

        private float m_SnapTimer;
        private bool m_SnapPending;
        private Vector2 m_FreeCursor;
        private bool m_FreeCursorValid;
        private float m_ScrollAccumulator;
        private Rect m_FocusRect;
        private bool m_HasFocus;
        private bool m_CursorFree;
        private bool m_StickSnapHeld;
        private float m_StickSnapTimer;

        public MenuScheme(string name, params Binding[] bindings)
            : base(name, bindings)
        {
        }

        public override void Activate()
        {
            m_SnapPending = true;
            m_SnapTimer = InitialSnapDelay;
            m_FreeCursorValid = false;
            m_ScrollAccumulator = 0f;
            m_HasFocus = false;
            m_CursorFree = false;
            m_StickSnapHeld = false;
            TooltipAssist.Reset();
        }

        /// <summary>
        /// Console style locks the cursor: LS snaps between elements like the d-pad, L3 frees it for
        /// the odd spot the snapping can't reach, L3 again locks it; every menu opens locked. PC layout: always free.
        /// </summary>
        protected static bool CursorLocks => Plugin.Instance != null && Plugin.Instance.Style == ControlStyle.Console;

        /// <summary>LS moves a free cursor right now (else it snaps).</summary>
        protected bool CursorIsFree => !CursorLocks || m_CursorFree;

        /// <summary>False where L3 has another job (Console inventory: multi-select); call <see cref="ToggleFreeCursor"/> then.</summary>
        protected virtual bool L3TogglesCursor => true;

        protected void ToggleFreeCursor()
        {
            m_CursorFree = !m_CursorFree;
            m_StickSnapHeld = false;
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Menu cursor {(m_CursorFree ? "free" : "locked")}");
        }

        public override void Deactivate()
        {
            MouseEmulator.ReleaseAll();
            m_HasFocus = false;
        }

        /// <summary>False where A has its own job on this screen (it is the left mouse button otherwise).</summary>
        protected virtual bool ClickOnA => true;

        /// <summary>The d-pad's stops (screen points, Unity coordinates); default: every reachable element.</summary>
        protected virtual List<Vector2> CollectPoints(Transform root, Transform extraRoot) => FocusNavigator.Collect(root, extraRoot);

        /// <summary>False where X has its own job on this screen (it is the right mouse button otherwise).</summary>
        protected virtual bool RightClickOnX => true;

        /// <summary>False where the screen moves the locked cursor itself (no d-pad / LS snapping, no start snap).</summary>
        protected virtual bool SnapNavigation => true;

        /// <summary>False where RS has its own job on this screen (it is the mouse wheel otherwise).</summary>
        protected virtual bool ScrollOnRightStick => true;

        /// <summary>Where the d-pad looks for elements: the top window (null = the whole UI, e.g. the main menu).</summary>
        protected virtual Transform NavigationRoot(in SchemeContext ctx) => ctx.TopWindow != null ? ctx.TopWindow.transform : null;

        /// <summary>Screen point (Unity coordinates) the first focus snaps nearest to.</summary>
        protected virtual Vector2 StartNear => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        /// <summary>A window below the top one whose elements the d-pad can also reach (e.g. the inventory under the stash).</summary>
        protected virtual Transform ExtraNavigationRoot => null;

        /// <summary>Element under the cursor (what A would click), in GUI coordinates, for the focus frame.</summary>
        public bool TryGetFocusRect(out Rect rect)
        {
            rect = m_FocusRect;
            return m_HasFocus;
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            Transform root = NavigationRoot(ctx);

            // Wait for the window's open animation before choosing a starting element.
            if (m_SnapPending)
            {
                m_SnapTimer -= dt;
                if (m_SnapTimer <= 0f)
                {
                    m_SnapPending = false;
                    if (Plugin.Instance.Mode.Active == InputDevice.Gamepad && SnapNavigation)
                    {
                        SnapToStart(root, ExtraNavigationRoot, StartNear);
                    }
                }
            }

            if (CursorLocks && L3TogglesCursor && pad.GetButtonDown(PadButton.LeftStick))
            {
                ToggleFreeCursor();
            }

            Vector2 dir = SnapNavigation ? DpadDirection(pad) : Vector2.zero;
            if (dir == Vector2.zero && !CursorIsFree && SnapNavigation)
            {
                dir = StickSnapDirection(pad.LeftStick, dt);
            }
            if (dir != Vector2.zero)
            {
                m_SnapPending = false;
                List<Vector2> points = CollectPoints(root, ExtraNavigationRoot);
                if (FocusNavigator.TryPick(points, MouseEmulator.Position, dir, out Vector2 target))
                {
                    MouseEmulator.MoveTo(target);
                    m_FreeCursorValid = false;
                }
            }

            if (CursorIsFree)
            {
                MoveFreeCursor(pad.LeftStick, dt);
            }
            if (ScrollOnRightStick)
            {
                Scroll(pad.RightStick.y, dt);
            }

            MouseEmulator.SetLeft(ClickOnA && pad.GetButton(PadButton.South));
            MouseEmulator.SetRight(RightClickOnX && pad.GetButton(PadButton.West));

            // Before the first snap the cursor is wherever the mouse was left; don't frame that.
            m_HasFocus = !m_SnapPending && FocusNavigator.TryGetElementRect(MouseEmulator.Position, out m_FocusRect);
            // Console style: no frame on game buttons that light up by themselves (they show their own hover).
            if (m_HasFocus && CursorLocks)
            {
                Vector2 at = MouseEmulator.Position;
                m_HasFocus = !Compat.Features.Get(Compat.Features.FocusFrameRule,
                    () => FocusNavigator.HasOwnHighlight(FocusNavigator.ColliderAt(at)), false);
            }
            if (Plugin.Instance.Mode.Active == InputDevice.Gamepad)
            {
                Vector2 cursor = MouseEmulator.Position;
                Compat.Features.Run(Compat.Features.QuickTooltips, () => TooltipAssist.Update(cursor, dt));
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            AddNavigationPrompts(into);
            if (CursorLocks && L3TogglesCursor)
            {
                into.Add(new Prompt("lstick_click", m_CursorFree ? "Lock cursor" : "Free cursor"));
            }
            base.GetPrompts(ctx, into);
        }

        /// <summary>The cursor prompts before the screen's bindings; Console-style screens replace them with their own.</summary>
        protected virtual void AddNavigationPrompts(List<Prompt> into)
        {
            AddNavigatePrompt(into);
            into.Add(new Prompt("south", "Select"));
            if (RightClickOnX)
            {
                into.Add(new Prompt("west", "Right-click"));
            }
            into.Add(new Prompt("rstick_ud", "Scroll"));
        }

        /// <summary>"D-pad / LS Navigate" (locked cursor) or "D-pad Navigate · LS Cursor" (free).</summary>
        protected void AddNavigatePrompt(List<Prompt> into)
        {
            if (CursorIsFree)
            {
                into.Add(new Prompt("dpad", "Navigate"));
                into.Add(new Prompt("lstick", "Cursor"));
            }
            else
            {
                into.Add(new Prompt(new[] { "dpad", "lstick" }, "Navigate"));
            }
        }

        /// <summary>Locked cursor: the stick as a d-pad (main axis), once on push, then repeating while held.</summary>
        private Vector2 StickSnapDirection(Vector2 stick, float dt)
        {
            if (stick.magnitude < StickSnapThreshold)
            {
                m_StickSnapHeld = false;
                return Vector2.zero;
            }
            Vector2 dir = Mathf.Abs(stick.x) > Mathf.Abs(stick.y) ? new Vector2(Mathf.Sign(stick.x), 0f) : new Vector2(0f, Mathf.Sign(stick.y));
            if (!m_StickSnapHeld)
            {
                m_StickSnapHeld = true;
                m_StickSnapTimer = StickSnapDelay;
                return dir;
            }
            m_StickSnapTimer -= dt;
            if (m_StickSnapTimer > 0f)
            {
                return Vector2.zero;
            }
            m_StickSnapTimer = StickSnapInterval;
            return dir;
        }

        private static Vector2 DpadDirection(Gamepad pad)
        {
            if (pad.GetButtonRepeat(PadButton.DpadUp))
            {
                return Vector2.up;
            }
            if (pad.GetButtonRepeat(PadButton.DpadDown))
            {
                return Vector2.down;
            }
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                return Vector2.left;
            }
            if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                return Vector2.right;
            }
            return Vector2.zero;
        }

        private void SnapToStart(Transform root, Transform extraRoot, Vector2 near)
        {
            List<Vector2> points = CollectPoints(root, extraRoot);
            if (FocusNavigator.TryNearest(points, near, out Vector2 target))
            {
                MouseEmulator.MoveTo(target);
            }
        }

        private void MoveFreeCursor(Vector2 stick, float dt)
        {
            if (stick == Vector2.zero)
            {
                m_FreeCursorValid = false;
                return;
            }
            m_SnapPending = false;
            if (!m_FreeCursorValid)
            {
                // Track a sub-pixel position of our own; the OS cursor only holds whole pixels.
                m_FreeCursor = MouseEmulator.Position;
                m_FreeCursorValid = true;
            }
            // Squared response: precise near the centre, fast at full tilt.
            float speed = CursorSpeedAt1080 * (Screen.height / 1080f) * stick.sqrMagnitude;
            m_FreeCursor += stick.normalized * speed * dt;
            m_FreeCursor.x = Mathf.Clamp(m_FreeCursor.x, 0f, Screen.width - 1);
            m_FreeCursor.y = Mathf.Clamp(m_FreeCursor.y, 0f, Screen.height - 1);
            MouseEmulator.MoveTo(m_FreeCursor);
        }

        private void Scroll(float stickY, float dt)
        {
            if (Mathf.Abs(stickY) < 0.01f)
            {
                m_ScrollAccumulator = 0f;
                return;
            }
            m_ScrollAccumulator += stickY * ScrollNotchesPerSecond * dt;
            int notches = (int)m_ScrollAccumulator;
            if (notches != 0)
            {
                m_ScrollAccumulator -= notches;
                MouseEmulator.Scroll(notches);
            }
        }
    }
}
