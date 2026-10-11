using System.Collections.Generic;
using PoEController.Glyphs;
using PoEController.Input;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Controller glyphs on the game's own buttons (e.g. X on an inspect window's Compare button, A/B on a message
    /// box's buttons). The active scheme registers them every frame while building its prompts (the router clears the
    /// list first), so a glyph appears exactly when that pad button works; drawn centred above the button — beside it was ambiguous for side-by-side buttons.
    /// </summary>
    internal static class ButtonHints
    {
        private const float RefHeight = 1080f;

        public enum Placement
        {
            /// <summary>Centred above the button (text buttons in windows).</summary>
            Above,
            /// <summary>Small badge on the button's top-right corner (dense icon grids like the HUD bar).</summary>
            Badge,
        }

        private struct Hint
        {
            public GameObject Target;
            public string Glyph;
            public Placement Placement;
        }

        private static readonly List<UIWidget> s_Widgets = new List<UIWidget>();
        private static readonly HashSet<GameObject> s_Logged = new HashSet<GameObject>();

        private static readonly List<Hint> s_Hints = new List<Hint>();

        /// <summary>Gap between a button's top edge and its glyph, on the physical screen.</summary>
        private const float GapMillimetres = 1f;

        public static void Clear() => s_Hints.Clear();

        /// <summary>The gap in pixels (Screen.dpi; 96 dpi when the OS doesn't report it).</summary>
        private static float Gap()
        {
            float dpi = Screen.dpi > 1f ? Screen.dpi : 96f;
            return GapMillimetres * dpi / 25.4f;
        }

        public static void Add(GameObject target, string glyph, Placement placement = Placement.Above)
        {
            if (target != null && glyph != null)
            {
                s_Hints.Add(new Hint { Target = target, Glyph = glyph, Placement = placement });
            }
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (Transform parent = t.parent; parent != null && p.Length < 160; parent = parent.parent)
            {
                p = parent.name + "/" + p;
            }
            return p;
        }

        /// <summary>
        /// Is the button really drawn? Active objects can still be invisible (alpha 0, clipped, or a hidden HUD
        /// variant), which left glyphs floating over nothing. Visible = at least one of its widgets is (NGUI's own
        /// isVisible: not clipped by its panel and alpha above zero).
        /// </summary>
        private static bool Visible(GameObject target)
        {
            target.GetComponentsInChildren(false, s_Widgets);
            foreach (UIWidget w in s_Widgets)
            {
                if (w != null && w.enabled && w.isVisible)
                {
                    return true;
                }
            }
            return false;
        }

        public static void Draw(GlyphLibrary library, GlyphFamily family, float scale)
        {
            if (s_Hints.Count == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float s = Screen.height / RefHeight * scale;
            foreach (Hint h in s_Hints)
            {
                if (h.Target == null || !h.Target.activeInHierarchy || !Visible(h.Target))
                {
                    continue;
                }
                UICamera uiCam = UICamera.FindCameraForLayer(h.Target.layer);
                Camera cam = uiCam != null ? uiCam.cachedCamera : null;
                Texture2D tex = library.Get(family, h.Glyph);
                if (cam == null || tex == null)
                {
                    continue;
                }
                Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(h.Target.transform);
                Vector3 min = cam.WorldToScreenPoint(b.min);
                Vector3 max = cam.WorldToScreenPoint(b.max);
                Rect r = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Screen.height - Mathf.Max(min.y, max.y),
                    Mathf.Max(min.x, max.x), Screen.height - Mathf.Min(min.y, max.y));
                if (r.width < 4f || r.height < 4f)
                {
                    continue;
                }
                if (h.Placement == Placement.Badge)
                {
                    // Overlaps the icon's top-right corner, half in, half out: clearly attached to that icon only.
                    float bh = Mathf.Clamp(r.height * 0.5f, 16f * s, 28f * s);
                    float bw = tex.height > 0 ? bh * tex.width / tex.height : bh;
                    GUI.DrawTexture(new Rect(r.xMax - bw * 0.6f, r.yMin - bh * 0.4f, bw, bh), tex, ScaleMode.ScaleToFit);
                    if (s_Logged.Add(h.Target))
                    {
                        Plugin.Log.LogInfo($"HUD glyph {h.Glyph} on '{Path(h.Target.transform)}' at {r}");
                    }
                    continue;
                }
                float gh = Mathf.Clamp(r.height * 0.9f, 22f * s, 40f * s);
                float gw = tex.height > 0 ? gh * tex.width / tex.height : gh;
                GUI.DrawTexture(new Rect(r.center.x - gw * 0.5f, r.yMin - Gap() - gh, gw, gh), tex, ScaleMode.ScaleToFit);
            }
        }
    }
}
