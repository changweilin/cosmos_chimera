using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Creatures.Tests
{
    // REQ: creatures.md#進化 (Q-C32 option B). Exact checks use a synthetic section so balance tuning never breaks them;
    // the real balance.json is only compared against the approved proposal it was copied from.
    public sealed class MaterialDropsTests
    {
        private const string Synthetic = "{\"materials\":{" +
            "\"normal\":{\"tier\":1,\"chance_percent\":0,\"qty\":1}," +
            "\"elite\":{\"tier\":2,\"chance_percent\":37,\"qty\":2}," +
            "\"boss\":{\"tier\":3,\"chance_percent\":100,\"qty\":5}}}";

        private static readonly Element[] Twilight = { Element.Light, Element.Shadow };

        private static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [Test]
        public void Parses_every_role()
        {
            MaterialDrops d = MaterialDrops.FromJson(Synthetic);
            Assert.That(d.TierFor(SpawnRole.Normal), Is.EqualTo(1));
            Assert.That(d.ChancePercent(SpawnRole.Elite), Is.EqualTo(37));
            Assert.That(d.QtyFor(SpawnRole.Elite), Is.EqualTo(2));
            Assert.That(d.TierFor(SpawnRole.Boss), Is.EqualTo(3));
            Assert.That(d.QtyFor(SpawnRole.Boss), Is.EqualTo(5));
        }

        [Test]
        public void Balance_json_matches_the_approved_option_B_proposal()
        {
            MaterialDrops d = MaterialDrops.FromJson(File.ReadAllText(Path.Combine(Root(), "Data", "balance.json")));
            JObject rules = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Root(), "Data", "_incoming",
                "p2-05-material-proposal", "drops-b.json")))["rules"];
            foreach ((string name, SpawnRole role) in new[] { ("normal", SpawnRole.Normal), ("elite", SpawnRole.Elite), ("boss", SpawnRole.Boss) })
            {
                Assert.That(d.TierFor(role), Is.EqualTo((int)rules[name]["tier"]), name);
                Assert.That(d.ChancePercent(role), Is.EqualTo((int)rules[name]["chance_percent"]), name);
                Assert.That(d.QtyFor(role), Is.EqualTo((int)rules[name]["qty"]), name);
            }
        }

        [TestCase("{}", TestName = "Missing section is refused")]
        [TestCase("{\"materials\":{\"normal\":{\"tier\":0,\"chance_percent\":10,\"qty\":1},\"elite\":{\"tier\":2,\"chance_percent\":60,\"qty\":1},\"boss\":{\"tier\":3,\"chance_percent\":100,\"qty\":1}}}", TestName = "Tier 0 is refused")]
        [TestCase("{\"materials\":{\"normal\":{\"tier\":1,\"chance_percent\":10,\"qty\":1},\"elite\":{\"tier\":4,\"chance_percent\":60,\"qty\":1},\"boss\":{\"tier\":3,\"chance_percent\":100,\"qty\":1}}}", TestName = "Tier above 3 is refused")]
        [TestCase("{\"materials\":{\"normal\":{\"tier\":1,\"chance_percent\":-1,\"qty\":1},\"elite\":{\"tier\":2,\"chance_percent\":60,\"qty\":1},\"boss\":{\"tier\":3,\"chance_percent\":100,\"qty\":1}}}", TestName = "Negative chance is refused")]
        [TestCase("{\"materials\":{\"normal\":{\"tier\":1,\"chance_percent\":10,\"qty\":1},\"elite\":{\"tier\":2,\"chance_percent\":60,\"qty\":1},\"boss\":{\"tier\":3,\"chance_percent\":101,\"qty\":1}}}", TestName = "Chance above 100 is refused")]
        [TestCase("{\"materials\":{\"normal\":{\"tier\":1,\"chance_percent\":10,\"qty\":0},\"elite\":{\"tier\":2,\"chance_percent\":60,\"qty\":1},\"boss\":{\"tier\":3,\"chance_percent\":100,\"qty\":1}}}", TestName = "Zero qty is refused")]
        [TestCase("{\"materials\":{\"normal\":{\"tier\":1,\"chance_percent\":10,\"qty\":1},\"elite\":{\"tier\":2,\"chance_percent\":60,\"qty\":1001},\"boss\":{\"tier\":3,\"chance_percent\":100,\"qty\":1}}}", TestName = "Qty above 1000 is refused")]
        public void Bad_section_is_refused_in_every_build(string json)
        {
            Assert.Throws<InvalidDataException>(() => MaterialDrops.FromJson(json));
        }

        [Test]
        public void FromJson_requires_text()
        {
            Assert.Throws<ContractException>(() => MaterialDrops.FromJson(""));
        }

        [Test]
        public void Zero_chance_never_drops_and_consumes_one_draw()
        {
            MaterialDrops d = MaterialDrops.FromJson(Synthetic);
            var rng = new Pcg32(42, 'D');
            var mirror = rng;
            for (int i = 0; i < 200; i++)
            {
                Assert.That(d.TryRoll(SpawnRole.Normal, Twilight, ref rng, out _), Is.False);
                mirror.NextUInt(100);
            }

            Assert.That(rng.NextUInt(), Is.EqualTo(mirror.NextUInt()), "exactly one bounded draw per failed roll");
        }

        [Test]
        public void Certain_drop_uses_the_role_tier_qty_and_only_bias_elements()
        {
            MaterialDrops d = MaterialDrops.FromJson(Synthetic);
            var rng = new Pcg32(7, 'D');
            var seen = new HashSet<Element>();
            for (int i = 0; i < 400; i++)
            {
                var mirror = rng;
                Assert.That(d.TryRoll(SpawnRole.Boss, Twilight, ref rng, out MaterialStack drop), Is.True);
                Assert.That(drop.Key.Tier, Is.EqualTo(3));
                Assert.That(drop.Qty, Is.EqualTo(5));
                CollectionAssert.Contains(Twilight, drop.Key.Element);
                seen.Add(drop.Key.Element);
                mirror.NextUInt(100);
                Assert.That(drop.Key.Element, Is.EqualTo(Twilight[(int)mirror.NextUInt(2)]), "chance draw, then element draw");
            }

            Assert.That(seen.Count, Is.EqualTo(2), "every bias element is reachable");
        }

        [Test]
        public void Partial_chance_succeeds_iff_the_draw_is_below_it()
        {
            MaterialDrops d = MaterialDrops.FromJson(Synthetic);
            var rng = new Pcg32(99, 'D');
            int hits = 0;
            for (int i = 0; i < 500; i++)
            {
                var mirror = rng;
                bool expected = mirror.NextUInt(100) < 37;
                Assert.That(d.TryRoll(SpawnRole.Elite, new[] { Element.Fire }, ref rng, out MaterialStack drop), Is.EqualTo(expected));
                if (expected)
                {
                    hits++;
                    Assert.That(drop.Key, Is.EqualTo(new MaterialKey(Element.Fire, 2)));
                }
            }

            Assert.That(hits, Is.GreaterThan(0).And.LessThan(500));
        }

        [Test]
        public void Same_stream_gives_the_same_drops()
        {
            MaterialDrops d = MaterialDrops.FromJson(Synthetic);
            var a = new Pcg32(5, 'D');
            var b = new Pcg32(5, 'D');
            for (int i = 0; i < 100; i++)
            {
                bool ra = d.TryRoll(SpawnRole.Elite, Twilight, ref a, out MaterialStack da);
                bool rb = d.TryRoll(SpawnRole.Elite, Twilight, ref b, out MaterialStack db);
                Assert.That(ra, Is.EqualTo(rb));
                Assert.That(da.Key, Is.EqualTo(db.Key));
            }
        }

        [Test]
        public void TryRoll_preconditions()
        {
            MaterialDrops d = MaterialDrops.FromJson(Synthetic);
            var rng = new Pcg32(1, 'D');
            Assert.Throws<ContractException>(() => d.TryRoll(SpawnRole.Boss, null, ref rng, out _));
            Assert.Throws<ContractException>(() => d.TryRoll(SpawnRole.Boss, new Element[0], ref rng, out _));
            Assert.Throws<ContractException>(() => d.TryRoll((SpawnRole)9, Twilight, ref rng, out _));
            Assert.Throws<ContractException>(() => d.TierFor((SpawnRole)9));
            Assert.Throws<ContractException>(() => d.ChancePercent((SpawnRole)9));
            Assert.Throws<ContractException>(() => d.QtyFor((SpawnRole)9));
        }
    }
}
