using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;

namespace Cosmimera.Capture
{
    /// <summary>Capture roll for a knocked-out wild creature. REQ: lobby-meta.md#收服與蛋 (Q-M2: wild capture after knock-out).</summary>
    /// <remarks>
    /// Percentages come from Data/balance.json#capture (placeholder until P5-07, tech-debt D0-B1) and the region affix
    /// affix_bounty (+10, Terra affixes.json). Pure; the caller owns the RNG stream so every peer rolls identically.
    /// </remarks>
    public static class CaptureRules
    {
        /// <summary>Bonus of the affix_bounty region affix, percent.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int BountyBonusPercent = 10;

        /// <summary>Capture chance in percent.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= basePercent, bossPercent &lt;= 100.
        /// Post: (isBoss ? bossPercent : basePercent) + (bounty ? 10 : 0), clamped to 0..100.
        /// Side effects: none.
        /// </remarks>
        public static int ChancePercent(bool isBoss, bool bounty, int basePercent, int bossPercent)
        {
            Contract.Requires(basePercent >= 0 && basePercent <= 100 && bossPercent >= 0 && bossPercent <= 100,
                "percents must be 0..100");
            int chance = (isBoss ? bossPercent : basePercent) + (bounty ? BountyBonusPercent : 0);
            return chance > 100 ? 100 : chance;
        }

        /// <summary>Rolls one capture attempt.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= chancePercent &lt;= 100; rng seeded.
        /// Post: true with probability chancePercent/100 (exactly: draw &lt; chance over 0..99); 100 always, 0 never.
        /// Side effects: advances rng by exactly one draw.
        /// </remarks>
        public static bool Roll(int chancePercent, ref Pcg32 rng)
        {
            Contract.Requires(chancePercent >= 0 && chancePercent <= 100, "chance must be 0..100");
            return rng.NextUInt(100) < (uint)chancePercent;
        }
    }
}
