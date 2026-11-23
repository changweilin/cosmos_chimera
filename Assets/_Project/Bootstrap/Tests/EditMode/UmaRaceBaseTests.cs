using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: docs/design/npc-uma-looks.md#種族基底 — each race and sex has its own base body at its own mean, and the DNA
    // keys baked on it must reach every resident of that race, with a visible change per standard deviation.
    public sealed class UmaRaceBaseTests
    {
        private const int Spread = 3000;
        private const int Typical = 1000;
        // 暫定 (npc-uma-looks.md 種族基底): smallest surface move, in mm, one standard deviation of a shape channel must make.
        private const float MinMmPerSigma = 1.0f;
        // Key weights past ±1 extrapolate a baked shape; a little is tolerated before the channel needs a wider source.
        private const float MaxKeyWeight = 1.05f;

        private static string NpcDir => Path.Combine(Application.dataPath, "..", "tools", "acg-character", "npc");

        private static IEnumerable<NpcRace> Races() =>
            Enum.GetValues(typeof(NpcRace)).Cast<NpcRace>().Where(r => r != NpcRace.Skeleton);

        internal static string BaseId(NpcRace race, NpcSex sex) =>
            "npc_" + race.ToString().ToLowerInvariant() + "_" + sex.ToString().ToLowerInvariant();

        private static NpcSpec SpecOf(ref Pcg32 rng, NpcRace race, NpcSex sex, bool typical)
        {
            var role = (NpcRole)rng.NextUInt((uint)Enum.GetValues(typeof(NpcRole)).Length);
            var region = (ArchRegion)rng.NextUInt(6);
            var era = (ArchEra)rng.NextUInt((uint)Enum.GetValues(typeof(ArchEra)).Length);
            // A typical member is a pure-blood adult (neither child nor elder, NpcMeshes.Look); the spread takes everyone.
            float age = typical ? 0.25f + 0.35f * rng.NextFloat() : rng.NextFloat();
            float health = typical ? 0.6f + 0.4f * rng.NextFloat() : rng.NextFloat();
            float stature = 6.9f * rng.NextFloat() - 3.45f;
            float bulk = 6.9f * rng.NextFloat() - 3.45f;
            ulong seed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
            NpcHeritage heritage = typical ? NpcHeritage.Pure : NpcMeshes.HeritageFor(race, region, era, seed);
            return new NpcSpec(race, role, sex, age, health, stature, bulk, region, era, region, era, rng.NextFloat(), seed,
                rng.NextFloat(), seed ^ 0x5EEDul, heritage);
        }

        internal sealed class Stats
        {
            public float[] Mean, Lo, Hi, Sd;
        }

        // Deterministic: fixed streams per race and sex, so the committed table can be compared exactly.
        internal static Stats StatsOf(NpcRace race, NpcSex sex)
        {
            int n = NpcMeshes.UmaChannels.Count;
            var s = new Stats { Mean = new float[n], Lo = Enumerable.Repeat(float.MaxValue, n).ToArray(), Hi = Enumerable.Repeat(float.MinValue, n).ToArray(), Sd = new float[n] };
            var rng = new Pcg32(0xBA5Eul ^ ((ulong)race << 8) ^ (ulong)sex, 0x54595049ul);
            for (int k = 0; k < Typical; k++)
            {
                NpcUmaRecipe r = NpcMeshes.UmaRecipeOf(SpecOf(ref rng, race, sex, true));
                for (int i = 0; i < n; i++)
                {
                    s.Mean[i] += r.Dna(i) / Typical;
                }
            }

            var sum = new double[n];
            var sq = new double[n];
            rng = new Pcg32(0xBA5Eul ^ ((ulong)race << 8) ^ (ulong)sex, 0x53505244ul);
            for (int k = 0; k < Spread; k++)
            {
                NpcUmaRecipe r = NpcMeshes.UmaRecipeOf(SpecOf(ref rng, race, sex, false));
                for (int i = 0; i < n; i++)
                {
                    float v = r.Dna(i);
                    s.Lo[i] = Math.Min(s.Lo[i], v);
                    s.Hi[i] = Math.Max(s.Hi[i], v);
                    sum[i] += v;
                    sq[i] += (double)v * v;
                }
            }

            for (int i = 0; i < n; i++)
            {
                double m = sum[i] / Spread;
                s.Sd[i] = (float)Math.Sqrt(Math.Max(0.0, sq[i] / Spread - m * m));
            }

            return s;
        }

        private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        private static bool FaceChannel(string name) =>
            name.StartsWith("face.") || name.StartsWith("nose.") || name.StartsWith("mouth.") || name.StartsWith("eye.") ||
            name.StartsWith("brow.") || name.StartsWith("ear.");

        // Writes tools/acg-character/npc/race_bases.json, the table npc_bases.py and race.py build the bases from. Run after
        // the sampling changes, then rebuild the bases (specs/README.md).
        [Test, Explicit("writes the race base table")]
        public void ExportRaceBases()
        {
            var sb = new StringBuilder();
            sb.Append("{\n \"_doc\": \"Per race and sex: mean DNA of a pure-blood adult (the base body's zero point) and lo, hi, sd over every resident of that race and sex, any age or blood. Written by UmaRaceBaseTests.ExportRaceBases; do not edit.\",\n \"bases\": {\n");
            var rows = new List<string>();
            foreach (NpcRace race in Races())
            {
                foreach (NpcSex sex in new[] { NpcSex.Male, NpcSex.Female })
                {
                    Stats s = StatsOf(race, sex);
                    var ch = new List<string>();
                    for (int i = 0; i < s.Mean.Length; i++)
                    {
                        ch.Add($"   \"{NpcMeshes.UmaChannels[i].Name}\": [{F(s.Mean[i])}, {F(s.Lo[i])}, {F(s.Hi[i])}, {F(s.Sd[i])}]");
                    }

                    rows.Add($"  \"{BaseId(race, sex)}\": {{\n   \"race\": \"{race}\", \"sex\": \"{sex}\",\n" + string.Join(",\n", ch) + "\n  }");
                }
            }

            sb.Append(string.Join(",\n", rows)).Append("\n }\n}\n");
            File.WriteAllText(Path.Combine(NpcDir, "race_bases.json"), sb.ToString().Replace("\r\n", "\n"));
        }

        // The bases are built from the table; a stale table would centre the keys away from where residents are.
        [Test]
        public void RaceBaseTableIsCurrent()
        {
            JObject bases = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(NpcDir, "race_bases.json")))["bases"];
            foreach (NpcRace race in Races())
            {
                foreach (NpcSex sex in new[] { NpcSex.Male, NpcSex.Female })
                {
                    Stats s = StatsOf(race, sex);
                    var row = (JObject)bases[BaseId(race, sex)];
                    Assert.That(row, Is.Not.Null, BaseId(race, sex) + " missing; run ExportRaceBases");
                    for (int i = 0; i < s.Mean.Length; i++)
                    {
                        NpcMeshes.UmaChannel c = NpcMeshes.UmaChannels[i];
                        var v = (JArray)row[c.Name];
                        float tol = 1e-3f * (c.Max - c.Min);
                        Assert.That((float)v[0], Is.EqualTo(s.Mean[i]).Within(tol), $"{BaseId(race, sex)} {c.Name} mean; run ExportRaceBases");
                        Assert.That((float)v[1], Is.EqualTo(s.Lo[i]).Within(tol), $"{BaseId(race, sex)} {c.Name} lo");
                        Assert.That((float)v[2], Is.EqualTo(s.Hi[i]).Within(tol), $"{BaseId(race, sex)} {c.Name} hi");
                    }
                }
            }
        }

        // Gate 2, reach and visibility: on every race base, each baked key covers its race's whole spread without
        // running past the weight MakeHuman allows, and one standard deviation moves the surface visibly.
        [Test]
        public void BakedKeysReachAndShowEveryResident()
        {
            string keysPath = Path.Combine(NpcDir, "race_keys.json");
            Assert.That(File.Exists(keysPath), "race_keys.json missing; bake the bases (specs/README.md)");
            JObject keys = (JObject)JObject.Parse(File.ReadAllText(keysPath))["bases"];
            JObject table = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(NpcDir, "race_bases.json")))["bases"];
            var problems = new List<string>();
            foreach (NpcRace race in Races())
            {
                foreach (NpcSex sex in new[] { NpcSex.Male, NpcSex.Female })
                {
                    string id = BaseId(race, sex);
                    var baked = (JObject)keys[id];
                    if (baked == null)
                    {
                        problems.Add(id + ": not baked");
                        continue;
                    }

                    var row = (JObject)table[id];
                    var probe = new Pcg32(0x4845ul, (ulong)race);
                    bool beastHead = NpcMeshes.UmaRecipeOf(SpecOf(ref probe, race, sex, true)).HeadSlot != null;
                    foreach (JProperty p in baked.Properties())
                    {
                        // A beast race always wears its own head slot (npc-uma-looks.md#種族覆蓋), so the humanoid face keys
                        // on its base are never shown; the head slot's blendshapes carry those channels instead.
                        if (beastHead && FaceChannel(p.Name))
                        {
                            continue;
                        }

                        var v = (JArray)row[p.Name];
                        float mean = (float)v[0], lo = (float)v[1], hi = (float)v[2], sd = (float)v[3];
                        foreach ((string side, float need) in new[] { ("plus", hi - mean), ("minus", mean - lo) })
                        {
                            var k = (JObject)p.Value[side];
                            if (k == null || need <= 0f)
                            {
                                continue;
                            }

                            if (k["per"] == null || k["per"].Type == JTokenType.Null || (float)k["per"] <= 0f)
                            {
                                problems.Add($"{id} {p.Name}{side}: key moves nothing measurable, or the wrong way");
                                continue;
                            }

                            float per = (float)k["per"], room = (float)k["room"], mm = (float)k["mm"];
                            float weight = need / per;
                            if (weight > room * MaxKeyWeight)
                            {
                                problems.Add($"{id} {p.Name}{side}: needs weight {weight:0.00}, base leaves {room:0.00}");
                            }

                            bool exempt = k["mm_exempt"] != null && (bool)k["mm_exempt"];
                            if (!exempt && sd > 0f && mm * sd / per < MinMmPerSigma)
                            {
                                problems.Add($"{id} {p.Name}{side}: 1 sd moves {mm * sd / per:0.00} mm");
                            }
                        }
                    }
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
