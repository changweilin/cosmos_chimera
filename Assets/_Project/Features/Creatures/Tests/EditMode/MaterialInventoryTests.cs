using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Creatures.Tests
{
    public sealed class MaterialInventoryTests
    {
        private static readonly MaterialKey Fire1 = new MaterialKey(Element.Fire, 1);
        private static readonly MaterialKey Fire2 = new MaterialKey(Element.Fire, 2);
        private static readonly MaterialKey Ice1 = new MaterialKey(Element.Ice, 1);

        [Test]
        public void Empty_inventory_counts_zero_for_any_key()
        {
            var inv = new MaterialInventory();
            Assert.AreEqual(0, inv.Count(Fire1));
            Assert.AreEqual(0, inv.Count(default), "default key is simply not held");
            Assert.IsEmpty(inv.Snapshot());
        }

        [Test]
        public void Add_accumulates_per_key()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, 2);
            inv.Add(Fire1, 3);
            inv.Add(Fire2, 1);
            Assert.AreEqual(5, inv.Count(Fire1));
            Assert.AreEqual(1, inv.Count(Fire2), "tier is part of the identity");
            Assert.AreEqual(0, inv.Count(Ice1), "element is part of the identity");
        }

        [Test]
        public void Add_reaches_int_max_but_not_beyond()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, int.MaxValue - 1);
            Assert.DoesNotThrow(() => inv.Add(Fire1, 1));
            Assert.AreEqual(int.MaxValue, inv.Count(Fire1));
            Assert.Throws<ContractException>(() => inv.Add(Fire1, 1));
            Assert.AreEqual(int.MaxValue, inv.Count(Fire1), "a rejected add changes nothing");
        }

        [Test]
        public void Add_overflow_off_by_one()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, 10);
            Assert.Throws<ContractException>(() => inv.Add(Fire1, int.MaxValue - 9));
            Assert.AreEqual(10, inv.Count(Fire1));
            Assert.DoesNotThrow(() => inv.Add(Fire1, int.MaxValue - 10));
        }

        [Test]
        public void Add_preconditions()
        {
            var inv = new MaterialInventory();
            Assert.Throws<ContractException>(() => inv.Add(Fire1, 0));
            Assert.Throws<ContractException>(() => inv.Add(Fire1, -1));
            Assert.Throws<ContractException>(() => inv.Add(default, 1));
        }

        [Test]
        public void TryConsume_removes_exactly_the_needs()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, 5);
            inv.Add(Ice1, 2);
            bool ok = inv.TryConsume(new[] { new MaterialStack(Fire1, 3), new MaterialStack(Ice1, 2) });
            Assert.IsTrue(ok);
            Assert.AreEqual(2, inv.Count(Fire1));
            Assert.AreEqual(0, inv.Count(Ice1));
            CollectionAssert.AreEqual(new[] { Fire1 }, KeysOf(inv.Snapshot()), "zero stacks are not kept");
        }

        [Test]
        public void TryConsume_is_all_or_nothing()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, 5);
            inv.Add(Ice1, 1);
            bool ok = inv.TryConsume(new[] { new MaterialStack(Fire1, 3), new MaterialStack(Ice1, 2) });
            Assert.IsFalse(ok);
            Assert.AreEqual(5, inv.Count(Fire1));
            Assert.AreEqual(1, inv.Count(Ice1));
        }

        [Test]
        public void TryConsume_exact_amount_succeeds_and_one_more_fails()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, 3);
            Assert.IsFalse(inv.TryConsume(new[] { new MaterialStack(Fire1, 4) }));
            Assert.IsTrue(inv.TryConsume(new[] { new MaterialStack(Fire1, 3) }));
            Assert.AreEqual(0, inv.Count(Fire1));
        }

        [Test]
        public void TryConsume_sums_repeated_keys_before_comparing()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, 5);
            var needs = new[] { new MaterialStack(Fire1, 3), new MaterialStack(Fire1, 3) };
            Assert.IsFalse(inv.TryConsume(needs), "3 + 3 > 5 even though each line alone fits");
            Assert.AreEqual(5, inv.Count(Fire1));
            inv.Add(Fire1, 1);
            Assert.IsTrue(inv.TryConsume(needs));
            Assert.AreEqual(0, inv.Count(Fire1));
        }

        [Test]
        public void TryConsume_repeated_keys_do_not_wrap_around_int()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, 5);
            var needs = new[] { new MaterialStack(Fire1, int.MaxValue), new MaterialStack(Fire1, int.MaxValue) };
            Assert.IsFalse(inv.TryConsume(needs));
            Assert.AreEqual(5, inv.Count(Fire1));
        }

        [Test]
        public void TryConsume_empty_list_is_a_successful_no_op()
        {
            var inv = new MaterialInventory();
            inv.Add(Fire1, 1);
            Assert.IsTrue(inv.TryConsume(new MaterialStack[0]));
            Assert.AreEqual(1, inv.Count(Fire1));
        }

        [Test]
        public void TryConsume_preconditions()
        {
            var inv = new MaterialInventory();
            Assert.Throws<ContractException>(() => inv.TryConsume(null));
            Assert.Throws<ContractException>(() => inv.TryConsume(new[] { default(MaterialStack) }), "Qty 0 line");
        }

        [Test]
        public void Snapshot_is_ordered_by_element_then_tier_and_detached()
        {
            var inv = new MaterialInventory();
            inv.Add(Ice1, 1);
            inv.Add(Fire2, 4);
            inv.Add(Fire1, 2);
            IReadOnlyList<MaterialStack> snap = inv.Snapshot();
            CollectionAssert.AreEqual(new[] { Fire1, Fire2, Ice1 }, KeysOf(snap));
            Assert.AreEqual(2, snap[0].Qty);
            Assert.AreEqual(4, snap[1].Qty);
            inv.Add(Fire1, 10);
            Assert.AreEqual(2, snap[0].Qty, "later changes do not touch an earlier snapshot");
        }

        private static List<MaterialKey> KeysOf(IReadOnlyList<MaterialStack> stacks)
        {
            var keys = new List<MaterialKey>();
            foreach (MaterialStack s in stacks)
            {
                keys.Add(s.Key);
            }

            return keys;
        }
    }
}
