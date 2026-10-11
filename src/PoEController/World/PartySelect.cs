using System.Collections.Generic;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Party-select mode: a focus marker walks the portrait bar and A / Y change the selection. The changes are the
    /// ones a click on a portrait makes (PartyMemberAI.ProcessSelection): shift-click toggles one member
    /// (<c>Selected</c>, never removing the last one), a plain click selects only that member (<c>ExclusiveSelect</c>),
    /// each with the member's "selected" bark.
    /// </summary>
    internal sealed class PartySelect
    {
        private readonly List<PartyMemberAI> m_Members = new List<PartyMemberAI>();
        private readonly List<Rect> m_Rects = new List<Rect>();
        private PartyMemberAI m_Focus;

        public bool Active { get; private set; }

        /// <summary>The focused member, for prompt text.</summary>
        public PartyMemberAI Focus => m_Focus;

        /// <summary>Members with a portrait, left to right (valid after Open / Update).</summary>
        public IReadOnlyList<PartyMemberAI> Members => m_Members;

        /// <summary>Console bumper-hold aiming: focus this member if it has a portrait.</summary>
        public void FocusOn(PartyMemberAI m)
        {
            if (m != null && m != m_Focus && m_Members.Contains(m))
            {
                m_Focus = m;
                if (GlobalAudioPlayer.Instance != null)
                {
                    GlobalAudioPlayer.Instance.Play(UIAudioList.UIAudioType.ButtonUp);
                }
            }
        }

        public void Open()
        {
            Refresh();
            if (m_Members.Count == 0)
            {
                return;
            }
            // Start on the selected leader so a quick Y keeps who you had.
            int leaderSlot = PartyMemberAI.GetSelectedLeaderSlot();
            m_Focus = m_Members[0];
            foreach (PartyMemberAI m in m_Members)
            {
                if (m.Slot == leaderSlot && m.Selected)
                {
                    m_Focus = m;
                }
            }
            Active = true;
        }

        public void Close()
        {
            Active = false;
            m_Focus = null;
        }

        /// <summary>Call every frame while active. Closes itself if the portrait bar or party is gone.</summary>
        public void Update()
        {
            Refresh();
            if (m_Members.Count == 0)
            {
                Close();
                return;
            }
            if (m_Focus == null || !m_Members.Contains(m_Focus))
            {
                m_Focus = m_Members[0];
            }
        }

        public void Move(int direction)
        {
            int i = m_Members.IndexOf(m_Focus);
            if (i < 0 || m_Members.Count == 0)
            {
                return;
            }
            i = (i + direction + m_Members.Count) % m_Members.Count;
            m_Focus = m_Members[i];
            if (GlobalAudioPlayer.Instance != null)
            {
                GlobalAudioPlayer.Instance.Play(UIAudioList.UIAudioType.ButtonUp);
            }
        }

        /// <summary>A: add / remove the focused member (shift-click on its portrait).</summary>
        public void Toggle()
        {
            PartyMemberAI m = m_Focus;
            if (m == null)
            {
                return;
            }
            if (m.Selected)
            {
                if (PartyMemberAI.NumSelectedMembers() > 1)
                {
                    m.Selected = false;
                }
                return;
            }
            m.Selected = true;
            Bark(m);
        }

        /// <summary>Y: select only the focused member (plain click on its portrait).</summary>
        public void SelectOnly()
        {
            PartyMemberAI m = m_Focus;
            if (m == null)
            {
                return;
            }
            m.ExclusiveSelect();
            Bark(m);
        }

        /// <summary>Screen rect (GUI coordinates) of the focused portrait.</summary>
        public bool TryGetFocusRect(out Rect rect)
        {
            int i = Active ? m_Members.IndexOf(m_Focus) : -1;
            rect = i >= 0 ? m_Rects[i] : default;
            return i >= 0;
        }

        private static void Bark(PartyMemberAI m)
        {
            if (m.Selected && !m.IsUnconscious)
            {
                SoundSet.TryPlayVoiceEffectWithLocalCooldown(m.gameObject, SoundSet.SoundAction.Selected, SoundSet.s_VeryShortVODelay, forceInterrupt: false);
            }
        }

        /// <summary>Primary party members with a visible portrait, left to right as shown on screen.</summary>
        private void Refresh()
        {
            m_Members.Clear();
            m_Rects.Clear();
            UIPartyPortraitBar bar = UIPartyPortraitBar.Instance;
            Camera ui = InGameUILayout.NGUICamera;
            if (bar == null || ui == null || !bar.gameObject.activeInHierarchy)
            {
                return;
            }
            foreach (PartyMemberAI m in PartyMemberAI.OnlyPrimaryPartyMembers)
            {
                UIPartyPortrait portrait = bar.GetPortraitFor(m);
                if (portrait == null || portrait.PartyMemberAI != m || !portrait.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(portrait.transform);
                Vector3 min = ui.WorldToScreenPoint(b.min);
                Vector3 max = ui.WorldToScreenPoint(b.max);
                Rect r = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Screen.height - Mathf.Max(min.y, max.y),
                    Mathf.Max(min.x, max.x), Screen.height - Mathf.Min(min.y, max.y));
                if (r.width < 4f || r.height < 4f)
                {
                    continue;
                }
                int at = 0;
                while (at < m_Rects.Count && m_Rects[at].x < r.x)
                {
                    at++;
                }
                m_Members.Insert(at, m);
                m_Rects.Insert(at, r);
            }
        }
    }
}
