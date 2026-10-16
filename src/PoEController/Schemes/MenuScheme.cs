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

        /// <summary>
        /// Set by the game's own page changes (a step-by-step window shows its next page, the options switch tab;
        /// Patches/MenuPagePatches): the active menu snaps to its starting element again, as when it opened.
        /// </summary>
        private static bool s_PageChanged;

        /// <summary>Wait after a page change before choosing (the new page's elements fade / slide in).</summary>
        private const float PageSnapDelay = 0.15f;

        public static void PageChanged() => s_PageChanged = true;

        private bool m_TooltipMode;
        /// <summary>Tooltip mode scrolled the text to reveal more words: step on in this direction next frame.</summary>
        private Vector2 m_RepickDir;
        private Vector2 m_TooltipReturn;

        /// <summary>Tooltip mode is on: the d-pad steps between the highlighted words and tooltip-only elements.</summary>
        protected bool InTooltipMode => m_TooltipMode;

        /// <summary>Tooltip mode within the description panel only (null = the whole window).</summary>
        private Transform m_TooltipScope;

        /// <summary>
        /// R3 = tooltip mode: into the description panel next to the list when the screen has one (its highlighted
        /// words; RS scrolls it), else every highlighted word and tooltip on screen; R3 or B back. Screens that use R3
        /// for something else (loot: stash / party, world map: current location) turn it off.
        /// </summary>
        protected virtual bool TooltipsOnR3 => true;

        private static bool TooltipModeOn => CursorLocks && Compat.Features.On(Compat.Features.TooltipMode);

        /// <summary>
        /// After a button is let go in a menu: one look, this long later, whether the cursor is still on a stop. If
        /// the press changed the page (any window: a tab, a page of a list, another character), it moves to the
        /// nearest stop from where it is. One check per press, not a running watch.
        /// </summary>
        private const float AfterPressCheckSeconds = 0.25f;
        /// <summary>Farther than this from every stop (pixels at 1080p) = the cursor is on nothing.</summary>
        private const float OnStopPixelsAt1080 = 10f;
        private float m_AfterPressTimer = -1f;
        private bool m_PageSnapFromCursor;

        private static readonly PadButton[] s_ActionButtons =
        {
            PadButton.South, PadButton.West, PadButton.North, PadButton.East, PadButton.LeftShoulder,
            PadButton.RightShoulder, PadButton.LeftTrigger, PadButton.RightTrigger, PadButton.Start,
        };
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
            m_TooltipMode = false;
            m_TooltipScope = null;
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
            FocusNavigator.UpdateCompanionHover(null);
        }

        /// <summary>False where A has its own job on this screen (it is the left mouse button otherwise).</summary>
        protected virtual bool ClickOnA => true;

        /// <summary>The d-pad's stops (screen points, Unity coordinates); default: every reachable element.</summary>
        protected virtual List<Vector2> CollectPoints(Transform root, Transform extraRoot)
        {
            FocusNavigator.SkipAlso = SkipStop;
            FocusNavigator.PreferAlso = PreferAsStart;
            FocusNavigator.KeepAlso = KeepStop;
            FocusNavigator.GroupAlso = GroupAs;
            FocusNavigator.TooltipMode = m_TooltipMode;
            FocusNavigator.TooltipScope = m_TooltipMode ? m_TooltipScope : null;
            FocusNavigator.TooltipsSeparate = TooltipModeOn;
            try
            {
                return FocusNavigator.Collect(root, extraRoot);
            }
            finally
            {
                FocusNavigator.SkipAlso = null;
                FocusNavigator.PreferAlso = null;
                FocusNavigator.KeepAlso = null;
                FocusNavigator.GroupAlso = null;
                FocusNavigator.TooltipMode = false;
                FocusNavigator.TooltipScope = null;
                FocusNavigator.TooltipsSeparate = false;
            }
        }

        /// <summary>This screen's own "not a d-pad stop" rule (e.g. page tabs reached with LB / RB); default: none.</summary>
        protected virtual bool SkipStop(Collider c) => false;

        /// <summary>This screen's "a stop after all" rule (a button with its own glyph it still wants on the d-pad); default: none.</summary>
        protected virtual bool KeepStop(Collider c) => false;

        /// <summary>This screen's rows: the row a collider belongs to (one stop), or null; default: none.</summary>
        protected virtual Transform GroupAs(Collider c) => null;

        /// <summary>
        /// A d-pad / LS direction this screen uses itself (e.g. left / right changes the value of the row under the
        /// cursor); true = handled, the cursor doesn't move. Default: none.
        /// </summary>
        protected virtual bool ConsumeDirection(Vector2 dir) => false;

        /// <summary>The cursor moved to another stop this frame (d-pad / LS), for screens that act on a new focus.</summary>
        protected bool MovedThisFrame { get; private set; }

        /// <summary>Speed of RS on a text panel (UI units per second at full tilt).</summary>
        private const float TextScrollSpeed = 900f;
        private UIDraggablePanel m_TextPanel;
        private Transform m_TextPanelRoot;
        private float m_TextPanelScan = -100f;

        /// <summary>
        /// RS reads: the window's text panel (a scrolling panel with a description, glossary words) scrolls, wherever
        /// the cursor is, like the console's right panel. False = no such panel here (RS is the mouse wheel then).
        /// The list the cursor is in doesn't count (the d-pad scrolls that one at its edge).
        /// </summary>
        private bool ScrollTextPanel(Transform root, float stickY, float dt)
        {
            if (root == null)
            {
                return false;
            }
            if (root != m_TextPanelRoot || Time.unscaledTime - m_TextPanelScan > 0.5f)
            {
                m_TextPanelRoot = root;
                m_TextPanelScan = Time.unscaledTime;
                m_TextPanel = FindTextPanel(root);
            }
            if (m_TextPanel == null || !m_TextPanel.gameObject.activeInHierarchy)
            {
                return false;
            }
            if (Mathf.Abs(stickY) > 0.15f && m_TextPanel.scale.y != 0f)
            {
                m_TextPanel.DisableSpring();
                // Stick down = further down the text: the content moves up.
                m_TextPanel.MoveRelative(new Vector3(0f, -stickY * TextScrollSpeed * dt, 0f));
                m_TextPanel.RestrictWithinBounds(true);
            }
            return true;
        }

        /// <summary>
        /// The description the right stick scrolls: the one whose text the cursor is on; else (the cursor is on a list
        /// entry, a button...) the one nearest the cursor, never the list the cursor is in. With two item cards side by
        /// side (comparing) that is the card being looked at, never the other one. A description = glossary words or a
        /// good amount of text, not a list.
        /// </summary>
        private static UIDraggablePanel FindTextPanel(Transform root)
        {
            Vector2 at = MouseEmulator.Position;
            Collider focus = FocusNavigator.ColliderAt(at);
            Vector2 gui = new Vector2(at.x, Screen.height - at.y);
            UIDraggablePanel nearest = null;
            float nearestDist = float.MaxValue;
            foreach (UIDraggablePanel p in root.GetComponentsInChildren<UIDraggablePanel>(false))
            {
                if (p == null || !p.enabled || !IsTextPanel(p))
                {
                    continue;
                }
                if (focus != null && focus.transform.IsChildOf(p.transform))
                {
                    // On the text itself: this one. On a list entry inside it (journal list): the text elsewhere.
                    if (OnText(focus))
                    {
                        return p;
                    }
                    continue;
                }
                float dist = UI.WorldTextAvoid.TryScreenRect(p.transform, out Rect r) ? DistanceTo(r, gui) : float.MaxValue - 1f;
                if (dist < nearestDist)
                {
                    nearest = p;
                    nearestDist = dist;
                }
            }
            return nearest;
        }

        /// <summary>The cursor is on text (a label, highlighted words), not on a button or list entry.</summary>
        private static bool OnText(Collider focus)
        {
            return focus.GetComponent<UILabel>() != null || focus.GetComponentInParent<UIGlossaryEnabledLabel>() != null
                || focus.GetComponent<UIGlossaryEnabledLabel>() != null;
        }

        private static bool IsTextPanel(UIDraggablePanel p)
        {
            if (p.GetComponentInChildren<UIGlossaryEnabledLabel>() != null)
            {
                return true;
            }
            int text = 0;
            foreach (UILabel l in p.GetComponentsInChildren<UILabel>(false))
            {
                text += l.text != null ? l.text.Length : 0;
            }
            return text > 200;
        }

        /// <summary>Distance from a point to a rectangle (0 inside it).</summary>
        private static float DistanceTo(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// The cursor is on an entry of a list that scrolls along <paramref name="dir"/>: the next entry that way (shown
        /// or not) is scrolled just fully into view and becomes the target, one entry per press, both ways. False = not
        /// in such a list, or the last entry that way (the d-pad then leaves the list as usual).
        /// </summary>
        private bool StepInList(Transform root, Vector2 dir, out Vector2 target)
        {
            target = default;
            Collider focus = FocusNavigator.ColliderAt(MouseEmulator.Position);
            UIDraggablePanel list = focus != null ? NGUITools.FindInParents<UIDraggablePanel>(focus.gameObject) : null;
            if (list == null || !list.enabled || list.panel == null || list.panel.clipping == UIDrawCall.Clipping.None)
            {
                return false;
            }
            bool vertical = Mathf.Abs(dir.y) >= Mathf.Abs(dir.x);
            // The panel's own drag axes (scale 0 = locked): content wider than a vertical list still must not move sideways.
            if (vertical ? list.scale.y == 0f || !list.shouldMoveVertically : list.scale.x == 0f || !list.shouldMoveHorizontally)
            {
                return false;
            }
            int axis = vertical ? 1 : 0, across = 1 - axis;
            float sign = vertical ? Mathf.Sign(dir.y) : Mathf.Sign(dir.x);
            Transform lt = list.transform;
            ClickTargets.Classify(focus, out Component focusOwner);
            Bounds from = LocalBounds(lt, focus.bounds);

            // The nearest entry beyond the focus that way, in the same column / row.
            Collider next = null;
            Bounds nextB = default;
            float best = float.MaxValue;
            foreach (Collider c in lt.GetComponentsInChildren<Collider>(false))
            {
                if (c == focus || !c.enabled)
                {
                    continue;
                }
                ClickTargets.Kind kind = ClickTargets.Classify(c, out Component owner);
                if ((kind != ClickTargets.Kind.Target && kind != ClickTargets.Kind.Piece) || owner == focusOwner)
                {
                    continue;
                }
                Bounds b = LocalBounds(lt, c.bounds);
                float ahead = (b.center[axis] - from.center[axis]) * sign;
                bool sameLine = b.max[across] > from.min[across] && b.min[across] < from.max[across];
                if (!sameLine || ahead <= from.extents[axis] * 0.5f || ahead >= best)
                {
                    continue;
                }
                best = ahead;
                next = c;
                nextB = b;
            }
            if (next == null)
            {
                return false;
            }

            // Scroll just enough that all of it is inside the clip (past the soft edges).
            Vector4 clip = list.panel.clipRange;
            Vector2 soft = list.panel.clipping == UIDrawCall.Clipping.SoftClip ? list.panel.clipSoftness : Vector2.zero;
            float centre = axis == 1 ? clip.y : clip.x;
            float half = (axis == 1 ? clip.w : clip.z) * 0.5f - (axis == 1 ? soft.y : soft.x);
            float shift = 0f;
            if (nextB.min[axis] < centre - half)
            {
                shift = centre - half - nextB.min[axis];
            }
            else if (nextB.max[axis] > centre + half)
            {
                shift = centre + half - nextB.max[axis];
            }
            if (shift != 0f)
            {
                list.DisableSpring();
                Vector3 move = Vector3.zero;
                move[axis] = shift;
                list.MoveRelative(move);
                list.RestrictWithinBounds(true);
                Physics.SyncTransforms();
                Plugin.Log.LogInfo($"List step: '{next.name}' scrolled into view in '{list.name}' ({shift:0.#})");
            }

            // The stop that entry is now (a piece's stop sits at its owner's middle): the stop nearest its centre.
            Camera cam = NGUITools.FindCameraForLayer(next.gameObject.layer);
            if (cam == null)
            {
                return false;
            }
            Vector3 sc = cam.WorldToScreenPoint(next.bounds.center);
            var want = new Vector2(sc.x, sc.y);
            float bestD = float.MaxValue;
            foreach (Vector2 p in CollectPoints(root, ExtraNavigationRoot))
            {
                float d = (p - want).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    target = p;
                }
            }
            return bestD < float.MaxValue;
        }

        private static Bounds LocalBounds(Transform t, Bounds world)
        {
            var b = new Bounds(t.InverseTransformPoint(world.center), Vector3.zero);
            b.Encapsulate(t.InverseTransformPoint(world.min));
            b.Encapsulate(t.InverseTransformPoint(world.max));
            return b;
        }

        /// <summary>
        /// Stops this screen starts on when nothing is selected (its list of choices: the first in reading order wins);
        /// default: none (then the nearest button).
        /// </summary>
        protected virtual bool PreferAsStart(Collider c) => false;

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
            if (m_TooltipMode && pad.GetButtonDown(PadButton.East))
            {
                // B leaves tooltip mode; held until released so the window's own B (close) doesn't see it.
                ExitTooltipMode();
                pad.SuppressHeldButtons();
            }
            else if (pad.GetButtonDown(PadButton.East) && OpenDropdowns.CloseOne())
            {
                // B closes an open dropdown list first (a mouse click anywhere does); the window stays.
                pad.SuppressHeldButtons();
            }
            if (TooltipsOnR3 && TooltipModeOn && pad.GetButtonDown(PadButton.RightStick))
            {
                if (m_TooltipMode)
                {
                    ExitTooltipMode();
                }
                else if (!EnterDescription(NavigationRoot(ctx)))
                {
                    ToggleTooltipMode(NavigationRoot(ctx));
                }
                pad.SuppressHeldButtons();
            }
            base.HandleInput(ctx, pad, dt);
            Transform root = NavigationRoot(ctx);

            // A new page in the same window: start again on its selected (or first) element; never in free cursor.
            if (s_PageChanged)
            {
                s_PageChanged = false;
                if (!CursorIsFree)
                {
                    m_SnapPending = true;
                    m_SnapTimer = PageSnapDelay;
                    m_PageSnapFromCursor = true;
                    m_AfterPressTimer = -1f;
                }
            }
            CheckAfterPress(root, pad, dt);

            // Wait for the window's open animation before choosing a starting element.
            if (m_SnapPending)
            {
                m_SnapTimer -= dt;
                if (m_SnapTimer <= 0f)
                {
                    m_SnapPending = false;
                    if (Plugin.Instance.Mode.Active == InputDevice.Gamepad && SnapNavigation)
                    {
                        // A new page: without a selected option, the nearest button to where the cursor was.
                        SnapToStart(root, ExtraNavigationRoot, m_PageSnapFromCursor ? MouseEmulator.Position : StartNear);
                    }
                    m_PageSnapFromCursor = false;
                }
            }

            if (CursorLocks && L3TogglesCursor && pad.GetButtonDown(PadButton.LeftStick))
            {
                if (m_TooltipMode)
                {
                    ExitTooltipMode();
                }
                ToggleFreeCursor();
            }

            Vector2 dir = SnapNavigation ? DpadDirection(pad) : Vector2.zero;
            if (dir == Vector2.zero && !CursorIsFree && SnapNavigation)
            {
                dir = StickSnapDirection(pad.LeftStick, dt);
            }

            MovedThisFrame = false;
            if (m_RepickDir != Vector2.zero)
            {
                // The text was scrolled last frame: the next word that came into view that way.
                Vector2 again = m_RepickDir;
                m_RepickDir = Vector2.zero;
                if (m_TooltipMode && FocusNavigator.TryPick(CollectPoints(root, ExtraNavigationRoot), MouseEmulator.Position, again, out Vector2 next))
                {
                    MouseEmulator.MoveTo(next);
                    MovedThisFrame = true;
                }
            }
            if (dir != Vector2.zero)
            {
                m_SnapPending = false;
                if (m_TooltipMode || !ConsumeDirection(dir))
                {
                    // In a list that scrolls this way: the next entry, scrolled fully into view. Else the nearest stop.
                    bool found = StepInList(root, dir, out Vector2 target)
                        || FocusNavigator.TryPick(CollectPoints(root, ExtraNavigationRoot), MouseEmulator.Position, dir, out target);
                    if (found)
                    {
                        MouseEmulator.MoveTo(target);
                        m_FreeCursorValid = false;
                        MovedThisFrame = true;
                    }
                    else if (m_TooltipMode && dir.y != 0f && ScrollForMoreWords(root, dir.y))
                    {
                        // No more words on screen that way in a long text (a soulbound item's card): scrolled; next frame
                        // picks the next word.
                        m_RepickDir = dir;
                    }
                }
            }

            if (CursorIsFree)
            {
                MoveFreeCursor(pad.LeftStick, dt);
            }
            if (ScrollOnRightStick && !ScrollTextPanel(root, pad.RightStick.y, dt))
            {
                Scroll(pad.RightStick.y, dt);
            }

            MouseEmulator.SetLeft(ClickOnA && pad.GetButton(PadButton.South));
            MouseEmulator.SetRight(RightClickOnX && !m_TooltipMode && pad.GetButton(PadButton.West));

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
                // Before the first snap the cursor is wherever the mouse was left: nothing is focused yet.
                FocusNavigator.UpdateCompanionHover(m_SnapPending ? null : FocusNavigator.ColliderAt(cursor));
            }
            else
            {
                FocusNavigator.UpdateCompanionHover(null);
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            if (m_TooltipMode)
            {
                into.Add(new Prompt(new[] { "dpad", "lstick" }, "Navigate"));
                into.Add(new Prompt("south", "Open entry"));
                into.Add(new Prompt(new[] { "rstick_click", "east" }, "Back"));
                into.Add(new Prompt("rstick_ud", "Scroll"));
                return;
            }
            AddNavigationPrompts(into);
            if (CursorLocks && L3TogglesCursor)
            {
                into.Add(new Prompt("lstick_click", m_CursorFree ? "Lock cursor" : "Free cursor"));
                if (TooltipModeOn && TooltipsOnR3)
                {
                    into.Add(new Prompt("rstick_click", "Tooltips"));
                }
            }
            base.GetPrompts(ctx, into);
            if (OpenDropdowns.Find() != null)
            {
                for (int i = 0; i < into.Count; i++)
                {
                    if (into[i].Glyphs != null && into[i].Glyphs.Length == 1 && into[i].Glyphs[0] == "east")
                    {
                        into[i] = new Prompt("east", "Close list");
                    }
                }
            }
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
            // The option the game has selected (Male, Human, the current class ...), like the console; otherwise a
            // button or option near the start point; a highlighted word only when the window has nothing else.
            List<Vector2> selected = FocusNavigator.LastSelectedPoints;
            List<Vector2> preferred = FocusNavigator.LastPreferredPoints;
            List<Vector2> elements = FocusNavigator.LastElementPoints;
            List<Vector2> from = selected.Count > 0 ? selected : preferred.Count > 0 ? preferred : elements.Count > 0 ? elements : points;
            Vector2 aim = selected.Count > 0 ? TopLeft(selected) : preferred.Count > 0 ? TopLeft(preferred) : near;
            if (FocusNavigator.TryNearest(from, aim, out Vector2 target))
            {
                MouseEmulator.MoveTo(target);
                Plugin.Log.LogInfo(selected.Count > 0 ? $"Focus start: on the selected option ({selected.Count} selected)"
                    : preferred.Count > 0 ? $"Focus start: nothing selected, first of {preferred.Count} choices"
                    : $"Focus start: nothing selected, nearest of {from.Count} stops");
            }
        }

        private void CheckAfterPress(Transform root, Gamepad pad, float dt)
        {
            if (CursorIsFree || !SnapNavigation || Plugin.Instance.Mode.Active != InputDevice.Gamepad)
            {
                m_AfterPressTimer = -1f;
                return;
            }
            foreach (PadButton b in s_ActionButtons)
            {
                if (pad.GetButtonUp(b))
                {
                    m_AfterPressTimer = AfterPressCheckSeconds;
                }
            }
            if (m_AfterPressTimer < 0f || m_SnapPending)
            {
                return;
            }
            m_AfterPressTimer -= dt;
            if (m_AfterPressTimer > 0f)
            {
                return;
            }
            m_AfterPressTimer = -1f;
            List<Vector2> points = CollectPoints(root, ExtraNavigationRoot);
            Vector2 at = MouseEmulator.Position;
            float tolerance = OnStopPixelsAt1080 * Screen.height / 1080f;
            if (points.Count == 0
                || (FocusNavigator.TryNearest(points, at, out Vector2 nearest) && (nearest - at).sqrMagnitude <= tolerance * tolerance))
            {
                return;
            }
            List<Vector2> elements = FocusNavigator.LastElementPoints;
            if (FocusNavigator.TryNearest(elements.Count > 0 ? elements : points, at, out Vector2 target))
            {
                MouseEmulator.MoveTo(target);
                m_FreeCursorValid = false;
                Plugin.Log.LogInfo("Focus: the press changed the page, cursor moved to the nearest stop");
            }
        }

        /// <summary>
        /// Tooltip mode, no further word on screen up / down: the text it is in (or the description nearest the cursor)
        /// scrolls about half its height that way, so the words below / above come into view. False when it can't
        /// move any further (the end of the text).
        /// </summary>
        private bool ScrollForMoreWords(Transform root, float dirY)
        {
            UIDraggablePanel panel = m_TooltipScope != null ? m_TooltipScope.GetComponent<UIDraggablePanel>() : null;
            if (panel == null && root != null)
            {
                panel = FindTextPanel(root);
            }
            if (panel == null || panel.panel == null || !panel.gameObject.activeInHierarchy)
            {
                return false;
            }
            float visible = panel.panel.clipRange.w;
            if (visible <= 0f)
            {
                return false;
            }
            Vector3 before = panel.transform.localPosition;
            panel.DisableSpring();
            // Down = further down the text: the content moves up (as the right stick scrolls it).
            panel.MoveRelative(new Vector3(0f, (dirY < 0f ? 1f : -1f) * visible * 0.5f, 0f));
            panel.RestrictWithinBounds(true);
            bool moved = (panel.transform.localPosition - before).sqrMagnitude > 0.25f;
            if (moved)
            {
                Plugin.Log.LogInfo($"Tooltip mode: text scrolled {(dirY < 0f ? "down" : "up")} for more words");
            }
            return moved;
        }

        /// <summary>Tooltip mode on (the nearest word / tooltip element) or off (back where the cursor was).</summary>
        protected void ToggleTooltipMode(Transform root)
        {
            if (m_TooltipMode)
            {
                ExitTooltipMode();
                return;
            }
            Vector2 at = MouseEmulator.Position;
            m_TooltipMode = true;
            List<Vector2> words = CollectPoints(root, ExtraNavigationRoot);
            if (!FocusNavigator.TryNearest(words, at, out Vector2 target))
            {
                m_TooltipMode = false;
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                Plugin.Log.LogInfo("Tooltip mode: nothing with a tooltip on this screen");
                return;
            }
            m_CursorFree = false;
            m_TooltipReturn = at;
            MouseEmulator.MoveTo(target);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Tooltip mode: on ({words.Count} words / tooltips)");
        }

        /// <summary>
        /// Tooltip mode inside the description panel, starting on its first highlighted word. False = the screen has
        /// no such panel (the caller then opens tooltip mode for the whole window).
        /// </summary>
        private bool EnterDescription(Transform root)
        {
            UIDraggablePanel panel = root != null ? FindTextPanel(root) : null;
            if (panel == null)
            {
                return false;
            }
            Vector2 at = MouseEmulator.Position;
            m_TooltipMode = true;
            m_TooltipScope = panel.transform;
            List<Vector2> words = CollectPoints(root, ExtraNavigationRoot);
            if (words.Count == 0)
            {
                // No highlighted words in the description: the whole window's tooltips instead.
                m_TooltipMode = false;
                m_TooltipScope = null;
                return false;
            }
            m_CursorFree = false;
            m_TooltipReturn = at;
            MouseEmulator.MoveTo(TopLeft(words));
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Tooltip mode: description, {words.Count} highlighted words");
            return true;
        }

        private void ExitTooltipMode()
        {
            if (!m_TooltipMode)
            {
                return;
            }
            m_TooltipMode = false;
            m_TooltipScope = null;
            m_RepickDir = Vector2.zero;
            MouseEmulator.MoveTo(m_TooltipReturn);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo("Tooltip mode: off");
        }

        /// <summary>Of several selected elements (chosen abilities): the first in reading order.</summary>
        private static Vector2 TopLeft(List<Vector2> points)
        {
            Vector2 best = points[0];
            foreach (Vector2 p in points)
            {
                // Unity screen y is up: higher first, then left.
                if (p.y > best.y + 4f || (Mathf.Abs(p.y - best.y) <= 4f && p.x < best.x))
                {
                    best = p;
                }
            }
            return best;
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
