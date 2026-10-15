using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace PoEController.Compat
{
    /// <summary>
    /// Startup check that every game type / method / field the mod relies on still exists (looked up by name, so a
    /// changed game can't stop the check itself). Logs "Compatibility: found/total" with anything missing, and turns
    /// off only the <see cref="Features"/> that need a missing member. Patch targets are checked again when each patch
    /// class is applied (Plugin.ApplyPatches).
    /// </summary>
    internal static class CompatibilityCheck
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private readonly struct Need
        {
            public readonly string Feature;
            public readonly string Type;
            public readonly string Member;
            public readonly bool Method;

            public Need(string feature, string type, string member, bool method)
            {
                Feature = feature;
                Type = type;
                Member = member;
                Method = method;
            }
        }

        private static Need F(string feature, string type, string member) => new Need(feature, type, member, false);

        private static Need M(string feature, string type, string member) => new Need(feature, type, member, true);

        private static readonly Need[] s_Needs =
        {
            // Core
            M(Features.GameActions, "GameInput", "GetControlDown"),
            M(Features.GameActions, "GameInput", "GetControlUp"),
            M(Features.ScreenTracking, "UIWindowManager", "WindowShown"),
            M(Features.ScreenTracking, "UIWindowManager", "WindowHidden"),
            M(Features.Camera, "CameraControl", "DoUpdate"),
            F(Features.Camera, "CameraControl", "position_offset"),
            F(Features.Camera, "CameraControl", "PlayerControlDisableLevel"),
            F(Features.Camera, "CameraControl", "m_smartCamera"),
            F(Features.Camera, "CameraControl", "m_FollowingUnits"),
            F(Features.Camera, "CameraControl", "InterpolatingToTarget"),
            M(Features.Camera, "CameraControl", "FocusOnPoint"),
            M(Features.Camera, "CameraControl", "CancelFollow"),
            F(Features.Camera, "Cutscene", "CutsceneActive"),
            // On-screen keyboard
            M(Features.Keyboard, "SteamManager", "TryShowGamepadTextInput"),
            M(Features.Keyboard, "UIInput", "NotifySubmit"),
            M(Features.Keyboard, "UIInput", "OnClick"),
            F(Features.Keyboard, "UIInput", "mText"),
            F(Features.Keyboard, "UIInput", "m_CaratCharacterIndex"),
            M(Features.Keyboard, "UIJournalContentText", "OnChildClick"),
            M(Features.Keyboard, "UIJournalContentText", "OnRecievedText"),
            // Text glyphs + highlighted-word stops (shared label layout)
            M(Features.TextGlyphs, "StringTableManager", "GetText"),
            F(Features.TextGlyphs, "StringTableManager", "CurrentLanguage"),
            M(Features.TextGlyphs, "ControlMapping", "GetControlString"),
            F(Features.TextGlyphs, "UILabel", "processedText"),
            M(Features.TextGlyphs, "UILabel", "ApplyOffset"),
            M(Features.TextGlyphs, "UIFont", "PrintExactCharacterPositions"),
            M(Features.LinkStops, "UILabel", "GetUrlIndexAtCharacterIndex"),
            M(Features.LinkStops, "UIFont", "PrintExactCharacterPositions"),
            F(Features.LinkStops, "UIGlossaryEnabledLabel", "ExtraCollider"),
            // Quick tooltips
            F(Features.QuickTooltips, "UICamera", "mTooltipTime"),
            F(Features.QuickTooltips, "UICamera", "mHover"),
            // Tutorials
            F(Features.Tutorials, "UITutorialBox", "m_CurrentTutorial"),
            F(Features.Tutorials, "UITutorialBox", "Maximized"),
            F(Features.Tutorials, "UITutorialBox", "Minimized"),
            F(Features.Tutorials, "UITutorialBox", "MaximizedBg"),
            F(Features.Tutorials, "UITutorialBox", "ContentLabel"),
            F(Features.Tutorials, "UITutorialBox", "CloseButton"),
            F(Features.Tutorials, "UITutorialBox", "MaximizeButton"),
            M(Features.Tutorials, "UITutorialBox", "Hide"),
            M(Features.Tutorials, "UITutorialBox", "ShowTutorial"),
            F(Features.Tutorials, "TutorialManager", "Tutorials"),
            // Inventory item menu
            M(Features.ItemMenu, "UIInventoryGridItem", "TrySwap"),
            M(Features.ItemMenu, "UIInventoryGridItem", "RemoveItem"),
            M(Features.ItemMenu, "UIInventoryGridItem", "PutItem"),
            M(Features.ItemMenu, "UIInventoryGridItem", "ItemTakeValid"),
            M(Features.ItemMenu, "UIInventoryGridItem", "ItemPlayerTakeValid"),
            M(Features.ItemMenu, "UIInventoryGridItem", "ItemSwapValid"),
            M(Features.ItemMenu, "UIInventoryItemZone", "Remove"),
            M(Features.ItemMenu, "UIInventoryItemZone", "Add"),
            F(Features.ItemMenu, "UIInventoryItemZone", "InfiniteStacking"),
            M(Features.ItemMenu, "UIInventoryManager", "SelectPartyMember"),
            M(Features.ItemMenu, "UIInventoryRowPanel", "RowForPartyMember"),
            M(Features.ItemMenu, "UIInventoryItemGrid", "FirstEmptyGridItem"),
            M(Features.ItemMenu, "UIInventoryEquipment", "GetGridItemForSlot"),
            M(Features.ItemMenu, "UIGlobalInventory", "PostMessage"),
            M(Features.ItemMenu, "BaseInventory", "GetFreeSpace"),
            // Item card buttons
            F(Features.InspectActions, "UIItemInspectManager", "EnchantButton"),
            F(Features.InspectActions, "UIItemInspectManager", "CompareButton"),
            F(Features.InspectActions, "UIItemInspectManager", "InspectionObject"),
            F(Features.InspectActions, "UIMultiSpriteImageButton", "onClick"),
            // Comparison
            F(Features.Compare, "UIItemInspectManager", "s_Last"),
            F(Features.Compare, "UIItemInspectManager", "m_IsStore"),
            F(Features.Compare, "UIItemInspectManager", "ObjectOwner"),
            M(Features.Compare, "UIItemInspectManager", "Examine"),
            M(Features.Compare, "UIItemInspectManager", "ExamineStore"),
            M(Features.Compare, "UIHudWindow", "SetPosition"),
            F(Features.Compare, "UIHudWindow", "Window"),
            M(Features.Compare, "UIInventoryEquipment", "GetComparisonTargets"),
            F(Features.Compare, "UIGlobalSelectAPartyMember", "SelectedCharacter"),
            M(Features.Compare, "CharacterStats", "SmallPortrait"),
            // Hidden cursor hover
            M(Features.HiddenCursorHover, "GameInput", "Update"),
            F(Features.HiddenCursorHover, "GameCursor", "CharacterUnderCursor"),
            M(Features.StopParty, "Player", "CancelAllActions"),
            F(Features.GeneralRadial, "PlayerInventory", "CampingSuppliesTotal"),
            F(Features.GeneralRadial, "InGameHUD", "HighlightActive"),
            F(Features.GeneralRadial, "Stronghold", "Activated"),
            F(Features.GeneralRadial, "PartyMemberAI", "UseInstructionSet"),
            M(Features.GeneralRadial, "AIController", "UpdateAggressionOfSummonedCreatures"),
            M(Features.GeneralRadial, "CharacterStats", "GetMaxLevelCanLevelUpTo"),
            M(Features.GeneralRadial, "UICharacterCreationManager", "OpenCharacterCreation"),
            F(Features.GeneralRadial, "PartyMemberAI", "FormationStyle"),
            F(Features.GeneralRadial, "FormationData", "m_formations"),
            F(Features.GeneralRadial, "UIFormationsManager", "StandardFormationNameIds"),
            M(Features.GeneralRadial, "UIFormationsManager", "ShowFormation"),
            F(Features.CombatRadial, "UIAiCustomizerManager", "SelectedCharacter"),
            F(Features.CombatRadial, "PartyMemberAI", "InstructionSetIndex"),
            F(Features.CombatRadial, "PartyMemberAI", "InstructionSet"),
            F(Features.CombatRadial, "PartyMemberInstructionSetList", "InstructionSetList"),
            M(Features.CombatRadial, "PartyMemberInstructionSetList", "GetClassSpellList"),
            // Spell / item details in the radials
            M(Features.RadialInfo, "GenericAbility", "GetTooltipContent"),
            M(Features.RadialInfo, "Item", "GetTooltipContent"),
            M(Features.RadialInfo, "UIItemInspectManager", "Examine"),
            M(Features.RadialInfo, "NGUITools", "StripSymbols"),
            M(Features.RadialInfo, "GUIUtils", "GetDefenseTypeString"),
            M(Features.RadialInfo, "GUIUtils", "GetDamageTypeString"),
            M(Features.RadialInfo, "UIPartyMemberStatIconGetter", "GetDefenseTypeSprite"),
            M(Features.RadialInfo, "UIPartyMemberStatIconGetter", "GetDamageTypeSprite"),
            M(Features.CombatRadial, "PartyMemberAI", "ExclusiveSelect"),
            M(Features.CombatRadial, "Player", "ObjectClicked"),
            M(Features.CombatRadial, "TacticalModeManager", "GetCanDelayTurn"),
            M(Features.ConsoleMenus, "UIJournalManager", "ChangeScreen"),
            F(Features.ConsoleMenus, "UIJournalManager", "m_CurrentScreen"),
            M(Features.ConsoleMenus, "PartyHelper", "SeekNextPartyMember"),
            M(Features.ConsoleMenus, "PartyHelper", "SeekPreviousPartyMember"),
            M(Features.ConsoleMenus, "UICharacterSheetManager", "SelectCharacter"),
            M(Features.ConsoleMenus, "UIGrimoireManager", "SelectCharacter"),
            M(Features.ConsoleMenus, "UIInventoryManager", "SelectPartyMember"),
            M(Features.ConsoleMenus, "UILootManager", "SelectPartyMember"),
            M(Features.ConsoleMenus, "UILootManager", "SelectStash"),
            F(Features.ConsoleMenus, "UIInventoryManager", "StashAccess"),
            F(Features.ConsoleMenus, "UIStoreManager", "Page"),
            M(Features.ConsoleMenus, "UIGrimoireManager", "LoadGrimoire"),
            M(Features.ConsoleMenus, "UIWindowManager", "SuspendFor"),
            M(Features.ConsoleMenus, "Consumable", "UseImmediately"),
            M(Features.ConsoleMenus, "Consumable", "CanUse"),
            M(Features.ConsoleMenus, "Consumable", "WhyCannotUse"),
            M(Features.ConsoleMenus, "UIInventoryGridItem", "ItemTransferValid"),
            M(Features.ConsoleMenus, "UIInventoryGridItem", "Activate"),
            F(Features.ConsoleMenus, "UIInventoryManager", "QuickItemsGrid"),
            F(Features.CombatLog, "UIConsole", "m_Modes"),
            F(Features.CombatLog, "UIConsole", "MaxEntries"),
            F(Features.CombatLog, "UIConsoleMode", "m_Entries"),
            F(Features.CombatLog, "UIConsoleMode", "m_EndPointer"),
            F(Features.CombatLog, "UIConsoleEntry", "m_Message"),
            F(Features.WorldMap, "UIWorldMapManager", "Instance"),
            F(Features.WorldMap, "UIWorldMapManager", "CurrentIconManager"),
            M(Features.WorldMap, "UIWorldMapManager", "GetCurrentMap"),
            F(Features.WorldMap, "UIWorldMapManager", "MasterLinks"),
            M(Features.WorldMap, "UIWorldMapIcons", "GetIcons"),
            M(Features.WorldMap, "UIWorldMapIcons", "IconForScene"),
            F(Features.WorldMap, "UIWorldMapIcons", "LinkManager"),
            F(Features.WorldMap, "UIWorldMapIcons", "BaseIcon"),
            F(Features.WorldMap, "UIWorldMapLinks", "MapLinks"),
            F(Features.WorldMap, "UIWorldMapMasterLinks", "MapLinks"),
            M(Features.WorldMap, "UIWorldMapMasterLinks", "GetConnected"),
            M(Features.WorldMap, "UIWorldMapMasterLinks", "TravelTimeTo"),
            F(Features.WorldMap, "WorldMapLink", "Place1"),
            F(Features.WorldMap, "WorldMapLink", "Place2"),
            M(Features.WorldMap, "UIWorldMapIcon", "GetData"),
            F(Features.WorldMap, "UIWorldMapIcon", "Widget"),
            F(Features.WorldMap, "UIWorldMapIcon", "CanTravel"),
            F(Features.WorldMap, "UIWorldMapIcon", "VirtualForTag"),
            F(Features.WorldMap, "MapData", "Visibility"),
            M(Features.WorldMap, "UIDraggablePanel", "MoveRelative"),
            M(Features.WorldMap, "UIDraggablePanel", "RestrictWithinBounds"),
            M(Features.WorldMap, "UIDraggablePanel", "DisableSpring"),
            M(Features.LinkColors, "UIGlobalColor", "Get"),
            M(Features.FocusFrameRule, "UIImageButtonRevised", "AddAdditionalCollider"),
            F(Features.FocusFrameRule, "UIInventoryGridItem", "InvItem"),
            F(Features.SlotCount, "UIInventoryGridItem", "QuantityLabel"),
            F(Features.ConversationScroll, "UIConversationManager", "ContentPanel"),
            F(Features.ConversationFocus, "UIConversationManager", "ConversationTextList"),
            F(Features.ConversationFocus, "UIConversationManager", "InteractionTextList"),
            F(Features.ConversationFocus, "UIConversationManager", "m_OutstandingResponses"),
            F(Features.ConversationFocus, "UIConversationManager", "m_MouseSelection"),
            M(Features.ConversationFocus, "UIConsoleText", "LineAt"),
            M(Features.ConversationFocus, "UIConversationManager", "DrawResponses"),
            M(Features.ConversationFocus, "UIConversationManager", "DrawResponsesDebug"),
            F(Features.ConversationFocus, "UIConversationManager", "m_ActiveFlowChart"),
            M(Features.ConversationFocus, "Conversation", "PassesConditionalsEx"),
            M(Features.ConversationFocus, "UIConversationManager", "MarkedAsRead"),
            M(Features.AnalogSpeed, "Mover", "UseCustomSpeed"),
            M(Features.Rumble, "CameraControl", "ScreenShake"),
            F(Features.Rumble, "CameraControl", "m_screenShakeStrength"),
            M(Features.Rumble, "Health", "DoDamage"),
            M(Features.Rumble, "Health", "HandleUnconscious"),
            M(Features.Rumble, "CharacterStats", "ApplyStatusEffectHelper"),
            M(Features.Rumble, "TacticalModeManager", "StartLongCast"),
            M(Features.Rumble, "TacticalModeManager", "HasLongCastEvent"),
            F(Features.Rumble, "TacticalModeManager", "CurrentEvent"),
            F(Features.Rumble, "AttackBase", "IsLongCast"),
            M(Features.Rumble, "AttackBase", "ValidTargetHostile"),
            F(Features.Rumble, "GenericAbility", "Attack"),
            F(Features.Rumble, "GenericAbility", "UsePrimaryAttack"),
            F(Features.Rumble, "GenericAbility", "UseFullAttack"),
            F(Features.Rumble, "AttackBase", "SkipAnimation"),
            F(Features.Rumble, "GameState", "InCombat"),
            F(Features.Lights, "PartyMemberAI", "Slot"),
            F(Features.NguiPadInput, "UICamera", "mList"),
            M(Features.MenuPageFocus, "UICharacterCreationController", "Show"),
            M(Features.MenuPageFocus, "UIOptionsManager", "ChangePage"),
            F(Features.MenuPageFocus, "UIImageButtonRevised", "overridePressed"),
            F(Features.MenuPageFocus, "UIImageButtonRevised", "m_toggledOn"),
            M(Features.MenuPageFocus, "UIPuckScrollbar", "OnUpArrow"),
            F(Features.NguiPadInput, "UICamera", "submitKey1"),
            F(Features.NguiPadInput, "UICamera", "cancelKey1"),
            F(Features.NguiPadInput, "UICamera", "useController"),
            F(Features.Lights, "AIController", "AvailableAction"),
            F(Features.Lights, "Equipment", "PrimaryAttack"),
            F(Features.Lights, "Equipment", "TwoHandedWeapon"),
            F(Features.Lights, "Weapon", "WeaponType"),
            F(Features.Lights, "AttackFirearm", "RequiresReload"),
            F(Features.Lights, "CharacterStats", "ActiveStatusEffects"),
            M(Features.Lights, "CharacterStats", "HasStatusEffectOfType"),
            M(Features.Lights, "CharacterStats", "HasStatusEffectFromAffliction"),
            F(Features.Lights, "StatusEffect", "AfflictionOrigin"),
            F(Features.Lights, "Affliction", "Tag"),
            F(Features.Lights, "AfflictionData", "Paralyzed"),
            F(Features.Lights, "StatusEffectParams", "IsHostile"),
            F(Features.Lights, "StatusEffectParams", "HideFromUi"),
            F(Features.Lights, "StatusEffect", "IsFromAura"),
            F(Features.Lights, "StatusEffect", "Duration"),
            F(Features.Lights, "StatusEffect", "LastsUntilCombatEnds"),
            F(Features.Lights, "StatusEffect", "LastsUntilRest"),
            F(Features.Lights, "Affliction", "HideFromUI"),
            F(Features.Lights, "Health", "CurrentHealth"),
            F(Features.Lights, "Health", "MaxHealth"),
            F(Features.Lights, "PartyMemberAI", "OnlyPrimaryPartyMembers"),
            F(Features.Lights, "Health", "CurrentStamina"),
            F(Features.Lights, "Health", "MaxStamina"),
            F(Features.Lights, "TacticalModeManager", "CurrentEvent"),
            F(Features.Lights, "GameState", "InCombat"),
            F(Features.Rumble, "TacticalModeManager", "IsTacticalModeEnabled"),
            F(Features.Rumble, "StatusEffect", "AbilityOrigin"),
            F(Features.Rumble, "StatusEffect", "Owner"),
            M(Features.Rumble, "Health", "HandleDeath"),
            M(Features.Rumble, "PartyHelper", "IsPartyMember"),
            F(Features.Rumble, "DamageInfo", "IsCriticalHit"),
            M(Features.AnalogSpeed, "Mover", "UseWalkSpeed"),
            M(Features.AnalogSpeed, "Mover", "UseRunSpeed"),
            M(Features.AnalogSpeed, "Mover", "GetWalkSpeed"),
            M(Features.AnalogSpeed, "Mover", "GetRunSpeed"),
            F(Features.AnalogSpeed, "Mover", "DesiredSpeed"),
            F(Features.ConversationScroll, "UIConversationManager", "InteractionContentPanel"),
            F(Features.ConversationScroll, "UIConversationManager", "ConversationWindow"),
            M(Features.ConversationScroll, "UIDraggablePanel", "SetDragAmount"),
            F(Features.SlotCount, "UIInventoryGridItem", "Background"),
            F(Features.SlotCount, "UIInventoryGridItem", "Empty"),
            M(Features.ActiveEffects, "UICharacterSheetStatusEffects", "BundleEffectsForUI"),
            M(Features.ActiveEffects, "StatusEffectParams", "ListToString"),
            M(Features.ActiveEffects, "StatusEffect", "GetDisplayIcon"),
            M(Features.ActiveEffects, "StatusEffect", "GetDisplayName"),
            M(Features.ActiveEffects, "GUIUtils", "CombatTime"),
            F(Features.ActiveEffects, "StatusEffectParams", "IsHostile"),
            M(Features.ConsoleMenus, "UICharacterCreationManager", "PressOkay"),
            M(Features.ConsoleMenus, "UICharacterCreationManager", "PressBack"),
            M(Features.ConsoleMenus, "UICharacterCreationManager", "CanGoBack"),
            F(Features.ConsoleMenus, "UIImageButtonRevised", "forceDown"),
            M(Features.ConsoleMenus, "UICharacterCreationManager", "GetCurrentController"),
            M(Features.ConsoleMenus, "UICharacterCreationController", "ReadyForNextStage"),
            F(Features.ConsoleMenus, "UICharacterCreationNavControl", "Control"),
            F(Features.ConsoleMenus, "UICharacterCreationSkillSetter", "Skill"),
            F(Features.ConsoleMenus, "UICharacterCreationAttributeSetter", "Stat"),
            F(Features.ConsoleMenus, "UIStoreManager", "StoreButton"),
            F(Features.ConsoleMenus, "UIStoreManager", "InnButton"),
            F(Features.ConsoleMenus, "UIStoreManager", "RecruitButton"),
            F(Features.ConsoleMenus, "UIStoreManager", "RespecPartyButton"),
            // Glyphs on game buttons
            F(Features.ButtonGlyphs, "UIMessageBox", "Buttons"),
            F(Features.ButtonGlyphs, "UIConversationManager", "ContinueButton"),
            F(Features.ButtonGlyphs, "UILootManager", "TakeAllButton"),
            F(Features.ButtonGlyphs, "UITriggerButtonByHotkey", "MappedControl"),
            F(Features.ButtonGlyphs, "UIActionBarOnClick", "Hotkey"),
        };

        public static void Run(ModLog log)
        {
            var missing = new List<string>();
            int found = 0;
            foreach (Need n in s_Needs)
            {
                Type type = AccessTools.TypeByName(n.Type);
                bool ok = type != null && (n.Method ? HasMethod(type, n.Member) : HasMember(type, n.Member));
                if (ok)
                {
                    found++;
                    continue;
                }
                string what = type == null ? $"type {n.Type}" : $"{n.Type}.{n.Member}";
                missing.Add($"{what} (needed by: {n.Feature})");
                Features.Off(n.Feature, $"game member {what} not found");
            }
            log.LogInfo($"Compatibility: {found}/{s_Needs.Length} game members found");
            foreach (string m in missing)
            {
                log.LogWarning($"Compatibility: missing {m}");
            }
        }

        private static bool HasMethod(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(All))
                {
                    if (m.Name == name)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasMember(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                if (t.GetField(name, All) != null || t.GetProperty(name, All) != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
