using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    public sealed class LobbySavePartnerTests
    {
        private static readonly Progression P = Progression.FromJson(File.ReadAllText(
            Path.Combine(Application.dataPath, "../Data/balance.json")));

        private static LobbySave Roster()
        {
            var save = new LobbySave();
            for (int i = 0; i < 7; i++) save.Add("species_" + i, VesselKind.GlassBead, 0);
            save.SetParty(new[] { 1, 2 });
            return save;
        }

        [Test]
        public void Assignments_survive_clone_and_round_trip_without_sharing_state()
        {
            LobbySave save = Roster();
            save.SetPartners(new[] { new PartnerPair(1, 3), new PartnerPair(2, 4) });
            LobbySave loaded = LobbySave.FromJson(save.ToJson(), P, _ => CreatureStage.Juvenile);
            Assert.That(loaded.Partners.Count, Is.EqualTo(2));
            Assert.That(loaded.Partners[1].MainInstanceId, Is.EqualTo(2));
            Assert.That(loaded.Partners[1].SubInstanceId, Is.EqualTo(4));
            LobbySave clone = save.Clone();
            clone.SetPartners(new PartnerPair[0]);
            Assert.That(save.Partners.Count, Is.EqualTo(2));
            Assert.That(clone.Partners, Is.Empty);
            Assert.Throws<System.NotSupportedException>(() => ((IList<PartnerPair>)save.Partners).Clear());
        }

        [Test]
        public void Invalid_replacements_are_atomic()
        {
            LobbySave save = Roster();
            save.SetPartners(new[] { new PartnerPair(1, 3) });
            string before = save.ToJson();
            Assert.Throws<ContractException>(() => save.SetPartners(null));
            Assert.Throws<ContractException>(() => save.SetPartners(new[] { new PartnerPair(3, 4) }));
            Assert.Throws<ContractException>(() => save.SetPartners(new[] { new PartnerPair(1, 2) }));
            Assert.Throws<ContractException>(() => save.SetPartners(new[] { new PartnerPair(1, 99) }));
            Assert.Throws<ContractException>(() => save.SetPartners(new[] { new PartnerPair(1, 3), new PartnerPair(2, 3) }));
            var full = new List<PartnerPair>();
            for (int i = 3; i <= 7; i++) full.Add(new PartnerPair(1, i));
            Assert.Throws<ContractException>(() => save.SetPartners(full));
            Assert.That(save.ToJson(), Is.EqualTo(before));
        }

        [Test]
        public void Four_subs_are_accepted_and_party_edits_preserve_exclusivity()
        {
            LobbySave save = Roster();
            save.SetPartners(new[] { new PartnerPair(1, 3), new PartnerPair(1, 4), new PartnerPair(1, 5), new PartnerPair(1, 6) });
            Assert.Throws<ContractException>(() => save.SetParty(new[] { 2, 3 }));
            Assert.That(save.Party, Is.EqualTo(new[] { 1, 2 }));
            save.SetParty(new[] { 2 });
            Assert.That(save.Partners, Is.Empty);
            save.SetParty(new[] { 2, 3 });
        }

        [Test]
        public void Format_four_migrates_to_empty_assignments()
        {
            string json = Roster().ToJson().Replace("\"version\": " + LobbySave.FormatVersion, "\"version\": 4");
            LobbySave save = LobbySave.FromJson(json, P, _ => CreatureStage.Juvenile);
            Assert.That(save.LoadedFormatVersion, Is.EqualTo(4));
            Assert.That(save.Partners, Is.Empty);
            Assert.That(save.ToJson(), Does.Contain("\"version\": " + LobbySave.FormatVersion));
        }

        [TestCase("[{\"main\":3,\"sub\":4}]")]
        [TestCase("[{\"main\":1,\"sub\":2}]")]
        [TestCase("[{\"main\":1,\"sub\":99}]")]
        [TestCase("[{\"main\":1,\"sub\":3},{\"main\":2,\"sub\":3}]")]
        [TestCase("[{\"main\":1,\"sub\":3},{\"main\":1,\"sub\":4},{\"main\":1,\"sub\":5},{\"main\":1,\"sub\":6},{\"main\":1,\"sub\":7}]")]
        public void Corrupt_assignments_are_rejected_in_all_builds(string partners)
        {
            string json = Roster().ToJson().Replace("\"partners\": []", "\"partners\": " + partners);
            Assert.Throws<InvalidDataException>(() => LobbySave.FromJson(json, P, _ => CreatureStage.Juvenile));
        }
    }
}
