using Cosmimera.Core.Contracts;

namespace Cosmimera.Combat
{
    /// <summary>
    /// Validated coefficients of the P2-02 derived-stat model, mirroring <c>Data/balance.json</c>.
    /// REQ: docs/design/derived-stats.md, docs/design/derived-stats/balance-keys.md.
    /// </summary>
    /// <remarks>
    /// The envelope checked here is the arithmetic-safety envelope of tools/balance/derived_stats.py, not a balance
    /// range: inside it every intermediate of <see cref="DerivedStats"/> fits Int64 and every result fits Int32.
    /// </remarks>
    public sealed class DerivedStatsCoefficients
    {
        /// <summary>Largest value any coefficient may take (portability envelope).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxCoefficient = 1_000_000;

        /// <summary>Formula / rounding convention this port implements.</summary>
        /// <remarks>Pre: none. Post: constant; a different model_version needs a new port. Side effects: none.</remarks>
        public const int SupportedModelVersion = 1;

        /// <summary>Builds a coefficient set.</summary>
        /// <remarks>
        /// Pre: modelVersion == SupportedModelVersion; every argument in 0..MaxCoefficient; statEffectiveCap,
        ///      every halfStat, hpBase, speedBaseMmS, cdFloorMs, rangedCapMm >= 1; both STR gains &lt;= 1000;
        ///      cdMinPermille in 1..1000; evasionCapPermille &lt;= 999; hitFloorPermille in 1..1000.
        /// Post: properties return the arguments unchanged.
        /// Side effects: allocates this instance.
        /// </remarks>
        public DerivedStatsCoefficients(int modelVersion, int statEffectiveCap,
            int hpBase, int hpGain, int hpHalfStat,
            int meleeVolumeGainPermille, int meleeVolumeHalfStat,
            int rangedGainPermille, int rangedHalfStat, int rangedCapMm,
            int cdMinPermille, int cdHalfStat, int cdFloorMs,
            int speedBaseMmS, int speedGainMmS, int speedHalfStat,
            int evasionCapPermille, int evasionHalfStat,
            int hitFloorPermille)
        {
            Contract.Requires(modelVersion == SupportedModelVersion, "unsupported balance model_version");
            RequireRange(statEffectiveCap, 1, MaxCoefficient, "stat_effective_cap");
            RequireRange(hpBase, 1, MaxCoefficient, "hp.base");
            RequireRange(hpGain, 0, MaxCoefficient, "hp.gain");
            RequireRange(hpHalfStat, 1, MaxCoefficient, "hp.half_stat");
            RequireRange(meleeVolumeGainPermille, 0, DerivedStats.Permille, "melee_volume.gain_permille");
            RequireRange(meleeVolumeHalfStat, 1, MaxCoefficient, "melee_volume.half_stat");
            RequireRange(rangedGainPermille, 0, DerivedStats.Permille, "ranged_distance.gain_permille");
            RequireRange(rangedHalfStat, 1, MaxCoefficient, "ranged_distance.half_stat");
            RequireRange(rangedCapMm, 1, MaxCoefficient, "ranged_distance.cap_mm");
            RequireRange(cdMinPermille, 1, DerivedStats.Permille, "cd.min_permille");
            RequireRange(cdHalfStat, 1, MaxCoefficient, "cd.half_stat");
            RequireRange(cdFloorMs, 1, MaxCoefficient, "cd.floor_ms");
            RequireRange(speedBaseMmS, 1, MaxCoefficient, "speed.base_mm_s");
            RequireRange(speedGainMmS, 0, MaxCoefficient, "speed.gain_mm_s");
            RequireRange(speedHalfStat, 1, MaxCoefficient, "speed.half_stat");
            RequireRange(evasionCapPermille, 0, DerivedStats.Permille - 1, "evasion.cap_permille");
            RequireRange(evasionHalfStat, 1, MaxCoefficient, "evasion.half_stat");
            RequireRange(hitFloorPermille, 1, DerivedStats.Permille, "hit.floor_permille");

            StatEffectiveCap = statEffectiveCap;
            HpBase = hpBase;
            HpGain = hpGain;
            HpHalfStat = hpHalfStat;
            MeleeVolumeGainPermille = meleeVolumeGainPermille;
            MeleeVolumeHalfStat = meleeVolumeHalfStat;
            RangedGainPermille = rangedGainPermille;
            RangedHalfStat = rangedHalfStat;
            RangedCapMm = rangedCapMm;
            CdMinPermille = cdMinPermille;
            CdHalfStat = cdHalfStat;
            CdFloorMs = cdFloorMs;
            SpeedBaseMmS = speedBaseMmS;
            SpeedGainMmS = speedGainMmS;
            SpeedHalfStat = speedHalfStat;
            EvasionCapPermille = evasionCapPermille;
            EvasionHalfStat = evasionHalfStat;
            HitFloorPermille = hitFloorPermille;
        }

        /// <summary>Stat value above which every derived value saturates.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int StatEffectiveCap { get; }

        /// <summary>HP at CON 0.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int HpBase { get; }

        /// <summary>Asymptotic HP gain from CON.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int HpGain { get; }

