using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: docs/design/npc-uma-looks.md#替換門檻 gates 1 and 2 (ADR-0015 decision 5): moving residents onto the UMA race
    // must not lose any variety the procedural figure draws.
    public sealed class UmaRecipeGateTests
    {
        private const int PerPlanet = 1000;
        private const int Planets = 6;
        private const int TownSize = 60;
        // 暫定 (npc-uma-looks.md 替換門檻 2): share of residents of one town allowed to repeat another's recipe.
        private const float MaxTownRepeatShare = 0.01f;

        // Fields a single edit of which is not expected to change the recipe, each with the reason.
        private static readonly Dictionary<string, string> Folded = new Dictionary<string, string>
        {
            ["Look.Child"] = "a child is carried by Look.Grown and the face fields it shapes",
            ["Look.Skirt"] = "derived from Look.Cut; the lower slot reads it together with the cut",
            ["Face.Shape"] = "the face shape is already folded into the face field means",
            // Not folded, but out of this edit's reach: Rig.Pose reads height through HeightM(spec), which re-derives the
            // traits from the spec. body.height carries it; ChannelsStayInsideTheirRangesAndVary sees it vary.
            ["Traits.HeightM"] = "re-derived from the spec by HeightM",
        };

        private static NpcSpec RandomSpec(ref Pcg32 rng, ulong planet, int town)
        {
            var race = (NpcRace)rng.NextUInt((uint)Enum.GetValues(typeof(NpcRace)).Length);
            var role = (NpcRole)rng.NextUInt((uint)Enum.GetValues(typeof(NpcRole)).Length);
            var sex = (NpcSex)rng.NextUInt(2);
            var region = (ArchRegion)((planet + (ulong)town) % 6);
            var era = (ArchEra)rng.NextUInt((uint)Enum.GetValues(typeof(ArchEra)).Length);
            var borrowRegion = (ArchRegion)rng.NextUInt(6);
            var borrowEra = (ArchEra)rng.NextUInt((uint)Enum.GetValues(typeof(ArchEra)).Length);
            float age = rng.NextFloat();
            float health = rng.NextFloat();
            float stature = 6.9f * rng.NextFloat() - 3.45f;
            float bulk = 6.9f * rng.NextFloat() - 3.45f;
            ulong seed = ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
            ulong culture = (planet << 20) ^ (ulong)(town + 1);
            return new NpcSpec(race, role, sex, age, health, stature, bulk, region, era, borrowRegion, borrowEra,
                rng.NextFloat(), seed, rng.NextFloat(), culture, NpcMeshes.HeritageFor(race, region, era, seed));
        }

        private static IEnumerable<NpcSpec> Residents(ulong planet, int count)
        {
            var rng = new Pcg32(0xC0FFEEul ^ planet, 0x554D41ul);
            for (int i = 0; i < count; i++)
            {
                yield return RandomSpec(ref rng, planet, i / TownSize);
            }
        }

        [Test]
        public void SameSpecGivesSameRecipe()
        {
            foreach (NpcSpec spec in Residents(1, 50))
            {
                Assert.That(NpcMeshes.UmaRecipeOf(spec).Key(), Is.EqualTo(NpcMeshes.UmaRecipeOf(spec).Key()));
            }
        }

        [Test]
        public void ChannelNamesAreUnique()
        {
            var names = NpcMeshes.UmaChannels.Select(c => c.Name).ToList();
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count));
        }

        // REQ: ADR-0014 / ADR-0015 — the Blender race baker (tools/acg-character/race_channels.json) and the mapper must
        // agree on every channel, or a DNA value would have nothing to drive.
        [Test]
        public void RaceChannelTableMatchesTheMapper()
        {
            string path = System.IO.Path.Combine(Application.dataPath, "..", "tools", "acg-character", "race_channels.json");
            var table = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
            var inTable = new HashSet<string>(((Newtonsoft.Json.Linq.JObject)table["channels"]).Properties().Select(p => p.Name));
            var inMapper = new HashSet<string>(NpcMeshes.UmaChannels.Select(c => c.Name));
            Assert.That(inMapper.Except(inTable), Is.Empty, "mapper channels missing from race_channels.json");
            Assert.That(inTable.Except(inMapper), Is.Empty, "race_channels.json channels the mapper never sets");
        }

        // Gate 1, completeness: every field Build draws is named by a channel or by the recipe's slots and colours.
        [Test]
        public void EverySampledFieldIsCarried()
        {
            var sources = new HashSet<string>(NpcMeshes.UmaChannels.SelectMany(c => c.Source.Split(',')));
            var missing = new List<string>();
            foreach ((string prefix, Type type) in SampledTypes())
            {
                foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    string name = prefix + "." + f.Name;
                    bool viaSlotOrColour = f.FieldType == typeof(Color) || f.FieldType == typeof(bool) || f.FieldType.IsEnum ||
                                           f.FieldType == typeof(int);
                    bool rigPose = type == typeof(NpcMeshes.Rig) && (f.FieldType == typeof(Unity.Mathematics.float3) ||
                                                                      f.FieldType == typeof(NpcMeshes.ArmPose));
                    if (!sources.Contains(name) && !viaSlotOrColour && !rigPose && !Folded.ContainsKey(name))
                    {
                        missing.Add(name);
                    }
                }
            }

            Assert.That(missing, Is.Empty, "fields with no UMA channel: " + string.Join(", ", missing));
        }

        // Gate 1, sensitivity: an edit of one look, face or trait field alone changes the recipe.
        [Test]
        public void EditingOneFieldChangesTheRecipe()
        {
            NpcSpec spec = FindSpec(s => s.Look.Beard && s.Look.Hat && s.Look.Piece && !s.Traits.Furred && s.Look.HairStyle != 4);
            NpcMeshes.UmaSample baseSample = NpcMeshes.UmaSampleOf(spec);
            string baseKey = NpcMeshes.UmaRecipeOf(baseSample).Key();
            var unchanged = new List<string>();

            foreach (FieldInfo f in typeof(NpcMeshes.Look).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object boxed = baseSample.Look;
                f.SetValue(boxed, Nudge(f, f.GetValue(boxed)));
                NpcMeshes.UmaSample s = NpcMeshes.UmaSampleOf(spec, baseSample.Traits, (NpcMeshes.Look)boxed);
                Check("Look." + f.Name, s);
            }

            foreach (FieldInfo f in typeof(NpcMeshes.Face).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                NpcMeshes.UmaSample s = baseSample;
                object boxed = s.Face;
                f.SetValue(boxed, Nudge(f, f.GetValue(boxed)));
                s.Face = (NpcMeshes.Face)boxed;
                Check("Face." + f.Name, s);
            }

            foreach (FieldInfo f in typeof(NpcMeshes.Traits).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object boxed = baseSample.Traits;
                f.SetValue(boxed, Nudge(f, f.GetValue(boxed)));
                // Furred alone hides the hair slot; Fat is a mean the look already drew from, so re-pose with the drawn look.
                NpcMeshes.UmaSample s = NpcMeshes.UmaSampleOf(spec, (NpcMeshes.Traits)boxed, baseSample.Look);
                Check("Traits." + f.Name, s);
            }

            Assert.That(unchanged, Is.Empty, "edits that leave the recipe unchanged: " + string.Join(", ", unchanged));

            void Check(string name, in NpcMeshes.UmaSample s)
            {
                if (Folded.ContainsKey(name))
                {
                    return;
                }

                if (NpcMeshes.UmaRecipeOf(s).Key() == baseKey)
                {
                    unchanged.Add(name);
                }
            }
        }

        // Gate 2, no clamping: on fresh seeds every channel stays inside the range its DNA converter must cover, and
        // every channel actually varies.
        [Test]
        public void ChannelsStayInsideTheirRangesAndVary()
        {
            int n = NpcMeshes.UmaChannels.Count;
            var lo = Enumerable.Repeat(float.MaxValue, n).ToArray();
            var hi = Enumerable.Repeat(float.MinValue, n).ToArray();
            var outside = new List<string>();
            for (ulong planet = 100; planet < 100 + Planets; planet++)
            {
                foreach (NpcSpec spec in Residents(planet, PerPlanet))
                {
                    NpcUmaRecipe r = NpcMeshes.UmaRecipeOf(spec);
                    for (int i = 0; i < n; i++)
                    {
                        float v = r.Dna(i);
                        lo[i] = Math.Min(lo[i], v);
                        hi[i] = Math.Max(hi[i], v);
                        NpcMeshes.UmaChannel c = NpcMeshes.UmaChannels[i];
                        // Values on a clamp bound come out a float ulp past it (0.6 × 1.08); far below save precision.
                        float eps = 1e-4f * (c.Max - c.Min);
                        if (float.IsNaN(v) || v < c.Min - eps || v > c.Max + eps)
                        {
                            outside.Add($"{c.Name}={v:0.####} ({spec.Race})");
                        }
                    }
                }
            }

            var flat = Enumerable.Range(0, n).Where(i => hi[i] - lo[i] <= 1e-6f).Select(i => NpcMeshes.UmaChannels[i].Name).ToList();
            Assert.That(outside.Distinct().Take(20), Is.Empty, "values outside the declared range");
            Assert.That(flat, Is.Empty, "channels that never vary");
        }

        // REQ: characters.md#路人-npc (眉毛粗細、唇厚 σ 20 %) — a negative thickness turns the brow or lip inside out on the
        // procedural head and is meaningless as a blendshape weight.
        [Test]
        public void BrowAndLipThicknessStayPositive()
        {
            for (ulong planet = 300; planet < 300 + Planets; planet++)
            {
                foreach (NpcSpec spec in Residents(planet, PerPlanet))
                {
                    NpcMeshes.Face f = NpcMeshes.UmaSampleOf(spec).Face;
                    Assert.That(f.BrowThick, Is.GreaterThan(0f), spec.Race + " brow");
                    Assert.That(f.LipUp, Is.GreaterThan(0f), spec.Race + " upper lip");
                    Assert.That(f.LipLow, Is.GreaterThan(0f), spec.Race + " lower lip");
                }
            }
        }

        // Gate 2, repeats: residents of one town are told apart at save precision.
        [Test]
        public void TownsRarelyRepeatARecipe()
        {
            for (ulong planet = 200; planet < 200 + Planets; planet++)
            {
                foreach (IGrouping<int, NpcSpec> town in Residents(planet, PerPlanet).Select((s, i) => (s, i))
                             .GroupBy(x => x.i / TownSize, x => x.s))
                {
                    var keys = town.Select(s => NpcMeshes.UmaRecipeOf(s).Key()).ToList();
                    int repeats = keys.Count - keys.Distinct().Count();
                    Assert.That(repeats, Is.LessThanOrEqualTo((int)(MaxTownRepeatShare * keys.Count)),
                        $"planet {planet} town {town.Key}");
                }
            }
        }

        // Diagnostic for re-deriving the declared ranges after the sampling changes: prints the observed span per channel.
        [Test, Explicit("diagnostic")]
        public void ProbeChannelSpans()
        {
            int n = NpcMeshes.UmaChannels.Count;
            var lo = Enumerable.Repeat(float.MaxValue, n).ToArray();
            var hi = Enumerable.Repeat(float.MinValue, n).ToArray();
            for (ulong planet = 0; planet < 20; planet++)
            {
                foreach (NpcSpec spec in Residents(planet, PerPlanet))
                {
                    NpcUmaRecipe r = NpcMeshes.UmaRecipeOf(spec);
                    for (int i = 0; i < n; i++)
                    {
                        lo[i] = Math.Min(lo[i], r.Dna(i));
                        hi[i] = Math.Max(hi[i], r.Dna(i));
                    }
                }
            }

            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                sb.AppendLine($"UMAPROBE {NpcMeshes.UmaChannels[i].Name} {lo[i]:0.#####} {hi[i]:0.#####}");
            }

            Debug.Log(sb.ToString());
        }

        private static IEnumerable<(string, Type)> SampledTypes()
        {
            yield return ("Traits", typeof(NpcMeshes.Traits));
            yield return ("Look", typeof(NpcMeshes.Look));
            yield return ("Rig", typeof(NpcMeshes.Rig));
            yield return ("Face", typeof(NpcMeshes.Face));
        }

        private static NpcSpec FindSpec(Func<NpcMeshes.UmaSample, bool> want)
        {
            foreach (NpcSpec spec in Residents(7, 20000))
            {
                if (want(NpcMeshes.UmaSampleOf(spec)))
                {
                    return spec;
                }
            }

            Assert.Fail("no resident matches");
            return default;
        }

        private static object Nudge(FieldInfo f, object v)
        {
            Type t = f.FieldType;
            if (t == typeof(float))
            {
                return (float)v + 0.05f;
            }

            if (t == typeof(bool))
            {
                return !(bool)v;
            }

            if (t == typeof(int))
            {
                // Hair and beard styles index slot tables; step within them, skipping the bald hair style.
                int i = (int)v + 1;
                return f.Name == "HairStyle" ? (i == 4 ? 5 : i % 9) : f.Name == "BeardStyle" ? i % 6 : i;
            }

            if (t == typeof(Color))
            {
                Color c = (Color)v;
                return new Color(1f - c.r, 1f - c.g, 1f - c.b, c.a);
            }

            if (t.IsEnum)
            {
                Array values = Enum.GetValues(t);
                int at = Array.IndexOf(values, v);
                return values.GetValue((at + 1) % values.Length);
            }

            throw new NotSupportedException(f.Name + " of " + t);
        }
    }
}
