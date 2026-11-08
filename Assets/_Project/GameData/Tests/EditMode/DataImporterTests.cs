using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData.Editor;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.GameData.Tests
{
    // Imports a copy of the P0-05 valid fixture so tests can mutate JSON without touching the repo,
    // and writes into a throwaway Assets folder because AssetDatabase only works under Assets/.
    public sealed class DataImporterTests
    {
        private const string OutputRoot = "Assets/__DataImporterTests";
        private static int _run;
        private string Output { get; set; }
        private string _src;

        private static string FixtureRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "_incoming", "p0-05-samples", "valid"));

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            AssetDatabase.DeleteAsset(OutputRoot);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            AssetDatabase.DeleteAsset(OutputRoot);
        }

        [SetUp]
        public void SetUp()
        {
            // FENCE: a fresh output folder per test instead of deleting and recreating one fixed folder · with
            // Windows Defender real-time scanning on, the just-deleted Assets/__DataImporterTests.meta stays
            // delete-pending while the scanner holds it, and the next test's CreateFolder failed with "Cannot open
            // file ... .meta for write" (a different test each run) · still needed if reverting to a fixed path
            // makes a full EditMode run fail about 1 in 2 times · REF: docs/roadmap/phase-2.md (2026-11-07 flake).
            Output = OutputRoot + "/run" + ++_run;
            _src = Path.Combine(Path.GetTempPath(), "cosmimera-import-" + System.Guid.NewGuid().ToString("N"));
            CopyDir(FixtureRoot, _src);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Output);
            if (Directory.Exists(_src))
            {
                Directory.Delete(_src, true);
            }
        }

        private GameDatabase Db => AssetDatabase.LoadAssetAtPath<GameDatabase>(Output + "/" + DataImporter.DatabaseFileName);

        [Test]
        public void Import_registers_every_id_with_its_kind()
        {
            DataImportReport report = DataImporter.Import(_src, Output);

            Assert.AreEqual(11, report.Created); // 6 moves + 1 trait + 1 planet type + 3 creatures
            Assert.AreEqual(11, Db.All.Count);
            Assert.IsTrue(Db.TryGet("sample_ember_strike", out MoveDef move));
            Assert.AreEqual(30, move.Power);
            Assert.AreEqual(MoveShape.Line, move.Shape);
            Assert.AreEqual(MoveMovement.Dash, move.Movement);
            Assert.AreEqual(2000, move.BaseRangeMm);
            Assert.Throws<ContractException>(() => _ = move.Origin);
            Assert.IsNull(move.Effect, "damage-only move has no effect");
            Assert.IsTrue(Db.TryGet("sample_trait_placeholder", out TraitDef trait));
            Assert.AreEqual(TriggerType.OnHpBelow, trait.Trigger.Type);
            Assert.AreEqual(30, trait.Trigger.Percent);
            Assert.AreEqual(EffectType.MasteryMod, trait.Effects[0].Type);
            Assert.AreEqual(Element.Fire, trait.Effects[0].Element);
            Assert.AreEqual(20, trait.Effects[0].Delta);
            Assert.IsTrue(Db.TryGet("sample_lava_planet", out PlanetTypeDef planet));
            CollectionAssert.AreEqual(new[] { Element.Fire, Element.Earth }, planet.ElementBias);
        }

        [Test]
        public void Creature_references_resolve_to_imported_assets()
        {
            DataImporter.Import(_src, Output);

            Assert.IsTrue(Db.TryGet("sample_fox_cub", out CreatureDef cub));
            Assert.AreEqual(CreatureStage.Juvenile, cub.Stage);
            Assert.AreEqual(CreatureSizeClass.S, cub.SizeClass);
            Assert.AreEqual(CreatureSizeClass.M, cub.EvolvesTo.SizeClass);
            Assert.AreEqual(CreatureSizeClass.L, cub.EvolvesTo.EvolvesTo.SizeClass);
            Assert.AreEqual("sample_ember_strike", cub.SignatureMove.Id);
            Assert.AreEqual("sample_trait_placeholder", cub.Trait.Id);
            Assert.AreEqual(25, cub.GetResist(Element.Fire));
            Assert.AreEqual(-25, cub.GetResist(Element.Water));
            Assert.AreEqual(100, cub.EvolutionExp);
            Assert.AreEqual("sample_fox_beast", cub.EvolvesTo.Id);
            CreatureDef lord = cub.EvolvesTo.EvolvesTo;
            Assert.AreEqual(CreatureStage.Adult, lord.Stage);
            Assert.IsNull(lord.EvolvesTo);
            Assert.AreEqual(0, lord.EvolutionExp);
            Assert.IsEmpty(lord.EvolutionMaterials);
        }

        [Test]
        public void TryGet_is_false_for_unknown_id_or_wrong_kind()
        {
            DataImporter.Import(_src, Output);

            Assert.IsFalse(Db.TryGet("no_such_id", out GameDef none));
            Assert.IsNull(none);
            Assert.IsFalse(Db.TryGet("sample_fox_cub", out MoveDef wrongKind));
            Assert.IsNull(wrongKind);
        }

        [Test]
        public void Relations_are_expanded_to_both_endpoints()
        {
            DataImporter.Import(_src, Output);

            Assert.AreEqual(2, Db.GetRelations("sample_fox_beast").Count);
            var cubLinks = Db.GetRelations("sample_fox_cub");
            Assert.AreEqual(1, cubLinks.Count);
            Assert.AreEqual("sample_fox_beast", cubLinks[0].Other.Id);
            Assert.AreEqual(RelationKind.EvolutionFamily, cubLinks[0].Kind);
            Assert.IsEmpty(Db.GetRelations("no_such_id"));
        }

        [Test]
        public void Reimport_after_json_edit_updates_the_same_asset()
        {
            DataImporter.Import(_src, Output);
            string path = Output + "/Moves/sample_ember_strike.asset";
            string guid = AssetDatabase.AssetPathToGUID(path);

            ReplaceInFile("moves/moves.json", "\"power\": 30,", "\"power\": 35,");
            DataImportReport report = DataImporter.Import(_src, Output);

            Assert.AreEqual(0, report.Created);
            Assert.AreEqual(11, report.Updated);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path), "GUID must survive so references survive");
            Assert.IsTrue(Db.TryGet("sample_ember_strike", out MoveDef move));
            Assert.AreEqual(35, move.Power);
        }

        [Test]
        public void Reimport_deletes_assets_whose_id_left_the_source()
        {
            DataImporter.Import(_src, Output);
            File.Delete(Path.Combine(_src, "planet-types", "planet-types.json"));

            DataImportReport report = DataImporter.Import(_src, Output);

            Assert.AreEqual(1, report.Deleted);
            Assert.IsFalse(File.Exists(Output + "/PlanetTypes/sample_lava_planet.asset"));
            Assert.IsFalse(Db.TryGet("sample_lava_planet", out PlanetTypeDef _));
        }

        [Test]
        public void Dangling_reference_aborts_without_writing()
        {
            DataImporter.Import(_src, Output);
            ReplaceInFile("moves/moves.json", "\"power\": 30,", "\"power\": 35,");
            ReplaceInFile("creatures/creatures.json", "\"signature_move\": \"sample_ember_strike\"", "\"signature_move\": \"missing_move\"");

            var e = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains("missing_move", e.Message);
            Assert.IsTrue(Db.TryGet("sample_ember_strike", out MoveDef move));
            Assert.AreEqual(30, move.Power, "valid edits in the same batch must not be half-applied");
        }

        [Test]
        public void Repeated_material_whose_summed_qty_overflows_int_is_rejected()
        {
            // Sol P2-05 review C2: each entry is schema-legal; EvolutionRequirement would throw when merging them.
            ReplaceInFile("creatures/creatures.json", "\"qty\": 3", "\"qty\": 2147483647 }, { \"element\": \"fire\", \"tier\": 1, \"qty\": 1");

            var e = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains("summed qty", e.Message);
        }

        [Test]
        public void Repeated_material_whose_summed_qty_fits_int_is_imported()
        {
            ReplaceInFile("creatures/creatures.json", "\"qty\": 3", "\"qty\": 2147483646 }, { \"element\": \"fire\", \"tier\": 1, \"qty\": 1");

            DataImporter.Import(_src, Output);

            Assert.IsTrue(Db.TryGet("sample_fox_cub", out CreatureDef cub));
            Assert.AreEqual(2, cub.EvolutionMaterials.Count, "the importer keeps entries; merging is EvolutionRequirement's job");
        }

        [Test]
        public void Float_literal_is_rejected()
        {
            ReplaceInFile("moves/moves.json", "\"power\": 30,", "\"power\": 30.5,");

            var e = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains("float literal", e.Message);
            Assert.IsFalse(AssetDatabase.IsValidFolder(Output));
        }

        [Test]
        public void Unknown_field_is_rejected()
        {
            ReplaceInFile("moves/moves.json", "\"power\": 30,", "\"power\": 30, \"powr\": 1,");

            var e = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains("powr", e.Message);
        }

        [Test]
        public void Unknown_enum_value_is_rejected()
        {
            ReplaceInFile("moves/moves.json", "\"shape\": \"line\"", "\"shape\": \"spiral\"");

            var e = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains("spiral", e.Message);
        }

        [Test]
        public void Rule_type_outside_vocabulary_is_rejected()
        {
            ReplaceInFile("traits/traits.json", "\"type\": \"on_hp_below\"", "\"type\": \"on_full_moon\"");

            var e = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains("on_full_moon", e.Message);
        }

        [TestCase("{ \"element\": \"any\", \"delta\": 20 }", "element")]
        [TestCase("{ \"element\": \"fire\", \"delta\": 101 }", "delta")]
        [TestCase("{ \"element\": \"fire\" }", "missing param 'delta'")]
        [TestCase("{ \"element\": \"fire\", \"delta\": 20, \"percent\": 5 }", "unknown param 'percent'")]
        public void Effect_params_must_match_their_type(string effectParams, string expected)
        {
            ReplaceInFile("traits/traits.json", "{ \"element\": \"fire\", \"delta\": 20 }", effectParams);

            var e = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains(expected, e.Message);
        }

        private const string BuffMoveHead = "[{\"id\":\"sample_buff\",\"element\":\"light\",\"kind\":\"effect\",\"power\":0,"
            + "\"cooldown_ms\":1000,\"accuracy\":100,\"range_class\":\"melee\",\"shape\":\"self\",\"base_range_mm\":0";
        private const string Haste = ",\"effect\":{\"type\":\"move_speed_mod_percent\",\"params\":{\"delta\":20}}";
        private const string Heal = ",\"effect\":{\"type\":\"heal_percent_max_hp\",\"params\":{\"percent\":20}}";

        [TestCase(Haste, ",\"effect_duration_ms\":5000", true)]
        [TestCase(Haste, "", false)]
        [TestCase(Haste, ",\"effect_duration_ms\":0", false)]
        [TestCase(Haste, ",\"effect_duration_ms\":600001", false)]
        [TestCase(Heal, "", true)]
        [TestCase(Heal, ",\"effect_duration_ms\":5000", false)]
        public void Effect_duration_is_required_iff_the_effect_is_timed(string effect, string duration, bool valid)
        {
            File.WriteAllText(Path.Combine(_src, "moves/buff.json"), BuffMoveHead + effect + duration + "}]");

            if (!valid)
            {
                var error = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));
                StringAssert.Contains("effect_duration_ms", error.Message);
                Assert.IsFalse(AssetDatabase.IsValidFolder(Output));
                return;
            }

            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_buff", out MoveDef move));
            Assert.AreEqual(effect == Haste, move.HasEffectDuration);
            if (effect == Haste)
            {
                Assert.AreEqual(5000, move.EffectDurationMs);
            }
            else
            {
                Assert.Throws<ContractException>(() => _ = move.EffectDurationMs);
            }
        }

        private const string FieldMoveHead = "[{\"id\":\"sample_field\",\"element\":\"fire\",\"kind\":\"damage\",\"power\":10,"
            + "\"cooldown_ms\":8000,\"accuracy\":100,\"range_class\":\"mid\",\"base_range_mm\":12000";

        [TestCase(",\"shape\":\"field\",\"field_duration_ms\":6000", true)]
        [TestCase(",\"shape\":\"field\",\"field_duration_ms\":600000", true)]
        [TestCase(",\"shape\":\"field\"", false)]
        [TestCase(",\"shape\":\"field\",\"field_duration_ms\":0", false)]
        [TestCase(",\"shape\":\"field\",\"field_duration_ms\":600001", false)]
        [TestCase(",\"shape\":\"line\",\"field_duration_ms\":6000", false)]
        public void Field_duration_is_required_iff_the_shape_is_field(string tail, bool valid)
        {
            File.WriteAllText(Path.Combine(_src, "moves/field.json"), FieldMoveHead + tail + "}]");

            if (!valid)
            {
                var error = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));
                StringAssert.Contains("field_duration_ms", error.Message);
                return;
            }

            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_field", out MoveDef move));
            Assert.Greater(move.FieldDurationMs, 0);
        }

        [Test]
        public void Non_field_move_has_no_field_duration()
        {
            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_ember_strike", out MoveDef move));

            Assert.Throws<ContractException>(() => _ = move.FieldDurationMs);
        }

        [TestCase(Haste, "", 1)]
        [TestCase(",\"effect\":{\"type\":\"move_speed_mod_percent\",\"params\":{\"delta\":20},\"max_stacks\":1}", "", 1)]
        [TestCase(",\"effect\":{\"type\":\"move_speed_mod_percent\",\"params\":{\"delta\":20},\"max_stacks\":10}", "", 10)]
        [TestCase(",\"effect\":{\"type\":\"move_speed_mod_percent\",\"params\":{\"delta\":20},\"max_stacks\":0}", "max_stacks", 0)]
        [TestCase(",\"effect\":{\"type\":\"move_speed_mod_percent\",\"params\":{\"delta\":20},\"max_stacks\":11}", "max_stacks", 0)]
        [TestCase(",\"effect\":{\"type\":\"heal_percent_max_hp\",\"params\":{\"percent\":20},\"max_stacks\":2}", "max_stacks", 0)]
        public void Max_stacks_defaults_to_one_and_is_bounded(string effect, string expectedError, int expectedStacks)
        {
            string duration = effect.Contains("heal_percent") ? "" : ",\"effect_duration_ms\":5000";
            File.WriteAllText(Path.Combine(_src, "moves/buff.json"), BuffMoveHead + effect + duration + "}]");

            if (expectedError.Length > 0)
            {
                var error = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));
                StringAssert.Contains(expectedError, error.Message);
                return;
            }

            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_buff", out MoveDef move));
            Assert.AreEqual(expectedStacks, move.Effect.MaxStacks);
        }

        [Test]
        public void Max_stacks_on_a_trigger_is_rejected()
        {
            ReplaceInFile("traits/traits.json", "\"type\": \"on_hp_below\"", "\"max_stacks\": 2, \"type\": \"on_hp_below\"");

            var error = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains("does not accept max_stacks", error.Message);
        }

        [Test]
        public void Melee_target_point_burst_is_rejected()
        {
            File.WriteAllText(Path.Combine(_src, "moves/blast.json"),
                "[{\"id\":\"sample_blast\",\"element\":\"fire\",\"kind\":\"damage\",\"power\":30,\"cooldown_ms\":1000,"
                + "\"accuracy\":100,\"range_class\":\"melee\",\"shape\":\"burst\",\"origin\":\"target\",\"base_range_mm\":3000}]");

            var error = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains("target-point burst", error.Message);
        }

        [Test]
        public void Damage_move_has_no_effect_duration()
        {
            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_ember_strike", out MoveDef move));

            Assert.IsFalse(move.HasEffectDuration);
            Assert.Throws<ContractException>(() => _ = move.EffectDurationMs);
        }

        [Test]
        public void Reading_a_param_the_rule_type_lacks_is_a_contract_violation()
        {
            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_trait_placeholder", out TraitDef trait));

            Assert.Throws<ContractException>(() => _ = trait.Trigger.DurationMs);
            Assert.Throws<ContractException>(() => _ = trait.Trigger.Element);
            Assert.Throws<ContractException>(() => _ = trait.Effects[0].Percent);
            Assert.IsFalse(trait.Effects[0].AnyElement);
        }

        [TestCase("percent", "9223372036854775808")]
        [TestCase("percent", "-9223372036854775809")]
        [TestCase("duration_ms", "9223372036854775808")]
        [TestCase("duration_ms", "-9223372036854775809")]
        [TestCase("delta", "9223372036854775808")]
        [TestCase("delta", "-9223372036854775809")]
        public void Huge_rule_integer_is_reported_without_writing(string parameter, string value)
        {
            string trigger = parameter == "duration_ms"
                ? "{\"type\":\"on_summon\",\"params\":{\"duration_ms\":" + value + "}}"
                : "{\"type\":\"on_hp_below\",\"params\":{\"percent\":" + (parameter == "percent" ? value : "30") + "}}";
            string effect = "{\"type\":\"mastery_mod\",\"params\":{\"element\":\"fire\",\"delta\":"
                + (parameter == "delta" ? value : "20") + "}}";
            File.WriteAllText(Path.Combine(_src, "traits/traits.json"),
                "[{\"id\":\"sample_trait_placeholder\",\"trigger\":" + trigger + ",\"effects\":[" + effect + "]}]");

            var error = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));

            StringAssert.Contains(parameter, error.Message);
            Assert.IsFalse(AssetDatabase.IsValidFolder(Output));
        }

        [Test]
        public void Hp_cross_below_imports_percent_and_duration()
        {
            File.WriteAllText(Path.Combine(_src, "traits/traits.json"),
                "[{\"id\":\"sample_trait_placeholder\",\"trigger\":{\"type\":\"on_hp_cross_below\",\"params\":"
                + "{\"percent\":25,\"duration_ms\":4000}},\"effects\":[{\"type\":\"heal_percent_max_hp\",\"params\":{\"percent\":12}}]}]");

            DataImporter.Import(_src, Output);

            Assert.IsTrue(Db.TryGet("sample_trait_placeholder", out TraitDef trait));
            Assert.AreEqual(TriggerType.OnHpCrossBelow, trait.Trigger.Type);
            Assert.AreEqual(25, trait.Trigger.Percent);
            Assert.AreEqual(4000, trait.Trigger.DurationMs);
            Assert.IsFalse(trait.Trigger.HasElement);
        }

        // SOL P2-09 E4: schema-valid data that EffectBook rejected must already fail at import.
        [TestCase("{\"type\":\"always\"}", 1, false, "")]
        [TestCase("{\"type\":\"on_hp_below\",\"params\":{\"percent\":30}}", 1, true, "event trigger")]
        [TestCase("{\"type\":\"on_hp_above\",\"params\":{\"percent\":30}}", 1, true, "event trigger")]
        [TestCase("{\"type\":\"always\"}", 1, true, "event trigger")]
        [TestCase("{\"type\":\"on_summon\",\"params\":{\"duration_ms\":1000}}", 1, true, "")]
        [TestCase("{\"type\":\"on_hp_cross_below\",\"params\":{\"percent\":25,\"duration_ms\":1000}}", 1, true, "")]
        [TestCase("{\"type\":\"on_hp_cross_below\",\"params\":{\"percent\":25}}", 1, true, "duration_ms")]
        [TestCase("{\"type\":\"on_summon\",\"params\":{\"duration_ms\":1000}}", 8, false, "")]
        [TestCase("{\"type\":\"on_summon\",\"params\":{\"duration_ms\":1000}}", 9, false, "effects must have")]
        public void Trait_effects_are_bounded_and_heals_need_an_event_trigger(string trigger, int count, bool heal, string expectedError)
        {
            string effect = heal
                ? "{\"type\":\"heal_percent_max_hp\",\"params\":{\"percent\":10}}"
                : "{\"type\":\"mastery_mod\",\"params\":{\"element\":\"fire\",\"delta\":20}}";
            var effects = new System.Collections.Generic.List<string>();
            for (int i = 0; i < count; i++)
            {
                effects.Add(effect);
            }

            File.WriteAllText(Path.Combine(_src, "traits/traits.json"),
                "[{\"id\":\"sample_trait_placeholder\",\"trigger\":" + trigger + ",\"effects\":[" + string.Join(",", effects) + "]}]");

            if (expectedError.Length > 0)
            {
                var error = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));
                StringAssert.Contains(expectedError, error.Message);
                Assert.IsFalse(AssetDatabase.IsValidFolder(Output));
                return;
            }

            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_trait_placeholder", out TraitDef trait));
            Assert.AreEqual(count, trait.Effects.Count);
        }

        [TestCase("{\"type\":\"always\"}", true)]
        [TestCase("{\"type\":\"always\",\"params\":{}}", true)]
        [TestCase("{\"type\":\"always\",\"params\":null}", false)]
        public void Always_trigger_accepts_omitted_or_empty_params_and_rejects_null(string trigger, bool valid)
        {
            File.WriteAllText(Path.Combine(_src, "traits/traits.json"),
                "[{\"id\":\"sample_trait_placeholder\",\"trigger\":" + trigger
                + ",\"effects\":[{\"type\":\"mastery_mod\",\"params\":{\"element\":\"fire\",\"delta\":20}}]}]");

            if (!valid)
            {
                var error = Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));
                StringAssert.Contains("params", error.Message);
                Assert.IsFalse(AssetDatabase.IsValidFolder(Output));
                return;
            }

            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_trait_placeholder", out TraitDef trait));
            Assert.AreEqual(TriggerType.Always, trait.Trigger.Type);
        }

        [Test]
        public void Schema_damage_bounds_and_imported_maximum_values_agree()
        {
            string schemas = Path.Combine(Application.dataPath, "..", "Data", "schemas");
            JObject common = JObject.Parse(File.ReadAllText(Path.Combine(schemas, "common.schema.json")));
            JObject moves = JObject.Parse(File.ReadAllText(Path.Combine(schemas, "move.schema.json")));
            JObject creatures = JObject.Parse(File.ReadAllText(Path.Combine(schemas, "creature.schema.json")));
            Assert.AreEqual(DamageModel.MaxPower, (int)common["$defs"]["movePower"]["maximum"]);
            Assert.AreEqual(DamageModel.MaxMasteryPercent, (int)common["$defs"]["masteryPercent"]["maximum"]);
            Assert.AreEqual("common.schema.json#/$defs/movePower", (string)moves["$defs"]["move"]["properties"]["power"]["$ref"]);
            Assert.AreEqual("common.schema.json#/$defs/masteryPercent", (string)creatures["$defs"]["creature"]["properties"]["mastery"]["additionalProperties"]["$ref"]);
            ReplaceInFile("moves/moves.json", "\"power\": 30,", "\"power\": " + DamageModel.MaxPower + ",");
            ReplaceInFile("creatures/creatures.json", "\"fire\": 15", "\"fire\": " + DamageModel.MaxMasteryPercent);

            DataImporter.Import(_src, Output);

            Assert.IsTrue(Db.TryGet("sample_ember_strike", out MoveDef move));
            Assert.IsTrue(Db.TryGet("sample_fox_cub", out CreatureDef cub));
            Assert.AreEqual(DamageModel.MaxPower, move.Power);
            Assert.AreEqual(DamageModel.MaxMasteryPercent, cub.Mastery[0].Value);
            Assert.Greater(DamageModel.Compute(move.Power, cub.Mastery[0].Value, -100), 0);
            Assert.AreEqual(0, DamageModel.Compute(move.Power, cub.Mastery[0].Value, 100));
        }

        [TestCase("front", MoveShape.Front)]
        [TestCase("sweep", MoveShape.Sweep)]
        [TestCase("burst", MoveShape.Burst)]
        [TestCase("line", MoveShape.Line)]
        [TestCase("cone", MoveShape.Cone)]
        [TestCase("arc", MoveShape.Arc)]
        [TestCase("self", MoveShape.Self)]
        [TestCase("ally", MoveShape.Ally)]
        [TestCase("field", MoveShape.Field)]
        public void Every_move_shape_survives_asset_reload(string shape, MoveShape expected)
        {
            JObject move = (JObject)JArray.Parse(File.ReadAllText(Path.Combine(_src, "moves/moves.json")))[0];
            move["shape"] = shape;
            move["movement"] = "none";
            if (shape == "burst")
            {
                move["origin"] = "target";
                move["range_class"] = "mid"; // a target-point burst cannot be melee
            }

            if (shape == "field")
            {
                move["field_duration_ms"] = 6000; // Q-C17: required iff field
            }

            JArray all = JArray.Parse(File.ReadAllText(Path.Combine(_src, "moves/moves.json")));
            all[0] = move;
            File.WriteAllText(Path.Combine(_src, "moves/moves.json"), all.ToString());

            DataImporter.Import(_src, Output);
            string path = Output + "/Moves/sample_ember_strike.asset";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var actual = AssetDatabase.LoadAssetAtPath<MoveDef>(path);

            Assert.AreEqual(expected, actual.Shape);
            Assert.AreEqual(2000, actual.BaseRangeMm);
            Assert.AreEqual(MoveMovement.None, actual.Movement);
            if (shape == "burst") Assert.AreEqual(BurstOrigin.Target, actual.Origin);
            else Assert.Throws<ContractException>(() => _ = actual.Origin);
            if (shape == "field") Assert.AreEqual(6000, actual.FieldDurationMs);
            else Assert.Throws<ContractException>(() => _ = actual.FieldDurationMs);
        }

        [TestCase("XS", CreatureSizeClass.XS)]
        [TestCase("S", CreatureSizeClass.S)]
        [TestCase("M", CreatureSizeClass.M)]
        [TestCase("L", CreatureSizeClass.L)]
        [TestCase("XL", CreatureSizeClass.XL)]
        public void Every_creature_size_survives_import(string size, CreatureSizeClass expected)
        {
            ReplaceInFile("creatures/creatures.json", "\"size_class\": \"S\"", "\"size_class\": \"" + size + "\"");
            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_fox_cub", out CreatureDef cub));
            Assert.AreEqual(expected, cub.SizeClass);
        }

        [TestCase("s")]
        [TestCase("xs")]
        [TestCase("3")]
        [TestCase("XXL")]
        public void Invalid_creature_size_aborts_without_writing(string size)
        {
            ReplaceInFile("creatures/creatures.json", "\"size_class\": \"S\"", "\"size_class\": \"" + size + "\"");
            Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));
            Assert.IsFalse(AssetDatabase.IsValidFolder(Output));
        }

        [TestCase("keep_distance", AiStyle.KeepDistance)]
        [TestCase("flank", AiStyle.Flank)]
        [TestCase("charge", AiStyle.Charge)]
        [TestCase("hold", AiStyle.Hold)]
        public void Every_ai_style_survives_import(string style, AiStyle expected)
        {
            ReplaceInFile("creatures/creatures.json", "\"ai_style\": \"charge\"", "\"ai_style\": \"" + style + "\"");
            DataImporter.Import(_src, Output);
            Assert.IsTrue(Db.TryGet("sample_fox_cub", out CreatureDef cub));
            Assert.AreEqual(expected, cub.AiStyle);
            Assert.AreEqual(AiStyle.Hold, cub.EvolvesTo.AiStyle);
            Assert.AreEqual(AiStyle.KeepDistance, cub.EvolvesTo.EvolvesTo.AiStyle);
        }

        [TestCase("Charge")]
        [TestCase("0")]
        [TestCase("kite")]
        public void Invalid_ai_style_aborts_without_writing(string style)
        {
            ReplaceInFile("creatures/creatures.json", "\"ai_style\": \"charge\"", "\"ai_style\": \"" + style + "\"");
            Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));
            Assert.IsFalse(AssetDatabase.IsValidFolder(Output));
        }

        [TestCase("aoe", "none", 2000, null)]
        [TestCase("dash", "none", 2000, null)]
        [TestCase("cone", "dash", 2000, null)]
        [TestCase("line", "dash", 0, null)]
        [TestCase("line", "dash", 36001, null)]
        [TestCase("line", "none", -1, null)]
        [TestCase("line", "none", 2000, "caster")]
        [TestCase("burst", "none", 2000, null)]
        [TestCase("burst", "none", 2000, "unknown")]
        [TestCase("line", "teleport", 2000, null)]
        public void Invalid_move_contract_aborts_without_writing(string shape, string movement, int range, string origin)
        {
            JArray moves = JArray.Parse(File.ReadAllText(Path.Combine(_src, "moves/moves.json")));
            moves[0]["shape"] = shape;
            moves[0]["movement"] = movement;
            moves[0]["base_range_mm"] = range;
            if (origin != null) moves[0]["origin"] = origin;
            File.WriteAllText(Path.Combine(_src, "moves/moves.json"), moves.ToString());

            Assert.Throws<DataImportException>(() => DataImporter.Import(_src, Output));
            Assert.IsFalse(AssetDatabase.IsValidFolder(Output));
        }

        [TestCase("p5-05-moves-draft-01", 40)]
        [TestCase("p5-05-moves-draft-02", 40)]
        [TestCase("p5-05-moves-draft-03", 40)]
        [TestCase("p5-05-moves-draft-04", 30)]
        public void Migrated_move_batches_round_trip_every_move(string batch, int expected)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "_incoming", batch));
            JArray source = JArray.Parse(File.ReadAllText(Path.Combine(root, "moves/moves.json")));

            DataImporter.Import(root, Output);

            Assert.AreEqual(expected, Db.All.Count);
            foreach (JToken definition in source)
            {
                Assert.IsTrue(Db.TryGet((string)definition["id"], out MoveDef move));
                Assert.AreEqual((string)definition["shape"], move.Shape.ToString().ToLowerInvariant());
                Assert.AreEqual((int)definition["base_range_mm"], move.BaseRangeMm);
                Assert.AreEqual((string)definition["movement"] ?? "none", move.Movement.ToString().ToLowerInvariant());
                if (move.Shape == MoveShape.Burst)
                {
                    Assert.AreEqual((string)definition["origin"], move.Origin.ToString().ToLowerInvariant());
                }
            }
        }

        [Test]
        public void Import_requires_existing_source_root()
        {
            Assert.Throws<ContractException>(() => DataImporter.Import(Path.Combine(_src, "nope"), Output));
        }

        [Test]
        public void Import_requires_output_under_assets()
        {
            Assert.Throws<ContractException>(() => DataImporter.Import(_src, "Packages/foo"));
        }

        [Test]
        public void Database_queries_require_non_null_id()
        {
            DataImporter.Import(_src, Output);

            Assert.Throws<ContractException>(() => Db.TryGet(null, out GameDef _));
            Assert.Throws<ContractException>(() => Db.GetRelations(null));
        }

        private void ReplaceInFile(string relative, string from, string to)
        {
            string path = Path.Combine(_src, relative);
            string text = File.ReadAllText(path);
            Assert.IsTrue(text.Contains(from), $"fixture changed: '{from}' not found in {relative}");
            File.WriteAllText(path, text.Replace(from, to));
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            }

            foreach (string dir in Directory.GetDirectories(from))
            {
                CopyDir(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }
    }
}
