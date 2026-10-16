using System;
using System.Collections.Generic;
using PoEController.Compat;
using PoEController.Input;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Console style's Combat Radial (RT, the console version's layout), red frames. Top ring, clockwise from the top:
    /// Quick items · AI script · 7 custom slots · Abilities · Weapon set · Talk to the companion (Delay turn instead
    /// on your turn in turn-based combat, when the game allows it).
    /// <para>
    /// Abilities, items and weapon switching are the ability wheel's own lists (<see cref="AbilityWheel.BuildTop"/>,
    /// <see cref="AbilityWheel.QuickItems"/>) and are used with its <see cref="AbilityWheel.Use"/>, i.e. exactly what a
    /// click on the ability bar does; abilities that need a target hand over to the world cursor like the wheel did.
    /// Custom slots: X (or A on an empty one) picks an entry from the lists, remembered per character
    /// (<see cref="CustomSlotStore"/>). Opens paused (not in turn-based), LB/RB other character, B back / close.
    /// </para>
    /// </summary>
    internal sealed class CombatRadial : IRadialModel
    {
        public sealed class Node
        {
            public string Name;
            public string IconName;
            public Texture Icon;
            /// <summary>Draw no icon (an empty custom slot).</summary>
            public bool Blank;
            public bool Usable = true;
            public bool Active;
            public string Badge;
            public string Status;
            /// <summary>Leaf from the ability wheel's lists: used with <see cref="AbilityWheel.Use"/>.</summary>
            public AbilityWheel.Entry Use;
            /// <summary>Key for pinning this leaf to a custom slot.</summary>
            public string PinKey;
            public List<Node> Children;
            public Action Run;
            /// <summary>Starts something that waits for a target (force attack): the pause goes to the aiming.</summary>
            public bool NeedsTarget;
            public string ConfirmLabel;
            public string AltLabel;
            /// <summary>X. Returns true if the radial should close.</summary>
            public Func<bool> Alt;
            /// <summary>0–6 for the custom slots (A on an empty one starts choosing).</summary>
            public int CustomSlot = -1;
        }

        private const float RefreshInterval = 0.25f;
        private const float StickSelect = 0.5f;
        private const int FirstCustomSlot = 2;

        private readonly List<string> m_Path = new List<string>();
        private List<Node> m_Shown = new List<Node>();
        private float m_RefreshTimer;
        private bool m_PausedByUs;
        /// <summary>Custom slot being filled (-1 = normal use).</summary>
        private int m_Picking = -1;

        /// <summary>Our party member whose turn it is in turn-based combat (for Delay turn); set by the scheme.</summary>
        public Func<AIController> TurnActor;

        public bool IsOpen { get; private set; }
        public CharacterStats Owner { get; private set; }
        public int Highlight { get; private set; } = -1;
        public bool InSubRing => m_Path.Count > 0 || m_Picking >= 0;
        public bool Picking => m_Picking >= 0;

        /// <summary>Did the last <see cref="Confirm"/> start something that waits for a target?</summary>
        public bool LastNeedsTarget { get; private set; }

        public Node Current => Highlight >= 0 && Highlight < m_Shown.Count ? m_Shown[Highlight] : null;

        public int Count => m_Shown.Count;
        public bool Combat => true;
        public bool SpreadOnHoles => false;

        public string Title => m_Picking >= 0
            ? $"Custom slot {m_Picking + 1}: choose"
            : (m_Path.Count > 0 ? m_Path[m_Path.Count - 1] : null);

        public RadialSlot GetSlot(int index)
        {
            Node n = m_Shown[index];
            return new RadialSlot
            {
                Name = n.Name,
                IconName = n.Blank ? null : n.IconName,
                Icon = n.Blank ? null : n.Icon,
                Usable = n.Usable,
                Active = n.Active,
                Badge = n.Badge,
                Status = n.Status,
                Blank = n.Blank,
                Info = n.Blank ? null : n.Use,
            };
        }

        /// <summary>Where the radial stood (ring, choosing a custom slot, highlighted slot), to come back to it.</summary>
        public sealed class Place
        {
            public List<string> Path;
            public int Picking;
            public int Highlight;
        }

        /// <summary>Y: the highlighted spell / ability / item has a card to open.</summary>
        public bool CanExamine => Current != null && !Current.Blank && RadialInfo.CanExamine(Current.Use);

        /// <summary>
        /// Y: opens the game's card for the highlighted entry. The radial closes (the card is a window over the world);
        /// returns where it stood, for <see cref="Reopen"/> once the card is closed. <paramref name="paused"/> = the
        /// radial had paused the game (the caller now owns that pause).
        /// </summary>
        public Place Examine(out bool paused)
        {
            paused = false;
            Node n = Current;
            if (n == null || !CanExamine)
            {
                return null;
            }
            var place = new Place { Path = new List<string>(m_Path), Picking = m_Picking, Highlight = Highlight };
            CharacterStats owner = Owner;
            Plugin.Log.LogInfo($"Combat radial: examine {n.Name}");
            paused = CloseKeepPause();
            RadialInfo.Examine(n.Use, owner);
            return place;
        }

        /// <summary>Opens again where <see cref="Examine"/> left off. False if the radial can't open now.</summary>
        public bool Reopen(Place place, bool inheritPause)
        {
            if (place == null || !Open(inheritPause))
            {
                return false;
            }
            m_Path.AddRange(place.Path);
            m_Picking = place.Picking;
            Rebuild();
            if (m_Shown.Count == 0)
            {
                // That ring is gone (e.g. its last spell was used up): the top ring instead.
                m_Path.Clear();
                m_Picking = -1;
                Rebuild();
            }
            Highlight = Math.Min(place.Highlight, m_Shown.Count - 1);
            return true;
        }

        /// <param name="inheritPause">The caller already paused on our behalf (coming back from targeting).</param>
        public bool Open(bool inheritPause = false)
        {
            if (!Features.On(Features.CombatRadial) || !CanOpen())
            {
                return false;
            }
            Owner = FindOwner();
            if (Owner == null)
            {
                return false;
            }
            m_Path.Clear();
            m_Picking = -1;
            Highlight = -1;
            Rebuild();
            if (m_Shown.Count == 0)
            {
                return false;
            }
            IsOpen = true;
            m_PausedByUs = inheritPause;
            TimeController tc = TimeController.Instance;
            if (tc != null && !tc.Paused && !TacticalModeManager.IsInTacticalCombat())
            {
                tc.SafePaused = true;
                m_PausedByUs = tc.Paused;
            }
            Plugin.Log.LogInfo("Combat radial opened");
            return true;
        }

        /// <summary>Closes without resuming; returns whether the radial had paused the game (the caller now owns that).</summary>
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
            m_Shown = new List<Node>();
            m_Path.Clear();
            m_Picking = -1;
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
            if (!CanOpen() || !Features.On(Features.CombatRadial))
            {
                Close();
                return;
            }
            CharacterStats owner = FindOwner();
            if (owner == null)
            {
                Close();
                return;
            }
            if (owner != Owner)
            {
                // LB/RB or a portrait click: follow the selection, back to the top ring.
                Owner = owner;
                m_Path.Clear();
                m_Picking = -1;
                Highlight = -1;
                m_RefreshTimer = 0f;
            }
            m_RefreshTimer -= dt;
            if (m_RefreshTimer <= 0f)
            {
                Rebuild();
            }
            if (m_Shown.Count == 0 && !InSubRing)
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

        /// <summary>A. Returns true if the radial should close (something was used).</summary>
        public bool Confirm()
        {
            Node n = Current;
            LastNeedsTarget = false;
            if (n == null)
            {
                return false;
            }
            if (n.Children != null)
            {
                m_Path.Add(n.Name);
                Highlight = -1;
                Rebuild();
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                return false;
            }
            if (m_Picking >= 0)
            {
                return Pick(n);
            }
            if (n.CustomSlot >= 0 && n.Blank)
            {
                StartPicking(n.CustomSlot);
                return false;
            }
            if (!n.Usable)
            {
                if (!string.IsNullOrEmpty(n.Status) && UISystemMessager.Instance != null)
                {
                    UISystemMessager.Instance.PostMessage(n.Status, Color.white);
                }
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                return false;
            }
            Plugin.Log.LogInfo($"Combat radial: {n.Name}");
            if (n.Use != null)
            {
                LastNeedsTarget = n.Use.NeedsTarget;
                CharacterStats owner = Owner;
                AbilityWheel.Entry use = n.Use;
                return Features.Get(Features.CombatRadial, () => AbilityWheel.Use(use, owner), false);
            }
            if (n.Run != null)
            {
                LastNeedsTarget = n.NeedsTarget;
                Features.Run(Features.CombatRadial, n.Run);
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                return true;
            }
            return false;
        }

        /// <summary>X: the slot's extra action (Customize). Returns true if the radial should close.</summary>
        public bool Alternate()
        {
            Node n = Current;
            if (n == null || n.Alt == null || m_Picking >= 0)
            {
                return false;
            }
            Plugin.Log.LogInfo($"Combat radial: {n.AltLabel} ({n.Name})");
            return Features.Get(Features.CombatRadial, n.Alt, false);
        }

        /// <summary>B. Returns true if the radial should close.</summary>
        public bool Back()
        {
            if (m_Path.Count > 0)
            {
                string group = m_Path[m_Path.Count - 1];
                m_Path.RemoveAt(m_Path.Count - 1);
                Rebuild();
                Highlight = Math.Max(-1, m_Shown.FindIndex(x => x.Name == group));
                return false;
            }
            if (m_Picking >= 0)
            {
                int slot = m_Picking;
                m_Picking = -1;
                Rebuild();
                Highlight = FirstCustomSlot + slot;
                return false;
            }
            return true;
        }

        /// <summary>What A would do, for the prompt bar; null = nothing.</summary>
        public string ConfirmLabel
        {
            get
            {
                Node n = Current;
                if (n == null)
                {
                    return null;
                }
                if (n.Children != null)
                {
                    return "Open";
                }
                if (m_Picking >= 0)
                {
                    return n.PinKey != null || n.Run != null ? "Choose" : null;
                }
                if (n.CustomSlot >= 0 && n.Blank)
                {
                    return "Customize";
                }
                if (!n.Usable)
                {
                    return null;
                }
                if (n.Use != null)
                {
                    switch (n.Use.Kind)
                    {
                        case AbilityWheel.EntryKind.WeaponSwitch:
                            return "Switch";
                        case AbilityWheel.EntryKind.Ability:
                            return n.Use.Ability.Modal && n.Use.Ability.UiActivated ? "Turn off" : (n.Use.NeedsTarget ? "Aim" : "Use");
                        default:
                            return n.Use.NeedsTarget ? "Aim" : "Use";
                    }
                }
                return n.ConfirmLabel ?? "Confirm";
            }
        }

        public string AltLabel => m_Picking < 0 && Current != null && Current.Alt != null ? Current.AltLabel : null;

        private void StartPicking(int slot)
        {
            m_Picking = slot;
            m_Path.Clear();
            Highlight = -1;
            Rebuild();
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Combat radial: choosing for custom slot {slot + 1}");
        }

        /// <summary>A while choosing: pin the entry (or clear the slot), back to the top ring on that slot.</summary>
        private bool Pick(Node n)
        {
            if (n.PinKey == null && n.Run == null)
            {
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                return false;
            }
            int slot = m_Picking;
            if (n.Run != null)
            {
                n.Run();
            }
            else
            {
                CustomSlotStore.Set(Owner, slot, n.PinKey, n.Name);
                Plugin.Log.LogInfo($"Combat radial: custom slot {slot + 1} = {n.Name}");
            }
            m_Picking = -1;
            m_Path.Clear();
            Rebuild();
            Highlight = FirstCustomSlot + slot;
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            return false;
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

        private static CharacterStats FindOwner()
        {
            GameObject go = UIAbilityBar.GetSelectedForBars() ?? DirectMovement.SelectedLeader();
            return go != null ? go.GetComponent<CharacterStats>() : null;
        }

        private void Rebuild()
        {
            m_RefreshTimer = RefreshInterval;
            CharacterStats owner = Owner;
            List<Node> top = owner == null ? null
                : Features.Get(Features.CombatRadial, () => m_Picking >= 0 ? BuildPicker(owner, m_Picking) : BuildTop(owner), null);
            List<Node> shown = top ?? new List<Node>();
            // Walk back down the open groups by name (contents are rebuilt live; a vanished group ends the walk).
            for (int i = 0; i < m_Path.Count; i++)
            {
                Node group = shown.Find(x => x.Children != null && x.Name == m_Path[i]);
                if (group == null)
                {
                    m_Path.RemoveRange(i, m_Path.Count - i);
                    break;
                }
                shown = group.Children;
            }
            m_Shown = shown;
        }

        // ---- Contents ----

        private List<Node> BuildTop(CharacterStats owner)
        {
            List<Node> abilities = AbilityNodes(owner);
            var byKey = new Dictionary<string, Node>();
            CollectLeaves(abilities, byKey);
            var top = new List<Node>(RadialLayout.Holes)
            {
                QuickItemsNode(owner),
                AttackNode(),
            };
            for (int k = 0; k < CustomSlotStore.Count; k++)
            {
                top.Add(CustomNode(owner, k, byKey));
            }
            top.Add(new Node
            {
                Name = "Abilities",
                IconName = ClassIcon(owner),
                Children = abilities.Count > 0 ? abilities : null,
                Usable = abilities.Count > 0,
                Status = abilities.Count > 0 ? "Spells, chants, abilities, modes, items" : "Nothing to use",
            });
            top.Add(WeaponNode(owner));
            top.Add(TalkOrDelayNode(owner));
            return top;
        }

        /// <summary>The picker for a custom slot: "Clear slot" (if filled) + the ability wheel's groups.</summary>
        private static List<Node> BuildPicker(CharacterStats owner, int slot)
        {
            var list = new List<Node>();
            if (!string.IsNullOrEmpty(CustomSlotStore.Get(owner, slot).Key))
            {
                list.Add(new Node
                {
                    Name = "Clear slot",
                    IconName = "ICO_cancel",
                    Status = $"Empty custom slot {slot + 1}",
                    Run = () => CustomSlotStore.Set(owner, slot, null, null),
                });
            }
            list.AddRange(AbilityNodes(owner));
            return list;
        }

        private static List<Node> AbilityNodes(CharacterStats owner)
        {
            var entries = new List<AbilityWheel.Entry>();
            AbilityWheel.BuildTop(owner, entries);
            return entries.ConvertAll(Convert);
        }

        private static Node Convert(AbilityWheel.Entry e)
        {
            if (e.Kind == AbilityWheel.EntryKind.Group)
            {
                return new Node
                {
                    Name = e.Name,
                    Icon = e.Icon,
                    Badge = e.Badge,
                    Status = e.Status,
                    Children = e.Children.ConvertAll(Convert),
                };
            }
            return new Node
            {
                Name = e.Name,
                Icon = e.Icon,
                IconName = e.Icon == null ? "unarmed_L" : null,
                Badge = e.Badge,
                Usable = e.Usable,
                Active = e.Active,
                Status = e.Status,
                Use = e,
                PinKey = PinKeyOf(e),
            };
        }

        private static void CollectLeaves(List<Node> nodes, Dictionary<string, Node> into)
        {
            foreach (Node n in nodes)
            {
                if (n.Children != null)
                {
                    CollectLeaves(n.Children, into);
                }
                else if (n.PinKey != null && !into.ContainsKey(n.PinKey))
                {
                    into[n.PinKey] = n;
                }
            }
        }

        private static string PinKeyOf(AbilityWheel.Entry e)
        {
            switch (e.Kind)
            {
                case AbilityWheel.EntryKind.Ability:
                    return e.Ability != null ? "a:" + Strip(e.Ability.name) : null;
                case AbilityWheel.EntryKind.Item:
                    return e.Item != null && e.Item.baseItem != null ? "i:" + Strip(e.Item.baseItem.name) : null;
                case AbilityWheel.EntryKind.WeaponSwitch:
                    return "w";
                default:
                    return null;
            }
        }

        private static string Strip(string name) => name == null ? null : name.Replace("(Clone)", "").Trim();

        private static Node QuickItemsNode(CharacterStats owner)
        {
            List<Node> items = AbilityWheel.QuickItems(owner).ConvertAll(Convert);
            return new Node
            {
                Name = "Quick items",
                IconName = "quickItems_S",
                Children = items.Count > 0 ? items : null,
                Usable = items.Count > 0,
                Badge = items.Count > 0 ? items.Count.ToString() : null,
                Status = items.Count > 0 ? null : "No quick items",
            };
        }

        /// <summary>
        /// Force attack: the HUD's sword button (UIActionBarOnClick.HandleAttack, keyboard A). The game's attack cursor
        /// then attacks whatever is picked, also characters it doesn't count as enemies (charmed / dominated ones) and
        /// objects meant to be attacked. As a spell: the radial closes, the pointer picks the target, A attacks, B
        /// cancels back to the radial. Who attacks = the selected party, as with the mouse.
        /// </summary>
        private static Node AttackNode()
        {
            bool anyone = GameState.s_playerCharacter != null && PartyMemberAI.NumSelectedMembers() > 0;
            return new Node
            {
                Name = "Attack",
                IconName = "ICO_attack",
                Usable = anyone,
                Status = anyone ? "Any target, even a friendly-looking one" : "Select a character first",
                ConfirmLabel = "Attack",
                NeedsTarget = true,
                Run = () => GameState.s_playerCharacter.IsInForceAttackMode = true,
            };
        }

        /// <summary>The name of the character's AI script (also shown by the General Radial's AI slot).</summary>
        internal static string ScriptName(CharacterStats owner, PartyMemberAI pm)
        {
            int index = pm.InstructionSetIndex;
            if (index >= 0 && pm.InstructionSet != null && PartyMemberInstructionSetList.InstructionSetList != null)
            {
                PartyMemberSpellList[] lists = PartyMemberInstructionSetList.InstructionSetList.GetClassSpellList(owner.CharacterClass).SpellLists;
                if (lists != null && index < lists.Length && lists[index] != null)
                {
                    return lists[index].ToString();
                }
            }
            // The game's own "no script" entry reads "None"; say what it means here.
            return "no script";
        }

        private Node CustomNode(CharacterStats owner, int slot, Dictionary<string, Node> byKey)
        {
            CustomSlotStore.Pin pin = CustomSlotStore.Get(owner, slot);
            Node n;
            if (string.IsNullOrEmpty(pin.Key))
            {
                n = new Node { Name = $"Custom slot {slot + 1} (Empty)", Blank = true, Usable = false, Status = "A or X to choose what goes here" };
            }
            else if (byKey.TryGetValue(pin.Key, out Node leaf))
            {
                n = new Node
                {
                    Name = leaf.Name,
                    Icon = leaf.Icon,
                    IconName = leaf.IconName,
                    Badge = leaf.Badge,
                    Usable = leaf.Usable,
                    Active = leaf.Active,
                    Status = leaf.Status,
                    Use = leaf.Use,
                    PinKey = leaf.PinKey,
                };
            }
            else
            {
                n = new Node { Name = pin.Name, Usable = false, Status = "Not available right now" };
            }
            n.CustomSlot = slot;
            n.AltLabel = "Customize";
            n.Alt = () =>
            {
                StartPicking(slot);
                return false;
            };
            return n;
        }

        private static Node WeaponNode(CharacterStats owner)
        {
            AbilityWheel.Entry e = AbilityWheel.WeaponSwitch(owner);
            if (e == null)
            {
                return new Node { Name = "Weapon set", IconName = "unarmed_L", Usable = false, Status = "Only one weapon set" };
            }
            Node n = Convert(e);
            n.Name = "Switch weapon set";
            return n;
        }

        private Node TalkOrDelayNode(CharacterStats owner)
        {
            AIController actor = TurnActor?.Invoke();
            if (TurnChecks.CanDelayTurn(actor))
            {
                return new Node
                {
                    Name = "Delay turn",
                    IconName = "ICO_cancel",
                    Status = "Act later this round",
                    ConfirmLabel = "Delay",
                    // The HUD button's own hotkey: down now, up next frame = one click.
                    Run = () =>
                    {
                        VirtualControls.Hold(MappedControl.WAIT_TURN);
                        Plugin.Log.LogInfo("Turn: delay (combat radial)");
                    },
                };
            }
            GameObject player = GameState.s_playerCharacter != null ? GameState.s_playerCharacter.gameObject : null;
            NPCDialogue dialogue = owner.GetComponent<NPCDialogue>();
            if (owner.gameObject == player || dialogue == null)
            {
                return new Node { Name = "Talk", IconName = "ICO_talk", Usable = false, Status = "Pick a companion with LB / RB" };
            }
            bool usable = dialogue.IsUsable;
            return new Node
            {
                Name = "Talk to " + owner.Name(),
                IconName = "ICO_talk",
                Usable = usable,
                Status = usable ? null : (GameState.InCombat ? "Not during combat" : "Busy right now"),
                ConfirmLabel = "Talk",
                // As the portrait's "wants to talk" icon: the main character starts the conversation.
                Run = () =>
                {
                    GameState.s_playerCharacter.GetComponent<PartyMemberAI>().ExclusiveSelect();
                    GameState.s_playerCharacter.ObjectClicked(dialogue);
                },
            };
        }

        private static string ClassIcon(CharacterStats owner)
        {
            switch (owner.CharacterClass)
            {
                case CharacterStats.Class.Priest: return "ICO_ability_priest";
                case CharacterStats.Class.Wizard: return "ICO_ability_wizard";
                case CharacterStats.Class.Druid: return "ICO_ability_druid";
                case CharacterStats.Class.Cipher: return "ICO_ability_cipher";
                case CharacterStats.Class.Chanter: return "ICO_CLASS_chanter";
                default: return "ICO_ability_generic";
            }
        }
    }
}
