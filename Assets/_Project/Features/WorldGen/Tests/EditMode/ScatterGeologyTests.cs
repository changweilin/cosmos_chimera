using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    // REQ: planet-world.md#地質變體 (P3-24 a) — bedrock swaps a landform's country rock; shape and everything else stay.
    public sealed class ScatterGeologyTests
    {
        private static readonly PlanetConfig Planet = new PlanetConfig(640f, float3.zero);

        private static float Hills(float3 d) => 3f * math.sin(d.x * 40f) * math.cos(d.z * 35f);

        private static ScatterPlanner WithGeology(string landform, System.Func<float3, Geology> geologyAt) =>
            new ScatterPlanner(Planet, 12345, "verdant_planet", Hills, null, null, _ => 0.7f,
                (d, roll) => ScatterBiomes.BiomeOf(landform), (d, roll) => geologyAt(d));

        private static ScatterPlanner WithoutGeology(string landform) =>
            new ScatterPlanner(Planet, 12345, "verdant_planet", Hills, null, null, _ => 0.7f,
                (d, roll) => ScatterBiomes.BiomeOf(landform));

        private static Dictionary<string, int> Census(ScatterPlanner p, System.Func<ScatterItem, bool> where = null)
        {
            var census = new Dictionary<string, int>();
            for (long k = 0; k < 6L * p.CellsPerFace * p.CellsPerFace; k += 3)
            {
                if (!p.TryGetItem(k, out ScatterItem it) || (where != null && !where(it))) continue;
                string id = p.Kinds[it.KindIndex].Id;
                census[id] = census.TryGetValue(id, out int n) ? n + 1 : 1;
            }

            return census;
        }

        [Test]
        public void Landforms_defined_by_their_rock_keep_it_and_the_others_get_every_bedrock()
        {
            foreach (string fixedRock in new[] { "desert", "canyon", "rift_valley", "volcano_foothills", "cavern_outer",
                         "archipelago", "swamp", "ice_field" })
            {
                int biome = ScatterBiomes.BiomeOf(fixedRock);
                Assert.That(ScatterGeology.Varies(biome), Is.False, fixedRock);
                for (uint h = 0; h < 200u; h++)
                {
                    Assert.That(ScatterGeology.For(biome, h * 2654435761u), Is.EqualTo(Geology.Native), fixedRock);
                }
            }

            int mountain = ScatterBiomes.BiomeOf("mountain");
            Assert.That(ScatterGeology.Varies(mountain), Is.True);
            var seen = new HashSet<Geology>();
            for (uint h = 0; h < 200u; h++)
            {
                Geology g = ScatterGeology.For(mountain, h);
                Assert.That(ScatterGeology.For(mountain, h), Is.EqualTo(g));
                seen.Add(g);
            }

            Assert.That(seen.Count, Is.EqualTo(ScatterGeology.Count));
            for (uint h = 0; h < 200u; h++)
            {
                Assert.That(ScatterGeology.For(ScatterBiomes.BiomeOf("fjord"), h), Is.Not.EqualTo(Geology.Karst));
            }
        }

        [Test]
        public void Substitute_only_swaps_country_rock_and_stays_inside_the_bedrock()
        {
            var sets = new Dictionary<Geology, string[]>
            {
                [Geology.Karst] = new[] { "limestone", "stone_spire", "marble" },
                [Geology.Basalt] = new[] { "basalt", "basalt_column" },
                [Geology.Granite] = new[] { "granite", "granite_tor" },
                [Geology.Slate] = new[] { "slate" },
            };
            string[] country = { "granite", "limestone", "marble", "slate", "sandstone", "basalt" };
            for (int s = 0; s < ScatterBiomes.CanopySpeciesCount; s++)
            {
                string id = ScatterBiomes.Species[s].Id;
                bool isCountry = System.Array.IndexOf(country, id) >= 0;
                Assert.That(ScatterGeology.Substitute(s, Geology.Native, 7u), Is.EqualTo(s));
                foreach (KeyValuePair<Geology, string[]> set in sets)
                {
                    var seen = new HashSet<string>();
                    for (uint roll = 0; roll < 100u; roll++)
                    {
                        int to = ScatterGeology.Substitute(s, set.Key, roll);
                        Assert.That(to, Is.InRange(0, ScatterBiomes.CanopySpeciesCount - 1));
                        seen.Add(ScatterBiomes.Species[to].Id);
                    }

                    if (isCountry)
                    {
                        Assert.That(seen, Is.EquivalentTo(set.Value), id + " on " + set.Key);
                    }
                    else
                    {
                        Assert.That(seen, Is.EquivalentTo(new[] { id }), id + " is not country rock");
                    }
                }
            }
        }

        [Test]
        public void Main_rock_is_a_catalog_species_of_the_bedrock()
        {
            Assert.That(ScatterGeology.MainRock(Geology.Native), Is.Null);
            for (Geology g = Geology.Karst; g <= Geology.Slate; g++)
            {
                int species = ScatterBiomes.IndexOf(ScatterGeology.MainRock(g));
                Assert.That(ScatterGeology.Substitute(ScatterBiomes.IndexOf("sandstone"), g, 0u), Is.EqualTo(species),
                    "the main rock is the bedrock's most common one");
            }
        }

        [Test]
        public void No_landform_table_grows_a_granite_tor_by_itself()
        {
            int tor = ScatterBiomes.IndexOf("granite_tor");
            for (int b = 0; b < ScatterBiomes.BiomeIds.Count; b++)
            {
                for (uint roll = 0; roll < 64u; roll++)
                {
                    Assert.That(ScatterBiomes.PickCanopy(b, roll), Is.Not.EqualTo(tor), ScatterBiomes.BiomeIds[b]);
                }
            }
        }

        [Test]
        public void Bedrock_changes_the_rocks_and_nothing_else()
        {
            // Karst east, native west, over grassland (tree slot, umbrella, granite, limestone).
            ScatterPlanner plain = WithoutGeology("grassland");
            ScatterPlanner p = WithGeology("grassland", d => d.x > 0f ? Geology.Karst : Geology.Native);
            int swapped = 0, same = 0;
            for (long k = 0; k < 6L * p.CellsPerFace * p.CellsPerFace; k += 3)
            {
                bool had = plain.TryGetItem(k, out ScatterItem a);
                Assert.That(p.TryGetItem(k, out ScatterItem b), Is.EqualTo(had), "bedrock never adds or removes an item");
                if (!had) continue;
                Assert.That(b.Direction, Is.EqualTo(a.Direction));
                Assert.That(b.YawDeg, Is.EqualTo(a.YawDeg));
                string from = plain.Kinds[a.KindIndex].Id, to = p.Kinds[b.KindIndex].Id;
                bool rock = from == "granite" || from == "limestone";
                if (!rock || a.Direction.x < -0.05f)
                {
                    if (rock && math.abs(a.Direction.x) <= 0.05f) continue;
                    Assert.That(b.KindIndex, Is.EqualTo(a.KindIndex));
                    Assert.That(b.SizeM, Is.EqualTo(a.SizeM));
                    same++;
                }
                else if (a.Direction.x > 0.05f)
                {
                    Assert.That(new[] { "limestone", "stone_spire", "marble" }, Does.Contain(to));
                    ScatterKind kind = p.Kinds[b.KindIndex];
                    Assert.That(b.SizeM, Is.InRange(kind.MinSizeM, kind.MaxSizeM));
                    Assert.That(b.Shape, Is.EqualTo(kind.Shape));
                    swapped++;
                }
            }

            Assert.That(swapped, Is.GreaterThan(100));
            Assert.That(same, Is.GreaterThan(100));
            Dictionary<string, int> east = Census(p, it => it.Direction.x > 0.05f);
            Assert.That(east.ContainsKey("granite"), Is.False, "no granite on karst");
            Assert.That(east.Keys, Is.SupersetOf(new[] { "limestone", "stone_spire", "marble", "broadleaf" }));
        }

        [Test]
        public void Each_bedrock_is_recognisable_by_its_rocks()
        {
            Dictionary<string, int> granite = Census(WithGeology("mountain", _ => Geology.Granite));
            Assert.That(granite.Keys, Is.SupersetOf(new[] { "granite", "granite_tor", "moraine", "ore_iron" }));
            Assert.That(granite.ContainsKey("slate"), Is.False);
            Dictionary<string, int> slate = Census(WithGeology("mountain", _ => Geology.Slate));
            Assert.That(slate.ContainsKey("granite"), Is.False);
            Assert.That(slate["slate"], Is.GreaterThan(100));
            Dictionary<string, int> basalt = Census(WithGeology("plateau", _ => Geology.Basalt));
            Assert.That(basalt.Keys, Is.SupersetOf(new[] { "basalt", "basalt_column", "umbrella" }));
            Assert.That(basalt.ContainsKey("sandstone") || basalt.ContainsKey("slate"), Is.False);
        }

        [Test]
        public void A_native_bedrock_map_equals_no_map()
        {
            ScatterPlanner plain = WithoutGeology("mountain");
            ScatterPlanner native = WithGeology("mountain", _ => Geology.Native);
            for (long k = 0; k < 6L * plain.CellsPerFace * plain.CellsPerFace; k += 7)
            {
                bool had = plain.TryGetItem(k, out ScatterItem a);
                Assert.That(native.TryGetItem(k, out ScatterItem b), Is.EqualTo(had));
                if (!had) continue;
                Assert.That(b.KindIndex, Is.EqualTo(a.KindIndex));
                Assert.That(b.SizeM, Is.EqualTo(a.SizeM));
                Assert.That(b.Direction, Is.EqualTo(a.Direction));
            }
        }

        [Test]
        public void Planet_geologies_are_reproducible_and_follow_each_stamp()
        {
            var tags = new List<string> { "mountain" };
            using PlanetTemplates t = TemplatePlanner.Build(Planet, 42UL, tags, PlanetSize.Planet);
            Geology[] a = TemplatePlanner.Geologies(t, 42UL, "verdant_planet", out Geology baseA);
            Geology[] b = TemplatePlanner.Geologies(t, 42UL, "verdant_planet", out Geology baseB);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(baseA, Is.EqualTo(baseB));
            Assert.That(a.Length, Is.EqualTo(t.StampIds.Count));
            for (int i = 0; i < a.Length; i++)
            {
                int biome = ScatterBiomes.BiomeOf(LandformSynth.KindOf(t.StampIds[i]));
                if (!ScatterGeology.Varies(biome)) Assert.That(a[i], Is.EqualTo(Geology.Native), t.StampIds[i]);
            }

            System.Func<float3, uint, Geology> at = TemplatePlanner.ScatterGeologyAt(t, a, baseA);
            System.Func<float3, uint, int> biomeAt = TemplatePlanner.ScatterBiome(t, "verdant_planet");
            TemplateView view = t.View;
            for (int i = 0; i < a.Length; i++)
            {
                float3 centre = view.Stamp(i).Center;
                Assert.That(view.TryGetStamp(centre, out int index, out float w), Is.True);
                Assert.That(index, Is.EqualTo(i));
                Assert.That(w, Is.EqualTo(1f));
                Assert.That(at(centre, 0xFFFFu), Is.EqualTo(a[i]), "a stamp's core is its own bedrock for any roll");
                Assert.That(biomeAt(centre, 0xFFFFu), Is.EqualTo(ScatterBiomes.BiomeOf(LandformSynth.KindOf(t.StampIds[i]))));
            }

            // Some seed gives a planet whose stamps do not all share one bedrock.
            bool varied = false;
            for (ulong seed = 1; seed <= 20 && !varied; seed++)
            {
                using PlanetTemplates s = TemplatePlanner.Build(Planet, seed, tags, PlanetSize.Planet);
                Geology[] g = TemplatePlanner.Geologies(s, seed, "verdant_planet", out _);
                foreach (Geology x in g) varied |= x != g[0];
            }

            Assert.That(varied, Is.True);
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => ScatterGeology.For(ScatterBiomes.BiomeIds.Count, 0u));
            Assert.Throws<ContractException>(() => ScatterGeology.Varies(-1));
            Assert.Throws<ContractException>(() => ScatterGeology.Substitute(ScatterBiomes.CanopySpeciesCount, Geology.Karst, 0u));
            Assert.Throws<ContractException>(() => ScatterGeology.Substitute(0, (Geology)9, 0u));
            Assert.Throws<ContractException>(() => ScatterGeology.MainRock((Geology)9));
            Assert.Throws<ContractException>(() => TemplatePlanner.Geologies(null, 1UL, null, out _));
            Assert.Throws<ContractException>(() => new ScatterPlanner(Planet, 1, null, Hills, null, null, _ => 0.5f,
                (d, roll) => 0, null));
            using PlanetTemplates t = TemplatePlanner.Build(Planet, 42UL, new List<string> { "fjord" }, PlanetSize.Moon);
            Assert.Throws<ContractException>(() => TemplatePlanner.ScatterGeologyAt(t, new Geology[0], Geology.Native));
            Assert.Throws<ContractException>(() => t.View.Stamp(t.View.Count));
            ScatterPlanner bad = new ScatterPlanner(Planet, 1, null, Hills, null, null, _ => 1f,
                (d, roll) => ScatterBiomes.BiomeOf("desert"), (d, roll) => (Geology)9);
            Assert.Throws<ContractException>(() =>
            {
                for (long k = 0; k < 400; k++) bad.TryGetItem(k, out _);
            });
        }
    }
}
