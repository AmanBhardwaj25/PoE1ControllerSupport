using System.Collections.Generic;
using System.Linq;
using PoEController.Compat;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Console style's Active Effects panel (Switch screenshot #11): one character's buffs and debuffs with what they
    /// do and how long they last. Contents are the character sheet's own: <c>UICharacterSheetStatusEffects
    /// .BundleEffectsForUI</c> groups the effects, <c>StatusEffectParams.ListToString</c> (character-sheet format)
    /// words them, <c>GetDisplayIcon</c> / <c>GUIUtils.CombatTime</c> / <c>Params.IsHostile</c> give icon, time left and
    /// colour. Refreshed twice a second while open. Pauses in real time (like the radials); LB / RB view another party
    /// member (selection unchanged). Runs under feature <see cref="Features.ActiveEffects"/>.
    /// </summary>
    internal sealed class ActiveEffectsPanel
    {
        public sealed class Row
        {
            public string Name;
            public string Text;
            public string Time;
            public bool Hostile;
            public Texture Icon;
            /// <summary>What it does, compact (the game's party-bar wording), for the hold-Y overlay.</summary>
            public string Short;
            /// <summary>Time left as a fraction of the duration (0..1); -1 when it has no timer.</summary>
            public float Fraction = -1f;
            /// <summary>For effects without a timer: "Until rest" / "Until combat ends" / "Active".</summary>
            public string Until;
        }

        private const float RefreshInterval = 0.5f;

        private readonly List<Row> m_Rows = new List<Row>();
        private float m_Refresh;
        private bool m_PausedByUs;

        public bool IsOpen { get; private set; }
        public CharacterStats Owner { get; private set; }
        public IReadOnlyList<Row> Rows => m_Rows;

        /// <summary>First row shown (d-pad / LS scroll).</summary>
        public int Scroll { get; private set; }

        public bool Open(CharacterStats owner)
        {
            if (owner == null || !Features.On(Features.ActiveEffects))
            {
                return false;
            }
            Owner = owner;
            Scroll = 0;
            Rebuild();
            IsOpen = true;
            m_PausedByUs = false;
            TimeController tc = TimeController.Instance;
            if (tc != null && !tc.Paused && !TacticalModeManager.IsInTacticalCombat())
            {
                tc.SafePaused = true;
                m_PausedByUs = tc.Paused;
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Active effects: {owner.Name()} ({m_Rows.Count})");
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            Owner = null;
            m_Rows.Clear();
            if (m_PausedByUs && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
            m_PausedByUs = false;
        }

        public void Update(float dt)
        {
            if (!IsOpen)
            {
                return;
            }
            if (Owner == null || GameState.IsLoading || !Features.On(Features.ActiveEffects))
            {
                Close();
                return;
            }
            m_Refresh -= dt;
            if (m_Refresh <= 0f)
            {
                Rebuild();
            }
        }

        public void ScrollBy(int delta)
        {
            Scroll = Mathf.Clamp(Scroll + delta, 0, Mathf.Max(0, m_Rows.Count - 1));
        }

        /// <summary>LB / RB: the previous / next party member (party-slot order); only who is shown changes.</summary>
        public void StepOwner(int dir)
        {
            List<PartyMemberAI> party = PartyMemberAI.OnlyPrimaryPartyMembers.Where(m => m != null).OrderBy(m => m.Slot).ToList();
            if (party.Count < 2 || Owner == null)
            {
                return;
            }
            int i = party.FindIndex(m => m.gameObject == Owner.gameObject);
            CharacterStats next = party[((i < 0 ? 0 : i + dir) % party.Count + party.Count) % party.Count].GetComponent<CharacterStats>();
            if (next != null)
            {
                Owner = next;
                Scroll = 0;
                Rebuild();
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            }
        }

        private void Rebuild()
        {
            m_Refresh = RefreshInterval;
            m_Rows.Clear();
            CharacterStats owner = Owner;
            List<Row> rows = owner == null ? null : Features.Get(Features.ActiveEffects, () => BuildRows(owner), null);
            if (rows != null)
            {
                m_Rows.AddRange(rows);
            }
            Scroll = Mathf.Clamp(Scroll, 0, Mathf.Max(0, m_Rows.Count - 1));
        }

        /// <summary>The effect rows for one character (also used by the portrait overlay).</summary>
        internal static List<Row> BuildRows(CharacterStats stats)
        {
            var rows = new List<Row>();
            foreach (List<StatusEffect> bundle in UICharacterSheetStatusEffects.BundleEffectsForUI(stats.ActiveStatusEffects))
            {
                if (bundle == null || bundle.Count == 0 || bundle[0] == null)
                {
                    continue;
                }
                StatusEffect first = bundle[0];
                float timeLeft = 0f;
                float duration = 0f;
                foreach (StatusEffect e in bundle)
                {
                    if (e != null && e.Duration > 0f && e.TimeLeft > timeLeft)
                    {
                        timeLeft = e.TimeLeft;
                        duration = e.Duration;
                    }
                }
                string brief = StatusEffectParams.ListToString(bundle, stats, null, null, null, StatusEffectFormatMode.PartyBar, AttackBase.TargetType.All);
                string text = StatusEffectParams.ListToString(bundle, stats, null, null, null, StatusEffectFormatMode.CharacterSheet, AttackBase.TargetType.All);
                rows.Add(new Row
                {
                    Name = NGUITools.StripSymbols(first.GetDisplayName() ?? ""),
                    Text = NGUITools.StripSymbols(text ?? ""),
                    Time = timeLeft > 0f ? GUIUtils.CombatTime(timeLeft) : null,
                    Hostile = first.Params != null && first.Params.IsHostile,
                    Icon = first.GetDisplayIcon(),
                    Short = NGUITools.StripSymbols(string.IsNullOrEmpty(brief) ? (text ?? "") : brief),
                    Fraction = timeLeft > 0f && duration > 0f ? Mathf.Clamp01(timeLeft / duration) : -1f,
                    Until = timeLeft > 0f ? null : first.LastsUntilRest ? "Until rest" : first.LastsUntilCombatEnds ? "Until combat ends" : "Active",
                });
            }
            return rows;
        }
    }
}
