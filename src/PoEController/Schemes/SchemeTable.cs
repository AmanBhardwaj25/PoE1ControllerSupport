using System;
using System.Collections.Generic;
using PoEController.Input;

namespace PoEController.Schemes
{
    /// <summary>
    /// Built-in schemes and the per-window table. Only bindings that actually work are listed, so the prompt bar
    /// shows exactly what the controller can do right now. Menus use <see cref="MenuScheme"/> (cursor navigation)
    /// plus close / screen / tab bindings.
    /// </summary>
    public static class SchemeTable
    {
        private static readonly Binding Close = new Binding(PadButton.East, MappedControl.CLOSE_WINDOW, "Close");
        private static readonly Binding StartCloses = new Binding(PadButton.Start, MappedControl.CLOSE_WINDOW, null);
        private static readonly Binding PrevScreen = new Binding(PadButton.LeftShoulder, MappedControl.MENU_CYCLE_LEFT, "Switch screen");
        private static readonly Binding NextScreen = new Binding(PadButton.RightShoulder, MappedControl.MENU_CYCLE_RIGHT, "Switch screen");
        // NEXT_TAB means something different per window (see each window's HandleInput).
        private static readonly Binding NextTab = new Binding(PadButton.RightTrigger, MappedControl.NEXT_TAB, "Next tab");
        private static readonly Binding NextCharacter = new Binding(PadButton.RightTrigger, MappedControl.NEXT_TAB, "Next character");
        private static readonly Binding NextSpellLevel = new Binding(PadButton.RightTrigger, MappedControl.NEXT_TAB, "Next spell level");

        /// <summary>World in PC layout (unchanged since 0.21.1).</summary>
        public static readonly ControlScheme World = new WorldScheme();

        /// <summary>World in Console style.</summary>
        public static readonly ControlScheme ConsoleWorld = new ConsoleWorldScheme();

        public static ControlScheme WorldFor(ControlStyle style) => style == ControlStyle.Console ? ConsoleWorld : World;

        /// <summary>
        /// The scheme for one window type: in Console style its <see cref="ConsoleByWindow"/> entry if there is one,
        /// otherwise (and always in PC layout) its <see cref="ByWindow"/> entry.
        /// </summary>
        public static bool TryGetWindow(ControlStyle style, Type window, out ControlScheme scheme)
        {
            if (style == ControlStyle.Console && ConsoleByWindow.TryGetValue(window, out scheme))
            {
                return true;
            }
            return ByWindow.TryGetValue(window, out scheme);
        }

        public static readonly ControlScheme MainMenu = new MenuScheme("Main menu");

        /// <summary>On-screen keyboard; overrides every other scheme while open.</summary>
        public static readonly ControlScheme Keyboard = new KeyboardScheme();

        /// <summary>The mod's settings panel (View + Start); above everything but the keyboard.</summary>
        public static readonly ControlScheme Settings = new SettingsScheme();

        /// <summary>Tutorial pop-up; above every window.</summary>
        public static readonly ControlScheme Tutorial = new TutorialScheme();

        /// <summary>Item comparison (portrait picker / comparison windows); above the inspect windows.</summary>
        public static readonly ControlScheme Compare = new CompareScheme();

        /// <summary>Picking the other item for a comparison in the window behind the put-away card.</summary>
        public static readonly ControlScheme CompareChoose = new CompareChooseScheme();

        /// <summary>Any window without its own entry: cursor navigation, B closes.</summary>
        public static readonly ControlScheme GenericMenu = new MenuScheme("Menu", Close, StartCloses);

        private static ControlScheme Screen(string name, params Binding[] extra)
        {
            var bindings = new List<Binding>(extra) { PrevScreen, NextScreen, Close, StartCloses };
            return new MenuScheme(name, bindings.ToArray());
        }

