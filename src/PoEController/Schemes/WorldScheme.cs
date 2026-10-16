using System.Collections.Generic;
using PoEController.Input;
using PoEController.Menus;
using PoEController.World;
using PoEController.UI;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// In-world play. Sub-modes, checked in this order each frame:
    /// <list type="number">
    /// <item>Ability wheel open (Y).</item>
    /// <item>Cursor mode: the game is waiting for a target (an ability needing one), it is a party member's turn in
    /// turn-based combat, or LT is held outside turn-based combat (precision cursor). The controller drives the real
    /// cursor (<see cref="WorldCursor"/>).</item>
    /// <item>Turn-based combat, not our turn: camera and menus only.</item>
    /// <item>Party select (d-pad down).</item>
    /// <item>Exploring / real-time combat: stick movement, A-targeting, party selection, pause.</item>
    /// </list>
    /// </summary>
    public sealed class WorldScheme : BindingScheme, IWorldScheme
    {
        private static readonly Binding[] s_Bindings =
        {
            new Binding(PadButton.RightTrigger, MappedControl.PAUSE, null),
            new Binding(PadButton.West, MappedControl.STEALTH_TOGGLE, "Scout"),
        };

        // While a turn-based fight runs: no scouting / party cycling; RT ends the turn instead of pausing.
        private static readonly Binding[] s_TurnBindings =
        {
        };

        // A wheel choice that needs a target has this long to put the game into targeting mode.
        private const float CastStartTimeout = 0.75f;

        private readonly DirectMovement m_Movement = new DirectMovement();
        private readonly InteractionTargeting m_Targeting = new InteractionTargeting();
        private readonly PartySelect m_PartySelect = new PartySelect();
        private readonly AbilityWheel m_Wheel = new AbilityWheel();
        /// <summary>The wheel closed for Examine (X): reopened there once the card is closed.</summary>
        private AbilityWheel.Place m_ExamineReturn;
        /// <summary>The wheel had paused the game when Examine closed it: that pause is ours until it reopens.</summary>
        private bool m_ExaminePausedByUs;
        private readonly WorldCursor m_Cursor = new WorldCursor();
        private Vector3 m_Heading;
        private bool m_ShoulderChordUsed;
        private bool m_MenuChordUsed;

        // Targeting started from the wheel: we keep the wheel's pause until the cast is placed or cancelled.
        private bool m_CastPausedByUs;
        private bool m_CastFromWheel;
        private float m_CastStartTimer;
        private bool m_WasCasting;
        private AIController m_TurnActor;
        private AIController m_LoggedTurn;

        public WorldScheme()
            : base("World", s_Bindings)
        {
        }

        public override BarStyle Style => BarStyle.Hud;

        internal InteractionTargeting Targeting => m_Targeting;

        internal PartySelect PartySelect => m_PartySelect;

        internal AbilityWheel Wheel => m_Wheel;

        /// <summary>The controller is driving the visible world cursor (the game cursor must stay shown).</summary>
        internal bool CursorActive => m_Cursor.Active;

        private static bool OnGamepad => Plugin.Instance.Mode.Active == InputDevice.Gamepad;

        /// <summary>B does nothing right now (plain exploring: no wheel, targeting, cursor or party select).</summary>
        internal bool EastIsFree => !m_Wheel.IsOpen && !IsCasting && !m_Cursor.Active && !m_PartySelect.Active;

        InteractionTargeting IWorldScheme.Targeting => m_Targeting;
        PartySelect IWorldScheme.PartySelect => m_PartySelect;
        AbilityWheel IWorldScheme.Wheel => m_Wheel;
        bool IWorldScheme.CursorActive => m_Cursor.Active;
        bool IWorldScheme.EastIsFree => EastIsFree;

        /// <summary>
        /// The game is waiting for a target: a spell / ability being aimed, or force attack (the wheel's Attack, the
        /// HUD's sword button). Both are picked with the cursor, A confirms, B cancels.
        /// </summary>
        private static bool IsCasting => GameState.s_playerCharacter != null
            && (GameState.s_playerCharacter.IsCasting() || GameState.s_playerCharacter.IsInForceAttackMode);

        /// <summary>A spell / ability is being aimed (not force attack).</summary>
        private static bool IsSpellCasting => GameState.s_playerCharacter != null && GameState.s_playerCharacter.IsCasting();

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            if (m_ExamineReturn != null)
            {
                ReturnFromExamine();
            }
            if (m_Wheel.IsOpen)
            {
                HandleWheel(pad, dt);
                return;
            }

            HandleMenuButtons(pad);

            // R3: follow-camera toggle (ours, remembered across areas and restarts; see CameraPan).
            if (pad.GetButtonDown(PadButton.RightStick) && Compat.Features.On(Compat.Features.Camera))
            {
                bool on = CameraPan.ToggleFollow();
                Plugin.Instance.SaveFollowCamera(on);
                Plugin.Log.LogInfo($"Camera follow: {(on ? "on" : "off")}");
                if (UISystemMessager.Instance != null)
                {
                    UISystemMessager.Instance.PostMessage(on ? "Camera follow: on" : "Camera follow: off", Color.white);
                }
            }

            UpdatePendingCast(dt);
            bool casting = IsCasting;
            if (m_WasCasting && !casting && m_CastStartTimer <= 0f)
            {
                // Target placed or targeting cancelled: give back the pause the wheel took for it.
                ReleaseCastPause();
            }
            m_WasCasting = casting;
            AIController turnActor = OurTurnActor();
            bool precision = pad.GetButton(PadButton.LeftTrigger) && !TacticalModeManager.IsInTacticalCombat();
            bool wantCursor = OnGamepad && (IsCasting || turnActor != null || precision);
            if (wantCursor && !m_Cursor.Active)
            {
                EnterCursorMode(turnActor, precision && !IsCasting);
            }
            else if (!wantCursor && m_Cursor.Active)
            {
                ExitCursorMode();
            }
            else if (m_Cursor.Active && turnActor != null && turnActor != m_TurnActor && !IsCasting)
            {
                // Next party member's turn: start them on the nearest enemy.
                m_TurnActor = turnActor;
                m_Cursor.Show(WorldCursor.NearestEnemy(turnActor.gameObject) ?? turnActor.gameObject);
            }

            if (m_Cursor.Active)
            {
                HandleCursorMode(pad, dt, turnActor);
                return;
            }
            if (TacticalModeManager.IsInTacticalCombat())
            {
                HandleTurnBasedIdle(pad, dt);
                return;
            }

            if (pad.GetButtonDown(PadButton.North) && !m_PartySelect.Active && OnGamepad && m_Wheel.Open())
            {
                m_Movement.Update(Vector2.zero, dt);
                CameraPan.Input = default;
                m_Targeting.Clear();
                return;
            }
            base.HandleInput(ctx, pad, dt);
            m_Movement.Update(pad.LeftStick, dt);
            CameraPan.Input = pad.RightStick;
            HandleShoulders(pad);
            if (pad.GetButtonDown(PadButton.DpadDown))
            {
                if (m_PartySelect.Active)
                {
                    m_PartySelect.Close();
                }
                else if (OnGamepad)
                {
                    m_PartySelect.Open();
                }
            }
            if (m_PartySelect.Active)
            {
                m_Targeting.Clear();
                HandlePartySelect(pad);
            }
            else
            {
                HandleTargeting(pad, dt);
            }
        }

        // ---- Ability wheel ----

        /// <summary>
        /// Ability wheel open (game paused): LS / d-pad pick, A opens a group or uses, B backs out, Y closes, LB/RB
        /// switch character (not in turn-based combat, where the game fixes who acts). Nothing else reacts meanwhile.
        /// </summary>
        private void HandleWheel(Gamepad pad, float dt)
        {
            m_Movement.Update(Vector2.zero, dt);
            CameraPan.Input = default;
            if (!OnGamepad || pad.GetButtonDown(PadButton.North))
            {
                m_Wheel.Close();
                return;
            }
            if (!TacticalModeManager.IsInTacticalCombat())
            {
                HandleShoulders(pad);
            }
            m_Wheel.Update(dt, pad.LeftStick);
            if (!m_Wheel.IsOpen)
            {
                return;
            }
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                m_Wheel.Step(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                m_Wheel.Step(1);
            }
            if (pad.GetButtonDown(PadButton.South) && m_Wheel.Confirm())
            {
                if (m_Wheel.LastNeedsTarget)
                {
                    // The game enters targeting within a frame or two; stay paused for it.
                    m_CastPausedByUs = m_Wheel.CloseKeepPause();
                    m_CastFromWheel = true;
                    m_CastStartTimer = CastStartTimeout;
                }
                else
                {
                    m_Wheel.Close();
                }
            }
            else if (pad.GetButtonDown(PadButton.West) && m_Wheel.CanExamine)
            {
                m_ExamineReturn = m_Wheel.Examine(out m_ExaminePausedByUs);
            }
            else if (pad.GetButtonDown(PadButton.East) && m_Wheel.Back())
            {
                m_Wheel.Close();
            }
        }

        /// <summary>
        /// Back in the world after Examine's card (closed with B or the mouse): the wheel opens again on the same group
        /// and entry, keeping the pause it had. Not on the controller any more, or it can't open now: the pause it had
        /// taken is given back.
        /// </summary>
        private void ReturnFromExamine()
        {
            AbilityWheel.Place place = m_ExamineReturn;
            bool paused = m_ExaminePausedByUs;
            m_ExamineReturn = null;
            m_ExaminePausedByUs = false;
            if (OnGamepad && m_Wheel.Reopen(place, paused))
            {
                return;
            }
            if (paused && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
        }

        /// <summary>Gives a wheel choice time to start targeting; if it never does, give the pause back.</summary>
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

        // ---- Cursor mode: targeting and turn-based turns ----

        /// <summary>
        /// The party member whose turn it is in turn-based combat, if we control them. The game selects them when the
        /// turn starts (TacticalTurnEvent.OnPreStart) but skips that in some cases (e.g. not visible); then we make
        /// the same call ourselves, once per turn, so the player is never left without control.
        /// </summary>
        private AIController OurTurnActor()
        {
            if (!TacticalModeManager.IsInTacticalCombat() || TacticalModeManager.Instance == null)
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
                    : $"Turn: {who.name} party={pm != null} controllable={(pm != null && pm.IsControllablePartyMember())} selected={(pm != null && pm.Selected)} paused={(TimeController.Instance != null && TimeController.Instance.Paused)} casting={IsCasting}");
                if (pm != null && pm.IsControllablePartyMember() && !pm.Selected)
                {
                    PartyMemberAI.ExclusiveSelectPartyMember(pm, playSelectedChatter: false);
                    Plugin.Log.LogInfo($"Turn: selected {who.name} (game had not), now selected={pm.Selected}");
                }
            }
            return pm != null && pm.IsControllablePartyMember() ? who : null;
        }

        private void EnterCursorMode(AIController turnActor, bool precision)
        {
            Plugin.Log.LogInfo($"Cursor mode on ({(turnActor != null ? "turn: " + turnActor.name : precision ? "precision" : "targeting")})");
            if (m_Movement.IsMoving)
            {
                m_Movement.Stop();
            }
            // Precision cursor: start on the enemy A was aimed at, otherwise mid-screen where the hidden cursor is parked
            // (never on a party member: a click there would change the selection).
            InteractionTargeting.Candidate aimed = m_Targeting.Target;
            GameObject precisionStart = aimed != null && aimed.Kind == InteractionTargeting.Kind.Attack ? aimed.Go : null;
            m_Targeting.Clear();
            m_PartySelect.Close();
            m_TurnActor = turnActor;
            if (precision)
            {
                m_Cursor.Show(precisionStart);
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
                // Targeting finished (placed or cancelled elsewhere): resume if the wheel paused for it.
                ReleaseCastPause();
            }
            // Park the hidden cursor again so it can't edge-scroll.
            MouseEmulator.MoveTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        }

        /// <summary>
        /// LS aims, d-pad left/right jumps between characters, A clicks (the game decides: cast, attack, move,
        /// interact, select), B cancels targeting (back to the wheel if it came from there). In turn-based combat: Y wheel,
        /// RT end turn, d-pad up delay turn. Precision cursor (LT held): Y wheel, LB/RB characters, plus the world
        /// buttons (RT pause, X scout, menus).
        /// </summary>
        private void HandleCursorMode(Gamepad pad, float dt, AIController turnActor)
        {
            bool turnBased = turnActor != null;
            if (turnBased)
            {
                HoldBindings(pad, s_TurnBindings);
            }
            else
            {
                base.HandleInput(default, pad, dt);
            }
            m_Cursor.Update(pad.LeftStick, dt);
            CameraPan.Input = Vector2.ClampMagnitude(pad.RightStick + m_Cursor.CameraPush, 1f);

            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                m_Cursor.Snap(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                m_Cursor.Snap(1);
            }
            if (pad.GetButtonDown(PadButton.South))
            {
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
                    GameState.s_playerCharacter.CancelModes(cancelAbility: true);
                    m_Cursor.Hide();
                    if (!backToWheel || !m_Wheel.Open(inheritPause: paused))
                    {
                        m_CastPausedByUs = paused;
                        ReleaseCastPause();
                    }
                }
                return;
            }

            if (turnBased)
            {
                if (pad.GetButtonDown(PadButton.North))
                {
                    m_Cursor.SetClick(false);
                    m_Wheel.Open();
                    return;
                }
                if (pad.GetButton(PadButton.RightTrigger))
                {
                    VirtualControls.Hold(MappedControl.PASS_TURN);
                }
                if (pad.GetButton(PadButton.DpadUp) && CanDelayTurn(turnActor))
                {
                    VirtualControls.Hold(MappedControl.WAIT_TURN);
                }
                return;
            }

            // Precision cursor. The wheel takes over while open; a targeted pick then aims from here.
            if (pad.GetButtonDown(PadButton.North))
            {
                m_Cursor.SetClick(false);
                m_Wheel.Open();
                return;
            }
            HandleShoulders(pad);
        }

        private static bool CanDelayTurn(AIController actor)
        {
            return World.TurnChecks.CanDelayTurn(actor);
        }

        private static void HoldBindings(Gamepad pad, Binding[] bindings)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                if (pad.GetButton(bindings[i].Button))
                {
                    VirtualControls.Hold(bindings[i].Control);
                }
            }
        }

        /// <summary>Turn-based combat while someone else acts: camera, follow and menus only.</summary>
        private void HandleTurnBasedIdle(Gamepad pad, float dt)
        {
            if (m_Movement.IsMoving)
            {
                m_Movement.Stop();
            }
            m_Targeting.Clear();
            m_PartySelect.Close();
            HoldBindings(pad, s_TurnBindings);
            CameraPan.Input = pad.RightStick;
        }

        /// <summary>What a click at the cursor would do, from the game's own cursor; null when it would do nothing.</summary>
        private static string ClickLabel()
        {
            GameCursor.CursorType c = GameCursor.DesiredCursor;
            switch (c)
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
                    // Over a party member a click selects them (real-time only; turn-based fixes who acts).
                    GameObject who = GameCursor.CharacterUnderCursor;
                    PartyMemberAI pm = who != null ? who.GetComponent<PartyMemberAI>() : null;
                    return pm != null && pm.enabled && !TacticalModeManager.IsInTacticalCombat() ? "Select" : null;
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

        // ---- Exploring / real-time ----

        /// <summary>Party-select mode: d-pad left/right walks the portraits, A adds/removes, Y selects only one.</summary>
        private void HandlePartySelect(Gamepad pad)
        {
            if (!OnGamepad || pad.GetButtonDown(PadButton.East))
            {
                m_PartySelect.Close();
                return;
            }
            m_PartySelect.Update();
            if (!m_PartySelect.Active)
            {
                return;
            }
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                m_PartySelect.Move(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                m_PartySelect.Move(1);
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                m_PartySelect.Toggle();
            }
            if (pad.GetButtonDown(PadButton.North))
            {
                m_PartySelect.SelectOnly();
            }
        }

        /// <summary>A uses / attacks the highlighted target; d-pad left/right picks another one.</summary>
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
            m_Targeting.Update(dt, m_Heading, m_Movement.IsMoving);
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                m_Targeting.Cycle(-1);
            }
            if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                m_Targeting.Cycle(1);
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                m_Targeting.Activate();
            }
        }

        public override void Activate()
        {
            // Menu navigation may have left the cursor at a screen edge, which would trigger edge scrolling.
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
            m_PartySelect.Close();
            m_Wheel.Close();
            m_Cursor.Hide();
            m_TurnActor = null;
            // Leave any targeting / pause in place for the mouse; just stop owning it.
            m_CastPausedByUs = false;
            m_CastFromWheel = false;
            m_CastStartTimer = 0f;
            m_WasCasting = false;
        }

        /// <summary>
        /// LB / RB tap: previous / next character. LB+RB together: select the whole party. Taps fire on release so
        /// the chord never also cycles.
        /// </summary>
        /// <summary>
        /// View = inventory, Start = game menu, both together = controller settings. Like LB/RB, the single presses
        /// act on release so the pair can be told apart from either one.
        /// </summary>
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
                    VirtualControls.Hold(MappedControl.INVENTORY);
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

        // ---- Prompts ----

        private static void AddCameraPrompt(List<Prompt> into)
        {
            if (Compat.Features.On(Compat.Features.Camera))
            {
                into.Add(new Prompt("rstick", "Camera"));
                into.Add(new Prompt("rstick_click", CameraPan.FollowOn ? "Follow: on" : "Follow: off"));
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            if (m_Wheel.IsOpen)
            {
                into.Add(new Prompt("lstick", "Choose"));
                string confirm = m_Wheel.ConfirmLabel;
                if (confirm != null)
                {
                    into.Add(new Prompt("south", confirm));
                }
                if (m_Wheel.CanExamine)
                {
                    into.Add(new Prompt("west", "Examine"));
                }
                into.Add(new Prompt("east", m_Wheel.InGroup ? "Back" : "Close"));
                if (!TacticalModeManager.IsInTacticalCombat())
                {
                    into.Add(new Prompt(new[] { "lb", "rb" }, "Character"));
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
                into.Add(new Prompt("lstick", IsCasting ? "Aim" : "Cursor"));
                into.Add(new Prompt("dpad_lr", "Next target"));
                if (IsCasting)
                {
                    into.Add(new Prompt("east", "Cancel"));
                    AddCameraPrompt(into);
                    return;
                }
                into.Add(new Prompt("north", "Abilities"));
                if (m_TurnActor == null)
                {
                    // Precision cursor (LT held).
                    AddCameraPrompt(into);
                    into.Add(new Prompt("rt", ctx.Paused ? "Resume" : "Pause"));
                    into.Add(new Prompt(new[] { "lb", "rb" }, "Character"));
                    GameButtonHints.ForHud(GameButtonHints.Pair(MappedControl.PAUSE, "rt"));
                    return;
                }
                into.Add(new Prompt("rt", "End turn"));
                bool canDelay = CanDelayTurn(m_TurnActor);
                if (canDelay)
                {
                    into.Add(new Prompt("dpad_up", "Delay turn"));
                }
                GameButtonHints.ForHud(GameButtonHints.Pair(MappedControl.PASS_TURN, "rt"),
                    GameButtonHints.Pair(MappedControl.WAIT_TURN, canDelay ? "dpad_up" : null));
                AddCameraPrompt(into);
                return;
            }
            if (TacticalModeManager.IsInTacticalCombat())
            {
                AddCameraPrompt(into);
                into.Add(new Prompt("start", "Menu"));
                GameButtonHints.ForHud(GameButtonHints.Pair(MappedControl.OPTIONS, "start"));
                return;
            }
            if (m_PartySelect.Active)
            {
                PartyMemberAI focus = m_PartySelect.Focus;
                if (focus != null && !focus.Selected)
                {
                    into.Add(new Prompt("south", "Add"));
                }
                else if (focus != null && PartyMemberAI.NumSelectedMembers() > 1)
                {
                    into.Add(new Prompt("south", "Remove"));
                }
                into.Add(new Prompt("north", "Only this one"));
                into.Add(new Prompt("dpad_lr", "Character"));
                into.Add(new Prompt(new[] { "east", "dpad_down" }, "Done"));
                into.Add(new Prompt("lstick", "Move"));
                into.Add(new Prompt("rt", ctx.Paused ? "Resume" : "Pause"));
                GameButtonHints.ForHud(GameButtonHints.Pair(MappedControl.PAUSE, "rt"));
                return;
            }
            if (m_Targeting.Label != null)
            {
                into.Add(new Prompt("south", m_Targeting.Label));
            }
            if (m_Targeting.CandidateCount >= 2)
            {
                into.Add(new Prompt("dpad_lr", "Target"));
            }
            into.Add(new Prompt("lstick", "Move"));
            AddCameraPrompt(into);
            into.Add(new Prompt("rt", ctx.Paused ? "Resume" : "Pause"));
            into.Add(new Prompt(new[] { "lb", "rb" }, "Character (both: Party)"));
            into.Add(new Prompt("north", "Abilities"));
            into.Add(new Prompt("dpad_down", "Pick party"));
            into.Add(new Prompt("lt", "Cursor (hold)"));
            base.GetPrompts(ctx, into);
            into.Add(new Prompt("back", "Inventory"));
            into.Add(new Prompt("start", "Menu"));
            if (Compat.Features.On(Compat.Features.Settings))
            {
                into.Add(new Prompt(new[] { "back", "start" }, "Settings"));
            }
            // HUD buttons for what the exploring controls press (RT pause + the world bindings).
            GameButtonHints.ForHud(
                GameButtonHints.Pair(MappedControl.PAUSE, "rt"),
                GameButtonHints.Pair(MappedControl.STEALTH_TOGGLE, "west"),
                GameButtonHints.Pair(MappedControl.INVENTORY, "back"),
                GameButtonHints.Pair(MappedControl.OPTIONS, "start"));
        }
    }
}
