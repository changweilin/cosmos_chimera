using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.Crafting;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: docs/design/destructibles.md#傷害 擊倒＝收成 (Q-C77) — a harvest that did not fit waits on the ground.
    public sealed class HarvestPilesTests
    {
        private static readonly CraftStack[] Timber = { new CraftStack("raw_timber", 2) };

        [Test]
        public void A_pile_is_taken_whole_only_within_reach_and_only_when_it_fits()
        {
            var piles = new HarvestPiles();
            var bag = new CraftInventory();
            var picked = new List<int>();
            int near = piles.Add(new float3(0f, 0f, 0f), Timber);
            int far = piles.Add(new float3(5f, 0f, 0f), Timber);
            Assert.That(near, Is.Not.EqualTo(far));

            piles.Collect(new float3(0.5f, 0f, 0f), 1f, bag, picked);
            Assert.That(picked, Is.EqualTo(new[] { near }));
            Assert.That(bag.Count("raw_timber"), Is.EqualTo(2));
            Assert.That(piles.Count, Is.EqualTo(1));

            picked.Clear();
            var full = new CraftInventory();
            // Capacity is int.MaxValue per id: one stack just below it leaves no room for the pile.
            Assert.That(full.TryExchange(System.Array.Empty<CraftStack>(), new[] { new CraftStack("raw_timber", int.MaxValue - 1) }), Is.True);
            int before = full.Count("raw_timber");
            piles.Collect(new float3(5f, 0f, 0f), 1f, full, picked);
            Assert.That(picked, Is.Empty, "a full bag leaves the pile on the ground");
            Assert.That(full.Count("raw_timber"), Is.EqualTo(before));
            Assert.That(piles.Count, Is.EqualTo(1));
        }

        [Test]
        public void Contract_violations_throw()
        {
            var piles = new HarvestPiles();
            Assert.Throws<ContractException>(() => piles.Add(float3.zero, null));
            Assert.Throws<ContractException>(() => piles.Add(float3.zero, new CraftStack[0]));
            Assert.Throws<ContractException>(() => piles.Add(new float3(float.NaN, 0f, 0f), Timber));
            Assert.Throws<ContractException>(() => piles.Collect(float3.zero, 1f, null, new List<int>()));
            Assert.Throws<ContractException>(() => piles.Collect(float3.zero, -1f, new CraftInventory(), new List<int>()));
        }
    }
}
