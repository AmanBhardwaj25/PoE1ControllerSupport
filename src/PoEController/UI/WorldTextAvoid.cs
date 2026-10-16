using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Keeps the in-world A prompt off the game's own floating text: barks (an examined object's description, a
    /// character's spoken line) and the target's name card. The prompt is moved up, above whatever it would cover.
    /// </summary>
    internal static class WorldTextAvoid
    {
        /// <summary>Space left between the prompt and the text below it (pixels at 1080p).</summary>
        private const float GapAt1080 = 4f;
        private const int MaxLifts = 6;

        private static readonly List<Rect> s_Obstacles = new List<Rect>();
        private static int s_Frame = -1;

        /// <summary>
        /// The bottom edge for the prompt: <paramref name="bottomY"/>, or higher, until the prompt's box
        /// (<paramref name="boxAt"/> for a bottom edge) overlaps none of the game's floating text (GUI coordinates).
        /// <paramref name="extra"/> = one more thing to stay off (the name card), if any.
        /// </summary>
        public static float Lift(float bottomY, Func<float, Rect> boxAt, Rect? extra)
        {
            List<Rect> obstacles = Obstacles();
            float gap = GapAt1080 * Screen.height / 1080f;
            for (int i = 0; i < MaxLifts; i++)
            {
                Rect box = boxAt(bottomY);
                Rect? hit = extra.HasValue && extra.Value.Overlaps(box) ? extra : null;
                foreach (Rect r in obstacles)
                {
                    if (r.Overlaps(box) && (!hit.HasValue || r.yMin < hit.Value.yMin))
                    {
                        hit = r;
                    }
                }
                if (!hit.HasValue)
                {
                    break;
                }
                bottomY = hit.Value.yMin - gap;
            }
            return bottomY;
        }

        /// <summary>Screen rectangles of the barks showing now (measured once a frame).</summary>
        private static List<Rect> Obstacles()
        {
            if (s_Frame == Time.frameCount)
            {
                return s_Obstacles;
            }
            s_Frame = Time.frameCount;
            s_Obstacles.Clear();
            UIBarkstringManager barks = UIBarkstringManager.Instance;
            if (barks == null)
            {
                return s_Obstacles;
            }
            foreach (UIBarkString b in barks.GetActiveBarks())
            {
                if (b != null && b.gameObject.activeInHierarchy && TryScreenRect(b.transform, out Rect r))
                {
                    s_Obstacles.Add(r);
                }
            }
            return s_Obstacles;
        }

        /// <summary>An NGUI element's on-screen rectangle (GUI coordinates, origin top-left).</summary>
        public static bool TryScreenRect(Transform t, out Rect rect)
        {
            rect = default;
            Camera ui = NGUITools.FindCameraForLayer(t.gameObject.layer);
            if (ui == null)
            {
                return false;
            }
            Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(t);
            Vector3 min = ui.WorldToScreenPoint(b.min);
            Vector3 max = ui.WorldToScreenPoint(b.max);
            rect = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Screen.height - Mathf.Max(min.y, max.y),
                Mathf.Max(min.x, max.x), Screen.height - Mathf.Min(min.y, max.y));
            // Nothing measured, or something implausibly large: ignored.
            return rect.width > 8f && rect.height > 8f && rect.width < Screen.width * 0.6f && rect.height < Screen.height * 0.5f;
        }
    }
}
