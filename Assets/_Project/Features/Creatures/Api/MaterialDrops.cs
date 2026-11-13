using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Creatures
{
    /// <summary>
    /// Evolution material drops from wild knock-outs (Q-C32 option B, 2026-11-09): the role fixes the tier and the success
    /// chance; the element is drawn uniformly from the planet type's element bias, so each planet type yields only its own
    /// elements (creatures.md#進化). Values come from <c>Data/balance.json#materials</c>.
    /// </summary>
    /// <remarks>
    /// Pre: construct with <see cref="FromJson"/>. Post: immutable. Side effects: none after construction.
    /// REQ: Data/_incoming/p2-05-material-proposal/drops-b.json — one drop per spawn's knock-out, roles never stack, no
    /// other trigger (capture, quests and region pickups give no material).
    /// </remarks>
    public sealed class MaterialDrops
    {
        /// <summary>Highest material tier the evolution data uses (creature schema: tier 1..3).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxTier = 3;

        private readonly int[] _tier = new int[3];
        private readonly int[] _chancePercent = new int[3];
        private readonly int[] _qty = new int[3];

        private MaterialDrops()
        {
        }

        /// <summary>Parses the <c>materials</c> section of balance.json.</summary>
        /// <remarks>
        /// Pre: json non-empty.
        /// Post: every role has tier 1..MaxTier, chance 0..100 and qty 1..1000.
        ///       <see cref="InvalidDataException"/> in every build when the section is missing or out of range: Contract
        ///       checks are stripped from Release, and an unchecked negative qty would turn a stockpile negative
        ///       (same failure class as tech-debt R5-01 for progression).
        /// Side effects: allocates.
        /// </remarks>
        public static MaterialDrops FromJson(string json)
        {
            Contract.Requires(!string.IsNullOrEmpty(json), "balance json must not be empty");
            Root root = JsonUtility.FromJson<Root>(json);
            Dto d = root?.materials;
            Check(d != null && d.normal != null && d.elite != null && d.boss != null,
                "balance json is missing the materials section");
            var drops = new MaterialDrops();
            drops.Set(SpawnRole.Normal, d.normal);
            drops.Set(SpawnRole.Elite, d.elite);
            drops.Set(SpawnRole.Boss, d.boss);
            return drops;
        }

        private void Set(SpawnRole role, RoleDto r)
        {
            Check(r.tier >= 1 && r.tier <= MaxTier, role + ": tier must be 1.." + MaxTier);
            Check(r.chance_percent >= 0 && r.chance_percent <= 100, role + ": chance_percent must be 0..100");
            // 1000 keeps a full-run sum far below Int32 even before MaterialInventory's own overflow check.
            Check(r.qty >= 1 && r.qty <= 1000, role + ": qty must be 1..1000");
            _tier[(int)role] = r.tier;
            _chancePercent[(int)role] = r.chance_percent;
            _qty[(int)role] = r.qty;
        }

        /// <summary>Material tier a knock-out of <paramref name="role"/> drops.</summary>
        /// <remarks>Pre: role defined. Post: 1..MaxTier. Side effects: none.</remarks>
        public int TierFor(SpawnRole role)
        {
            Contract.Requires(Enum.IsDefined(typeof(SpawnRole), role), "role must be defined");
            return _tier[(int)role];
        }

        /// <summary>Drop chance (percent) for a knock-out of <paramref name="role"/>.</summary>
        /// <remarks>Pre: role defined. Post: 0..100. Side effects: none.</remarks>
        public int ChancePercent(SpawnRole role)
        {
            Contract.Requires(Enum.IsDefined(typeof(SpawnRole), role), "role must be defined");
            return _chancePercent[(int)role];
        }

        /// <summary>Quantity of one successful drop for <paramref name="role"/>.</summary>
        /// <remarks>Pre: role defined. Post: 1..1000. Side effects: none.</remarks>
        public int QtyFor(SpawnRole role)
        {
            Contract.Requires(Enum.IsDefined(typeof(SpawnRole), role), "role must be defined");
            return _qty[(int)role];
        }

        /// <summary>Rolls the drop for one knock-out on a planet whose type prefers <paramref name="planetBias"/>.</summary>
        /// <remarks>
        /// Pre: role defined; planetBias non-null with &gt;= 1 element, every one defined (checked before the draw, so a bad
        ///      entry fails even on a roll that drops nothing — SOL W-05); every planet type has a bias, and a type without
        ///      one would make its planet drop nothing, which is a data error, not a rule. rng was seeded through a Pcg32
        ///      constructor (default(Pcg32) is not a seeded stream).
        /// Post: true iff the chance roll succeeded; then drop.Key == (planetBias[v], TierFor(role)) for a uniform v and
        ///       drop.Qty == QtyFor(role). Deterministic for a given rng state.
        /// Side effects: advances <paramref name="rng"/> by exactly one bounded draw on failure and two on success. Callers
        ///       pass a stream reserved for materials so adding drops never shifts combat or capture rolls.
        /// </remarks>
        public bool TryRoll(SpawnRole role, IReadOnlyList<Element> planetBias, ref Pcg32 rng, out MaterialStack drop) =>
            TryRoll(role, planetBias, ref rng, 0, 1, out drop);

        /// <summary>
        /// Rolls the drop for one knock-out with field-utility modifiers (<c>drop_rate_up</c> bonus and <c>magnet_pickup_double</c> multiplier).
        /// </summary>
        /// <remarks>
        /// Pre: role defined; planetBias non-null with &gt;= 1 defined element; dropRateBonusPercent &gt;= 0; pickupMultiplier &gt;= 1; rng seeded.
        /// Post: true iff the boosted chance roll succeeded; then drop.Qty == QtyFor(role) * pickupMultiplier.
        /// Side effects: advances <paramref name="rng"/> by 1 draw on failure, 2 draws on success.
        /// </remarks>
        public bool TryRoll(
            SpawnRole role,
            IReadOnlyList<Element> planetBias,
            ref Pcg32 rng,
            int dropRateBonusPercent,
            int pickupMultiplier,
            out MaterialStack drop)
        {
            Contract.Requires(Enum.IsDefined(typeof(SpawnRole), role), "role must be defined");
            Contract.Requires(planetBias != null && planetBias.Count >= 1, "planet type must have an element bias");
            Contract.Requires(dropRateBonusPercent >= 0, "dropRateBonusPercent must be >= 0");
            Contract.Requires(pickupMultiplier >= 1, "pickupMultiplier must be >= 1");
            for (int i = 0; i < planetBias.Count; i++)
            {
                Contract.Requires(Enum.IsDefined(typeof(Element), planetBias[i]), "planet bias elements must be defined");
            }

            drop = default;
            int chance = _chancePercent[(int)role] + dropRateBonusPercent;
            if (chance > 100)
            {
                chance = 100;
            }

            // Always draw the chance, even at 100%: the stream position then depends only on the knock-out sequence.
            if (rng.NextUInt(100) >= (uint)chance)
            {
                return false;
            }

            Element element = planetBias[(int)rng.NextUInt((uint)planetBias.Count)];
            drop = new MaterialStack(new MaterialKey(element, _tier[(int)role]), _qty[(int)role] * pickupMultiplier);
            return true;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException("balance materials: " + message);
            }
        }

        // Field names mirror balance.json keys because JsonUtility maps by name.
#pragma warning disable 0649, IDE1006
        [Serializable] private sealed class Root { public Dto materials; }

        [Serializable] private sealed class Dto
        {
            public RoleDto normal;
            public RoleDto elite;
            public RoleDto boss;
        }

        [Serializable] private sealed class RoleDto
        {
            public int tier;
            public int chance_percent;
            public int qty;
        }
#pragma warning restore 0649, IDE1006
    }
}
