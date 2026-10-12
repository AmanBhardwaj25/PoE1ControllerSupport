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
    /// then a Move state), using only public game APIs.
    /// </summary>
    internal sealed class DirectMovement
    {
        private const float MinLookAhead = 1.5f;
        private const float MaxLookAhead = 4f;
        private const float RefreshInterval = 0.15f;
        private const float RefreshAngle = 15f;
        private const float NavMeshSnapDistance = 1.5f;
        /// <summary>The stop order sits this far ahead of the leader, only so the formation keeps the walking direction.</summary>
        private const float StopNudge = 0.05f;

        private readonly List<PartyMemberAI> m_Anchored = new List<PartyMemberAI>();
        private float m_RefreshTimer;
        private Vector3 m_LastDirection;
        private bool m_Moving;

        public bool IsMoving => m_Moving;

        /// <summary>Feed the left stick each frame while the world scheme is active.</summary>
        public void Update(Vector2 stick, float dt)
        {
            if (stick == Vector2.zero || !CanMove())
            {
                if (m_Moving)
                {
                    Stop();
                }
                return;
            }

            Vector3 direction = CameraRelative(stick);
            if (direction == Vector3.zero)
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

        /// <summary>
        /// Stick released: gather the party in formation around where the leader is now, facing the way it was
        /// walking. The game turns a formation from the leader towards the destination; ordering exactly to the
        /// leader's own position gave it no direction, and the formation snapped to face north.
        /// </summary>
        public void Stop()
        {
            m_Moving = false;
            Vector3 heading = m_LastDirection;
            m_LastDirection = Vector3.zero;
            if (!CanMove())
            {
                return;
            }
            GameObject leader = SelectedLeader();
            if (leader != null)
            {
                OrderSelectedTo(leader.transform.position + heading * StopNudge);
            }
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
