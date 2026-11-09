using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Creatures.Tests
{
    // Regressions for docs/reviews/P2-05-P2-07-sol.md: one test per surviving mutant (M15–M45) and C1.
    public sealed class CreaturesReviewSolTests
    {
        private static readonly MaterialKey F1 = new MaterialKey(Element.Fire, 1);
        private static readonly MaterialKey F2 = new MaterialKey(Element.Fire, 2);

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private FakeRelationGraph _graph;
        private FakeSpeciesLookup _species;
        private FakeStages _stages;

        [SetUp]
        public void SetUp()
        {
            _graph = new FakeRelationGraph();
            _species = new FakeSpeciesLookup();
            _stages = new FakeStages();
            foreach (string main in new[] { "main_1", "main_2" })
            {
                for (int i = 10; i < 30; i++)
                {
                    _graph.Relate(main, "sub_" + i);
                }
            }

            for (int i = 10; i < 30; i++)
            {
                _species.Add("sub_" + i, "move_" + i, "trait_" + i);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _created)
            {
                UnityEngine.Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private static OwnedCreature Main(int id) => Make.Creature(id, "main_" + id);

        private static OwnedCreature Sub(int id, VesselKind vessel = VesselKind.GlassBead) => Make.Creature(id, "sub_" + id, vessel);

        private static string SpeciesOfId(int id) => id < 10 ? "main_" + id : "sub_" + id;

        private static EvolutionRequirement Req(int exp, params MaterialStack[] mats) => new EvolutionRequirement(exp, mats, "next_form");

        // ---- C1 ----

        [Test]
        public void ResolveProvisions_checks_its_preconditions_even_without_subs()
        {
            var loadout = new PartnerLoadout(new[] { 1 });
            Func<int, OwnedCreature> creatureOf = id => Sub(id);

            Assert.Throws<ContractException>(() => loadout.ResolveProvisions(1, null, creatureOf, _species));
            Assert.Throws<ContractException>(() => loadout.ResolveProvisions(1, "", creatureOf, _species));
            Assert.Throws<ContractException>(() => loadout.ResolveProvisions(1, "main_1", creatureOf, null));
            Assert.AreEqual(0, loadout.ResolveProvisions(1, "main_1", creatureOf, _species).Count);
        }

        // ---- C5: provisions keep the individual (M15, M16, M27, M43) ----

        [Test]
        public void Provision_source_is_the_providing_individual()
        {
            Provision move = Provision.Resolve(Sub(10, VesselKind.TalismanCard), "main_1", _species);
            Provision trait = Provision.Resolve(Sub(11, VesselKind.GlassBead), "main_1", _species);

            Assert.AreEqual(10, move.SourceInstanceId);
            Assert.AreEqual("move_10", move.Id);
            Assert.AreEqual(11, trait.SourceInstanceId);
            Assert.AreEqual("trait_11", trait.Id);
        }

        [Test]
        public void Several_magic_cubes_give_one_overload_each_in_order()
        {
            var loadout = new PartnerLoadout(new[] { 1 });
            loadout.TryAssign(Main(1), Sub(10, VesselKind.MagicCube), _graph, _stages);
            loadout.TryAssign(Main(1), Sub(11, VesselKind.MagicCube), _graph, _stages);

            IReadOnlyList<Provision> p = loadout.ResolveProvisions(1, "main_1", id => Sub(id, VesselKind.MagicCube), _species);

            Assert.AreEqual(2, p.Count);
            Assert.AreEqual(ProvisionKind.Overload, p[0].Kind);
            Assert.AreEqual(ProvisionKind.Overload, p[1].Kind);
            Assert.AreEqual(10, p[0].SourceInstanceId);
            Assert.AreEqual(11, p[1].SourceInstanceId);
        }

        [Test]
        public void Provision_rejects_an_undefined_kind()
        {
            Assert.Throws<ContractException>(() => new Provision((ProvisionKind)(-1), "x", 1));
            Assert.Throws<ContractException>(() => new Provision((ProvisionKind)3, "x", 1));
        }

        // ---- C6: refusal-order intersections and loadout Posts (M17–M30) ----

        private PartnerLoadout FullMainOne()
        {
            var loadout = new PartnerLoadout(new[] { 1, 2 });
            for (int i = 10; i < 14; i++)
            {
                Assert.AreEqual(PartnerResult.Ok, loadout.TryAssign(Main(1), Sub(i), _graph, _stages));
            }

            return loadout;
        }

        [Test]
        public void Full_main_still_reports_SubIsAMain_first_without_querying_relations()
        {
            PartnerLoadout loadout = FullMainOne();
            int calls = _graph.Calls;

            Assert.AreEqual(PartnerResult.SubIsAMain, loadout.TryAssign(Main(1), Main(2), _graph, _stages));
            Assert.AreEqual(calls, _graph.Calls);
        }

        [Test]
        public void Full_main_still_reports_AlreadySubOfAnotherMain_first()
        {
            PartnerLoadout loadout = FullMainOne();
            Assert.AreEqual(PartnerResult.Ok, loadout.TryAssign(Main(2), Sub(20), _graph, _stages));

            Assert.AreEqual(PartnerResult.AlreadySubOfAnotherMain, loadout.TryAssign(Main(1), Sub(20), _graph, _stages));
        }

        [Test]
        public void Reassigning_the_same_pair_does_not_requery_relations()
        {
            var loadout = new PartnerLoadout(new[] { 1 });
            loadout.TryAssign(Main(1), Sub(10), _graph, _stages);
            _graph.Unrelate("main_1", "sub_10");
            int calls = _graph.Calls;

            Assert.AreEqual(PartnerResult.AlreadyAssignedToThisMain, loadout.TryAssign(Main(1), Sub(10), _graph, _stages));
            Assert.AreEqual(calls, _graph.Calls);
        }

        [Test]
        public void Invalid_pairs_follow_mains_order_then_assignment_order_across_mains()
        {
            var loadout = new PartnerLoadout(new[] { 2, 1 });
            loadout.TryAssign(Main(2), Sub(20), _graph, _stages);
            loadout.TryAssign(Main(2), Sub(21), _graph, _stages);
            loadout.TryAssign(Main(1), Sub(10), _graph, _stages);

            IReadOnlyList<PartnerPair> invalid = loadout.FindInvalidPairs(new FakeRelationGraph(), SpeciesOfId);

            Assert.AreEqual(3, invalid.Count);
            Assert.AreEqual(2, invalid[0].MainInstanceId);
            Assert.AreEqual(20, invalid[0].SubInstanceId);
            Assert.AreEqual(2, invalid[1].MainInstanceId);
            Assert.AreEqual(21, invalid[1].SubInstanceId);
            Assert.AreEqual(1, invalid[2].MainInstanceId);
            Assert.AreEqual(10, invalid[2].SubInstanceId);
        }

        [Test]
        public void Invalid_pairs_are_found_on_a_later_main_only()
        {
            var loadout = new PartnerLoadout(new[] { 1, 2 });
            loadout.TryAssign(Main(1), Sub(10), _graph, _stages);
            loadout.TryAssign(Main(2), Sub(20), _graph, _stages);
            _graph.Unrelate("main_2", "sub_20");

            IReadOnlyList<PartnerPair> invalid = loadout.FindInvalidPairs(_graph, SpeciesOfId);

            Assert.AreEqual(1, invalid.Count);
            Assert.AreEqual(2, invalid[0].MainInstanceId);
            Assert.AreEqual(20, invalid[0].SubInstanceId);
        }

        [Test]
        public void Unassign_keeps_the_order_of_the_remaining_subs()
        {
            PartnerLoadout loadout = FullMainOne();

            Assert.AreEqual(PartnerResult.Ok, loadout.Unassign(11));
            CollectionAssert.AreEqual(new[] { 10, 12, 13 }, loadout.SubsOf(1));
        }

        [Test]
        public void SubsOf_is_a_live_view()
        {
            var loadout = new PartnerLoadout(new[] { 1 });
            IReadOnlyList<int> view = loadout.SubsOf(1);

            loadout.TryAssign(Main(1), Sub(10), _graph, _stages);

            CollectionAssert.AreEqual(new[] { 10 }, view);
        }

        [Test]
        public void Mains_keep_construction_order_and_six_are_allowed()
        {
            CollectionAssert.AreEqual(new[] { 2, 1 }, new PartnerLoadout(new[] { 2, 1 }).Mains);
            Assert.AreEqual(6, new PartnerLoadout(new[] { 1, 2, 3, 4, 5, 6 }).Mains.Count);
            Assert.Throws<ContractException>(() => new PartnerLoadout(new[] { 1, 2, 3, 4, 5, 6, 7 }));
        }

        [Test]
        public void TryGetMainOf_an_unassigned_creature_is_false_with_zero()
        {
            var loadout = new PartnerLoadout(new[] { 1 });
            loadout.TryAssign(Main(1), Sub(10), _graph, _stages);

            Assert.IsFalse(loadout.TryGetMainOf(42, out int main));
            Assert.AreEqual(0, main);
        }

        [Test]
        public void ResolveProvisions_rejects_a_callback_returning_the_wrong_individual()
        {
            var loadout = new PartnerLoadout(new[] { 1 });
            loadout.TryAssign(Main(1), Sub(10), _graph, _stages);

            Assert.Throws<ContractException>(() => loadout.ResolveProvisions(1, "main_1", id => Sub(11), _species));
        }

        [Test]
        public void FindInvalidPairs_rejects_a_missing_current_species()
        {
            var loadout = new PartnerLoadout(new[] { 1 });
            loadout.TryAssign(Main(1), Sub(10), _graph, _stages);

            Assert.Throws<ContractException>(() => loadout.FindInvalidPairs(_graph, id => id == 1 ? null : "sub_10"));
            Assert.Throws<ContractException>(() => loadout.FindInvalidPairs(_graph, id => id == 1 ? "main_1" : ""));
        }

        // ---- C5: value-type domains (M31–M34, M40–M42) ----

        [Test]
        public void MaterialStack_rejects_negative_qty()
        {
            Assert.Throws<ContractException>(() => new MaterialStack(F1, -1));
        }

        [Test]
        public void Max_tier_is_a_distinct_key_through_inventory_round_trip()
        {
            var top = new MaterialKey(Element.Fire, int.MaxValue);
            var inv = new MaterialInventory();
            inv.Add(top, 2);
            inv.Add(F1, 5);

            Assert.IsTrue(inv.TryConsume(new[] { new MaterialStack(top, 2) }));
            Assert.AreEqual(0, inv.Count(top));
            Assert.AreEqual(5, inv.Count(F1));
            Assert.AreNotEqual(F1, top);
        }

        [Test]
        public void Max_required_level_is_kept_and_reported_unclamped()
        {
            EvolutionRequirement req = Req(int.MaxValue, new MaterialStack(F1, 1));
            var inv = new MaterialInventory();
            inv.Add(F1, 1);

            EvolutionCheck check = EvolutionRules.Check(Make.Creature(1, "a", exp: 0), req, inv, EvolutionVenue.Lobby, Make.Progression);

            Assert.AreEqual(int.MaxValue, req.RequiredLevel);
            Assert.AreEqual(int.MaxValue - 1, check.LevelShortBy, "exp 0 is LV1");
        }

        [Test]
        public void SpeciesInfo_rejects_missing_ids()
        {
            foreach (string bad in new[] { null, "" })
            {
                Assert.Throws<ContractException>(() => new SpeciesInfo(bad, "m", "t", CreatureStage.Juvenile));
                Assert.Throws<ContractException>(() => new SpeciesInfo("s", bad, "t", CreatureStage.Juvenile));
                Assert.Throws<ContractException>(() => new SpeciesInfo("s", "m", bad, CreatureStage.Juvenile));
            }

            Assert.Throws<ContractException>(() => new SpeciesInfo("s", "m", "t", (CreatureStage)99));
        }

        // ---- C5: evolution blockers and identity (M35, M44, M45) ----

        [Test]
        public void Adult_on_a_planet_reports_both_blockers()
        {
            EvolutionCheck check = EvolutionRules.Check(Make.Creature(1, "a"), default, new MaterialInventory(), EvolutionVenue.Planet, Make.Progression);

            Assert.IsTrue(check.AlreadyFinal);
            Assert.IsTrue(check.NotInLobby);
        }

        [TestCase(VesselKind.GlassBead)]
        [TestCase(VesselKind.MagicCube)]
        [TestCase(VesselKind.TalismanCard)]
        public void Evolving_keeps_vessel_and_max_exp(VesselKind vessel)
        {
            var inv = new MaterialInventory();
            inv.Add(F2, 3);
            var creature = new OwnedCreature(7, "a", vessel, int.MaxValue);

            EvolutionResult r = EvolutionRules.TryEvolve(creature, Req(100, new MaterialStack(F2, 3)), inv, EvolutionVenue.Lobby, Make.Progression);

            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual(vessel, r.Creature.Vessel);
            Assert.AreEqual(int.MaxValue, r.Creature.Exp);
            Assert.AreEqual(7, r.Creature.InstanceId);
            Assert.AreEqual("next_form", r.Creature.SpeciesId);
        }

        // ---- C7: GameDatabase adapters (M36–M39) ----

        private T New<T>(string id) where T : GameDef
        {
            var def = ScriptableObject.CreateInstance<T>();
            def.SetId(id);
            _created.Add(def);
            return def;
        }

        private GameDatabase Database()
        {
            MoveDef moveB = New<MoveDef>("move_b");
            TraitDef traitB = New<TraitDef>("trait_b");
            var defs = new List<GameDef> { moveB, traitB };
            var creatures = new Dictionary<string, CreatureDef>();
            foreach (string id in new[] { "a", "b", "c" })
            {
                CreatureDef c = New<CreatureDef>(id);
                c.Set("line", CreatureStage.Adult, CreatureSizeClass.M, AiStyle.Hold, null, 0, new List<EvolutionMaterial>(), new int[10],
                    new List<ElementValue>(), default, moveB, traitB, "{}", false);
                creatures.Add(id, c);
                defs.Add(c);
            }

            var db = ScriptableObject.CreateInstance<GameDatabase>();
            _created.Add(db);
            db.SetContents(defs, new List<RelationEdge>
            {
                new RelationEdge(creatures["a"], creatures["b"], RelationKind.MythKin, null),
                new RelationEdge(creatures["a"], creatures["c"], RelationKind.Mentor, null),
            });
            return db;
        }

        [Test]
        public void Relation_adapter_finds_every_edge_both_ways_and_exact_ids_only()
        {
            var graph = new GameDatabaseRelationGraph(Database());

            Assert.IsTrue(graph.AreRelated("a", "b"));
            Assert.IsTrue(graph.AreRelated("a", "c"), "the second edge of a must be found");
            Assert.IsTrue(graph.AreRelated("c", "a"));
            Assert.IsFalse(graph.AreRelated("b", "c"));
            Assert.IsFalse(graph.AreRelated("a", "B"), "ids are case-sensitive");
            Assert.IsFalse(graph.AreRelated("zz", "a"));
        }

        [Test]
        public void Species_adapter_reads_move_and_trait_of_creatures_only()
        {
            var lookup = new GameDatabaseSpeciesLookup(Database());

            Assert.IsTrue(lookup.TryGet("b", out SpeciesInfo info));
            Assert.AreEqual("b", info.SpeciesId);
            Assert.AreEqual("move_b", info.SignatureMoveId);
            Assert.AreEqual("trait_b", info.TraitId);
            Assert.IsFalse(lookup.TryGet("move_b", out _), "ids of other kinds are not species");
            Assert.IsFalse(lookup.TryGet("zz", out _));
        }
    }
}