        public static readonly Dictionary<Type, ControlScheme> ByWindow = new Dictionary<Type, ControlScheme>
        {
            {
                typeof(UIMessageBox), new BindingScheme("Message box",
                    new Binding(PadButton.South, MappedControl.MB_CONFIRM, "Confirm"),
                    new Binding(PadButton.East, MappedControl.CLOSE_WINDOW, "Cancel"))
            },
            {
                // The keyboard opens over this by itself; after closing it, the cursor reaches OK / Cancel / the box.
                typeof(UIStringPromptBox), new MenuScheme("Text entry",
                    new Binding(PadButton.East, MappedControl.MB_CANCEL, "Cancel"))
            },
            {
                typeof(UIConversationManager), new BindingScheme("Conversation",
                    new Binding(PadButton.DpadUp, MappedControl.UI_UP, "Choose"),
                    new Binding(PadButton.DpadDown, MappedControl.UI_DOWN, "Choose"),
                    new Binding(PadButton.South, MappedControl.UI_SELECT, "Select / Continue"),
                    new Binding(PadButton.South, MappedControl.CONV_CONTINUE, null))
            },
            // Item inspect window (X on an item anywhere): its buttons on X / Y (or Y = list of all).
            { typeof(UIItemInspectManager), new InspectScheme(Close, StartCloses) },
            { typeof(UIStoreManager), new StoreScheme(Close, StartCloses) },
            { typeof(UILootManager), new MenuScheme("Loot", new Binding(PadButton.North, MappedControl.TAKE_ALL, "Take all"), Close, StartCloses) },
            { typeof(UIInventoryManager), new InventoryScheme("Inventory", false, NextCharacter, PrevScreen, NextScreen, Close, StartCloses) },
            // Opens over the inventory; the party rows below stay reachable.
            { typeof(UIStashManager), new InventoryScheme("Stash", true, Close, StartCloses) },
            { typeof(UICharacterSheetManager), Screen("Character", NextCharacter) },
            { typeof(UIGrimoireManager), Screen("Grimoire", NextSpellLevel) },
            { typeof(UIJournalManager), Screen("Journal", NextTab) },
            { typeof(UIAreaMapManager), Screen("Area map") },
            { typeof(UIPartyManager), Screen("Party") },
            { typeof(UIFormationsManager), Screen("Formations") },
            { typeof(UIStrongholdManager), Screen("Stronghold") },
        };

        /// <summary>
        /// Console-style window schemes (after the console version). Empty for now: every window uses its PC-layout entry
        /// above. Step 3 adds windows here one by one; the PC table stays untouched.
        /// </summary>
        private static readonly Binding PrevScreenLT = new Binding(PadButton.LeftTrigger, MappedControl.MENU_CYCLE_LEFT, "Screen");
        private static readonly Binding NextScreenRT = new Binding(PadButton.RightTrigger, MappedControl.MENU_CYCLE_RIGHT, "Screen");

        private static ControlScheme ConsoleScreen(string name, string shoulderLabel, Action<int> step) =>
            new ConsoleScreenScheme(name, shoulderLabel, step, PrevScreenLT, NextScreenRT, Close, StartCloses);

        public static readonly Dictionary<Type, ControlScheme> ConsoleByWindow = new Dictionary<Type, ControlScheme>
        {
            { typeof(UICharacterSheetManager), ConsoleScreen("Character (Console)", "Character", ConsoleScreenScheme.StepCharacterSheet) },
            { typeof(UIGrimoireManager), ConsoleScreen("Grimoire (Console)", "Character", ConsoleScreenScheme.StepGrimoire) },
            { typeof(UIJournalManager), ConsoleScreen("Journal (Console)", "Tab", ConsoleScreenScheme.StepJournalTab) },
            { typeof(UIAreaMapManager), ConsoleScreen("Area map (Console)", null, null) },
            // Travel screen: LS steps between locations along the roads, RS pans, R3 current location.
            { typeof(UIWorldMapManager), new ConsoleWorldMapScheme(Close, StartCloses) },
            { typeof(UIPartyManager), ConsoleScreen("Party (Console)", null, null) },
            { typeof(UIFormationsManager), ConsoleScreen("Formations (Console)", null, null) },
            { typeof(UIStrongholdManager), ConsoleScreen("Stronghold (Console)", null, null) },
            { typeof(UIStoreManager), new ConsoleStoreScheme(Close, StartCloses) },
            // Level-up and character creation (one window): its own buttons only, B = Quit (asks), Start = Done.
            { typeof(UICharacterCreationManager), new ConsoleLevelUpScheme() },
            { typeof(UIInventoryManager), new ConsoleInventoryScheme("Inventory (Console)", false, PrevScreenLT, NextScreenRT, Close, StartCloses) },
            // Opens over the inventory; the party rows below stay reachable.
            { typeof(UIStashManager), new ConsoleInventoryScheme("Stash (Console)", true, Close, StartCloses) },
            { typeof(UILootManager), new ConsoleLootScheme(new Binding(PadButton.West, MappedControl.TAKE_ALL, "Take all"), Close, StartCloses) },
        };
    }
}
