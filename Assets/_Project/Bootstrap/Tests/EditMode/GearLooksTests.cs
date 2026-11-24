using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.Crafting;
using Cosmimera.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: docs/design/gear-looks.md — gear looks are sampled from the settlement style axes, identically on every client.
    public sealed class GearLooksTests
    {
        private const string SnapshotPath = "_Project/Bootstrap/Tests/Snapshots/gear-looks-v2.txt";

        private static ArchMix Mixed() =>
            new ArchMix(ArchRegion.Storm, ArchEra.Industrial, 0.5f, 0.6f, 0.4f).WithTertiary(ArchRegion.Twilight, ArchEra.Future, 0.3f);

        private static IEnumerable<TestCaseData> EveryKind()
        {
            foreach (GearKind kind in Enum.GetValues(typeof(GearKind))) yield return new TestCaseData(kind).SetName($"Kind_{kind}");
        }

        [TestCaseSource(nameof(EveryKind))]
        public void Same_inputs_sample_the_same_look(GearKind kind)
        {
            ArchMix mix = Mixed();
            GearLookSpec a = GearLooks.Sample(kind, GearContext.Combat, ArchRegion.Verdant, ArchEra.Agrarian, mix, 42);
            GearLookSpec b = GearLooks.Sample(kind, GearContext.Combat, ArchRegion.Verdant, ArchEra.Agrarian, mix, 42);
            Assert.That(Line(b), Is.EqualTo(Line(a)));
        }

        [TestCaseSource(nameof(EveryKind))]
        public void Ranges_hold_over_many_seeds(GearKind kind)
        {
            ArchMix mix = Mixed();
            int slots = GearLooks.SlotCount(GearLooks.FamilyOf(kind));
            for (ulong seed = 0; seed < 300; seed++)
            {
                GearLookSpec s = GearLooks.Sample(kind, (GearContext)(seed % 5), ArchRegion.Lava, ArchEra.Steampunk, mix, seed);
                Assert.That(s.PartVariants.Count, Is.EqualTo(slots));
                foreach (int v in s.PartVariants) Assert.That(v, Is.InRange(0, GearLooks.VariantsPerSlot - 1));
                Assert.That(s.Length, Is.InRange(0.7f, 1.3f));
                Assert.That(s.Width, Is.InRange(0.65f, 1.35f));
                Assert.That(s.Thickness, Is.InRange(0.58f, 1.42f));
                Assert.That(s.Curvature, Is.InRange(0f, 1f));
                Assert.That(s.MotifDensity, Is.InRange(0f, 1f));
                Assert.That(s.HueShift, Is.InRange(-0.105f, 0.105f));
                Assert.That(s.ValueShift, Is.InRange(-0.21f, 0.21f));
                Assert.That(s.Age, Is.InRange(0f, 1f));
                Assert.That(s.Damage, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void Crafted_gear_is_pure_and_brand_new()
        {
            ArchMix crafted = GearLooks.Crafted(ArchRegion.Ice, ArchEra.Mercantile);
            var pure = new GearStyle(ArchRegion.Ice, ArchEra.Mercantile);
            for (ulong seed = 0; seed < 200; seed++)
            {
                GearLookSpec s = GearLooks.Sample(GearKind.Sword, GearContext.Combat, ArchRegion.Ice, ArchEra.Mercantile, crafted, seed);
                Assert.That(s.Palette, Is.EqualTo(pure));
                Assert.That(s.Motif, Is.EqualTo(pure));
                Assert.That(s.Parts, Is.EqualTo(pure));
                Assert.That(s.Age, Is.EqualTo(0f));
                Assert.That(s.Damage, Is.EqualTo(0f));
            }
        }

        [Test]
        public void Mixed_source_borrows_secondary_and_tertiary_styles()
        {
            ArchMix mix = Mixed();
            var seen = new HashSet<GearStyle>();
            for (ulong seed = 0; seed < 400; seed++)
            {
                GearLookSpec s = GearLooks.Sample(GearKind.Upper, GearContext.Labor, ArchRegion.Verdant, ArchEra.Agrarian, mix, seed);
                seen.Add(s.Palette);
                seen.Add(s.Motif);
                seen.Add(s.Parts);
            }

            Assert.That(seen, Is.EquivalentTo(new[]
            {
                new GearStyle(ArchRegion.Verdant, ArchEra.Agrarian),
                new GearStyle(ArchRegion.Storm, ArchEra.Industrial),
                new GearStyle(ArchRegion.Twilight, ArchEra.Future),
            }));
        }

        [Test]
        public void Ceremony_gear_is_more_ornate_than_labor_gear()
        {
            ArchMix mix = Mixed();
            float ceremony = 0f, labor = 0f;
            for (ulong seed = 0; seed < 300; seed++)
            {
                ceremony += GearLooks.Sample(GearKind.Upper, GearContext.Ceremony, ArchRegion.Crystal, ArchEra.Primal, mix, seed).MotifDensity;
                labor += GearLooks.Sample(GearKind.Upper, GearContext.Labor, ArchRegion.Crystal, ArchEra.Primal, mix, seed).MotifDensity;
            }

            Assert.That(ceremony, Is.GreaterThan(labor + 300 * 0.5f));
        }

        [Test]
        public void Older_settlements_wear_their_gear_more()
        {
            var fresh = new ArchMix(ArchRegion.Storm, ArchEra.Industrial, 0.3f, 0.1f, 0.05f);
            var worn = new ArchMix(ArchRegion.Storm, ArchEra.Industrial, 0.3f, 0.8f, 0.5f);
            float freshAge = 0f, wornAge = 0f, freshDamage = 0f, wornDamage = 0f;
            for (ulong seed = 0; seed < 300; seed++)
            {
                GearLookSpec f = GearLooks.Sample(GearKind.Axe, GearContext.Labor, ArchRegion.Lava, ArchEra.Wasteland, fresh, seed);
                GearLookSpec w = GearLooks.Sample(GearKind.Axe, GearContext.Labor, ArchRegion.Lava, ArchEra.Wasteland, worn, seed);
                freshAge += f.Age;
                wornAge += w.Age;
                freshDamage += f.Damage;
                wornDamage += w.Damage;
            }

            Assert.That(wornAge, Is.GreaterThan(freshAge));
            Assert.That(wornDamage, Is.GreaterThan(freshDamage));
        }

        [Test]
        public void Every_role_maps_to_a_context()
        {
            Assert.That(GearLooks.ContextFor(NpcRole.Guard), Is.EqualTo(GearContext.Combat));
            Assert.That(GearLooks.ContextFor(NpcRole.Cleric), Is.EqualTo(GearContext.Ceremony));
            Assert.That(GearLooks.ContextFor(NpcRole.Leader), Is.EqualTo(GearContext.Status));
            Assert.That(GearLooks.ContextFor(NpcRole.Farmer), Is.EqualTo(GearContext.Labor));
            foreach (NpcRole role in Enum.GetValues(typeof(NpcRole)))
                Assert.That(Enum.IsDefined(typeof(GearContext), GearLooks.ContextFor(role)), Is.True);
        }

        [Test]
        public void Undefined_inputs_violate_preconditions()
        {
            ArchMix mix = Mixed();
            Assert.Throws<ContractException>(() => GearLooks.Sample((GearKind)99, GearContext.Combat, ArchRegion.Verdant, ArchEra.Primal, mix, 1));
            Assert.Throws<ContractException>(() => GearLooks.Sample(GearKind.Sword, (GearContext)99, ArchRegion.Verdant, ArchEra.Primal, mix, 1));
            Assert.Throws<ContractException>(() => GearLooks.Sample(GearKind.Sword, GearContext.Combat, (ArchRegion)99, ArchEra.Primal, mix, 1));
            Assert.Throws<ContractException>(() => GearLooks.FamilyOf((GearKind)99));
            Assert.Throws<ContractException>(() => GearLooks.SlotCount((GearFamily)99));
            Assert.Throws<ContractException>(() => GearLooks.ContextFor((NpcRole)99));
            Assert.Throws<ContractException>(() => new GearStyle(ArchRegion.Verdant, (ArchEra)99));
        }

        [Test]
        public void Snapshot_matches_generator_version()
        {
            Assert.That(GearLooks.GeneratorVersion, Is.EqualTo(2), "bump the snapshot file name with GeneratorVersion");
            ArchMix mix = Mixed();
            var actual = new List<string>();
            foreach (GearKind kind in Enum.GetValues(typeof(GearKind)))
            {
                for (ulong seed = 1; seed <= 3; seed++)
                    actual.Add(Line(GearLooks.Sample(kind, (GearContext)((int)kind % 5), ArchRegion.Crystal, ArchEra.Mercantile, mix, seed * 7919)));
            }

            string path = Path.Combine(Application.dataPath, SnapshotPath);
            if (!File.Exists(path))
            {
                string dump = Path.Combine(Application.dataPath, "../Logs/gear-looks-v2.actual.txt");
                File.WriteAllLines(dump, actual);
                Assert.Fail($"snapshot missing; actual written to {dump}");
            }

            Assert.That(actual, Is.EqualTo(File.ReadAllLines(path)));
        }

        // Quantised so the snapshot pins the sampling order without depending on float formatting.
        private static string Line(GearLookSpec s)
        {
            static int Q(float v) => Mathf.RoundToInt(v * 1000f);
            return string.Join(",", new[]
            {
                ((int)s.Kind).ToString(CultureInfo.InvariantCulture),
                ((int)s.Palette.Region).ToString(CultureInfo.InvariantCulture), ((int)s.Palette.Era).ToString(CultureInfo.InvariantCulture),
                ((int)s.Motif.Region).ToString(CultureInfo.InvariantCulture), ((int)s.Motif.Era).ToString(CultureInfo.InvariantCulture),
                ((int)s.Parts.Region).ToString(CultureInfo.InvariantCulture), ((int)s.Parts.Era).ToString(CultureInfo.InvariantCulture),
                string.Join("", s.PartVariants),
                Q(s.Length).ToString(CultureInfo.InvariantCulture), Q(s.Width).ToString(CultureInfo.InvariantCulture),
                Q(s.Thickness).ToString(CultureInfo.InvariantCulture), Q(s.Curvature).ToString(CultureInfo.InvariantCulture),
                Q(s.MotifDensity).ToString(CultureInfo.InvariantCulture), Q(s.HueShift).ToString(CultureInfo.InvariantCulture),
                Q(s.ValueShift).ToString(CultureInfo.InvariantCulture), Q(s.Age).ToString(CultureInfo.InvariantCulture),
                Q(s.Damage).ToString(CultureInfo.InvariantCulture),
            });
        }

        private static EquipmentSpec Spec(string equipment)
        {
            var catalog = new CraftCatalog("[{ \"id\": \"gear_a\", \"category\": \"equipment\", \"sources\": [], \"utility\": \"none\", " +
                "\"quantity\": 1, \"season\": \"all\", \"equipment\": " + equipment + " }]", "[]");
            Assert.That(catalog.TryGetItem("gear_a", out CraftItem item), Is.True);
            return item.Equipment;
        }

        private static string Weapon(string kind, string extra) =>
            "{ \"kind\": \"" + kind + "\", \"target\": \"creature\", \"player\": { \"max_hp_percent\": 0, \"move_speed_percent\": 0, " +
            "\"roll_cooldown_percent\": 0 }, \"creature_effects\": [], \"weapon\": { \"element\": \"metal\", \"element_mastery\": 1, " +
            "\"power_permille\": 1000, \"speed_permille\": 1000" + extra + " } }";

        private static string Armor(string kind) =>
            "{ \"kind\": \"" + kind + "\", \"target\": \"both\", \"player\": { \"max_hp_percent\": 0, \"move_speed_percent\": 0, " +
            "\"roll_cooldown_percent\": 0 }, \"creature_effects\": [] }";

        [TestCase("one_handed", "", GearKind.Sword)]
        [TestCase("two_handed", "", GearKind.Greatsword)]
        [TestCase("one_handed", ", \"weapon_type\": \"dagger_claw\"", GearKind.Dagger)]
        [TestCase("one_handed", ", \"weapon_type\": \"mace_1h\"", GearKind.Mace)]
        [TestCase("two_handed", ", \"weapon_type\": \"spear_halberd\"", GearKind.Spear)]
        [TestCase("two_handed", ", \"range\": \"ranged\", \"weapon_type\": \"magic_staff\"", GearKind.MagicStaff)]
        [TestCase("one_handed", ", \"range\": \"ranged\", \"weapon_type\": \"thrown_shuriken\"", GearKind.ThrowingBlade)]
        public void Weapons_draw_as_their_named_type(string kind, string extra, GearKind expected)
        {
            Assert.That(GearLooks.KindFor(Spec(Weapon(kind, extra))), Is.EqualTo(expected));
        }

        [TestCase("shield", GearKind.Shield)]
        [TestCase("upper", GearKind.Upper)]
        [TestCase("lower", GearKind.Lower)]
        [TestCase("accessory", GearKind.Accessory)]
        public void Armor_and_accessories_draw_as_their_slot(string kind, GearKind expected)
        {
            Assert.That(GearLooks.KindFor(Spec(Armor(kind))), Is.EqualTo(expected));
        }

        [Test]
        public void Every_weapon_type_maps_to_a_weapon_family()
        {
            foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
            {
                WeaponTypeSpec spec = CraftingExpansionRules.GetWeaponTypeSpec(type);
                string hands = spec.IsTwoHanded ? "two_handed" : "one_handed";
                string range = spec.Range == Cosmimera.Combat.WeaponRange.Ranged ? ", \"range\": \"ranged\"" : "";
                string id = WeaponIds[(int)type];
                GearKind kind = GearLooks.KindFor(Spec(Weapon(hands, range + ", \"weapon_type\": \"" + id + "\"")));
                Assert.That((int)kind, Is.EqualTo((int)type), id);
                Assert.That(GearLooks.FamilyOf(kind), Is.Not.EqualTo(GearFamily.Garment).And.Not.EqualTo(GearFamily.Ornament).And.Not.EqualTo(GearFamily.Shield));
            }
        }

        private static readonly string[] WeaponIds =
        {
            "dagger_claw", "sword_1h", "axe_1h", "mace_1h", "twinblade_staff", "spear_halberd", "greatsword", "greataxe_hammer",
            "thrown_shuriken", "hand_crossbow", "wand_gun", "javelin", "repeater_bow", "longbow", "magic_staff", "heavy_crossbow_cannon",
        };

        [Test]
        public void A_copy_looks_like_its_origin_planet_and_is_brand_new()
        {
            var lava = new ItemLook(new GearOrigin("lava_planet", 42), 9);
            GearLookSpec a = GearLooks.ForCopy(GearKind.Sword, lava), b = GearLooks.ForCopy(GearKind.Sword, lava);
            ArchPlanetStyle style = ArchitectureMeshes.PlanetStyleFor("lava_planet", 42);
            Assert.That(a.Palette, Is.EqualTo(new GearStyle(style.Region, style.Era)));
            Assert.That(a.Context, Is.EqualTo(GearContext.Combat));
            Assert.That(a.Age, Is.EqualTo(0f));
            Assert.That(a.Damage, Is.EqualTo(0f));
            Assert.That(b.Length, Is.EqualTo(a.Length));
            Assert.That(b.HueShift, Is.EqualTo(a.HueShift));
            Assert.That(GearLooks.ForCopy(GearKind.Sword, new ItemLook(lava.Origin, 10)).Length, Is.Not.EqualTo(a.Length));
            Assert.Throws<ContractException>(() => GearLooks.ForCopy(GearKind.Sword, default));
            Assert.Throws<ContractException>(() => GearLooks.KindFor(null));
        }

        [Test]
        public void Block_shield_derives_pattern_from_upper_and_lower_or_falls_back_to_element()
        {
            var elem = new UnityEngine.Color(0.2f, 0.5f, 0.9f);
            BlockShieldSpec plain = GearLooks.BlockShieldFor(null, null, elem);
            Assert.That(plain.HasPattern, Is.False);
            Assert.That(plain.ElementColor, Is.EqualTo(elem));

            var dmg = new ArchMix(ArchRegion.Lava, ArchEra.Industrial, 0f, 0f, 0.8f);
            GearLookSpec upper = GearLooks.Sample(GearKind.Upper, GearContext.Combat, ArchRegion.Lava, ArchEra.Agrarian,
                ArchMix.Pure(ArchRegion.Lava, ArchEra.Agrarian), 1);
            GearLookSpec lower = GearLooks.Sample(GearKind.Lower, GearContext.Combat, ArchRegion.Ice, ArchEra.Future, dmg, 2);

            BlockShieldSpec both = GearLooks.BlockShieldFor(upper, lower, elem);
            Assert.That(both.HasPattern, Is.True);
            Assert.That(both.Palette, Is.EqualTo(upper.Palette));
            Assert.That(both.Motif, Is.EqualTo(upper.Motif));
            Assert.That(both.Border, Is.EqualTo(lower.Motif));
            Assert.That(both.Damage, Is.EqualTo(lower.Damage));

            BlockShieldSpec onlyUpper = GearLooks.BlockShieldFor(upper, null, elem);
            Assert.That(onlyUpper.HasPattern, Is.True);
            Assert.That(onlyUpper.Border, Is.EqualTo(upper.Motif));

            BlockShieldSpec onlyLower = GearLooks.BlockShieldFor(null, lower, elem);
            Assert.That(onlyLower.HasPattern, Is.True);
            Assert.That(onlyLower.Palette, Is.EqualTo(lower.Palette));
            Assert.That(onlyLower.Motif, Is.EqualTo(lower.Motif));
            Assert.That(onlyLower.Border, Is.EqualTo(lower.Motif));
        }
    }
}
