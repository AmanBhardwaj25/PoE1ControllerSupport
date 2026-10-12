using System.Collections.Generic;
using AI.Player;
using UnityEngine;
using UnityEngine.AI;

namespace PoEController.World
{
    /// <summary>
    /// Left-stick movement. While the stick is held, the selected party is ordered toward a point a few metres
    /// ahead of the leader in the stick's (camera-relative) direction; the order is refreshed as the stick turns or
    /// the leader closes in. Orders are built the same way Player's click-to-move builds them (formation position,
    /// then a Move state), using only public game APIs. Letting go stops every walking member on the spot (the HUD
    /// Stop button's per-member call), keeping the way they face. A stick springing back to the centre overshoots
    /// a little to the opposite side; a small, falling push in a very different direction is taken as letting go,
    /// not as a new direction.
    /// </summary>
    internal sealed class DirectMovement
    {
        private const float MinLookAhead = 1.5f;
        private const float MaxLookAhead = 4f;
        private const float RefreshInterval = 0.15f;
        private const float RefreshAngle = 15f;
        private const float NavMeshSnapDistance = 1.5f;
        /// <summary>From rest, the stick has to reach this far before the party starts walking (no drift starts).</summary>
        private const float StartMagnitude = 0.3f;
        /// <summary>While walking, a push below this that is falling or points far away is the stick springing back.</summary>
        private const float ReleaseMagnitude = 0.45f;
        private const float ReleaseAngle = 90f;
        /// <summary>A spring-back drops the push this fast (full to centre in a few hundredths of a second); a thumb
        /// easing off or wobbling on a light tilt is far slower and keeps walking.</summary>
        private const float FastFallPerSecond = 4f;
        /// <summary>Analog speed: up to this tilt the party walks (the game's walk speed and animation)...</summary>
        private const float WalkUpTo = 0.55f;
        /// <summary>...from here it runs at the game's normal speed; in between it speeds up gradually.</summary>
        private const float RunFrom = 0.9f;

        /// <summary>Config Input.AnalogMovementSpeed: a light tilt walks, a full push runs. Off = always the game's run.</summary>
        public static bool AnalogSpeed = true;

        private readonly List<PartyMemberAI> m_Anchored = new List<PartyMemberAI>();
        private float m_RefreshTimer;
        private Vector3 m_LastDirection;
        private bool m_Moving;
        private float m_PrevMagnitude;
        /// <summary>After a spring-back stop: ignore the stick until it rests or is pushed deliberately again.</summary>
        private bool m_WaitForRest;

        public bool IsMoving => m_Moving;

        /// <summary>Feed the left stick each frame while the world scheme is active.</summary>
        public void Update(Vector2 stick, float dt)
        {
            UpdateOrders(stick, dt);
            ApplySpeed(stick);
        }

        private void UpdateOrders(Vector2 stick, float dt)
        {
            float magnitude = stick.magnitude;
            float previous = m_PrevMagnitude;
            m_PrevMagnitude = magnitude;
            if (stick == Vector2.zero || !CanMove())
            {
                m_WaitForRest = false;
                if (m_Moving)
                {
                    Stop("stick released");
                }
                return;
            }

            Vector3 direction = CameraRelative(stick);
            if (direction == Vector3.zero)
            {
                return;
            }
            if (m_Moving && magnitude < ReleaseMagnitude
                && (previous - magnitude > FastFallPerSecond * dt || Vector3.Angle(direction, m_LastDirection) > ReleaseAngle))
            {
                // The stick springing back to the centre, not a new direction.
                Stop("stick springing back");
                m_WaitForRest = true;
                return;
            }
            if (m_WaitForRest)
            {
                if (magnitude < StartMagnitude * 2f)
                {
                    return;
                }
                m_WaitForRest = false;
            }
            if (!m_Moving && magnitude < StartMagnitude)
            {
                return;
            }

            m_RefreshTimer -= dt;
            bool turned = Vector3.Angle(direction, m_LastDirection) > RefreshAngle;
            if (m_Moving && !turned && m_RefreshTimer > 0f)
            {
                return;
            }

            GameObject leader = SelectedLeader();
            if (leader == null)
            {
                return;
            }
            float lookAhead = Mathf.Lerp(MinLookAhead, MaxLookAhead, stick.magnitude);
            if (!TryFindWalkable(leader.transform.position, direction, lookAhead, out Vector3 target))
            {
                return;
            }

            if (!m_Moving)
            {
                // Starting to move cancels ability targeting etc., as a click-to-move does.
                GameState.s_playerCharacter.CancelModes(cancelAbility: true);
            }
            OrderSelectedTo(target);
            m_Moving = true;
            m_LastDirection = direction;
            m_RefreshTimer = RefreshInterval;
        }

        /// <summary>Every frame after the orders: keeps the walking party at the stick's speed.</summary>
        private void ApplySpeed(Vector2 stick)
        {
            if (!m_Moving || !AnalogSpeed)
            {
                return;
            }
            float magnitude = stick.magnitude;
            Compat.Features.Run(Compat.Features.AnalogSpeed, () => SetSpeed(magnitude));
        }

