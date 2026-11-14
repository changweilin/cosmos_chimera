using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Food
{
    /// <summary>One active timed food buff slot.</summary>
    /// <remarks>Pre: managed by <see cref="ActiveFoodBuffs"/>. Post: immutable snapshot. Side effects: none.</remarks>
    public readonly struct ActiveFoodBuffEntry
    {
        internal ActiveFoodBuffEntry(string foodId, FoodEffectSpec effect, long expiresMs)
        {
            FoodId = foodId;
            Effect = effect;
            ExpiresMs = expiresMs;
        }

        /// <summary>Food id that granted this buff.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string FoodId { get; }

        /// <summary>Effect specification.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public FoodEffectSpec Effect { get; }

        /// <summary>Simulation timestamp (ms) when this buff expires.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public long ExpiresMs { get; }
    }

    /// <summary>
    /// Tracks active timed food buffs for a run (max <see cref="MaxActiveBuffs"/> concurrent distinct dishes).
    /// </summary>
    /// <remarks>Pre: monotonic simulation time in CombatTime. Post: expired buffs pruned on Tick/query. Side effects: mutates buff list.</remarks>
    public sealed class ActiveFoodBuffs
    {
        /// <summary>Maximum concurrent distinct food buffs active at once.</summary>
        /// <remarks>Pre: none. Post: constant 3. Side effects: none.</remarks>
        public const int MaxActiveBuffs = 3;

        private readonly List<ActiveFoodBuffEntry> _entries = new List<ActiveFoodBuffEntry>(MaxActiveBuffs);
        private long _watermarkMs;

        /// <summary>Applies or refreshes a timed food buff at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: foodId non-empty; effect != null; durationMs in 1..EffectBook.MaxDurationMs; nowMs in CombatTime and &gt;= watermark.
        /// Post: foodId is active until nowMs + durationMs; if already active, duration is refreshed; if at MaxActiveBuffs, earliest-expiring slot is replaced.
        /// Side effects: advances watermark; mutates active entries.
        /// </remarks>
        public void Apply(string foodId, FoodEffectSpec effect, int durationMs, long nowMs)
        {
            Contract.Requires(!string.IsNullOrEmpty(foodId), "foodId must be non-empty");
            Contract.Requires(effect != null, "effect must not be null");
            Contract.Requires(durationMs >= 1 && durationMs <= EffectBook.MaxDurationMs, "durationMs must be 1..MaxDurationMs");
            CombatTime.Require(nowMs, _watermarkMs);
            _watermarkMs = nowMs;

            PruneExpired(nowMs);
            long expires = nowMs + durationMs;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].FoodId == foodId)
                {
                    _entries[i] = new ActiveFoodBuffEntry(foodId, effect, expires);
                    return;
                }
            }

            if (_entries.Count >= MaxActiveBuffs)
            {
                int oldestIndex = 0;
                for (int i = 1; i < _entries.Count; i++)
                {
                    if (_entries[i].ExpiresMs < _entries[oldestIndex].ExpiresMs)
                    {
                        oldestIndex = i;
                    }
                }

                _entries.RemoveAt(oldestIndex);
            }

            _entries.Add(new ActiveFoodBuffEntry(foodId, effect, expires));
        }

        /// <summary>Prunes expired buffs at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: nowMs in CombatTime and &gt;= watermark.
        /// Post: all remaining entries have ExpiresMs &gt; nowMs.
        /// Side effects: advances watermark; removes expired entries.
        /// </remarks>
        public void Tick(long nowMs)
        {
            CombatTime.Require(nowMs, _watermarkMs);
            _watermarkMs = nowMs;
            PruneExpired(nowMs);
        }

        /// <summary>Returns the number of currently active (non-expired) buffs at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: nowMs in CombatTime and &gt;= watermark. Post: 0..MaxActiveBuffs. Side effects: none.</remarks>
        public int CountActive(long nowMs)
        {
            CombatTime.Require(nowMs, _watermarkMs);
            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].ExpiresMs > nowMs)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Aggregated capture rate bonus (percentage points) from active food buffs at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: nowMs in CombatTime and &gt;= watermark. Post: &gt;= 0. Side effects: none.</remarks>
        public int GetCatchRateBonusPercent(long nowMs)
        {
            CombatTime.Require(nowMs, _watermarkMs);
            int sum = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].ExpiresMs > nowMs)
                {
                    sum += _entries[i].Effect.CatchRateBonusPercent;
                }
            }

            return sum;
        }

        /// <summary>Aggregated drop rate bonus (percentage points) from active food buffs at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: nowMs in CombatTime and &gt;= watermark. Post: &gt;= 0. Side effects: none.</remarks>
        public int GetDropRateBonusPercent(long nowMs)
        {
            CombatTime.Require(nowMs, _watermarkMs);
            int sum = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].ExpiresMs > nowMs)
                {
                    sum += _entries[i].Effect.DropRateBonusPercent;
                }
            }

            return sum;
        }

        /// <summary>Aggregated player dodge-roll cooldown modifier (%) from active food buffs at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: nowMs in CombatTime and &gt;= watermark. Post: clamped to -50..100. Side effects: none.</remarks>
        public int GetRollCooldownModPercent(long nowMs)
        {
            CombatTime.Require(nowMs, _watermarkMs);
            int sum = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].ExpiresMs > nowMs)
                {
                    sum += _entries[i].Effect.RollCooldownModPercent;
                }
            }

            if (sum < -50) return -50;
            if (sum > 100) return 100;
            return sum;
        }

        /// <summary>Clears all active buffs.</summary>
        /// <remarks>Pre: none. Post: CountActive == 0. Side effects: clears internal list.</remarks>
        public void Clear()
        {
            _entries.Clear();
        }

        private void PruneExpired(long nowMs)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].ExpiresMs <= nowMs)
                {
                    _entries.RemoveAt(i);
                }
            }
        }
    }
}
