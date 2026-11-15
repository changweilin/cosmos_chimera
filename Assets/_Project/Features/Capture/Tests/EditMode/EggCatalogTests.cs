using System.IO;
using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Capture.Tests
{
    public sealed class EggCatalogTests
    {
        [Test]
        public void Parses_weighted_pools_and_misses_unknown_ids()
        {
            EggCatalog c = EggCatalog.FromJson(
                "[{\"id\":\"egg_a\",\"outcomes\":[{\"species\":\"s1\",\"weight\":1},{\"species\":\"s2\",\"weight\":3}]}]");
            Assert.That(c.Count, Is.EqualTo(1));
            Assert.That(c.TryGet("egg_a", out EggDef def), Is.True);
            Assert.That(def.Outcomes.Count, Is.EqualTo(2));
            Assert.That(c.TryGet("egg_gone", out _), Is.False);
            Assert.That(c.TryGet(null, out _), Is.False);
        }

        [TestCase("{}")]
        [TestCase("[{\"id\":\"\",\"outcomes\":[{\"species\":\"s\",\"weight\":1}]}]")]
        [TestCase("[{\"id\":\"e\",\"outcomes\":[]}]")]
        [TestCase("[{\"id\":\"e\",\"outcomes\":[{\"species\":\"s\",\"weight\":0}]}]")]
        [TestCase("[{\"id\":\"e\",\"outcomes\":[{\"species\":\"s\",\"weight\":1},{\"species\":\"s\",\"weight\":1}]}]")]
        [TestCase("[{\"id\":\"e\",\"outcomes\":[{\"species\":\"s\",\"weight\":1}]},{\"id\":\"e\",\"outcomes\":[{\"species\":\"s\",\"weight\":1}]}]")]
        public void Malformed_tables_are_rejected(string json)
        {
            Assert.Throws<InvalidDataException>(() => EggCatalog.FromJson(json));
        }

        [Test]
        public void Shipped_sample_table_parses()
        {
            string path = Path.Combine(UnityEngine.Application.dataPath, "../Data/_incoming/p4-05-eggs-draft-01/eggs/hatchery-eggs.json");
            Assert.That(EggCatalog.FromJson(File.ReadAllText(path)).Count, Is.EqualTo(4));
        }

        [Test]
        public void Progress_rates_match_the_hatchery()
        {
            HatchConfig c = HatchConfig.Default;
            Assert.That(EggProgress.Battles(1, c, false), Is.EqualTo(20_000));
            Assert.That(EggProgress.Boss(c, false), Is.EqualTo(100_000));
            Assert.That(EggProgress.Boss(c, true), Is.EqualTo(150_000));
            Assert.That(EggProgress.Walk(1000, 4000, c, false), Is.EqualTo(250_000));
            Assert.Throws<ContractException>(() => EggProgress.Walk(1, 0, c, false));
        }
    }
}
