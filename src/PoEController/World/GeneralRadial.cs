using System;
using System.Collections.Generic;
using PoEController.Compat;
using PoEController.Input;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Console style's General Radial (LT, the console version's layout): the PC HUD's action buttons as a 12-slot wheel,
    /// in the Switch's order, clockwise from the top. Opens paused (like the ability wheel), LS / d-pad choose, A
    /// confirm, X the slot's extra action, B back / close, LB/RB other character.
    /// <para>
    /// Screens open by pressing the HUD button's own hotkey (<see cref="VirtualControls"/>), so the game's checks run;
    /// formations, AI and level-up make the same calls as the HUD formation popup, the ability bar's AI button and the
    /// portrait's level-up icon. Everything that touches the game runs under feature <see cref="Features.GeneralRadial"/>.
    /// </para>
    /// </summary>
    internal sealed class GeneralRadial : IRadialModel
    {
        public sealed class Entry
        {
            public string Name;
            /// <summary>Icon file name in the plugin's radial folder, without ".png".</summary>
            public string Icon;
            public bool Usable = true;
            public bool Active;
            public string Status;
            public string Badge;
            public Action Run;
            /// <summary>A keeps the radial open (toggles, so the new state shows).</summary>
            public bool KeepOpen;
            /// <summary>A opens the formation ring instead of acting.</summary>
            public bool OpensFormations;
            /// <summary>A opens the Info ring (Active effects, Combat log) instead of acting.</summary>
            public bool OpensInfo;
            public string AltLabel;
            public Action Alt;
        }

        public const int Slots = 12;
        private const float RefreshInterval = 0.25f;
        private const float StickSelect = 0.5f;
        /// <summary>Slot index of Formation in the top ring (B in the formation ring comes back to it).</summary>
        private const int FormationSlot = 8;
        /// <summary>Slot index of Info (B in the Info ring comes back to it).</summary>
        private const int InfoSlot = 5;

        private readonly List<Entry> m_Shown = new List<Entry>();
        private float m_RefreshTimer;
        private bool m_PausedByUs;

        /// <summary>Opens the Active Effects card for a character (set by the Console world scheme; slot 5).</summary>
        public Action<CharacterStats> OpenEffects;

        /// <summary>Opens the readable combat log (set by the Console world scheme; Info ring).</summary>
        public Action OpenCombatLog;

        /// <summary>Which sub-ring is open: null = the top ring, else "Formation" / "Info".</summary>
        private string m_Sub;

        public bool IsOpen { get; private set; }
        /// <summary>A sub-ring (Formation / Info) is open.</summary>
        public bool InSubRing => m_Sub != null;
        public CharacterStats Owner { get; private set; }
        public IReadOnlyList<Entry> Shown => m_Shown;

        /// <summary>-1 until the stick or d-pad picks a slot (the Switch opens with nothing highlighted).</summary>
        public int Highlight { get; private set; } = -1;

        public Entry Current => Highlight >= 0 && Highlight < m_Shown.Count ? m_Shown[Highlight] : null;

        public int Count => m_Shown.Count;
        public string Title => m_Sub;
        public bool Combat => false;
        public bool SpreadOnHoles => false;

        public RadialSlot GetSlot(int index)
        {
            Entry e = m_Shown[index];
            return new RadialSlot { Name = e.Name, IconName = e.Icon, Usable = e.Usable, Active = e.Active, Badge = e.Badge, Status = e.Status };
        }

        public bool Open()
        {
            if (!Features.On(Features.GeneralRadial) || !CanOpen())
            {
                return false;
            }
            Owner = FindOwner();
            m_Sub = null;
            Highlight = -1;
            Rebuild();
            if (m_Shown.Count == 0)
            {
                return false;
            }
            IsOpen = true;
            TimeController tc = TimeController.Instance;
            m_PausedByUs = false;
            if (tc != null && !tc.Paused && !TacticalModeManager.IsInTacticalCombat())
            {
                tc.SafePaused = true;
                m_PausedByUs = tc.Paused;
            }
            Plugin.Log.LogInfo("General radial opened");
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            m_Sub = null;
            m_Shown.Clear();
            Owner = null;
            if (m_PausedByUs && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
            m_PausedByUs = false;
        }

        public void Update(float dt, Vector2 stick)
        {
            if (!IsOpen)
            {
                return;
            }
            if (!CanOpen() || !Features.On(Features.GeneralRadial))
            {
                Close();
                return;
            }
            CharacterStats owner = FindOwner();
            if (owner != Owner)
            {
                // LB/RB or a portrait click changed the selection: follow it.
                Owner = owner;
                m_RefreshTimer = 0f;
            }
            m_RefreshTimer -= dt;
            if (m_RefreshTimer <= 0f)
            {
                Rebuild();
            }
            if (m_Shown.Count == 0)
            {
                Close();
                return;
            }
            if (stick.magnitude >= StickSelect)
            {
                Highlight = RadialLayout.Pick(stick, m_Shown.Count);
            }
            if (Highlight >= m_Shown.Count)
            {
                Highlight = m_Shown.Count - 1;
            }
        }

        public void Step(int direction)
        {
            if (m_Shown.Count == 0)
            {
                return;
            }
            Highlight = Highlight < 0 ? (direction > 0 ? 0 : m_Shown.Count - 1) : (Highlight + direction + m_Shown.Count) % m_Shown.Count;
        }

        /// <summary>A. Returns true if the radial should close.</summary>
        public bool Confirm()
        {
            Entry e = Current;
            if (e == null)
            {
                return false;
            }
            if (!e.Usable)
            {
                if (!string.IsNullOrEmpty(e.Status) && UISystemMessager.Instance != null)
                {
                    UISystemMessager.Instance.PostMessage(e.Status, Color.white);
                }
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                return false;
            }
            if (e.OpensFormations || e.OpensInfo)
            {
                m_Sub = e.OpensFormations ? "Formation" : "Info";
                Highlight = -1;
                Rebuild();
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                return false;
            }
            Plugin.Log.LogInfo($"General radial: {e.Name}");
            Features.Run(Features.GeneralRadial, e.Run);
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            if (e.KeepOpen)
            {
                Rebuild();
                return false;
            }
            return true;
        }

        /// <summary>X: the highlighted slot's extra action (Edit formations). Returns true if the radial should close.</summary>
        public bool Alternate()
        {
            Entry e = Current;
            if (e == null || e.Alt == null)
            {
                return false;
            }
            Plugin.Log.LogInfo($"General radial: {e.AltLabel} ({e.Name})");
            Features.Run(Features.GeneralRadial, e.Alt);
            return true;
        }

        /// <summary>B. Returns true if the radial should close.</summary>
        public bool Back()
        {
            if (m_Sub == null)
            {
                return true;
            }
            int slot = m_Sub == "Info" ? InfoSlot : FormationSlot;
            m_Sub = null;
            Rebuild();
            Highlight = slot;
            return false;
        }

        private static bool CanOpen()
        {
            return GameState.s_playerCharacter != null
                && !GameState.IsLoading
                && !Cutscene.CutsceneActive
                && !GameState.IsCutsceneOrConversationRunning()
                && !GameState.PartyDead
                && UIWindowManager.KeyInputAvailable;
        }

        private static CharacterStats FindOwner()
        {
            GameObject go = UIAbilityBar.GetSelectedForBars() ?? DirectMovement.SelectedLeader();
            return go != null ? go.GetComponent<CharacterStats>() : null;
        }

        private void Rebuild()
        {
            m_RefreshTimer = RefreshInterval;
            m_Shown.Clear();
            List<Entry> built = Features.Get(Features.GeneralRadial,
                () => m_Sub == "Formation" ? BuildFormations() : m_Sub == "Info" ? BuildInfo(Owner, OpenEffects, OpenCombatLog) : BuildTop(Owner), null);
            if (built != null)
            {
                m_Shown.AddRange(built);
            }
        }

        // ---- Contents ----

        private static Action Press(MappedControl control) => () => VirtualControls.Hold(control);

        private static Entry Screen(string name, string icon, MappedControl control) => new Entry { Name = name, Icon = icon, Run = Press(control) };

        /// <summary>The 12 slots, clockwise from the top (the console version's order).</summary>
        private static List<Entry> BuildTop(CharacterStats owner)
        {
            var top = new List<Entry>(Slots)
            {
                Screen("Character sheet", "ICO_charSheet", MappedControl.CHARACTER_SHEET),
                Screen("Map", "ICO_map", MappedControl.AREAMAP),
                RestEntry(),
                Screen("Journal", "ICO_journal", MappedControl.JOURNAL),
                HighlightEntry(),
                // Slot 5 (the Switch's combat log): Info, a ring of Active effects + Combat log (the ring
                // art has exactly 12 holes, so extra entries go into sub-rings like Formation's).
                new Entry { Name = "Info", Icon = "ICO_log", OpensInfo = true, Status = "Active effects, combat log" },
                StrongholdEntry(),
                Screen("Options", "ICO_options", MappedControl.OPTIONS),
                new Entry
                {
                    Name = "Formation",
                    Icon = "ICO_formationA",
                    OpensFormations = true,
                    AltLabel = "Edit formations",
                    Alt = EditFormations,
                },
                Screen("Inventory", "ICO_inventory", MappedControl.INVENTORY),
                AiEntry(owner),
                LevelUpEntry(owner),
            };
            return top;
        }

        private static Entry RestEntry()
        {
            PlayerInventory inv = GameState.s_playerCharacter != null ? GameState.s_playerCharacter.GetComponent<PlayerInventory>() : null;
            return new Entry
            {
                Name = "Rest",
                Icon = "ICO_rest",
                Run = Press(MappedControl.CAMP),
                Badge = inv != null ? inv.CampingSuppliesTotal.ToString() : null,
                Status = inv != null ? $"Camping supplies: {inv.CampingSuppliesTotal}" : null,
            };
        }

        private static Entry HighlightEntry()
        {
            bool on = InGameHUD.Instance != null && InGameHUD.Instance.HighlightActive;
            return new Entry
            {
                Name = "Highlight objects",
                Icon = "ICO_status",
                Active = on,
                Status = on ? "On" : "Off",
                Run = Press(MappedControl.HIGHLIGHT_TOGGLE),
                KeepOpen = true,
            };
        }

        private static Entry StrongholdEntry()
        {
            bool have = Stronghold.Instance != null && Stronghold.Instance.Activated;
            return new Entry
            {
                Name = "Stronghold",
                Icon = "ICO_strongHold",
                Usable = have,
                Status = have ? null : "No stronghold yet",
                Run = Press(MappedControl.STRONGHOLD),
            };
        }

        /// <summary>
        /// The character's AI script on / off, as the ability bar's AI button (PartyMemberAI.UseInstructionSet);
        /// X opens the game's AI customizer for them. A party setting, so here rather than in the Combat Radial
        /// (whose slot holds force attack).
        /// </summary>
        private static Entry AiEntry(CharacterStats owner)
        {
            PartyMemberAI pm = owner != null ? owner.GetComponent<PartyMemberAI>() : null;
            if (pm == null)
            {
                return new Entry { Name = "AI", Icon = "ICO_ai_off", Usable = false, Status = "No AI for this character" };
            }
            bool on = pm.UseInstructionSet;
            return new Entry
            {
                Name = "AI: " + CombatRadial.ScriptName(owner, pm),
                Icon = on ? "ICO_ai_on" : "ICO_ai_off",
                Active = on,
                Status = on ? "AI script is active" : "AI script is off",
                KeepOpen = true,
                Run = () =>
                {
                    pm.UseInstructionSet = !pm.UseInstructionSet;
                    pm.UpdateAggressionOfSummonedCreatures(includeCompanion: true);
                },
                AltLabel = "Customize",
                Alt = () =>
                {
                    if (UIAiCustomizerManager.Instance != null)
                    {
                        UIAiCustomizerManager.Instance.SelectedCharacter = owner;
                        UIAiCustomizerManager.Instance.ShowWindow();
                    }
                },
            };
        }

        /// <summary>Level up the character, as the portrait's level-up icon (UIPartyPortraitIcon.OnLevelUpClick).</summary>
        private static Entry LevelUpEntry(CharacterStats owner)
        {
            // The Switch's ICO_levelup_inactive is nearly black (invisible once dimmed): dim the normal icon instead.
            var e = new Entry { Name = "Level up", Icon = "ICO_levelup", Usable = false };
            if (owner == null)
            {
                e.Status = "No character selected";
                return e;
            }
            int max = owner.GetMaxLevelCanLevelUpTo();
            if (owner.Level >= max)
            {
                e.Status = owner.Name() + " can't level up yet";
                return e;
            }
            if (GameState.InCombat)
            {
                e.Status = "Not during combat";
                return e;
            }
            e.Usable = true;
            e.Status = owner.Name();
            GameObject go = owner.gameObject;
            e.Run = () =>
            {
                CharacterStats stats = go.GetComponent<CharacterStats>();
                int endingLevel = Math.Min(stats.GetMaxLevelCanLevelUpTo(), stats.Level + 1);
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.LevelUp);
                UICharacterCreationManager.Instance.OpenCharacterCreation(UICharacterCreationManager.CharacterCreationType.LevelUp, go, 0, endingLevel, stats.Experience);
            };
            return e;
        }

        /// <summary>The Info ring: the Active Effects card for the radial's character, and the readable combat log.</summary>
        private static List<Entry> BuildInfo(CharacterStats owner, Action<CharacterStats> openEffects, Action openLog)
        {
            return new List<Entry>
            {
                new Entry
                {
                    Name = "Active effects",
                    Icon = "ICO_status",
                    Usable = owner != null && openEffects != null,
                    Status = owner != null ? owner.Name() : "No character selected",
                    Run = () => openEffects?.Invoke(owner),
                },
                new Entry
                {
                    Name = "Combat log",
                    Icon = "ICO_log",
                    Usable = openLog != null,
                    Status = "Read back the log",
                    Run = () => openLog?.Invoke(),
                },
            };
        }

        private static void EditFormations()
        {
            if (UIFormationsManager.Instance != null)
            {
                UIFormationsManager.Instance.ShowWindow();
            }
        }

        /// <summary>One slot per formation; A uses it for the whole party (as the HUD formation popup), X edits.</summary>
        private static List<Entry> BuildFormations()
        {
            var list = new List<Entry>();
            UIFormationsManager mgr = UIFormationsManager.Instance;
            Vector3[][] all = FormationData.Instance != null ? Compat.GameRef.Get<Vector3[][]>(FormationData.Instance, "m_formations") : null;
            if (mgr == null || all == null)
            {
                return list;
            }
            int standard = mgr.NumStandardSets;
            int current = -1;
            foreach (PartyMemberAI pm in PartyMemberAI.PartyMembers)
            {
                if (pm != null)
                {
                    current = pm.FormationStyle;
                    break;
                }
            }
            string[] standardIcons = { "ICO_formationA", "ICO_formationB", "ICO_formationC" };
            for (int i = 0; i < all.Length && list.Count < Slots; i++)
            {
                int index = i;
                bool custom = i >= standard;
                list.Add(new Entry
                {
                    Name = custom ? $"Custom formation {i - standard + 1}" : GUIUtils.GetText(mgr.StandardFormationNameIds[i]),
                    Icon = !custom && i < standardIcons.Length ? standardIcons[i] : "ICO_formationCustom",
                    Badge = custom ? (i - standard + 1).ToString() : null,
                    Active = i == current,
                    Status = i == current ? "In use" : null,
                    Run = () =>
                    {
                        foreach (PartyMemberAI pm in PartyMemberAI.PartyMembers)
                        {
                            if (pm != null)
                            {
                                pm.FormationStyle = index;
                            }
                        }
                    },
                    AltLabel = custom ? "Edit" : "Edit formations",
                    Alt = custom ? (Action)(() => UIFormationsManager.Instance.ShowFormation(index)) : EditFormations,
                });
            }
            return list;
        }
    }
}