        /// <summary>CON giving half of <see cref="HpGain"/>.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int HpHalfStat { get; }

        /// <summary>Asymptotic melee hit-volume gain over 1000‰ (volume, not linear size).</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int MeleeVolumeGainPermille { get; }

        /// <summary>STR giving half of the melee volume gain.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int MeleeVolumeHalfStat { get; }

        /// <summary>Asymptotic ranged-distance gain over 1000‰.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int RangedGainPermille { get; }

        /// <summary>STR giving half of the ranged gain.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int RangedHalfStat { get; }

        /// <summary>Planet-independent cap on scaled ranged distance in mm; smaller planets lower it further (HorizonRangeTuning).</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int RangedCapMm { get; }

        /// <summary>Lower bound of the SPI cooldown multiplier, in permille.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int CdMinPermille { get; }

        /// <summary>SPI giving half of the cooldown reduction.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int CdHalfStat { get; }

        /// <summary>Absolute cooldown floor in ms.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int CdFloorMs { get; }

        /// <summary>Move speed at AGI 0, mm/s.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int SpeedBaseMmS { get; }

        /// <summary>Asymptotic move-speed gain from AGI, mm/s.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int SpeedGainMmS { get; }

        /// <summary>AGI giving half of the speed gain.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int SpeedHalfStat { get; }

        /// <summary>Asymptotic evasion cap, permille.</summary>
        /// <remarks>Pre: none. Post: 0..999. Side effects: none.</remarks>
        public int EvasionCapPermille { get; }

        /// <summary>AGI giving half of the evasion cap.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int EvasionHalfStat { get; }

        /// <summary>Minimum post-geometry hit chance, permille.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int HitFloorPermille { get; }

        private static void RequireRange(int value, int min, int max, string name)
        {
            Contract.Requires(value >= min && value <= max, name + " outside its portability envelope");
        }
    }

    /// <summary>
    /// Stat → derived value functions (HP, melee volume / linear scale, ranged distance, cooldown, speed, evasion,
    /// conditional hit chance). Bit-exact port of tools/balance/derived_stats.py (model_version 1).
    /// REQ: docs/design/derived-stats.md.
    /// </summary>
    /// <remarks>
    /// Every formula quantizes in the documented order (floor per division, CD multiplied then ceiled); fusing
    /// divisions would change results against tools/balance/golden_derived_stats.json. All arithmetic is Int64 —
    /// coefficient ≤ 1e6 × stat ≤ 1e6 and Int32 move values × 2000‰ both stay far below Int64 range.
    /// </remarks>
    public sealed class DerivedStats
    {
        /// <summary>Fixed-point unit for rates: 1000 = 100 %. Wire unit, not a balance knob.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int Permille = 1000;

        private readonly DerivedStatsCoefficients _c;

        /// <summary>Wraps a validated coefficient set.</summary>
        /// <remarks>Pre: coefficients != null. Post: <see cref="Coefficients"/> == coefficients. Side effects: none.</remarks>
        public DerivedStats(DerivedStatsCoefficients coefficients)
        {
            Contract.Requires(coefficients != null, "coefficients must not be null");
            _c = coefficients;
        }

        /// <summary>The coefficients this model evaluates.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public DerivedStatsCoefficients Coefficients => _c;

        /// <summary>Max HP for <paramref name="con"/>.</summary>
        /// <remarks>
        /// Pre: con >= 0. Post: HpBase &lt;= result &lt;= HpBase + HpGain; non-decreasing in con; constant above
        /// StatEffectiveCap. Side effects: none.
        /// </remarks>
        public int MaxHp(int con)
        {
            return _c.HpBase + (int)Curve(_c.HpGain, Saturate(con, "con"), _c.HpHalfStat);
        }

        /// <summary>Melee hit-volume multiplier in permille for <paramref name="str"/>.</summary>
        /// <remarks>
        /// Pre: str >= 0. Post: 1000 &lt;= result &lt;= 1000 + MeleeVolumeGainPermille; non-decreasing. This is a
        /// volume ratio — never use it as a linear scale. Side effects: none.
        /// </remarks>
        public int MeleeVolumePermille(int str)
        {
            return Permille + (int)Curve(_c.MeleeVolumeGainPermille, Saturate(str, "str"), _c.MeleeVolumeHalfStat);
        }

        /// <summary>Linear scale (permille) applied to every melee hit-volume dimension and offset.</summary>
        /// <remarks>
        /// Pre: str >= 0. Post: L = result satisfies L³ &lt;= volume·1000² &lt; (L+1)³ with volume =
        /// MeleeVolumePermille(str); 1000 &lt;= L; non-decreasing. Side effects: none.
        /// </remarks>
        public int MeleeLinearPermille(int str)
        {
            return CubeRootFloor((long)MeleeVolumePermille(str) * Permille * Permille);
        }

        /// <summary>Ranged distance multiplier g(STR) in permille.</summary>
        /// <remarks>
        /// Pre: str >= 0. Post: 1000 &lt;= result &lt;= 1000 + RangedGainPermille; non-decreasing. Side effects: none.
        /// </remarks>
        public int RangedPermille(int str)
        {
            return Permille + (int)Curve(_c.RangedGainPermille, Saturate(str, "str"), _c.RangedHalfStat);
        }

