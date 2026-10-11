using System.Collections.Generic;
using System.Linq;
using PoEController.Compat;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Console style's party ring (hold LB / RB; after Baldur's Gate's console version): the
    /// party's portraits on the radial ring, open until closed. LS / d-pad choose, A add / remove (never the last
    /// one), Y only this one, LB+RB everyone, B or LB / RB close. Pauses while open (not in turn-based combat, where it
    /// can't open: the game decides who acts).
    /// <para>
    /// Selection changes are the ones a click on a portrait makes (as <see cref="PartySelect"/>): shift-click toggles
    /// <c>PartyMemberAI.Selected</c>, a plain click is <c>ExclusiveSelect</c>, each with the "selected" bark.
    /// </para>
    /// </summary>
    internal sealed class PartyRadial : IRadialModel
    {
        private const float StickSelect = 0.5f;

        private readonly List<PartyMemberAI> m_Members = new List<PartyMemberAI>();
        private bool m_PausedByUs;

        public bool IsOpen { get; private set; }
        public int Highlight { get; private set; } = -1;
        public int Count => m_Members.Count;
        public bool Combat => false;
        public string Title => "Party";
        public bool SpreadOnHoles => true;

        /// <summary>The highlighted member's portrait goes in the middle.</summary>
        public CharacterStats Owner => Focus != null ? Focus.GetComponent<CharacterStats>() : null;

        public PartyMemberAI Focus => Highlight >= 0 && Highlight < m_Members.Count ? m_Members[Highlight] : null;

        public RadialSlot GetSlot(int index)
        {
            PartyMemberAI m = m_Members[index];
            if (m == null)
            {
                return new RadialSlot { Name = "", Blank = true };
            }
            CharacterStats stats = m.GetComponent<CharacterStats>();
            Health health = m.GetComponent<Health>();
            string endurance = health != null && health.MaxStamina > 0f
                ? $"Endurance {Mathf.CeilToInt(health.CurrentStamina)}/{Mathf.CeilToInt(health.MaxStamina)}"
                : null;
            return new RadialSlot
            {
                Name = stats != null ? stats.Name() : m.name,
                Icon = CharacterStats.SmallPortrait(m.gameObject),
                Usable = !m.IsUnconscious,
                Active = m.Selected,
                Status = (m.Selected ? "Selected" : "Not selected") + (endurance != null ? " · " + endurance : ""),
            };
        }

        public bool Open()
        {
            if (!Features.On(Features.PartyRadial) || !CanOpen())
            {
                return false;
            }
            Refresh();
            if (m_Members.Count == 0)
            {
                return false;
            }
            // Start on the selected leader.
            int leaderSlot = PartyMemberAI.GetSelectedLeaderSlot();
            Highlight = Mathf.Max(0, m_Members.FindIndex(m => m.Slot == leaderSlot && m.Selected));
            IsOpen = true;
            m_PausedByUs = false;
            TimeController tc = TimeController.Instance;
            if (tc != null && !tc.Paused)
            {
                tc.SafePaused = true;
                m_PausedByUs = tc.Paused;
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo("Party ring opened");
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            m_Members.Clear();
            Highlight = -1;
            if (m_PausedByUs && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
            m_PausedByUs = false;
            Plugin.Log.LogInfo($"Party ring closed ({PartyMemberAI.NumSelectedMembers()} selected)");
        }

        public void Update(Vector2 stick)
        {
            if (!IsOpen)
            {
                return;
            }
            if (!CanOpen() || !Features.On(Features.PartyRadial))
            {
                Close();
                return;
            }
            PartyMemberAI focus = Focus;
            Refresh();
            if (m_Members.Count == 0)
            {
                Close();
                return;
            }
            // Keep the highlight on the same member if the order changed.
            if (focus != null && m_Members.Contains(focus))
            {
                Highlight = m_Members.IndexOf(focus);
            }
            if (stick.magnitude >= StickSelect)
            {
                Highlight = RadialLayout.Pick(stick, m_Members.Count, spreadOnHoles: true);
            }
            Highlight = Mathf.Clamp(Highlight, 0, m_Members.Count - 1);
        }

        public void Step(int direction)
        {
            if (m_Members.Count > 0)
            {
                Highlight = (Highlight + direction + m_Members.Count) % m_Members.Count;
            }
        }

        /// <summary>A: add / remove the highlighted member (shift-click on its portrait).</summary>
        public void Toggle()
        {
            PartyMemberAI m = Focus;
            if (m == null)
            {
                return;
            }
            Features.Run(Features.PartyRadial, () =>
            {
                if (m.Selected)
                {
                    if (PartyMemberAI.NumSelectedMembers() > 1)
                    {
                        m.Selected = false;
                    }
                    else
                    {
                        GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                    }
                    return;
                }
                m.Selected = true;
                Bark(m);
            });
        }

        /// <summary>Y: select only the highlighted member (plain click on its portrait).</summary>
        public void SelectOnly()
        {
            PartyMemberAI m = Focus;
            if (m != null)
            {
                Features.Run(Features.PartyRadial, () =>
                {
                    m.ExclusiveSelect();
                    Bark(m);
                });
            }
        }

        /// <summary>A's word for the prompt bar.</summary>
        public string ToggleLabel
        {
            get
            {
                PartyMemberAI m = Focus;
                if (m == null)
                {
                    return null;
                }
                return !m.Selected ? "Add" : (PartyMemberAI.NumSelectedMembers() > 1 ? "Remove" : null);
            }
        }

        private static bool CanOpen()
        {
            return GameState.s_playerCharacter != null
                && !GameState.IsLoading
                && !Cutscene.CutsceneActive
                && !GameState.IsCutsceneOrConversationRunning()
                && !GameState.PartyDead
                && !TacticalModeManager.IsInTacticalCombat()
                && UIWindowManager.KeyInputAvailable;
        }

        /// <summary>Primary party members in portrait order (party slot).</summary>
        private void Refresh()
        {
            m_Members.Clear();
            m_Members.AddRange(PartyMemberAI.OnlyPrimaryPartyMembers.Where(m => m != null).OrderBy(m => m.Slot));
        }

        private static void Bark(PartyMemberAI m)
        {
            if (m.Selected && !m.IsUnconscious)
            {
                SoundSet.TryPlayVoiceEffectWithLocalCooldown(m.gameObject, SoundSet.SoundAction.Selected, SoundSet.s_VeryShortVODelay, forceInterrupt: false);
            }
        }
    }
}
