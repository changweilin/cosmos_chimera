using System.Collections;
using System.IO;
using System.Reflection;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Codex;
using Cosmimera.GameData;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class EncyclopediaIntegrationTests
    {
        private static string Copy(string name) => AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/Copy/" + name + ".json").text;
        private static DemoCopy Text() => new DemoCopy("zh-TW", Copy("strings"), Copy("names"), Copy("affixes"), Copy("region-themes"), Copy("quest-templates"), Copy("boss-epithets"));

        [Test]
        public void Unknown_subject_names_are_hidden_from_titles_and_search()
        {
            var catalog = new EncyclopediaCatalog();
            JObject entry = catalog.Find(CodexKind.Creature, "cr_aqua_slime_juvenile");
            Assert.That(entry, Is.Not.Null);
            var progress = new CodexProgress();
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var panel = new CodexPanel(database, Text(), _ => false, null, progress);
            Assert.That(panel.Title(entry), Is.EqualTo("？？？"));
            typeof(CodexPanel).GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, (string)entry["basic"]["name_zh"]);
            typeof(CodexPanel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
            var entries = (ICollection)typeof(CodexPanel).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
            Assert.That(entries.Count, Is.Zero);
            progress.Record(CodexEvent.Encounter, "cr_aqua_slime_juvenile");
            Assert.That(panel.Level(entry), Is.EqualTo(CodexLevel.Basic));
            Assert.That(panel.Title(entry), Is.EqualTo((string)entry["basic"]["name_zh"]));
            Assert.That(progress.Get(CodexKind.Creature, "cr_aqua_slime_adult"), Is.EqualTo(CodexLevel.Unknown));
        }

        [Test]
        public void All_sixty_requested_drive_forms_are_imported_with_real_moves_and_budgeted_stats()
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var path = Path.Combine(Application.dataPath, "../Data/_incoming/p4-09-encyclopedia-01/creatures/drive-creatures.json");
            if (!File.Exists(path))
            {
                path = Path.Combine(Application.dataPath, "../Data/_demo/creatures/p4-09-encyclopedia-01--drive-creatures.json");
            }
            Assume.That(File.Exists(path), Is.True, "drive-creatures.json not present (gitignored _demo/_incoming)");
            JArray forms = JArray.Parse(File.ReadAllText(path));
            Assert.That(forms.Count, Is.EqualTo(60));
            foreach (JObject form in forms)
            {
                Assert.That(database.TryGet((string)form["id"], out CreatureDef creature), Is.True, (string)form["id"]);
                Assert.That(creature.SignatureMove, Is.Not.Null);
                Assert.That(creature.Trait, Is.Not.Null);
                Assert.That(creature.Stats.Hp + creature.Stats.Speed + creature.Stats.YinAtk + creature.Stats.YangAtk + creature.Stats.YinDef + creature.Stats.YangDef,
                    Is.EqualTo((int)form["stats"]["hp"] + (int)form["stats"]["speed"] + (int)form["stats"]["yin_atk"] + (int)form["stats"]["yang_atk"] + (int)form["stats"]["yin_def"] + (int)form["stats"]["yang_def"]));
            }
        }
    }
}