        /// <summary>
        /// The game sets walk or run speed when a member gets a new destination (every refresh here), so the speed is
        /// set again each frame after the orders. Below <see cref="WalkUpTo"/>: the game's walk speed (its own walk
        /// animation); above <see cref="RunFrom"/>: its run speed (stats, buffs and turn-based stride included); in
        /// between a custom speed, which the game animates as a slower run. Scouting keeps the game's sneaking pace.
        /// </summary>
        private static void SetSpeed(float magnitude)
        {
            foreach (GameObject go in PartyMemberAI.SelectedPartyMembers)
            {
                if (go == null)
                {
                    continue;
                }
                PartyMemberAI ai = go.GetComponent<PartyMemberAI>();
                Mover mover = ai != null ? ai.Mover : null;
                if (mover == null || ai.StateManager == null || !(ai.StateManager.CurrentState is Move) || Stealth.IsInStealthMode(go))
                {
                    continue;
                }
                float walk = mover.GetWalkSpeed();
                float run = mover.GetRunSpeed();
                if (magnitude >= RunFrom || run <= walk)
                {
                    if (mover.DesiredSpeed != run)
                    {
                        mover.UseRunSpeed();
                    }
                }
                else if (magnitude <= WalkUpTo)
                {
                    if (mover.DesiredSpeed != walk)
                    {
                        mover.UseWalkSpeed();
                    }
                }
                else
                {
                    float speed = Mathf.Lerp(walk, run, (magnitude - WalkUpTo) / (RunFrom - WalkUpTo));
                    if (Mathf.Abs(mover.DesiredSpeed - speed) > 0.05f)
                    {
                        mover.UseCustomSpeed(speed);
                    }
                }
            }
        }

        /// <summary>Stops the walking party on the spot (other callers: modes that take over the stick).</summary>
        public void Stop() => Stop("mode change");

        /// <summary>
        /// Every selected member still walking (a Move state) stops where it is, facing the way it was going: the
        /// game's own per-member stop (<c>Player.CancelAction</c>, what the HUD Stop button runs), which also steps a
        /// member off another one's spot. Members doing something else (an attack A started) are left alone.
        /// Earlier versions re-formed the formation around the leader instead, so the followers kept walking to
        /// their new places and turned to wherever those were.
        /// </summary>
        private void Stop(string reason)
        {
            m_Moving = false;
            m_LastDirection = Vector3.zero;
            if (!CanMove() || GameState.s_playerCharacter == null)
            {
                return;
            }
            int stopped = 0;
            foreach (GameObject go in PartyMemberAI.SelectedPartyMembers)
            {
                if (go == null)
                {
                    continue;
                }
                PartyMemberAI ai = go.GetComponent<PartyMemberAI>();
                if (ai != null && ai.StateManager != null && ai.StateManager.CurrentState is Move)
                {
                    GameState.s_playerCharacter.CancelAction(go);
                    stopped++;
                }
            }
            Plugin.Log.LogInfo($"Movement stop ({reason}): {stopped} stopped on the spot");
        }

        private static bool CanMove()
        {
            return GameState.s_playerCharacter != null
                && !GameState.IsLoading
                && !Cutscene.CutsceneActive
                && !TacticalModeManager.IsInTacticalCombat()
                && UIWindowManager.MouseInputAvailable;
        }

        internal static Vector3 CameraRelative(Vector2 stick)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return Vector3.zero;
            }
            // The isometric camera's 'up' points into the screen; flatten both axes onto the ground plane.
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            Vector3 dir = right * stick.x + forward * stick.y;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
        }

        internal static GameObject SelectedLeader()
        {
            int leaderSlot = PartyMemberAI.GetSelectedLeaderSlot();
            GameObject first = null;
            foreach (GameObject go in PartyMemberAI.SelectedPartyMembers)
            {
                if (go == null)
                {
                    continue;
                }
                PartyMemberAI ai = go.GetComponent<PartyMemberAI>();
                if (ai == null)
                {
                    continue;
                }
                if (ai.Slot == leaderSlot)
                {
                    return go;
                }
                if (first == null)
                {
                    first = go;
                }
            }
            return first;
        }

        /// <summary>Walks back from the full look-ahead until a point on the nav mesh is found (handles walls).</summary>
        private static bool TryFindWalkable(Vector3 from, Vector3 direction, float distance, out Vector3 target)
        {
            for (float d = distance; d >= 0.75f; d -= 0.75f)
            {
                Vector3 probe = from + direction * d;
                if (NavMeshUtility.SamplePosition(probe, out NavMeshHit hit, NavMeshSnapDistance, null))
                {
                    target = hit.position;
                    return true;
                }
            }
            target = from;
            return false;
        }

        private void OrderSelectedTo(Vector3 target)
        {
            m_Anchored.Clear();
            foreach (GameObject go in PartyMemberAI.SelectedPartyMembers)
            {
                if (go == null)
                {
                    continue;
                }
                PartyMemberAI ai = go.GetComponent<PartyMemberAI>();
                if (ai == null || ai.StateManager == null)
                {
                    continue;
                }
                ai.DesiredRotationPosition = ai.CalculateFormationPosition(target, ignoreSelection: false, out int _);
                ai.ResolveDesiredRotationPosition(target, m_Anchored);
                m_Anchored.Add(ai);

                if (ai.StateManager.CurrentState is Move current)
                {
                    current.Destination = ai.DesiredRotationPosition;
                    current.Range = 0.05f;
                    current.ShowDestinationCircle = false;
                    if (ai.Mover != null)
                    {
                        ai.Mover.ClearAllTurnDirections();
                    }
                    ai.StateManager.ClearQueuedStates();
                    if (ai.Mover != null && ai.Mover.enabled)
                    {
                        current.OnEnter();
                    }
                }
                else
                {
                    Move move = AIStateManager.StatePool.Allocate<Move>();
                    move.Destination = ai.DesiredRotationPosition;
                    move.Range = 0.05f;
                    move.ShowDestinationCircle = false;
                    ai.StateManager.PushState(move, clearStack: true);
                }
                // No destination ring while steering: the target hops a few metres ahead every ~0.15 s, and each new
                // position restarts the ring's pop-in animation (a flicker under every character). Clearing the state
                // also hides a ring left over from an earlier click order.
                if (ai.DestinationCircleState != null)
                {
                    ai.DestinationCircleState = null;
                    ai.HideDestination();
                }
            }
        }
    }
}
