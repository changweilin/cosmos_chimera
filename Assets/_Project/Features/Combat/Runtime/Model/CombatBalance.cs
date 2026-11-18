using System;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Combat
{
    /// <summary>
    /// Wild territory radius per habit (<c>Data/balance.json#ai.territory_mm</c>). REQ: combat.md#野生領域
    /// (D0-E2, 2026-11-10), creature-ecology.md#習性; the radii themselves are provisional (Q-C35).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class TerritoryTuning
    {
        /// <summary>Creates validated radii.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= each radius &lt; Encounter.DisengageRadiusM (in mm); a radius at or beyond the disengage
        ///      distance would re-engage the moment a fight ends.
        /// Post: RadiusMm returns the matching argument. Side effects: allocates this instance.
        /// </remarks>
        public TerritoryTuning(int avoidantMm, int vigilantMm, int tolerantMm, int territorialMm, int aggressiveMm,
            int dominantMm)
        {
            const int limitMm = (int)(Encounter.DisengageRadiusM * 1000f);
            Contract.Requires(avoidantMm >= 0 && avoidantMm < limitMm, "territory_mm.avoidant must be 0..disengage");
            Contract.Requires(vigilantMm >= 0 && vigilantMm < limitMm, "territory_mm.vigilant must be 0..disengage");
            Contract.Requires(tolerantMm >= 0 && tolerantMm < limitMm, "territory_mm.tolerant must be 0..disengage");
            Contract.Requires(territorialMm >= 0 && territorialMm < limitMm, "territory_mm.territorial must be 0..disengage");
            Contract.Requires(aggressiveMm >= 0 && aggressiveMm < limitMm, "territory_mm.aggressive must be 0..disengage");
            Contract.Requires(dominantMm >= 0 && dominantMm < limitMm, "territory_mm.dominant must be 0..disengage");
            _radiusMm = new int[6];
            _radiusMm[(int)Habit.Tolerant] = tolerantMm;
            _radiusMm[(int)Habit.Avoidant] = avoidantMm;
            _radiusMm[(int)Habit.Aggressive] = aggressiveMm;
            _radiusMm[(int)Habit.Vigilant] = vigilantMm;
            _radiusMm[(int)Habit.Territorial] = territorialMm;
            _radiusMm[(int)Habit.Dominant] = dominantMm;
        }

        // Indexed by the Habit value (Tolerant 0, Avoidant 1, Aggressive 2, Vigilant 3, Territorial 4, Dominant 5).
        private readonly int[] _radiusMm;

        /// <summary>Territory radius in mm for <paramref name="habit"/>.</summary>
        /// <remarks>Pre: habit defined. Post: >= 0. Side effects: none.</remarks>
        public int RadiusMm(Habit habit)
        {
            Contract.Requires(habit >= Habit.Tolerant && habit <= Habit.Dominant, "undefined Habit");
            return _radiusMm[(int)habit];
        }
    }

    /// <summary>
    /// Body radii that keep characters and creatures from passing through each other (<c>Data/balance.json#ai.body_mm</c>).
    /// REQ: combat.md#實體碰撞 (D0-W2, 2026-11-10); the values were confirmed by the user (Q-D2, 2026-11-10).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class CollisionTuning
    {
        // Only rejects unit mix-ups (m vs mm); no creature body is anywhere near 10 m.
        private const int MaxBodyMm = 10000;

        /// <summary>Creates validated body radii.</summary>
        /// <remarks>
        /// Pre: every radius 1..10000 mm.
        /// Post: BodyMm returns the matching argument; CharacterMm == characterMm.
        /// Side effects: allocates this instance.
        /// </remarks>
        public CollisionTuning(int xsMm, int sMm, int mMm, int lMm, int xlMm, int characterMm)
        {
            Contract.Requires(xsMm >= 1 && xsMm <= MaxBodyMm, "body_mm.xs must be 1..10000");
            Contract.Requires(sMm >= 1 && sMm <= MaxBodyMm, "body_mm.s must be 1..10000");
            Contract.Requires(mMm >= 1 && mMm <= MaxBodyMm, "body_mm.m must be 1..10000");
            Contract.Requires(lMm >= 1 && lMm <= MaxBodyMm, "body_mm.l must be 1..10000");
            Contract.Requires(xlMm >= 1 && xlMm <= MaxBodyMm, "body_mm.xl must be 1..10000");
            Contract.Requires(characterMm >= 1 && characterMm <= MaxBodyMm, "body_mm.character must be 1..10000");
            _bodyMm = new int[5];
            _bodyMm[(int)CreatureSizeClass.XS] = xsMm;
            _bodyMm[(int)CreatureSizeClass.S] = sMm;
            _bodyMm[(int)CreatureSizeClass.M] = mMm;
            _bodyMm[(int)CreatureSizeClass.L] = lMm;
            _bodyMm[(int)CreatureSizeClass.XL] = xlMm;
            CharacterMm = characterMm;
        }

        // Indexed by the CreatureSizeClass value (XS == 4, see its remarks).
        private readonly int[] _bodyMm;

        /// <summary>Body radius (mm) of the player character.</summary>
        /// <remarks>Pre: none. Post: 1..10000. Side effects: none.</remarks>
        public int CharacterMm { get; }

        /// <summary>Body radius (mm) of a creature of <paramref name="sizeClass"/> (before the boss scale).</summary>
        /// <remarks>Pre: sizeClass defined. Post: 1..10000. Side effects: none.</remarks>
        public int BodyMm(CreatureSizeClass sizeClass)
        {
            Contract.Requires(sizeClass >= CreatureSizeClass.S && sizeClass <= CreatureSizeClass.XS,
                "undefined CreatureSizeClass");
            return _bodyMm[(int)sizeClass];
        }
    }

    /// <summary>
    /// Boss enrage stages (<c>Data/balance.json#boss.enrage</c>): below each HP line the boss moves faster and its
    /// cooldowns shorten. REQ: combat.md#BOSS-階段 (Q-C48 「狂暴：移速與 CD 加成」; values provisional, Q-C53).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class BossEnrageTuning
    {
        private readonly int[] _hpBelowPercent;
        private readonly int[] _moveSpeedPercent;
        private readonly int[] _cdPercent;

        /// <summary>Creates validated stages; stage k (1-based) holds while HP·100 &lt; hpBelowPercent[k-1]·MaxHp.</summary>
        /// <remarks>
        /// Pre: three non-null arrays of equal length 1..8; hpBelowPercent strictly decreasing within 1..99;
        ///      moveSpeedPercent 0..100; cdPercent -100..0 (a stage never slows the boss down).
        /// Post: StageCount == length; accessors return the arguments. Side effects: copies the arrays.
        /// </remarks>
        public BossEnrageTuning(int[] hpBelowPercent, int[] moveSpeedPercent, int[] cdPercent)
        {
            Contract.Requires(hpBelowPercent != null && moveSpeedPercent != null && cdPercent != null,
                "boss.enrage arrays must be present");
            Contract.Requires(hpBelowPercent.Length >= 1 && hpBelowPercent.Length <= 8 &&
                moveSpeedPercent.Length == hpBelowPercent.Length && cdPercent.Length == hpBelowPercent.Length,
                "boss.enrage needs 1..8 stages with matching lengths");
            for (int i = 0; i < hpBelowPercent.Length; i++)
            {
                Contract.Requires(hpBelowPercent[i] >= 1 && hpBelowPercent[i] <= 99, "enrage HP line must be 1..99");
                Contract.Requires(i == 0 || hpBelowPercent[i] < hpBelowPercent[i - 1], "enrage HP lines must decrease");
                Contract.Requires(moveSpeedPercent[i] >= 0 && moveSpeedPercent[i] <= 100, "enrage speed must be 0..100");
                Contract.Requires(cdPercent[i] >= -100 && cdPercent[i] <= 0, "enrage cd must be -100..0");
            }

            _hpBelowPercent = (int[])hpBelowPercent.Clone();
            _moveSpeedPercent = (int[])moveSpeedPercent.Clone();
            _cdPercent = (int[])cdPercent.Clone();
        }

        /// <summary>Number of stages after the opening one.</summary>
        /// <remarks>Pre: none. Post: 1..8. Side effects: none.</remarks>
        public int StageCount => _hpBelowPercent.Length;

        /// <summary>Deepest stage whose HP line lies above <paramref name="hp"/>; 0 = not enraged.</summary>
        /// <remarks>
        /// Strict like on_hp_below so exactly 66 % HP is still the opening stage.
        /// Pre: maxHp >= 1; 0 &lt;= hp &lt;= maxHp. Post: 0..StageCount. Side effects: none.
        /// </remarks>
        public int StageFor(int hp, int maxHp)
        {
            Contract.Requires(maxHp >= 1 && hp >= 0 && hp <= maxHp, "need 0 <= hp <= maxHp, maxHp >= 1");
            int stage = 0;
            for (int i = 0; i < _hpBelowPercent.Length; i++)
            {
                if ((long)hp * 100 < (long)_hpBelowPercent[i] * maxHp) stage = i + 1;
            }

            return stage;
        }

        /// <summary>Relative move-speed bonus of <paramref name="stage"/>, percent.</summary>
        /// <remarks>Pre: 1 &lt;= stage &lt;= StageCount. Post: 0..100. Side effects: none.</remarks>
        public int MoveSpeedPercent(int stage)
        {
            Contract.Requires(stage >= 1 && stage <= StageCount, "stage out of range");
            return _moveSpeedPercent[stage - 1];
        }

        /// <summary>Relative cooldown change of <paramref name="stage"/> for every element, percent.</summary>
        /// <remarks>Pre: 1 &lt;= stage &lt;= StageCount. Post: -100..0. Side effects: none.</remarks>
        public int CdPercent(int stage)
        {
            Contract.Requires(stage >= 1 && stage <= StageCount, "stage out of range");
            return _cdPercent[stage - 1];
        }
    }

    /// <summary>
    /// Stagger gauge and knockback (<c>Data/balance.json#stagger</c>). REQ: combat.md#失衡與擊退 (Q-C46; values Q-C53).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class StaggerTuning
    {
        /// <summary>Creates validated stagger tuning.</summary>
        /// <remarks>
        /// Pre: gaugeHpPermille 1..10000; regenDelayMs 0..600000; regenPermillePerS 0..1000; durationMs 1..600000;
        ///      damageTakenBonusPercent 0..1000; knockbackHpPermille 1..10000; knockbackMm 0..36000.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public StaggerTuning(int gaugeHpPermille, int regenDelayMs, int regenPermillePerS, int durationMs,
            int damageTakenBonusPercent, int knockbackHpPermille, int knockbackMm)
        {
            Contract.Requires(gaugeHpPermille >= 1 && gaugeHpPermille <= 10000, "stagger.gauge_hp_permille must be 1..10000");
            Contract.Requires(regenDelayMs >= 0 && regenDelayMs <= 600000, "stagger.regen_delay_ms must be 0..600000");
            Contract.Requires(regenPermillePerS >= 0 && regenPermillePerS <= 1000, "stagger.regen_permille_per_s must be 0..1000");
            Contract.Requires(durationMs >= 1 && durationMs <= 600000, "stagger.duration_ms must be 1..600000");
            Contract.Requires(damageTakenBonusPercent >= 0 && damageTakenBonusPercent <= 1000,
                "stagger.damage_taken_bonus_percent must be 0..1000");
            Contract.Requires(knockbackHpPermille >= 1 && knockbackHpPermille <= 10000,
                "stagger.knockback_hp_permille must be 1..10000");
            Contract.Requires(knockbackMm >= 0 && knockbackMm <= 36000, "stagger.knockback_mm must be 0..36000");
            GaugeHpPermille = gaugeHpPermille;
            RegenDelayMs = regenDelayMs;
            RegenPermillePerS = regenPermillePerS;
            DurationMs = durationMs;
            DamageTakenBonusPercent = damageTakenBonusPercent;
            KnockbackHpPermille = knockbackHpPermille;
            KnockbackMm = knockbackMm;
        }

        /// <summary>Gauge capacity as permille of max HP.</summary>
        /// <remarks>Pre: none. Post: 1..10000. Side effects: none.</remarks>
        public int GaugeHpPermille { get; }

        /// <summary>Time after the last damage before the gauge starts draining.</summary>
        /// <remarks>Pre: none. Post: 0..600000. Side effects: none.</remarks>
        public int RegenDelayMs { get; }

        /// <summary>Gauge drained per second, as permille of its capacity.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int RegenPermillePerS { get; }

        /// <summary>How long a stagger lasts.</summary>
        /// <remarks>Pre: none. Post: 1..600000. Side effects: none.</remarks>
        public int DurationMs { get; }

        /// <summary>Extra damage taken while staggered, percent.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int DamageTakenBonusPercent { get; }

        /// <summary>One hit dealing at least this permille of max HP knocks the target back.</summary>
        /// <remarks>Pre: none. Post: 1..10000. Side effects: none.</remarks>
        public int KnockbackHpPermille { get; }

        /// <summary>Knockback distance on the ground.</summary>
        /// <remarks>Pre: none. Post: 0..36000. Side effects: none.</remarks>
        public int KnockbackMm { get; }

        /// <summary>Gauge capacity in HP for a combatant with <paramref name="maxHp"/>.</summary>
        /// <remarks>Pre: maxHp >= 1. Post: >= 1. Side effects: none.</remarks>
        public int CapacityHp(int maxHp)
        {
            Contract.Requires(maxHp >= 1, "maxHp must be >= 1");
            return (int)System.Math.Max(1L, (long)maxHp * GaugeHpPermille / 1000);
        }
    }

    /// <summary>
    /// Planet-size bound on ranged distances, derived from the horizon (<c>Data/balance.json#ranged_distance</c>).
    /// REQ: docs/design/derived-stats.md#星球射程上限 (Q-D1, user 2026-11-11; k and h proposed by SOL, Q-D1-R320-sol.md).
    /// </summary>
    /// <remarks>
    /// All-integer so every peer derives the same cap from the same radius: cap = min(cap_mm, isqrt(K²(2rH + H²)) / 1000)
    /// with r = floor(R × 1000) mm. The horizon is never rounded on its own first (that loses 1 mm at R=320).
    /// </remarks>
    public sealed class HorizonRangeTuning
    {
        /// <summary>Largest eye height accepted; keeps K²·N inside Int64 up to R = 8000 m.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxEyeHeightMm = 10000;

        /// <summary>Creates a validated horizon bound.</summary>
        /// <remarks>
        /// Pre: 1 &lt;= factorPermille &lt;= 1000; 1 &lt;= eyeHeightMm &lt;= MaxEyeHeightMm; capMm >= 1.
        /// Post: properties return the arguments. Side effects: allocates this instance.
        /// </remarks>
        public HorizonRangeTuning(int factorPermille, int eyeHeightMm, int capMm)
        {
            Contract.Requires(factorPermille >= 1 && factorPermille <= 1000,
                "ranged_distance.horizon_factor_permille must be 1..1000");
            Contract.Requires(eyeHeightMm >= 1 && eyeHeightMm <= MaxEyeHeightMm,
                "ranged_distance.horizon_eye_height_mm must be 1..10000");
            Contract.Requires(capMm >= 1, "cap_mm must be >= 1");
            FactorPermille = factorPermille;
            EyeHeightMm = eyeHeightMm;
            GlobalCapMm = capMm;
        }

        /// <summary>k in permille: share of the horizon a ranged move may reach.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int FactorPermille { get; }

        /// <summary>h: the balance reference eye height in mm (not the live camera height).</summary>
        /// <remarks>Pre: none. Post: 1..MaxEyeHeightMm. Side effects: none.</remarks>
        public int EyeHeightMm { get; }

        /// <summary>The planet-independent cap (ranged_distance.cap_mm).</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int GlobalCapMm { get; }

        /// <summary>Ranged-distance cap in mm on a planet of radius <paramref name="radiusM"/>.</summary>
        /// <remarks>
        /// Pre: 0 &lt; radiusM &lt;= 8000 (PlanetConfig's range).
        /// Post: 0 &lt;= result &lt;= GlobalCapMm; non-decreasing in radiusM; == floor(k·sqrt(2rH + H²)) when below the global cap.
        /// Side effects: none (pure).
        /// </remarks>
        public int CapMm(float radiusM)
        {
            Contract.Requires(radiusM > 0f && radiusM <= 8000f, "radius must be in (0, 8000] m");
            // double × 1000 is exact for every float radius, so floor is the only quantization.
            long r = (long)Math.Floor((double)radiusM * 1000d);
            long h = EyeHeightMm;
            long k = FactorPermille;
            long n = checked(2L * r * h + h * h);
            long t = checked(k * k * n);
            long cap = IntSqrt(t) / 1000L;
            return cap >= GlobalCapMm ? GlobalCapMm : (int)cap;
        }

        // Bisection on q with q <= t / q, so the search never forms q² and cannot overflow (SOL Q-D1 A).
        private static long IntSqrt(long t)
        {
            if (t < 2L)
            {
                return t;
            }

            long lo = 1L;
            long hi = Math.Min(t, 3037000499L) + 1L;
            while (hi - lo > 1L)
            {
                long m = lo + (hi - lo) / 2L;
                if (m <= t / m)
                {
                    lo = m;
                }
                else
                {
                    hi = m;
                }
            }

            return lo;
        }
    }

    /// <summary>Grip bonuses for held weapons (<c>Data/balance.json#equipment</c>). REQ: crafting.md#穿戴與數值 (values provisional).</summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class GripTuning
    {
        /// <summary>Creates validated grip bonuses.</summary>
        /// <remarks>Pre: both 0..500. Post: properties equal the arguments. Side effects: none.</remarks>
        public GripTuning(int twoHandedDamagePercent, int dualWieldSpeedPercent)
        {
            Contract.Requires(twoHandedDamagePercent >= 0 && twoHandedDamagePercent <= 500, "equipment.two_handed_damage_percent must be 0..500");
            Contract.Requires(dualWieldSpeedPercent >= 0 && dualWieldSpeedPercent <= 500, "equipment.dual_wield_speed_percent must be 0..500");
            TwoHandedDamagePercent = twoHandedDamagePercent;
            DualWieldSpeedPercent = dualWieldSpeedPercent;
        }

        /// <summary>Damage bonus while holding a two-handed weapon.</summary>
        /// <remarks>Pre: none. Post: 0..500. Side effects: none.</remarks>
        public int TwoHandedDamagePercent { get; }

        /// <summary>Attack-speed bonus while dual wielding.</summary>
        /// <remarks>Pre: none. Post: 0..500. Side effects: none.</remarks>
        public int DualWieldSpeedPercent { get; }
    }

    /// <summary>
    /// Block, stance gauge and perfect block (<c>Data/balance.json#block</c>). REQ: combat.md#格擋 (Q-C56; values provisional).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class BlockTuning
    {
        /// <summary>Creates validated block tuning.</summary>
        /// <remarks>
        /// Pre: basePermille 0..1000; stanceHpPermille 1..10000; stanceRegenDelayMs 0..600000; stanceRegenPermillePerS
        ///      0..1000; guardBreakMs 1..600000; perfectWindowMs 0..1000; moveSpeedPermille 0..1000; reflectPercent 0..1000;
        ///      basePermille &lt;= shieldPermille &lt;= 1000.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public BlockTuning(int basePermille, int stanceHpPermille, int stanceRegenDelayMs, int stanceRegenPermillePerS,
            int guardBreakMs, int perfectWindowMs, int moveSpeedPermille, int reflectPercent, int shieldPermille)
        {
            Contract.Requires(shieldPermille >= basePermille && shieldPermille <= 1000, "block.shield_permille must be base_permille..1000");
            ShieldPermille = shieldPermille;
            Contract.Requires(reflectPercent >= 0 && reflectPercent <= 1000, "block.reflect_percent must be 0..1000");
            ReflectPercent = reflectPercent;
            Contract.Requires(basePermille >= 0 && basePermille <= 1000, "block.base_permille must be 0..1000");
            Contract.Requires(stanceHpPermille >= 1 && stanceHpPermille <= 10000, "block.stance_hp_permille must be 1..10000");
            Contract.Requires(stanceRegenDelayMs >= 0 && stanceRegenDelayMs <= 600000,
                "block.stance_regen_delay_ms must be 0..600000");
            Contract.Requires(stanceRegenPermillePerS >= 0 && stanceRegenPermillePerS <= 1000,
                "block.stance_regen_permille_per_s must be 0..1000");
            Contract.Requires(guardBreakMs >= 1 && guardBreakMs <= 600000, "block.guard_break_ms must be 1..600000");
            Contract.Requires(perfectWindowMs >= 0 && perfectWindowMs <= 1000, "block.perfect_window_ms must be 0..1000");
            Contract.Requires(moveSpeedPermille >= 0 && moveSpeedPermille <= 1000, "block.move_speed_permille must be 0..1000");
            BasePermille = basePermille;
            StanceHpPermille = stanceHpPermille;
            StanceRegenDelayMs = stanceRegenDelayMs;
            StanceRegenPermillePerS = stanceRegenPermillePerS;
            GuardBreakMs = guardBreakMs;
            PerfectWindowMs = perfectWindowMs;
            MoveSpeedPermille = moveSpeedPermille;
        }

        /// <summary>Damage removed by a block without a shield, permille.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int BasePermille { get; }

        /// <summary>Damage removed by a block while holding a shield, permille (crafting.md#穿戴與數值).</summary>
        /// <remarks>Pre: none. Post: BasePermille..1000. Side effects: none.</remarks>
        public int ShieldPermille { get; }

        /// <summary>Stance gauge capacity as permille of max HP.</summary>
        /// <remarks>Pre: none. Post: 1..10000. Side effects: none.</remarks>
        public int StanceHpPermille { get; }

        /// <summary>Time without blocking before the stance gauge starts recovering.</summary>
        /// <remarks>Pre: none. Post: 0..600000. Side effects: none.</remarks>
        public int StanceRegenDelayMs { get; }

        /// <summary>Stance recovered per second, permille of capacity.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int StanceRegenPermillePerS { get; }

        /// <summary>Stun after the stance gauge fills (no block, cast or movement).</summary>
        /// <remarks>Pre: none. Post: 1..600000. Side effects: none.</remarks>
        public int GuardBreakMs { get; }

        /// <summary>A hit landing at most this long after the block key was released is a perfect block.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int PerfectWindowMs { get; }

        /// <summary>Perfect-block counter-damage, percent of the blocked move's damage against the attacker (Q-C59).</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int ReflectPercent { get; }

        /// <summary>Move speed while blocking, permille of normal.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int MoveSpeedPermille { get; }

        /// <summary>Stance gauge capacity for a unit with <paramref name="maxHp"/>.</summary>
        /// <remarks>Pre: maxHp >= 1. Post: >= 1. Side effects: none.</remarks>
        public int CapacityHp(int maxHp)
        {
            Contract.Requires(maxHp >= 1, "maxHp must be >= 1");
            return (int)System.Math.Max(1L, (long)maxHp * StanceHpPermille / 1000);
        }
    }

    /// <summary>
    /// Player-side creature roll (<c>Data/balance.json#creature_roll</c>). REQ: combat.md#完美閃避 (Q-C55: the main
    /// creature rolls during combat; values mirror the character roll).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class CreatureRollTuning
    {
        /// <summary>Creates validated roll tuning.</summary>
        /// <remarks>
        /// Pre: durationMs 1..10000; 0 &lt;= perfectDodgeMs &lt;= invulnerableMs &lt;= durationMs; cooldownMs >= durationMs
        ///      and &lt;= 60000; distanceHeightPermille 0..10000.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public CreatureRollTuning(int durationMs, int invulnerableMs, int cooldownMs, int perfectDodgeMs,
            int distanceHeightPermille)
        {
            Contract.Requires(distanceHeightPermille >= 0 && distanceHeightPermille <= 10000,
                "creature_roll.distance_height_permille must be 0..10000");
            DistanceHeightPermille = distanceHeightPermille;
            Contract.Requires(durationMs >= 1 && durationMs <= 10000, "creature_roll.duration_ms must be 1..10000");
            Contract.Requires(invulnerableMs >= 0 && invulnerableMs <= durationMs,
                "creature_roll.invulnerable_ms must be 0..duration_ms");
            Contract.Requires(perfectDodgeMs >= 0 && perfectDodgeMs <= invulnerableMs,
                "creature_roll.perfect_dodge_ms must be 0..invulnerable_ms");
            Contract.Requires(cooldownMs >= durationMs && cooldownMs <= 60000,
                "creature_roll.cooldown_ms must be duration_ms..60000");
            DurationMs = durationMs;
            InvulnerableMs = invulnerableMs;
            CooldownMs = cooldownMs;
            PerfectDodgeMs = perfectDodgeMs;
        }

        /// <summary>Roll length.</summary>
        /// <remarks>Pre: none. Post: 1..10000. Side effects: none.</remarks>
        public int DurationMs { get; }

        /// <summary>I-frames from the roll start, half-open [start, start + InvulnerableMs).</summary>
        /// <remarks>Pre: none. Post: 0..DurationMs. Side effects: none.</remarks>
        public int InvulnerableMs { get; }

        /// <summary>Time from one roll start to the next allowed start.</summary>
        /// <remarks>Pre: none. Post: DurationMs..60000. Side effects: none.</remarks>
        public int CooldownMs { get; }

        /// <summary>Roll distance as permille of the creature's height (2 × its collision radius; Q-C58).</summary>
        /// <remarks>Pre: none. Post: 0..10000. Side effects: none.</remarks>
        public int DistanceHeightPermille { get; }

        /// <summary>Contact within this long of the roll start is a perfect dodge (the attacker staggers).</summary>
        /// <remarks>Pre: none. Post: 0..InvulnerableMs. Side effects: none.</remarks>
        public int PerfectDodgeMs { get; }
    }

    /// <summary>All combat tunables parsed from <c>Data/balance.json</c> (the single source; the importer copies the text).</summary>
    /// <remarks>
    /// Pre: build through <see cref="FromJson"/>. Post: every member non-null and validated by its own constructor.
    /// Side effects: none after construction.
    /// </remarks>
    public sealed class CombatBalance
    {
        private CombatBalance(DerivedStats stats, HitVolumeDefaults hit, EffectRulesConfig effects, AiTuning ai,
            int switchCooldownMs, PlayerVitalsConfig player, int captureBasePercent, int captureBossPercent,
            int captureFriendlyBonusPercent,
            TerritoryTuning territory, CollisionTuning collision, HorizonRangeTuning horizon,
            BossEnrageTuning enrage, int bossWindupPermille, StaggerTuning stagger, BlockTuning block,
            CreatureRollTuning creatureRoll, GripTuning grips, int playerBaseDefence, int harvestDamagePermille)
        {
            PlayerBaseDefence = playerBaseDefence;
            HarvestDamagePermille = harvestDamagePermille;
            Block = block;
            Grips = grips;
            CreatureRoll = creatureRoll;
            Enrage = enrage;
            BossWindupPermille = bossWindupPermille;
            Stagger = stagger;
            Horizon = horizon;
            Collision = collision;
            Territory = territory;
            Stats = stats;
            Hit = hit;
            Effects = effects;
            Ai = ai;
            SwitchCooldownMs = switchCooldownMs;
            Player = player;
            CaptureBasePercent = captureBasePercent;
            CaptureBossPercent = captureBossPercent;
            CaptureFriendlyBonusPercent = captureFriendlyBonusPercent;
        }

        /// <summary>LV100 basis of the player character's yin and yang defence (player.base_defense; combat.md#玩家角色的守).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int PlayerBaseDefence { get; }

        /// <summary>Object damage multiplier for a caster whose life skill matches the object's substance (destructibles.md#傷害).</summary>
        /// <remarks>Pre: none. Post: 1000..10000 (field_utilities.harvest_damage_permille). Side effects: none.</remarks>
        public int HarvestDamagePermille { get; }

        /// <summary>Boss windup scale in permille (boss.windup_permille; combat.md#前搖).</summary>
        /// <remarks>Pre: none. Post: 1..10000. Side effects: none.</remarks>
        public int BossWindupPermille { get; }

        /// <summary>Stagger gauge and knockback.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public StaggerTuning Stagger { get; }

        /// <summary>Block, stance gauge and perfect block.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public BlockTuning Block { get; }

        /// <summary>Grip bonuses for held weapons (Data/balance.json#equipment).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public GripTuning Grips { get; }

        /// <summary>Player-side creature roll and perfect dodge.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CreatureRollTuning CreatureRoll { get; }

        /// <summary>Boss enrage stages (Q-C48).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public BossEnrageTuning Enrage { get; }

        /// <summary>Wild territory radii by habit.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public TerritoryTuning Territory { get; }

        /// <summary>Body radii for character/creature separation.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CollisionTuning Collision { get; }

        /// <summary>Planet-size bound on ranged distances.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public HorizonRangeTuning Horizon { get; }

        /// <summary>Derived-stat model.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public DerivedStats Stats { get; }

        /// <summary>Global hit-volume dimensions.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public HitVolumeDefaults Hit { get; }

        /// <summary>Effect multiplier clamps and field tick.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public EffectRulesConfig Effects { get; }

        /// <summary>AI stand-off ratios and leash.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public AiTuning Ai { get; }

        /// <summary>Switch cooldown in ms (team.switch_cooldown_ms).</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int SwitchCooldownMs { get; }

        /// <summary>Player-character HP and roll timings.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public PlayerVitalsConfig Player { get; }

        /// <summary>Capture chance for a knocked-out wild creature, percent (capture.base_percent).</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CaptureBasePercent { get; }

        /// <summary>Capture chance for a knocked-out boss, percent (capture.boss_percent).</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CaptureBossPercent { get; }

        /// <summary>Capture chance bonus for a friendly wild ally after winning a confrontation (capture.friendly_bonus_percent).</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CaptureFriendlyBonusPercent { get; }

        /// <summary>Parses balance.json text.</summary>
        /// <remarks>
        /// Pre: json is the content of Data/balance.json (all sections present; values within each config's Pre).
        /// Post: a validated balance; missing sections fail with ContractException rather than defaulting to 0.
        /// Side effects: allocates.
        /// </remarks>
        public static CombatBalance FromJson(string json)
        {
            Contract.Requires(!string.IsNullOrEmpty(json), "balance json must not be empty");
            Dto d = JsonUtility.FromJson<Dto>(json);
            Contract.Requires(d != null && d.hp != null && d.melee_volume != null && d.ranged_distance != null &&
                d.cd != null && d.speed != null && d.evasion != null && d.hit != null && d.hit_volume != null &&
                d.team != null && d.field != null && d.effects != null && d.crit != null && d.damage != null && d.player != null && d.ai != null && d.ai.territory_mm != null &&
                d.ai.body_mm != null &&
                d.capture != null, "balance json is missing a section");
            Contract.Requires(d.capture.base_percent >= 0 && d.capture.base_percent <= 100 &&
                d.capture.boss_percent >= 0 && d.capture.boss_percent <= 100 &&
                d.capture.friendly_bonus_percent >= 0 && d.capture.friendly_bonus_percent <= 100, "capture percents must be 0..100");

            var coefficients = new DerivedStatsCoefficients(d.model_version, d.stat_effective_cap,
                d.hp.@base, d.hp.gain, d.hp.half_stat,
                d.melee_volume.gain_permille, d.melee_volume.half_stat,
                d.ranged_distance.gain_permille, d.ranged_distance.half_stat, d.ranged_distance.cap_mm,
                d.cd.min_permille, d.cd.half_stat, d.cd.floor_ms,
                d.speed.base_mm_s, d.speed.gain_mm_s, d.speed.half_stat,
                d.evasion.cap_permille, d.evasion.half_stat, d.hit.floor_permille,
                d.hp.per_point, d.shared_base_stat, d.damage.ratio_min_permille, d.damage.ratio_max_permille);
            HitVolumeDto h = d.hit_volume;
            var hit = new HitVolumeDefaults(h.height_mm, h.front_width_mm, h.sweep_angle_deg, h.cone_angle_deg,
                h.line_width_mm, h.arc_landing_radius_mm, h.field_radius_mm, h.target_cast_mid_mm, h.target_cast_long_mm);
            var effects = new EffectRulesConfig(d.effects.multiplier_min_percent, d.effects.multiplier_max_percent,
                d.field.tick_ms, d.crit.base_rate_permille, d.crit.base_damage_percent);
            var ai = new AiTuning(d.ai.keep_distance_permille, d.ai.close_in_permille, d.ai.hold_permille,
                d.ai.follow_leash_mm);
            Contract.Requires(d.field_utilities != null, "balance json is missing field_utilities");
            Contract.Requires(d.field_utilities.harvest_damage_permille >= 1000 && d.field_utilities.harvest_damage_permille <= 10000,
                "field_utilities.harvest_damage_permille must be 1000..10000");
            // An absent key parses as 0, which would put every hit on the player at the ratio cap.
            Contract.Requires(d.player.base_defense >= 1, "player.base_defense must be >= 1");
            var player = new PlayerVitalsConfig(d.player.max_hp, d.player.roll_duration_ms, d.player.roll_invulnerable_ms,
                d.player.roll_cooldown_ms);
            Contract.Requires(d.team.switch_cooldown_ms >= 0, "switch cooldown must be >= 0");
            var territory = new TerritoryTuning(
                d.ai.territory_mm.avoidant,
                d.ai.territory_mm.vigilant,
                d.ai.territory_mm.tolerant,
                d.ai.territory_mm.territorial,
                d.ai.territory_mm.aggressive,
                d.ai.territory_mm.dominant);
            // An absent body_mm key parses as 0 and is rejected by CollisionTuning.
            BodyDto b = d.ai.body_mm;
            var collision = new CollisionTuning(b.xs, b.s, b.m, b.l, b.xl, b.character);
            // Absent horizon keys parse as 0 and are rejected by HorizonRangeTuning.
            var horizon = new HorizonRangeTuning(d.ranged_distance.horizon_factor_permille,
                d.ranged_distance.horizon_eye_height_mm, d.ranged_distance.cap_mm);
            Contract.Requires(d.boss != null && d.boss.enrage != null, "balance json is missing boss.enrage");
            int stages = d.boss.enrage.Length;
            var hpBelow = new int[stages];
            var speed = new int[stages];
            var cd = new int[stages];
            for (int i = 0; i < stages; i++)
            {
                hpBelow[i] = d.boss.enrage[i].hp_below_percent;
                speed[i] = d.boss.enrage[i].move_speed_percent;
                cd[i] = -d.boss.enrage[i].cd_reduction_percent;
            }

            var enrage = new BossEnrageTuning(hpBelow, speed, cd);
            Contract.Requires(d.boss.windup_permille >= 1 && d.boss.windup_permille <= 10000,
                "boss.windup_permille must be 1..10000");
            Contract.Requires(d.stagger != null, "balance json is missing stagger");
            StaggerDto st = d.stagger;
            var stagger = new StaggerTuning(st.gauge_hp_permille, st.regen_delay_ms, st.regen_permille_per_s, st.duration_ms,
                st.damage_taken_bonus_percent, st.knockback_hp_permille, st.knockback_mm);
            Contract.Requires(d.block != null, "balance json is missing block");
            BlockDto bk = d.block;
            var block = new BlockTuning(bk.base_permille, bk.stance_hp_permille, bk.stance_regen_delay_ms,
                bk.stance_regen_permille_per_s, bk.guard_break_ms, bk.perfect_window_ms, bk.move_speed_permille,
                bk.reflect_percent, bk.shield_permille);
            Contract.Requires(d.creature_roll != null, "balance json is missing creature_roll");
            CreatureRollDto cr = d.creature_roll;
            var creatureRoll = new CreatureRollTuning(cr.duration_ms, cr.invulnerable_ms, cr.cooldown_ms, cr.perfect_dodge_ms,
                cr.distance_height_permille);
            return new CombatBalance(new DerivedStats(coefficients), hit, effects, ai, d.team.switch_cooldown_ms, player,
                d.capture.base_percent, d.capture.boss_percent, d.capture.friendly_bonus_percent, territory, collision, horizon, enrage,
                d.boss.windup_permille, stagger, block, creatureRoll,
                RequireGrips(d.equipment), d.player.base_defense, d.field_utilities.harvest_damage_permille);
        }

        // Field names mirror balance.json keys because JsonUtility maps by name.
#pragma warning disable 0649, IDE1006
        [Serializable] private sealed class Dto
        {
            public FieldUtilitiesDto field_utilities;
            public int model_version;
            public int stat_effective_cap;
            public HpDto hp;
            public GainDto melee_volume;
            public RangedDto ranged_distance;
            public CdDto cd;
            public SpeedDto speed;
            public EvasionDto evasion;
            public HitDto hit;
            public HitVolumeDto hit_volume;
            public TeamDto team;
            public FieldDto field;
            public EffectsDto effects;
            public CritDto crit;
            public int shared_base_stat;
            public DamageDto damage;
            public PlayerDto player;
            public AiDto ai;
            public CaptureDto capture;
            public BossDto boss;
            public StaggerDto stagger;
            public BlockDto block;
            public CreatureRollDto creature_roll;
            public EquipmentDto equipment;
        }

        [Serializable] private sealed class HpDto { public int @base; public int gain; public int half_stat; public int per_point; }
        [Serializable] private sealed class DamageDto { public int ratio_min_permille; public int ratio_max_permille; }
        [Serializable] private sealed class GainDto { public int gain_permille; public int half_stat; }
        [Serializable] private sealed class RangedDto
        {
            public int gain_permille;
            public int half_stat;
            public int cap_mm;
            public int horizon_factor_permille;
            public int horizon_eye_height_mm;
        }
        [Serializable] private sealed class CdDto { public int min_permille; public int half_stat; public int floor_ms; }
        [Serializable] private sealed class SpeedDto { public int base_mm_s; public int gain_mm_s; public int half_stat; }
        [Serializable] private sealed class EvasionDto { public int cap_permille; public int half_stat; }
        [Serializable] private sealed class HitDto { public int floor_permille; }
        [Serializable] private sealed class TeamDto { public int switch_cooldown_ms; }
        [Serializable] private sealed class FieldDto { public int tick_ms; }
        [Serializable] private sealed class EffectsDto { public int multiplier_min_percent; public int multiplier_max_percent; }
        [Serializable] private sealed class CritDto { public int base_rate_permille; public int base_damage_percent; }
        [Serializable] private sealed class CaptureDto { public int base_percent; public int boss_percent; public int friendly_bonus_percent; }

        [Serializable] private sealed class HitVolumeDto
        {
            public int height_mm;
            public int front_width_mm;
            public int sweep_angle_deg;
            public int cone_angle_deg;
            public int line_width_mm;
            public int arc_landing_radius_mm;
            public int field_radius_mm;
            public int target_cast_mid_mm;
            public int target_cast_long_mm;
        }

        [Serializable] private sealed class FieldUtilitiesDto
        {
            public int harvest_damage_permille;
        }

        [Serializable] private sealed class PlayerDto
        {
            public int max_hp;
            public int roll_duration_ms;
            public int roll_invulnerable_ms;
            public int roll_cooldown_ms;
            public int base_defense;
        }

        [Serializable] private sealed class AiDto
        {
            public int keep_distance_permille;
            public int close_in_permille;
            public int hold_permille;
            public int follow_leash_mm;
            public TerritoryDto territory_mm;
            public BodyDto body_mm;
        }

        [Serializable] private sealed class BodyDto
        {
            public int xs;
            public int s;
            public int m;
            public int l;
            public int xl;
            public int character;
        }

        [Serializable] private sealed class BossDto { public int windup_permille; public EnrageDto[] enrage; }

        [Serializable] private sealed class StaggerDto
        {
            public int gauge_hp_permille;
            public int regen_delay_ms;
            public int regen_permille_per_s;
            public int duration_ms;
            public int damage_taken_bonus_percent;
            public int knockback_hp_permille;
            public int knockback_mm;
        }

        private static GripTuning RequireGrips(EquipmentDto e)
        {
            Contract.Requires(e != null, "balance json is missing equipment");
            return new GripTuning(e.two_handed_damage_percent, e.dual_wield_speed_percent);
        }

        [Serializable] private sealed class EquipmentDto
        {
            public int two_handed_damage_percent;
            public int dual_wield_speed_percent;
        }

        [Serializable] private sealed class BlockDto
        {
            public int base_permille;
            public int stance_hp_permille;
            public int stance_regen_delay_ms;
            public int stance_regen_permille_per_s;
            public int guard_break_ms;
            public int perfect_window_ms;
            public int move_speed_permille;
            public int reflect_percent;
            public int shield_permille;
        }

        [Serializable] private sealed class CreatureRollDto
        {
            public int duration_ms;
            public int invulnerable_ms;
            public int cooldown_ms;
            public int perfect_dodge_ms;
            public int distance_height_permille;
        }

        [Serializable] private sealed class EnrageDto
        {
            public int hp_below_percent;
            public int move_speed_percent;
            public int cd_reduction_percent;
        }

        [Serializable] private sealed class TerritoryDto
        {
            public int avoidant;
            public int vigilant;
            public int tolerant;
            public int territorial;
            public int aggressive;
            public int dominant;
        }
#pragma warning restore 0649, IDE1006
    }
}
