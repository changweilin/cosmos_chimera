using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.GameData.Tests
{
    // Imports a copy of the P0-05 valid fixture so tests can mutate JSON without touching the repo,
    // and writes into a throwaway Assets folder because AssetDatabase only works under Assets/.
    public sealed class DataImporterTests
    {
        private const string Output = "Assets/__DataImporterTests";
        private string _src;

        private static string FixtureRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "_incoming", "p0-05-samples", "valid"));

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Output);
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
            Assert.AreEqual(MoveShape.Dash, move.Shape);
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
            ReplaceInFile("moves/moves.json", "\"shape\": \"dash\"", "\"shape\": \"spiral\"");

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
