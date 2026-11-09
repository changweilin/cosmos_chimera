using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Creatures
{
    /// <summary>Where an event's main XP pool comes from. REQ: level-progression.md#獎勵量（Q-C29／Q-C30 定案）.</summary>
    /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
    public enum XpSource
    {
        Wild = 0, Capture = 1, QuestStep = 2, QuestChain = 3, Boss = 4,
    }

    /// <summary>One reward recipient of an event: a main (Owner == -1) or a sidekick of mains[Owner].</summary>
    /// <remarks>
    /// Pre: InstanceId &gt;= 1; Exp &gt;= 0 (cumulative XP); Appeared only meaningful for mains.
    /// Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct XpRecipient
    {
        /// <summary>Creates a recipient.</summary>
        /// <remarks>Pre: see type. Post: properties equal the arguments. Side effects: none.</remarks>
        public XpRecipient(int instanceId, int exp, bool dead, bool appeared, int owner)
        {
            Contract.Requires(instanceId >= 1, "instance id must be >= 1");
            Contract.Requires(exp >= 0, "exp must be >= 0");
            InstanceId = instanceId;
            Exp = exp;
            Dead = dead;
            Appeared = appeared;
            Owner = owner;
        }

        /// <summary>Persistent instance id (also the largest-remainder tie-break key).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public int InstanceId { get; }

        /// <summary>Cumulative XP before this event.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public int Exp { get; }

        /// <summary>Fainted when the event settles (gets nothing, leaves the denominator).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public bool Dead { get; }

        /// <summary>A main that appeared in this event (weight 4 instead of 2).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public bool Appeared { get; }

        /// <summary>-1 for a main; otherwise the index (in the recipient list) of the main this sidekick belongs to.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public int Owner { get; }
    }

    /// <summary>Result of <see cref="Progression.Settle"/>: XP actually credited and the loss ledger.</summary>
    /// <remarks>Pre: none. Post: immutable. Side effects: none.</remarks>
    public sealed class XpSettlement
    {
        internal XpSettlement(int[] paid, int[] assigned, long mainLoss, long sidekickEntitlementHalves, long sidekickPaid)
        {
            // Read-only wrappers: a raw int[] behind IReadOnlyList can be cast back and mutated (SOL P2-12 R7).
            Paid = Array.AsReadOnly(paid);
            Assigned = Array.AsReadOnly(assigned);
            MainLoss = mainLoss;
            SidekickEntitlementHalves = sidekickEntitlementHalves;
            SidekickPaid = sidekickPaid;
        }

        /// <summary>XP credited per recipient (same order as the input), after the LV100 cap.</summary>
        /// <remarks>Pre: none. Post: each &gt;= 0 and min(Exp, MaxExp) + Paid &lt;= MaxExp. Side effects: none.</remarks>
        public IReadOnlyList<int> Paid { get; }

        /// <summary>XP assigned per recipient before the cap (mains: integer share; sidekicks: group share).</summary>
        /// <remarks>Pre: none. Post: each &gt;= Paid. Side effects: none.</remarks>
        public IReadOnlyList<int> Assigned { get; }

        /// <summary>Pool XP not credited to any main (all dead, or capped shares).</summary>
        /// <remarks>Pre: none. Post: sum(main Paid) + MainLoss == pool. Side effects: none.</remarks>
        public long MainLoss { get; }

        /// <summary>Twice the exact sidekick entitlement (sum of main actual / 2 over mains with a living sidekick).</summary>
        /// <remarks>Pre: none. Post: &gt;= 2 × SidekickPaid. Side effects: none.</remarks>
        public long SidekickEntitlementHalves { get; }

        /// <summary>Total XP credited to sidekicks (extra issuance, never taken from the pool).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public long SidekickPaid { get; }
    }

    /// <summary>
    /// Approved level curve and option-A XP rewards (2026-11-09), read from <c>Data/balance.json#progression</c>.
    /// REQ: docs/design/level-progression.md. Python reference: tools/balance/level_rewards.py (golden vectors in
    /// tools/balance/golden_level_rewards.json).
    /// </summary>
    /// <remarks>
    /// Integer-only so every client and the reference agree bit for bit (no float pow at runtime).
    /// Pre: construct with <see cref="FromJson"/>. Post: immutable. Side effects: none after construction.
    /// </remarks>
    public sealed class Progression
    {
        /// <summary>Lowest level.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MinLevel = 1;

        private const int MaxMains = 6;

        private readonly int[] _thresholds; // index = level, [1] = 0
        private readonly int[] _costs; // index = level, cost to reach level + 1; [MaxLevel] = 0
        private readonly int[] _floorLevels;
        private readonly int[] _baseXp;
        private readonly int _referenceLevel;
        private readonly int _eliteMultiplier;
        private readonly int _levelBase;
        private readonly int _levelPerDifficulty;
        private readonly int _levelPerTier;
        private readonly int[] _roleBonus;
        private readonly int _appearedWeight;
        private readonly int _reserveWeight;
        private readonly int _sidekickDivisor;

        private Progression(Dto d)
        {
            MaxLevel = d.level_max;
            StarterLevel = d.starter_level;
            _thresholds = new int[MaxLevel + 1];
            _costs = new int[MaxLevel + 1];
            BigInteger num = BigInteger.One;
            BigInteger den = BigInteger.One;
            long total = 0;
            for (int level = 1; level < MaxLevel; level++)
            {
                // Each cost is ceil(first × (n/d)^(L−1)) from the exact ratio, never the previous rounded cost × 1.05:
                // compounding the rounding would drift from the approved table (level-progression.md#指數經驗曲線).
                BigInteger scaled = d.xp_first_level_cost * num;
                BigInteger cost = BigInteger.Divide(scaled + den - 1, den);
                Check(cost <= int.MaxValue, "xp cost exceeds Int32");
                _costs[level] = (int)cost;
                total += (int)cost;
                Check(total <= int.MaxValue, "cumulative xp exceeds Int32");
                _thresholds[level + 1] = (int)total;
                num *= d.xp_growth_numerator;
                den *= d.xp_growth_denominator;
            }

            _floorLevels = d.migration_stage_floor_levels;
            RewardsDto r = d.rewards;
            _baseXp = new[] { r.base_xp.wild, r.base_xp.capture, r.base_xp.quest_step, r.base_xp.quest_chain, r.base_xp.boss };
            _referenceLevel = r.reference_level;
            _eliteMultiplier = r.elite_multiplier;
            _levelBase = r.wild_level.@base;
            _levelPerDifficulty = r.wild_level.per_difficulty;
            _levelPerTier = r.wild_level.per_tier;
            _roleBonus = new[] { r.wild_level.role_bonus.normal, r.wild_level.role_bonus.elite, r.wild_level.role_bonus.boss };
            _appearedWeight = d.appeared_weight;
            _reserveWeight = d.reserve_weight;
            _sidekickDivisor = r.sidekick_share_divisor;
        }

        /// <summary>Highest level (100).</summary>
        /// <remarks>Pre: none. Post: &gt;= 2. Side effects: none.</remarks>
        public int MaxLevel { get; }

        /// <summary>Level of a newly granted starter (10).</summary>
        /// <remarks>Pre: none. Post: 1..MaxLevel. Side effects: none.</remarks>
        public int StarterLevel { get; }

        /// <summary>Cumulative XP cap, T(MaxLevel); XP beyond it is discarded, never banked.</summary>
        /// <remarks>Pre: none. Post: == XpThreshold(MaxLevel). Side effects: none.</remarks>
        public int MaxExp => _thresholds[MaxLevel];

        /// <summary>Parses the <c>progression</c> section of balance.json.</summary>
        /// <remarks>
        /// Pre: json is Data/balance.json content with a complete progression section (curve growth &gt; 1, positive
        ///      costs, 3 stage floors inside 1..MaxLevel, 5 base XP values &gt;= 0, elite multiplier 1..100, every
        ///      source's reward at C(MaxLevel−1) within Int32, weights &gt;= 1, divisor 2).
        /// Post: thresholds strictly increase and fit Int32. <see cref="InvalidDataException"/> on any violation, in
        ///       every build: these are data checks, and a Release build that strips them would let an unapproved
        ///       config make SourceXp negative (tech-debt R5-01).
        /// Side effects: allocates.
        /// </remarks>
        public static Progression FromJson(string json)
        {
            Check(!string.IsNullOrEmpty(json), "balance json must not be empty");
            Root root = JsonUtility.FromJson<Root>(json);
            Dto d = root?.progression;
            Check(d != null && d.migration_stage_floor_levels != null && d.rewards != null &&
                d.rewards.base_xp != null && d.rewards.wild_level != null && d.rewards.wild_level.role_bonus != null,
                "balance json is missing the progression section");
            Check(d.level_max >= 2 && d.level_max <= 1000, "level_max out of range");
            Check(d.starter_level >= MinLevel && d.starter_level <= d.level_max, "starter_level out of range");
            Check(d.xp_first_level_cost >= 1 && d.xp_growth_denominator >= 1 &&
                d.xp_growth_numerator > d.xp_growth_denominator, "xp curve must start >= 1 and grow");
            Check(d.migration_stage_floor_levels.Length == 3, "three stage floor levels are required");
            foreach (int f in d.migration_stage_floor_levels)
            {
                Check(f >= MinLevel && f <= d.level_max, "stage floor level out of range");
            }

            RewardsDto r = d.rewards;
            Check(r.base_xp.wild >= 0 && r.base_xp.capture >= 0 && r.base_xp.quest_step >= 0 &&
                r.base_xp.quest_chain >= 0 && r.base_xp.boss >= 0, "base xp must be >= 0");
            Check(r.reference_level >= MinLevel && r.reference_level < d.level_max, "reference_level out of range");
            // Same envelope as tools/balance/level_rewards.py (SOL P2-12 R5): C# and Python must accept the same configs.
            Check(r.elite_multiplier >= 1 && r.elite_multiplier <= 100, "elite_multiplier must be 1..100");
            // The ledger counts sidekick entitlement in half points; another divisor needs a different ledger unit.
            Check(r.sidekick_share_divisor == 2, "only the approved half share (divisor 2) is supported");
            Check(d.appeared_weight >= 1 && d.reserve_weight >= 1, "weights must be >= 1");
            var progression = new Progression(d);
            // SourceXp is monotonic in level and elite, so its largest value per source is at C(MaxLevel−1) with the
            // multiplier. Bounding that by Int32 here also bounds SourceXp's Int64 product (< 2^31 · C(ref) < 2^62), so
            // SourceXp can never wrap negative at run time (SOL P2-12 R5: base and elite at Int32.Max gave −1982292521).
            BigInteger maxCost = progression._costs[progression.MaxLevel - 1];
            for (int s = 0; s < progression._baseXp.Length; s++)
            {
                int multiplier = s == (int)XpSource.Wild ? progression._eliteMultiplier : 1;
                BigInteger top = progression._baseXp[s] * maxCost * multiplier / progression._costs[progression._referenceLevel];
                Check(top <= int.MaxValue, "base xp makes a source reward exceed Int32 at the level cap");
            }

            return progression;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException("balance progression: " + message);
            }
        }


        /// <summary>Minimum cumulative XP for <paramref name="level"/>, T(L).</summary>
        /// <remarks>Pre: 1 &lt;= level &lt;= MaxLevel. Post: T(1) == 0; strictly increasing. Side effects: none.</remarks>
        public int XpThreshold(int level)
        {
            Contract.Requires(level >= MinLevel && level <= MaxLevel, "level out of range");
            return _thresholds[level];
        }

        /// <summary>XP needed to go from <paramref name="level"/> to the next, C(L).</summary>
        /// <remarks>Pre: 1 &lt;= level &lt; MaxLevel. Post: &gt;= 1. Side effects: none.</remarks>
        public int XpToNext(int level)
        {
            Contract.Requires(level >= MinLevel && level < MaxLevel, "level out of range");
            return _costs[level];
        }

        /// <summary>Level reached with <paramref name="exp"/> cumulative XP.</summary>
        /// <remarks>
        /// Pre: exp &gt;= 0. Post: largest L with T(L) &lt;= exp; MaxLevel for exp &gt;= MaxExp. Side effects: none.
        /// </remarks>
        public int LevelOf(int exp)
        {
            Contract.Requires(exp >= 0, "exp must be >= 0");
            int index = Array.BinarySearch(_thresholds, 1, MaxLevel, exp);
            return index >= 0 ? index : ~index - 1;
        }

        /// <summary>
        /// Cumulative XP a pre-level save (format 1) is migrated to: T(stage floor 10/20/35). Old evolution XP is not
        /// converted (option A), because its meaning differs from cumulative level XP.
        /// </summary>
        /// <remarks>Pre: stage defined. Post: XpThreshold(floor[stage]). Side effects: none.</remarks>
        public int MigrationExp(CreatureStage stage)
        {
            Contract.Requires(Enum.IsDefined(typeof(CreatureStage), stage), "stage must be defined");
            return _thresholds[_floorLevels[(int)stage]];
        }

        /// <summary>Level of a wild encounter: clamp(10 + 10(d−1) + 2t + role bonus 0/3/5, 1, MaxLevel).</summary>
        /// <remarks>
        /// Pre: 1 &lt;= difficulty &lt;= 10; 0 &lt;= tier &lt;= 2; role defined. Post: 1..MaxLevel. Side effects: none.
        /// </remarks>
        public int WildLevel(int difficulty, int tier, WorldGen.SpawnRole role)
        {
            Contract.Requires(difficulty >= 1 && difficulty <= 10, "difficulty must be 1..10");
            Contract.Requires(tier >= 0 && tier <= 2, "tier must be 0..2");
            Contract.Requires(Enum.IsDefined(typeof(WorldGen.SpawnRole), role), "role must be defined");
            int level = _levelBase + _levelPerDifficulty * (difficulty - 1) + _levelPerTier * tier + _roleBonus[(int)role];
            return Math.Min(MaxLevel, Math.Max(MinLevel, level));
        }

        /// <summary>
        /// One event's main pool: floor(base × C(min(L, MaxLevel−1)) × (elite ? 2 : 1) / C(reference level)).
        /// A boss defeat is one Boss event only (no extra Wild event); capture is never role-multiplied.
        /// </summary>
        /// <remarks>
        /// Pre: source defined; 1 &lt;= level &lt;= MaxLevel; elite only with XpSource.Wild.
        /// Post: 0..Int32.MaxValue. Side effects: none.
        /// </remarks>
        public int SourceXp(XpSource source, int level, bool elite)
        {
            Contract.Requires(Enum.IsDefined(typeof(XpSource), source), "source must be defined");
            Contract.Requires(level >= MinLevel && level <= MaxLevel, "level out of range");
            Contract.Requires(!elite || source == XpSource.Wild, "only a wild defeat can be elite");
            // LV100 has no next-level cost; the last positive cost C(99) stands in (SOL rewards.json option A).
            long cost = _costs[Math.Min(level, MaxLevel - 1)];
            long result = _baseXp[(int)source] * cost * (elite ? _eliteMultiplier : 1) / _costs[_referenceLevel];
            Contract.Requires(result <= int.MaxValue, "source xp exceeds Int32");
            return (int)result;
        }

        /// <summary>
        /// Settles one event: living mains split <paramref name="pool"/> by weight 4 (appeared) / 2 (reserve) with
        /// largest-remainder rounding (ties: ascending instance id); each main's living sidekicks share
        /// floor(main actual / 2) equally on top. Every credit is capped at <see cref="MaxExp"/> and the excess is lost.
        /// </summary>
        /// <remarks>
        /// Pre: pool &gt;= 0; 1..6 mains (Owner == -1) listed before their sidekicks; each sidekick's Owner indexes a
        ///      main; instance ids &gt;= 1 and unique; Exp &gt;= 0.
        /// Post: sum(main Paid) + MainLoss == pool; a dead recipient gets 0; when any main lives,
        ///       sum(main Assigned) == pool; min(Exp, MaxExp) + Paid &lt;= MaxExp for every recipient (Exp
        ///       above MaxExp earns nothing); identical inputs give identical outputs.
        /// Side effects: allocates the result.
        /// </remarks>
        public XpSettlement Settle(int pool, IReadOnlyList<XpRecipient> recipients)
        {
            Contract.Requires(pool >= 0, "pool must be >= 0");
            Contract.Requires(recipients != null, "recipients are required");
            int mains = 0;
            var ids = new HashSet<int>();
            for (int i = 0; i < recipients.Count; i++)
            {
                XpRecipient r = recipients[i];
                Contract.Requires(r.InstanceId >= 1 && ids.Add(r.InstanceId), "instance ids must be >= 1 and unique");
                Contract.Requires(r.Exp >= 0, "exp must be >= 0");
                if (r.Owner == -1)
                {
                    Contract.Requires(mains == i, "mains must come before sidekicks");
                    mains++;
                }
                else
                {
                    Contract.Requires(r.Owner >= 0 && r.Owner < mains, "sidekick owner must index a main");
                }
            }

            Contract.Requires(mains >= 1 && mains <= MaxMains, "1..6 mains are required");
            int n = recipients.Count;
            var assigned = new int[n];
            var paid = new int[n];

            long weightSum = 0;
            var weights = new long[mains];
            for (int i = 0; i < mains; i++)
            {
                XpRecipient r = recipients[i];
                weights[i] = r.Dead ? 0 : r.Appeared ? _appearedWeight : _reserveWeight;
                weightSum += weights[i];
            }

            long mainPaid = 0;
            if (weightSum > 0)
            {
                var remainders = new long[mains];
                long given = 0;
                for (int i = 0; i < mains; i++)
                {
                    long exact = pool * weights[i];
                    assigned[i] = (int)(exact / weightSum);
                    remainders[i] = exact % weightSum;
                    given += assigned[i];
                }

                GiveRemainder(recipients, assigned, remainders, 0, mains, pool - given);
                for (int i = 0; i < mains; i++)
                {
                    paid[i] = Credit(recipients[i].Exp, assigned[i]);
                    mainPaid += paid[i];
                }
            }

            long entitlementHalves = 0;
            long sidekickPaid = 0;
            var living = new List<int>();
            for (int m = 0; m < mains; m++)
            {
                living.Clear();
                for (int i = mains; i < n; i++)
                {
                    if (recipients[i].Owner == m && !recipients[i].Dead)
                    {
                        living.Add(i);
                    }
                }

                if (living.Count == 0)
                {
                    continue;
                }

                // REQ: option A — the group shares floor(actual / 2); the dropped half point stays in the ledger.
                entitlementHalves += paid[m] * 2L / _sidekickDivisor;
                long budget = paid[m] / _sidekickDivisor;
                var remainders = new long[living.Count];
                long given = 0;
                for (int k = 0; k < living.Count; k++)
                {
                    assigned[living[k]] = (int)(budget / living.Count);
                    remainders[k] = budget % living.Count;
                    given += assigned[living[k]];
                }

                // Equal remainders: the largest-remainder pass reduces to ascending instance id.
                var sub = new List<XpRecipient>(living.Count);
                var subAssigned = new int[living.Count];
                for (int k = 0; k < living.Count; k++)
                {
                    sub.Add(recipients[living[k]]);
                    subAssigned[k] = assigned[living[k]];
                }

                GiveRemainder(sub, subAssigned, remainders, 0, living.Count, budget - given);
                for (int k = 0; k < living.Count; k++)
                {
                    int i = living[k];
                    assigned[i] = subAssigned[k];
                    paid[i] = Credit(recipients[i].Exp, assigned[i]);
                    sidekickPaid += paid[i];
                }
            }

            return new XpSettlement(paid, assigned, pool - mainPaid, entitlementHalves, sidekickPaid);
        }

        private int Credit(int exp, int amount)
        {
            int stored = Math.Min(exp, MaxExp);
            return Math.Min(amount, MaxExp - stored);
        }

        private static void GiveRemainder(IReadOnlyList<XpRecipient> who, int[] assigned, long[] remainders, int from,
            int count, long left)
        {
            if (left <= 0)
            {
                return;
            }

            var order = new List<int>(count);
            for (int i = from; i < from + count; i++)
            {
                order.Add(i);
            }

            // Larger remainder first; equal remainders go to the smaller persistent instance id (rewards.json
            // rounding_tie_break), never party order, so reordering the party cannot change who gets the extra point.
            order.Sort((a, b) =>
            {
                int c = remainders[b - from].CompareTo(remainders[a - from]);
                return c != 0 ? c : who[a].InstanceId.CompareTo(who[b].InstanceId);
            });
            for (int k = 0; k < left; k++)
            {
                assigned[order[k]]++;
            }
        }

        // Field names mirror balance.json keys because JsonUtility maps by name.
#pragma warning disable 0649, IDE1006
        [Serializable] private sealed class Root { public Dto progression; }

        [Serializable] private sealed class Dto
        {
            public int level_max;
            public int starter_level;
            public int xp_first_level_cost;
            public int xp_growth_numerator;
            public int xp_growth_denominator;
            public int[] migration_stage_floor_levels;
            public int appeared_weight;
            public int reserve_weight;
            public RewardsDto rewards;
        }

        [Serializable] private sealed class RewardsDto
        {
            public BaseXpDto base_xp;
            public int reference_level;
            public int elite_multiplier;
            public WildLevelDto wild_level;
            public int sidekick_share_divisor;
        }

        [Serializable] private sealed class BaseXpDto
        {
            public int wild;
            public int capture;
            public int quest_step;
            public int quest_chain;
            public int boss;
        }

        [Serializable] private sealed class WildLevelDto
        {
            public int @base;
            public int per_difficulty;
            public int per_tier;
            public RoleBonusDto role_bonus;
        }

        [Serializable] private sealed class RoleBonusDto { public int normal; public int elite; public int boss; }
#pragma warning restore 0649, IDE1006
    }
}
