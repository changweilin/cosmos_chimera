using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Creatures.Tests
{
    public sealed class PartnerLoadoutTests
    {
        private static readonly OwnedCreature MainA = Make.Creature(1, "sun_juvenile");
        private static readonly OwnedCreature MainB = Make.Creature(2, "moon_juvenile");

        private FakeRelationGraph _graph;
        private PartnerLoadout _loadout;

        [SetUp]
        public void SetUp()
        {
            _graph = new FakeRelationGraph();
            for (int i = 10; i < 20; i++)
            {
                _graph.Relate("sun_juvenile", "sub_" + i).Relate("moon_juvenile", "sub_" + i);
            }

            _loadout = new PartnerLoadout(new[] { 1, 2 });
        }

        private static OwnedCreature Sub(int id, VesselKind vessel = VesselKind.GlassBead) => Make.Creature(id, "sub_" + id, vessel);

        [Test]
        public void Assign_up_to_four_then_full()
        {
            for (int i = 10; i < 14; i++)
            {
                Assert.AreEqual(PartnerResult.Ok, _loadout.TryAssign(MainA, Sub(i), _graph));
            }

            Assert.AreEqual(PartnerResult.MainFull, _loadout.TryAssign(MainA, Sub(14), _graph));
            CollectionAssert.AreEqual(new[] { 10, 11, 12, 13 }, _loadout.SubsOf(1));
            Assert.AreEqual(PartnerResult.Ok, _loadout.TryAssign(MainB, Sub(14), _graph), "the limit is per main");
        }

        [Test]
        public void One_creature_serves_one_main_q_c10()
        {
            _loadout.TryAssign(MainA, Sub(10), _graph);

            Assert.AreEqual(PartnerResult.AlreadySubOfAnotherMain, _loadout.TryAssign(MainB, Sub(10), _graph));
            Assert.AreEqual(PartnerResult.AlreadyAssignedToThisMain, _loadout.TryAssign(MainA, Sub(10), _graph));
            Assert.IsTrue(_loadout.TryGetMainOf(10, out int main));
            Assert.AreEqual(1, main);
        }

        [Test]
        public void A_main_is_never_a_sub()
        {
            Assert.AreEqual(PartnerResult.SubIsAMain, _loadout.TryAssign(MainA, MainB, _graph));
            Assert.AreEqual(PartnerResult.SubIsAMain, _loadout.TryAssign(MainA, MainA, _graph));
        }

        [Test]
        public void Unknown_main_and_missing_relation_are_refused()
        {
            Assert.AreEqual(PartnerResult.UnknownMain, _loadout.TryAssign(Make.Creature(3, "x"), Sub(10), _graph));
            Assert.AreEqual(PartnerResult.NoRelation, _loadout.TryAssign(MainA, Make.Creature(30, "stranger"), _graph));
            Assert.IsEmpty(_loadout.SubsOf(1));
        }

        [Test]
        public void Rejection_order_is_as_documented()
        {
            for (int i = 10; i < 14; i++)
            {
                _loadout.TryAssign(MainA, Sub(i), _graph);
            }

            Assert.AreEqual(PartnerResult.AlreadyAssignedToThisMain, _loadout.TryAssign(MainA, Sub(10), _graph),
                "already-assigned wins over full");
            int calls = _graph.Calls;
            Assert.AreEqual(PartnerResult.MainFull, _loadout.TryAssign(MainA, Make.Creature(40, "stranger"), _graph),
                "full wins over no relation");
            Assert.AreEqual(calls, _graph.Calls, "relations are consulted last");
            Assert.AreEqual(PartnerResult.UnknownMain, _loadout.TryAssign(Make.Creature(99, "x"), MainB, _graph),
                "unknown main wins over sub-is-a-main");
        }

        [Test]
        public void Unassign_frees_the_creature()
        {
            _loadout.TryAssign(MainA, Sub(10), _graph);
            _loadout.TryAssign(MainA, Sub(11), _graph);

            Assert.AreEqual(PartnerResult.Ok, _loadout.Unassign(10));
            Assert.AreEqual(PartnerResult.NotAssigned, _loadout.Unassign(10));
            CollectionAssert.AreEqual(new[] { 11 }, _loadout.SubsOf(1));
            Assert.IsFalse(_loadout.TryGetMainOf(10, out _));
            Assert.AreEqual(PartnerResult.Ok, _loadout.TryAssign(MainB, Sub(10), _graph));
        }

        [Test]
        public void Evolution_can_invalidate_a_pair_without_removing_it()
        {
            _loadout.TryAssign(MainA, Sub(10), _graph);
            _loadout.TryAssign(MainA, Sub(11), _graph);
            var species = new Dictionary<int, string> { [1] = "sun_adolescent", [10] = "sub_10", [11] = "sub_11" };
            _graph.Relate("sun_adolescent", "sub_11");

            IReadOnlyList<PartnerPair> invalid = _loadout.FindInvalidPairs(_graph, Make.SpeciesOf(species));

            Assert.AreEqual(1, invalid.Count);
            Assert.AreEqual(1, invalid[0].MainInstanceId);
            Assert.AreEqual(10, invalid[0].SubInstanceId);
            Assert.AreEqual(2, _loadout.SubsOf(1).Count, "nothing is auto-removed");
        }

        [Test]
        public void Each_vessel_provides_one_thing()
        {
            _loadout.TryAssign(MainA, Sub(10, VesselKind.TalismanCard), _graph);
            _loadout.TryAssign(MainA, Sub(11, VesselKind.GlassBead), _graph);
            _loadout.TryAssign(MainA, Sub(12, VesselKind.MagicCube), _graph);
            var lookup = new FakeSpeciesLookup().Add("sub_10", "move_10", "trait_10").Add("sub_11", "move_11", "trait_11");
            var byId = new Dictionary<int, OwnedCreature>
            {
                [10] = Sub(10, VesselKind.TalismanCard), [11] = Sub(11, VesselKind.GlassBead), [12] = Sub(12, VesselKind.MagicCube),
            };

            IReadOnlyList<Provision> p = _loadout.ResolveProvisions(1, "sun_juvenile", id => byId[id], lookup);

            Assert.AreEqual(3, p.Count);
            Assert.AreEqual(ProvisionKind.Move, p[0].Kind);
            Assert.AreEqual("move_10", p[0].Id);
            Assert.AreEqual(ProvisionKind.Trait, p[1].Kind);
            Assert.AreEqual("trait_11", p[1].Id);
            Assert.AreEqual(ProvisionKind.Overload, p[2].Kind);
            Assert.AreEqual("sun_juvenile", p[2].Id, "magic cube unlocks the main's own overload; sub species not needed");
            Assert.AreEqual(12, p[2].SourceInstanceId);
        }

        [Test]
        public void Provision_preconditions()
        {
            var lookup = new FakeSpeciesLookup();
            Assert.Throws<ContractException>(() => Provision.Resolve(Sub(10, VesselKind.TalismanCard), "m", lookup), "unknown species");
            Assert.Throws<ContractException>(() => Provision.Resolve(default, "m", lookup));
            Assert.Throws<ContractException>(() => Provision.Resolve(Sub(10), "", lookup));
            Assert.Throws<ContractException>(() => Provision.Resolve(Sub(10), "m", null));
            Assert.Throws<ContractException>(() => new Provision((ProvisionKind)7, "x", 1));
            Assert.Throws<ContractException>(() => new Provision(ProvisionKind.Move, "", 1));
            Assert.Throws<ContractException>(() => new Provision(ProvisionKind.Move, "x", 0));
        }

        [Test]
        public void Loadout_preconditions()
        {
            Assert.Throws<ContractException>(() => new PartnerLoadout(null));
            Assert.Throws<ContractException>(() => new PartnerLoadout(new int[0]));
            Assert.Throws<ContractException>(() => new PartnerLoadout(new[] { 1, 2, 3, 4, 5, 6, 7 }));
            Assert.Throws<ContractException>(() => new PartnerLoadout(new[] { 1, 1 }));
            Assert.Throws<ContractException>(() => new PartnerLoadout(new[] { 0 }));
            Assert.Throws<ContractException>(() => _loadout.SubsOf(5));
            Assert.Throws<ContractException>(() => _loadout.TryAssign(default, Sub(10), _graph));
            Assert.Throws<ContractException>(() => _loadout.TryAssign(MainA, default, _graph));
            Assert.Throws<ContractException>(() => _loadout.TryAssign(MainA, Sub(10), null));
            Assert.Throws<ContractException>(() => _loadout.Unassign(0));
            Assert.Throws<ContractException>(() => _loadout.FindInvalidPairs(null, id => "x"));
            Assert.Throws<ContractException>(() => _loadout.FindInvalidPairs(_graph, null));
            Assert.Throws<ContractException>(() => _loadout.ResolveProvisions(5, "m", id => Sub(id), new FakeSpeciesLookup()));
            Assert.Throws<ContractException>(() => _loadout.ResolveProvisions(1, "m", null, new FakeSpeciesLookup()));
        }

        [Test]
        public void Exposed_lists_are_read_only()
        {
            Assert.IsInstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>(_loadout.Mains);
            Assert.IsInstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>>(_loadout.SubsOf(1));
        }
    }
}
