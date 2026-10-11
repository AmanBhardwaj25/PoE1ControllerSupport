using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// Console style's in-world markers (Switch screenshot #04):
    /// <list type="bullet">
    /// <item>Hold-A aiming: a soft light cone from the leader towards the stick, and four white arrows pointing at the
    /// target.</item>
    /// </list>
    /// Sizes follow the screen height like the rest of the mod's UI.
    /// </summary>
    internal sealed class WorldMarkersView
    {
        private const float RefHeight = 1080f;
        private const float AimConeHalfAngle = 40f;
        private static readonly Color ConeColor = new Color(1f, 0.93f, 0.75f, 0.32f);
        private static readonly Color ArrowColor = new Color(1f, 1f, 1f, 0.95f);

        private Texture2D m_Cone;
        private Texture2D m_Arrow;

        /// <summary>The cone: <paramref name="pivot"/> = leader on screen (GUI coords), angle clockwise from screen-up.</summary>
        public void DrawAimCone(Vector2 pivot, float angleDeg, float length)
        {
            if (Event.current.type != EventType.Repaint || length < 4f)
            {
                return;
            }
            if (m_Cone == null)
            {
                m_Cone = MakeCone(256);
            }
            Matrix4x4 m = GUI.matrix;
            Color old = GUI.color;
            GUIUtility.RotateAroundPivot(angleDeg, pivot);
            GUI.color = ConeColor;
            GUI.DrawTexture(new Rect(pivot.x - length, pivot.y - length, length * 2f, length), m_Cone);
            GUI.color = old;
            GUI.matrix = m;
        }

        /// <summary>Four arrows around <paramref name="target"/> (GUI rect), each pointing at it.</summary>
        public void DrawAimArrows(Rect target, float scale)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (m_Arrow == null)
            {
                m_Arrow = MakeArrow(64);
            }
            float s = Screen.height / RefHeight * scale;
            float a = 30f * s;
            // Gently pulse towards the target.
            float gap = 6f * s + Mathf.PingPong(Time.unscaledTime * 18f * s, 6f * s);
            Vector2 c = target.center;
            Color old = GUI.color;
            GUI.color = ArrowColor;
            DrawArrow(new Vector2(c.x, target.yMin - gap - a * 0.5f), 180f, a);  // above, pointing down
            DrawArrow(new Vector2(c.x, target.yMax + gap + a * 0.5f), 0f, a);    // below, pointing up
            DrawArrow(new Vector2(target.xMin - gap - a * 0.5f, c.y), 90f, a);   // left, pointing right
            DrawArrow(new Vector2(target.xMax + gap + a * 0.5f, c.y), 270f, a);  // right, pointing left
            GUI.color = old;
        }

        private void DrawArrow(Vector2 centre, float angleDeg, float size)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDeg, centre);
            GUI.DrawTexture(new Rect(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size), m_Arrow);
            GUI.matrix = m;
        }

        /// <summary>Wedge of ±40° from the bottom-middle pixel, fading out with distance and at its edges.</summary>
        private static Texture2D MakeCone(int size)
        {
            int w = size * 2;
            var tex = new Texture2D(w, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Texture rows go bottom-up: y = 0 is the cone's tip row.
                    float dx = x + 0.5f - size;
                    float dy = y + 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / size;
                    float angle = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
                    float a = 0f;
                    if (r < 1f && angle < AimConeHalfAngle)
                    {
                        float edge = Mathf.Clamp01((AimConeHalfAngle - angle) / 8f);
                        float fade = Mathf.Clamp01((1f - r) / 0.35f) * Mathf.Clamp01(r / 0.08f);
                        a = edge * fade;
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>A white triangle pointing up, with a soft edge.</summary>
        private static Texture2D MakeArrow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Bottom-up rows: apex at the top (y = size), base at y = 8.
                    float v = (y + 0.5f - 8f) / (size - 16f);
                    float half = (1f - v) * 0.5f * (size - 12f);
                    float dist = Mathf.Abs(x + 0.5f - size * 0.5f);
                    float a = v >= 0f && v <= 1f ? Mathf.Clamp01(half - dist + 0.5f) : 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
