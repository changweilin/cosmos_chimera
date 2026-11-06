using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Creatures.Tests
{
    public sealed class EvolutionRulesTests
    {
        private static readonly MaterialKey Fire1 = new MaterialKey(Element.Fire, 1);
        private static readonly MaterialKey Wind1 = new MaterialKey(Element.Wind, 1);

        private static EvolutionRequirement Req(int exp = 100) => new EvolutionRequirement(exp,
            new List<MaterialStack> { new MaterialStack(Fire1, 3), new MaterialStack(Wind1, 2) }, "fox_adolescent");

        private static MaterialInventory Stocked(int fire = 3, int wind = 2)
        {
            var inv = new MaterialInventory();
            if (fire > 0)
            {
                inv.Add(Fire1, fire);
            }

            if (wind > 0)
            {
                inv.Add(Wind1, wind);
            }

            return inv;
        }

        [Test]
        public void Exact_requirements_evolve_and_consume_exactly()
        {
            OwnedCreature fox = Make.Creature(7, "fox_juvenile", VesselKind.TalismanCard, 100);
            MaterialInventory inv = Stocked(5, 2);

            EvolutionResult r = EvolutionRules.TryEvolve(fox, Req(), inv, EvolutionVenue.Lobby);

            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual("fox_adolescent", r.Creature.SpeciesId);
            Assert.AreEqual(7, r.Creature.InstanceId);
            Assert.AreEqual(VesselKind.TalismanCard, r.Creature.Vessel);
            Assert.AreEqual(100, r.Creature.Exp, "experience is a threshold, not consumed");
            Assert.AreEqual(2, inv.Count(Fire1));
            Assert.AreEqual(0, inv.Count(Wind1));
        }

        [Test]
        public void Every_missing_item_is_reported_at_once()
        {
            OwnedCreature fox = Make.Creature(1, "fox_juvenile", exp: 60);
            MaterialInventory inv = Stocked(1, 0);

            EvolutionCheck c = EvolutionRules.Check(fox, Req(), inv, EvolutionVenue.Planet);

            Assert.IsFalse(c.CanEvolve);
            Assert.IsTrue(c.NotInLobby);
            Assert.AreEqual(40, c.ExpShortBy);
            Assert.AreEqual(2, c.Shortfalls.Count);
            Assert.AreEqual(Fire1, c.Shortfalls[0].Key);
            Assert.AreEqual(1, c.Shortfalls[0].Have);
            Assert.AreEqual(2, c.Shortfalls[0].Missing);
            Assert.AreEqual(Wind1, c.Shortfalls[1].Key);
            Assert.AreEqual(2, c.Shortfalls[1].Missing);
        }

        [TestCase(99, 3, 2, EvolutionVenue.Lobby)]
        [TestCase(100, 2, 2, EvolutionVenue.Lobby)]
        [TestCase(100, 3, 1, EvolutionVenue.Lobby)]
        [TestCase(100, 3, 2, EvolutionVenue.Planet)]
        public void Any_single_blocker_refuses_and_consumes_nothing(int exp, int fire, int wind, EvolutionVenue venue)
        {
            OwnedCreature fox = Make.Creature(1, "fox_juvenile", exp: exp);
            MaterialInventory inv = Stocked(fire, wind);

            EvolutionResult r = EvolutionRules.TryEvolve(fox, Req(), inv, venue);

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual("fox_juvenile", r.Creature.SpeciesId);
            Assert.AreEqual(fire, inv.Count(Fire1));
            Assert.AreEqual(wind, inv.Count(Wind1));
        }

        [Test]
        public void Higher_tier_does_not_substitute()
        {
            var inv = new MaterialInventory();
            inv.Add(new MaterialKey(Element.Fire, 2), 10);
            inv.Add(Wind1, 2);

            EvolutionCheck c = EvolutionRules.Check(Make.Creature(1, "fox_juvenile", exp: 100), Req(), inv, EvolutionVenue.Lobby);

            Assert.AreEqual(1, c.Shortfalls.Count);
            Assert.AreEqual(Fire1, c.Shortfalls[0].Key);
        }

        [Test]
        public void Final_stage_cannot_evolve()
        {
            EvolutionResult r = EvolutionRules.TryEvolve(Make.Creature(1, "fox_adult", exp: 99999), default,
                Stocked(), EvolutionVenue.Lobby);

            Assert.IsFalse(r.Succeeded);
            Assert.IsTrue(r.Check.AlreadyFinal);
            Assert.IsNull(r.Check.NextSpeciesId);
            Assert.AreEqual(0, r.Check.ExpShortBy);
            Assert.IsEmpty(r.Check.Shortfalls);
        }

        [Test]
        public void Zero_exp_requirement_with_no_materials_evolves()
        {
            var req = new EvolutionRequirement(0, new List<MaterialStack>(), "next");
            Assert.IsTrue(EvolutionRules.TryEvolve(Make.Creature(1, "a"), req, new MaterialInventory(), EvolutionVenue.Lobby).Succeeded);
        }

        [Test]
        public void Preconditions()
        {
            Assert.Throws<ContractException>(() => EvolutionRules.Check(default, Req(), Stocked(), EvolutionVenue.Lobby));
            Assert.Throws<ContractException>(() => EvolutionRules.Check(Make.Creature(1, "a"), Req(), null, EvolutionVenue.Lobby));
            Assert.Throws<ContractException>(() => EvolutionRules.Check(Make.Creature(1, "a"), Req(), Stocked(), (EvolutionVenue)9));
            Assert.Throws<ContractException>(() => EvolutionRules.TryEvolve(default, Req(), Stocked(), EvolutionVenue.Lobby));
        }
    }
}
