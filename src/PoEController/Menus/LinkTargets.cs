using System.Collections.Generic;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// Highlighted words inside text (glossary terms such as "Endurance", buff/debuff values). The game makes the
    /// whole label one collider and works out the word under the mouse from the text layout
    /// (<see cref="UIGlossaryEnabledLabel"/>), so for the d-pad each link becomes its own stop and the focus frame
    /// hugs the word. The layout comes from the same public NGUI calls the label's own hit test uses
    /// (<c>UILabel.GetCharacterIndexAtPosition</c>): <c>UIFont.PrintExactCharacterPositions</c> + <c>UILabel.ApplyOffset</c>.
    /// </summary>
    internal static class LinkTargets
    {
        /// <summary>A run of one link's characters on one line, in the label's layout space.</summary>
        private struct Segment
        {
            public int Url;
            public Vector2 Min;
            public Vector2 Max;
        }

        private static readonly BetterList<Vector3> s_Verts = new BetterList<Vector3>();
        private static readonly BetterList<int> s_Indices = new BetterList<int>();
        private static readonly List<Segment> s_Segments = new List<Segment>();
        private static readonly Dictionary<Collider, UIGlossaryEnabledLabel> s_ExtraColliders = new Dictionary<Collider, UIGlossaryEnabledLabel>();

        private static bool s_Broken => !Compat.Features.On(Compat.Features.LinkStops);
        private static UILabel s_CachedLabel;
        private static string s_CachedText;
        private static Vector2 s_CachedSize;

        /// <summary>Remembers labels whose hover comes from a separate collider (<c>ExtraCollider</c>).</summary>
        public static void IndexWindow(Transform root)
        {
            s_ExtraColliders.Clear();
            if (s_Broken)
            {
                return;
            }
            try
            {
                IndexWindowUnsafe(root);
            }
            catch (System.Exception e)
            {
                Fail(e);
            }
        }

        private static void IndexWindowUnsafe(Transform root)
        {
            UIGlossaryEnabledLabel[] labels = root != null
                ? root.GetComponentsInChildren<UIGlossaryEnabledLabel>(false)
                : Object.FindObjectsOfType<UIGlossaryEnabledLabel>();
            foreach (UIGlossaryEnabledLabel l in labels)
            {
                Collider extra = l.ExtraCollider != null ? l.ExtraCollider.GetComponent<Collider>() : null;
                if (extra != null)
                {
                    s_ExtraColliders[extra] = l;
                }
            }
        }

        /// <summary>
        /// Adds one point per visible link in the text behind <paramref name="c"/> (centre of the link's first line).
        /// Returns how many were added; 0 = not link text, use the collider as a whole.
        /// </summary>
        public static int AddLinkPoints(Collider c, Camera cam, List<Vector2> into)
        {
            if (s_Broken)
            {
                return 0;
            }
            int before = into.Count;
            try
            {
                return AddLinkPointsUnsafe(c, cam, into);
            }
            catch (System.Exception e)
            {
                into.RemoveRange(before, into.Count - before);
                Fail(e);
                return 0;
            }
        }

        private static int AddLinkPointsUnsafe(Collider c, Camera cam, List<Vector2> into)
        {
            UILabel label = LabelOf(c);
            if (label == null || !Layout(label))
            {
                return 0;
            }
            int added = 0;
            int lastUrl = -1;
            for (int i = 0; i < s_Segments.Count; i++)
            {
                Segment s = s_Segments[i];
                if (s.Url == lastUrl)
                {
                    continue; // a link wrapped onto the next line: its first line is enough
                }
                lastUrl = s.Url;
                Vector3 p = cam.WorldToScreenPoint(ToWorld(label, (s.Min + s.Max) * 0.5f));
                if (p.x < 0f || p.y < 0f || p.x >= Screen.width || p.y >= Screen.height)
                {
                    continue;
                }
                // Visible, not scrolled away, not covered: the same test a real mouse click gets.
                if (!UICamera.Raycast(p, out RaycastHit hit) || hit.collider != c)
                {
                    continue;
                }
                into.Add(new Vector2(p.x, p.y));
                added++;
            }
            return added;
        }

        /// <summary>Screen rectangle (Unity coords, y up) of the link under <paramref name="screenPos"/> in the text behind <paramref name="c"/>.</summary>
        public static bool TryGetLinkRect(Collider c, Camera cam, Vector2 screenPos, out Rect rect)
        {
            rect = default;
            if (s_Broken)
            {
                return false;
            }
            try
            {
                return TryGetLinkRectUnsafe(c, cam, screenPos, out rect);
            }
            catch (System.Exception e)
            {
                Fail(e);
                return false;
            }
        }

        /// <summary>Link handling is a nicety: on any error, turn it off and treat text as whole elements again.</summary>
        private static void Fail(System.Exception e)
        {
            Compat.Features.Off(Compat.Features.LinkStops, "error: " + e);
            s_ExtraColliders.Clear();
            s_Segments.Clear();
            s_CachedLabel = null;
        }

        private static bool TryGetLinkRectUnsafe(Collider c, Camera cam, Vector2 screenPos, out Rect rect)
        {
            rect = default;
            UILabel label = LabelOf(c);
            if (label == null || !Layout(label))
            {
                return false;
            }
            Transform t = label.cachedTransform;
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            world.z = t.position.z;
            Vector3 local = t.InverseTransformPoint(world);
            local.Scale(t.localScale);
            for (int i = 0; i < s_Segments.Count; i++)
            {
                Segment s = s_Segments[i];
                if (local.x < s.Min.x || local.x > s.Max.x || local.y < s.Min.y || local.y > s.Max.y)
                {
                    continue;
                }
                Vector3 a = cam.WorldToScreenPoint(ToWorld(label, s.Min));
                Vector3 b = cam.WorldToScreenPoint(ToWorld(label, s.Max));
                rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                return true;
            }
            return false;
        }

        private static UILabel LabelOf(Collider c)
        {
            UIGlossaryEnabledLabel g = c.GetComponent<UIGlossaryEnabledLabel>();
            if (g == null)
            {
                s_ExtraColliders.TryGetValue(c, out g);
            }
            if (g == null || !g.enabled)
            {
                return null;
            }
            UILabel label = g.GetComponent<UILabel>();
            return label != null && label.font != null && label.text != null && label.text.IndexOf("[url=") >= 0 ? label : null;
        }

        private static Vector3 ToWorld(UILabel label, Vector2 p) => LabelLayout.ToWorld(label, p);

        /// <summary>Fills <see cref="s_Segments"/> for <paramref name="label"/>; cached until its text or size changes.</summary>
        private static bool Layout(UILabel label)
        {
            string text = label.text;
            Vector2 size = label.relativeSize;
            if (label == s_CachedLabel && ReferenceEquals(text, s_CachedText) && size == s_CachedSize)
            {
                return s_Segments.Count > 0;
            }
            s_CachedLabel = label;
            s_CachedText = text;
            s_CachedSize = size;
            s_Segments.Clear();

            if (!LabelLayout.Print(label, s_Verts, s_Indices))
            {
                return false;
            }

            // Two vertices per character: bottom-left, top-right. Merge neighbours of the same link on the same line.
            for (int i = 0; i < s_Indices.size && i * 2 + 1 < s_Verts.size; i++)
            {
                int url = label.GetUrlIndexAtCharacterIndex(s_Indices[i]);
                if (url < 0)
                {
                    continue;
                }
                Vector3 a = s_Verts[i * 2];
                Vector3 b = s_Verts[i * 2 + 1];
                int last = s_Segments.Count - 1;
                if (last >= 0 && s_Segments[last].Url == url && Mathf.Approximately(s_Segments[last].Min.y, a.y))
                {
                    Segment s = s_Segments[last];
                    s.Max = new Vector2(Mathf.Max(s.Max.x, b.x), Mathf.Max(s.Max.y, b.y));
                    s_Segments[last] = s;
                }
                else
                {
                    s_Segments.Add(new Segment { Url = url, Min = new Vector2(a.x, a.y), Max = new Vector2(b.x, b.y) });
                }
            }
            s_Verts.Clear();
            s_Indices.Clear();
            return s_Segments.Count > 0;
        }
    }
}