        /// <summary>Scaled ranged reach in mm for a move with <paramref name="baseRangeMm"/>.</summary>
        /// <remarks>
        /// Pre: baseRangeMm >= 0; str >= 0. Post: 0 &lt;= result &lt;= RangedCapMm; non-decreasing in both.
        /// Side effects: none.
        /// </remarks>
        public int RangedDistanceMm(int baseRangeMm, int str)
        {
            Contract.Requires(baseRangeMm >= 0, "baseRangeMm must be nonnegative");
            long scaled = (long)baseRangeMm * RangedPermille(str) / Permille;
            return scaled > _c.RangedCapMm ? _c.RangedCapMm : (int)scaled;
        }

        /// <summary>Cooldown multiplier f(SPI) in permille.</summary>
        /// <remarks>
        /// Pre: spi >= 0. Post: CdMinPermille &lt;= result &lt;= 1000; non-increasing. Side effects: none.
        /// </remarks>
        public int CooldownPermille(int spi)
        {
            return Permille - (int)Curve(Permille - _c.CdMinPermille, Saturate(spi, "spi"), _c.CdHalfStat);
        }

        /// <summary>Effective cooldown in ms for a move with <paramref name="baseCooldownMs"/>.</summary>
        /// <remarks>
        /// Pre: baseCooldownMs >= 0; spi >= 0. Post: CdFloorMs &lt;= result &lt;= max(baseCooldownMs, CdFloorMs);
        /// non-increasing in spi. Side effects: none.
        /// </remarks>
        public int EffectiveCooldownMs(int baseCooldownMs, int spi)
        {
            Contract.Requires(baseCooldownMs >= 0, "baseCooldownMs must be nonnegative");
            // Ceil, not floor: a cooldown must never come ready earlier than the unquantized multiplier allows.
            long cd = ((long)baseCooldownMs * CooldownPermille(spi) + Permille - 1) / Permille;
            return cd < _c.CdFloorMs ? _c.CdFloorMs : (int)cd;
        }

        /// <summary>Move speed in mm/s for <paramref name="agi"/>.</summary>
        /// <remarks>
        /// Pre: agi >= 0. Post: SpeedBaseMmS &lt;= result &lt;= SpeedBaseMmS + SpeedGainMmS; non-decreasing.
        /// Side effects: none.
        /// </remarks>
        public int MoveSpeedMmS(int agi)
        {
            return _c.SpeedBaseMmS + (int)Curve(_c.SpeedGainMmS, Saturate(agi, "agi"), _c.SpeedHalfStat);
        }

        /// <summary>Evasion in permille for <paramref name="agi"/>.</summary>
        /// <remarks>
        /// Pre: agi >= 0. Post: 0 &lt;= result &lt;= EvasionCapPermille &lt; 1000; non-decreasing. Side effects: none.
        /// </remarks>
        public int EvasionPermille(int agi)
        {
            return (int)Curve(_c.EvasionCapPermille, Saturate(agi, "agi"), _c.EvasionHalfStat);
        }

        /// <summary>Hit chance (permille) of a roll that already passed the geometric test.</summary>
        /// <remarks>
        /// REQ: docs/design/combat.md#命中與閃避. Pre: 0 &lt;= accuracyPercent &lt;= 100;
        /// 0 &lt;= evasionPermille &lt;= 1000 (normally <see cref="EvasionPermille"/>; P2-09 modifiers clamp first).
        /// Post: HitFloorPermille &lt;= result &lt;= 1000; non-decreasing in accuracy, non-increasing in evasion.
        /// Side effects: none.
        /// </remarks>
        public int HitChancePermille(int accuracyPercent, int evasionPermille)
        {
            Contract.Requires(accuracyPercent >= 0 && accuracyPercent <= 100, "accuracyPercent must be 0..100");
            Contract.Requires(evasionPermille >= 0 && evasionPermille <= Permille, "evasionPermille must be 0..1000");
            int hit = accuracyPercent * (Permille - evasionPermille) / 100;
            return hit < _c.HitFloorPermille ? _c.HitFloorPermille : hit > Permille ? Permille : hit;
        }

        private int Saturate(int stat, string name)
        {
            Contract.Requires(stat >= 0, name + " must be nonnegative");
            return stat > _c.StatEffectiveCap ? _c.StatEffectiveCap : stat;
        }

        private static long Curve(int gain, int x, int halfStat)
        {
            return (long)gain * x / ((long)x + halfStat);
        }

        // Binary search instead of Math.Pow/Cbrt: floating cube roots differ in the last ulp across backends and
        // would desync host/client hit volumes. n ≤ 2000·1000² so the root ≤ 2000 and mid³ ≤ 8e9.
        private static int CubeRootFloor(long n)
        {
            long low = 0;
            long high = 2 * Permille + 1;
            while (low + 1 < high)
            {
                long mid = (low + high) / 2;
                if (mid * mid * mid <= n)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return (int)low;
        }
    }
}
