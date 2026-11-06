using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Creatures.Tests
{
    public sealed class OwnedCreatureTests
    {
        [Test]
        public void Constructor_stores_all_fields()
        {
            var c = new OwnedCreature(7, "fox_juvenile", VesselKind.MagicCube, 250);
            Assert.AreEqual(7, c.InstanceId);
            Assert.AreEqual("fox_juvenile", c.SpeciesId);
            Assert.AreEqual(VesselKind.MagicCube, c.Vessel);
            Assert.AreEqual(250, c.Exp);
        }

        [Test]
        public void Constructor_boundaries_are_inclusive()
        {
            Assert.DoesNotThrow(() => new OwnedCreature(1, "a", VesselKind.TalismanCard, 0));
            Assert.DoesNotThrow(() => new OwnedCreature(int.MaxValue, "a", VesselKind.TalismanCard, int.MaxValue));
        }

        [Test]
        public void Constructor_rejects_each_invalid_argument()
        {
            Assert.Throws<ContractException>(() => new OwnedCreature(0, "a", VesselKind.GlassBead, 0));
            Assert.Throws<ContractException>(() => new OwnedCreature(-1, "a", VesselKind.GlassBead, 0));
            Assert.Throws<ContractException>(() => new OwnedCreature(1, null, VesselKind.GlassBead, 0));
            Assert.Throws<ContractException>(() => new OwnedCreature(1, "", VesselKind.GlassBead, 0));
            Assert.Throws<ContractException>(() => new OwnedCreature(1, "a", (VesselKind)3, 0));
            Assert.Throws<ContractException>(() => new OwnedCreature(1, "a", (VesselKind)(-1), 0));
            Assert.Throws<ContractException>(() => new OwnedCreature(1, "a", VesselKind.GlassBead, -1));
        }

        [Test]
        public void WithSpecies_changes_only_the_species()
        {
            var c = new OwnedCreature(4, "a", VesselKind.TalismanCard, 99);
            OwnedCreature next = c.WithSpecies("b");
            Assert.AreEqual("b", next.SpeciesId);
            Assert.AreEqual(4, next.InstanceId);
            Assert.AreEqual(VesselKind.TalismanCard, next.Vessel);
            Assert.AreEqual(99, next.Exp);
            Assert.AreEqual("a", c.SpeciesId, "value type: the original is untouched");
        }

        [Test]
        public void WithSpecies_preconditions()
        {
            var c = new OwnedCreature(4, "a", VesselKind.TalismanCard, 0);
            Assert.Throws<ContractException>(() => c.WithSpecies(null));
            Assert.Throws<ContractException>(() => c.WithSpecies(""));
            Assert.Throws<ContractException>(() => default(OwnedCreature).WithSpecies("b"));
        }

        [Test]
        public void MaterialKey_validates_and_has_value_equality()
        {
            Assert.AreEqual(new MaterialKey(Element.Fire, 2), new MaterialKey(Element.Fire, 2));
            Assert.AreEqual(new MaterialKey(Element.Fire, 2).GetHashCode(), new MaterialKey(Element.Fire, 2).GetHashCode());
            Assert.AreNotEqual(new MaterialKey(Element.Fire, 2), new MaterialKey(Element.Fire, 1));
            Assert.AreNotEqual(new MaterialKey(Element.Fire, 2), new MaterialKey(Element.Ice, 2));
            Assert.DoesNotThrow(() => new MaterialKey(Element.Metal, 1));
            Assert.Throws<ContractException>(() => new MaterialKey(Element.Fire, 0));
            Assert.Throws<ContractException>(() => new MaterialKey(Element.Fire, -1));
            Assert.Throws<ContractException>(() => new MaterialKey((Element)10, 1));
            Assert.Throws<ContractException>(() => new MaterialKey((Element)(-1), 1));
        }

        [Test]
        public void MaterialStack_validates()
        {
            var key = new MaterialKey(Element.Wood, 1);
            Assert.AreEqual(1, new MaterialStack(key, 1).Qty);
            Assert.Throws<ContractException>(() => new MaterialStack(key, 0));
            Assert.Throws<ContractException>(() => new MaterialStack(default, 1));
        }

        [Test]
        public void MaterialShortfall_validates_and_reports_missing()
        {
            var key = new MaterialKey(Element.Wood, 1);
            var s = new MaterialShortfall(key, 1, 4);
            Assert.AreEqual(3, s.Missing);
            Assert.DoesNotThrow(() => new MaterialShortfall(key, 0, 1));
            Assert.Throws<ContractException>(() => new MaterialShortfall(key, 4, 4));
            Assert.Throws<ContractException>(() => new MaterialShortfall(key, -1, 4));
            Assert.Throws<ContractException>(() => new MaterialShortfall(default, 0, 1));
        }
    }
}
