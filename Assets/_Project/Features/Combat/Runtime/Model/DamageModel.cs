using Cosmimera.Core.Contracts;

namespace Cosmimera.Combat
{
    /// <summary>
    /// Damage rule: power × (1 + mastery%) × (1 − clamp(resist%, −100, 100)). No attack/defence stats, no global
    /// type chart; STR never enters here. REQ: docs/design/combat.md#傷害公式 (Q-C1, Q-C2).
    /// </summary>
    /// <remarks>
    /// Integer-only (Int64 intermediate, one rounding at the end) so host and clients agree bit-for-bit and the
    /// result matches the P2-02 integer conventions. Rounding (half up, minimum 1 for a non-immune hit) was
    /// approved on 2026-11-06: without the minimum, a 20-power move into 99% resist rounds to 0
    /// and reads as immunity, which only resist 100 is supposed to mean.
    /// </remarks>
    public static class DamageModel
    {
        /// <summary>Largest move power accepted. Representability bound, not balance (drafts use 20–120).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxPower = 100_000;

        /// <summary>Largest effective mastery percent accepted. Representability bound, not balance.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxMasteryPercent = 1_000;

        /// <summary>Resist magnitude the formula saturates at: +100 = immune, −100 = double damage.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int ResistClamp = 100;

        /// <summary>Damage dealt by a move of <paramref name="power"/> to a target.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= power &lt;= MaxPower; 0 &lt;= masteryPercent &lt;= MaxMasteryPercent (the attacker's effective
        /// mastery in the move's element, after trait modifiers — the caller floors negative sums at 0);
        /// resistPercent is any int (the target's effective resist in the move's element; clamped here).
        /// Post: 0 &lt;= result &lt;= MaxPower × 11 × 2;
        /// result == 0 iff power == 0 or clamp(resistPercent) == 100;
        /// non-decreasing in power and masteryPercent, non-increasing in resistPercent.
        /// Side effects: none (pure).
        /// </remarks>
        public static int Compute(int power, int masteryPercent, int resistPercent)
        {
            Contract.Requires(power >= 0 && power <= MaxPower, "power must be in 0..MaxPower");
            Contract.Requires(masteryPercent >= 0 && masteryPercent <= MaxMasteryPercent,
                "masteryPercent must be in 0..MaxMasteryPercent");

            int resist = resistPercent < -ResistClamp ? -ResistClamp
                : resistPercent > ResistClamp ? ResistClamp : resistPercent;
            if (power == 0 || resist == ResistClamp)
            {
                return 0;
            }

            // power·(100+m)·(100−r) / 10000, max 1e5·1100·200 = 2.2e10: needs Int64, result fits Int32.
            long scaled = (long)power * (100 + masteryPercent) * (100 - resist);
            long damage = (scaled + 5_000) / 10_000;
            return damage < 1 ? 1 : (int)damage;
        }

        /// <summary>Scales base move power by an environmental percentage delta (e.g. weather elemental synergy).</summary>
        /// <remarks>
        /// Pre: 0 &lt;= power &lt;= MaxPower; -90 &lt;= deltaPercent &lt;= 100.
        /// Post: 0 if power == 0; otherwise in [1, MaxPower].
        /// Side effects: none (pure).
        /// </remarks>
        public static int ScalePower(int power, int deltaPercent)
        {
            Contract.Requires(power >= 0 && power <= MaxPower, "power must be in 0..MaxPower");
            Contract.Requires(deltaPercent >= -90 && deltaPercent <= 100, "deltaPercent must be in -90..100");
            if (power == 0 || deltaPercent == 0)
            {
                return power;
            }

            long scaled = ((long)power * (100 + deltaPercent) + 50L) / 100L;
            if (scaled < 1)
            {
                return 1;
            }

            return scaled > MaxPower ? MaxPower : (int)scaled;
        }
    }
}
