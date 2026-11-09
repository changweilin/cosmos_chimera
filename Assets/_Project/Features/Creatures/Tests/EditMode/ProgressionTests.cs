using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Creatures.Tests
{
    // Golden vectors come from the Python reference (tools/balance/generate_level_rewards.py, SOL 2026-11-09), so the
    // C# port and the approved option-A model must agree on every integer.
    public sealed class ProgressionTests
    {
        private static readonly Progression P = Make.Progression;

        private static JObject Golden() => JObject.Parse(File.ReadAllText(Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "tools", "balance", "golden_level_rewards.json")));

        private static string BalanceJson() => File.ReadAllText(Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Data", "balance.json"));

        private static SpawnRole Role(string r) => r == "elite" ? SpawnRole.Elite : r == "boss" ? SpawnRole.Boss : SpawnRole.Normal;

        private static XpSource Source(string s) => s switch
        {
            "wild" => XpSource.Wild,
            "capture" => XpSource.Capture,
            "quest_step" => XpSource.QuestStep,
            "quest_chain" => XpSource.QuestChain,
            "boss" => XpSource.Boss,
            _ => throw new System.ArgumentException(s),
        };

        [Test]
        public void Thresholds_and_costs_match_the_reference()
        {
            JToken t = Golden()["thresholds"];
            for (int level = 1; level <= 100; level++)
            {
                Assert.AreEqual((int)t["T"][level], P.XpThreshold(level), $"T({level})");
            }

            for (int level = 1; level < 100; level++)
            {
                Assert.AreEqual((int)t["C"][level], P.XpToNext(level), $"C({level})");
            }

            Assert.AreEqual(248528, P.MaxExp, "level-progression.md table");
            Assert.AreEqual(1106, P.XpThreshold(10));
        }

        [Test]
        public void LevelOf_matches_the_reference()
        {
            foreach (JToken c in Golden()["level_of_xp"])
            {
                Assert.AreEqual((int)c["level"], P.LevelOf((int)c["xp"]), $"xp {c["xp"]}");
            }
        }

        [Test]
        public void Scaled_stats_match_the_reference()
        {
            foreach (JToken c in Golden()["scaled_stat"])
            {
                int b = (int)c["base"];
                CreatureStats s = new CreatureStats(b, b, b, b).AtLevel((int)c["level"]);
                Assert.AreEqual((int)c["value"], s.Con, $"base {b} level {c["level"]}");
                Assert.AreEqual(s.Con, s.Agi);
            }
        }

        [Test]
        public void Wild_levels_match_the_reference()
        {
            foreach (JToken c in Golden()["wild_level"])
            {
                Assert.AreEqual((int)c["level"], P.WildLevel((int)c["difficulty"], (int)c["tier"], Role((string)c["role"])),
                    c.ToString());
            }
        }

        [Test]
        public void Source_xp_matches_the_reference()
        {
            foreach (JToken c in Golden()["source_xp"])
            {
                XpSource source = Source((string)c["source"]);
                bool elite = source == XpSource.Wild && (string)c["role"] == "elite";
                Assert.AreEqual((int)c["xp"], P.SourceXp(source, (int)c["level"], elite), c.ToString());
            }
        }

        [Test]
        public void Migration_matches_the_reference()
        {
            foreach (JToken c in Golden()["migrate"])
            {
                int expected = (int)c["xp"];
                if ((bool)c["already_cumulative"])
                {
                    // A format-2 save only clips (LobbySave.FromJson); there is nothing else to port.
                    Assert.AreEqual(expected, System.Math.Min((int)c["old_xp"], P.MaxExp));
                }
                else
                {
                    Assert.AreEqual(expected, P.MigrationExp((CreatureStage)(int)c["stage"]), c.ToString());
                }
            }
        }

        [Test]
        public void Settlements_match_the_reference()
        {
            foreach (JToken c in Golden()["settle"])
            {
                JToken input = c["inputs"];
                JToken output = c["outputs"];
                var recipients = new List<XpRecipient>();
                var mains = (JArray)input["mains"];
                foreach (JToken m in mains)
                {
                    recipients.Add(new XpRecipient((int)m["id"], (int)m["xp"], (bool)m["dead"], (bool)m["appeared"], -1));
                }

                for (int i = 0; i < mains.Count; i++)
                {
                    JToken group = input["sidekicks"]?[((int)mains[i]["id"]).ToString()];
                    foreach (JToken s in group ?? new JArray())
                    {
                        recipients.Add(new XpRecipient((int)s["id"], (int)s["xp"], (bool)s["dead"], false, i));
                    }
                }

                XpSettlement r = P.Settle((int)input["E"], recipients);
                string name = (string)c["name"];
                for (int i = 0; i < recipients.Count; i++)
                {
                    string id = recipients[i].InstanceId.ToString();
                    Assert.AreEqual((int)output["paid"][id], r.Paid[i], $"{name} paid {id}");
                    string bucket = recipients[i].Owner == -1 ? "main_assigned" : "sidekick_assigned";
                    Assert.AreEqual((int)output[bucket][id], r.Assigned[i], $"{name} {bucket} {id}");
                }

                AssertFraction((string)output["main_loss"], 2 * r.MainLoss, name + " main_loss");
                AssertFraction((string)output["sidekick_loss"], r.SidekickEntitlementHalves - 2 * r.SidekickPaid,
                    name + " sidekick_loss");
            }
        }

        // expected is "n/d"; actual is the loss counted in half points.
        private static void AssertFraction(string expected, long actualHalves, string message)
        {
            string[] parts = expected.Split('/');
            long n = long.Parse(parts[0]);
            long d = long.Parse(parts[1]);
            Assert.AreEqual(2 * n, actualHalves * d, message + " = " + expected);
        }

        [Test]
        public void Party_order_does_not_change_who_gets_the_remainder()
        {
            var a = new[] { new XpRecipient(5, 0, false, true, -1), new XpRecipient(2, 0, false, true, -1), new XpRecipient(9, 0, false, true, -1) };
            XpSettlement ra = P.Settle(10, a);
            XpSettlement rb = P.Settle(10, a.Reverse().ToArray());
            Assert.AreEqual(new[] { 3, 4, 3 }, ra.Paid.ToArray(), "id 2 has the smallest id and gets the extra point");
            Assert.AreEqual(new[] { 3, 4, 3 }, rb.Paid.ToArray());
        }

        [Test]
        public void Level_queries_reject_out_of_range_input()
        {
            Assert.Throws<ContractException>(() => P.XpThreshold(0));
            Assert.Throws<ContractException>(() => P.XpThreshold(101));
            Assert.Throws<ContractException>(() => P.XpToNext(100));
            Assert.Throws<ContractException>(() => P.LevelOf(-1));
            Assert.AreEqual(100, P.LevelOf(int.MaxValue));
            Assert.Throws<ContractException>(() => P.WildLevel(0, 0, SpawnRole.Normal));
            Assert.Throws<ContractException>(() => P.WildLevel(11, 0, SpawnRole.Normal));
            Assert.Throws<ContractException>(() => P.WildLevel(1, 3, SpawnRole.Normal));
            Assert.Throws<ContractException>(() => P.WildLevel(1, 0, (SpawnRole)9));
            Assert.Throws<ContractException>(() => P.SourceXp(XpSource.Wild, 0, false));
            Assert.Throws<ContractException>(() => P.SourceXp(XpSource.Boss, 10, true), "only a wild defeat is elite");
            Assert.Throws<ContractException>(() => P.SourceXp((XpSource)9, 10, false));
            Assert.Throws<ContractException>(() => P.MigrationExp((CreatureStage)9));
            Assert.Throws<ContractException>(() => new CreatureStats(1, 1, 1, 1).AtLevel(0));
            Assert.Throws<ContractException>(() => new CreatureStats(1, 1, 1, 1).AtLevel(101));
        }

        [Test]
        public void Settle_rejects_malformed_recipient_lists()
        {
            var main = new XpRecipient(1, 0, false, true, -1);
            Assert.Throws<ContractException>(() => P.Settle(-1, new[] { main }));
            Assert.Throws<ContractException>(() => P.Settle(1, null));
            Assert.Throws<ContractException>(() => P.Settle(1, new XpRecipient[0]), "no main");
            Assert.Throws<ContractException>(() => P.Settle(1, Enumerable.Range(1, 7).Select(i => new XpRecipient(i, 0, false, true, -1)).ToArray()));
            Assert.Throws<ContractException>(() => P.Settle(1, new[] { main, new XpRecipient(1, 0, false, false, 0) }), "duplicate id");
            Assert.Throws<ContractException>(() => P.Settle(1, new[] { new XpRecipient(0, 0, false, true, -1) }));
            Assert.Throws<ContractException>(() => P.Settle(1, new[] { new XpRecipient(1, -1, false, true, -1) }));
            Assert.Throws<ContractException>(() => P.Settle(1, new[] { main, new XpRecipient(2, 0, false, false, 1) }), "owner must be a main");
            Assert.Throws<ContractException>(() => P.Settle(1, new[] { new XpRecipient(2, 0, false, false, 0), main }), "mains first");
        }

        [Test]
        public void FromJson_rejects_missing_or_unsupported_sections()
        {
            Assert.Throws<ContractException>(() => Progression.FromJson(""));
            Assert.Throws<ContractException>(() => Progression.FromJson("{}"));
            string json = BalanceJson();
            Assert.Throws<ContractException>(() => Progression.FromJson(json.Replace("\"sidekick_share_divisor\": 2", "\"sidekick_share_divisor\": 3")));
            Assert.Throws<ContractException>(() => Progression.FromJson(json.Replace("\"xp_growth_numerator\": 105", "\"xp_growth_numerator\": 100")));
            Assert.Throws<ContractException>(() => Progression.FromJson(json.Replace("\"starter_level\": 10", "\"starter_level\": 0")));
            Assert.AreEqual(10, Progression.FromJson(json).StarterLevel);
            // SOL P2-12 R5: the Python envelope (elite 1..100) and no config whose source reward overflows Int32.
            Assert.Throws<ContractException>(() => Progression.FromJson(json.Replace("\"elite_multiplier\": 2", "\"elite_multiplier\": 101")));
            Assert.Throws<ContractException>(() => Progression.FromJson(json.Replace("\"wild\": 80, \"capture\"", "\"wild\": 2147483647, \"capture\"")));
            Assert.Throws<ContractException>(() => Progression.FromJson(json.Replace("\"boss\": 640", "\"boss\": 2147483647")));
        }

        // The bands live in the schema, CreatureDef and balance.json; tools/tests checks schema == balance.json.
        [Test]
        public void CreatureDef_bands_match_balance_json()
        {
            JToken bands = JObject.Parse(BalanceJson())["progression"]["evolution_level_bands"];
            Assert.AreEqual((int)bands["juvenile"][0], CreatureDef.EvolutionLevelMin[0]);
            Assert.AreEqual((int)bands["juvenile"][1], CreatureDef.EvolutionLevelMax[0]);
            Assert.AreEqual((int)bands["adolescent"][0], CreatureDef.EvolutionLevelMin[1]);
            Assert.AreEqual((int)bands["adolescent"][1], CreatureDef.EvolutionLevelMax[1]);
        }
    }
}
