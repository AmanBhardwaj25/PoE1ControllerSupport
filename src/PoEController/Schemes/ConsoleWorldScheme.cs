using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;
using PoEController.Tutorials;
using PoEController.UI;
using PoEController.World;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// In-world play in Console style — the Switch-based base layout (after the console version). Its own class on
    /// purpose: PC layout (<see cref="WorldScheme"/>) stays exactly as in 0.21.1, this one reuses the same building
    /// blocks (movement, A-targeting, ability wheel, world cursor) with Console buttons.
    /// <list type="bullet">
    /// <item>LS move, RS camera, R3 follow, L3 (tap) scouting, LB/RB characters (both = party).</item>
    /// <item>A interact / attack the target in front, B stop the party (the HUD's Stop), X pause.</item>
    /// <item>Y move pointer on/off (the world cursor; A = click there).</item>
    /// <item>RT ability wheel — stand-in for the Combat Radial until it is built (after the console version).</item>
    /// <item>LT General Radial (<see cref="GeneralRadial"/>): screens, rest, formation, AI, level up.</item>
    /// <item>D-pad ↑/↓ zoom, →/← fast / slow mode. View area map, Menu game menu, View+Menu our settings.</item>
    /// <item>Turn-based, own turn: pointer up automatically, LB/RB next target, hold X end turn (tap only hints),
    /// RT wheel with a Delay turn slot when allowed.</item>
    /// </list>
    /// </summary>
    public sealed class ConsoleWorldScheme : BindingScheme, IWorldScheme
    {
        private static readonly Binding[] s_Bindings =
        {
            new Binding(PadButton.West, MappedControl.PAUSE, null),
        };

        private const float CastStartTimeout = 0.75f;

        /// <summary>Hold X this long to end the turn (a tap must never end it).</summary>
        private const float EndTurnHoldSeconds = 0.5f;

        /// <summary>L3 shorter than this = scouting; longer holds belong to the tutorial recall (min 0.3 s).</summary>
        private const float ScoutTapSeconds = 0.3f;

        /// <summary>Hold A this long = aim (LS) instead of acting on the target in front; a tap acts on release.</summary>
        private const float AimHoldSeconds = 0.3f;

        /// <summary>Hold LB / RB this long = the party ring; a tap switches character.</summary>
        private const float BumperHoldSeconds = 0.3f;

        private readonly DirectMovement m_Movement = new DirectMovement();
        private readonly InteractionTargeting m_Targeting = new InteractionTargeting();
        private readonly PartySelect m_PartySelect = new PartySelect();
        // Not used by Console style (RT is the Combat Radial); kept for IWorldScheme, never opened.
        private readonly AbilityWheel m_Wheel = new AbilityWheel();
        private readonly CombatRadial m_Combat = new CombatRadial();
        /// <summary>The Combat Radial closed for Examine (Y): reopened there once the card is closed.</summary>
        private CombatRadial.Place m_ExamineReturn;
        /// <summary>The radial had paused the game when Examine closed it: that pause is ours until it reopens.</summary>
        private bool m_ExaminePausedByUs;
        private readonly ActiveEffectsPanel m_Effects = new ActiveEffectsPanel();
        private readonly CombatLogPanel m_Log = new CombatLogPanel();
        private bool m_PendingLog;
        private bool m_LogStickLatched;

        /// <summary>Hold Y this long = effect icons over the portraits while held (a tap toggles the pointer).</summary>
        private const float EffectsHoldSeconds = 0.4f;
        private bool m_YDown;
        private float m_YTime;
        private bool m_YHeldLong;
        /// <summary>LB/RB while Y is held: the highlighted character's details panel instead of everyone's icons.</summary>
        private bool m_YDetails;
        /// <summary>That first bumper press only switches the view; it must not also change the character.</summary>
        private bool m_IgnoreBumperPress;
        private bool m_EffectsStickLatched;
        /// <summary>General Radial → Active effects: opened the frame after the radial has closed (pause hand-over).</summary>
        private CharacterStats m_PendingEffects;
        private readonly WorldCursor m_Cursor = new WorldCursor();
        private readonly GeneralRadial m_Radial = new GeneralRadial();
        private Vector3 m_Heading;
        private bool m_ShoulderChordUsed;
        private bool m_MenuChordUsed;

        private bool m_PointerOn;
        /// <summary>The pointer was raised by a real-time fight starting (not by Y), so the fight's end hides it again.</summary>
        private bool m_PointerAuto;
        /// <summary>The player hid the pointer during this fight: don't raise it again until the fight ends.</summary>
        private bool m_PointerDismissed;
        private bool m_WasInCombat;
        /// <summary>The next pointer start goes onto the nearest enemy (automatic raise).</summary>
        private bool m_PointerOnEnemy;
        private float m_EndTurnHeld;
        /// <summary>End turn fired; X must be released before it can fire again.</summary>
        private bool m_EndTurnFired;
        private bool m_ScoutArmed;
        private float m_ScoutHeld;

        // Hold-A aiming.
        private bool m_AArmed;
        private float m_AHeld;
        private bool m_Aiming;

        // Bumper-hold party aiming.
        private PadButton m_BumperHeld;
        private bool m_BumperArmed;
        private float m_BumperTime;
        private readonly PartyRadial m_Party = new PartyRadial();
        private bool m_PartyChordUsed;

        private bool m_CastPausedByUs;
        private bool m_CastFromWheel;
        private float m_CastStartTimer;
        private bool m_WasCasting;
        private AIController m_TurnActor;
        private AIController m_LoggedTurn;

        public ConsoleWorldScheme()
            : base("World (Console)", s_Bindings)
        {
            m_Combat.TurnActor = () => m_TurnActor;
            m_Radial.OpenEffects = stats => m_PendingEffects = stats;
            m_Radial.OpenCombatLog = () => m_PendingLog = true;
        }

        public override BarStyle Style => BarStyle.Hud;

        InteractionTargeting IWorldScheme.Targeting => m_Targeting;
        PartySelect IWorldScheme.PartySelect => m_PartySelect;
        AbilityWheel IWorldScheme.Wheel => m_Wheel;
        bool IWorldScheme.CursorActive => m_Cursor.Active;

        internal GeneralRadial Radial => m_Radial;

        internal CombatRadial Combat => m_Combat;

        internal ActiveEffectsPanel Effects => m_Effects;

        internal CombatLogPanel Log => m_Log;

        /// <summary>Hold Y: effect icons over every portrait (no pause, input carries on).</summary>
        internal bool EffectsDetails => m_YDetails;

        internal bool EffectsOverlayVisible => m_YDown && m_YHeldLong && !m_Effects.IsOpen && !m_Log.IsOpen && !m_Radial.IsOpen && !m_Combat.IsOpen && !m_Party.IsOpen;

        /// <summary>B stops the party, but a showing timed tip takes it first (TutorialFlow runs before us).</summary>
        bool IWorldScheme.EastIsFree => !m_Radial.IsOpen && !m_Combat.IsOpen && !m_Party.IsOpen && !m_Effects.IsOpen && !m_Log.IsOpen && !IsCasting && !m_Cursor.Active;

        private static bool OnGamepad => Plugin.Instance.Mode.Active == InputDevice.Gamepad;

        /// <summary>
        /// The game is waiting for a target: a spell / ability being aimed, or force attack (the Combat Radial's
        /// Attack, the HUD's sword button). Both are picked with the pointer, A confirms, B cancels.
        /// </summary>
        private static bool IsCasting => GameState.s_playerCharacter != null
            && (GameState.s_playerCharacter.IsCasting() || GameState.s_playerCharacter.IsInForceAttackMode);

        /// <summary>A spell / ability is being aimed (not force attack).</summary>
        private static bool IsSpellCasting => GameState.s_playerCharacter != null && GameState.s_playerCharacter.IsCasting();

        private static bool TurnBased => TacticalModeManager.IsInTacticalCombat();

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            if (m_ExamineReturn != null)
            {
                ReturnFromExamine();
            }
            if (m_PendingEffects != null && !m_Radial.IsOpen)
            {
                CharacterStats who = m_PendingEffects;
                m_PendingEffects = null;
                OpenEffectsCard(who);
            }
            if (m_PendingLog && !m_Radial.IsOpen)
            {
                m_PendingLog = false;
                if (m_Log.Open())
                {
                    if (m_Movement.IsMoving)
                    {
                        m_Movement.Stop();
                    }
                    m_LogStickLatched = true;
                }
            }
            if (m_Log.IsOpen)
            {
                HandleLog(pad, dt);
                return;
            }
            if (m_Radial.IsOpen)
            {
                HandleRadial(pad, dt);
                return;
            }
            if (m_Combat.IsOpen)
            {
                HandleCombatRadial(pad, dt);
                return;
            }
            if (m_Party.IsOpen)
            {
                HandlePartyRadial(pad, dt);
                return;
            }
            if (m_Effects.IsOpen)
            {
                HandleEffects(pad, dt);
                return;
            }
            // LT: General Radial (not while aiming a spell; the pointer stays as it was underneath).
            if (pad.GetButtonDown(PadButton.LeftTrigger) && OnGamepad && !IsCasting && m_Radial.Open())
            {
                m_Movement.Update(Vector2.zero, dt);
                CameraPan.Input = default;
                m_Cursor.SetClick(false);
                m_Targeting.Clear();
                return;
            }

            HandleMenuButtons(pad);
            HandleFollowToggle(pad);
            HandleZoomAndSpeed(pad);
            HandleScout(pad, dt);

            UpdatePendingCast(dt);
            bool casting = IsCasting;
            if (casting != m_WasCasting)
            {
                Plugin.Log.LogInfo(casting ? "Aiming an ability (targeting started)" : "Targeting ended");
            }
            if (m_WasCasting && !casting && m_CastStartTimer <= 0f)
            {
                ReleaseCastPause();
            }
            m_WasCasting = casting;
            AIController turnActor = OurTurnActor();

            // Y: tap = pointer on / off (real time) or Active Effects (your turn in turn-based); hold = Active Effects.
            if (HandleY(pad, dt, casting, turnActor))
            {
                return;
            }
            if (!OnGamepad || TurnBased)
            {
                m_PointerOn = false;
            }
            UpdateCombatPointer();

            bool wantCursor = OnGamepad && (casting || turnActor != null || m_PointerOn);
            if (wantCursor && !m_Cursor.Active)
            {
                EnterCursorMode(turnActor, m_PointerOn && !casting);
            }
            else if (!wantCursor && m_Cursor.Active)
            {
                ExitCursorMode();
            }
            else if (m_Cursor.Active && turnActor != null && turnActor != m_TurnActor && !casting)
            {
                m_TurnActor = turnActor;
                m_Cursor.Show(WorldCursor.NearestEnemy(turnActor.gameObject) ?? turnActor.gameObject);
            }
            if (turnActor == null)
            {
                m_EndTurnHeld = 0f;
                // Still holding X from the last end turn: wait for a release before the next turn can end.
                if (!pad.GetButton(PadButton.West))
                {
                    m_EndTurnFired = false;
                }
            }

            if (m_Cursor.Active)
            {
                HandleCursorMode(pad, dt, turnActor);
                return;
            }
            if (TurnBased)
            {
                HandleTurnBasedIdle(pad);
                return;
            }

            if (pad.GetButtonDown(PadButton.RightTrigger) && OnGamepad && m_Combat.Open())
            {
                m_Movement.Update(Vector2.zero, dt);
                CameraPan.Input = default;
                m_Targeting.Clear();
                return;
            }
            base.HandleInput(ctx, pad, dt);
            CameraPan.Input = pad.RightStick;
            if (HandleBumpers(pad, dt))
            {
                return;
            }
            // While aiming, LS points instead of walking.
            m_Movement.Update(m_Aiming ? Vector2.zero : pad.LeftStick, dt);
            if (pad.GetButtonDown(PadButton.East) && OnGamepad && TutorialFlow.EastUsedFrame != Time.frameCount)
            {
                if (m_Aiming || m_AArmed)
                {
                    EndAim("cancelled");
                }
                else
                {
                    StopParty();
                }
            }
            HandleTargeting(pad, dt);
        }

        // ---- Bumpers: tap = character, both = party, hold = party ring ----

        /// <summary>
        /// LB / RB tap: previous / next character (on release). LB+RB: whole party. Hold one for 0.3 s: the party ring
        /// opens (and stays open). Returns true when the ring just opened (skip the rest of the frame).
        /// </summary>
        private bool HandleBumpers(Gamepad pad, float dt)
        {
            bool lb = pad.GetButton(PadButton.LeftShoulder);
            bool rb = pad.GetButton(PadButton.RightShoulder);
            if (lb && rb)
            {
                if (!m_ShoulderChordUsed)
                {
                    VirtualControls.Hold(MappedControl.SELECT_ALL);
                }
                m_ShoulderChordUsed = true;
                m_BumperArmed = false;
                return false;
            }
            if (m_ShoulderChordUsed)
            {
                if (!lb && !rb)
                {
                    m_ShoulderChordUsed = false;
                }
                return false;
            }
            if (pad.GetButtonDown(PadButton.LeftShoulder) || pad.GetButtonDown(PadButton.RightShoulder))
            {
                if (m_IgnoreBumperPress)
                {
                    // This press switched the hold-Y view to details; it doesn't change the character.
                    m_IgnoreBumperPress = false;
                    m_BumperArmed = false;
                    return false;
                }
                m_BumperHeld = pad.GetButtonDown(PadButton.LeftShoulder) ? PadButton.LeftShoulder : PadButton.RightShoulder;
                m_BumperArmed = true;
                m_BumperTime = 0f;
            }
            if (!m_BumperArmed)
            {
                return false;
            }
            if (pad.GetButton(m_BumperHeld))
            {
                m_BumperTime += dt;
                if (m_BumperTime >= BumperHoldSeconds && OnGamepad)
                {
                    m_BumperArmed = false;
                    EndAim(null);
                    if (m_Party.Open())
                    {
                        if (m_Movement.IsMoving)
                        {
                            m_Movement.Stop();
                        }
                        m_Targeting.Clear();
                        m_PartyChordUsed = false;
                        // The bumper that opened it is still down: ignore it until released.
                        pad.SuppressHeldButtons();
                        return true;
                    }
                }
                return false;
            }
            if (pad.GetButtonUp(m_BumperHeld))
            {
                VirtualControls.Hold(m_BumperHeld == PadButton.LeftShoulder ? MappedControl.PREVIOUS_COMPANION : MappedControl.NEXT_COMPANION);
            }
            m_BumperArmed = false;
            return false;
        }

        // ---- Party ring (hold LB / RB) ----

        /// <summary>
        /// LS / d-pad choose, A add / remove, Y only this one, LB+RB everyone, B or a single LB / RB press close. Stays
        /// open after the bumper that opened it is released.
        /// </summary>
        private void HandlePartyRadial(Gamepad pad, float dt)
        {
            m_Movement.Update(Vector2.zero, dt);
            CameraPan.Input = pad.RightStick;
            if (!OnGamepad || pad.GetButtonDown(PadButton.East))
            {
                m_Party.Close();
                return;
            }
            bool lb = pad.GetButton(PadButton.LeftShoulder);
            bool rb = pad.GetButton(PadButton.RightShoulder);
            if (lb && rb)
            {
                if (!m_PartyChordUsed)
                {
                    VirtualControls.Hold(MappedControl.SELECT_ALL);
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                }
                m_PartyChordUsed = true;
            }
            else if (!lb && !rb)
            {
                // A single bumper press (released without the other) closes; after LB+RB, wait for both to be up.
                bool singleReleased = (pad.GetButtonUp(PadButton.LeftShoulder) || pad.GetButtonUp(PadButton.RightShoulder)) && !m_PartyChordUsed;
                m_PartyChordUsed = false;
                if (singleReleased)
                {
                    m_Party.Close();
                    return;
                }
            }
            m_Party.Update(pad.LeftStick);
            if (!m_Party.IsOpen)
            {
                return;
            }
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                m_Party.Step(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                m_Party.Step(1);
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                m_Party.Toggle();
            }
            if (pad.GetButtonDown(PadButton.North))
            {
                m_Party.SelectOnly();
            }
        }

        // ---- Buttons shared by all world modes ----

        /// <summary>View = area map, Menu = game menu, both = controller settings. Singles act on release.</summary>
        private void HandleMenuButtons(Gamepad pad)
        {
            bool back = pad.GetButton(PadButton.Back);
            bool start = pad.GetButton(PadButton.Start);
            if (back && start)
            {
                if (!m_MenuChordUsed && Compat.Features.On(Compat.Features.Settings))
                {
                    Settings.SettingsPanel.Open();
                }
                m_MenuChordUsed = true;
                return;
            }
            if (!m_MenuChordUsed)
            {
                if (pad.GetButtonUp(PadButton.Back))
                {
                    VirtualControls.Hold(MappedControl.AREAMAP);
                }
                if (pad.GetButtonUp(PadButton.Start))
                {
                    VirtualControls.Hold(MappedControl.OPTIONS);
                }
            }
            if (!back && !start)
            {
                m_MenuChordUsed = false;
            }
        }

        private static void HandleFollowToggle(Gamepad pad)
        {
            if (pad.GetButtonDown(PadButton.RightStick) && Compat.Features.On(Compat.Features.Camera))
            {
                if (CameraPan.LookingAround)
                {
                    // Follow is on but the camera was moved away: R3 brings it back (follow stays on).
                    CameraPan.Recenter();
                    Plugin.Log.LogInfo("Camera: back to the party (R3)");
                    return;
                }
                bool on = CameraPan.ToggleFollow();
                Plugin.Instance.SaveFollowCamera(on);
                Plugin.Log.LogInfo($"Camera follow: {(on ? "on" : "off")}");
                if (UISystemMessager.Instance != null)
                {
                    UISystemMessager.Instance.PostMessage(on ? "Camera follow: on" : "Camera follow: off", Color.white);
                }
            }
        }

        /// <summary>D-pad ↑/↓ zoom (repeats while held, the game's own key repeat), →/← fast / slow toggle.</summary>
        private static void HandleZoomAndSpeed(Gamepad pad)
        {
            if (pad.GetButton(PadButton.DpadUp))
            {
                VirtualControls.Hold(MappedControl.ZOOM_IN);
            }
            if (pad.GetButton(PadButton.DpadDown))
            {
                VirtualControls.Hold(MappedControl.ZOOM_OUT);
            }
            if (TurnBased)
            {
                return;
            }
            if (pad.GetButton(PadButton.DpadRight))
            {
                VirtualControls.Hold(MappedControl.FAST_TOGGLE);
            }
            if (pad.GetButton(PadButton.DpadLeft))
            {
                VirtualControls.Hold(MappedControl.SLOW_TOGGLE);
            }
        }

        /// <summary>
        /// L3 tap = scouting (the game's STEALTH_TOGGLE, which acts on release). Holding L3 is the tutorial recall
        /// and L3 on a timed tip reads it (TutorialFlow, which takes the press first), so only a short tap counts.
        /// </summary>
        private void HandleScout(Gamepad pad, float dt)
        {
            if (pad.GetButtonDown(PadButton.LeftStick))
            {
                m_ScoutArmed = true;
                m_ScoutHeld = 0f;
            }
            if (pad.GetButton(PadButton.LeftStick))
            {
                m_ScoutHeld += dt;
                return;
            }
            if (pad.GetButtonUp(PadButton.LeftStick) && m_ScoutArmed && m_ScoutHeld < ScoutTapSeconds)
            {
                VirtualControls.Hold(MappedControl.STEALTH_TOGGLE);
            }
            m_ScoutArmed = false;
        }

        private void HandleShoulders(Gamepad pad)
        {
            bool lb = pad.GetButton(PadButton.LeftShoulder);
            bool rb = pad.GetButton(PadButton.RightShoulder);
            if (lb && rb)
            {
                if (!m_ShoulderChordUsed)
                {
                    VirtualControls.Hold(MappedControl.SELECT_ALL);
                }
                m_ShoulderChordUsed = true;
                return;
            }
            if (!m_ShoulderChordUsed)
            {
                if (pad.GetButtonUp(PadButton.LeftShoulder))
                {
                    VirtualControls.Hold(MappedControl.PREVIOUS_COMPANION);
                }
                if (pad.GetButtonUp(PadButton.RightShoulder))
                {
                    VirtualControls.Hold(MappedControl.NEXT_COMPANION);
                }
            }
            if (!lb && !rb)
            {
                m_ShoulderChordUsed = false;
            }
        }

        /// <summary>B while exploring: what the HUD's Stop button does (Player.CancelAllActions).</summary>
        private static void StopParty()
        {
            Compat.Features.Run(Compat.Features.StopParty, () =>
            {
                if (GameState.s_playerCharacter != null && PartyMemberAI.NumSelectedMembers() > 0 && UIWindowManager.KeyInputAvailable)
                {
                    GameState.s_playerCharacter.CancelAllActions();
                    Plugin.Log.LogInfo("Stop (B)");
                }
            });
        }

        // ---- General Radial (LT) ----

        /// <summary>LS / d-pad choose, A confirm, X extra action, B back / close, LT close, LB/RB other character.</summary>
        private void HandleRadial(Gamepad pad, float dt)
        {
            m_Movement.Update(Vector2.zero, dt);
            CameraPan.Input = default;
            if (!OnGamepad || pad.GetButtonDown(PadButton.LeftTrigger))
            {
                m_Radial.Close();
                return;
            }
            if (!TurnBased)
            {
                HandleShoulders(pad);
            }
            m_Radial.Update(dt, pad.LeftStick);
            if (!m_Radial.IsOpen)
            {
                return;
            }
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                m_Radial.Step(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                m_Radial.Step(1);
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                if (m_Radial.Confirm())
                {
                    m_Radial.Close();
                }
            }
            else if (pad.GetButtonDown(PadButton.West))
            {
                if (m_Radial.Alternate())
                {
                    m_Radial.Close();
                }
            }
            else if (pad.GetButtonDown(PadButton.East) && m_Radial.Back())
            {
                m_Radial.Close();
            }
        }

        // ---- Active Effects (hold Y; tap Y on your turn in turn-based) ----

        /// <summary>
        /// Y: held 0.4 s = the effect icons over the portraits while it stays held (no pause, play
        /// carries on); a short tap toggles the pointer (real time). Never consumes the frame.
        /// </summary>
        private bool HandleY(Gamepad pad, float dt, bool casting, AIController turnActor)
        {
            if (casting || !OnGamepad)
            {
                m_YDown = false;
                m_YHeldLong = false;
                return false;
            }
            if (pad.GetButtonDown(PadButton.North))
            {
                m_YDown = true;
                m_YTime = 0f;
                m_YHeldLong = false;
                m_YDetails = false;
            }
            if (!m_YDown)
            {
                return false;
            }
            if (pad.GetButton(PadButton.North))
            {
                m_YTime += dt;
                if (!m_YHeldLong && m_YTime >= EffectsHoldSeconds)
                {
                    m_YHeldLong = true;
                    Plugin.Log.LogInfo("Effects over portraits (hold Y)");
                }
                if (m_YHeldLong && !m_YDetails && (pad.GetButtonDown(PadButton.LeftShoulder) || pad.GetButtonDown(PadButton.RightShoulder)))
                {
                    // First bumper while holding Y: details for the character highlighted right now.
                    m_YDetails = true;
                    m_IgnoreBumperPress = true;
                    Plugin.Log.LogInfo("Effects: details of the highlighted character");
                }
                return false;
            }
            m_YDown = false;
            m_YDetails = false;
            m_IgnoreBumperPress = false;
            if (m_YHeldLong)
            {
                m_YHeldLong = false;
                return false;
            }
            // A short tap (released before the hold time).
            if (turnActor == null && !TurnBased)
            {
                m_PointerOn = !m_PointerOn;
                // The player took over: a fight's end no longer hides it, and hiding it holds for this fight.
                m_PointerAuto = false;
                m_PointerDismissed = !m_PointerOn && GameState.InCombat;
                Plugin.Log.LogInfo($"Pointer {(m_PointerOn ? "on" : "off")}");
            }
            return false;
        }

        /// <summary>The full Active Effects card (General Radial → Active effects).</summary>
        private void OpenEffectsCard(CharacterStats stats)
        {
            if (!m_Effects.Open(stats))
            {
                return;
            }
            if (m_Movement.IsMoving)
            {
                m_Movement.Stop();
            }
            m_Cursor.SetClick(false);
            m_EffectsStickLatched = true;
        }

        /// <summary>Combat log: d-pad / LS up-down a line, LB / RB a page, X details, B close.</summary>
        private void HandleLog(Gamepad pad, float dt)
        {
            m_Movement.Update(Vector2.zero, dt);
            CameraPan.Input = default;
            if (!OnGamepad || pad.GetButtonDown(PadButton.East))
            {
                m_Log.Close();
                pad.SuppressHeldButtons();
                return;
            }
            if (pad.GetButtonDown(PadButton.West))
            {
                m_Log.ToggleDetails();
            }
            if (pad.GetButtonRepeat(PadButton.DpadUp))
            {
                m_Log.ScrollBy(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadDown))
            {
                m_Log.ScrollBy(1);
            }
            if (pad.GetButtonRepeat(PadButton.LeftShoulder))
            {
                m_Log.ScrollBy(-8);
            }
            if (pad.GetButtonRepeat(PadButton.RightShoulder))
            {
                m_Log.ScrollBy(8);
            }
            float y = pad.LeftStick.y;
            if (!m_LogStickLatched && Mathf.Abs(y) > 0.6f)
            {
                m_LogStickLatched = true;
                m_Log.ScrollBy(y > 0f ? -1 : 1);
            }
            else if (Mathf.Abs(y) < 0.3f)
            {
                m_LogStickLatched = false;
            }
        }

        /// <summary>D-pad / LS up-down scroll, LB / RB another party member, B or Y close.</summary>
        private void HandleEffects(Gamepad pad, float dt)
        {
            m_Movement.Update(Vector2.zero, dt);
            CameraPan.Input = default;
            if (!OnGamepad || pad.GetButtonDown(PadButton.East) || pad.GetButtonDown(PadButton.North))
            {
                m_Effects.Close();
                pad.SuppressHeldButtons();
                return;
            }
            m_Effects.Update(dt);
            if (!m_Effects.IsOpen)
            {
                return;
            }
            if (pad.GetButtonDown(PadButton.LeftShoulder))
            {
                m_Effects.StepOwner(-1);
            }
            if (pad.GetButtonDown(PadButton.RightShoulder))
            {
                m_Effects.StepOwner(1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadUp))
            {
                m_Effects.ScrollBy(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadDown))
            {
                m_Effects.ScrollBy(1);
            }
            float y = pad.LeftStick.y;
            if (!m_EffectsStickLatched && Mathf.Abs(y) > 0.6f)
            {
                m_EffectsStickLatched = true;
                m_Effects.ScrollBy(y > 0f ? -1 : 1);
            }
            else if (Mathf.Abs(y) < 0.3f)
            {
                m_EffectsStickLatched = false;
            }
        }

        // ---- Combat Radial (RT) ----

        /// <summary>
        /// LS / d-pad choose, A use / open, X customize, B back / close, RT close, LB/RB other character. A targeted
        /// pick hands the pause over to the world cursor (B there comes back here), as the ability wheel did.
        /// </summary>
        private void HandleCombatRadial(Gamepad pad, float dt)
        {
            m_Movement.Update(Vector2.zero, dt);
            CameraPan.Input = default;
            if (!OnGamepad || pad.GetButtonDown(PadButton.RightTrigger))
            {
                m_Combat.Close();
                return;
            }
            if (!TurnBased)
            {
                HandleShoulders(pad);
            }
            m_Combat.Update(dt, pad.LeftStick);
            if (!m_Combat.IsOpen)
            {
                return;
            }
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                m_Combat.Step(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                m_Combat.Step(1);
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                if (m_Combat.Confirm())
                {
                    if (m_Combat.LastNeedsTarget)
                    {
                        // The game enters targeting within a frame or two; stay paused for it.
                        m_CastPausedByUs = m_Combat.CloseKeepPause();
                        m_CastFromWheel = true;
                        m_CastStartTimer = CastStartTimeout;
                    }
                    else
                    {
                        m_Combat.Close();
                    }
                }
            }
            else if (pad.GetButtonDown(PadButton.West))
            {
                if (m_Combat.Alternate())
                {
                    m_Combat.Close();
                }
            }
            else if (pad.GetButtonDown(PadButton.North) && m_Combat.CanExamine)
            {
                m_ExamineReturn = m_Combat.Examine(out m_ExaminePausedByUs);
            }
            else if (pad.GetButtonDown(PadButton.East) && m_Combat.Back())
            {
                m_Combat.Close();
            }
        }

        /// <summary>
        /// Back in the world after Examine's card (closed with B or the mouse): the Combat Radial opens again on the
        /// same ring and slot, keeping the pause it had. Not on the controller any more, or it can't open now: the
        /// pause it had taken is given back.
        /// </summary>
        private void ReturnFromExamine()
        {
            CombatRadial.Place place = m_ExamineReturn;
            bool paused = m_ExaminePausedByUs;
            m_ExamineReturn = null;
            m_ExaminePausedByUs = false;
            if (OnGamepad && m_Combat.Reopen(place, paused))
            {
                return;
            }
            if (paused && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
        }

        private void UpdatePendingCast(float dt)
        {
            if (m_CastStartTimer <= 0f)
            {
                return;
            }
            if (IsCasting)
            {
                m_CastStartTimer = 0f;
                return;
            }
            m_CastStartTimer -= dt;
            if (m_CastStartTimer <= 0f)
            {
                ReleaseCastPause();
            }
        }

        private void ReleaseCastPause()
        {
            if (m_CastPausedByUs && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
            m_CastPausedByUs = false;
            m_CastFromWheel = false;
            m_CastStartTimer = 0f;
        }

        // ---- Pointer: move pointer, targeting, turn-based turns ----

        /// <summary>As <see cref="WorldScheme"/>: our controllable party member whose turn it is (selected once if the game didn't).</summary>
        private AIController OurTurnActor()
        {
            if (!TurnBased || TacticalModeManager.Instance == null)
            {
                m_LoggedTurn = null;
                return null;
            }
            AIController who = TacticalModeManager.Instance.WhoseTurn;
            PartyMemberAI pm = who as PartyMemberAI;
            if (who != m_LoggedTurn)
            {
                m_LoggedTurn = who;
                Plugin.Log.LogInfo(who == null
                    ? "Turn: none (between turns)"
                    : $"Turn: {who.name} party={pm != null} controllable={(pm != null && pm.IsControllablePartyMember())} selected={(pm != null && pm.Selected)}");
                if (pm != null && pm.IsControllablePartyMember() && !pm.Selected)
                {
                    PartyMemberAI.ExclusiveSelectPartyMember(pm, playSelectedChatter: false);
                }
            }
            return pm != null && pm.IsControllablePartyMember() ? who : null;
        }

        /// <summary>Diagnostics for A at the pointer: what the game sees there (for checking a pointer click the game refuses).</summary>
        private static void LogClick(bool canClick)
        {
            GameObject ui = GameCursor.UiObjectUnderCursor;
            GameObject who = GameCursor.CharacterUnderCursor;
            // Up to five levels of the UI object's path, enough to tell which HUD part it is.
            string uiPath = null;
            Transform t = ui != null ? ui.transform : null;
            for (int i = 0; i < 5 && t != null; i++, t = t.parent)
            {
                uiPath = uiPath == null ? t.name : t.name + "/" + uiPath;
            }
            Plugin.Log.LogInfo($"Pointer A: cursor={GameCursor.DesiredCursor} casting={IsCasting} canClick={canClick} mouseInput={UIWindowManager.MouseInputAvailable}"
                + $" ui={(uiPath ?? "none")} character={(who != null ? who.name : "none")}");
        }

        private static bool CanDelayTurn(AIController actor)
        {
            return World.TurnChecks.CanDelayTurn(actor);
        }

        /// <summary>
        /// Real-time fights (Switch #08: "in combat the move pointer is up automatically"): raise the pointer on the
        /// nearest enemy when a fight starts, hide it when the fight ends if it was raised that way. Y / B during the
        /// fight hide it until the next one. Config Controls.ConsoleCombatPointer.
        /// </summary>
        private void UpdateCombatPointer()
        {
            bool inCombat = OnGamepad && !TurnBased && GameState.InCombat;
            if (inCombat && !m_WasInCombat && !m_PointerOn && !m_PointerDismissed
                && Plugin.Instance != null && Plugin.Instance.ConsoleCombatPointer)
            {
                m_PointerOn = true;
                m_PointerAuto = true;
                m_PointerOnEnemy = true;
                Plugin.Log.LogInfo("Pointer on (combat started)");
            }
            if (!GameState.InCombat)
            {
                if (m_PointerAuto && m_PointerOn)
                {
                    m_PointerOn = false;
                    Plugin.Log.LogInfo("Pointer off (combat ended)");
                }
                m_PointerAuto = false;
                m_PointerDismissed = false;
            }
            m_WasInCombat = inCombat;
        }

        private void EnterCursorMode(AIController turnActor, bool pointer)
        {
            Plugin.Log.LogInfo($"Cursor mode on ({(turnActor != null ? "turn: " + turnActor.name : pointer ? "pointer" : "targeting")})");
            if (m_Movement.IsMoving)
            {
                m_Movement.Stop();
            }
            // Pointer: start on the enemy A was aimed at, else mid-screen (never on a party member).
            InteractionTargeting.Candidate aimed = m_Targeting.Target;
            GameObject pointerStart = aimed != null && aimed.Kind == InteractionTargeting.Kind.Attack ? aimed.Go : null;
            m_Targeting.Clear();
            m_TurnActor = turnActor;
            if (pointer)
            {
                if (m_PointerOnEnemy)
                {
                    m_PointerOnEnemy = false;
                    pointerStart = pointerStart ?? WorldCursor.NearestEnemy(DirectMovement.SelectedLeader());
                }
                m_Cursor.Show(pointerStart);
                return;
            }
            GameObject from = turnActor != null ? turnActor.gameObject : DirectMovement.SelectedLeader();
            m_Cursor.Show(WorldCursor.NearestEnemy(from) ?? from);
        }

        private void ExitCursorMode()
        {
            Plugin.Log.LogInfo("Cursor mode off");
            m_Cursor.Hide();
            m_TurnActor = null;
            if (m_CastStartTimer <= 0f)
            {
                ReleaseCastPause();
            }
            MouseEmulator.MoveTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        }

        /// <summary>
        /// LS moves the pointer, A clicks there (the game decides what that does). Targeting: LB/RB next target, B
        /// cancel (back to the wheel). Turn-based: LB/RB next target, hold X end turn, RT wheel. Move pointer (real
        /// time): Y or B hides it, X pause, RT wheel, LB/RB characters.
        /// </summary>
        private void HandleCursorMode(Gamepad pad, float dt, AIController turnActor)
        {
            bool turnBased = turnActor != null;
            if (!turnBased)
            {
                base.HandleInput(default, pad, dt);
            }
            m_Cursor.Update(pad.LeftStick, dt);
            CameraPan.Input = Vector2.ClampMagnitude(pad.RightStick + m_Cursor.CameraPush, 1f);

            if (turnBased || IsCasting)
            {
                if (pad.GetButtonRepeat(PadButton.LeftShoulder))
                {
                    m_Cursor.Snap(-1);
                }
                if (pad.GetButtonRepeat(PadButton.RightShoulder))
                {
                    m_Cursor.Snap(1);
                }
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                bool canClick = m_Cursor.CanClick;
                if (IsCasting || !canClick)
                {
                    LogClick(canClick);
                }
                if (!canClick)
                {
                    // The click would land on game UI under the pointer: say so instead of doing nothing.
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                }
                m_Cursor.SetClick(true);
            }
            else if (!pad.GetButton(PadButton.South))
            {
                m_Cursor.SetClick(false);
            }

            if (IsCasting)
            {
                if (pad.GetButtonDown(PadButton.East))
                {
                    bool backToWheel = m_CastFromWheel;
                    bool paused = m_CastPausedByUs;
                    m_CastPausedByUs = false;
                    m_CastFromWheel = false;
                    Plugin.Log.LogInfo("Targeting cancelled (B)");
                    GameState.s_playerCharacter.CancelModes(cancelAbility: true);
                    m_Cursor.Hide();
                    if (!backToWheel || !m_Combat.Open(inheritPause: paused))
                    {
                        m_CastPausedByUs = paused;
                        ReleaseCastPause();
                    }
                }
                return;
            }

            if (pad.GetButtonDown(PadButton.RightTrigger))
            {
                m_Cursor.SetClick(false);
                m_Combat.Open();
                return;
            }

            if (turnBased)
            {
                HandleEndTurn(pad, dt);
                return;
            }

            if (pad.GetButtonDown(PadButton.East))
            {
                m_PointerOn = false;
                m_PointerAuto = false;
                m_PointerDismissed = GameState.InCombat;
                Plugin.Log.LogInfo("Pointer off (B)");
                return;
            }
            HandleShoulders(pad);
        }

        /// <summary>Hold X ≥ 0.5 s = end turn (ring fills on the prompt); a shorter press only says how.</summary>
        private void HandleEndTurn(Gamepad pad, float dt)
        {
            if (pad.GetButton(PadButton.West))
            {
                if (m_EndTurnFired)
                {
                    return;
                }
                m_EndTurnHeld += dt;
                Patches.RumblePatches.EndTurnHold(m_EndTurnHeld / EndTurnHoldSeconds);
                if (m_EndTurnHeld >= EndTurnHoldSeconds)
                {
                    m_EndTurnFired = true;
                    Patches.RumblePatches.EndTurnRelease(confirmed: true);
                    m_Cursor.SetClick(false);
                    // The HUD End Turn button's hotkey: down now, up next frame = one click.
                    VirtualControls.Hold(MappedControl.PASS_TURN);
                    Plugin.Log.LogInfo("Turn: end (hold X)");
                }
                return;
            }
            if (pad.GetButtonUp(PadButton.West) && !m_EndTurnFired && UISystemMessager.Instance != null)
            {
                UISystemMessager.Instance.PostMessage("Hold to end turn", Color.white);
            }
            Patches.RumblePatches.EndTurnRelease(confirmed: false);
            m_EndTurnHeld = 0f;
            m_EndTurnFired = false;
        }

        /// <summary>Turn-based combat while someone else acts: camera, zoom and menus only.</summary>
        private void HandleTurnBasedIdle(Gamepad pad)
        {
            if (m_Movement.IsMoving)
            {
                m_Movement.Stop();
            }
            m_Targeting.Clear();
            CameraPan.Input = pad.RightStick;
        }

        /// <summary>What a click at the pointer would do, from the game's own cursor (same as PC layout).</summary>
        private static string ClickLabel()
        {
            switch (GameCursor.DesiredCursor)
            {
                case GameCursor.CursorType.CastAbility:
                case GameCursor.CursorType.CastAbilityFar:
                case GameCursor.CursorType.CastAbilityNoLOS:
                    return "Cast";
                case GameCursor.CursorType.Attack:
                case GameCursor.CursorType.SpecialAttack:
                case GameCursor.CursorType.AttackAdvantage:
                    return IsSpellCasting ? "Cast" : "Attack";
                case GameCursor.CursorType.Walk:
                    return PartyMemberAI.NumSelectedMembers() > 0 ? "Move" : null;
                case GameCursor.CursorType.Disengage:
                    return PartyMemberAI.NumSelectedMembers() > 0 ? "Disengage" : null;
                case GameCursor.CursorType.Normal:
                    GameObject who = GameCursor.CharacterUnderCursor;
                    PartyMemberAI pm = who != null ? who.GetComponent<PartyMemberAI>() : null;
                    return pm != null && pm.enabled && !TurnBased ? "Select" : null;
                case GameCursor.CursorType.OpenDoor:
                    return "Open";
                case GameCursor.CursorType.CloseDoor:
                    return "Close";
                case GameCursor.CursorType.LockedDoor:
                case GameCursor.CursorType.StealingLocked:
                    return "Pick lock";
                case GameCursor.CursorType.Talk:
                    return "Talk";
                case GameCursor.CursorType.Loot:
                    return "Loot";
                case GameCursor.CursorType.Stealing:
                    return "Steal";
                case GameCursor.CursorType.Disarm:
                    return "Disarm";
                case GameCursor.CursorType.Interact:
                case GameCursor.CursorType.Examine:
                    return "Use";
                case GameCursor.CursorType.AreaTransition:
                    return "Travel";
                default:
                    return null;
            }
        }

        // ---- Exploring ----

        /// <summary>A uses / attacks the target in front (holding A aims instead).</summary>
        private void HandleTargeting(Gamepad pad, float dt)
        {
            if (!OnGamepad)
            {
                m_Targeting.Clear();
                return;
            }
            if (pad.LeftStick != Vector2.zero)
            {
                Vector3 dir = DirectMovement.CameraRelative(pad.LeftStick);
                if (dir != Vector3.zero)
                {
                    m_Heading = dir;
                }
            }
            // A: a tap acts on release (the target in front); held for 0.3 s it becomes aiming: farther targets in the
            // stick's direction, release to act, B cancels (Switch hold-A).
            if (pad.GetButtonDown(PadButton.South))
            {
                m_AArmed = true;
                m_AHeld = 0f;
            }
            if (m_AArmed && pad.GetButton(PadButton.South))
            {
                m_AHeld += dt;
                if (!m_Aiming && m_AHeld >= AimHoldSeconds)
                {
                    m_Aiming = true;
                    m_Targeting.Aiming = true;
                    if (m_Movement.IsMoving)
                    {
                        m_Movement.Stop();
                    }
                    Plugin.Log.LogInfo("Aiming on (hold A)");
                }
            }
            m_Targeting.Update(dt, m_Heading, m_Movement.IsMoving);
            if (m_AArmed && pad.GetButtonUp(PadButton.South))
            {
                m_Targeting.Activate();
                EndAim(null);
            }
        }

        private void EndAim(string why)
        {
            if (m_Aiming && why != null)
            {
                Plugin.Log.LogInfo($"Aiming {why}");
            }
            m_AArmed = false;
            m_Aiming = false;
            m_Targeting.Aiming = false;
        }

        internal bool Aiming => m_Aiming;

        internal PartyRadial Party => m_Party;

        /// <summary>Aim range on screen for the cone (InteractionTargeting aims up to 2 × 10 m for objects).</summary>
        private const float AimConeRange = 20f;

        /// <summary>
        /// While aiming: the light cone from the selected leader towards the stick. <paramref name="pivot"/> = leader's
        /// feet in GUI coordinates, angle clockwise from screen-up, length in pixels (capped to 45 % of the height).
        /// </summary>
        internal bool TryGetAimCone(out Vector2 pivot, out float angleDeg, out float length)
        {
            pivot = default;
            angleDeg = 0f;
            length = 0f;
            Camera cam = Camera.main;
            GameObject leader = DirectMovement.SelectedLeader();
            if (!m_Aiming || cam == null || leader == null)
            {
                return false;
            }
            Vector3 heading = m_Heading.sqrMagnitude > 0.0001f ? m_Heading : leader.transform.forward;
            heading.y = 0f;
            heading.Normalize();
            Vector3 origin = leader.transform.position;
            Vector3 p0 = cam.WorldToScreenPoint(origin);
            Vector3 p1 = cam.WorldToScreenPoint(origin + heading * AimConeRange);
            if (p0.z <= 0f || p1.z <= 0f)
            {
                return false;
            }
            pivot = new Vector2(p0.x, Screen.height - p0.y);
            angleDeg = Mathf.Atan2(p1.x - p0.x, p1.y - p0.y) * Mathf.Rad2Deg;
            length = Mathf.Min(Vector2.Distance(p0, p1), Screen.height * 0.45f);
            return true;
        }

        /// <summary>
        /// While aiming (hold A): the target's screen rect (GUI coordinates) for the four arrows.
        /// </summary>
        internal bool TryGetAimRect(out Rect rect)
        {
            rect = default;
            Camera cam = Camera.main;
            if (cam == null)
            {
                return false;
            }
            InteractionTargeting.Candidate t = m_Aiming ? m_Targeting.Target : null;
            GameObject go = t != null ? t.Go : null;
            bool character = t != null && (t.Kind == InteractionTargeting.Kind.Attack || t.Kind == InteractionTargeting.Kind.Talk);
            Vector3 anchor = t != null ? t.Anchor : default;
            if (go == null)
            {
                return false;
            }
            Vector3 centre = character ? go.transform.position + Vector3.up * 1f : anchor;
            Vector3 sp = cam.WorldToScreenPoint(centre);
            if (sp.z <= 0f)
            {
                return false;
            }
            float size = Screen.height * (character ? 0.14f : 0.09f);
            rect = new Rect(sp.x - size * 0.5f, Screen.height - sp.y - size * 0.5f, size, size);
            return true;
        }

        public override void Activate()
        {
            // Back from a menu mid-fight: the pointer comes up again (unless the player hid it during this fight).
            m_WasInCombat = false;
            if (OnGamepad)
            {
                MouseEmulator.MoveTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            }
        }

        public override void Deactivate()
        {
            if (m_Movement.IsMoving)
            {
                m_Movement.Stop();
            }
            CameraPan.Input = default;
            m_ShoulderChordUsed = false;
            m_Targeting.Clear();
            m_Radial.Close();
            m_Combat.Close();
            m_Effects.Close();
            m_Log.Close();
            m_PendingLog = false;
            m_YDown = false;
            m_YHeldLong = false;
            m_YDetails = false;
            m_IgnoreBumperPress = false;
            m_PendingEffects = null;
            m_Cursor.Hide();
            m_TurnActor = null;
            m_PointerOn = false;
            m_EndTurnHeld = 0f;
            m_EndTurnFired = false;
            m_ScoutArmed = false;
            EndAim(null);
            m_Party.Close();
            m_BumperArmed = false;
            m_CastPausedByUs = false;
            m_CastFromWheel = false;
            m_CastStartTimer = 0f;
            m_WasCasting = false;
        }

        // ---- Prompts ----

        private static void AddCameraPrompt(List<Prompt> into)
        {
            if (Compat.Features.On(Compat.Features.Camera))
            {
                into.Add(new Prompt("rstick", "Camera"));
                into.Add(new Prompt("rstick_click", CameraPan.LookingAround ? "Back to party" : CameraPan.FollowOn ? "Follow: on" : "Follow: off"));
            }
        }

        /// <summary>
        /// Short bar (default, config Prompts.ConsoleShortBar) keeps only what matters right now, like the Switch: the
        /// A action, pause state, the current mode's own buttons. Off = every world control is listed.
        /// </summary>
        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            bool full = Plugin.Instance.ConsoleFullBar;
            if (EffectsOverlayVisible)
            {
                into.Add(new Prompt(new[] { "lb", "rb" }, m_YDetails ? "Other character" : "Details"));
            }
            if (m_Log.IsOpen)
            {
                into.Add(new Prompt(new[] { "dpad_ud", "lstick" }, "Scroll"));
                into.Add(new Prompt(new[] { "lb", "rb" }, "Page"));
                into.Add(new Prompt("west", m_Log.Details ? "Short text" : "Details"));
                into.Add(new Prompt("east", "Close"));
                return;
            }
            if (m_Effects.IsOpen)
            {
                if (m_Effects.Rows.Count > 1)
                {
                    into.Add(new Prompt(new[] { "dpad_ud", "lstick" }, "Scroll"));
                }
                into.Add(new Prompt(new[] { "lb", "rb" }, "Character"));
                into.Add(new Prompt(new[] { "east", "north" }, "Close"));
                return;
            }
            if (m_Party.IsOpen)
            {
                into.Add(new Prompt("lstick", "Navigate"));
                string toggle = m_Party.ToggleLabel;
                if (toggle != null)
                {
                    into.Add(new Prompt("south", toggle));
                }
                into.Add(new Prompt("north", "Only this one"));
                into.Add(new Prompt(new[] { "lb", "rb" }, "Everyone (both)"));
                into.Add(new Prompt("east", "Done"));
                return;
            }
            if (m_Radial.IsOpen)
            {
                GeneralRadial.Entry cur = m_Radial.Current;
                into.Add(new Prompt("lstick", "Navigate"));
                if (cur != null && cur.Usable)
                {
                    into.Add(new Prompt("south", "Confirm"));
                }
                if (cur != null && cur.Alt != null)
                {
                    into.Add(new Prompt("west", cur.AltLabel));
                }
                into.Add(new Prompt("east", m_Radial.InSubRing ? "Back" : "Close"));
                if (!TurnBased)
                {
                    into.Add(new Prompt(new[] { "lb", "rb" }, "Switch character"));
                }
                return;
            }
            if (m_Combat.IsOpen)
            {
                into.Add(new Prompt("lstick", "Navigate"));
                string confirm = m_Combat.ConfirmLabel;
                if (confirm != null)
                {
                    into.Add(new Prompt("south", confirm));
                }
                string alt = m_Combat.AltLabel;
                if (alt != null)
                {
                    into.Add(new Prompt("west", alt));
                }
                if (m_Combat.CanExamine)
                {
                    into.Add(new Prompt("north", "Examine"));
                }
                into.Add(new Prompt("east", m_Combat.InSubRing ? "Back" : "Close"));
                if (!TurnBased)
                {
                    into.Add(new Prompt(new[] { "lb", "rb" }, "Switch character"));
                }
                return;
            }
            if (m_Cursor.Active)
            {
                string click = m_Cursor.CanClick ? ClickLabel() : null;
                if (click != null)
                {
                    into.Add(new Prompt("south", click));
                }
                into.Add(new Prompt("lstick", IsCasting ? "Aim" : "Pointer"));
                if (IsCasting)
                {
                    into.Add(new Prompt(new[] { "lb", "rb" }, "Next target"));
                    into.Add(new Prompt("east", "Cancel"));
                    if (full)
                    {
                        AddCameraPrompt(into);
                    }
                    return;
                }
                if (m_TurnActor != null)
                {
                    float progress = m_EndTurnFired ? 1f : Mathf.Clamp01(m_EndTurnHeld / EndTurnHoldSeconds);
                    into.Add(new Prompt("west", "End turn (hold)", progress));
                    into.Add(new Prompt("north", "Effects (hold)"));
                    into.Add(new Prompt(new[] { "lb", "rb" }, "Next target"));
                    into.Add(new Prompt("rt", CanDelayTurn(m_TurnActor) ? "Combat radial (Delay turn)" : "Combat radial"));
                    if (full)
                    {
                        into.Add(new Prompt("dpad_ud", "Zoom"));
                        AddCameraPrompt(into);
                    }
                    GameButtonHints.ForHud(GameButtonHints.Pair(MappedControl.PASS_TURN, "west"));
                    return;
                }
                // Move pointer (real time).
                into.Add(new Prompt(new[] { "north", "east" }, "Hide pointer"));
                into.Add(new Prompt("west", ctx.Paused ? "Resume" : "Pause"));
                into.Add(new Prompt("rt", "Combat radial"));
                if (full)
                {
                    into.Add(new Prompt(new[] { "lb", "rb" }, "Character"));
                    AddCameraPrompt(into);
                }
                GameButtonHints.ForHud(GameButtonHints.Pair(MappedControl.PAUSE, "west"));
                return;
            }
            if (TurnBased)
            {
                if (full)
                {
                    AddCameraPrompt(into);
                    into.Add(new Prompt("dpad_ud", "Zoom"));
                }
                into.Add(new Prompt("start", "Menu"));
                if (!ConsoleHud.Hidden)
                {
                    GameButtonHints.ForHud(GameButtonHints.Pair(MappedControl.OPTIONS, "start"));
                }
                return;
            }

            if (m_Aiming)
            {
                into.Add(new Prompt("lstick", "Target"));
                into.Add(new Prompt("south", m_Targeting.Label != null ? "Release: " + m_Targeting.Label : "Release"));
                into.Add(new Prompt("east", "Cancel"));
                return;
            }

            // Exploring / real-time combat.
            if (!full && CameraPan.LookingAround && Compat.Features.On(Compat.Features.Camera))
            {
                into.Add(new Prompt("rstick_click", "Back to party"));
            }
            if (m_Targeting.Label != null)
            {
                into.Add(new Prompt("south", m_Targeting.Label));
            }
            if (full || ctx.Paused || ctx.InCombat)
            {
                into.Add(new Prompt("west", ctx.Paused ? "Resume" : "Pause"));
            }
            if (full || ctx.InCombat)
            {
                into.Add(new Prompt("east", "Stop"));
            }
            if (full)
            {
                into.Add(new Prompt("lt", "General radial"));
                into.Add(new Prompt("north", "Pointer (hold: effects)"));
                into.Add(new Prompt("rt", "Combat radial"));
                into.Add(new Prompt("lstick", "Move"));
                into.Add(new Prompt("lstick_click", "Scout"));
                AddCameraPrompt(into);
                into.Add(new Prompt(new[] { "lb", "rb" }, "Character (both: Party)"));
                into.Add(new Prompt("dpad_ud", "Zoom"));
                into.Add(new Prompt("dpad_lr", "Slow / Fast"));
                into.Add(new Prompt("back", "Map"));
                into.Add(new Prompt("start", "Game menu"));
                if (Compat.Features.On(Compat.Features.Settings))
                {
                    into.Add(new Prompt(new[] { "back", "start" }, "Settings"));
                }
            }
            if (ConsoleHud.Hidden)
            {
                // Only the clock / pause dial is still shown.
                GameButtonHints.ForHud(GameButtonHints.Pair(MappedControl.PAUSE, "west"));
                return;
            }
            GameButtonHints.ForHud(
                GameButtonHints.Pair(MappedControl.PAUSE, "west"),
                GameButtonHints.Pair(MappedControl.STEALTH_TOGGLE, "lstick_click"),
                GameButtonHints.Pair(MappedControl.CANCEL_ACTION, "east"),
                GameButtonHints.Pair(MappedControl.AREAMAP, "back"),
                GameButtonHints.Pair(MappedControl.OPTIONS, "start"));
        }
    }
}
