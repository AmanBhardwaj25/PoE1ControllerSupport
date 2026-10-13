using System;
using HarmonyLib;
using PoEController.Input;
using UnityEngine;

namespace PoEController.Patches
{
    /// <summary>Config Input.VibrationMode: how much the controller vibrates.</summary>
    public enum VibrationMode
    {
        /// <summary>Never.</summary>
        Off,
        /// <summary>Exactly the console version: only when the game shakes the camera.</summary>
        Essential,
        /// <summary>Essential plus combat you can feel: every hit and spell, from pattern files.</summary>
        Extended,
    }

    /// <summary>
    /// Controller rumble, played through <see cref="RumblePlayer"/> from the pattern files (<see cref="HapticLibrary"/>).
    /// <para><b>Essential</b> is the console version's: the only place it vibrates is the camera shake
    /// (<c>CameraControl.ScreenShake(duration, strength)</c>), steady at the shake's strength for its duration, and only
    /// when the game accepts the shake (a weaker one during a stronger one is ignored).</para>
    /// <para><b>Extended</b> adds, felt from the main character's side: every attack hit (<c>Health.DoDamage</c>) at
    /// strength = 0.1 + damage / the target's maximum endurance right now, full scale when the main character deals or
    /// takes it and <see cref="CompanionScale"/> when only a companion or summon is involved (enemy-on-enemy stays
    /// quiet); crits ×1.3 plus the "critical" pattern on top; the main character's own attacks and spells also play
    /// their "cast" layers from launch until they land and their "launch" layers (<c>AttackBase.Launch</c>). The
    /// pattern: the spell's own file (custom, then shipped, found through <see cref="HapticIndex"/> in every game
    /// language), else its bucket, else a bucket picked by attack kind and damage type; later pulses of a pulsing area
    /// spell use "linger". Knock-outs and deaths of party members use the "knocked_out" / "death" buckets.</para>
    /// </summary>
    [Compat.Feature(Compat.Features.Rumble)]
    internal static class RumblePatches
    {
        public static VibrationMode Mode = VibrationMode.Essential;

        /// <summary>Hits where only a companion is involved, relative to the main character's.</summary>
        public const float CompanionScale = 0.4f;
        /// <summary>The smallest hit still gets this much, so a 1 % scratch is felt at all.</summary>
        private const float MinHit = 0.1f;
        private const float CritBoost = 1.3f;

        private static int s_LastLaunchFrame = -1;
        private static int s_LastLaunchAttack;

        /// <summary>Spells that apply effects without dealing damage (heals, buffs, afflictions) play at this strength.</summary>
        private const float EffectStrength = 0.6f;
        /// <summary>The same spell again within this many seconds plays its "linger" phase (pulsing heals, re-applied chants).</summary>
        private const float LingerWindow = 4f;
        /// <summary>Effects of the same spell closer together than this are one event (auras re-applying, many effects per cast).</summary>
        private const float EffectGap = 2.5f;

        /// <summary>
        /// An effect waits this long before it plays: a spell often applies its effect a moment before its damage
        /// lands, and then only the hit (sized by the damage) should play.
        /// </summary>
        private const float EffectWait = 0.12f;

        private struct PendingEffect
        {
            public int Ability;
            public float Due;
            public bool HadPrevious;
            public float Previous;
            public HapticLayer[] Layers;
            public float Strength;
            public float Pace;
            public Color[] Colors;
            public float LightStrength;
            public string Log;
        }

        private static readonly System.Collections.Generic.List<PendingEffect> s_Pending = new System.Collections.Generic.List<PendingEffect>();

        /// <summary>
        /// Real-time with pause (the game's turn-based option off): a lean set so fast fights don't blur into one long
        /// buzz. Only hits (damage share and who is involved) and the spells that have their own pattern file, each
        /// played faster and shorter (<see cref="RealTimePace"/>). No cast build-up / release, no critical overlay
        /// (crits are still stronger), no group patterns for other spells' effects, no combat start, knock-out or death.
        /// The game's camera shake (the console vibration) plays in both.
        /// </summary>
        internal static bool RealTime => !TacticalModeManager.IsTacticalModeEnabled;

