using System.Collections.Generic;
using System.Reflection;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Creatures.Tests
{
    public sealed class EvolutionRequirementTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MaterialKey Fire1 = new MaterialKey(Element.Fire, 1);
        private static readonly MaterialKey Light2 = new MaterialKey(Element.Light, 2);

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        [Test]
        public void Constructor_exposes_values()
        {
            var r = new EvolutionRequirement(100, new[] { new MaterialStack(Fire1, 3), new MaterialStack(Light2, 2) }, "next");
            Assert.AreEqual(100, r.RequiredExp);
            Assert.AreEqual("next", r.NextSpeciesId);
            Assert.IsFalse(r.IsFinal);
            Assert.AreEqual(2, r.Materials.Count);
            Assert.AreEqual(Fire1, r.Materials[0].Key);
            Assert.AreEqual(3, r.Materials[0].Qty);
        }

        [Test]
        public void Zero_exp_and_no_materials_are_allowed()
        {
            var r = new EvolutionRequirement(0, new MaterialStack[0], "next");
            Assert.AreEqual(0, r.RequiredExp);
            Assert.IsEmpty(r.Materials);
        }

        [Test]
        public void Default_value_is_the_final_requirement()
        {
            EvolutionRequirement r = default;
            Assert.IsTrue(r.IsFinal);
            Assert.IsNull(r.NextSpeciesId);
            Assert.IsNotNull(r.Materials);
            Assert.IsEmpty(r.Materials);
            Assert.AreEqual(0, r.RequiredExp);
        }

        [Test]
        public void Repeated_keys_are_merged_in_first_seen_order()
        {
            var r = new EvolutionRequirement(1, new[]
            {
                new MaterialStack(Fire1, 2), new MaterialStack(Light2, 1), new MaterialStack(Fire1, 3),
            }, "next");
            Assert.AreEqual(2, r.Materials.Count);
            Assert.AreEqual(Fire1, r.Materials[0].Key);
            Assert.AreEqual(5, r.Materials[0].Qty);
            Assert.AreEqual(Light2, r.Materials[1].Key);
        }

        [Test]
        public void Merged_quantity_may_reach_int_max_but_not_exceed_it()
        {
            Assert.DoesNotThrow(() => new EvolutionRequirement(1, new[] { new MaterialStack(Fire1, int.MaxValue - 1), new MaterialStack(Fire1, 1) }, "n"));
            Assert.Throws<ContractException>(() => new EvolutionRequirement(1, new[] { new MaterialStack(Fire1, int.MaxValue), new MaterialStack(Fire1, 1) }, "n"));
        }

        [Test]
        public void Input_list_is_copied_and_materials_are_read_only()
        {
            var source = new List<MaterialStack> { new MaterialStack(Fire1, 1) };
            var r = new EvolutionRequirement(1, source, "next");
            source.Add(new MaterialStack(Light2, 1));
            Assert.AreEqual(1, r.Materials.Count);
            Assert.Throws<System.NotSupportedException>(() => ((IList<MaterialStack>)r.Materials).Add(new MaterialStack(Light2, 1)));
        }

        [Test]
        public void Constructor_preconditions()
        {
            var ok = new[] { new MaterialStack(Fire1, 1) };
            Assert.Throws<ContractException>(() => new EvolutionRequirement(-1, ok, "n"));
            Assert.Throws<ContractException>(() => new EvolutionRequirement(1, null, "n"));
            Assert.Throws<ContractException>(() => new EvolutionRequirement(1, ok, null));
            Assert.Throws<ContractException>(() => new EvolutionRequirement(1, ok, ""));
            Assert.Throws<ContractException>(() => new EvolutionRequirement(1, new[] { default(MaterialStack) }, "n"), "unconstructed stack");
        }

        [Test]
        public void FromDef_null_is_a_contract_violation()
        {
            Assert.Throws<ContractException>(() => EvolutionRequirement.FromDef(null));
        }

        [Test]
        public void FromDef_adult_is_final()
        {
            CreatureDef adult = NewDef("fox_adult");
            EvolutionRequirement r = EvolutionRequirement.FromDef(adult);
            Assert.IsTrue(r.IsFinal);
            Assert.IsEmpty(r.Materials);
        }

        // GameData setters are internal, so the def is filled through its serialized private fields; the field
        // names are the contract with CreatureDef/GameDef (see GameData/CreatureDef.cs).
        [Test]
        public void FromDef_non_adult_mirrors_exp_materials_and_next_id()
        {
            CreatureDef next = NewDef("fox_adolescent");
            CreatureDef juvenile = NewDef("fox_juvenile");
            Set(juvenile, "evolvesTo", next);
            Set(juvenile, "evolutionExp", 120);
            Set(juvenile, "evolutionMaterials", new List<EvolutionMaterial>
            {
                Material(Element.Fire, 1, 3),
                Material(Element.Light, 2, 2),
            });

            EvolutionRequirement r = EvolutionRequirement.FromDef(juvenile);

            Assert.IsFalse(r.IsFinal);
            Assert.AreEqual("fox_adolescent", r.NextSpeciesId);
            Assert.AreEqual(120, r.RequiredExp);
            Assert.AreEqual(2, r.Materials.Count);
            Assert.AreEqual(new MaterialKey(Element.Fire, 1), r.Materials[0].Key);
            Assert.AreEqual(3, r.Materials[0].Qty);
            Assert.AreEqual(new MaterialKey(Element.Light, 2), r.Materials[1].Key);
            Assert.AreEqual(2, r.Materials[1].Qty);
        }

        private CreatureDef NewDef(string id)
        {
            var def = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(def);
            typeof(GameDef).GetField("id", Private).SetValue(def, id);
            return def;
        }

        private static void Set(CreatureDef def, string field, object value)
        {
            typeof(CreatureDef).GetField(field, Private).SetValue(def, value);
        }

        private static EvolutionMaterial Material(Element element, int tier, int qty)
        {
            object boxed = new EvolutionMaterial();
            typeof(EvolutionMaterial).GetField("element", Private).SetValue(boxed, element);
            typeof(EvolutionMaterial).GetField("tier", Private).SetValue(boxed, tier);
            typeof(EvolutionMaterial).GetField("qty", Private).SetValue(boxed, qty);
            return (EvolutionMaterial)boxed;
        }
    }
}
