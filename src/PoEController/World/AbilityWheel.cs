using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// The ability wheel (Y): the selected character's spells by level, chants, abilities, modes and quick items in
    /// a ring, with the game paused while it is open.
    /// <para>
    /// What is listed mirrors the game's ability bar (UIAbilityBarButtonSet.SetButtons*: the same filters over
    /// <c>CharacterStats.ActiveAbilities</c> and the quickbar, and the bar's own <c>ShowOnSpellBar</c> for spells).
    /// Readiness is the bar's: <c>GenericAbility.ReadyForUI</c> / <c>Consumable.CanUse</c>. Using an entry makes the
    /// calls a click on its bar button makes (UIAbilityBarButton.Trigger): <c>TriggerFromUI</c> +
    /// <c>DeactivateOtherModal(onlyUi)</c>, <c>Consumable.StartUse</c>, <c>Equipment.SelectNextWeaponSet</c>.
    /// </para>
    /// <para>
    /// Abilities that need a target make the game enter its own targeting mode (Player.StartCasting); the world
    /// scheme then hands the controller to <see cref="WorldCursor"/>, keeping the game paused until the cast is
    /// placed or cancelled (B comes back here).
    /// </para>
    /// </summary>
    internal sealed class AbilityWheel
    {
        public enum EntryKind
        {
            Group,
            Ability,
            Item,
            WeaponSwitch,
            /// <summary>Force attack (the HUD's sword button): the next pick is attacked, whoever it is.</summary>
            ForceAttack,
        }

        public sealed class Entry
        {
            public EntryKind Kind;
            public string Name;
            public Texture Icon;
            /// <summary>An icon from the game's HUD atlas by name, when there is no <see cref="Icon"/>.</summary>
            public string IconName;
            public string Badge;
            public GenericAbility Ability;
            public InventoryItem Item;
            public List<Entry> Children;
            public bool Usable;
            public bool Active;
            public bool NeedsTarget;
            public string Status;
        }

        private const int MaxSlots = 12;
        private const float RefreshInterval = 0.25f;
        private const float StickSelect = 0.5f;

        private readonly List<Entry> m_Top = new List<Entry>();
        private List<Entry> m_Shown;
        private string m_OpenGroup;
        private float m_RefreshTimer;
        private bool m_PausedByUs;

        public bool IsOpen { get; private set; }
        public CharacterStats Owner { get; private set; }
        public IReadOnlyList<Entry> Shown => m_Shown;
        public int Highlight { get; private set; }
        public bool InGroup => m_OpenGroup != null;
        public string GroupName => m_OpenGroup;

        public Entry Current => m_Shown != null && Highlight >= 0 && Highlight < m_Shown.Count ? m_Shown[Highlight] : null;

        /// <summary>Did the last successful <see cref="Confirm"/> start something that waits for a target?</summary>
        public bool LastNeedsTarget { get; private set; }

        /// <param name="inheritPause">The caller already paused the game on our behalf (coming back from targeting).</param>
        public bool Open(bool inheritPause = false)
        {
            CharacterStats owner = FindOwner();
            if (owner == null || !CanOpen())
            {
                return false;
            }
            Owner = owner;
            m_OpenGroup = null;
            Highlight = 0;
            Rebuild();
            if (m_Top.Count == 0)
            {
                return false;
            }
            IsOpen = true;
            TimeController tc = TimeController.Instance;
            m_PausedByUs = inheritPause;
            // Turn-based combat already waits for the player; pausing there only risks freezing the turn order.
            if (tc != null && !tc.Paused && !TacticalModeManager.IsInTacticalCombat())
            {
                tc.SafePaused = true;
                m_PausedByUs = tc.Paused;
            }
            return true;
        }

        /// <summary>Closes without resuming; returns whether the wheel had paused the game (the caller now owns that).</summary>
        public bool CloseKeepPause()
        {
            bool paused = m_PausedByUs;
            m_PausedByUs = false;
            Close();
            return paused;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            m_Shown = null;
            m_OpenGroup = null;
            Owner = null;
            if (m_PausedByUs && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
            m_PausedByUs = false;
        }

        /// <summary>Per frame while open: keeps the contents live and closes if the game state no longer allows it.</summary>
        public void Update(float dt, Vector2 stick)
        {
            if (!IsOpen)
            {
                return;
            }
            if (Owner == null || !CanOpen() || !IsSelected(Owner))
            {
                // The owner may have changed through LB/RB or a portrait click; follow the selection.
                CharacterStats owner = CanOpen() ? FindOwner() : null;
                if (owner == null)
                {
                    Close();
                    return;
                }
                if (owner != Owner)
                {
                    Owner = owner;
                    m_OpenGroup = null;
                    Highlight = 0;
                }
            }
            m_RefreshTimer -= dt;
            if (m_RefreshTimer <= 0f)
            {
                Rebuild();
            }
            if (m_Shown == null || m_Shown.Count == 0)
            {
                Close();
                return;
            }
            if (stick.magnitude >= StickSelect)
            {
                // Slot 0 at the top, clockwise.
                float angle = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg;
                if (angle < 0f)
                {
                    angle += 360f;
                }
                float step = 360f / m_Shown.Count;
                Highlight = Mathf.RoundToInt(angle / step) % m_Shown.Count;
            }
            Highlight = Mathf.Clamp(Highlight, 0, m_Shown.Count - 1);
        }

        public void Step(int direction)
        {
            if (m_Shown == null || m_Shown.Count == 0)
            {
                return;
            }
            Highlight = (Highlight + direction + m_Shown.Count) % m_Shown.Count;
        }

        /// <summary>A. Returns true if the wheel should close (an action was taken).</summary>
        public bool Confirm()
        {
            Entry e = Current;
            if (e == null || !e.Usable)
            {
                return false;
            }
            LastNeedsTarget = e.NeedsTarget;
            if (e.Kind == EntryKind.Group)
            {
                m_OpenGroup = e.Name;
                Highlight = 0;
                Rebuild();
                return false;
            }
            return Use(e, Owner);
        }

        /// <summary>
        /// Uses an ability / item / weapon-switch entry for <paramref name="owner"/>, with the calls a click on its ability
        /// bar button makes. Also used by Console style's Combat Radial. Returns true if something was done.
        /// </summary>
        internal static bool Use(Entry e, CharacterStats owner)
        {
            switch (e.Kind)
            {
                case EntryKind.Ability:
                    e.Ability.TriggerFromUI();
                    e.Ability.DeactivateOtherModal(onlyUi: true);
                    return true;
                case EntryKind.Item:
                    Consumable consumable = e.Item.baseItem != null ? e.Item.baseItem.GetComponent<Consumable>() : null;
                    if (consumable == null)
                    {
                        return false;
                    }
                    consumable.StartUse(owner.gameObject);
                    return true;
                case EntryKind.ForceAttack:
                    // The HUD's sword button (UIActionBarOnClick.HandleAttack, keyboard A).
                    GameState.s_playerCharacter.IsInForceAttackMode = true;
                    return true;
                case EntryKind.WeaponSwitch:
                    Equipment equipment = owner.GetComponent<Equipment>();
                    if (equipment == null)
                    {
                        return false;
                    }
                    equipment.SelectNextWeaponSet();
                    if (UIAbilityBar.Instance != null)
                    {
                        UIAbilityBar.Instance.RefreshWeaponSets();
                    }
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Where the wheel stood (open group, highlighted entry), to come back to it.</summary>
        public sealed class Place
        {
            public string Group;
            public int Highlight;
        }

        /// <summary>The highlighted spell / ability / item has a card to open.</summary>
        public bool CanExamine => RadialInfo.CanExamine(Current);

        /// <summary>
        /// Opens the game's card for the highlighted entry. The wheel closes (the card is a window over the world);
        /// returns where it stood, for <see cref="Reopen"/> once the card is closed. <paramref name="paused"/> = the
        /// wheel had paused the game (the caller now owns that pause).
        /// </summary>
        public Place Examine(out bool paused)
        {
            paused = false;
            Entry e = Current;
            if (!CanExamine)
            {
                return null;
            }
            var place = new Place { Group = m_OpenGroup, Highlight = Highlight };
            CharacterStats owner = Owner;
            Plugin.Log.LogInfo($"Ability wheel: examine {e.Name}");
            paused = CloseKeepPause();
            RadialInfo.Examine(e, owner);
            return place;
        }

        /// <summary>Opens again where <see cref="Examine"/> left off. False if the wheel can't open now.</summary>
        public bool Reopen(Place place, bool inheritPause)
        {
            if (place == null || !Open(inheritPause))
            {
                return false;
            }
            m_OpenGroup = place.Group;
            Rebuild(); // a group that is gone falls back to the top ring
            Highlight = Mathf.Clamp(place.Highlight, 0, m_Shown.Count - 1);
            return true;
        }

        /// <summary>B. Returns true if the wheel should close.</summary>
        public bool Back()
        {
            if (m_OpenGroup == null)
            {
                return true;
            }
            string group = m_OpenGroup;
            m_OpenGroup = null;
            Rebuild();
            Highlight = Math.Max(0, m_Top.FindIndex(t => t.Name == group));
            return false;
        }

        /// <summary>What A would do right now, for the prompt bar; null when A does nothing.</summary>
        public string ConfirmLabel
        {
            get
            {
                Entry e = Current;
                if (e == null || !e.Usable)
                {
                    return null;
                }
                switch (e.Kind)
                {
                    case EntryKind.Group:
                        return "Open";
                    case EntryKind.WeaponSwitch:
                        return "Switch";
                    case EntryKind.ForceAttack:
                        return "Attack";
                    case EntryKind.Ability:
                        return e.Ability.Modal && e.Ability.UiActivated ? "Turn off" : (e.NeedsTarget ? "Aim" : "Use");
                    default:
                        return e.NeedsTarget ? "Aim" : "Use";
                }
            }
        }

        private static bool CanOpen()
        {
            return GameState.s_playerCharacter != null
                && !GameState.IsLoading
                && !Cutscene.CutsceneActive
                && !GameState.IsCutsceneOrConversationRunning()
                && !GameState.PartyDead
                && !GameState.s_playerCharacter.IsActionCursor()
                && UIWindowManager.MouseInputAvailable;
        }

        /// <summary>The character the game's ability bar shows; with several selected, the selected leader.</summary>
        private static CharacterStats FindOwner()
        {
            GameObject go = UIAbilityBar.GetSelectedForBars() ?? DirectMovement.SelectedLeader();
            return go != null ? go.GetComponent<CharacterStats>() : null;
        }

        private static bool IsSelected(CharacterStats stats)
        {
            PartyMemberAI ai = stats.GetComponent<PartyMemberAI>();
            return ai != null && ai.Selected;
        }

        private void Rebuild()
        {
            m_RefreshTimer = RefreshInterval;
            m_Top.Clear();
            if (Owner != null)
            {
                BuildTop(Owner, m_Top);
                // PC layout's way to force attack (Console style has it in the Combat Radial).
                m_Top.Add(ForceAttackEntry());
            }
            m_Shown = m_Top;
            if (m_OpenGroup != null)
            {
                Entry group = m_Top.FirstOrDefault(t => t.Kind == EntryKind.Group && t.Name == m_OpenGroup);
                if (group != null)
                {
                    m_Shown = group.Children;
                }
                else
                {
                    m_OpenGroup = null;
                }
            }
        }

        // ---- Contents (mirrors UIAbilityBarButtonSet) ----

        /// <summary>
        /// Force attack: the next character or object picked with the pointer is attacked, also ones the game doesn't
        /// count as enemies (charmed / dominated characters). By the selected party, as with the mouse.
        /// </summary>
        private static Entry ForceAttackEntry()
        {
            bool anyone = GameState.s_playerCharacter != null && PartyMemberAI.NumSelectedMembers() > 0;
            return new Entry
            {
                Kind = EntryKind.ForceAttack,
                Name = "Attack",
                IconName = "ICO_attack",
                Usable = anyone,
                NeedsTarget = true,
                Status = anyone ? "Any target, even a friendly-looking one" : "Select a character first",
            };
        }

        internal static void BuildTop(CharacterStats stats, List<Entry> top)
        {
            UIAbilityBar bar = UIAbilityBar.Instance;

            if (SpellMax.Instance != null && CharacterStats.IsPlayableClass(stats.CharacterClass))
            {
                int maxLevel = SpellMax.Instance.GetMaxSpellLevel(stats.gameObject);
                for (int level = 1; level <= maxLevel; level++)
                {
                    int lvl = level;
                    List<Entry> spells = Abilities(stats, a => UIAbilityBarButtonSet.ShowOnSpellBar(a, stats, lvl));
                    int max = SpellMax.Instance.GetSpellCastMax(stats.gameObject, lvl);
                    string badge = max == int.MaxValue ? null : Mathf.Max(0, max - stats.SpellCastCount[lvl - 1]).ToString();
                    AddGroups(top, GUIUtils.FormatSpellLevel(stats.CharacterClass, lvl), bar != null ? bar.GetClassIcon(stats.CharacterClass) : null, spells, badge);
                }
            }

            AddGroups(top, "Chants", bar != null ? bar.ChanterSpellIcon : null,
                Abilities(stats, a => !a.Passive && a is Chant).OrderBy(e => ((Chant)e.Ability).UiIndex).ToList(), null);

            AddGroups(top, "Abilities", null, Abilities(stats, a => IsClassAbility(stats, a)), null);

            AddGroups(top, "Modes", null, Abilities(stats, a => a.Modal && !(a is Chant) && !(a is GenericSpell) && !a.Passive), null);

            List<Entry> items = QuickItems(stats);
            items.AddRange(Abilities(stats, a => !a.Passive && GenericAbility.AbilityTypeIsAnyEquipment(a.EffectType)));
            AddGroups(top, "Items", bar != null ? bar.ItemsIcon : null, items, null);

            Entry weapons = WeaponSwitch(stats);
            if (weapons != null)
            {
                top.Add(weapons);
            }
        }

        /// <summary>The non-spell abilities the bar spreads over its per-encounter / per-rest / other rows.</summary>
        private static bool IsClassAbility(CharacterStats stats, GenericAbility a)
        {
            if (a.Passive || a.Modal || a is Chant || GenericAbility.AbilityTypeIsAnyEquipment(a.EffectType))
            {
                return false;
            }
            if (a.MasteryLevel > 0 || a.IsWatcherAbility)
            {
                return true;
            }
            GenericSpell spell = a as GenericSpell;
            if (spell != null)
            {
                // Stolen spells and spells of another class (scrolls of a sort, items granting spells).
                return spell.StatusEffectGrantingSpell != null
                    || spell.SpellClass != stats.CharacterClass
                    || !CharacterStats.IsPlayableClass(spell.SpellClass);
            }
            return !(a is GenericCipherAbility);
        }

        private static void AddGroups(List<Entry> top, string name, Texture icon, List<Entry> children, string badge)
        {
            if (children.Count == 0)
            {
                return;
            }
            int pages = (children.Count + MaxSlots - 1) / MaxSlots;
            for (int p = 0; p < pages; p++)
            {
                List<Entry> page = children.Skip(p * MaxSlots).Take(MaxSlots).ToList();
                top.Add(new Entry
                {
                    Kind = EntryKind.Group,
                    Name = pages > 1 ? $"{name} ({p + 1})" : name,
                    Icon = icon ?? page[0].Icon,
                    Badge = badge,
                    Children = page,
                    Usable = true,
                    Status = page.Count(c => c.Usable) + " of " + page.Count + " ready",
                });
            }
        }

        private static List<Entry> Abilities(CharacterStats stats, Func<GenericAbility, bool> condition)
        {
            var list = new List<Entry>();
            PartyMemberAI pm = stats.GetComponent<PartyMemberAI>();
            bool summoned = pm != null && pm.SummonType == AIController.AISummonType.Summoned;
            foreach (GenericAbility a in stats.ActiveAbilities.OrderBy(x => x.Sortkey))
            {
                if (a == null || !a.IsVisibleOnUI || a.TriggeredAutomatically
                    || (summoned && GenericAbility.AbilityTypeIsAnyEquipment(a.EffectType))
                    || !PrerequisiteData.CheckVisibilityPrerequisites(stats.gameObject, null, a.ActivationPrerequisites, null)
                    || !condition(a))
                {
                    continue;
                }
                list.Add(AbilityEntry(stats, a));
            }
            return list;
        }

        private static Entry AbilityEntry(CharacterStats stats, GenericAbility a)
        {
            bool ready = a.ReadyForUI;
            bool needsTarget = NeedsTarget(stats, a, a.GetComponent<AttackBase>());
            int uses = a is GenericSpell && a.MasteryLevel == 0 ? int.MaxValue : a.UsesLeft();
            return new Entry
            {
                Kind = EntryKind.Ability,
                Name = a.Name(),
                Icon = a.Icon,
                Ability = a,
                Badge = uses == int.MaxValue ? null : Mathf.Max(0, uses).ToString(),
                Usable = ready,
                Active = a.UiActivated,
                NeedsTarget = needsTarget,
                Status = !ready ? NotReadyText(a) : needsTarget ? "Choose a target next" : (a.Modal ? (a.UiActivated ? "On" : "Off") : null),
            };
        }

        internal static List<Entry> QuickItems(CharacterStats stats)
        {
            var list = new List<Entry>();
            PartyMemberAI pm = stats.GetComponent<PartyMemberAI>();
            if (pm == null || pm.QuickbarInventory == null)
            {
                return list;
            }
            foreach (InventoryItem ii in pm.QuickbarInventory.ItemList.OrderBy(x => x.uiSlot))
            {
                if (ii == null || ii.baseItem == null)
                {
                    continue;
                }
                Consumable c = ii.baseItem.GetComponent<Consumable>();
                if (c == null)
                {
                    // Non-consumable quick items (spare grimoires) are not handled yet.
                    continue;
                }
                bool canUse = c.CanUse(stats);
                GenericAbility template = ii.baseItem.GetComponent<GenericAbility>();
                bool needsTarget = template != null && NeedsTarget(stats, template, template.GetComponent<AttackBase>());
                string why = canUse ? null : c.WhyCannotUse(stats);
                list.Add(new Entry
                {
                    Kind = EntryKind.Item,
                    Name = ii.baseItem.Name,
                    Icon = ii.baseItem.GetIconTexture(),
                    Item = ii,
                    Badge = ii.stackSize > 1 ? ii.stackSize.ToString() : null,
                    Usable = canUse,
                    NeedsTarget = needsTarget,
                    Status = !canUse ? (string.IsNullOrEmpty(why) ? "Can't use now" : why) : needsTarget ? "Choose a target next" : null,
                });
            }
            return list;
        }

        internal static Entry WeaponSwitch(CharacterStats stats)
        {
            Equipment eq = stats.GetComponent<Equipment>();
            PartyMemberAI pm = stats.GetComponent<PartyMemberAI>();
            if (eq == null || pm == null || pm.Secondary || eq.WeaponSets == null)
            {
                return null;
            }
            int sets = eq.WeaponSets.Count(w => w != null && !w.Empty());
            if (sets < 2)
            {
                return null;
            }
            int index = eq.SelectedWeaponSetSerialized;
            WeaponSet current = index >= 0 && index < eq.WeaponSets.Length ? eq.WeaponSets[index] : null;
            return new Entry
            {
                Kind = EntryKind.WeaponSwitch,
                Name = "Switch weapons",
                Icon = current != null && !current.Empty() ? current.GetTooltipIcon() : (UIAbilityBar.Instance != null ? UIAbilityBar.Instance.UnarmedIcon : null),
                Badge = RomanNumeral.Convert(index + 1),
                Usable = true,
            };
        }

        /// <summary>
        /// Would activating this make the game wait for a click on a target? Mirrors GenericAbility.Update →
        /// PartyMemberAI.TriggerAbility → Player.StartCasting: no attack, an on-hit trigger, or an attack with a forced
        /// target (self, own companion, paired owner, master) all act straight away.
        /// </summary>
        private static bool NeedsTarget(CharacterStats stats, GenericAbility a, AttackBase attack)
        {
            if (a.TriggerOnHit)
            {
                return false;
            }
            if (a.UsePrimaryAttack || a.UseFullAttack)
            {
                Equipment eq = stats.GetComponent<Equipment>();
                attack = eq != null ? eq.PrimaryAttack : null;
            }
            if (attack == null || attack.HasForcedTarget)
            {
                return false;
            }
            return !attack.ForceTarget(out GameObject _);
        }

        private static string NotReadyText(GenericAbility a)
        {
            GenericAbility.NotReadyValue why = a.WhyNotReady;
            if ((why & GenericAbility.NotReadyValue.AtMaxPer) != 0)
            {
                return "No uses left";
            }
            if ((why & GenericAbility.NotReadyValue.AlreadyActivated) != 0)
            {
                return "Already active";
            }
            if ((why & (GenericAbility.NotReadyValue.InRecovery | GenericAbility.NotReadyValue.InModalRecovery)) != 0)
            {
                return "Recovering";
            }
            if ((why & GenericAbility.NotReadyValue.OnlyInCombat) != 0)
            {
                return "Only in combat";
            }
            if ((why & GenericAbility.NotReadyValue.OnlyOutsideCombat) != 0)
            {
                return "Only outside combat";
            }
            if ((why & GenericAbility.NotReadyValue.NotEnoughFocus) != 0)
            {
                return "Not enough Focus";
            }
            if ((why & (GenericAbility.NotReadyValue.NotInGrimoire | GenericAbility.NotReadyValue.NoGrimoire)) != 0)
            {
                return "Not in the grimoire";
            }
            if ((why & GenericAbility.NotReadyValue.GrimoireCooldown) != 0)
            {
                return "Grimoire recently swapped";
            }
            if ((why & (GenericAbility.NotReadyValue.SpellCastingDisabled | GenericAbility.NotReadyValue.AbilitiesDisabled)) != 0)
            {
                return "Disabled by an effect";
            }
            if ((why & GenericAbility.NotReadyValue.InStealth) != 0)
            {
                return "Not while scouting";
            }
            if ((why & GenericAbility.NotReadyValue.NotWhileMoving) != 0)
            {
                return "Not while moving";
            }
            if ((why & (GenericAbility.NotReadyValue.NotYourTurn | GenericAbility.NotReadyValue.NoAction | GenericAbility.NotReadyValue.UsedThisRound)) != 0)
            {
                return "Not this turn";
            }
            return "Not ready";
        }
    }
}
