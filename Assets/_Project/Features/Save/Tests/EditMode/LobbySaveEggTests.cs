using System.Collections.Generic;
using System.IO;
using Cosmimera.Capture;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    public sealed class LobbySaveEggTests
    {
        private static readonly Progression P = Progression.FromJson(File.ReadAllText(
            Path.Combine(Application.dataPath, "../Data/balance.json")));

        private static LobbySave Load(string json) => LobbySave.FromJson(json, P, _ => CreatureStage.Juvenile);

        [Test]
        public void Eggs_round_trip_and_clone_without_sharing_state()
        {
            var save = new LobbySave();
            save.SetEggs(new[] { new IncubatingEgg("egg_a", 1, 250_000), new IncubatingEgg("egg_b", 2) });
            LobbySave loaded = Load(save.ToJson());
            Assert.That(loaded.Eggs.Count, Is.EqualTo(2));
            Assert.That(loaded.Eggs[0].EggId, Is.EqualTo("egg_a"));
            Assert.That(loaded.Eggs[0].Vessel, Is.EqualTo(1));
            Assert.That(loaded.Eggs[0].ProgressPpm, Is.EqualTo(250_000));
            LobbySave clone = save.Clone();
            var h = new EggHatchery(HatchConfig.Default);
            h.TryPlace(clone.Eggs[0]);
            h.AddBossVictory(false);
            Assert.That(clone.Eggs[0].ProgressPpm, Is.EqualTo(350_000));
            Assert.That(save.Eggs[0].ProgressPpm, Is.EqualTo(250_000));
        }

        [Test]
        public void SetEggs_copies_so_hatchery_mutation_does_not_leak()
        {
            var save = new LobbySave();
            var egg = new IncubatingEgg("egg_a", 0);
            save.SetEggs(new[] { egg });
            var h = new EggHatchery(HatchConfig.Default);
            h.TryPlace(egg);
            h.AddBossVictory(false);
            Assert.That(save.Eggs[0].ProgressPpm, Is.EqualTo(0));
        }

        [Test]
        public void Format_five_migrates_to_no_eggs()
        {
            string json = new LobbySave().ToJson().Replace("\"version\": " + LobbySave.FormatVersion, "\"version\": 5");
            LobbySave save = Load(json);
            Assert.That(save.LoadedFormatVersion, Is.EqualTo(5));
            Assert.That(save.Eggs, Is.Empty);
        }

        [TestCase("[{\"egg_id\":\"\",\"vessel\":0,\"progress_ppm\":0}]")]
        [TestCase("[{\"egg_id\":\"e\",\"vessel\":9,\"progress_ppm\":0}]")]
        [TestCase("[{\"egg_id\":\"e\",\"vessel\":0,\"progress_ppm\":-1}]")]
        [TestCase("[{\"egg_id\":\"e\",\"vessel\":0,\"progress_ppm\":1000001}]")]
        [TestCase("[{\"egg_id\":\"e\",\"vessel\":0,\"progress_ppm\":0},{\"egg_id\":\"e\",\"vessel\":0,\"progress_ppm\":0},{\"egg_id\":\"e\",\"vessel\":0,\"progress_ppm\":0},{\"egg_id\":\"e\",\"vessel\":0,\"progress_ppm\":0},{\"egg_id\":\"e\",\"vessel\":0,\"progress_ppm\":0}]")]
        public void Corrupt_eggs_are_rejected(string eggs)
        {
            string json = new LobbySave().ToJson().Replace("\"eggs\": []", "\"eggs\": " + eggs);
            Assert.Throws<InvalidDataException>(() => Load(json));
        }

        [Test]
        public void SetEggs_rejects_overflow_and_keeps_state()
        {
            var save = new LobbySave();
            save.SetEggs(new[] { new IncubatingEgg("egg_a", 0) });
            var five = new List<IncubatingEgg>();
            for (int i = 0; i < 5; i++) five.Add(new IncubatingEgg("egg_" + i, 0));
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => save.SetEggs(five));
            Assert.That(save.Eggs.Count, Is.EqualTo(1));
        }
    }
}
