using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Combat
{
    /// <summary>
    /// Exponential damage rule: power × 2^((attack − defence + mastery − resist + 2·levelDiff) / 50).
    /// REQ: docs/design/combat.md#傷害公式.
    /// </summary>
    /// <remarks>
    /// Integer-only fixed-point (2^20 scale with a 50-entry table and one half-up rounding at the end) so host and
    /// clients agree bit-for-bit without floating-point pow drift. Non-zero power always deals at least 1 damage.
    /// </remarks>
    public static class DamageModel
    {
        /// <summary>Largest move power accepted. Representability bound, not balance (drafts use 20–120).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxPower = 100_000;

        /// <summary>Largest effective mastery value accepted. Representability bound, not balance.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxMasteryPercent = 1_000;

        /// <summary>Maximum absolute effective resist accepted by Combatant/EffectMath.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int ResistClamp = 300;

        /// <summary>Neutral baseline for attack, defence, mastery, and resist (multiplier = 1×).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int NeutralStat = 100;

        // round(2^(r / 50) * 2^20) for r = 0..49.
        private static readonly long[] Pow2StepTable =
        {
            1048576, 1063214, 1078055, 1093105, 1108364, 1123836, 1139524, 1155431, 1171560, 1187915,
            1204498, 1221312, 1238361, 1255647, 1273176, 1290948, 1308969, 1327242, 1345770, 1364556,
            1383604, 1402919, 1422503, 1442360, 1462495, 1482910, 1503611, 1524601, 1545883, 1567463,
            1589344, 1611530, 1634027, 1656837, 1679965, 1703417, 1727196, 1751306, 1775754, 1800542,
            1825677, 1851162, 1877004, 1903206, 1929773, 1956712, 1984027, 2011723, 2039805, 2068280,
        };

        /// <summary>Damage dealt by a move of <paramref name="power"/> when attack equals defence and levelDiff is 0.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= power &lt;= MaxPower; 0 &lt;= masteryPercent &lt;= MaxMasteryPercent; resistPercent in -ResistClamp..ResistClamp.
        /// Post: 0 iff power == 0, else >= 1; non-decreasing in power and masteryPercent, non-increasing in resistPercent.
        /// Side effects: none (pure).
        /// </remarks>
        public static int Compute(int power, int masteryPercent, int resistPercent)
        {
            return Compute(power, masteryPercent, resistPercent, NeutralStat, NeutralStat, 0);
        }

        /// <summary>Damage dealt by a move of <paramref name="power"/> using exponential atk/def, mastery, resist, and levelDiff.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= power &lt;= MaxPower; 0 &lt;= masteryPercent &lt;= MaxMasteryPercent;
        ///      resistPercent in -ResistClamp..ResistClamp; attack >= 0; defence >= 0.
        /// Post: 0 iff power == 0, else >= 1 (capped at int.MaxValue);
        ///       approximates round_half_up(power × 2^((attack − defence + masteryPercent − resistPercent + 2·levelDiff) / 50));
        ///       non-decreasing in power, masteryPercent, attack, levelDiff; non-increasing in resistPercent, defence.
        /// Side effects: none (pure).
        /// </remarks>
        public static int Compute(int power, int masteryPercent, int resistPercent, int attack, int defence, int levelDiff = 0,
            int attackerLevelPpm = LevelCurve.PpmOne)
        {
            Contract.Requires(attackerLevelPpm > 0 && attackerLevelPpm <= LevelCurve.PpmOne,
                "attackerLevelPpm must be in 1..LevelCurve.PpmOne");
            Contract.Requires(power >= 0 && power <= MaxPower, "power must be in 0..MaxPower");
            Contract.Requires(masteryPercent >= 0 && masteryPercent <= MaxMasteryPercent,
                "masteryPercent must be in 0..MaxMasteryPercent");
            Contract.Requires(attack >= 0 && defence >= 0, "attack and defence must be nonnegative");
            if (power == 0)
            {
                return 0;
            }

            int resist = resistPercent < -ResistClamp ? -ResistClamp
                : resistPercent > ResistClamp ? ResistClamp : resistPercent;
            long delta = (long)attack - defence + masteryPercent - resist + (long)levelDiff * 2;
            long k = delta >= 0 ? delta / 50 : (delta - 49) / 50;
            int r = (int)(delta - k * 50);

            if (k >= 20)
            {
                return int.MaxValue;
            }

            if (k <= -30)
            {
                return 1;
            }

            long num = (long)power * Pow2StepTable[r];
            if (attackerLevelPpm != LevelCurve.PpmOne)
            {
                // Why: base damage follows the attacker's level on the same curve as max HP (LevelCurve), so the
                // damage/HP ratio of a matchup depends on the level difference only. Max num 1e5 × 2^21 × 1e6 fits Int64.
                num = (num * attackerLevelPpm + LevelCurve.PpmOne / 2) / LevelCurve.PpmOne;
            }

            int shift = 20 - (int)k; // 1..49: fits Int64 without shift overflow.
            long damage = (num + (1L << (shift - 1))) >> shift;
            return damage < 1 ? 1 : damage > int.MaxValue ? int.MaxValue : (int)damage;
        }

        /// <summary>Largest crit multiplier accepted, percent. Representability bound, not balance (base 150).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxCritDamagePercent = 1_000;

        /// <summary>Applies a critical hit's multiplier. REQ: docs/design/combat.md#暴擊.</summary>
        /// <remarks>
        /// Pre: damage >= 0; 100 &lt;= critDamagePercent &lt;= MaxCritDamagePercent.
        /// Post: round_half_up(damage × critDamagePercent / 100) capped at int.MaxValue; >= damage; 0 iff damage == 0.
        /// Side effects: none (pure).
        /// </remarks>
        public static int ApplyCrit(int damage, int critDamagePercent)
        {
            Contract.Requires(damage >= 0, "damage must be nonnegative");
            Contract.Requires(critDamagePercent >= 100 && critDamagePercent <= MaxCritDamagePercent,
                "critDamagePercent must be 100..MaxCritDamagePercent");
            long scaled = ((long)damage * critDamagePercent + 50) / 100;
            return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
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

        /// <summary>Scales power by weather, then by a weapon multiplier in permille.</summary>
        /// <remarks>
        /// REQ: docs/design/crafting.md#武器數值模型 — the weapon attack multiplier multiplies move power.
        /// Pre: as <see cref="ScalePower(int, int)"/>; 1 &lt;= weaponPermille &lt;= MaxWeaponPermille.
        /// Post: 0 if power == 0; otherwise in [1, MaxPower]; equals ScalePower(power, deltaPercent) when weaponPermille == 1000.
        /// Side effects: none (pure).
        /// </remarks>
        public static int ScalePower(int power, int deltaPercent, int weaponPermille)
        {
            Contract.Requires(weaponPermille >= 1 && weaponPermille <= MaxWeaponPermille, "weaponPermille must be 1..MaxWeaponPermille");
            int weathered = ScalePower(power, deltaPercent);
            if (weathered == 0 || weaponPermille == 1000)
            {
                return weathered;
            }

            long scaled = ((long)weathered * weaponPermille + 500L) / 1000L;
            if (scaled < 1)
            {
                return 1;
            }

            return scaled > MaxPower ? MaxPower : (int)scaled;
        }

        /// <summary>Upper bound of a weapon power multiplier (permille), grip bonus included.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxWeaponPermille = 10_000;
    }
}
