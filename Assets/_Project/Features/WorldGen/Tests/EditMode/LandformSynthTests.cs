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
        // Pinned digests at seed 12345, index 0. The synth is integer-only, so unlike RunPlanner's pins these must also
        // hold on IL2CPP and other machines (ADR-0007 seaCount). V1 covers the twelve v1 kinds, which v2 must not
        // change; V2 covers all kinds (LandformSynth.GeneratorVersion 2: twelve kinds added, Q-W18 / P3-24 b).
        private const ulong SnapshotSeed12345V1 = 15533561194335027287UL;
        private const ulong SnapshotSeed12345V2 = 12616586232240399627UL;

        private static readonly string[] V1Kinds =
        {
            "forest", "river_valley", "volcano", "rift_valley", "ice_field", "fjord",
            "plateau", "archipelago", "canyon", "cave", "eternal_day", "eternal_night",
        };

        private static readonly string[] GlacialKinds = { "tundra", "glacier", "cirque", "moraine" };

        private static int Count(TemplateSource s, byte water)
        {
            int n = 0;
            foreach (byte w in s.Water)
            {
                n += w == water ? 1 : 0;
            }

            return n;
        }

        private const int Size = LandformSynth.Size;

        private static int Cheb(int x, int y) =>
            math.max(math.abs(2 * x - (Size - 1)), math.abs((Size - 1) - 2 * y));

        [Test]
        public void Kinds_start_with_the_twelve_schema_terrain_tags_and_add_the_table_cold_and_bedrock_landforms()
        {
            var expected = new List<string>(V1Kinds);
            expected.AddRange(new[] { "grassland", "desert", "swamp", "mountain" });
            expected.AddRange(GlacialKinds);
            expected.AddRange(new[] { "karst", "basalt_terrace", "granite_domes", "slate_cliffs" });
            CollectionAssert.AreEqual(expected, LandformSynth.Kinds);
        }

        [Test]
        public void Every_kind_has_its_own_surface_profile_species_table_and_bedrock_rule()
        {
            foreach (string kind in LandformSynth.Kinds)
            {
                string terrain = kind == "volcano" ? "volcano_foothills" : kind == "cave" ? "cavern_outer" : kind;
                Assert.That(TerrainProfiles.For(terrain).Id, Is.EqualTo(terrain), "no silent grassland fallback");
                Assert.That(TerrainProfiles.For(terrain).IsAdvancedDungeon, Is.False, terrain);
                Assert.That(ScatterBiomes.BiomeIds[ScatterBiomes.BiomeOf(kind)], Is.EqualTo(terrain));
            }

            // REQ: planet-world.md#地質變體 (b) — a bedrock landform is always its bedrock.
            var pinned = new (string kind, Geology geology)[]
            {
                ("karst", Geology.Karst), ("basalt_terrace", Geology.Basalt),
                ("granite_domes", Geology.Granite), ("slate_cliffs", Geology.Slate),
            };
            foreach ((string kind, Geology geology) in pinned)
            {
                for (uint h = 0; h < 200u; h++)
                {
                    Assert.That(ScatterGeology.For(ScatterBiomes.BiomeOf(kind), h * 2654435761u), Is.EqualTo(geology), kind);
                }
            }
        }

        [Test]
        public void Lake_landforms_hold_a_little_water_and_a_glacier_drains_by_a_meltwater_stream()
        {
            foreach (ulong seed in new[] { 3UL, 99UL, 12345UL })
            {
                foreach (string kind in new[] { "tundra", "cirque", "moraine", "swamp" })
                {
                    int sea = Count(LandformSynth.Generate(kind, seed, 0), 1);
                    Assert.That(sea, Is.GreaterThan(0).And.LessThan(Size * Size / 4), kind + " has lakes, not a sea");
                }

                TemplateSource glacier = LandformSynth.Generate("glacier", seed, 0);
                Assert.That(Count(glacier, 1), Is.EqualTo(0));
                Assert.That(Count(glacier, 2), Is.GreaterThan(0));
            }
        }

        [Test]
        public void Glacier_trough_floor_lies_below_both_shoulders()
        {
            // Dir is random, so find the trough by its river: every river sample must sit well below the stamp's top.
            TemplateSource s = LandformSynth.Generate("glacier", 12345UL, 0);
            int top = 0;
            foreach (ushort h in s.Heights)
            {
                top = math.max(top, h);
            }

            for (int i = 0; i < s.Water.Length; i++)
            {
                if (s.Water[i] == 2)
                {
                    Assert.That((int)s.Heights[i], Is.LessThan(top / 2));
                }
            }
        }

        [Test]
        public void Glacial_kinds_appear_only_on_planets_that_prefer_them_or_in_the_cold_band()
        {
            var cold = new HashSet<string>(GlacialKinds) { "ice_field" };
            var tagSets = new[]
            {
                new[] { "forest", "river_valley" }, new[] { "volcano", "rift_valley" }, new[] { "plateau", "archipelago" },
                new[] { "canyon", "cave" }, new[] { "eternal_day", "eternal_night" }, new string[0],
            };
            int coldStamps = 0;
            foreach (string[] tags in tagSets)
            {
                for (ulong seed = 1; seed <= 150; seed++)
                {
                    foreach (int count in new[] { 4, 6 })
                    {
                        string[] kinds = TemplatePlanner.Kinds(seed, tags, count, out List<TemplatePlacement> where);
                        Assert.That(kinds.Length, Is.EqualTo(count));
                        Assert.That(where.Count, Is.EqualTo(count));
                        for (int i = 0; i < count; i++)
                        {
                            CollectionAssert.Contains(LandformSynth.Kinds, kinds[i]);
                            bool inBand = math.abs(where[i].Center.y) > TemplatePlanner.ColdBandY;
                            if (inBand)
                            {
                                coldStamps++;
                                Assert.That(cold.Contains(kinds[i]) || kinds[i] == "fjord", Is.True, $"seed {seed}: {kinds[i]} in the cold band");
                            }
                            else
                            {
                                CollectionAssert.DoesNotContain(GlacialKinds, kinds[i], $"seed {seed}: glacial landform outside the cold band");
                            }
                        }
                    }
                }
            }

            Assert.That(coldStamps, Is.GreaterThan(100), "the cold band is actually reached");
        }

        [Test]
        public void An_ice_planet_prefers_glacial_kinds_at_any_latitude_and_every_one_of_them_occurs()
        {
            var tags = new[] { "ice_field", "fjord" };
            var preferred = new HashSet<string>(GlacialKinds) { "ice_field", "fjord" };
            var seen = new HashSet<string>();
            for (ulong seed = 1; seed <= 100; seed++)
            {
                foreach (string kind in TemplatePlanner.Kinds(seed, tags, 4, out _))
                {
                    Assert.That(preferred.Contains(kind), Is.True, $"seed {seed}: {kind}");
                    seen.Add(kind);
                }
            }

            CollectionAssert.AreEquivalent(preferred, seen);
        }

        [Test]
        public void Affinity_puts_related_landforms_first_and_Kinds_is_deterministic()
        {
            // verdant tags: forest, river_valley plus grassland (→ forest) and swamp (→ river_valley) fill four slots.
            var verdant = new[] { "forest", "river_valley" };
            var expected = new HashSet<string> { "forest", "river_valley", "grassland", "swamp", "tundra", "glacier",
                "cirque", "moraine", "ice_field" };
            for (ulong seed = 1; seed <= 60; seed++)
            {
                string[] a = TemplatePlanner.Kinds(seed, verdant, 4, out List<TemplatePlacement> pa);
                string[] b = TemplatePlanner.Kinds(seed, verdant, 4, out List<TemplatePlacement> pb);
                CollectionAssert.AreEqual(a, b);
                for (int i = 0; i < a.Length; i++)
                {
                    Assert.That(expected.Contains(a[i]), Is.True, $"seed {seed}: {a[i]}");
                    Assert.That(pa[i].Center, Is.EqualTo(pb[i].Center));
                }
            }

            Assert.Throws<ContractException>(() => TemplatePlanner.Kinds(1UL, null, 4, out _));
            Assert.Throws<ContractException>(() => TemplatePlanner.Kinds(1UL, verdant, 0, out _));
            Assert.Throws<ContractException>(() => TemplatePlanner.Kinds(1UL, verdant, 9, out _));
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
            Assert.That(LandformSynth.GeneratorVersion, Is.EqualTo(2), "bump the pin with the version");
            Assert.That(Digest(V1Kinds), Is.EqualTo(SnapshotSeed12345V1), "v1 kinds must stay bit-identical");
            ulong h = Digest(LandformSynth.Kinds);
            Assert.That(h, Is.EqualTo(SnapshotSeed12345V2), "actual " + h);
        }

        private static ulong Digest(IReadOnlyList<string> kinds)
        {
            ulong h = 14695981039346656037UL;
            foreach (string kind in kinds)
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

            return h;
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
        public void Surface_profiles_follow_stamps_and_keep_volcano_and_cave_on_the_general_surface()
        {
            var planet = new PlanetConfig(640f, float3.zero);
            var sources = new[] { LandformSynth.Generate("volcano", 7, 0), LandformSynth.Generate("cave", 7, 1),
                LandformSynth.Generate("ice_field", 7, 2) };
            var centers = new[] { new float3(0, 1, 0), new float3(1, 0, 0), new float3(0, -1, 0) };
            var placements = new[] { new TemplatePlacement(0, centers[0], 0f, 0.3f),
                new TemplatePlacement(1, centers[1], 0f, 0.3f), new TemplatePlacement(2, centers[2], 0f, 0.3f) };
            using PlanetTemplates templates = PlanetTemplates.Create(planet, sources, placements);
            System.Func<float3, TerrainProfile> at = TemplatePlanner.SurfaceProfile(templates, "verdant_planet");
            var expected = new[] { "volcano_foothills", "cavern_outer", "ice_field" };
            for (int i = 0; i < centers.Length; i++)
            {
                Assert.That(at(centers[i]).Id, Is.EqualTo(expected[i]));
                Assert.That(at(centers[i]).IsAdvancedDungeon, Is.False);
            }
            Assert.That(at(new float3(0, 0, 1)).Id, Is.EqualTo("forest"), "between stamps use the planet baseline");
        }

        [TestCase("verdant_planet", "forest")]
        [TestCase("ice_crystal_planet", "ice_field")]
        [TestCase(null, "grassland")]
        [TestCase("unknown", "grassland")]
        public void Surface_profiles_without_landforms_use_the_planet_and_validate_directions(string type, string expected)
        {
            System.Func<float3, TerrainProfile> at = TemplatePlanner.SurfaceProfile(null, type);
            Assert.That(at(new float3(0, 1, 0)).Id, Is.EqualTo(expected));
            Assert.That(at(new float3(0, -1, 0)).Id, Is.EqualTo(expected));
            Assert.Throws<ContractException>(() => at(float3.zero));
            Assert.Throws<ContractException>(() => at(new float3(0, 2, 0)));
            Assert.Throws<ContractException>(() => at(new float3(float.NaN, 0, 0)));
            Assert.Throws<ContractException>(() => at(new float3(float.PositiveInfinity, 0, 0)));
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
