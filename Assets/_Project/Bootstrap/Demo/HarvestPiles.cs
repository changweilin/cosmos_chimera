using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Crafting;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Knockdown harvests that did not fit the crafting bag, lying where the object broke until the player walks up
    /// with room for them. REQ: destructibles.md#傷害 (擊倒＝收成, Q-C77).
    /// </summary>
    /// <remarks>
    /// Pure rule state (no GameObjects): the run draws one marker per pile id. A pile is taken whole or not at all, so
    /// a recipe-sized harvest is never split across the bag and the ground.
    /// Pre: host/main-thread use. Post: see members. Side effects: see members.
    /// </remarks>
    public sealed class HarvestPiles
    {
        private readonly List<Pile> _piles = new List<Pile>();
        private int _nextId;

        /// <summary>Piles still on the ground.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Count => _piles.Count;

        /// <summary>Leaves <paramref name="stacks"/> on the ground at <paramref name="position"/>.</summary>
        /// <remarks>
        /// Pre: stacks non-null and non-empty, every stack constructed; position finite.
        /// Post: a new pile with a copy of stacks; returns its id (unique within this instance).
        /// Side effects: allocates.
        /// </remarks>
        public int Add(float3 position, IReadOnlyList<CraftStack> stacks)
        {
            Contract.Requires(stacks != null && stacks.Count > 0, "a pile needs at least one stack");
            Contract.Requires(math.all(math.isfinite(position)), "position must be finite");
            var copy = new CraftStack[stacks.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = stacks[i];
            int id = _nextId++;
            _piles.Add(new Pile(id, position, copy));
            return id;
        }

        /// <summary>Moves every pile within <paramref name="radiusM"/> of <paramref name="playerPosition"/> that fits into the bag.</summary>
        /// <remarks>
        /// Pre: inventory and pickedIds non-null; radiusM &gt;= 0; playerPosition finite.
        /// Post: each collected pile's stacks are in inventory and its id is appended to pickedIds; piles that do not
        ///       fit whole stay. Piles are tried in creation order.
        /// Side effects: mutates inventory, the pile list and pickedIds.
        /// </remarks>
        public void Collect(float3 playerPosition, float radiusM, CraftInventory inventory, List<int> pickedIds)
        {
            Contract.Requires(inventory != null && pickedIds != null, "inventory and pickedIds are required");
            Contract.Requires(radiusM >= 0f, "radius must be >= 0");
            float r2 = radiusM * radiusM;
            for (int i = 0; i < _piles.Count; i++)
            {
                Pile p = _piles[i];
                if (math.distancesq(p.Position, playerPosition) > r2) continue;
                if (!inventory.TryExchange(System.Array.Empty<CraftStack>(), p.Stacks)) continue;
                pickedIds.Add(p.Id);
                _piles.RemoveAt(i);
                i--;
            }
        }

        private readonly struct Pile
        {
            internal Pile(int id, float3 position, CraftStack[] stacks) { Id = id; Position = position; Stacks = stacks; }
            internal int Id { get; }
            internal float3 Position { get; }
            internal CraftStack[] Stacks { get; }
        }
    }
}
