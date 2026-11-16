using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Crafting
{
    /// <summary>One material or crafted-item stack.</summary>
    /// <remarks>Pre: constructed with a snake_case id and positive quantity. Post: immutable. Side effects: none.</remarks>
    public readonly struct CraftStack
    {
        /// <summary>Creates a persistent item stack.</summary>
        /// <remarks>Pre: id is snake_case, at most 64 characters; quantity positive. Post: arguments retained. Side effects: none.</remarks>
        public CraftStack(string id, int quantity)
        {
            Contract.Requires(CraftInventory.ValidId(id), "item id must be snake_case and at most 64 characters");
            Contract.Requires(quantity > 0, "quantity must be positive");
            Id = id;
            Quantity = quantity;
        }

        /// <summary>Stable item id.</summary>
        /// <remarks>Pre: constructed stack. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Number of items.</summary>
        /// <remarks>Pre: constructed stack. Post: positive. Side effects: none.</remarks>
        public int Quantity { get; }
    }

    /// <summary>Persistent raw-material and crafting stock with atomic exchanges.</summary>
    /// <remarks>Pre: single-threaded use. Post: all stored counts positive and representable. Side effects: instance mutation.</remarks>
    public sealed class CraftInventory
    {
        private static readonly Regex IdPattern = new Regex(@"\A[a-z][a-z0-9]*(_[a-z0-9]+)*\z");
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Checks the persisted item-id domain without requiring a current catalog entry.</summary>
        /// <remarks>Pre: none. Post: true iff id is snake_case and at most 64 characters. Side effects: none.</remarks>
        public static bool ValidId(string id) => id != null && id.Length <= 64 && IdPattern.IsMatch(id);

        /// <summary>Returns held quantity, including unknown but well-formed retired ids.</summary>
        /// <remarks>Pre: id valid. Post: nonnegative. Side effects: none.</remarks>
        public int Count(string id)
        {
            Contract.Requires(ValidId(id), "invalid item id");
            return _counts.TryGetValue(id, out int value) ? value : 0;
        }

        /// <summary>Previews an all-or-nothing exchange, combining repeated ids before checking stock and capacity.</summary>
        /// <remarks>Pre: both lists non-null; every stack constructed. Post: true iff the full exchange fits. Side effects: allocates.</remarks>
        public bool CanExchange(IReadOnlyList<CraftStack> costs, IReadOnlyList<CraftStack> gains) =>
            Plan(costs, gains) != null;

        /// <summary>Consumes all costs and grants all gains, or leaves every count unchanged.</summary>
        /// <remarks>Pre: both lists non-null; every stack constructed. Post: succeeds iff CanExchange; no overflow or partial consumption. Side effects: mutates stock on success.</remarks>
        public bool TryExchange(IReadOnlyList<CraftStack> costs, IReadOnlyList<CraftStack> gains)
        {
            Dictionary<string, int> plan = Plan(costs, gains);
            if (plan == null) return false;
            foreach (KeyValuePair<string, int> entry in plan)
            {
                if (entry.Value == 0) _counts.Remove(entry.Key);
                else _counts[entry.Key] = entry.Value;
            }
            return true;
        }

        /// <summary>Copies nonempty stacks in stable ordinal id order.</summary>
        /// <remarks>Pre: none. Post: snapshot independent of future mutations. Side effects: allocates.</remarks>
        public IReadOnlyList<CraftStack> Snapshot()
        {
            var ids = new List<string>(_counts.Keys);
            ids.Sort(StringComparer.Ordinal);
            var result = new List<CraftStack>(ids.Count);
            foreach (string id in ids) result.Add(new CraftStack(id, _counts[id]));
            return result.AsReadOnly();
        }

        private Dictionary<string, int> Plan(IReadOnlyList<CraftStack> costs, IReadOnlyList<CraftStack> gains)
        {
            Contract.Requires(costs != null && gains != null, "costs and gains required");
            Dictionary<string, long> spent = Sum(costs);
            Dictionary<string, long> earned = Sum(gains);
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, long> entry in spent)
            {
                int held = Count(entry.Key);
                // Gains cannot pay for the materials needed to start a recipe.
                if (held < entry.Value) return null;
                result[entry.Key] = (int)(held - entry.Value);
            }
            foreach (KeyValuePair<string, long> entry in earned)
            {
                int held = result.TryGetValue(entry.Key, out int remaining) ? remaining : Count(entry.Key);
                long total = held + entry.Value;
                if (total > int.MaxValue) return null;
                result[entry.Key] = (int)total;
            }
            return result;
        }

        private static Dictionary<string, long> Sum(IReadOnlyList<CraftStack> stacks)
        {
            var totals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (CraftStack stack in stacks)
            {
                Contract.Requires(ValidId(stack.Id) && stack.Quantity > 0, "every stack must be constructed");
                totals.TryGetValue(stack.Id, out long previous);
                totals[stack.Id] = previous + stack.Quantity;
            }
            return totals;
        }
    }
}
