using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// The world map (travel screen) in Console style (after the console version): LS / d-pad step from location
    /// to location, preferring the ones a road leads to, the map pans itself to keep the chosen one in view; A = a
    /// click on it (the game's own travel question with the travel time, or opening a sub-map such as the White
    /// March); RS pans the map; R3 back to where the party is; L3 free cursor (as every Console menu); B close. The
    /// locations, roads and travel times are the game's own (UIWorldMapIcons, UIWorldMapLinks, UIWorldMapMasterLinks).
    /// </summary>
    public sealed class ConsoleWorldMapScheme : MenuScheme
    {
        /// <summary>R3 here jumps to the current location.</summary>
        protected override bool TooltipsOnR3 => false;

        private const float StickThreshold = 0.5f;
        private const float StickRepeatDelay = 0.35f;
        private const float StickRepeatInterval = 0.18f;
        private const float MaxStepAngle = 60f;
        private const float RoadBonus = 0.6f;
        private const float PanSpeedAt1080 = 900f;
        private const float PanDeadzone = 0.15f;
        private const float CenterSeconds = 0.6f;
        /// <summary>The chosen location is re-centred once it leaves the middle part of the map area (fraction of its size).</summary>
        private const float SafeAreaFraction = 0.6f;

        private UIWorldMapIcons m_Manager;
        private UIWorldMapIcon m_Selected;
        private bool m_WasFree;
        private bool m_StickHeld;
        private float m_StickTimer;
        private bool m_Panned;
        private bool m_Centering;
        private float m_CenterTimer;

        public ConsoleWorldMapScheme(params Binding[] bindings)
            : base("World map (Console)", bindings)
        {
        }

        private static bool On => Compat.Features.On(Compat.Features.WorldMap);

        protected override bool SnapNavigation => !On;

        protected override bool ScrollOnRightStick => !On;

        protected override bool RightClickOnX => !On;

        public override void Activate()
        {
            base.Activate();
            // The selection survives the travel question's message box; it is re-checked every frame.
            m_StickHeld = false;
            m_Panned = false;
            m_WasFree = false;
        }

        public override void Deactivate()
        {
            base.Deactivate();
            // Closed (not just covered by the travel question): the next opening starts where the party is.
            bool stillOpen = Compat.Features.Get(Compat.Features.WorldMap,
                () => UIWorldMapManager.Instance != null && UIWorldMapManager.Instance.IsVisible, false);
            if (!stillOpen)
            {
                m_Manager = null;
                m_Selected = null;
                m_Centering = false;
            }
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            if (On && Plugin.Instance.Mode.Active == InputDevice.Gamepad)
            {
                Compat.Features.Run(Compat.Features.WorldMap, () => Step(pad, dt));
            }
        }

        private void Step(Gamepad pad, float dt)
        {
            UIWorldMapManager map = UIWorldMapManager.Instance;
            Camera cam = InGameUILayout.NGUICamera;
            if (map == null || !map.IsVisible || cam == null)
            {
                return;
            }
            UIWorldMapIcons manager = map.CurrentIconManager;
            UIDraggablePanel drag = manager != null ? NGUITools.FindInParents<UIDraggablePanel>(manager.gameObject) : null;
            if (manager == null || drag == null)
            {
                return;
            }
            if (manager != m_Manager)
            {
                // First open, or a sub-map (White March, Od Nua…) replaced the map.
                m_Manager = manager;
                m_Selected = null;
            }
            if (m_Selected != null && !IsStop(manager, m_Selected))
            {
                m_Selected = null;
            }
            Rect area = MapArea(cam, drag);

            Vector2 rs = pad.RightStick;
            bool panning = rs.magnitude > PanDeadzone;
            if (panning)
            {
                m_Centering = false;
                m_Panned = true;
                float speed = PanSpeedAt1080 * (Screen.height / 1080f);
                Pan(cam, drag, -rs * speed * dt);
            }

            if (CursorIsFree)
            {
                m_WasFree = true;
                return;
            }

            UIWorldMapIcon before = m_Selected;
            if (m_WasFree)
            {
                // Back from the free cursor: the location nearest to where it was left.
                m_WasFree = false;
                m_Selected = Nearest(manager, cam, MouseEmulator.Position);
            }
            if (m_Selected == null)
            {
                m_Selected = Here(map, manager) ?? Nearest(manager, cam, area.center);
                m_Centering = m_Selected != null;
                m_CenterTimer = 0f;
            }
            if (!panning && m_Panned)
            {
                // After panning, a location that went off the map area is replaced by the one in the middle.
                m_Panned = false;
                if (m_Selected != null && !area.Contains(ScreenPoint(cam, m_Selected)))
                {
                    m_Selected = Nearest(manager, cam, area.center);
                }
            }

            if (pad.GetButtonDown(PadButton.RightStick))
            {
                UIWorldMapIcon here = Here(map, manager);
                if (here != null)
                {
                    m_Selected = here;
                    m_Centering = true;
                    m_CenterTimer = 0f;
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                    Plugin.Log.LogInfo("World map: back to the current location (R3)");
                }
            }

            Vector2 dir = Direction(pad, dt);
            if (dir != Vector2.zero && m_Selected != null)
            {
                UIWorldMapIcon next = Pick(map, manager, cam, m_Selected, dir);
                if (next != null)
                {
                    m_Selected = next;
                    Vector2 p = ScreenPoint(cam, next);
                    if (!SafeArea(area).Contains(p))
                    {
                        m_Centering = true;
                        m_CenterTimer = 0f;
                    }
                }
            }

            if (m_Selected == null)
            {
                return;
            }
            if (m_Selected != before)
            {
                Plugin.Log.LogInfo($"World map: {NameOf(m_Selected)}");
            }
            if (m_Centering)
            {
                m_CenterTimer += dt;
                Vector2 delta = area.center - ScreenPoint(cam, m_Selected);
                if (delta.magnitude < 2f || m_CenterTimer > CenterSeconds)
                {
                    m_Centering = false;
                }
                else
                {
                    Pan(cam, drag, delta * (1f - Mathf.Exp(-12f * dt)));
                }
            }
            // The cursor sits on the chosen location so A clicks it (the game's hover, tooltip and click all apply).
            Vector2 target = ScreenPoint(cam, m_Selected);
            if (area.Contains(target) && (target - MouseEmulator.Position).sqrMagnitude > 1f)
            {
                MouseEmulator.MoveTo(target);
            }
        }

        // ---- input ----

        /// <summary>D-pad (with repeat) or LS in any direction: once on push, then repeating while held.</summary>
        private Vector2 Direction(Gamepad pad, float dt)
        {
            if (pad.GetButtonRepeat(PadButton.DpadUp)) return Vector2.up;
            if (pad.GetButtonRepeat(PadButton.DpadDown)) return Vector2.down;
            if (pad.GetButtonRepeat(PadButton.DpadLeft)) return Vector2.left;
            if (pad.GetButtonRepeat(PadButton.DpadRight)) return Vector2.right;
            Vector2 stick = pad.LeftStick;
            if (stick.magnitude < StickThreshold)
            {
                m_StickHeld = false;
                return Vector2.zero;
            }
            if (!m_StickHeld)
            {
                m_StickHeld = true;
                m_StickTimer = StickRepeatDelay;
                return stick.normalized;
            }
            m_StickTimer -= dt;
            if (m_StickTimer > 0f)
            {
                return Vector2.zero;
            }
            m_StickTimer = StickRepeatInterval;
            return stick.normalized;
        }

        // ---- locations ----

        /// <summary>A location you can step onto: shown, not locked, and either a place or a sub-map entrance.</summary>
        private static bool IsStop(UIWorldMapIcons manager, UIWorldMapIcon icon)
        {
            if (icon == null || icon == manager.BaseIcon || !icon.gameObject.activeInHierarchy)
            {
                return false;
            }
            UIWidget w = icon.Widget;
            if (w == null || !w.gameObject.activeInHierarchy)
            {
                return false;
            }
            MapData data = icon.GetData();
            if (data == null)
            {
                return !string.IsNullOrEmpty(icon.VirtualForTag);
            }
            return data.Visibility != MapData.VisibilityType.Locked;
        }

        /// <summary>Where the party is (the game's "current location" icon), if it is on this map.</summary>
        private static UIWorldMapIcon Here(UIWorldMapManager map, UIWorldMapIcons manager)
        {
            MapData current = map.GetCurrentMap();
            if (current == null)
            {
                return null;
            }
            UIWorldMapIcon icon = manager.IconForScene(current.SceneName, false);
            return IsStop(manager, icon) ? icon : null;
        }

        private static UIWorldMapIcon Nearest(UIWorldMapIcons manager, Camera cam, Vector2 point)
        {
            UIWorldMapIcon best = null;
            float bestDist = float.MaxValue;
            foreach (UIWorldMapIcon icon in manager.GetIcons())
            {
                if (!IsStop(manager, icon))
                {
                    continue;
                }
                float d = (ScreenPoint(cam, icon) - point).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = icon;
                }
            }
            return best;
        }

        /// <summary>
        /// The next location in <paramref name="dir"/>: within 60° of it, nearer and straighter is better, and a
        /// location a road leads to counts as closer (so the stick follows the roads the map draws).
        /// </summary>
        private static UIWorldMapIcon Pick(UIWorldMapManager map, UIWorldMapIcons manager, Camera cam, UIWorldMapIcon from, Vector2 dir)
        {
            Vector2 origin = ScreenPoint(cam, from);
            HashSet<UIWorldMapIcon> roads = RoadsFrom(map, manager, from);
            UIWorldMapIcon best = null;
            float bestScore = float.MaxValue;
            foreach (UIWorldMapIcon icon in manager.GetIcons())
            {
                if (icon == from || !IsStop(manager, icon))
                {
                    continue;
                }
                Vector2 d = ScreenPoint(cam, icon) - origin;
                float dist = d.magnitude;
                if (dist < 4f)
                {
                    continue;
                }
                float angle = Vector2.Angle(d, dir);
                if (angle > MaxStepAngle)
                {
                    continue;
                }
                float score = dist * (1f + 1.5f * angle / MaxStepAngle) * (roads.Contains(icon) ? RoadBonus : 1f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = icon;
                }
            }
            return best;
        }

        private static HashSet<UIWorldMapIcon> RoadsFrom(UIWorldMapManager map, UIWorldMapIcons manager, UIWorldMapIcon from)
        {
            HashSet<UIWorldMapIcon> roads = new HashSet<UIWorldMapIcon>();
            void Add(List<WorldMapLink> links)
            {
                if (links == null)
                {
                    return;
                }
                foreach (WorldMapLink link in links)
                {
                    if (link == null)
                    {
                        continue;
                    }
                    if (link.Place1 == from && link.Place2 != null)
                    {
                        roads.Add(link.Place2);
                    }
                    else if (link.Place2 == from && link.Place1 != null)
                    {
                        roads.Add(link.Place1);
                    }
                }
            }
            if (manager.LinkManager != null)
            {
                Add(manager.LinkManager.MapLinks);
            }
            if (map.MasterLinks != null)
            {
                Add(map.MasterLinks.MapLinks);
            }
            return roads;
        }

        private static Vector2 ScreenPoint(Camera cam, UIWorldMapIcon icon)
        {
            Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(icon.Widget.cachedTransform);
            Vector3 p = cam.WorldToScreenPoint(b.center);
            return new Vector2(p.x, p.y);
        }

        private static string NameOf(UIWorldMapIcon icon)
        {
            if (icon.Label != null && !string.IsNullOrEmpty(icon.Label.text))
            {
                return NGUITools.StripSymbols(icon.Label.text);
            }
            return icon.name;
        }

        // ---- the map panel ----

        /// <summary>The visible map area on screen (Unity coordinates): the draggable panel's clip rectangle.</summary>
        private static Rect MapArea(Camera cam, UIDraggablePanel drag)
        {
            UIPanel panel = drag.panel;
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            if (panel == null || panel.clipping == UIDrawCall.Clipping.None)
            {
                return screen;
            }
            Vector4 clip = panel.clipRange;
            if (clip.z <= 0f || clip.w <= 0f)
            {
                return screen;
            }
            Transform t = panel.cachedTransform;
            Vector3 a = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(clip.x - clip.z * 0.5f, clip.y - clip.w * 0.5f, 0f)));
            Vector3 b = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(clip.x + clip.z * 0.5f, clip.y + clip.w * 0.5f, 0f)));
            Rect r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            r.xMin = Mathf.Max(r.xMin, 0f);
            r.yMin = Mathf.Max(r.yMin, 0f);
            r.xMax = Mathf.Min(r.xMax, Screen.width);
            r.yMax = Mathf.Min(r.yMax, Screen.height);
            return r.width > 8f && r.height > 8f ? r : screen;
        }

        private static Rect SafeArea(Rect area)
        {
            float w = area.width * SafeAreaFraction;
            float h = area.height * SafeAreaFraction;
            return new Rect(area.center.x - w * 0.5f, area.center.y - h * 0.5f, w, h);
        }

        /// <summary>Moves the map content by <paramref name="screenDelta"/> pixels, kept inside the map's edges.</summary>
        private static void Pan(Camera cam, UIDraggablePanel drag, Vector2 screenDelta)
        {
            Vector3 w0 = cam.ScreenToWorldPoint(new Vector3(0f, 0f, 0f));
            Vector3 w1 = cam.ScreenToWorldPoint(new Vector3(screenDelta.x, screenDelta.y, 0f));
            Transform parent = drag.transform.parent;
            Vector3 local = parent != null ? parent.InverseTransformVector(w1 - w0) : w1 - w0;
            local.z = 0f;
            drag.DisableSpring();
            drag.MoveRelative(local);
            drag.RestrictWithinBounds(true);
        }

        // ---- prompts ----

        protected override void AddNavigationPrompts(List<Prompt> into)
        {
            if (!On)
            {
                base.AddNavigationPrompts(into);
                return;
            }
            if (CursorIsFree)
            {
                AddNavigatePrompt(into);
                into.Add(new Prompt("south", "Select"));
            }
            else
            {
                into.Add(new Prompt(new[] { "dpad", "lstick" }, "Choose location"));
                string label = Compat.Features.Get(Compat.Features.WorldMap, () => SelectedLabel(), null);
                if (label != null)
                {
                    into.Add(new Prompt("south", label));
                }
            }
            into.Add(new Prompt("rstick", "Pan map"));
            into.Add(new Prompt("rstick_click", "Current location"));
        }

        /// <summary>What A does on the chosen location: travel (with the game's travel time), open a sub-map, or nothing here.</summary>
        private string SelectedLabel()
        {
            UIWorldMapManager map = UIWorldMapManager.Instance;
            UIWorldMapIcon icon = m_Selected;
            if (map == null || icon == null)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(icon.VirtualForTag))
            {
                return "Open";
            }
            MapData data = icon.GetData();
            MapData current = map.GetCurrentMap();
            if (data == null || current == null || data == current || !icon.CanTravel)
            {
                return null;
            }
            if (map.MasterLinks != null && map.MasterLinks.GetConnected(current, data))
            {
                return $"Travel ({map.MasterLinks.TravelTimeTo(data).FormatNonZero(1)})";
            }
            return "Select";
        }
    }
}
