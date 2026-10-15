using System.Collections.Generic;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// A controller-driven world cursor for picking targets and spots: it moves the real mouse cursor, so everything
    /// the game shows and checks on hover (attack / cast / move cursors, movement line and cost in turn-based combat,
    /// AoE previews, success chances) works unchanged, and A is a real left click, so the game's own click handling
    /// decides what happens (move, attack, cast, interact) with all of its rules.
    /// <list type="bullet">
    /// <item>LS moves it (squared response). Near the screen edge the camera follows.</item>
    /// <item>Snap: jumps to the next visible character left / right on screen and stays on it while it moves.</item>
    /// <item>Clicks are refused while the cursor is over game UI (that would press a HUD button instead).</item>
    /// </list>
    /// </summary>
    internal sealed class WorldCursor
    {
        private const float SpeedAt1080 = 1100f;
        private const float EdgeMarginX = 0.03f;
        private const float EdgeMarginTop = 0.05f;
        private const float EdgeMarginBottom = 0.05f;
        private const float CameraEdge = 0.1f;

        private readonly List<GameObject> m_Snappable = new List<GameObject>();
        private Vector2 m_Pos;
        private GameObject m_SnappedTo;
        private bool m_ClickHeld;

        public bool Active { get; private set; }

        /// <summary>Camera push requested by the cursor being near an edge (added to the right stick by the scheme).</summary>
        public Vector2 CameraPush { get; private set; }

        public GameObject SnappedTo => m_SnappedTo;

        public void Show(GameObject snapTo)
        {
            Active = true;
            m_SnappedTo = null;
            m_Pos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (snapTo != null)
            {
                SnapTo(snapTo);
            }
            else
            {
                Apply();
            }
        }

        public void Hide()
        {
            if (m_ClickHeld)
            {
                MouseEmulator.SetLeft(false);
                m_ClickHeld = false;
            }
            Active = false;
            m_SnappedTo = null;
            CameraPush = Vector2.zero;
        }

        /// <summary>Per frame while active.</summary>
        public void Update(Vector2 stick, float dt)
        {
            if (!Active)
            {
                return;
            }
            CameraPush = Vector2.zero;
            // Moving while the button is down would turn the click into a drag-select.
            if (m_ClickHeld)
            {
                stick = Vector2.zero;
            }
            if (stick != Vector2.zero)
            {
                m_SnappedTo = null;
                float speed = SpeedAt1080 * (Screen.height / 1080f) * stick.sqrMagnitude;
                m_Pos += stick.normalized * speed * dt;
                Clamp();
                Vector2 vp = new Vector2(m_Pos.x / Screen.width, m_Pos.y / Screen.height);
                Vector2 push = Vector2.zero;
                if (vp.x < EdgeMarginX + CameraEdge && stick.x < 0f) push.x = stick.x;
                if (vp.x > 1f - EdgeMarginX - CameraEdge && stick.x > 0f) push.x = stick.x;
                if (vp.y < EdgeMarginBottom + CameraEdge && stick.y < 0f) push.y = stick.y;
                if (vp.y > 1f - EdgeMarginTop - CameraEdge && stick.y > 0f) push.y = stick.y;
                CameraPush = push;
            }
            else if (m_SnappedTo != null)
            {
                if (!m_SnappedTo.activeInHierarchy)
                {
                    m_SnappedTo = null;
                }
                else if (TryScreenPoint(m_SnappedTo, out Vector2 sp))
                {
                    m_Pos = sp;
                    Clamp();
                }
            }
            Apply();
        }

        /// <summary>Jumps to the next character left (-1) or right (+1) of the cursor on screen, wrapping around.</summary>
        public void Snap(int direction)
        {
            CollectSnappable();
            GameObject best = null;
            GameObject wrap = null;
            float bestDx = float.MaxValue;
            float wrapX = 0f;
            foreach (GameObject go in m_Snappable)
            {
                if (go == m_SnappedTo || !TryScreenPoint(go, out Vector2 sp))
                {
                    continue;
                }
                float dx = (sp.x - m_Pos.x) * direction;
                // Characters at (nearly) the same x: prefer the closer one vertically by nudging the distance.
                float score = dx + Mathf.Abs(sp.y - m_Pos.y) * 0.01f;
                if (dx > 1f && score < bestDx)
                {
                    bestDx = score;
                    best = go;
                }
                float wx = sp.x * -direction;
                if (wrap == null || wx > wrapX)
                {
                    wrap = go;
                    wrapX = wx;
                }
            }
            GameObject next = best ?? wrap;
            if (next != null)
            {
                SnapTo(next);
            }
        }

        public void SnapTo(GameObject go)
        {
            if (go != null && TryScreenPoint(go, out Vector2 sp))
            {
                m_SnappedTo = go;
                m_Pos = sp;
                Clamp();
                Apply();
            }
        }

        /// <summary>True when a click now would land in the world rather than on HUD / UI.</summary>
        public bool CanClick => Active && UIWindowManager.MouseInputAvailable && (GameCursor.UiObjectUnderCursor == null || OnWorldMarker());

        /// <summary>
        /// The game UI under the pointer is a marker standing in for something in the world (an area exit's icon with
        /// the map name, a trigger's icon): clicking it is the world action a mouse player gets (travel), so it is
        /// allowed; any other HUD element under the pointer is not.
        /// </summary>
        private static bool OnWorldMarker()
        {
            try
            {
                GameObject ui = GameCursor.UiObjectUnderCursor;
                UITriggerIcon marker = ui != null ? ui.GetComponentInParent<UITriggerIcon>() : null;
                return marker != null && !marker.Hiding && marker.ClickCallback != null;
            }
            catch (System.Exception)
            {
                return false; // a changed game: back to "no clicks on game UI"
            }
        }

        /// <summary>A pressed / released. The press only goes through when <see cref="CanClick"/>.</summary>
        public void SetClick(bool down)
        {
            if (down && !m_ClickHeld && CanClick)
            {
                m_ClickHeld = true;
                MouseEmulator.SetLeft(true);
            }
            else if (!down && m_ClickHeld)
            {
                m_ClickHeld = false;
                MouseEmulator.SetLeft(false);
            }
        }

        private void Apply()
        {
            MouseEmulator.MoveTo(m_Pos);
        }

        private void Clamp()
        {
            m_Pos.x = Mathf.Clamp(m_Pos.x, Screen.width * EdgeMarginX, Screen.width * (1f - EdgeMarginX));
            m_Pos.y = Mathf.Clamp(m_Pos.y, Screen.height * EdgeMarginBottom, Screen.height * (1f - EdgeMarginTop));
        }

        /// <summary>Screen point the game's own character picking accepts (GameInput tests the character's position).</summary>
        private static bool TryScreenPoint(GameObject go, out Vector2 sp)
        {
            Camera cam = Camera.main;
            sp = default;
            if (cam == null || go == null)
            {
                return false;
            }
            Vector3 p = cam.WorldToScreenPoint(go.transform.position);
            if (p.z <= 0f || p.x < 0f || p.x > Screen.width || p.y < 0f || p.y > Screen.height)
            {
                return false;
            }
            sp = new Vector2(p.x, p.y);
            return true;
        }

        /// <summary>Living, visible characters on screen: enemies, party and allies (heals and buffs need those too).</summary>
        private void CollectSnappable()
        {
            m_Snappable.Clear();
            List<Faction> factions = Faction.ActiveFactionComponents;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || !f.isActiveAndEnabled || !f.DrawSelectionCircle || f.GetComponent<Trap>() != null)
                {
                    continue;
                }
                Health h = f.GetComponent<Health>();
                if (h == null || h.Dead || h.ShowDead)
                {
                    continue;
                }
                AIController ai = f.GetComponent<AIController>();
                if (ai != null && ai.IsInvisible && !(ai is PartyMemberAI))
                {
                    continue;
                }
                if (FogOfWar.Instance != null && !FogOfWar.Instance.PointVisible(f.transform.position))
                {
                    continue;
                }
                m_Snappable.Add(f.gameObject);
            }
        }

        /// <summary>Nearest living, visible hostile to <paramref name="from"/> that is on screen, or null.</summary>
        public static GameObject NearestEnemy(GameObject from)
        {
            if (from == null)
            {
                return null;
            }
            GameObject best = null;
            float bestD = float.MaxValue;
            List<Faction> factions = Faction.ActiveFactionComponents;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || !f.isActiveAndEnabled || !f.DrawSelectionCircle || f.GetComponent<Trap>() != null || !f.IsHostile(from))
                {
                    continue;
                }
                Health h = f.GetComponent<Health>();
                if (h == null || h.Dead || h.ShowDead || h.Unconscious)
                {
                    continue;
                }
                AIController ai = f.GetComponent<AIController>();
                if ((ai != null && ai.IsInvisible) || (FogOfWar.Instance != null && !FogOfWar.Instance.PointVisible(f.transform.position)))
                {
                    continue;
                }
                float d = (f.transform.position - from.transform.position).sqrMagnitude;
                if (d < bestD && TryScreenPoint(f.gameObject, out Vector2 _))
                {
                    bestD = d;
                    best = f.gameObject;
                }
            }
            return best;
        }
    }
}
