using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PoEController.Context;
using PoEController.Glyphs;
using PoEController.Input;
using PoEController.Schemes;
using PoEController.UI;
using PoEController.World;
using UnityEngine;

namespace PoEController
{
    public enum GlyphOverride
    {
        Auto,
        Xbox,
        PlayStation,
        Switch,
        SteamDeck,
    }

    /// <summary>How the A prompt over the world target is labelled (config Prompts.TargetLabel).</summary>
    public enum TargetLabelMode
    {
        Full,
        GlyphOnly,
        Brief,
    }

    /// <summary>Which control scheme the pad uses (config Controls.Style; the console version's layout).</summary>
    public enum ControlStyle
    {
        /// <summary>Switch-style controls (radials on the triggers, X pauseâ€¦). Default.</summary>
        Console,
        /// <summary>The mod's original controls built around the PC HUD (= 0.21.1).</summary>
        PcLayout,
    }

    [BepInPlugin(Guid, "PoE Controller", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "poe.controller";
        public const string Version = "0.41.0";

        private ConfigEntry<ControlStyle> m_ControlStyle;
        private ConfigEntry<bool> m_ConsoleShortBar;
        private ConfigEntry<bool> m_ConsoleHideActionBar;
        private ConfigEntry<bool> m_ConsoleCombatPointer;
        private bool m_TrimHud;

        private ConfigEntry<GlyphOverride> m_GlyphOverride;
        private ConfigEntry<float> m_Deadzone;
        private ConfigEntry<KeyboardShortcut> m_DebugToggle;
        private ConfigEntry<bool> m_PromptsEnabled;
        private ConfigEntry<float> m_PromptScale;
        private ConfigEntry<BarPosition> m_HudBarPosition;
        private ConfigEntry<BarPosition> m_MenuBarPosition;
        private ConfigEntry<string> m_PromptFont;
        private ConfigEntry<float> m_CameraSpeed;
        private ConfigEntry<bool> m_FollowCamera;
        private ConfigEntry<TargetLabelMode> m_TargetLabel;
        private GameObject m_LabelTarget;
        private string m_LabelText;
        private float m_LabelSince;
        private ConfigEntry<bool> m_PauseOnDisconnect;
        private ConfigEntry<bool> m_QuickTooltips;
        private ConfigEntry<bool> m_BrightTooltipWords;
        private ConfigEntry<float> m_TooltipDelay;
        private DeviceTransitions m_Transitions;
        private Harmony m_Harmony;
        private PromptBar m_PromptBar;
        private WheelView m_WheelView;
        private RadialView m_RadialView;
        private WorldMarkersView m_WorldMarkers;
        private ActiveEffectsView m_EffectsView;
        private EffectsOverlayView m_EffectsOverlay;
        private CombatLogView m_LogView;
        private KeyboardView m_KeyboardView;
        private ItemMenuView m_ItemMenuView;
        private TutorialView m_TutorialView;
        private PortraitStripView m_PortraitStrip;
        private SettingsView m_SettingsView;
        private ConfigEntry<bool> m_TimedTutorials;
        private ConfigEntry<float> m_ReadingWpm;
        private ConfigEntry<float> m_RecallHold;
        private bool m_ShowDebug;
        private bool m_GuiFailed;

        public static Plugin Instance { get; private set; }
        public static ModLog Log { get; private set; }
        public Gamepad Pad { get; private set; }
        public InputModeTracker Mode { get; private set; }
        public GlyphLibrary Glyphs { get; private set; }
        public SchemeRouter Router { get; private set; }

        /// <summary>Read live; the router swaps schemes on the next frame after a change.</summary>
        public ControlStyle Style => m_ControlStyle.Value;

        /// <summary>Console style: list every world control in the bar (Prompts.ConsoleShortBar off).</summary>
        public bool ConsoleFullBar => !m_ConsoleShortBar.Value;

        /// <summary>Console style: the move pointer comes up by itself when a real-time fight starts (Controls.ConsoleCombatPointer).</summary>
        public bool ConsoleCombatPointer => m_ConsoleCombatPointer.Value;

        public GlyphFamily ActiveGlyphFamily
        {
            get
            {
                switch (m_GlyphOverride.Value)
                {
                    case GlyphOverride.Xbox: return GlyphFamily.Xbox;
                    case GlyphOverride.PlayStation: return GlyphFamily.PlayStation;
                    case GlyphOverride.Switch: return GlyphFamily.Switch;
                    case GlyphOverride.SteamDeck: return GlyphFamily.SteamDeck;
                    default: return Pad.DetectedFamily;
                }
            }
        }

        private void Awake()
        {
            Instance = this;
            Log = new ModLog(Logger);
            m_ControlStyle = Config.Bind("Controls", "Style", ControlStyle.Console, "Console = Switch-style controls (LT General Radial, RT Combat Radial, X pause, Y pointer, View map; menus still PC layout for now). PC layout = the mod's original controls (RT pause, Y ability wheel). Switch any time.");
            m_ControlStyle.SettingChanged += (_, __) => LogControlStyle("changed to");
            m_ConsoleHideActionBar = Config.Bind("Controls", "ConsoleHideActionBar", true, "Console style on the controller: hide the PC action bar's buttons and the ability bar (the radials replace them), like the Switch. Portraits, clock and turn-based widget stay. Mouse / keyboard brings them back.");
            m_ConsoleCombatPointer = Config.Bind("Controls", "ConsoleCombatPointer", true, "Console style, real-time combat: the move pointer comes up by itself on the nearest enemy when a fight starts and goes away when it ends, like the Switch. Y or B hides it for the rest of the fight. Turn-based combat has its own pointer.");
            LogControlStyle("is");
            m_GlyphOverride =Config.Bind("Glyphs", "Style", GlyphOverride.Auto, "Button prompt style. Auto follows the connected controller.");
            m_Deadzone = Config.Bind("Input", "StickDeadzone", 0.2f, new ConfigDescription("Radial deadzone for both sticks.", new AcceptableValueRange<float>(0.05f, 0.6f)));
            m_DebugToggle = Config.Bind("Debug", "ToggleOverlay", new KeyboardShortcut(KeyCode.F8), "Shows the controller debug overlay.");
            m_CameraSpeed = Config.Bind("Input", "CameraSpeed", 1.5f, new ConfigDescription("Right-stick camera speed, relative to the keyboard pan speed.", new AcceptableValueRange<float>(0.25f, 5f)));
            m_FollowCamera = Config.Bind("Input", "FollowCamera", false, "Controller follow camera (toggled with R3; remembered across areas and restarts).");
            m_PauseOnDisconnect = Config.Bind("Input", "PauseOnDisconnect", true, "Pause the game when the controller disconnects while you are playing with it.");
            m_QuickTooltips = Config.Bind("Menus", "QuickTooltips", true, "On the controller, show the game's hover tooltip once the cursor has rested on an element for Menus.TooltipDelay (the game's own Options delay still applies if it is shorter).");
            m_BrightTooltipWords = Config.Bind("Menus", "BrightTooltipWords", true, "Show the words that have a tooltip (character sheet, item and ability texts) in a clear blue instead of the game's dim colour. Applies the next time a text is shown.");
            m_TooltipDelay = Config.Bind("Menus", "TooltipDelay", 0.4f, new ConfigDescription("Seconds the controller cursor rests on an element before its tooltip shows.", new AcceptableValueRange<float>(0.1f, 3f)));
            m_TimedTutorials = Config.Bind("Tutorials", "Timed", true, "On the controller, tutorials the game does not pause for close by themselves after their reading time (L3 = read and pause, B = dismiss). Off = every tutorial pauses and takes the controls.");
            m_ReadingWpm = Config.Bind("Tutorials", "ReadingWordsPerMinute", 238f, new ConfigDescription("Reading speed for the timed tutorials: 2 s + words at this speed, at least 5 s. 238 = average adult silent reading (Brysbaert 2019).", new AcceptableValueRange<float>(80f, 600f)));
            m_RecallHold = Config.Bind("Tutorials", "RecallHoldSeconds", 1f, new ConfigDescription("Hold L3 this long (with no tutorial showing) to show the last tutorial again.", new AcceptableValueRange<float>(0.3f, 5f)));
            m_TargetLabel = Config.Bind("Prompts", "TargetLabel", TargetLabelMode.Full, "The A prompt over the highlighted thing in the world: Full = glyph + word (Open, Talkâ€¦), GlyphOnly = just the glyph, Brief = the word for 2 seconds when a new target is highlighted, then just the glyph.");
            // Replaces 0.23's ConsoleFullBar (default flipped; a new key so saved files pick up the new default).
            m_ConsoleShortBar = Config.Bind("Prompts", "ConsoleShortBar", true, "Console style: show only what matters right now in the world prompt bar (the A action, pause, the current mode's buttons), like the Switch. Off = list every world control.");
            m_PromptsEnabled = Config.Bind("Prompts", "Enabled", true, "Show the button prompt bar while using a controller.");
            m_PromptScale = Config.Bind("Prompts", "Scale", 1f, new ConfigDescription("Prompt bar size multiplier.", new AcceptableValueRange<float>(0.5f, 2f)));
            m_HudBarPosition = Config.Bind("Prompts", "WorldPosition", BarPosition.Top, "Where the prompt bar sits during in-world play (the bottom is taken by the game HUD).");
            m_MenuBarPosition = Config.Bind("Prompts", "MenuPosition", BarPosition.Bottom, "Where the prompt bar sits in menus.");
            // New key name so the old fragment-matching value saved in existing config files is not reused.
            m_PromptFont = Config.Bind("Prompts", "FontNames", "EspinosaNova-Regular,EBGaramond-Regular", "Comma-separated exact font names to try, in order. Available fonts are listed in the log.");

            string pluginDir = Path.GetDirectoryName(Info.Location);
            if (!InitSdl(pluginDir))
            {
                return;
            }

            Pad = new Gamepad(Log) { StickDeadzone = m_Deadzone.Value };
            m_Deadzone.SettingChanged += (_, __) => Pad.StickDeadzone = m_Deadzone.Value;
            Mode = new InputModeTracker();
            Mode.Changed += d => Log.LogInfo($"Input device -> {d}");
            Mode.Changed += _ => TextGlyphs.Refresh();
            Glyphs = new GlyphLibrary(Path.Combine(pluginDir, "glyphs"), Log);
            ScreenTracker.TopChanged += w => Log.LogInfo($"Screen -> {(w != null ? w.GetType().Name : "World")}");
            Router = new SchemeRouter();
            Router.Changed += s => Log.LogInfo($"Scheme -> {(s != null ? s.Name : "none")}");
            m_Transitions = new DeviceTransitions(this);
            m_PromptBar = new PromptBar(Glyphs, m_PromptFont.Value);
            m_WheelView = new WheelView(m_PromptBar);
            m_RadialView = new RadialView(m_PromptBar);
            // Build the radials' generated frame art now (a hitch here is hidden by the game's start-up).
            try
            {
                _ = RadialArt.Ring;
                _ = RadialArt.GoldFrame;
                _ = RadialArt.RedFrame;
                _ = RadialArt.Glow;
                _ = RadialArt.Divider;
            }
            catch (Exception e)
            {
                Log.LogWarning($"Radial art could not be built: {e.Message}");
            }
            m_WorldMarkers = new WorldMarkersView();
            m_EffectsView = new ActiveEffectsView(m_PromptBar);
            m_EffectsOverlay = new EffectsOverlayView(m_PromptBar);
            m_LogView = new CombatLogView(m_PromptBar);
            m_KeyboardView = new KeyboardView(m_PromptBar);
            m_ItemMenuView = new ItemMenuView(m_PromptBar);
            m_TutorialView = new TutorialView(m_PromptBar);
            m_PortraitStrip = new PortraitStripView(m_PromptBar);
            m_SettingsView = new SettingsView(m_PromptBar);

            CameraPan.FollowOn = m_FollowCamera.Value;
            // Changed from the settings panel (or the file): apply at once.
            m_FollowCamera.SettingChanged += (_, __) =>
            {
                if (CameraPan.FollowOn != m_FollowCamera.Value)
                {
                    CameraPan.ToggleFollow();
                }
            };
            // Check the game first: parts whose game members are missing are switched off (and not patched).
            Compat.CompatibilityCheck.Run(Log);
            m_Harmony = new Harmony(Guid);
            ApplyPatches();
            foreach (System.Reflection.MethodBase m in m_Harmony.GetPatchedMethods())
            {
                Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");
            }
            int off = 0;
            foreach (System.Collections.Generic.KeyValuePair<string, string> f in Compat.Features.OffList)
            {
                off++;
            }
            Log.LogInfo(off == 0 ? $"PoE Controller {Version} loaded, all features on"
                : $"PoE Controller {Version} loaded, {off} feature(s) off (see warnings above)");
        }

        private void LogControlStyle(string verb)
        {
            // Update as Console style grows (after the console version).
            string note = Style == ControlStyle.Console ? " (world, radials and menus Console)" : "";
            Log.LogInfo($"Control style {verb} {Style}{note}");
        }

        /// <summary>Console hold-A cone + arrows.</summary>
        private void DrawAimMarkers(ConsoleWorldScheme console)
        {
            if (console.Aiming)
            {
                if (console.TryGetAimCone(out Vector2 pivot, out float coneAngle, out float coneLength))
                {
                    m_WorldMarkers.DrawAimCone(pivot, coneAngle, coneLength);
                }
                if (console.TryGetAimRect(out Rect aim))
                {
                    m_WorldMarkers.DrawAimArrows(aim, m_PromptScale.Value);
                }
            }
        }

        private const float BriefLabelSeconds = 2f;

        /// <summary>The word next to the A glyph over the world target, per <c>Prompts.TargetLabel</c> (null = glyph only).</summary>
        private string TargetLabelToShow(InteractionTargeting targeting)
        {
            string label = targeting.Label;
            GameObject target = targeting.Target != null ? targeting.Target.Go : null;
            if (target != m_LabelTarget || label != m_LabelText)
            {
                m_LabelTarget = target;
                m_LabelText = label;
                m_LabelSince = Time.unscaledTime;
            }
            switch (m_TargetLabel.Value)
            {
                case TargetLabelMode.GlyphOnly:
                    return null;
                case TargetLabelMode.Brief:
                    return Time.unscaledTime - m_LabelSince < BriefLabelSeconds ? label : null;
                default:
                    return label;
            }
        }

        /// <summary>R3 toggled the follow camera: remember it (BepInEx saves the config file).</summary>
        public void SaveFollowCamera(bool on)
        {
            m_FollowCamera.Value = on;
        }

        /// <summary>
        /// Applies each Harmony patch class on its own (instead of PatchAll), so a patch whose game method has changed
        /// only turns off its feature (<see cref="Compat.FeatureAttribute"/> on the class or an enclosing class).
        /// </summary>
        private void ApplyPatches()
        {
            foreach (Type t in typeof(Plugin).Assembly.GetTypes())
            {
                if (!t.IsDefined(typeof(HarmonyPatch), false))
                {
                    continue;
                }
                string feature = t.Name;
                for (Type d = t; d != null; d = d.DeclaringType)
                {
                    if (d.GetCustomAttributes(typeof(Compat.FeatureAttribute), false) is Compat.FeatureAttribute[] attrs && attrs.Length > 0)
                    {
                        feature = attrs[0].Name;
                        break;
                    }
                }
                if (!Compat.Features.On(feature))
                {
                    Log.LogInfo($"Not patching {t.Name}: {feature} is off");
                    continue;
                }
                try
                {
                    m_Harmony.CreateClassProcessor(t).Patch();
                }
                catch (Exception e)
                {
                    Compat.Features.Off(feature, $"patch {t.Name} could not be applied: {e.Message}");
                }
            }
        }

        private bool InitSdl(string pluginDir)
        {
            string sdlPath = Path.Combine(pluginDir, "SDL2.dll");
            if (Sdl.LoadLibrary(sdlPath) == IntPtr.Zero)
            {
                Log.LogError($"Could not load {sdlPath} (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}). Controller support disabled.");
                enabled = false;
                return false;
            }
            // Positional face buttons: SDL 'A' is always the bottom button, even on Nintendo pads.
            Sdl.SDL_SetHint("SDL_GAMECONTROLLER_USE_BUTTON_LABELS", "0");
            Sdl.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (Sdl.SDL_Init(Sdl.INIT_GAMECONTROLLER | Sdl.INIT_JOYSTICK | Sdl.INIT_EVENTS) != 0)
            {
                Log.LogError($"SDL_Init failed: {Sdl.SDL_GetError()}. Controller support disabled.");
                enabled = false;
                return false;
            }
            return true;
        }

        private void Update()
        {
            try
            {
                float dt = Time.unscaledDeltaTime;
                Pad.Update(dt);
                m_Transitions.PauseOnDisconnect = m_PauseOnDisconnect.Value;
                Menus.TooltipAssist.Enabled = m_QuickTooltips.Value;
                Patches.LinkColorPatches.Enabled = m_BrightTooltipWords.Value && Compat.Features.On(Compat.Features.LinkColors);
                Menus.TooltipAssist.Delay = m_TooltipDelay.Value;
                Tutorials.TutorialFlow.TimedEnabled = m_TimedTutorials.Value;
                Tutorials.TutorialFlow.WordsPerMinute = m_ReadingWpm.Value;
                Tutorials.TutorialFlow.RecallHold = m_RecallHold.Value;
                m_Transitions.AfterPadUpdate();
                Mode.Update(Pad);
                CameraPan.Speed = m_CameraSpeed.Value;
                CameraPan.OnGamepad = Mode.Active == InputDevice.Gamepad;
                VirtualControls.BeginFrame();
                Router.Update(Pad, dt);
                VirtualControls.EndFrame(dt);
                m_Transitions.UpdateCursor();
                m_TrimHud = Style == ControlStyle.Console && m_ConsoleHideActionBar.Value && Mode.Active == InputDevice.Gamepad
                    && Pad.Connected && GameState.s_playerCharacter != null && !GameState.IsLoading;
                bool trimHud = m_TrimHud;
                Compat.Features.Run(Compat.Features.ConsoleHud, () => ConsoleHud.Update(trimHud));
                bool moveCounts = Style == ControlStyle.Console && Mode.Active == InputDevice.Gamepad && Router.Active is MenuScheme;
                if (Compat.Features.On(Compat.Features.SlotCount))
                {
                    Compat.Features.Run(Compat.Features.SlotCount, () => Menus.SlotCountMover.Update(moveCounts));
                }
                else
                {
                    // Switched itself off: never leave a count in the corner.
                    try { Menus.SlotCountMover.Restore(); } catch (Exception) { }
                }
                if (m_DebugToggle.Value.IsDown())
                {
                    m_ShowDebug = !m_ShowDebug;
                }
            }
            catch (Exception e)
            {
                // Never take the game down with us: log once and fall back to vanilla input.
                Log.LogError($"Controller update failed, disabling controller support: {e}");
                Mode?.Set(InputDevice.KeyboardMouse);
                VirtualControls.Reset();
                CameraPan.Input = Vector2.zero;
                try
                {
                    m_Transitions?.Shutdown();
                    ConsoleHud.Restore();
                    Menus.SlotCountMover.Restore();
                    Menus.MouseEmulator.ReleaseAll();
                }
                catch (Exception)
                {
                    // Already failing; nothing more to do.
                }
                enabled = false;
            }
        }

        private void OnGUI()
        {
            if (m_GuiFailed)
            {
                return;
            }
            try
            {
                if (Mode.Active == InputDevice.Gamepad)
                {
                    Compat.Features.Run(Compat.Features.TextGlyphs, () => TextGlyphs.Draw(Glyphs, ActiveGlyphFamily));
                }
                if (m_PromptsEnabled.Value && Mode.Active == InputDevice.Gamepad && Router.Active != null)
                {
                    m_PromptBar.Scale = m_PromptScale.Value;
                    m_PromptBar.HudPosition = m_HudBarPosition.Value;
                    m_PromptBar.MenuPosition = m_MenuBarPosition.Value;
                    ControlScheme active = Router.Active;
                    float scale = m_PromptScale.Value;
                    GlyphFamily family = ActiveGlyphFamily;
                    if (active is CompareChooseScheme choosing)
                    {
                        Compat.Features.Run(Compat.Features.Compare, choosing.DrawGreyed);
                    }
                    // Under the bar, so a focused element behind it never hides the prompts.
                    if (Router.Active is MenuScheme menu && menu.TryGetFocusRect(out Rect menuFocus))
                    {
                        m_PromptBar.DrawFocusFrame(menuFocus);
                    }
                    m_PromptBar.Draw(ActiveGlyphFamily, Router.Active.Style, Router.Prompts);
                    // Each extra draws under its own feature guard: a failure hides only that part.
                    SchemeContext context = Router.Context;
                    Compat.Features.Run(Compat.Features.Tutorials, () => m_TutorialView.Draw(family, Tutorials.TutorialFlow.CanDismissWithB(context, active), scale));
                    Compat.Features.Run(Compat.Features.ButtonGlyphs, () => ButtonHints.Draw(Glyphs, family, scale));
                    Compat.Features.Run(Compat.Features.Compare, () => m_PortraitStrip.Draw(scale));
                    Compat.Features.Run(Compat.Features.Settings, () => m_SettingsView.Draw(scale));
                    Compat.Features.Run(Compat.Features.InspectActions, () => m_ItemMenuView.Draw(Menus.PopupMenu.Menu, scale));
                    if (active is InventoryScheme inventory)
                    {
                        Compat.Features.Run(Compat.Features.ItemMenu, () => m_ItemMenuView.Draw(inventory, scale));
                    }
                    if (Router.Active is KeyboardScheme)
                    {
                        m_KeyboardView.Draw(m_PromptScale.Value);
                    }
                    else if (Router.Active is IWorldScheme world)
                    {
                        m_WheelView.Draw(world.Wheel, m_PromptScale.Value);
                        if (world is ConsoleWorldScheme console)
                        {
                            Compat.Features.Run(Compat.Features.GeneralRadial, () => m_RadialView.Draw(console.Radial, m_PromptScale.Value));
                            Compat.Features.Run(Compat.Features.CombatRadial, () => m_RadialView.Draw(console.Combat, m_PromptScale.Value));
                            Compat.Features.Run(Compat.Features.PartyRadial, () => m_RadialView.Draw(console.Party, m_PromptScale.Value));
                            Compat.Features.Run(Compat.Features.ActiveEffects, () => m_EffectsOverlay.Draw(console.EffectsOverlayVisible, console.EffectsDetails, m_PromptScale.Value));
                            Compat.Features.Run(Compat.Features.ActiveEffects, () => m_EffectsView.Draw(console.Effects, m_PromptScale.Value));
                            Compat.Features.Run(Compat.Features.CombatLog, () => m_LogView.Draw(console.Log, m_PromptScale.Value));
                            Compat.Features.Run(Compat.Features.AimMarkers, () => DrawAimMarkers(console));
                        }
                        if (!world.Wheel.IsOpen && world.Targeting.TryGetMarkerPosition(out Vector3 marker))
                        {
                            m_PromptBar.DrawWorldPrompt(ActiveGlyphFamily, marker, "south", TargetLabelToShow(world.Targeting));
                        }
                        if (world.PartySelect.TryGetFocusRect(out Rect focus))
                        {
                            m_PromptBar.DrawFocusFrame(focus);
                        }
                    }
                }
                if (m_ShowDebug)
                {
                    DebugOverlay.Draw(this);
                }
            }
            catch (Exception e)
            {
                // Drawing must never break the game; stop drawing but keep input running.
                Log.LogError($"Prompt drawing failed, hiding prompts: {e}");
                m_GuiFailed = true;
            }
        }

        /// <summary>Re-applies the Console HUD fade after the game's own Updates (some HUD code re-colours buttons every frame).</summary>
        private void LateUpdate()
        {
            if (m_TrimHud && enabled)
            {
                Compat.Features.Run(Compat.Features.ConsoleHud, () => ConsoleHud.Update(true));
            }
        }

        private void OnDestroy()
        {
            m_Transitions?.Shutdown();
            ConsoleHud.Restore();
            Menus.SlotCountMover.Restore();
            m_Harmony?.UnpatchSelf();
            Pad?.Dispose();
            Sdl.SDL_Quit();
        }
    }
}