        /// <summary>Real-time: patterns run in this share of their time (0.6 = 40 % faster and shorter).</summary>
        private const float RealTimePace = 0.6f;

        private static float Pace => RealTime ? RealTimePace : 1f;

        /// <summary>Lights: how long a set of layers runs (the flash follows it), at least a quarter second.</summary>
        private static float LengthOf(HapticLayer[] layers)
        {
            float end = 0.25f;
            foreach (HapticLayer l in layers ?? new HapticLayer[0])
            {
                if (l != null)
                {
                    end = Mathf.Max(end, l.delay + Mathf.Max(0, l.repeat) * l.every + (l.length > 0f ? l.length : 0.5f));
                }
            }
            return end;
        }

        /// <summary>Lights: a flash in the colours of the attack / ability, as long as its vibration.</summary>
        private static void FlashFor(AttackBase attack, GenericAbility ability, DamagePacket.DamageType type, HapticLayer[] layers, bool main, float delay = 0f)
        {
            LightPlayer.Play(ColorsFor(attack, ability, type, out _), LengthOf(layers) * Pace + 0.15f, main ? 1f : 0.6f, delay);
        }

        /// <summary>Lights: a flash in a bucket's colours.</summary>
        private static void FlashBucket(string bucket, float seconds, float strength = 1f, float delay = 0f)
        {
            HapticPattern p = HapticLibrary.Bucket(bucket);
            if (p != null)
            {
                LightPlayer.Play(p.colors, seconds, strength, delay);
            }
        }

        /// <summary>The ability has a pattern file of its own (haptics/custom or haptics/spells), not only a bucket.</summary>
        private static bool HasOwnFile(GenericAbility ability)
        {
            HapticIndexEntry entry = HapticIndex.For(ability);
            return entry != null && HapticLibrary.Spell(entry.file) != null;
        }

        // ---- Turn-based long casts: a pulse while the spell is prepared, until the caster's turn ends.
        private const int PrepareTag = 0x50524550;
        /// <summary>Safety cap: a preparation pulse never runs longer than this.</summary>
        private const float PrepareMaxSeconds = 90f;
        private static AIController s_Preparing;
        /// <summary>The character preparing a long cast right now (null = none), and the bucket of that spell.</summary>
        internal static AIController Preparing => s_Preparing;
        internal static string PrepareBucket { get; private set; }
        private static float s_PrepareStart;

        // ---- Combat start
        private static bool s_WasInCombat;
        private static bool s_CombatKnown;

        private static void StopPrepare(string why)
        {
            if (s_Preparing == null)
            {
                return;
            }
            s_Preparing = null;
            RumblePlayer.Stop(PrepareTag);
            LightPlayer.EndPrepare();
            Plugin.Log.LogInfo($"Rumble: preparation pulse stopped ({why})");
        }

        private static void UpdatePrepare()
        {
            if (s_Preparing == null)
            {
                return;
            }
            TacticalModeManager tm = TacticalModeManager.Instance;
            if (Mode != VibrationMode.Extended || tm == null || !TacticalModeManager.IsInTacticalCombat())
            {
                StopPrepare("combat over");
            }
            else if (!tm.HasLongCastEvent(s_Preparing))
            {
                StopPrepare("cast done");
            }
            else if (!(tm.CurrentEvent is TacticalTurnEvent turn) || turn.Actor != s_Preparing)
            {
                StopPrepare("turn ended");
            }
            else if (Time.unscaledTime - s_PrepareStart > PrepareMaxSeconds)
            {
                StopPrepare("time limit");
            }
        }

        private static void UpdateCombatStart()
        {
            bool loading = GameState.IsLoading;
            bool inCombat = !loading && GameState.InCombat;
            // The first reading after start-up or a load only sets the baseline (a save made mid-fight stays quiet).
            if (loading || !s_CombatKnown)
            {
                s_WasInCombat = inCombat;
                s_CombatKnown = !loading;
                return;
            }
            if (inCombat && !s_WasInCombat && Mode == VibrationMode.Extended && !RealTime)
            {
                RumblePlayer.Play(HapticLibrary.Layers(HapticLibrary.Bucket("combat_start"), Group, "impact"), 1f);
                FlashBucket("combat_start", 1.6f);
                Plugin.Log.LogInfo($"Rumble: combat start [{Group}]");
            }
            s_WasInCombat = inCombat;
        }

