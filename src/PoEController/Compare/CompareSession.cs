using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Compare
{
    /// <summary>
    /// Our item comparison, replacing the game's Compare button flow for the controller. The game compares only with
    /// the selected character's first matching item; here:
    /// <list type="bullet">
    /// <item><b>With equipped</b>: a portrait strip of the party; d-pad ←/→ changes companion with the comparison
    /// windows updating live, A keeps it. Everything the item would replace is shown — both weapon hands, both rings
    /// (the game can show three inspect windows: the item + two).</item>
    /// <item><b>With an item from inventory</b>: the card is put away and the item is picked in the window behind it.</item>
    /// <item><b>Quick compare</b> (shops, Y): the item's inspect window + the selected companion's equipped items.</item>
    /// </list>
    /// Comparison windows are ordinary inspect windows opened the way the game's own Compare does
    /// (<c>UIItemInspectManager.Examine(item, owner)</c> + <c>SetPosition</c> beside the item's window).
    /// </summary>
    internal static class CompareSession
    {
        public enum Stage
        {
            Off,
            /// <summary>Portrait strip up; ←/→ change companion (live), A keeps, B cancels.</summary>
            Picking,
            /// <summary>Comparison shown; B closes it, X changes companion / item.</summary>
            Viewing,
            /// <summary>The item's card is put away; pick an item to compare with from the window behind it.</summary>
            Choosing,
        }

        /// <summary>Horizontal distance between neighbouring inspect windows (what the game's Compare uses).</summary>
        private const float WindowSpacing = 490f;

        private static readonly FieldInfo s_LastWindow = AccessTools.Field(typeof(UIItemInspectManager), "s_Last");
        private static readonly FieldInfo s_IsStoreField = AccessTools.Field(typeof(UIItemInspectManager), "m_IsStore");

        /// <summary>How the item's card was opened, to open it again after choosing.</summary>
        private static GameObject s_SourceOwner;
        private static bool s_SourceIsStore;

        private static readonly List<UIItemInspectManager> s_Windows = new List<UIItemInspectManager>();
        private static Equippable s_Item;
        private static UIItemInspectManager s_Source;
        private static bool s_OwnSource;
        private static bool s_FromInventory;

        public static Stage Mode { get; private set; }
        public static bool Active => Mode != Stage.Off;
        /// <summary>X in Viewing: pick another companion (equipped compare) or another item (inventory compare).</summary>
        public static string ChangeLabel => s_FromInventory ? "Other item" : "Other companion";

        public static readonly List<PartyMemberAI> Members = new List<PartyMemberAI>();
        /// <summary>Per member: null if comparable, else why not (dimmed in the strip).</summary>
        public static readonly List<string> MemberStatus = new List<string>();
        public static int MemberIndex { get; private set; }

        /// <summary>The item can be compared at all (equipment that goes in a slot).</summary>
        public static bool Comparable(Item item)
        {
            return item is Equippable e && e.GetPreferredSlot() != Equippable.EquipmentSlot.None;
        }

        /// <summary>The character the inventory / shop currently shows (shared by both windows).</summary>
        public static PartyMemberAI SelectedMember()
        {
            CharacterStats stats = UIGlobalSelectAPartyMember.Instance != null ? UIGlobalSelectAPartyMember.Instance.SelectedCharacter : null;
            return stats != null ? stats.GetComponent<PartyMemberAI>() : null;
        }

        /// <summary>Compare <paramref name="item"/> (shown in <paramref name="source"/>) with what the party has equipped.</summary>
        public static void StartEquipped(Equippable item, UIItemInspectManager source)
        {
            Begin(item, source, ownSource: false, fromInventory: false);
            FillMembers(SelectedMember());
            ShowMember();
            Mode = Stage.Picking;
        }

        /// <summary>Shop quick compare: opens the item's inspect window and the selected companion's equipped items. False = nothing to compare (caller plays the fail sound).</summary>
        public static bool QuickCompare(Equippable item, bool storeItem)
        {
            PartyMemberAI member = SelectedMember();
            if (member == null || Targets(item, member).Count == 0)
            {
                return false;
            }
            if (storeItem)
            {
                UIItemInspectManager.ExamineStore(item, member.gameObject);
            }
            else
            {
                UIItemInspectManager.Examine(item, member.gameObject);
            }
            UIItemInspectManager source = LastWindow();
            if (source == null)
            {
                return false;
            }
            Begin(item, source, ownSource: true, fromInventory: false);
            FillMembers(member);
            ShowMember();
            Mode = Stage.Viewing;
            return true;
        }

        /// <summary>
        /// "Compare with item from inventory": put the card away and let the player pick an item in the window behind
        /// it (<see cref="Schemes.CompareChooseScheme"/>); <see cref="Choose"/> brings the card back with the comparison.
        /// </summary>
        public static void StartChoosing(Equippable item, UIItemInspectManager source)
        {
            GameObject owner = source != null ? Compat.GameRef.Get<GameObject>(source, "ObjectOwner") : null;
            bool isStore = source != null && s_IsStoreField != null && (bool)s_IsStoreField.GetValue(source);
            Begin(item, source, ownSource: false, fromInventory: true);
            s_SourceOwner = owner;
            s_SourceIsStore = isStore;
            if (s_Source != null && s_Source.WindowActive())
            {
                s_Source.HideWindow();
            }
            s_Source = null;
            Mode = Stage.Choosing;
        }

        /// <summary>The slot holds something the item can be compared with (greyed out and unselectable otherwise).</summary>
        public static bool CanChoose(UIInventoryGridItem slot)
        {
            return s_Item != null && slot != null && slot.InvItem != null && slot.InvItem.baseItem is Equippable e
                && e != s_Item && SameKind(s_Item, e);
        }

        /// <summary>Picked: card back, comparison beside it.</summary>
        public static void Choose(UIInventoryGridItem slot)
        {
            Equippable other = (Equippable)slot.InvItem.baseItem;
            GameObject holder = slot.Owner != null ? slot.Owner.OwnerGameObject : null;
            ReopenSource();
            if (s_Source == null)
            {
                End();
                return;
            }
            OpenWindow(other, holder, 0);
            Mode = Stage.Viewing;
        }

        /// <summary>B while choosing: back to the card.</summary>
        public static void CancelChoosing()
        {
            ReopenSource();
            // The card is the player's again (not a comparison of ours): forget it without closing it.
            Mode = Stage.Off;
            s_Source = null;
            s_Item = null;
            Members.Clear();
            MemberStatus.Clear();
        }

        private static void ReopenSource()
        {
            if (s_Item == null)
            {
                return;
            }
            if (s_SourceIsStore)
            {
                UIItemInspectManager.ExamineStore(s_Item, s_SourceOwner);
            }
            else
            {
                UIItemInspectManager.Examine(s_Item, s_SourceOwner);
            }
            s_Source = LastWindow();
        }

        /// <summary>Picking: next / previous companion.</summary>
        public static void Step(int dir)
        {
            if (Members.Count == 0)
            {
                return;
            }
            MemberIndex = (MemberIndex + dir + Members.Count) % Members.Count;
            ShowMember();
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        public static void Keep() => Mode = Stage.Viewing;

        /// <summary>Viewing → Picking again (equipped compare only).</summary>
        public static void PickAgain()
        {
            if (!s_FromInventory && Members.Count > 0)
            {
                Mode = Stage.Picking;
            }
        }

        public static UIItemInspectManager Source => s_Source;
        public static Equippable Item => s_Item;
        public static bool FromInventory => s_FromInventory;

        /// <summary>The first comparison window (RS scrolls it).</summary>
        public static UIItemInspectManager FirstWindow => s_Windows.Count > 0 ? s_Windows[0] : null;

        /// <summary>Closes the comparison windows (and the item's window if we opened it).</summary>
        public static void End()
        {
            if (Mode == Stage.Off)
            {
                return;
            }
            Mode = Stage.Off;
            CloseWindows();
            if (s_OwnSource && s_Source != null && s_Source.WindowActive())
            {
                s_Source.HideWindow();
            }
            s_Source = null;
            s_Item = null;
            Members.Clear();
            MemberStatus.Clear();
        }

        /// <summary>
        /// Runs a compare / popup action; on any error the comparison is cancelled and logged instead of taking the
        /// whole controller down (scheme exceptions disable the plugin).
        /// </summary>
        public static void Safe(Action action, string what, string feature = Compat.Features.Compare)
        {
            if (!Compat.Features.On(feature))
            {
                return;
            }
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Compat.Features.Off(feature, $"{what} failed: {e}");
                try
                {
                    PopupMenu.Close();
                    End();
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }
        }

        /// <summary>Every frame (router): ends the session when its windows are gone (closed by mouse, scene change…).</summary>
        public static void Validate()
        {
            if (Mode == Stage.Off)
            {
                return;
            }
            if (Mode == Stage.Choosing)
            {
                // The card is put away on purpose; only give up if the window behind it is gone too.
                if (s_Item == null || Context.ScreenTracker.Top == null)
                {
                    End();
                }
                return;
            }
            if (s_Source == null || !s_Source.WindowActive() || s_Item == null)
            {
                End();
            }
        }

        private static void Begin(Equippable item, UIItemInspectManager source, bool ownSource, bool fromInventory)
        {
            End();
            s_Item = item;
            s_Source = source;
            s_OwnSource = ownSource;
            s_FromInventory = fromInventory;
        }

        private static void FillMembers(PartyMemberAI preferred)
        {
            Members.Clear();
            MemberStatus.Clear();
            MemberIndex = 0;
            foreach (PartyMemberAI m in PartyMemberAI.OnlyPrimaryPartyMembers)
            {
                if (m == null)
                {
                    continue;
                }
                if (m == preferred)
                {
                    MemberIndex = Members.Count;
                }
                Members.Add(m);
                MemberStatus.Add(WhyNot(s_Item, m));
            }
        }

        private static string WhyNot(Equippable item, PartyMemberAI m)
        {
            switch (item.WhyCantEquip(m.gameObject))
            {
                case Equippable.CantEquipReason.ClassMismatch:
                    return "Wrong class";
                case Equippable.CantEquipReason.SoulboundToOther:
                    return "Soulbound";
            }
            return Targets(item, m).Count == 0 ? "Nothing equipped" : null;
        }

        private static void ShowMember()
        {
            CloseWindows();
            if (MemberIndex < 0 || MemberIndex >= Members.Count)
            {
                return;
            }
            PartyMemberAI m = Members[MemberIndex];
            List<Equippable> targets = Targets(s_Item, m);
            for (int i = 0; i < targets.Count && i < 2; i++)
            {
                OpenWindow(targets[i], m.gameObject, i);
            }
        }

        /// <summary>
        /// What <paramref name="item"/> would replace on <paramref name="m"/>: the game's comparison targets (selected
        /// weapon set's hands the item can use, or the item in its slot) plus the other ring for rings.
        /// </summary>
        public static List<Equippable> Targets(Equippable item, PartyMemberAI m)
        {
            var list = new List<Equippable>(2);
            Equipment eq = m != null ? m.GetComponent<Equipment>() : null;
            if (item == null || eq == null || eq.CurrentItems == null)
            {
                return list;
            }
            foreach (Item.UIEquippedItem t in UIInventoryEquipment.GetComparisonTargets(item, eq))
            {
                if (t.item is Equippable e && e != item && !list.Contains(e))
                {
                    list.Add(e);
                }
            }
            Equippable.EquipmentSlot slot = item.GetPreferredSlot();
            if (slot == Equippable.EquipmentSlot.RightRing || slot == Equippable.EquipmentSlot.LeftRing)
            {
                foreach (Equippable.EquipmentSlot ring in new[] { Equippable.EquipmentSlot.RightRing, Equippable.EquipmentSlot.LeftRing })
                {
                    Equippable e = eq.CurrentItems.GetItemInSlot(ring);
                    if (e != null && e != item && !list.Contains(e))
                    {
                        list.Add(e);
                    }
                }
            }
            return list;
        }

        /// <summary>Opens an inspect window for <paramref name="item"/> beside the source: first on the right, second on the left.</summary>
        private static void OpenWindow(Equippable item, GameObject owner, int position)
        {
            GameObject sourceWindow = s_Source != null ? Compat.GameRef.Object(s_Source, "Window") : null;
            if (item == null || sourceWindow == null)
            {
                return;
            }
            UIItemInspectManager.Examine(item, owner);
            UIItemInspectManager w = LastWindow();
            if (w == null || w == s_Source || !w.WindowActive())
            {
                Plugin.Log.LogWarning($"Compare: no inspect window available for {item.name}");
                return;
            }
            // The comparison shows someone else's equipment: no Compare/Enchant buttons of its own.
            Compat.GameRef.Set(w, "NoCompare", true);
            Vector3 offset = new Vector3(position == 0 ? WindowSpacing : -WindowSpacing, 0f, 0f);
            w.SetPosition(sourceWindow.transform.localPosition + offset);
            s_Windows.Add(w);
        }

        private static void CloseWindows()
        {
            foreach (UIItemInspectManager w in s_Windows)
            {
                if (w != null && w.WindowActive())
                {
                    w.HideWindow();
                }
            }
            s_Windows.Clear();
        }

        private static UIItemInspectManager LastWindow()
        {
            return s_LastWindow != null ? s_LastWindow.GetValue(null) as UIItemInspectManager : null;
        }

        /// <summary>Competes for the same slot: weapons/shields sharing a hand, rings with rings, otherwise the same slot.</summary>
        private static bool SameKind(Equippable a, Equippable b)
        {
            Equippable.EquipmentSlot sa = a.GetPreferredSlot();
            Equippable.EquipmentSlot sb = b.GetPreferredSlot();
            if (Equippable.IsWeapon(sa) || Equippable.IsWeapon(sb))
            {
                return Equippable.IsWeapon(sa) && Equippable.IsWeapon(sb)
                    && ((a.PrimaryWeaponSlot && b.PrimaryWeaponSlot) || (a.SecondaryWeaponSlot && b.SecondaryWeaponSlot));
            }
            bool ringA = sa == Equippable.EquipmentSlot.RightRing || sa == Equippable.EquipmentSlot.LeftRing;
            bool ringB = sb == Equippable.EquipmentSlot.RightRing || sb == Equippable.EquipmentSlot.LeftRing;
            return ringA ? ringB : sa == sb;
        }
    }
}
