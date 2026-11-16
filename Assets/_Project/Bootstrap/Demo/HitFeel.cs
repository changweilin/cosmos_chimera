using System;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Presentation tier of one hostile hit (combat-feel.md#命中回饋分級).</summary>
    /// <remarks>Pre: none. Post: ordered by intensity (a larger value never gets weaker feedback). Side effects: none.</remarks>
    internal enum HitTier
    {
        Miss = 0,
        Immune = 1,
        Light = 2,
        Medium = 3,
        Heavy = 4,
        Knockout = 5,
    }

    /// <summary>Hit-feel numbers (暫定; presentation only, never read by rules).</summary>
    /// <remarks>Pre: none. Post: immutable. Side effects: none.</remarks>
    internal readonly struct HitFeelTuning
    {
        /// <summary>Damage ÷ max HP thresholds in permille.</summary>
        public readonly int MediumPermille;
        public readonly int HeavyPermille;

        /// <summary>Per-tier hitstop (ms) and camera shake amplitude (m), indexed by <see cref="HitTier"/>.</summary>
        private readonly int[] _hitstopMs;
        private readonly float[] _shakeM;

        /// <summary>Boss multiplier for hitstop and shake (combat-feel.md#BOSS-戰加強).</summary>
        public readonly int BossPermille;

        /// <summary>Token bucket per unit: at most this much hitstop per second.</summary>
        public readonly int BudgetMsPerSecond;

        public readonly int KnockoutSlowMoMs;
        public readonly int BossFinishSlowMoMs;
        public readonly float SlowMoTimeScale;

        /// <summary>Shake falls off linearly from full at <see cref="ShakeNearM"/> to zero at <see cref="ShakeFarM"/> from the player.</summary>
        public readonly float ShakeNearM;
        public readonly float ShakeFarM;

        private HitFeelTuning(int mediumPermille, int heavyPermille, int[] hitstopMs, float[] shakeM, int bossPermille,
            int budgetMsPerSecond, int knockoutSlowMoMs, int bossFinishSlowMoMs, float slowMoTimeScale, float shakeNearM,
            float shakeFarM)
        {
            MediumPermille = mediumPermille;
            HeavyPermille = heavyPermille;
            _hitstopMs = hitstopMs;
            _shakeM = shakeM;
            BossPermille = bossPermille;
            BudgetMsPerSecond = budgetMsPerSecond;
            KnockoutSlowMoMs = knockoutSlowMoMs;
            BossFinishSlowMoMs = bossFinishSlowMoMs;
            SlowMoTimeScale = slowMoTimeScale;
            ShakeNearM = shakeNearM;
            ShakeFarM = shakeFarM;
        }

        /// <summary>combat-feel.md table values.</summary>
        /// <remarks>Pre: none. Post: thresholds 0 &lt; Medium &lt; Heavy &lt;= 1000; budgets &gt; 0. Side effects: allocates two small arrays.</remarks>
        public static HitFeelTuning Default => new HitFeelTuning(
            mediumPermille: 50, heavyPermille: 150,
            hitstopMs: new[] { 0, 0, 0, 50, 90, 150 },
            shakeM: new[] { 0f, 0f, 0f, 0.04f, 0.09f, 0.16f },
            bossPermille: 1500, budgetMsPerSecond: 200, knockoutSlowMoMs: 300, bossFinishSlowMoMs: 600,
            slowMoTimeScale: 0.3f, shakeNearM: 10f, shakeFarM: 40f);

        /// <summary>Hitstop for <paramref name="tier"/>, before the per-unit budget.</summary>
        /// <remarks>Pre: tier defined. Post: >= 0; non-decreasing in tier; × BossPermille/1000 when boss. Side effects: none.</remarks>
        public int HitstopMs(HitTier tier, bool boss)
        {
            int ms = _hitstopMs[(int)tier];
            return boss ? ms * BossPermille / 1000 : ms;
        }

        /// <summary>Camera shake amplitude for <paramref name="tier"/> at <paramref name="distanceM"/> from the player.</summary>
        /// <remarks>Pre: tier defined; distanceM >= 0. Post: >= 0; 0 at or beyond ShakeFarM. Side effects: none.</remarks>
        public float ShakeM(HitTier tier, bool boss, float distanceM)
        {
            float amp = _shakeM[(int)tier] * (boss ? BossPermille / 1000f : 1f);
            if (distanceM <= ShakeNearM)
            {
                return amp;
            }

            if (distanceM >= ShakeFarM)
            {
                return 0f;
            }

            return amp * (ShakeFarM - distanceM) / (ShakeFarM - ShakeNearM);
        }
    }

    /// <summary>Pure classification of hostile hits into feedback tiers (combat-feel.md#命中回饋分級).</summary>
    /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
    internal static class HitFeel
    {
        /// <summary>Tier of a landed hostile hit.</summary>
        /// <remarks>
        /// Pre: maxHp >= 1; amount >= 0; resist in −100..100.
        /// Post: Immune iff dealsDamage and resist == 100; else Knockout iff knockedOut; else Heavy when
        ///       amount·1000 >= HeavyPermille·maxHp or (resist &lt; 0 and amount &gt; 0); Medium when >= MediumPermille; else Light.
        /// Side effects: none.
        /// </remarks>
        public static HitTier Classify(in HitFeelTuning tuning, int amount, int maxHp, int resist, bool dealsDamage,
            bool knockedOut)
        {
            Contract.Requires(maxHp >= 1, "maxHp must be >= 1");
            Contract.Requires(amount >= 0, "amount must be >= 0");
            Contract.Requires(resist >= -100 && resist <= 100, "resist must be -100..100");
            if (dealsDamage && resist >= 100)
            {
                return HitTier.Immune;
            }

            if (knockedOut)
            {
                return HitTier.Knockout;
            }

            long scaled = (long)amount * 1000;
            if (scaled >= (long)tuning.HeavyPermille * maxHp || (resist < 0 && amount > 0))
            {
                return HitTier.Heavy;
            }

            return scaled >= (long)tuning.MediumPermille * maxHp ? HitTier.Medium : HitTier.Light;
        }

        /// <summary>Field ticks cap at Medium so lingering fields never stutter the screen (combat-feel.md).</summary>
        /// <remarks>Pre: tier defined. Post: min(tier, Medium) unless tier is Knockout. Side effects: none.</remarks>
        public static HitTier CapForFieldTick(HitTier tier) =>
            tier == HitTier.Knockout || tier <= HitTier.Medium ? tier : HitTier.Medium;
    }

    /// <summary>
    /// Per-unit hitstop with a token-bucket budget, so AoE and field chains never exceed
    /// <see cref="HitFeelTuning.BudgetMsPerSecond"/> of frozen time per second.
    /// </summary>
    /// <remarks>
    /// Pre: unit ids are small non-negative ints (encounter unit indices); time is presentation time in ms, non-decreasing.
    /// Side effects: per member.
    /// </remarks>
    internal sealed class HitstopLedger
    {
        private readonly int _budgetMsPerSecond;
        private float[] _tokensMs = new float[16];
        private float[] _refilledAtMs = new float[16];
        private float[] _frozenUntilMs = new float[16];

        /// <summary>Creates an empty ledger; every unit starts with a full budget.</summary>
        /// <remarks>Pre: budgetMsPerSecond >= 1. Post: no unit frozen. Side effects: allocates.</remarks>
        public HitstopLedger(int budgetMsPerSecond)
        {
            Contract.Requires(budgetMsPerSecond >= 1, "budget must be >= 1");
            _budgetMsPerSecond = budgetMsPerSecond;
            for (int i = 0; i < _tokensMs.Length; i++)
            {
                _tokensMs[i] = budgetMsPerSecond;
            }
        }

        /// <summary>Freezes <paramref name="unitId"/> for up to <paramref name="requestMs"/>, limited by its remaining budget.</summary>
        /// <remarks>
        /// Pre: unitId >= 0; requestMs >= 0; nowMs non-decreasing per unit.
        /// Post: returns granted ms in 0..requestMs; total granted over any 1000 ms window &lt;= 2 × budget (bucket burst + refill);
        ///       FrozenUntil(unitId) >= nowMs + granted. Overlapping requests extend, never stack.
        /// Side effects: consumes budget; grows internal arrays when unitId is new.
        /// </remarks>
        public int Request(int unitId, float nowMs, int requestMs)
        {
            Contract.Requires(unitId >= 0, "unitId must be >= 0");
            Contract.Requires(requestMs >= 0, "requestMs must be >= 0");
            Ensure(unitId);
            float refill = (nowMs - _refilledAtMs[unitId]) * _budgetMsPerSecond / 1000f;
            if (refill > 0f)
            {
                _tokensMs[unitId] = Math.Min(_budgetMsPerSecond, _tokensMs[unitId] + refill);
                _refilledAtMs[unitId] = nowMs;
            }

            // Already frozen past now: only the extension beyond the current freeze costs budget.
            float already = Math.Max(0f, _frozenUntilMs[unitId] - nowMs);
            float extra = Math.Max(0f, requestMs - already);
            float granted = Math.Min(extra, _tokensMs[unitId]);
            _tokensMs[unitId] -= granted;
            float until = nowMs + already + granted;
            if (until > _frozenUntilMs[unitId])
            {
                _frozenUntilMs[unitId] = until;
            }

            return (int)granted;
        }

        /// <summary>Whether the unit's view is held still at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: unitId >= 0. Post: true iff nowMs &lt; the latest freeze end. Side effects: none.</remarks>
        public bool IsFrozen(int unitId, float nowMs) =>
            unitId >= 0 && unitId < _frozenUntilMs.Length && nowMs < _frozenUntilMs[unitId];

        private void Ensure(int unitId)
        {
            if (unitId < _tokensMs.Length)
            {
                return;
            }

            int size = Math.Max(unitId + 1, _tokensMs.Length * 2);
            int old = _tokensMs.Length;
            Array.Resize(ref _tokensMs, size);
            Array.Resize(ref _refilledAtMs, size);
            Array.Resize(ref _frozenUntilMs, size);
            for (int i = old; i < size; i++)
            {
                _tokensMs[i] = _budgetMsPerSecond;
            }
        }
    }
}
