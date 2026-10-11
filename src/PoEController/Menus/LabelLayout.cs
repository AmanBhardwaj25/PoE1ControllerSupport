using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// Where each character of an NGUI label sits, computed with the same public calls and branches the label's own
    /// hit test uses (<c>UILabel.GetCharacterIndexAtPosition</c>): <c>UIFont.PrintExactCharacterPositions</c> +
    /// <c>UILabel.ApplyOffset</c>. Positions are in "layout space" (local position × local scale).
    /// </summary>
    internal static class LabelLayout
    {
        /// <summary>
        /// Fills <paramref name="verts"/> with two vertices per printed character (bottom-left, top-right) and
        /// <paramref name="indices"/> with each one's index in <c>processedText</c>. False if nothing is printed.
        /// </summary>
        public static bool Print(UILabel label, BetterList<Vector3> verts, BetterList<int> indices)
        {
            verts.Clear();
            indices.Clear();
            string processed = label.processedText;
            UIFont font = label.font;
            if (font == null || string.IsNullOrEmpty(processed))
            {
                return false;
            }
            Vector2 size = label.relativeSize;
            bool premultiply = font.material != null && font.material.shader != null && font.material.shader.name.Contains("Premultiplied");
            switch (label.pivot)
            {
                case UIWidget.Pivot.TopLeft:
                case UIWidget.Pivot.Left:
                case UIWidget.Pivot.BottomLeft:
                    font.PrintExactCharacterPositions(processed, verts, indices, label.supportEncoding, label.symbolStyle,
                        UIFont.Alignment.Left, 0, premultiply,
                        new UIFont.IndentationData(label.indentSubsequent, label.indentAmount, label.indentFirst));
                    break;
                case UIWidget.Pivot.TopRight:
                case UIWidget.Pivot.Right:
                case UIWidget.Pivot.BottomRight:
                    font.PrintExactCharacterPositions(processed, verts, indices, label.supportEncoding, label.symbolStyle,
                        UIFont.Alignment.Right, Mathf.RoundToInt(size.x * font.size), premultiply);
                    break;
                default:
                    font.PrintExactCharacterPositions(processed, verts, indices, label.supportEncoding, label.symbolStyle,
                        UIFont.Alignment.Center, Mathf.RoundToInt(size.x * font.size), premultiply);
                    break;
            }
            if (verts.size == 0)
            {
                return false;
            }
            label.ApplyOffset(verts, 0);
            return true;
        }

        /// <summary>Layout space → world.</summary>
        public static Vector3 ToWorld(UILabel label, Vector2 p)
        {
            Transform t = label.cachedTransform;
            Vector3 s = t.localScale;
            return t.TransformPoint(new Vector3(s.x != 0f ? p.x / s.x : 0f, s.y != 0f ? p.y / s.y : 0f, 0f));
        }

        /// <summary>
        /// Visible strength of the label: its own alpha times the alpha of the panel that draws it — the nearest one.
        /// In NGUI 2 a panel's alpha does not pass down to panels inside it (a tooltip panel under a faded-out HUD
        /// panel is still fully visible). No side effects, unlike <c>finalAlpha</c>, which may create a panel.
        /// </summary>
        public static float Alpha(UILabel label)
        {
            for (Transform t = label.cachedTransform; t != null; t = t.parent)
            {
                UIPanel p = t.GetComponent<UIPanel>();
                if (p != null)
                {
                    return label.color.a * p.alpha;
                }
            }
            return label.color.a;
        }

        /// <summary>Screen rectangle (Unity coords, y up) that clipping panels above the label leave visible.</summary>
        public static Rect VisibleArea(UILabel label, Camera cam) => VisibleArea(label.cachedTransform, cam);

        /// <summary>Screen rectangle (Unity coords, y up) that clipping panels above <paramref name="target"/> leave visible.</summary>
        public static Rect VisibleArea(Transform target, Camera cam)
        {
            Rect r = new Rect(0f, 0f, Screen.width, Screen.height);
            for (Transform t = target; t != null; t = t.parent)
            {
                UIPanel p = t.GetComponent<UIPanel>();
                if (p == null || p.clipping == UIDrawCall.Clipping.None)
                {
                    continue;
                }
                Vector4 clip = p.clipRange;
                if (clip.z <= 0f || clip.w <= 0f)
                {
                    continue;
                }
                Vector3 a = cam.WorldToScreenPoint(p.cachedTransform.TransformPoint(new Vector3(clip.x - clip.z * 0.5f, clip.y - clip.w * 0.5f, 0f)));
                Vector3 b = cam.WorldToScreenPoint(p.cachedTransform.TransformPoint(new Vector3(clip.x + clip.z * 0.5f, clip.y + clip.w * 0.5f, 0f)));
                r = Rect.MinMaxRect(Mathf.Max(r.xMin, Mathf.Min(a.x, b.x)), Mathf.Max(r.yMin, Mathf.Min(a.y, b.y)),
                    Mathf.Min(r.xMax, Mathf.Max(a.x, b.x)), Mathf.Min(r.yMax, Mathf.Max(a.y, b.y)));
            }
            return r;
        }
    }
}
