using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Draws a planet's main and sub-elements and their percentage split from seed, size, and planet-type bias.
    /// REQ: docs/design/planet-world.md#星球屬性.
    /// </summary>
    /// <remarks>
    /// Own stream Pcg32(seed, 'E') so element composition never shifts 'B', 'R', 'T', 'S', 'G', or 'L'.
    /// </remarks>
    public static class PlanetElementDraw
    {
        private const int MaxSubPercent = 35;
        private const int ElementCount = 10;

        /// <summary>Draws main/sub-elements and their percentages for one planet.</summary>
        /// <remarks>
        /// Pre: <paramref name="size"/> is a defined <see cref="PlanetSize"/>; <paramref name="elementBias"/> is non-null
        ///      and every entry is a defined <see cref="Element"/>.
        /// Post: MainCount == 1 for Asteroid/Moon/Planet, 1..2 for Supergiant;
        ///       SubCount == 0 for Asteroid, 0..1 for Moon, 0..2 for Planet/Supergiant;
        ///       SubPercentTotal == 0 iff SubCount == 0, else 1..35 (at least SubCount so each sub-element >= 1);
        ///       MainPercentTotal == 100 - SubPercentTotal; every share has Percent >= 1 and all Percents sum to 100;
        ///       main elements prefer <paramref name="elementBias"/> before falling back to other elements;
        ///       same inputs → same output.
        /// Side effects: allocates the returned <see cref="PlanetElements"/>.
        /// </remarks>
        public static PlanetElements Draw(ulong seed, PlanetSize size, IReadOnlyList<Element> elementBias)
        {
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), size), "size must be defined");
            Contract.Requires(elementBias != null, "elementBias must not be null");
            for (int i = 0; i < elementBias.Count; i++)
            {
                Contract.Requires(Enum.IsDefined(typeof(Element), elementBias[i]), "elementBias entries must be defined");
            }

            var rng = new Pcg32(seed, 'E');
            (int mainCount, int subCount) = CountsFor(size, ref rng);

            var biasPool = new List<Element>(elementBias.Count);
            for (int i = 0; i < elementBias.Count; i++)
            {
                if (!biasPool.Contains(elementBias[i]))
                {
                    biasPool.Add(elementBias[i]);
                }
            }

            var mainPicked = new List<Element>(mainCount);
            PickUnique(biasPool, mainCount, mainPicked, ref rng);

            if (mainPicked.Count < mainCount)
            {
                List<Element> fallbackPool = RemainingElements(mainPicked);
                PickUnique(fallbackPool, mainCount - mainPicked.Count, mainPicked, ref rng);
            }

            var subPicked = new List<Element>(subCount);
            if (subCount > 0)
            {
                List<Element> subPool = RemainingElements(mainPicked);
                PickUnique(subPool, subCount, subPicked, ref rng);
            }

            int subTotal = subCount == 0
                ? 0
                : subCount + (int)rng.NextUInt((uint)(MaxSubPercent - subCount + 1));
            int mainTotal = 100 - subTotal;

            int[] mainPercents = SplitTotal(mainTotal, mainCount, ref rng);
            int[] subPercents = SplitTotal(subTotal, subCount, ref rng);

            var shares = new PlanetElementShare[mainCount + subCount];
            for (int i = 0; i < mainCount; i++)
            {
                shares[i] = new PlanetElementShare(mainPicked[i], mainPercents[i], isMain: true);
            }

            for (int i = 0; i < subCount; i++)
            {
                shares[mainCount + i] = new PlanetElementShare(subPicked[i], subPercents[i], isMain: false);
            }

            SortDescending(shares, 0, mainCount);
            SortDescending(shares, mainCount, subCount);

            return new PlanetElements(shares, mainCount, subCount, mainTotal, subTotal);
        }

        private static (int mainCount, int subCount) CountsFor(PlanetSize size, ref Pcg32 rng)
        {
            switch (size)
            {
                case PlanetSize.Asteroid:
                    return (1, 0);
                case PlanetSize.Moon:
                    return (1, (int)rng.NextUInt(2));
                case PlanetSize.Planet:
                    return (1, (int)rng.NextUInt(3));
                case PlanetSize.Supergiant:
                    return (1 + (int)rng.NextUInt(2), (int)rng.NextUInt(3));
                default:
                    throw new ArgumentOutOfRangeException(nameof(size), size, "undefined PlanetSize");
            }
        }

        private static void PickUnique(List<Element> pool, int count, List<Element> dst, ref Pcg32 rng)
        {
            int take = Math.Min(count, pool.Count);
            for (int i = 0; i < take; i++)
            {
                int idx = (int)rng.NextUInt((uint)pool.Count);
                dst.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
        }

        private static List<Element> RemainingElements(List<Element> exclude)
        {
            var result = new List<Element>(ElementCount);
            for (int e = 0; e < ElementCount; e++)
            {
                var elem = (Element)e;
                if (!exclude.Contains(elem))
                {
                    result.Add(elem);
                }
            }

            return result;
        }

        private static int[] SplitTotal(int total, int count, ref Pcg32 rng)
        {
            if (count == 0)
            {
                return Array.Empty<int>();
            }

            if (count == 1)
            {
                return new[] { total };
            }

            int first = 1 + (int)rng.NextUInt((uint)(total - 1));
            return new[] { first, total - first };
        }

        private static void SortDescending(PlanetElementShare[] shares, int start, int count)
        {
            if (count == 2 && (shares[start].Percent < shares[start + 1].Percent ||
                (shares[start].Percent == shares[start + 1].Percent && shares[start].Element > shares[start + 1].Element)))
            {
                (shares[start], shares[start + 1]) = (shares[start + 1], shares[start]);
            }
        }
    }
}
