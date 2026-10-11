using System.Collections.Generic;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Picks the best thing to interact with or attack near the selected leader, highlights it, and performs the
    /// action on A. Every action goes through the same entry point a mouse click on that object reaches:
    /// <list type="bullet">
    /// <item>Doors, containers, NPCs, rest spots, loot piles, animated props: <c>Player.ObjectClicked(usable)</c>
    /// (what their own OnMouseDown / Update click handlers call).</item>
    /// <item>Objects clicked through a 2D outline (scripted interactions, switches, traps, examinables, many
    /// containers): <c>Player.ObjectClicked(collider.FirstUsable)</c>, gated by <c>PE_Collider2D.CanUse</c>, exactly
    /// as <c>PE_Collider2D.Update</c> does.</item>
    /// <item>Area exits: <c>SceneTransition.UseTransition</c>, the callback of the exit's on-screen icon (it does the
    /// party-gathering / in-combat checks itself).</item>
    /// <item>Enemies: the same <c>AI.Player.Attack</c> order <c>Player.IssueCommands</c> gives on a click on a hostile
    /// (that method is private and reads the mouse, so the order is built the same way here).</item>
    /// </list>
    /// We never press the game's INTERACT / ATTACK / SELECT actions: those act on whatever the (parked) OS cursor
    /// happens to be over.
    /// </summary>
    internal sealed class InteractionTargeting
    {
        public enum Kind
        {
            Attack,
            Talk,
            Door,
            Container,
            Transition,
            Collider,
            Generic,
        }

        public sealed class Candidate
        {
            public GameObject Go;
            public Kind Kind;
            public Usable Usable;
            public PE_Collider2D Collider;
            public Faction Faction;
            public Health Health;
            public Vector3 Anchor;
            public float Score;
            public float ScreenX;
        }

        private const float UsableRange = 10f;
        private const float EnemyRange = 16f;
        private const float RescanInterval = 1f;
        private const float EvaluateInterval = 0.1f;
        private const float SwitchFactor = 0.6f;
        /// <summary>Aiming (Console hold-A): ranges × this, only things within the cone count.</summary>
        private const float AimRangeFactor = 2f;
        private const float AimCone = 40f;
        private const float AimEvaluateInterval = 0.05f;
        private const float AimSwitchFactor = 0.85f;

        private readonly List<Usable> m_Usables = new List<Usable>();
        private readonly List<Candidate> m_Candidates = new List<Candidate>();
        private readonly List<Candidate> m_Pool = new List<Candidate>();
        private readonly HashSet<GameObject> m_Seen = new HashSet<GameObject>();
        private float m_RescanTimer;
        private float m_EvaluateTimer;
        private Candidate m_Target;
        private GameObject m_TargetGo;
        private HighlightCharacter m_Highlighted;
        private bool m_ManualLock;
        private string m_Label;

        /// <summary>
        /// Console style's hold-A aiming: twice the range, and the target is whatever lies closest to the heading
        /// (the stick) inside a 40° cone, instead of the nearest thing in front. Off by default (PC layout never sets it).
        /// </summary>
        public bool Aiming { get; set; }

        public Candidate Target => m_Target;
        public string Label => m_Label;
        public int CandidateCount => m_Candidates.Count;

        /// <summary>Where to draw the in-world prompt: above a character's head, or just above an object.</summary>
        public bool TryGetMarkerPosition(out Vector3 world)
        {
            Candidate t = m_Target;
            if (t == null || m_Label == null || t.Go == null)
            {
                world = default;
                return false;
            }
            bool character = t.Kind == Kind.Attack || t.Kind == Kind.Talk;
            world = (character ? t.Go.transform.position : t.Anchor) + Vector3.up * (character ? 2.3f : 0.8f);
            return true;
        }

        /// <summary>Call every frame the world scheme runs with a controller in use.</summary>
        public void Update(float dt, Vector3 heading, bool partyMoving)
        {
            GameObject leader = DirectMovement.SelectedLeader();
            if (!CanTarget() || leader == null)
            {
                Clear();
                return;
            }
            if (partyMoving)
            {
                m_ManualLock = false;
            }

            m_RescanTimer -= dt;
            if (m_RescanTimer <= 0f)
            {
                m_RescanTimer = RescanInterval;
                m_Usables.Clear();
                m_Usables.AddRange(Object.FindObjectsOfType<Usable>());
            }

            m_EvaluateTimer -= dt;
            if (m_EvaluateTimer <= 0f || (m_Target != null && !IsStillValid(m_Target)))
            {
                m_EvaluateTimer = Aiming ? AimEvaluateInterval : EvaluateInterval;
                Evaluate(leader, heading);
            }
            m_Label = m_Target != null ? LabelFor(m_Target) : null;
            if (m_Target != null && m_Label == null)
            {
                SetTarget(null);
            }
        }

        /// <summary>D-pad left/right: step to the next candidate on screen in that direction (wraps around).</summary>
        public void Cycle(int direction)
        {
            if (m_Candidates.Count < 2)
            {
                return;
            }
            float currentX = m_Target != null ? m_Target.ScreenX : (direction > 0 ? float.MinValue : float.MaxValue);
            Candidate best = null;
            Candidate wrap = null;
            foreach (Candidate c in m_Candidates)
            {
                if (c == m_Target)
                {
                    continue;
                }
                float dx = (c.ScreenX - currentX) * direction;
                if (dx > 0f && (best == null || dx < (best.ScreenX - currentX) * direction))
                {
                    best = c;
                }
                // For wrapping: the candidate farthest the other way.
                if (wrap == null || (c.ScreenX - wrap.ScreenX) * direction < 0f)
                {
                    wrap = c;
                }
            }
            Candidate next = best ?? wrap;
            if (next != null)
            {
                SetTarget(next);
                m_ManualLock = true;
                m_Label = LabelFor(next);
            }
        }

        /// <summary>A: perform the target's action. Returns true if something was ordered.</summary>
        public bool Activate()
        {
            Candidate t = m_Target;
            if (t == null || !IsStillValid(t) || GameState.s_playerCharacter == null)
            {
                return false;
            }
            Player player = GameState.s_playerCharacter;
            switch (t.Kind)
            {
                case Kind.Attack:
                    return OrderAttack(t.Go);
                case Kind.Transition:
                    ((SceneTransition)t.Usable).UseTransition(t.Go);
                    return true;
                case Kind.Collider:
                    Usable first = t.Collider.FirstUsable;
                    if (first == null)
                    {
                        return false;
                    }
                    player.ObjectClicked(first);
                    return true;
                default:
                    player.ObjectClicked(t.Usable);
                    return true;
            }
        }

        public void Clear()
        {
            SetTarget(null);
            m_Candidates.Clear();
            m_ManualLock = false;
            m_Label = null;
        }

        private static bool CanTarget()
        {
            Player player = GameState.s_playerCharacter;
            return player != null
                && !GameState.IsLoading
                && !Cutscene.CutsceneActive
                && !GameState.PartyDead
                && !TacticalModeManager.IsInTacticalCombat()
                && !player.IsActionCursor()
                && UIWindowManager.MouseInputAvailable
                && Camera.main != null
                && PartyMemberAI.GetSelectedPartyMembers().Count > 0;
        }

        private void Evaluate(GameObject leader, Vector3 heading)
        {
            Camera cam = Camera.main;
            Vector3 origin = leader.transform.position;
            if (heading.sqrMagnitude < 0.0001f)
            {
                heading = leader.transform.forward;
            }
            heading.y = 0f;
            heading.Normalize();

            int pooled = 0;
            m_Candidates.Clear();
            m_Seen.Clear();
            bool primarySelected = PartyMemberAI.IsPrimaryPartyMemberSelected();

            // Enemies first so a hostile NPC with a dialogue component is an attack target, not a talk target.
            List<Faction> factions = Faction.ActiveFactionComponents;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null || !IsAttackable(f, leader))
                {
                    continue;
                }
                Candidate c = Take(ref pooled);
                c.Go = f.gameObject;
                c.Kind = Kind.Attack;
                c.Usable = null;
                c.Collider = null;
                c.Faction = f;
                c.Health = f.GetComponent<Health>();
                c.Anchor = f.transform.position;
                if (Consider(c, cam, origin, heading, EnemyRange * (Aiming ? AimRangeFactor : 1f)))
                {
                    m_Seen.Add(c.Go);
                }
                else
                {
                    pooled--;
                }
            }

            for (int i = 0; i < m_Usables.Count; i++)
            {
                Usable u = m_Usables[i];
                if (u == null || !u.isActiveAndEnabled || m_Seen.Contains(u.gameObject))
                {
                    continue;
                }
                Candidate c = Take(ref pooled);
                if (!Classify(u, primarySelected, c) || LabelFor(c) == null || !Consider(c, cam, origin, heading, UsableRange * (Aiming ? AimRangeFactor : 1f)))
                {
                    pooled--;
                    continue;
                }
                m_Seen.Add(c.Go);
            }

            Candidate best = null;
            foreach (Candidate c in m_Candidates)
            {
                if (best == null || c.Score < best.Score)
                {
                    best = c;
                }
            }

            // Keep the current target (found again by GameObject, since candidates are rebuilt) unless it is gone or
            // something clearly better turned up; a manual pick sticks until the party moves.
            Candidate current = null;
            if (m_TargetGo != null)
            {
                foreach (Candidate c in m_Candidates)
                {
                    if (c.Go == m_TargetGo)
                    {
                        current = c;
                        break;
                    }
                }
            }
            if (current != null && (m_ManualLock || best == null || best.Score >= current.Score * (Aiming ? AimSwitchFactor : SwitchFactor)))
            {
                SetTarget(current);
            }
            else
            {
                m_ManualLock = false;
                SetTarget(best);
            }
        }

        private Candidate Take(ref int pooled)
        {
            if (pooled == m_Pool.Count)
            {
                m_Pool.Add(new Candidate());
            }
            return m_Pool[pooled++];
        }

        /// <summary>Distance/heading score (lower is better) and on-screen check. Adds the candidate if usable.</summary>
        private bool Consider(Candidate c, Camera cam, Vector3 origin, Vector3 heading, float range)
        {
            Vector3 to = c.Anchor - origin;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > range)
            {
                return false;
            }
            Vector3 vp = cam.WorldToViewportPoint(c.Anchor);
            if (vp.z <= 0f || vp.x < 0.02f || vp.x > 0.98f || vp.y < 0.04f || vp.y > 0.96f)
            {
                return false;
            }
            float angle = dist > 0.01f ? Vector3.Angle(heading, to / dist) : 0f;
            float score;
            if (Aiming)
            {
                // Aiming: direction first, distance only breaks near-ties.
                if (angle > AimCone)
                {
                    return false;
                }
                score = angle / AimCone + dist / range * 0.35f;
            }
            else
            {
                // Things in front win over things behind at the same distance; very close things win regardless.
                score = (dist + 1f) * (1f + angle / 90f);
            }
            if (c.Kind == Kind.Attack && GameState.InCombat && !Aiming)
            {
                score *= 0.5f;
            }
            c.Score = score;
            c.ScreenX = vp.x;
            m_Candidates.Add(c);
            return true;
        }

        private static bool IsAttackable(Faction f, GameObject leader)
        {
            if (!f.isActiveAndEnabled || !f.DrawSelectionCircle || !f.CanBeTargeted || !f.IsHostile(leader))
            {
                return false;
            }
            if (f.GetComponent<Trap>() != null || f.GetComponent<PartyMemberAI>() != null)
            {
                return false;
            }
            Health h = f.GetComponent<Health>();
            if (h == null || h.ShowDead || h.Dead || h.Unconscious || h.CurrentHealth <= 0f)
            {
                return false;
            }
            AIController ai = f.GetComponent<AIController>();
            if (ai != null && ai.IsInvisible)
            {
                return false;
            }
            return FogOfWar.Instance == null || FogOfWar.Instance.PointVisible(f.transform.position);
        }

        /// <summary>
        /// Mirrors which objects respond to a click (and what the cursor shows over them) in vanilla. Returns false for
        /// anything a click would not act on.
        /// </summary>
        private static bool Classify(Usable u, bool primarySelected, Candidate c)
        {
            GameObject go = u.gameObject;
            c.Go = go;
            c.Usable = u;
            c.Faction = null;
            c.Health = null;
            c.Collider = go.GetComponent<PE_Collider2D>();

            if (u is SceneTransition st)
            {
                if (!st.IsUsable || GameState.InCombat)
                {
                    return false;
                }
                c.Kind = Kind.Transition;
                c.Collider = null;
                c.Anchor = go.transform.position;
                return true;
            }

            // Outline-clicked objects: PE_Collider2D.Update calls ObjectClicked(FirstUsable) when CanUse (which also
            // hides undetected stashes and traps).
            if (c.Collider != null)
            {
                if (!c.Collider.isActiveAndEnabled || !c.Collider.CanUse || !primarySelected)
                {
                    return false;
                }
                if (FogOfWar.Instance != null && !FogOfWar.PointVisibleInFog(c.Collider.Center))
                {
                    return false;
                }
                Usable first = c.Collider.FirstUsable;
                if (first is Container && GameState.InCombat)
                {
                    return false;
                }
                c.Kind = Kind.Collider;
                c.Usable = first;
                c.Anchor = c.Collider.Center;
                return true;
            }

            if (!u.IsUsable)
            {
                return false;
            }
            Detectable detectable = go.GetComponent<Detectable>();
            if (detectable != null && !detectable.Detected)
            {
                return false;
            }
            c.Anchor = u.GetClosestInteractionPoint(go.transform.position);

            if (u is NPCDialogue npc)
            {
                PartyMemberAI pm = go.GetComponent<PartyMemberAI>();
                if (!primarySelected || (pm != null && pm.IsInSlot) || npc.ConversationFile == null || npc.ConversationFile.Filename == null)
                {
                    return false;
                }
                Faction f = go.GetComponent<Faction>();
                if (f != null && f.RelationshipToPlayer == Faction.Relationship.Hostile)
                {
                    return false;
                }
                AIController ai = go.GetComponent<AIController>();
                if (ai != null && ai.IsFactionSwapped())
                {
                    return false;
                }
                Health h = go.GetComponent<Health>();
                if (h != null && (h.Dead || h.Unconscious))
                {
                    return false;
                }
                c.Kind = Kind.Talk;
                c.Anchor = go.transform.position;
                return true;
            }
            if (u is Door)
            {
                c.Kind = Kind.Door;
                c.Anchor = go.transform.position;
                return true;
            }
            if (u is Container)
            {
                // Container.IsUsable already excludes combat and empty bodies.
                if (!primarySelected)
                {
                    return false;
                }
                c.Kind = Kind.Container;
                return true;
            }
            if (u is AutoLootContainer || u is RestInteraction)
            {
                if (!primarySelected || GameState.InCombat)
                {
                    return false;
                }
                c.Kind = Kind.Generic;
                return true;
            }
            if (u is Animate)
            {
                if (!primarySelected)
                {
                    return false;
                }
                c.Kind = Kind.Generic;
                return true;
            }
            // Other usables have no click handler of their own outside a PE_Collider2D.
            return false;
        }

        /// <summary>Cheap per-frame check that the current target still makes sense.</summary>
        private static bool IsStillValid(Candidate c)
        {
            if (c.Go == null || !c.Go.activeInHierarchy)
            {
                return false;
            }
            switch (c.Kind)
            {
                case Kind.Attack:
                    return c.Health != null && !c.Health.Dead && !c.Health.Unconscious && !c.Health.ShowDead;
                case Kind.Collider:
                    return c.Collider != null && c.Collider.CanUse;
                default:
                    return c.Usable != null && c.Usable.IsUsable;
            }
        }

        /// <summary>Prompt text, matching the cursor vanilla shows over the object. Null = nothing to do.</summary>
        private static string LabelFor(Candidate c)
        {
            switch (c.Kind)
            {
                case Kind.Attack:
                    return "Attack";
                case Kind.Talk:
                    return "Talk";
                case Kind.Transition:
                    return "Travel";
                case Kind.Door:
                    return DoorLabel((Door)c.Usable);
                case Kind.Container:
                    return ContainerLabel((Container)c.Usable);
                case Kind.Collider:
                    if (c.Usable is Trap trap)
                    {
                        return trap.CanDisarm ? "Disarm" : null;
                    }
                    if (c.Usable is Container container)
                    {
                        return ContainerLabel(container);
                    }
                    if (c.Usable is Door door)
                    {
                        return DoorLabel(door);
                    }
                    if (c.Usable is Examinable)
                    {
                        return "Examine";
                    }
                    return "Use";
                default:
                    if (c.Usable is AutoLootContainer)
                    {
                        return "Pick up";
                    }
                    if (c.Usable is RestInteraction)
                    {
                        return "Rest";
                    }
                    return "Use";
            }
        }

        private static string DoorLabel(Door door)
        {
            switch (door.CurrentState)
            {
                case OCL.State.Closed:
                    return "Open";
                case OCL.State.Open:
                    // Vanilla refuses to close a door someone is standing in (NoWalk cursor).
                    return door.IsAnyMoverIntersectingNavMeshObstacle() ? null : "Close";
                case OCL.State.Locked:
                    return LockedLabel(door);
                default:
                    return null;
            }
        }

        private static string ContainerLabel(Container container)
        {
            Trap trap = container.GetComponent<Trap>();
            if (trap != null && !trap.Disarmed && trap.Visible && trap.CanDisarm)
            {
                return "Disarm";
            }
            bool stealing = container.StealingFactionID != FactionName.None || container.AttackThief || container.AlliesAttackThief;
            if (container.CurrentState == OCL.State.Locked)
            {
                return LockedLabel(container);
            }
            return stealing ? "Steal" : "Loot";
        }

        private static string LockedLabel(OCL ocl)
        {
            if (ocl.PartyHasKey())
            {
                return "Unlock";
            }
            return ocl.MustHaveKey ? "Locked" : "Pick lock";
        }

        private void SetTarget(Candidate c)
        {
            m_Target = c;
            GameObject go = c != null ? c.Go : null;
            if (go == m_TargetGo)
            {
                return;
            }
            m_TargetGo = go;
            if (m_Highlighted != null)
            {
                m_Highlighted.Targeted = false;
                m_Highlighted = null;
            }
            if (go != null)
            {
                // The game's own target highlight (used while casting). 2D-outline objects ignore it and are shown
                // by the in-world prompt only.
                m_Highlighted = go.GetComponent<HighlightCharacter>();
                if (m_Highlighted != null)
                {
                    m_Highlighted.Targeted = true;
                }
            }
        }

        /// <summary>
        /// The order Player.IssueCommands gives every selected party member when a hostile is clicked: a manual
        /// Attack state replacing the current one (queued behind a reload), not restarted if already attacking that
        /// target, plus the leader's attack bark.
        /// </summary>
        private static bool OrderAttack(GameObject target)
        {
            bool ordered = false;
            int leaderSlot = PartyMemberAI.GetSelectedLeaderSlot();
            foreach (GameObject member in PartyMemberAI.GetSelectedPartyMembers())
            {
                PartyMemberAI ai = member != null ? member.GetComponent<PartyMemberAI>() : null;
                if (ai == null || ai.StateManager == null)
                {
                    continue;
                }
                ai.ClearPlayerMoveCommandTimer();
                AIState current = ai.StateManager.CurrentState;
                AI.Player.Attack existing = current as AI.Player.Attack;
                if (existing == null && current is AI.Achievement.Attack)
                {
                    existing = ai.StateManager.QueuedState as AI.Player.Attack;
                }
                if (existing != null && existing.Target == target)
                {
                    if (current is AI.Player.Attack)
                    {
                        ai.StateManager.ClearQueuedStates();
                    }
                    else
                    {
                        ai.StateManager.ClearQueuedStates(3);
                    }
                }
                else
                {
                    AI.Player.Attack attack = AIStateManager.StatePool.Allocate<AI.Player.Attack>();
                    attack.IsAutoAttack = false;
                    attack.Target = target;
                    if (current is AI.Achievement.ReloadWeapon)
                    {
                        ai.StateManager.ClearQueuedStates();
                        ai.StateManager.QueueState(attack);
                    }
                    else
                    {
                        ai.StateManager.PushState(attack, clearStack: true);
                    }
                }
                ordered = true;
                if (ai.Slot == leaderSlot && !GameState.Paused)
                {
                    SoundSet.TryPlayVoiceEffectWithLocalCooldown(member, SoundSet.SoundAction.Attack, SoundSet.s_VeryShortVODelay, forceInterrupt: true);
                }
            }
            return ordered;
        }
    }
}
