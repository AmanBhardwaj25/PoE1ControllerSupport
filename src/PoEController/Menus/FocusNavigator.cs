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
        private static readonly List<Collider> s_Colliders = new List<Collider>(512);

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
            for (int i = 0; i < s_Colliders.Count; i++)
            {
                Collider c = s_Colliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || ((1 << c.gameObject.layer) & uiMask) == 0)
                {
                    continue;
                }
                Bounds b = c.bounds;
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
            }
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
            Rect r = ScreenRect(cam, hit.collider.bounds.min, hit.collider.bounds.max);
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
