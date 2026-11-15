using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class LandformSynthTests
    {
        // Pinned digest of every kind at seed 12345, index 0 (LandformSynth.GeneratorVersion 1). The synth is integer-only,
        // so unlike RunPlanner's pins this one must also hold on IL2CPP and other machines (ADR-0007 seaCount).
        private const ulong SnapshotSeed12345V1 = 15533561194335027287UL;

        private const int Size = LandformSynth.Size;

        private static int Cheb(int x, int y) =>
            math.max(math.abs(2 * x - (Size - 1)), math.abs((Size - 1) - 2 * y));

        [Test]
        public void Kinds_are_the_twelve_schema_terrain_tags()
        {
            CollectionAssert.AreEquivalent(new[]
            {
                "forest", "river_valley", "volcano", "rift_valley", "ice_field", "fjord",
                "plateau", "archipelago", "canyon", "cave", "eternal_day", "eternal_night",
            }, LandformSynth.Kinds);
        }

        [Test]
        public void Every_kind_builds_a_full_grid_with_consistent_water()
        {
            foreach (string kind in LandformSynth.Kinds)
            {
                TemplateSource s = LandformSynth.Generate(kind, 7UL, 1);
                Assert.That(s.Size, Is.EqualTo(Size), kind);
                Assert.That(s.Heights.Length, Is.EqualTo(Size * Size), kind);
                Assert.That(s.SeaNorm, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f), kind);
                int seaQ = (int)(s.SeaNorm * 65536f);
                ushort lo = ushort.MaxValue, hi = 0;
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int i = y * Size + x;
                        lo = (ushort)math.min((int)lo, s.Heights[i]);
                        hi = (ushort)math.max((int)hi, s.Heights[i]);
                        if (s.Water[i] == 1)
                        {
                            Assert.That(s.Heights[i], Is.LessThan(seaQ), $"{kind} sea above sea level at {x},{y}");
                        }
                        else if (s.Water[i] == 2)
                        {
                            Assert.That(s.Heights[i], Is.GreaterThanOrEqualTo(seaQ), $"{kind} river below sea level at {x},{y}");
                        }

                        if (Cheb(x, y) > 210)
                        {
                            Assert.That(s.Water[i], Is.EqualTo(0), $"{kind} water in the blend band at {x},{y}");
                        }
                    }
                }

                Assert.That(hi - lo, Is.GreaterThan(65535 / 5), kind + " has real relief, not a flat plate");
            }
        }

        [Test]
        public void Kinds_without_sea_level_have_no_sea_and_coastal_kinds_have_both()
        {
            foreach (string kind in LandformSynth.Kinds)
            {
                TemplateSource s = LandformSynth.Generate(kind, 99UL, 0);
                int sea = 0;
                foreach (byte w in s.Water)
                {
                    sea += w == 1 ? 1 : 0;
                }

                if (s.SeaNorm == 0f)
                {
                    Assert.That(sea, Is.EqualTo(0), kind);
                }
            }

            foreach (string kind in new[] { "fjord", "archipelago", "eternal_day", "eternal_night" })
            {
                TemplateSource s = LandformSynth.Generate(kind, 99UL, 0);
                int sea = 0;
                foreach (byte w in s.Water)
                {
                    sea += w == 1 ? 1 : 0;
                }

                Assert.That(sea, Is.GreaterThan(0).And.LessThan(Size * Size / 2), kind + " mixes sea and land");
            }
        }

        [Test]
        public void Volcano_peaks_near_the_centre()
        {
            TemplateSource s = LandformSynth.Generate("volcano", 3UL, 0);
            int centre = s.Heights[(Size / 2) * Size + Size / 2 + 20];
            int edge = s.Heights[(Size / 2) * Size + 4];
            Assert.That(centre, Is.GreaterThan(edge + 65535 / 3));
        }

        [Test]
        public void Same_inputs_give_identical_grids_and_index_or_seed_changes_them()
        {
            TemplateSource a = LandformSynth.Generate("canyon", 5UL, 2);
            TemplateSource b = LandformSynth.Generate("canyon", 5UL, 2);
            CollectionAssert.AreEqual(a.Heights, b.Heights);
            CollectionAssert.AreEqual(a.Water, b.Water);
            CollectionAssert.AreNotEqual(a.Heights, LandformSynth.Generate("canyon", 5UL, 3).Heights);
            CollectionAssert.AreNotEqual(a.Heights, LandformSynth.Generate("canyon", 6UL, 2).Heights);
        }

        [Test]
        public void Unknown_kind_or_index_out_of_range_violates_the_contract()
        {
            Assert.Throws<ContractException>(() => LandformSynth.Generate("moon_crater", 1UL, 0));
            Assert.Throws<ContractException>(() => LandformSynth.Generate(null, 1UL, 0));
            Assert.Throws<ContractException>(() => LandformSynth.Generate("forest", 1UL, -1));
            Assert.Throws<ContractException>(() => LandformSynth.Generate("forest", 1UL, LandformSynth.MaxIndex + 1));
            Assert.DoesNotThrow(() => LandformSynth.Generate("forest", 1UL, LandformSynth.MaxIndex));
        }

        [Test]
        public void Snapshot_seed_12345_is_pinned()
        {
            Assert.That(LandformSynth.GeneratorVersion, Is.EqualTo(1), "bump the pin with the version");
            ulong h = 14695981039346656037UL;
            foreach (string kind in LandformSynth.Kinds)
            {
                TemplateSource s = LandformSynth.Generate(kind, 12345UL, 0);
                foreach (ushort v in s.Heights)
                {
                    h = (h ^ v) * 1099511628211UL;
                }

                foreach (byte w in s.Water)
                {
                    h = (h ^ w) * 1099511628211UL;
                }
            }

            Assert.That(h, Is.EqualTo(SnapshotSeed12345V1), "actual " + h);
        }

        [Test]
        public void TemplatePlanner_Build_places_one_synthesized_stamp_per_slot()
        {
            var planet = new PlanetConfig(640f, float3.zero);
            using PlanetTemplates t = TemplatePlanner.Build(planet, 42UL, new List<string> { "fjord" }, PlanetSize.Moon);
            using PlanetTemplates u = TemplatePlanner.Build(planet, 42UL, new List<string> { "fjord" }, PlanetSize.Moon);
            using PlanetTemplates big = TemplatePlanner.Build(new PlanetConfig(960f, float3.zero), 42UL,
                new List<string> { "fjord" }, PlanetSize.Planet);
            Assert.That(t.View.Count, Is.EqualTo(TemplatePlanner.CountFor(PlanetSize.Moon)));
            Assert.That(big.View.Count, Is.EqualTo(TemplatePlanner.CountFor(PlanetSize.Planet)));
            PlanetHeight noise = PlanetHeight.ForPlanet(planet, 42UL);
            var rng = new Core.Random.Pcg32(1UL, 2UL);
            int sea = 0;
            for (int i = 0; i < 4000; i++)
            {
                float3 d = math.normalize(new float3(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f));
                Assert.That(t.Sample(noise, d), Is.EqualTo(u.Sample(noise, d)), "same inputs → same surface");
                Assert.That(t.View.IsSea(d), Is.EqualTo(u.View.IsSea(d)));
                sea += t.View.IsSea(d) ? 1 : 0;
            }

            Assert.That(sea, Is.GreaterThan(0), "a fjord planet's first stamp is a fjord, which has sea");
        }

        [Test]
        public void KindOf_recovers_the_kind_of_every_generated_stamp()
        {
            foreach (string kind in LandformSynth.Kinds)
            {
                Assert.That(LandformSynth.KindOf(LandformSynth.Generate(kind, 7UL, 3).Id), Is.EqualTo(kind));
            }

            Assert.Throws<ContractException>(() => LandformSynth.KindOf(null));
            Assert.Throws<ContractException>(() => LandformSynth.KindOf("forest"));
            Assert.Throws<ContractException>(() => LandformSynth.KindOf("no_such_kind#0"));
        }

        [Test]
        public void ScatterDensity_follows_the_landform_under_each_point()
        {
            var planet = new PlanetConfig(640f, float3.zero);
            const float Base = 0.33f;
            using PlanetTemplates t = TemplatePlanner.Build(planet, 42UL, new List<string> { "fjord" }, PlanetSize.Moon);
            System.Func<float3, float> density = TemplatePlanner.ScatterDensity(t, Base);
            TemplateView view = t.View;
            var rng = new Core.Random.Pcg32(3UL, 4UL);
            int outside = 0, core = 0, band = 0;
            for (int i = 0; i < 4000; i++)
            {
                float3 d = math.normalize(new float3(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f));
                float value = density(d);
                Assert.That(value, Is.InRange(0f, 1f));
                if (!view.TryGetStamp(d, out int stamp, out float w))
                {
                    outside++;
                    Assert.That(value, Is.EqualTo(Base));
                    continue;
                }

                string kind = LandformSynth.KindOf(t.StampIds[stamp]);
                // REQ: planet-world.md#1 — volcano and cave stamps are the general landform around them on the surface.
                string terrain = kind == "volcano" ? "volcano_foothills" : kind == "cave" ? "cavern_outer" : kind;
                float own = TerrainProfiles.For(terrain).ScatterDensity;
                if (w >= 1f)
                {
                    core++;
                    Assert.That(value, Is.EqualTo(own).Within(1e-6f));
                }
                else
                {
                    band++;
                    Assert.That(value, Is.InRange(math.min(Base, own) - 1e-6f, math.max(Base, own) + 1e-6f));
                }
            }

            Assert.That(outside, Is.GreaterThan(0));
            Assert.That(core, Is.GreaterThan(0));
            Assert.That(band, Is.GreaterThan(0));
        }

        [Test]
        public void ScatterDensity_rejects_null_templates_and_out_of_range_base()
        {
            using PlanetTemplates t = TemplatePlanner.Build(new PlanetConfig(640f, float3.zero), 1UL,
                new List<string>(), PlanetSize.Moon);
            Assert.Throws<ContractException>(() => TemplatePlanner.ScatterDensity(null, 0.5f));
            Assert.Throws<ContractException>(() => TemplatePlanner.ScatterDensity(t, -0.1f));
            Assert.Throws<ContractException>(() => TemplatePlanner.ScatterDensity(t, 1.1f));
            Assert.Throws<ContractException>(() => TemplatePlanner.ScatterDensity(t, float.NaN));
        }

        [Test]
        public void TemplatePlanner_Build_rejects_null_tags_and_invalid_config()
        {
            Assert.Throws<ContractException>(() =>
                TemplatePlanner.Build(new PlanetConfig(640f, float3.zero), 1UL, null, PlanetSize.Moon));
            Assert.Throws<ContractException>(() =>
                TemplatePlanner.Build(default, 1UL, new List<string>(), PlanetSize.Moon));
        }
    }
}
