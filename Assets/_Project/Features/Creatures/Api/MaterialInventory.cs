using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Creatures
{
    /// <summary>The player's stock of evolution materials, keyed by (element, tier). REQ: docs/design/creatures.md#進化.</summary>
    /// <remarks>
    /// Pre: single-threaded use (lobby / main thread).
    /// Post: invariant — every count is in 0..int.MaxValue; zero-count keys are not stored.
    /// Side effects: mutates only its own state.
    /// </remarks>
    public sealed class MaterialInventory
    {
        private readonly Dictionary<MaterialKey, int> _counts = new Dictionary<MaterialKey, int>();

        /// <summary>How many of <paramref name="key"/> are held.</summary>
        /// <remarks>Pre: none (an unknown or default key simply has none). Post: &gt;= 0. Side effects: none.</remarks>
        public int Count(MaterialKey key) => _counts.TryGetValue(key, out int n) ? n : 0;

        /// <summary>Adds <paramref name="amount"/> of <paramref name="key"/> (e.g. a planet drop).</summary>
        /// <remarks>
        /// Pre: key.Tier &gt;= 1; amount &gt;= 1; Count(key) + amount does not exceed int.MaxValue.
        /// Post: Count(key) increased by exactly amount.
        /// Side effects: mutates the stock.
        /// </remarks>
        public void Add(MaterialKey key, int amount)
        {
            Contract.Requires(key.Tier >= 1, "key must be a constructed MaterialKey");
            Contract.Requires(amount >= 1, "amount must be >= 1");
            int have = Count(key);
            Contract.Requires(have <= int.MaxValue - amount, "adding would overflow the material count");
            // checked: Contract is stripped in Release, and a silent wrap would turn a stockpile negative.
            _counts[key] = checked(have + amount);
        }

        /// <summary>Removes all of <paramref name="needs"/> at once, or nothing.</summary>
        /// <remarks>
        /// Pre: needs != null; every entry has Qty &gt;= 1. Repeated keys are summed (a list is not required to be merged).
        /// Post: true iff for every key the held count &gt;= the summed need; then each count dropped by exactly the
        ///       summed need. false leaves the stock untouched. An empty list returns true and changes nothing.
        /// Side effects: mutates the stock on true.
        /// </remarks>
        public bool TryConsume(IReadOnlyList<MaterialStack> needs)
        {
            Contract.Requires(needs != null, "needs is null");

            // long totals: summing repeated keys must not wrap before the all-or-nothing comparison.
            var totals = new Dictionary<MaterialKey, long>();
            for (int i = 0; i < needs.Count; i++)
            {
                MaterialStack need = needs[i];
                Contract.Requires(need.Qty >= 1, "every need must have Qty >= 1");
                totals.TryGetValue(need.Key, out long sum);
                totals[need.Key] = sum + need.Qty;
            }

            foreach (KeyValuePair<MaterialKey, long> total in totals)
            {
                if (Count(total.Key) < total.Value)
                {
                    return false;
                }
            }

            foreach (KeyValuePair<MaterialKey, long> total in totals)
            {
                int remaining = (int)(Count(total.Key) - total.Value);
                if (remaining == 0)
                {
                    _counts.Remove(total.Key);
                }
                else
                {
                    _counts[total.Key] = remaining;
                }
            }

            return true;
        }

        /// <summary>A copy of every non-empty stack, for saving or listing in the UI.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: one entry per key with Count &gt;= 1, ordered by element then tier (deterministic for saves);
        ///       later changes to the inventory do not affect the returned list.
        /// Side effects: allocates.
        /// </remarks>
        public IReadOnlyList<MaterialStack> Snapshot()
        {
            var stacks = new List<MaterialStack>(_counts.Count);
            foreach (KeyValuePair<MaterialKey, int> entry in _counts)
            {
                stacks.Add(new MaterialStack(entry.Key, entry.Value));
            }

            stacks.Sort(CompareStacks);
            return stacks.AsReadOnly();
        }

        private static int CompareStacks(MaterialStack a, MaterialStack b)
        {
            int byElement = ((int)a.Key.Element).CompareTo((int)b.Key.Element);
            return byElement != 0 ? byElement : a.Key.Tier.CompareTo(b.Key.Tier);
        }
    }
}
