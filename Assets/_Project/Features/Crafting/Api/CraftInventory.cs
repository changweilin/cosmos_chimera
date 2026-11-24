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

    /// <summary>Persistent raw-material and crafting stock with atomic exchanges; crafted gear copies carry their own look.</summary>
    /// <remarks>
    /// REQ: docs/design/gear-looks.md#外觀與數值 — the bag shows stacks, but each copy of a stack may carry an <see cref="ItemLook"/>.
    /// Copies of an id are ordered: untagged copies (older saves, non-crafted gains) first, then tagged copies oldest first.
    /// Pre: single-threaded use. Post: all stored counts positive and representable; tagged copies never exceed the count.
    /// Side effects: instance mutation.
    /// </remarks>
    public sealed class CraftInventory
    {
        private static readonly Regex IdPattern = new Regex(@"\A[a-z][a-z0-9]*(_[a-z0-9]+)*\z");
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ItemLook>> _looks = new Dictionary<string, List<ItemLook>>(StringComparer.Ordinal);

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
            Apply(plan);
            return true;
        }

        /// <summary>Like <see cref="TryExchange"/>, and tags every gained copy of <paramref name="lookIds"/> as crafted on <paramref name="origin"/>.</summary>
        /// <remarks>
        /// REQ: gear-looks.md#外觀與數值. Pre: as TryExchange; lookIds non-null; origin constructed.
        /// Post: as TryExchange; on success each gained copy of a look id is tagged with ItemLook.Crafted(id, origin, n), n being
        ///       the id's count before the gain plus the copy's position, and sits after every older copy.
        /// Side effects: mutates stock on success.
        /// </remarks>
        public bool TryExchangeCrafted(IReadOnlyList<CraftStack> costs, IReadOnlyList<CraftStack> gains,
            IReadOnlyCollection<string> lookIds, GearOrigin origin)
        {
            Contract.Requires(gains != null && lookIds != null && ValidId(origin.PlanetTypeId), "gains, look ids and origin required");
            var before = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CraftStack gain in gains)
                if (ValidId(gain.Id) && !before.ContainsKey(gain.Id)) before[gain.Id] = Count(gain.Id);
            if (!TryExchange(costs, gains)) return false;
            var added = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CraftStack gain in gains)
            {
                if (!Contains(lookIds, gain.Id)) continue;
                List<ItemLook> list = LooksFor(gain.Id);
                added.TryGetValue(gain.Id, out int n);
                for (int i = 0; i < gain.Quantity; i++, n++)
                    list.Add(ItemLook.Crafted(gain.Id, origin, (ulong)(before[gain.Id] + n)));
                added[gain.Id] = n;
            }
            return true;
        }

        /// <summary>Look of copy <paramref name="copy"/> of <paramref name="id"/>.</summary>
        /// <remarks>
        /// Pre: id valid; 0 &lt;= copy &lt; Count(id).
        /// Post: untagged copies read ItemLook.Default(id, copy), stable while no copy of the id is removed below them.
        /// Side effects: none.
        /// </remarks>
        public ItemLook LookOf(string id, int copy)
        {
            int count = Count(id);
            Contract.Requires(copy >= 0 && copy < count, "copy index out of range");
            int tagged = _looks.TryGetValue(id, out List<ItemLook> list) ? list.Count : 0;
            int untagged = count - tagged;
            return copy < untagged ? ItemLook.Default(id, copy) : list[copy - untagged];
        }

        /// <summary>Index of the first copy of <paramref name="id"/> whose look equals <paramref name="look"/>, or -1.</summary>
        /// <remarks>Pre: id valid. Post: -1 or a valid copy index. Side effects: none.</remarks>
        public int FindCopy(string id, ItemLook look)
        {
            int count = Count(id);
            for (int i = 0; i < count; i++) if (LookOf(id, i).Equals(look)) return i;
            return -1;
        }

        /// <summary>Removes one chosen copy (optional) and puts back concrete copies, all or nothing.</summary>
        /// <remarks>
        /// REQ: gear-looks.md#外觀與數值 — equipping picks a copy; unequipping returns it with its look.
        /// Pre: puts non-null with constructed copies; takeId null, or valid with 0 &lt;= takeCopy &lt; Count(takeId).
        /// Post: false leaves stock unchanged (a put would overflow). true removes the chosen copy (taken = its look; default
        ///       when takeId is null) and appends each put copy as the newest copy of its id; looks of all other copies unchanged.
        /// Side effects: mutates stock on success.
        /// </remarks>
        public bool TryMoveCopies(string takeId, int takeCopy, IReadOnlyList<ItemCopy> puts, out ItemLook taken)
        {
            Contract.Requires(puts != null, "puts required");
            taken = default;
            var costs = new List<CraftStack>();
            if (takeId != null)
            {
                Contract.Requires(ValidId(takeId) && takeCopy >= 0 && takeCopy < Count(takeId), "copy index out of range");
                costs.Add(new CraftStack(takeId, 1));
            }
            var gains = new List<CraftStack>(puts.Count);
            foreach (ItemCopy put in puts)
            {
                Contract.Requires(ValidId(put.Id), "puts must be constructed");
                gains.Add(new CraftStack(put.Id, 1));
            }
            Dictionary<string, int> plan = Plan(costs, gains);
            if (plan == null) return false;
            if (takeId != null) taken = Take(takeId, takeCopy);
            // Untagged copies always sit in front, so an appended tagged copy is the newest without pinning anything.
            foreach (ItemCopy put in puts) LooksFor(put.Id).Add(put.Look);
            foreach (KeyValuePair<string, int> entry in plan)
            {
                if (entry.Value == 0) _counts.Remove(entry.Key);
                else _counts[entry.Key] = entry.Value;
            }
            foreach (string id in new List<string>(_looks.Keys)) if (_looks[id].Count == 0) _looks.Remove(id);
            return true;
        }

        /// <summary>Tagged copies per id in ordinal id order.</summary>
        /// <remarks>Pre: none. Post: only ids with at least one tagged copy; snapshot independent of later mutation. Side effects: allocates.</remarks>
        public IReadOnlyList<ItemLookList> LookSnapshot()
        {
            var ids = new List<string>(_looks.Keys);
            ids.Sort(StringComparer.Ordinal);
            var result = new List<ItemLookList>(ids.Count);
            foreach (string id in ids) result.Add(new ItemLookList(id, _looks[id]));
            return result.AsReadOnly();
        }

        /// <summary>Replaces tagged looks after counts were restored (save load, run departure).</summary>
        /// <remarks>
        /// Pre: lists non-null; unique valid ids; each list's Looks.Count &lt;= Count(id).
        /// Post: LookSnapshot equals lists (ids with empty lists omitted). Side effects: replaces looks.
        /// </remarks>
        public void RestoreLooks(IReadOnlyList<ItemLookList> lists)
        {
            Contract.Requires(lists != null, "lists required");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemLookList entry in lists)
            {
                Contract.Requires(entry != null && seen.Add(entry.Id) && entry.Looks.Count <= Count(entry.Id),
                    "unique ids with no more looks than copies required");
            }
            _looks.Clear();
            foreach (ItemLookList entry in lists)
                if (entry.Looks.Count > 0) _looks[entry.Id] = new List<ItemLook>(entry.Looks);
        }

        /// <summary>Replaces this stock (counts and looks) with a copy of <paramref name="other"/>.</summary>
        /// <remarks>Pre: other non-null. Post: Snapshot and LookSnapshot equal other's. Side effects: replaces stock.</remarks>
        public void CopyFrom(CraftInventory other)
        {
            Contract.Requires(other != null, "other required");
            if (ReferenceEquals(other, this)) return;
            _counts.Clear();
            foreach (KeyValuePair<string, int> entry in other._counts) _counts[entry.Key] = entry.Value;
            _looks.Clear();
            foreach (KeyValuePair<string, List<ItemLook>> entry in other._looks) _looks[entry.Key] = new List<ItemLook>(entry.Value);
        }

        private void Apply(Dictionary<string, int> plan)
        {
            foreach (KeyValuePair<string, int> entry in plan)
            {
                int removed = Count(entry.Key) - entry.Value;
                if (entry.Value == 0) _counts.Remove(entry.Key);
                else _counts[entry.Key] = entry.Value;
                // REQ: gear-looks.md#外觀與數值 — recipes consume the newest copies first: tagged copies from the end, then untagged.
                if (removed > 0 && _looks.TryGetValue(entry.Key, out List<ItemLook> list))
                {
                    list.RemoveRange(Math.Max(0, list.Count - removed), Math.Min(removed, list.Count));
                    if (list.Count == 0) _looks.Remove(entry.Key);
                }
            }
        }

        // Caller updates the count afterwards. Removing an untagged copy would shift the index-derived looks of the untagged
        // copies above it, so only those are pinned (O(copies above), not O(stack): stacks may hold int.MaxValue).
        private ItemLook Take(string id, int copy)
        {
            List<ItemLook> list = LooksFor(id);
            int untagged = Count(id) - list.Count;
            if (copy >= untagged)
            {
                ItemLook tagged = list[copy - untagged];
                list.RemoveAt(copy - untagged);
                return tagged;
            }
            var pinned = new List<ItemLook>(untagged - copy - 1 + list.Count);
            for (int i = copy + 1; i < untagged; i++) pinned.Add(ItemLook.Default(id, i));
            pinned.AddRange(list);
            _looks[id] = pinned;
            return ItemLook.Default(id, copy);
        }

        private List<ItemLook> LooksFor(string id)
        {
            if (!_looks.TryGetValue(id, out List<ItemLook> list)) _looks[id] = list = new List<ItemLook>();
            return list;
        }

        private static bool Contains(IReadOnlyCollection<string> ids, string id)
        {
            foreach (string candidate in ids) if (string.Equals(candidate, id, StringComparison.Ordinal)) return true;
            return false;
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
