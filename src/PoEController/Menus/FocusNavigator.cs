using System.Collections.Generic;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// Finds the clickable NGUI elements on screen and picks the best one in a direction. An element counts only if
    /// a UI raycast at its centre actually hits it, which filters out hidden, clipped (scrolled away) and covered
    /// elements exactly the way a real mouse would see them.
    /// </summary>
    internal static class FocusNavigator
    {
        /// <summary>Colliders covering more than this fraction of the screen in both axes are backgrounds/blockers.</summary>
        private const float MaxElementScreenFraction = 0.45f;
        private const float MinElementPixels = 6f;
        /// <summary>Half-angle of the search cone; tan(60°).</summary>
        private const float ConeSlope = 1.73f;
        private const float PerpendicularWeight = 2.5f;

        private static readonly List<Vector2> s_Points = new List<Vector2>(256);
        private static readonly List<Vector2> s_ElementPoints = new List<Vector2>(256);

        /// <summary>
        /// The last Collect's stops without the highlighted words in texts (buttons, slots, options ...): where the
        /// focus starts or lands again after a page change; empty when the window has only words.
        /// </summary>
        public static List<Vector2> LastElementPoints => s_ElementPoints;

        private static readonly List<Vector2> s_SelectedPoints = new List<Vector2>(16);

        /// <summary>The last Collect's stops the game shows as selected (a held-down option button, a checked radio button).</summary>
        public static List<Vector2> LastSelectedPoints => s_SelectedPoints;
        private static readonly List<Collider> s_Colliders = new List<Collider>(512);

        /// <summary>
        /// Only elements a click would act on are stops (<see cref="ClickTargets"/>): scroll areas, scroll bars,
        /// frames, backgrounds and other click blockers are skipped, and the pieces of one button are one stop.
        /// Off = the old rule (every visible collider). Set from config Menus.OnlyClickableStops.
        /// </summary>
        public static bool OnlyClickable = true;

        /// <summary>
        /// Tooltip mode (hold L3): Collect returns only what shows a tooltip, the highlighted words in texts and
        /// tooltip-only elements (stat icons ...), in place of the normal stops. Set by the menu during Collect.
        /// </summary>
        public static bool TooltipMode;

        /// <summary>Tooltip mode within one panel only (Y: the description next to a list), or null = the whole window.</summary>
        public static Transform TooltipScope;

        /// <summary>
        /// The menu has a tooltip mode (Console style): highlighted words and tooltip-only elements are left out of the
        /// normal stops, so they don't get in the way. Off (PC layout): they stay normal stops. Set during Collect.
        /// </summary>
        public static bool TooltipsSeparate;

        /// <summary>The active menu's own "not a stop" rule (page tabs it reaches with LB / RB ...), during Collect.</summary>
        public static System.Func<Collider, bool> SkipAlso;

        /// <summary>The active menu's "start here when nothing is selected" rule (a page's list of choices), during Collect.</summary>
        public static System.Func<Collider, bool> PreferAlso;

        /// <summary>
        /// The active menu's "a stop after all" rule: a button with its own pad button (a glyph) that the menu still
        /// wants on the d-pad (e.g. Done on the last page of character creation), during Collect.
        /// </summary>
        public static System.Func<Collider, bool> KeepAlso;

        /// <summary>
        /// The active menu's rows: colliders that belong to one row (a value with its - / + buttons) map to the row,
        /// which becomes one stop at its middle (set by the menu while it is active).
        /// </summary>
        public static System.Func<Collider, Transform> GroupAlso;

        private struct GroupStop
        {
            public Vector2 Point;
            public Transform Row;
            public Bounds Bounds;
        }

        private static readonly List<GroupStop> s_Groups = new List<GroupStop>(16);

        /// <summary>The row whose stop is at <paramref name="screenPos"/> (the cursor on a row), or null.</summary>
        public static Transform RowAt(Vector2 screenPos)
        {
            foreach (GroupStop g in s_Groups)
            {
                if (g.Row != null && g.Row.gameObject.activeInHierarchy && (g.Point - screenPos).sqrMagnitude < 9f)
                {
                    return g.Row;
                }
            }
            return null;
        }

        private static readonly List<Vector2> s_PreferredPoints = new List<Vector2>(32);

        /// <summary>The last Collect's stops the menu prefers to start on (see <see cref="PreferAlso"/>).</summary>
        public static List<Vector2> LastPreferredPoints => s_PreferredPoints;

        private static readonly HashSet<int> s_Owners = new HashSet<int>();
        private static readonly List<Collider> s_OwnerColliders = new List<Collider>(16);
        private static readonly Dictionary<string, string> s_Logged = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> s_Dropped = new Dictionary<string, int>();
        private static readonly List<string> s_DroppedNames = new List<string>();
        private static readonly List<string> s_KeptNames = new List<string>();
        private static int s_Merged;
        private static string s_Window;

        /// <summary>Screen positions (Unity coords) of every reachable element under <paramref name="root"/>, or the whole UI if null.</summary>
        public static List<Vector2> Collect(Transform root)
        {
            return Collect(root, null);
        }

        private static readonly List<Collider> s_Extra = new List<Collider>(256);

        /// <summary>As <see cref="Collect(Transform)"/>, also including elements under <paramref name="extraRoot"/> (a window below the top one).</summary>
        public static List<Vector2> Collect(Transform root, Transform extraRoot)
        {
            s_Points.Clear();
            Camera cam = UICamera.mainCamera;
            if (cam == null)
            {
                return s_Points;
            }
            s_Colliders.Clear();
            if (root != null)
            {
                root.GetComponentsInChildren(false, s_Colliders);
                if (extraRoot != null)
                {
                    extraRoot.GetComponentsInChildren(false, s_Extra);
                    s_Colliders.AddRange(s_Extra);
                    s_Extra.Clear();
                }
            }
            else
            {
                s_Colliders.AddRange(Object.FindObjectsOfType<Collider>());
            }

            int uiMask = cam.cullingMask;
            s_Window = root != null ? root.name : "whole UI";
            float maxW = Screen.width * MaxElementScreenFraction;
            float maxH = Screen.height * MaxElementScreenFraction;
            LinkTargets.IndexWindow(root);
            Camera previous = UICamera.currentCamera;
            try
            {
                CollectVisible(cam, uiMask, maxW, maxH);
            }
            finally
            {
                UICamera.currentCamera = previous;
            }
            return s_Points;
        }

        private static void CollectVisible(Camera cam, int uiMask, float maxW, float maxH)
        {
            s_Owners.Clear();
            s_ElementPoints.Clear();
            s_SelectedPoints.Clear();
            s_PreferredPoints.Clear();
            s_Groups.Clear();
            s_Dropped.Clear();
            s_DroppedNames.Clear();
            s_KeptNames.Clear();
            s_KeptTargets.Clear();
            s_Merged = 0;
            for (int i = 0; i < s_Colliders.Count; i++)
            {
                Collider c = s_Colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || ((1 << c.gameObject.layer) & uiMask) == 0)
                {
                    continue;
                }
                Bounds b = c.bounds;
                string reason = "";
                if (TooltipMode)
                {
                    if (TooltipScope == null || c.transform.IsChildOf(TooltipScope))
                    {
                        AddTooltipStop(c, cam, b);
                    }
                    continue;
                }
                if (OnlyClickable)
                {
                    ClickTargets.Kind kind = ClickTargets.Classify(c, out Component owner);
                    if (kind == ClickTargets.Kind.Scroll || kind == ClickTargets.Kind.Blocker || kind == ClickTargets.Kind.Text)
                    {
                        // Text with highlighted words: each word is a stop, unless tooltip mode reaches them.
                        if (TooltipsSeparate || LinkTargets.AddLinkPoints(c, cam, s_Points) == 0)
                        {
                            Drop(kind, c);
                        }
                        continue;
                    }
                    // Buttons with their own pad button (they show its glyph) and the menu's page tabs: not stops.
                    bool keep = KeepAlso != null && KeepAlso(c);
                    if (!keep && (UI.ButtonHints.Covers(c.transform) || (kind == ClickTargets.Kind.Piece && UI.ButtonHints.Covers(owner.transform))
                        || (SkipAlso != null && SkipAlso(c))))
                    {
                        Drop("shortcut", c);
                        continue;
                    }
                    Transform row = GroupAlso != null ? GroupAlso(c) : null;
                    if (row != null)
                    {
                        AddRow(c, row, cam, b);
                        continue;
                    }
                    if (kind == ClickTargets.Kind.Piece)
                    {
                        if (!s_Owners.Add(owner.GetInstanceID()))
                        {
                            continue;
                        }
                        s_Merged++;
                        b = OwnerBounds(owner, b);
                        Vector3 mid = cam.WorldToScreenPoint(b.center);
                        if (mid.x < 0f || mid.y < 0f || mid.x >= Screen.width || mid.y >= Screen.height)
                        {
                            continue;
                        }
                        // The button counts when a click at its centre lands on any of its pieces.
                        if (!UICamera.Raycast(mid, out RaycastHit pieceHit) || !BelongsTo(pieceHit.collider, owner))
                        {
                            continue;
                        }
                        s_Points.Add(new Vector2(mid.x, mid.y));
                        s_ElementPoints.Add(new Vector2(mid.x, mid.y));
                        if (ClickTargets.IsSelected(owner))
                        {
                            s_SelectedPoints.Add(new Vector2(mid.x, mid.y));
                        }
                        if (PreferAlso != null && PreferAlso(c))
                        {
                            s_PreferredPoints.Add(new Vector2(mid.x, mid.y));
                        }
                        Keep(owner.name, "button");
                        continue;
                    }
                    if (kind == ClickTargets.Kind.Tooltip)
                    {
                        // The label of one button (an option and its text): part of that button, not a stop of its own.
                        Collider ownerTarget = ClickTargets.TooltipOwner(c);
                        if (ownerTarget != null)
                        {
                            s_Merged++;
                            Drop(ClickTargets.Kind.Piece, c);
                            continue;
                        }
                        if (TooltipsSeparate)
                        {
                            Drop(ClickTargets.Kind.Tooltip, c);
                            continue;
                        }
                        reason = "tooltip";
                    }
                    else
                    {
                        reason = ClickTargets.LastReason;
                        // One control drawn as two nested parts (an icon on its background, both with the same
                        // action component): one stop.
                        if (NestedTwin(c, reason))
                        {
                            Drop(ClickTargets.Kind.Piece, c);
                            continue;
                        }
                    }
                }
                Vector3 min = cam.WorldToScreenPoint(b.min);
                Vector3 max = cam.WorldToScreenPoint(b.max);
                float w = Mathf.Abs(max.x - min.x);
                float h = Mathf.Abs(max.y - min.y);
                if ((w > maxW && h > maxH) || w < MinElementPixels || h < MinElementPixels)
                {
                    continue;
                }
                // Text with highlighted words: each word is its own stop instead of the whole sentence.
                if (LinkTargets.AddLinkPoints(c, cam, s_Points) > 0)
                {
                    continue;
                }
                Vector3 centre = cam.WorldToScreenPoint(b.center);
                if (centre.x < 0f || centre.y < 0f || centre.x >= Screen.width || centre.y >= Screen.height)
                {
                    continue;
                }
                if (!UICamera.Raycast(centre, out RaycastHit hit) || hit.collider != c)
                {
                    continue;
                }
                s_Points.Add(new Vector2(centre.x, centre.y));
                s_ElementPoints.Add(new Vector2(centre.x, centre.y));
                if (OnlyClickable && ClickTargets.IsSelected(c))
                {
                    s_SelectedPoints.Add(new Vector2(centre.x, centre.y));
                }
                if (PreferAlso != null && PreferAlso(c))
                {
                    s_PreferredPoints.Add(new Vector2(centre.x, centre.y));
                }
                Keep(c.name, reason);
            }
            if (OnlyClickable)
            {
                LogWindow();
            }
        }

        /// <summary>
        /// Tooltip mode: every highlighted word of a text, and an element that only shows a tooltip (not the label of
        /// one button: that button shows it; not a row: the row is a normal stop), if a mouse could hover it.
        /// </summary>
        private static void AddTooltipStop(Collider c, Camera cam, Bounds b)
        {
            if (LinkTargets.AddLinkPoints(c, cam, s_Points) > 0)
            {
                return;
            }
            if (ClickTargets.Classify(c, out _) != ClickTargets.Kind.Tooltip || ClickTargets.TooltipOwner(c) != null
                || (GroupAlso != null && GroupAlso(c) != null))
            {
                return;
            }
            Vector3 centre = cam.WorldToScreenPoint(b.center);
            if (centre.x < 0f || centre.y < 0f || centre.x >= Screen.width || centre.y >= Screen.height
                || !UICamera.Raycast(centre, out RaycastHit hit) || hit.collider != c)
            {
                return;
            }
            s_Points.Add(new Vector2(centre.x, centre.y));
            Keep(c.name, "tooltip");
        }

        /// <summary>A row is one stop at its middle once any of its parts is visible (a click there hits it).</summary>
        private static void AddRow(Collider c, Transform row, Camera cam, Bounds b)
        {
            int id = row.GetInstanceID();
            if (s_Owners.Contains(id))
            {
                return;
            }
            Vector3 part = cam.WorldToScreenPoint(b.center);
            if (part.x < 0f || part.y < 0f || part.x >= Screen.width || part.y >= Screen.height
                || !UICamera.Raycast(part, out RaycastHit hit) || hit.collider == null || !hit.transform.IsChildOf(row))
            {
                return;
            }
            s_Owners.Add(id);
            Bounds rb = OwnerBounds(row, b);
            Vector3 mid = cam.WorldToScreenPoint(rb.center);
            var p = new Vector2(mid.x, mid.y);
            s_Points.Add(p);
            s_ElementPoints.Add(p);
            s_Groups.Add(new GroupStop { Point = p, Row = row, Bounds = rb });
            Keep(row.name, "row");
        }

        private static readonly List<KeyValuePair<Collider, string>> s_KeptTargets = new List<KeyValuePair<Collider, string>>(64);

        /// <summary>
        /// A target with the same acting component as one already kept, in the same parent (or one inside the other),
        /// whose area lies inside or around that one's: the same control again.
        /// </summary>
        private static bool NestedTwin(Collider c, string reason)
        {
            Bounds b = c.bounds;
            foreach (KeyValuePair<Collider, string> kv in s_KeptTargets)
            {
                Collider k = kv.Key;
                if (k == null || kv.Value != reason)
                {
                    continue;
                }
                Transform a = c.transform, o = k.transform;
                bool related = a.parent == o.parent || a.IsChildOf(o) || o.IsChildOf(a);
                if (!related)
                {
                    continue;
                }
                Bounds kb = k.bounds;
                if (Contains(kb, b) || Contains(b, kb))
                {
                    return true;
                }
            }
            s_KeptTargets.Add(new KeyValuePair<Collider, string>(c, reason));
            return false;
        }

        private static bool Contains(Bounds outer, Bounds inner)
        {
            const float slack = 0.001f;
            return inner.min.x >= outer.min.x - slack && inner.min.y >= outer.min.y - slack
                && inner.max.x <= outer.max.x + slack && inner.max.y <= outer.max.y + slack;
        }

        private static void Keep(string name, string reason)
        {
            if (s_KeptNames.Count < 60)
            {
                s_KeptNames.Add(string.IsNullOrEmpty(reason) ? name : $"{name} [{reason}]");
            }
        }

        private static void Drop(ClickTargets.Kind kind, Collider c) => Drop(kind.ToString().ToLowerInvariant(), c);

        private static void Drop(string k, Collider c)
        {
            s_Dropped.TryGetValue(k, out int n);
            s_Dropped[k] = n + 1;
            if (s_DroppedNames.Count < 8 && !s_DroppedNames.Contains(c.name))
            {
                s_DroppedNames.Add(c.name);
            }
        }

        /// <summary>Once per window and result: how many stops, buttons merged, and what was left out.</summary>
        private static void LogWindow()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in s_Dropped)
            {
                parts.Add($"{kv.Key} {kv.Value}");
            }
            string line = $"{s_Points.Count} stops ({s_Merged} buttons from pieces), left out: {(parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "nothing")}"
                + (s_DroppedNames.Count > 0 ? $" (e.g. {string.Join(", ", s_DroppedNames.ToArray())})" : "");
            if (s_Logged.Count < 200 && (!s_Logged.TryGetValue(s_Window, out string last) || last != line))
            {
                s_Logged[s_Window] = line;
                Plugin.Log.LogInfo($"Focus stops in '{s_Window}': {line}");
                Plugin.Log.LogInfo($"  stops: {string.Join("; ", s_KeptNames.ToArray())}");
            }
        }

        /// <summary>Bounds of every enabled, active collider of a button (its pieces).</summary>
        private static Bounds OwnerBounds(Component owner, Bounds fallback)
        {
            owner.GetComponentsInChildren(false, s_OwnerColliders);
            bool any = false;
            Bounds b = fallback;
            for (int i = 0; i < s_OwnerColliders.Count; i++)
            {
                Collider oc = s_OwnerColliders[i];
                if (oc == null || !oc.enabled || !oc.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!any)
                {
                    b = oc.bounds;
                    any = true;
                }
                else
                {
                    b.Encapsulate(oc.bounds);
                }
            }
            return b;
        }

        private static readonly List<Collider> s_Companions = new List<Collider>(4);
        private static readonly List<Collider> s_NewCompanions = new List<Collider>(4);
        private static Collider s_CompanionsOf;

        /// <summary>
        /// Every frame on the pad: the label(s) that belong to the focused button get its hover, so what they show on
        /// hover (an option's description, a tooltip) still appears although the label is no longer a stop. Null =
        /// no focus (menu closed, mouse in use): the last labels get their hover back off.
        /// </summary>
        public static void UpdateCompanionHover(Collider focused)
        {
            if (focused == s_CompanionsOf)
            {
                return;
            }
            s_CompanionsOf = focused;
            s_NewCompanions.Clear();
            if (focused != null && OnlyClickable)
            {
                ClickTargets.TooltipsOf(focused, s_NewCompanions);
            }
            foreach (Collider old in s_Companions)
            {
                if (old != null && !s_NewCompanions.Contains(old))
                {
                    UICamera.Notify(old.gameObject, "OnHover", false);
                }
            }
            foreach (Collider now in s_NewCompanions)
            {
                if (now != null && !s_Companions.Contains(now))
                {
                    UICamera.Notify(now.gameObject, "OnHover", true);
                }
            }
            s_Companions.Clear();
            s_Companions.AddRange(s_NewCompanions);
        }

        /// <summary>The hit collider is a piece of <paramref name="owner"/> (inside it, or registered to it from outside).</summary>
        private static bool BelongsTo(Collider hit, Component owner)
        {
            if (hit == null)
            {
                return false;
            }
            if (hit.transform == owner.transform || hit.transform.IsChildOf(owner.transform))
            {
                return true;
            }
            return ClickTargets.Classify(hit, out Component hitOwner) == ClickTargets.Kind.Piece && hitOwner == owner;
        }

        /// <summary>
        /// Screen rectangle (GUI coordinates, y down) of the element a click at <paramref name="screenPos"/> would hit,
        /// cut to the visible part of any scroll area it sits in. False over nothing clickable or over a background.
        /// </summary>
        public static bool TryGetElementRect(Vector2 screenPos, out Rect guiRect)
        {
            guiRect = default;
            if (screenPos.x < 0f || screenPos.y < 0f || screenPos.x >= Screen.width || screenPos.y >= Screen.height)
            {
                return false;
            }
            foreach (GroupStop g in s_Groups)
            {
                if (g.Row != null && g.Row.gameObject.activeInHierarchy && (g.Point - screenPos).sqrMagnitude < 9f && UICamera.mainCamera != null)
                {
                    Rect gr = ScreenRect(UICamera.mainCamera, g.Bounds.min, g.Bounds.max);
                    guiRect = new Rect(gr.x, Screen.height - gr.yMax, gr.width, gr.height);
                    return true;
                }
            }
            // UICamera.Raycast leaves the hitting camera in the static currentCamera; give NGUI back its own.
            Camera previous = UICamera.currentCamera;
            Camera cam;
            RaycastHit hit;
            bool found;
            try
            {
                found = UICamera.Raycast(screenPos, out hit);
                cam = UICamera.currentCamera;
            }
            finally
            {
                UICamera.currentCamera = previous;
            }
            if (!found || hit.collider == null || cam == null)
            {
                return false;
            }
            Bounds hb = hit.collider.bounds;
            if (OnlyClickable && ClickTargets.Classify(hit.collider, out Component owner) == ClickTargets.Kind.Piece)
            {
                hb = OwnerBounds(owner, hb);
            }
            Rect r = ScreenRect(cam, hb.min, hb.max);
            if ((r.width > Screen.width * MaxElementScreenFraction && r.height > Screen.height * MaxElementScreenFraction)
                || r.width < MinElementPixels || r.height < MinElementPixels)
            {
                return false;
            }
            if (LinkTargets.TryGetLinkRect(hit.collider, cam, screenPos, out Rect link))
            {
                r = link;
            }
            UIPanel[] panels = PanelsOf(hit.collider);
            for (int i = 0; i < panels.Length; i++)
            {
                UIPanel p = panels[i];
                if (p == null || p.clipping == UIDrawCall.Clipping.None)
                {
                    continue;
                }
                Vector4 clip = p.clipRange;
                if (clip.z <= 0f || clip.w <= 0f)
                {
                    continue;
                }
                Transform t = p.cachedTransform;
                Rect area = ScreenRect(cam,
                    t.TransformPoint(new Vector3(clip.x - clip.z * 0.5f, clip.y - clip.w * 0.5f, 0f)),
                    t.TransformPoint(new Vector3(clip.x + clip.z * 0.5f, clip.y + clip.w * 0.5f, 0f)));
                float xMin = Mathf.Max(r.xMin, area.xMin);
                float yMin = Mathf.Max(r.yMin, area.yMin);
                float xMax = Mathf.Min(r.xMax, area.xMax);
                float yMax = Mathf.Min(r.yMax, area.yMax);
                if (xMax - xMin < MinElementPixels || yMax - yMin < MinElementPixels)
                {
                    return false;
                }
                r = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
            // Unity screen space has y up; IMGUI has y down.
            guiRect = new Rect(r.x, Screen.height - r.yMax, r.width, r.height);
            return true;
        }

        /// <summary>The UI collider a click at <paramref name="screenPos"/> would hit, or null.</summary>
        public static Collider ColliderAt(Vector2 screenPos)
        {
            if (screenPos.x < 0f || screenPos.y < 0f || screenPos.x >= Screen.width || screenPos.y >= Screen.height)
            {
                return null;
            }
            Camera previous = UICamera.currentCamera;
            try
            {
                return UICamera.Raycast(screenPos, out RaycastHit hit) ? hit.collider : null;
            }
            finally
            {
                UICamera.currentCamera = previous;
            }
        }

        private static readonly HashSet<string> s_LoggedOwnHighlight = new HashSet<string>();

        /// <summary>
        /// True when the element is a game button that lights up by itself under the cursor (0.36.0: the focus
        /// frame looks out of place on real buttons — main menu, options, window buttons). Item slots keep the frame
        /// even though they carry a button inside. Each decision is logged once per element name.
        /// </summary>
        public static bool HasOwnHighlight(Collider c)
        {
            if (c == null)
            {
                return false;
            }
            GameObject go = c.gameObject;
            if (NGUITools.FindInParents<UIInventoryGridItem>(go) != null)
            {
                return false;
            }
            string kind = null;
            if (Active(go.GetComponent<UIImageButtonRevised>())) kind = "UIImageButtonRevised";
            else if (Active(go.GetComponent<UIMultiSpriteImageButton>())) kind = "UIMultiSpriteImageButton";
            else if (Active(go.GetComponent<UIButtonColor>())) kind = "UIButtonColor";
            else if (Active(go.GetComponent<UIButtonScale>())) kind = "UIButtonScale";
            else if (Active(go.GetComponent<UIButtonOffset>())) kind = "UIButtonOffset";
            else if (Active(go.GetComponent<UIButtonTween>())) kind = "UIButtonTween";
            else if (Active(go.GetComponent<UIImageButton>())) kind = "UIImageButton";
            if (s_LoggedOwnHighlight.Count < 300 && s_LoggedOwnHighlight.Add(go.name + "|" + (kind ?? "-")))
            {
                Plugin.Log.LogInfo(kind != null
                    ? $"Focus frame hidden on '{go.name}' (game button {kind})"
                    : $"Focus frame shown on '{go.name}'");
            }
            return kind != null;
        }

        private static bool Active(Behaviour b) => b != null && b.enabled;

        private static Collider s_PanelsOwner;
        private static UIPanel[] s_Panels = new UIPanel[0];

        /// <summary>Panels above <paramref name="c"/>, cached for the last collider (the focus rarely changes).</summary>
        private static UIPanel[] PanelsOf(Collider c)
        {
            if (c != s_PanelsOwner)
            {
                s_PanelsOwner = c;
                s_Panels = c.GetComponentsInParent<UIPanel>(true);
            }
            return s_Panels;
        }

        /// <summary>Screen-space (y up) rectangle spanned by two world points.</summary>
        private static Rect ScreenRect(Camera cam, Vector3 a, Vector3 b)
        {
            Vector3 sa = cam.WorldToScreenPoint(a);
            Vector3 sb = cam.WorldToScreenPoint(b);
            return Rect.MinMaxRect(Mathf.Min(sa.x, sb.x), Mathf.Min(sa.y, sb.y), Mathf.Max(sa.x, sb.x), Mathf.Max(sa.y, sb.y));
        }

        /// <summary>Best element from <paramref name="from"/> in <paramref name="direction"/> (unit vector), if any.</summary>
        public static bool TryPick(List<Vector2> points, Vector2 from, Vector2 direction, out Vector2 picked)
        {
            float best = float.MaxValue;
            picked = from;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 d = points[i] - from;
                float along = Vector2.Dot(d, direction);
                if (along < 4f)
                {
                    continue;
                }
                float perp = Mathf.Abs(d.x * direction.y - d.y * direction.x);
                if (perp > along * ConeSlope)
                {
                    continue;
                }
                float score = along + perp * PerpendicularWeight;
                if (score < best)
                {
                    best = score;
                    picked = points[i];
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>Element closest to <paramref name="target"/>.</summary>
        public static bool TryNearest(List<Vector2> points, Vector2 target, out Vector2 picked)
        {
            float best = float.MaxValue;
            picked = target;
            for (int i = 0; i < points.Count; i++)
            {
                float d = (points[i] - target).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    picked = points[i];
                }
            }
            return best < float.MaxValue;
        }
    }
}