        /// <summary>
        /// Turn-based: a party member starts a long cast (the spell loops its casting animation and lands later in the
        /// turn order). Plays the spell's "prepare" layers (its bucket's, or the generic "preparing" bucket) until
        /// that character's turn ends, the cast finishes or is interrupted, or the fight ends.
        /// </summary>
        [HarmonyPatch(typeof(TacticalModeManager), nameof(TacticalModeManager.StartLongCast))]
        private static class LongCast
        {
            private static void Postfix(AI.Achievement.Attack attackState)
            {
                try
                {
                    if (Mode != VibrationMode.Extended || attackState == null)
                    {
                        return;
                    }
                    AIController caster = attackState.OwnerAI;
                    AttackBase attack = attackState.CurrentAttack;
                    if (caster == null || attack == null || !PartyHelper.IsPartyMember(caster.gameObject))
                    {
                        return;
                    }
                    StopPrepare("new cast");
                    DamagePacket.DamageType type = attack.DamageData != null ? attack.DamageData.Type : DamagePacket.DamageType.None;
                    HapticPattern pattern = PatternFor(attack, attack.AbilityOrigin, type, out string source);
                    HapticGroup group = Group;
                    HapticLayer[] layers = HapticLibrary.Layers(pattern, group, "prepare");
                    if (layers == null)
                    {
                        layers = HapticLibrary.Layers(HapticLibrary.Bucket("preparing"), group, "prepare");
                        source = "bucket preparing";
                    }
                    if (layers == null)
                    {
                        return;
                    }
                    bool main = IsMain(caster.gameObject);
                    RumblePlayer.Play(layers, main ? 1f : CompanionScale, PrepareMaxSeconds, 0f, false, PrepareTag);
                    LightPlayer.Prepare(ColorsFor(attack, attack.AbilityOrigin, type, out _));
                    s_Preparing = caster;
                    s_PrepareStart = Time.unscaledTime;
                    PrepareBucket = pattern == null ? "preparing" : !string.IsNullOrEmpty(pattern.bucket) ? pattern.bucket : pattern.name;
                    Plugin.Log.LogInfo($"Rumble: {source} prepare [{group}] ({(main ? "main character" : "companion")})");
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        // ---- Turn-based end turn (hold X): a surge that builds with the hold, then one jolt at its peak.
        private const float SurgeGap = 0.07f;
        private const float SurgeJolt = 0.09f;
        private static float s_SurgePeak;

        /// <summary>
        /// Holding end turn, <paramref name="progress"/> 0..1 of the hold: the level rises 10 % -> 100 % and speeds up
        /// towards the end (0.1 + 0.9 * progress²).
        /// </summary>
        public static void EndTurnHold(float progress)
        {
            if (Mode != VibrationMode.Extended)
            {
                return;
            }
            float p = Mathf.Clamp01(progress);
            s_SurgePeak = 0.1f + 0.9f * p * p;
            RumblePlayer.Surge(s_SurgePeak);
            LightPlayer.Surge(s_SurgePeak);
        }

        /// <summary>
        /// The hold ended. <paramref name="confirmed"/> = the turn was ended: the surge cuts out and, after a short
        /// gap, one quick jolt plays at the highest level it reached. Let go early: it just stops.
        /// </summary>
        public static void EndTurnRelease(bool confirmed)
        {
            if (s_SurgePeak <= 0f)
            {
                return;
            }
            RumblePlayer.Surge(0f);
            LightPlayer.Surge(0f);
            if (confirmed && Mode == VibrationMode.Extended)
            {
                LightPlayer.Play(new[] { Color.white }, 0.35f, 1f, SurgeGap);
                RumblePlayer.Play(new[] { new HapticLayer { motor = "both", amp = 1f, fade = 1000f, length = SurgeJolt, delay = SurgeGap } },
                    s_SurgePeak, linear: true);
                Plugin.Log.LogInfo($"Rumble: end turn jolt {s_SurgePeak:0.##}");
            }
            s_SurgePeak = 0f;
        }

        /// <summary>Every frame: effects whose wait is over, the preparation pulse, combat start.</summary>
        public static void Tick()
        {
            UpdatePrepare();
            UpdateCombatStart();
            if (s_Pending.Count == 0)
            {
                return;
            }
            if (Mode != VibrationMode.Extended)
            {
                s_Pending.Clear();
                return;
            }
            float now = Time.unscaledTime;
            for (int i = s_Pending.Count - 1; i >= 0; i--)
            {
                PendingEffect p = s_Pending[i];
                if (now >= p.Due)
                {
                    s_Pending.RemoveAt(i);
                    RumblePlayer.Play(p.Layers, p.Strength, pace: p.Pace);
                    LightPlayer.Play(p.Colors, LengthOf(p.Layers) * p.Pace + 0.15f, p.LightStrength);
                    Plugin.Log.LogInfo(p.Log);
                }
            }
        }

        /// <summary>A hit of this ability arrived: drop its waiting effect (and the claim it made).</summary>
        private static void CancelPending(GenericAbility ability)
        {
            if (ability == null || s_Pending.Count == 0)
            {
                return;
            }
            int id = ability.GetInstanceID();
            for (int i = s_Pending.Count - 1; i >= 0; i--)
            {
                if (s_Pending[i].Ability != id)
                {
                    continue;
                }
                if (s_Pending[i].HadPrevious)
                {
                    s_LastImpact[id] = s_Pending[i].Previous;
                }
                else
                {
                    s_LastImpact.Remove(id);
                }
                s_ClaimedThisFrame.Remove(id);
                s_Pending.RemoveAt(i);
            }
        }

        /// <summary>
        /// Repeated hits of one ability on one target (missiles, a flurry) that land closer together than this are
        /// spaced out to it, so each one is felt on its own instead of blurring into one bump.
        /// </summary>
        private const float HitSpacing = 0.13f;
        /// <summary>Spacing never pushes a hit later than this.</summary>
        private const float MaxSpacingDelay = 0.6f;
        private static readonly System.Collections.Generic.Dictionary<long, float> s_NextHitSlot = new System.Collections.Generic.Dictionary<long, float>();

        /// <summary>Seconds to wait before this hit plays (0 = now), and books the next slot for the same ability and target.</summary>
        private static float SpacedDelay(GenericAbility ability, AttackBase attack, GameObject target)
        {
            UnityEngine.Object source = ability != null ? (UnityEngine.Object)ability : attack;
            if (source == null || target == null)
            {
                return 0f;
            }
            long key = ((long)source.GetInstanceID() << 32) ^ (uint)target.GetInstanceID();
            float now = Time.unscaledTime;
            float start = s_NextHitSlot.TryGetValue(key, out float slot) && slot > now ? slot : now;
            float delay = Mathf.Min(start - now, MaxSpacingDelay);
            s_NextHitSlot[key] = now + delay + HitSpacing * Pace;
            if (s_NextHitSlot.Count > 128)
            {
                s_NextHitSlot.Clear();
            }
            return delay;
        }

        private static int s_ClaimFrame = -1;
        private static readonly System.Collections.Generic.HashSet<int> s_ClaimedThisFrame = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.Dictionary<int, float> s_LastImpact = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>
        /// One vibration per spell per frame, whichever path sees it first (a cast that damages and applies effects to
        /// several targets at once). <paramref name="linger"/> = the same spell already played within
        /// <see cref="LingerWindow"/>. <paramref name="minGap"/> > 0 also drops a repeat closer than that.
        /// </summary>
        private static bool Claim(GenericAbility ability, float minGap, out bool linger)
        {
            linger = false;
            if (ability == null)
            {
                return true;
            }
            if (s_ClaimFrame != Time.frameCount)
            {
                s_ClaimFrame = Time.frameCount;
                s_ClaimedThisFrame.Clear();
            }
            int id = ability.GetInstanceID();
            float now = Time.unscaledTime;
            if (s_LastImpact.TryGetValue(id, out float last))
            {
                if (minGap > 0f && now - last < minGap && !s_ClaimedThisFrame.Contains(id))
                {
                    return false;
                }
                linger = now - last < LingerWindow && !s_ClaimedThisFrame.Contains(id);
            }
            if (minGap > 0f && s_ClaimedThisFrame.Contains(id))
            {
                return false;
            }
            s_ClaimedThisFrame.Add(id);
            s_LastImpact[id] = now;
            if (s_LastImpact.Count > 256)
            {
                s_LastImpact.Clear();
            }
            return true;
        }

        private static void Fail(Exception e)
        {
            Compat.Features.Off(Compat.Features.Rumble, e.Message);
            Mode = VibrationMode.Off;
        }

        private static bool IsMain(GameObject go) => go != null && GameState.s_playerCharacter != null && go == GameState.s_playerCharacter.gameObject;

        /// <summary>The vibration hardware of the controller in use.</summary>
        public static HapticGroup Group
        {
            get
            {
                Gamepad pad = Plugin.Instance != null ? Plugin.Instance.Pad : null;
                if (pad == null)
                {
                    return HapticGroup.Motors;
                }
                // Steam's virtual pad: on the Deck it is the Deck's haptics; on a PC it hides the real pad (Xbox-like).
                if (pad.IsSteamVirtual)
                {
                    return Plugin.IsSteamOS ? HapticGroup.Haptic : HapticGroup.Motors;
                }
                switch (pad.Type)
                {
                    case Sdl.ControllerType.PS5:
                        return HapticGroup.DualSense;
                    case Sdl.ControllerType.SwitchPro:
                    case Sdl.ControllerType.JoyConLeft:
                    case Sdl.ControllerType.JoyConRight:
                    case Sdl.ControllerType.JoyConPair:
                        return HapticGroup.Haptic;
                }
                return pad.Name != null && pad.Name.IndexOf("Steam Deck", StringComparison.OrdinalIgnoreCase) >= 0 ? HapticGroup.Haptic : HapticGroup.Motors;
            }
        }

        /// <summary>A bucket for hits that aren't from an indexed spell: by attack kind, then damage type.</summary>
        internal static string BucketFor(AttackBase attack, DamagePacket.DamageType type)
        {
            if (attack is AttackRoar)
            {
                return "roar";
            }
            bool area = attack is AttackAOE;
            switch (type)
            {
                case DamagePacket.DamageType.Burn: return area ? "fire_blast" : (attack is AttackBeam ? "beams" : "fire_blast");
                case DamagePacket.DamageType.Freeze: return "frost_burst";
                case DamagePacket.DamageType.Shock: return "lightning";
                case DamagePacket.DamageType.Corrode: return "acid_decay";
            }
            if (attack is AttackBeam)
            {
                return "beams";
            }
            if (area)
            {
                return "force_kinetic";
            }
            if (attack is AttackFirearm)
            {
                return "weapon_firearm";
            }
            if (attack is AttackRanged)
            {
                return "weapon_bow";
            }
            switch (type)
            {
                case DamagePacket.DamageType.Slash: return "weapon_slash";
                case DamagePacket.DamageType.Pierce: return "weapon_pierce";
                default: return "weapon_crush";
            }
        }

        /// <summary>
        /// Light colours for an attack or ability, from its in-game icon art: the "lights" of its pattern file
        /// (haptics/custom first, then haptics/spells), else the colours in the spell index (read from the icon),
        /// else its bucket's "lights" (also the bucket picked by attack kind). Empty when nothing has colours.
        /// </summary>
        internal static Color[] ColorsFor(AttackBase attack, GenericAbility ability, DamagePacket.DamageType type, out string source)
        {
            HapticIndexEntry entry = HapticIndex.For(ability);
            if (entry != null)
            {
                HapticPattern own = HapticLibrary.Spell(entry.file);
                if (own != null && own.colors.Length > 0)
                {
                    source = entry.file + ".json";
                    return own.colors;
                }
                if (entry.colors.Length > 0)
                {
                    source = "icon of " + entry.name;
                    return entry.colors;
                }
                HapticPattern b = string.IsNullOrEmpty(entry.bucket) ? null : HapticLibrary.Bucket(entry.bucket);
                if (b != null && b.colors.Length > 0)
                {
                    source = "bucket " + entry.bucket;
                    return b.colors;
                }
            }
            string bucket = BucketFor(attack, type);
            HapticPattern fallback = HapticLibrary.Bucket(bucket);
            source = "bucket " + bucket;
            return fallback != null ? fallback.colors : new Color[0];
        }

        /// <summary>The pattern for an attack: the spell's own file, its bucket, or one picked by attack kind.</summary>
        internal static HapticPattern PatternFor(AttackBase attack, GenericAbility ability, DamagePacket.DamageType type, out string source,
            string fallback = null, bool ownFileOnly = false)
        {
            HapticIndexEntry entry = HapticIndex.For(ability);
            if (entry != null)
            {
                HapticPattern own = HapticLibrary.Spell(entry.file);
                if (own != null)
                {
                    source = entry.file + ".json";
                    return own;
                }
                if (!ownFileOnly && !string.IsNullOrEmpty(entry.bucket))
                {
                    HapticPattern b = HapticLibrary.Bucket(entry.bucket);
                    if (b != null)
                    {
                        source = "bucket " + entry.bucket + " (" + entry.name + ")";
                        return b;
                    }
                }
            }
            string bucket = fallback ?? BucketFor(attack, type);
            source = "bucket " + bucket;
            return HapticLibrary.Bucket(bucket);
        }

        [HarmonyPatch(typeof(CameraControl), nameof(CameraControl.ScreenShake), typeof(float), typeof(float))]
        private static class Shake
        {
            private static void Postfix(CameraControl __instance, float duration, float strength)
            {
                try
                {
                    if (Mode == VibrationMode.Off || duration <= 0f || strength <= 0f)
                    {
                        return;
                    }
                    // Accepted = the game now runs exactly this shake (it keeps a stronger one that is still running).
                    if (Compat.GameRef.TryGet(__instance, "m_screenShakeStrength", out float current) && current == strength)
                    {
                        RumblePlayer.PlaySteady(strength, duration);
                        Plugin.Log.LogInfo($"Rumble: camera shake {strength:0.###} for {duration:0.##} s");
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        /// <summary>Extended: every attack hit (Health.DoDamage returns the damage done).</summary>
        [HarmonyPatch(typeof(Health), nameof(Health.DoDamage), typeof(DamageInfo), typeof(GameObject))]
        private static class Hit
        {
            private static void Postfix(Health __instance, DamageInfo damage, GameObject attacker, float __result)
            {
                try
                {
                    if (Mode != VibrationMode.Extended || damage == null || damage.IsMiss || __result <= 0f || __instance == null)
                    {
                        return;
                    }
                    GameObject target = __instance.gameObject;
                    bool targetInParty = PartyHelper.IsPartyMember(target);
                    bool attackerInParty = attacker != null && PartyHelper.IsPartyMember(attacker);
                    if (!targetInParty && !attackerInParty)
                    {
                        return;
                    }
                    bool main = IsMain(target) || IsMain(attacker);
                    float max = __instance.MaxStamina > 0f ? __instance.MaxStamina : __instance.MaxHealth;
                    float share = max > 0f ? __result / max : 0f;
                    float strength = Mathf.Clamp01(MinHit + share) * (main ? 1f : CompanionScale);
                    AttackBase attack = damage.Attack;
                    GenericAbility ability = damage.Ability ?? (attack != null ? attack.AbilityOrigin : null);
                    CancelPending(ability);
                    Claim(ability, 0f, out bool again);
                    bool realTime = RealTime;
                    HapticPattern pattern = PatternFor(attack, ability, damage.DamageType, out string source, ownFileOnly: realTime);
                    bool laterPulse = attack is AttackPulsedAOE && Compat.GameRef.TryGet(attack, "m_first_pulse", out bool first) && !first;
                    string phase = laterPulse || (again && attack is AttackPulsedAOE) ? "linger" : "impact";
                    HapticGroup group = Group;
                    float wait = SpacedDelay(ability, attack, target);
                    if (damage.IsCriticalHit)
                    {
                        strength = Mathf.Clamp01(strength * CritBoost);
                        if (!realTime)
                        {
                            RumblePlayer.Play(HapticLibrary.Layers(HapticLibrary.Bucket("critical"), group, "impact"), strength, 0f, wait);
                            FlashBucket("critical", 0.45f, 1f, wait + 0.17f);
                        }
                    }
                    HapticLayer[] layers = HapticLibrary.Layers(pattern, group, phase);
                    RumblePlayer.Play(layers, strength, 0f, wait, pace: Pace);
                    FlashFor(attack, ability, damage.DamageType, layers, main, wait);
                    Plugin.Log.LogInfo($"Rumble: {source} {phase}{(damage.IsCriticalHit ? (realTime ? " critical" : " + critical") : "")} {strength:0.##} [{group}]{(realTime ? " real-time" : "")}{(wait > 0f ? $" spaced +{wait:0.00} s" : "")} " +
                        $"({__result:0.#} of {max:0} endurance, {(main ? "main character" : "companion")}, {(targetInParty ? "party hit" : "party hits")})");
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        /// <summary>Extended: the main character's own attack / spell starts: cast build-up until it lands, then the release.</summary>
        private static void OnLaunch(AttackBase attack)
        {
            if (Mode != VibrationMode.Extended || RealTime || attack == null || !IsMain(attack.Owner))
            {
                return;
            }
            // Ranged attacks call the other Launch overload from inside this one: only once per attack and frame.
            int id = attack.GetInstanceID();
            if (s_LastLaunchFrame == Time.frameCount && s_LastLaunchAttack == id)
            {
                return;
            }
            s_LastLaunchFrame = Time.frameCount;
            s_LastLaunchAttack = id;
            // A turn-based long cast: the preparation pulse stands in for the build-up and the release.
            if (attack.IsLongCast && !attack.SkipAnimation && TacticalModeManager.IsInTacticalCombat(attack.Owner))
            {
                return;
            }
            GenericAbility ability = attack.AbilityOrigin;
            DamagePacket.DamageType type = attack.DamageData != null ? attack.DamageData.Type : DamagePacket.DamageType.None;
            HapticPattern pattern = PatternFor(attack, ability, type, out string source);
            HapticGroup group = Group;
            float until = Mathf.Max(0f, attack.AttackHitTime * attack.SpeedFactor);
            HapticLayer[] cast = HapticLibrary.Layers(pattern, group, "cast");
            HapticLayer[] launch = HapticLibrary.Layers(pattern, group, "launch");
            if (cast == null && launch == null)
            {
                return;
            }
            RumblePlayer.Play(cast, 1f, until);
            RumblePlayer.Play(launch, 1f);
            Plugin.Log.LogInfo($"Rumble: {source} cast {until:0.##} s [{group}]");
        }

        [HarmonyPatch(typeof(AttackBase), nameof(AttackBase.Launch), typeof(GameObject), typeof(int))]
        private static class LaunchAtTarget
        {
            private static void Postfix(AttackBase __instance)
            {
                try
                {
                    OnLaunch(__instance);
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        [HarmonyPatch(typeof(AttackBase), nameof(AttackBase.Launch), typeof(Vector3), typeof(GameObject), typeof(int))]
        private static class LaunchAtLocation
        {
            private static void Postfix(AttackBase __instance)
            {
                try
                {
                    OnLaunch(__instance);
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        /// <summary>
        /// Extended: a spell or ability applies an effect (heal, buff, debuff, affliction such as prone or sleep). Every
        /// effect goes through CharacterStats.ApplyStatusEffectHelper (afflictions apply theirs through it too). Same
        /// perspective rule as hits; one vibration per spell and moment; passive abilities and effects without a spell
        /// (items, auras of equipment) stay quiet.
        /// </summary>
        [HarmonyPatch(typeof(CharacterStats), "ApplyStatusEffectHelper", typeof(StatusEffect))]
        private static class Effect
        {
            private static void Postfix(CharacterStats __instance, StatusEffect effect, bool __result)
            {
                try
                {
                    if (Mode != VibrationMode.Extended || !__result || effect == null || __instance == null)
                    {
                        return;
                    }
                    GenericAbility ability = effect.AbilityOrigin;
                    if (ability == null || ability.Passive || (RealTime && !HasOwnFile(ability)))
                    {
                        return;
                    }
                    GameObject target = __instance.gameObject;
                    // Some effects carry no owner; the ability's user applied them.
                    GameObject caster = effect.Owner != null ? effect.Owner : ability.Owner;
                    // Delivered through the weapon (Knock Down, Crippling Strike): the weapon hit is the vibration, its effects
                    // (on the target or on the user) stay quiet. An ability with its own attack on enemies that also puts
                    // something on its user: the hit plays, not that.
                    bool weaponDelivered = ability.UsePrimaryAttack || ability.UseFullAttack;
                    if (weaponDelivered || (caster == target && ability.Attack != null && ability.Attack.ValidTargetHostile()))
                    {
                        Plugin.Log.LogInfo($"Rumble: {ability.name} effect on {target.name} skipped ({(weaponDelivered ? "weapon ability, the hit plays" : "self-effect of an attack on enemies")})");
                        return;
                    }
                    bool targetInParty = PartyHelper.IsPartyMember(target);
                    bool casterInParty = caster != null && PartyHelper.IsPartyMember(caster);
                    if (!targetInParty && !casterInParty)
                    {
                        return;
                    }
                    int id = ability.GetInstanceID();
                    bool hadPrevious = s_LastImpact.TryGetValue(id, out float previous);
                    if (!Claim(ability, EffectGap, out bool again))
                    {
                        return;
                    }
                    bool main = IsMain(target) || IsMain(caster);
                    float strength = EffectStrength * (main ? 1f : CompanionScale);
                    // The effect's own hostile flag is set inconsistently in the game data; sides are reliable.
                    bool hostile = caster != null && caster != target && targetInParty != casterInParty;
                    HapticPattern pattern = PatternFor(null, ability, DamagePacket.DamageType.None, out string source,
                        hostile ? "curses_debuffs" : "healing_buffs");
                    string phase = again ? "linger" : "impact";
                    HapticGroup group = Group;
                    s_Pending.Add(new PendingEffect
                    {
                        Ability = id,
                        Due = Time.unscaledTime + EffectWait,
                        HadPrevious = hadPrevious,
                        Previous = previous,
                        Layers = HapticLibrary.Layers(pattern, group, phase),
                        Strength = strength,
                        Pace = Pace,
                        Colors = ColorsFor(null, ability, DamagePacket.DamageType.None, out _),
                        LightStrength = main ? 1f : 0.6f,
                        Log = $"Rumble: {source} {phase} (effect, {(hostile ? "hostile" : "beneficial")}) {strength:0.##} [{group}] ({(main ? "main character" : "companion")}, " +
                            $"{(caster != null ? caster.name : "?")} -> {target.name}, attack {(ability.Attack == null ? "none" : ability.Attack.ValidTargetHostile() ? "on enemies" : "not on enemies")})",
                    });
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        [HarmonyPatch(typeof(Health), nameof(Health.HandleUnconscious), typeof(GameObject))]
        private static class KnockedOut
        {
            private static void Postfix(Health __instance)
            {
                try
                {
                    if (Mode == VibrationMode.Extended && !RealTime && __instance != null && PartyHelper.IsPartyMember(__instance.gameObject))
                    {
                        RumblePlayer.Play(HapticLibrary.Layers(HapticLibrary.Bucket("knocked_out"), Group, "impact"), IsMain(__instance.gameObject) ? 1f : 0.7f);
                        FlashBucket("knocked_out", 1.2f);
                        Plugin.Log.LogInfo("Rumble: party member knocked out");
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        [HarmonyPatch(typeof(Health), nameof(Health.HandleDeath), typeof(GameObject))]
        private static class Died
        {
            private static void Postfix(Health __instance)
            {
                try
                {
                    if (Mode == VibrationMode.Extended && !RealTime && __instance != null && PartyHelper.IsPartyMember(__instance.gameObject))
                    {
                        RumblePlayer.Play(HapticLibrary.Layers(HapticLibrary.Bucket("death"), Group, "impact"), 1f);
                        FlashBucket("death", 2.5f);
                        Plugin.Log.LogInfo("Rumble: party member died");
                    }
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }
    }
}
