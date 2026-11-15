using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class ScatterPlannerTests
    {
        private static readonly PlanetConfig Planet = new PlanetConfig(640f, float3.zero);

        private static float Hills(float3 d) => 3f * math.sin(d.x * 40f) * math.cos(d.z * 35f);

        private static ScatterPlanner Planner(string type = "verdant_planet", System.Func<float3, bool> isClear = null) =>
            new ScatterPlanner(Planet, 12345, type, Hills, d => d.y < -0.9f, isClear);

        private static ulong Fold(ulong h, ulong v) => (h ^ v) * 1099511628211ul;

        private static ulong Digest(ScatterPlanner p, float3 up, bool understory = false)
        {
            var cells = new HashSet<long>();
            p.CollectCells(up, 90f, cells);
            var keys = new List<long>(cells);
            keys.Sort();
            ulong h = 14695981039346656037ul;
            foreach (long k in keys)
            {
                if (p.IsUnderstoryKey(k) != understory || !p.TryGetItem(k, out ScatterItem it)) continue;
                h = Fold(h, (ulong)k);
                h = Fold(h, (ulong)it.KindIndex);
                h = Fold(h, math.asuint(it.Direction.x));
                h = Fold(h, math.asuint(it.Direction.y));
                h = Fold(h, math.asuint(it.Direction.z));
                h = Fold(h, math.asuint(it.SizeM));
                h = Fold(h, math.asuint(it.YawDeg));
            }

            return h;
        }

        [Test]
        public void Same_inputs_give_the_same_items_in_any_order()
        {
            float3 up = math.normalize(new float3(1, 1, 1));
            ScatterPlanner a = Planner(), b = Planner();
            var cells = new HashSet<long>();
            a.CollectCells(up, 90f, cells);
            var keys = new List<long>(cells);
            keys.Sort((x, y) => y.CompareTo(x));
            foreach (long k in keys) b.TryGetItem(k, out _);
            Assert.That(Digest(b, up), Is.EqualTo(Digest(a, up)));
            Assert.That(Digest(b, up, true), Is.EqualTo(Digest(a, up, true)));
            Assert.That(Digest(a, up), Is.EqualTo(Digest(a, up)), "cached answers equal fresh ones");
        }

        // Snapshot (testing.md): update only with a ScatterPlanner.GeneratorVersion change.
        [Test]
        public void Snapshot_seed_12345()
        {
            // Version 4 (Q-W15): species follow the landform and forest stands, so both digests were re-pinned.
            Assert.That(ScatterPlanner.GeneratorVersion, Is.EqualTo(4), "new version: re-pin the digests below");
            float3 up = math.normalize(new float3(1, 1, 1));
            Assert.That(Digest(Planner(), up), Is.EqualTo(SnapshotDigest));
            Assert.That(Digest(Planner(), up, true), Is.EqualTo(UnderstoryDigest));
        }

        private static ScatterPlanner PerPoint(System.Func<float3, float> chanceAt) =>
            new ScatterPlanner(Planet, 12345, "verdant_planet", Hills, d => d.y < -0.9f, null, chanceAt);

        [Test]
        public void A_uniform_per_point_density_equals_the_constant_planner()
        {
            float3 up = math.normalize(new float3(1, 1, 1));
            ScatterPlanner p = PerPoint(_ => 0.55f);
            Assert.That(Digest(p, up), Is.EqualTo(SnapshotDigest));
            Assert.That(Digest(p, up, true), Is.EqualTo(UnderstoryDigest));
        }

        [Test]
        public void Per_point_density_only_removes_or_keeps_canopy_items()
        {
            // Bare west, full east: the east must match a planner that is full everywhere, item for item.
            ScatterPlanner split = PerPoint(d => d.x > 0f ? 1f : 0f);
            var full = new ScatterPlanner(Planet, 12345, "verdant_planet", Hills, d => d.y < -0.9f, null, 1f);
            int kept = 0, removed = 0;
            for (long k = 0; k < 6L * full.CellsPerFace * full.CellsPerFace; k += 11)
            {
                bool inFull = full.TryGetItem(k, out ScatterItem f);
                bool inSplit = split.TryGetItem(k, out ScatterItem s);
                if (!inFull)
                {
                    Assert.That(inSplit, Is.False);
                    continue;
                }

                Assert.That(inSplit, Is.EqualTo(f.Direction.x > 0f));
                if (!inSplit)
                {
                    removed++;
                    continue;
                }

                kept++;
                Assert.That(s.Direction, Is.EqualTo(f.Direction));
                Assert.That(s.KindIndex, Is.EqualTo(f.KindIndex));
                Assert.That(s.SizeM, Is.EqualTo(f.SizeM));
                Assert.That(s.YawDeg, Is.EqualTo(f.YawDeg));
            }

            Assert.That(kept, Is.GreaterThan(100));
            Assert.That(removed, Is.GreaterThan(100));
        }

        [Test]
        public void Per_point_density_contract_violations_throw()
        {
            Assert.Throws<ContractException>(() =>
                new ScatterPlanner(Planet, 1, null, Hills, null, null, (System.Func<float3, float>)null));
            ScatterPlanner bad = PerPoint(_ => 1.5f);
            Assert.Throws<ContractException>(() =>
            {
                for (long k = 0; k < 50; k++) bad.TryGetItem(k, out _);
            });
        }

        private const ulong SnapshotDigest = 1236021966666354270ul;
        private const ulong UnderstoryDigest = 14843648030852674448ul;

        [Test]
        public void Understory_kinds_follow_their_altitude_band_and_keep_off_the_canopy()
        {
            // Swells of ±12 m around the 7.68 m highland line, gentle enough to pass the slope test.
            float Terraces(float3 d) => 12f * math.sin(d.x * 9f);
            foreach (string type in new[] { "verdant_planet", "storm_planet", "twilight_planet" })
            {
                var p = new ScatterPlanner(Planet, 777, type, Terraces, null, null);
                var seen = new HashSet<string>();
                var near = new List<ScatterItem>();
                int checkedForCanopy = 0;
                for (long k = 6L * p.CellsPerFace * p.CellsPerFace; k < p.KeyCount; k += 5)
                {
                    if (!p.TryGetItem(k, out ScatterItem it)) continue;
                    Assert.That(it.KindIndex, Is.GreaterThanOrEqualTo(p.CanopyKindCount));
                    ScatterKind kind = p.Kinds[it.KindIndex];
                    seen.Add(kind.Id);
                    bool highland = it.AltitudeM >= ScatterPlanner.HighlandFractionOfRadius * Planet.Radius;
                    Assert.That(kind.Habitat, Is.Not.EqualTo(highland ? ScatterHabitat.Lowland : ScatterHabitat.Highland));
                    Assert.That(it.SizeM, Is.InRange(kind.MinSizeM, kind.MaxSizeM));
                    bool soft = it.Shape == ScatterShape.TallGrass || it.Shape == ScatterShape.Fern ||
                                it.Shape == ScatterShape.Reed || it.Shape == ScatterShape.Flowers;
                    Assert.That(it.Blocks, Is.EqualTo(!soft));
                    Assert.That(it.VolumeL, Is.GreaterThanOrEqualTo(1));
                    if (checkedForCanopy++ % 20 != 0) continue;
                    near.Clear();
                    p.Query(it.Direction, 3f, near);
                    foreach (ScatterItem other in near)
                    {
                        if (p.IsUnderstoryKey(other.CellKey)) continue;
                        Assert.That(math.distance(it.Direction, other.Direction) * Planet.Radius,
                            Is.GreaterThan(it.BlockRadiusM + other.BlockRadiusM), "nothing grows out of a trunk or rock");
                    }
                }

                Assert.That(seen.Count, Is.GreaterThanOrEqualTo(2), type + ": more than one understory species");
            }

            // The verdant planet's own landform is forest: ferns everywhere, cane clumps on the highland.
            var forest = new ScatterPlanner(Planet, 777, "verdant_planet", Terraces, null, null);
            var ids = new HashSet<string>();
            for (long k = 6L * forest.CellsPerFace * forest.CellsPerFace; k < forest.KeyCount; k += 5)
            {
                if (forest.TryGetItem(k, out ScatterItem it)) ids.Add(forest.Kinds[it.KindIndex].Id);
            }

            Assert.That(ids, Is.SupersetOf(new[] { "fern", "shrub", "bamboo" }));
            Assert.That(ids, Has.No.Member("reed"), "reeds belong to river valleys and swamps");
        }

        [Test]
        public void Every_planner_uses_the_species_catalog_and_the_same_key_space()
        {
            foreach (string type in new[] { "verdant_planet", "lava_planet", "ice_crystal_planet", "storm_planet",
                         "mineral_planet", "twilight_planet", null, "no_such_planet" })
            {
                var p = new ScatterPlanner(Planet, 1, type, Hills, null, null);
                Assert.That(p.Kinds, Is.SameAs(ScatterBiomes.KindsFor(type)));
                Assert.That(p.Kinds.Count, Is.EqualTo(ScatterBiomes.Species.Count));
                Assert.That(p.CanopyKindCount, Is.EqualTo(ScatterBiomes.CanopySpeciesCount));
                Assert.That(p.KeyCount, Is.EqualTo(6L * p.CellsPerFace * p.CellsPerFace +
                                                    6L * p.UnderstoryCellsPerFace * p.UnderstoryCellsPerFace));
            }
        }

        private static Dictionary<string, int> CanopyCensus(ScatterPlanner p, System.Func<ScatterItem, bool> where = null)
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

        private static ScatterPlanner WithBiome(string landform, ulong seed = 12345) =>
            new ScatterPlanner(Planet, seed, "verdant_planet", Hills, null, null, _ => 0.7f,
                (d, roll) => ScatterBiomes.BiomeOf(landform));

        // REQ: planet-world.md#散佈 (Q-W15) — the landform under a point decides what grows there.
        [Test]
        public void Species_follow_the_landform_under_each_point()
        {
            int desert = ScatterBiomes.BiomeOf("desert"), forest = ScatterBiomes.BiomeOf("forest");
            var p = new ScatterPlanner(Planet, 12345, "verdant_planet", Hills, null, null, _ => 0.7f,
                (d, roll) => d.x > 0f ? forest : desert);
            Dictionary<string, int> east = CanopyCensus(p, it => it.Direction.x > 0.05f);
            Dictionary<string, int> west = CanopyCensus(p, it => it.Direction.x < -0.05f);
            Assert.That(east.ContainsKey("cactus"), Is.False);
            Assert.That(east["broadleaf"] + east["conifer"], Is.GreaterThan(200));
            Assert.That(west.ContainsKey("broadleaf") || west.ContainsKey("conifer"), Is.False, "no forest in the desert");
            Assert.That(west["cactus"], Is.GreaterThan(50));
            Assert.That(west["sandstone"], Is.GreaterThan(50));
        }

        [Test]
        public void Minerals_stones_and_cold_ground_species_appear_in_their_landforms()
        {
            Dictionary<string, int> canyon = CanopyCensus(WithBiome("canyon"));
            Assert.That(canyon.Keys, Is.SupersetOf(new[] { "sandstone", "stone_spire", "ore_copper", "ore_gold", "coal_seam", "gem_geode" }));
            Dictionary<string, int> cavern = CanopyCensus(WithBiome("cave"));
            Assert.That(cavern.Keys, Is.SupersetOf(new[] { "stone_spire", "limestone", "marble", "ore_silver", "gem_geode", "giant_mushroom" }));
            Dictionary<string, int> rift = CanopyCensus(WithBiome("rift_valley"));
            Assert.That(rift.Keys, Is.SupersetOf(new[] { "basalt", "basalt_column", "ore_iron", "oil_shale" }));
            Dictionary<string, int> ice = CanopyCensus(WithBiome("ice_field"));
            Assert.That(ice.Keys, Is.SupersetOf(new[] { "ice_spire", "moraine" }));
            Assert.That(ice.ContainsKey("broadleaf"), Is.False);
            Dictionary<string, int> swamp = CanopyCensus(WithBiome("swamp"));
            Assert.That(swamp.Keys, Is.SupersetOf(new[] { "swamp_tree", "log", "snag" }));
        }

        [Test]
        public void Forest_stands_decide_the_trees()
        {
            ScatterPlanner p = WithBiome("forest");
            int n = p.CellsPerFace;
            var perType = new Dictionary<ForestType, Dictionary<string, int>>();
            var plantationSizes = new List<float>();
            int sameAsNeighbour = 0, pairs = 0;
            for (long k = 0; k < 6L * n * n; k++)
            {
                ForestType type = p.ForestTypeOfCell(k);
                Assert.That(p.ForestTypeOfCell(k), Is.EqualTo(type));
                if (k % n < n - 1)
                {
                    pairs++;
                    if (p.ForestTypeOfCell(k + 1) == type) sameAsNeighbour++;
                }

                if (!p.TryGetItem(k, out ScatterItem it)) continue;
                ScatterKind kind = p.Kinds[it.KindIndex];
                if (kind.Shape != ScatterShape.Tree && kind.Shape != ScatterShape.Log) continue;
                if (!perType.TryGetValue(type, out Dictionary<string, int> census)) perType[type] = census = new Dictionary<string, int>();
                census[kind.Id] = census.TryGetValue(kind.Id, out int c) ? c + 1 : 1;
                float t = (it.SizeM - kind.MinSizeM) / (kind.MaxSizeM - kind.MinSizeM);
                if (type == ForestType.Plantation) plantationSizes.Add(t);
                if (type == ForestType.Secondary && (kind.Id == "broadleaf" || kind.Id == "conifer"))
                    Assert.That(t, Is.LessThanOrEqualTo(0.451f), "regrowth is young");
                // A tropical stand has no conifers (|y| < 0.3 is the tropical band; highland mixes them in only for 闊葉林).
                if (math.abs(it.Direction.y) < 0.25f && type != ForestType.Broadleaf && type != ForestType.Mixed &&
                    type != ForestType.Plantation && type != ForestType.Secondary)
                    Assert.Fail("tropical stands are never conifer forest");
            }

            Assert.That(sameAsNeighbour, Is.GreaterThan(pairs * 0.6), "cells group into stands");
            Assert.That(perType.Keys, Is.EquivalentTo(new[]
            {
                ForestType.Broadleaf, ForestType.Conifer, ForestType.Mixed, ForestType.Plantation, ForestType.Secondary,
            }));
            Assert.That(perType[ForestType.Conifer].ContainsKey("broadleaf"), Is.False);
            Assert.That(perType[ForestType.Mixed].Keys, Is.SupersetOf(new[] { "broadleaf", "conifer" }));
            Assert.That(perType[ForestType.Plantation].ContainsKey("snag"), Is.False, "a plantation has no deadwood");
            Assert.That(perType[ForestType.Plantation].ContainsKey("log"), Is.False);
            Assert.That(perType[ForestType.Secondary].Keys, Is.SupersetOf(new[] { "snag", "log" }));
            foreach (float t in plantationSizes) Assert.That(t, Is.InRange(0.449f, 0.601f), "one age class");
            Assert.That(plantationSizes.Count, Is.GreaterThan(100));
        }

        [Test]
        public void Biome_tables_pick_only_their_layer_and_forest_types_follow_the_climate()
        {
            for (int b = 0; b < ScatterBiomes.BiomeIds.Count; b++)
            {
                Assert.That(ScatterBiomes.BiomeOf(ScatterBiomes.BiomeIds[b]), Is.EqualTo(b));
                Assert.That(ScatterBiomes.UnderstoryChance(b), Is.GreaterThan(0f).And.LessThanOrEqualTo(ScatterBiomes.MaxUnderstoryChance));
                bool slot = false;
                for (uint roll = 0; roll < 400; roll++)
                {
                    int canopy = ScatterBiomes.PickCanopy(b, roll * 2654435761u);
                    slot |= canopy == ScatterBiomes.TreeSlot;
                    if (canopy != ScatterBiomes.TreeSlot) Assert.That(canopy, Is.InRange(0, ScatterBiomes.CanopySpeciesCount - 1));
                    foreach (bool highland in new[] { false, true })
                    {
                        int under = ScatterBiomes.PickUnderstory(b, roll * 2654435761u, highland);
                        Assert.That(under, Is.InRange(ScatterBiomes.CanopySpeciesCount, ScatterBiomes.Species.Count - 1));
                        Assert.That(ScatterBiomes.Species[under].Habitat,
                            Is.Not.EqualTo(highland ? ScatterHabitat.Lowland : ScatterHabitat.Highland));
                    }
                }

                Assert.That(slot, Is.EqualTo(ScatterBiomes.HasTrees(b)), ScatterBiomes.BiomeIds[b]);
            }

            var seen = new HashSet<ForestType>();
            for (uint h = 0; h < 100; h++)
            {
                Assert.That(ScatterBiomes.ForestTypeFor(h, ForestClimate.Tropical), Is.Not.EqualTo(ForestType.Conifer));
                seen.Add(ScatterBiomes.ForestTypeFor(h, ForestClimate.Temperate));
                int tree = ScatterBiomes.PickTree(ForestType.Plantation, false, 0x100u, h, out float lo, out float hi);
                Assert.That(ScatterBiomes.Species[tree].Id, Is.EqualTo("broadleaf"), "one species per plantation stand");
                Assert.That(lo, Is.LessThan(hi));
                Assert.That(ScatterBiomes.DensityBoost((ForestType)(h % 5)), Is.InRange(1f, ScatterBiomes.MaxDensityBoost));
            }

            Assert.That(seen.Count, Is.EqualTo(5), "every forest type occurs in the temperate band");
            Assert.That(ScatterBiomes.BiomeOf("volcano"), Is.EqualTo(ScatterBiomes.BiomeOf("volcano_foothills")));
            Assert.That(ScatterBiomes.BiomeOf(null), Is.EqualTo(ScatterBiomes.BiomeOf("grassland")));
            Assert.That(ScatterBiomes.Species[ScatterBiomes.IndexOf("ore_gold")].SubstanceId, Is.EqualTo("metal"));
        }

        [Test]
        public void Items_respect_sea_clearance_and_kind_table()
        {
            ScatterPlanner p = Planner(isClear: d => d.x > 0f);
            int found = 0;
            for (long k = 0; k < 6L * p.CellsPerFace * p.CellsPerFace; k += 37)
            {
                if (!p.TryGetItem(k, out ScatterItem it)) continue;
                found++;
                Assert.That(it.Direction.x, Is.GreaterThan(0f));
                Assert.That(it.Direction.y, Is.GreaterThanOrEqualTo(-0.9f));
                ScatterKind kind = p.Kinds[it.KindIndex];
                Assert.That(it.Shape, Is.EqualTo(kind.Shape));
                Assert.That(it.SubstanceId, Is.EqualTo(kind.SubstanceId));
                Assert.That(it.SizeM, Is.InRange(kind.MinSizeM, kind.MaxSizeM));
                Assert.That(it.BlockRadiusM, Is.GreaterThan(0f).And.LessThanOrEqualTo(p.MaxBlockRadiusM));
                Assert.That(it.VolumeL, Is.GreaterThanOrEqualTo(1));
            }

            Assert.That(found, Is.GreaterThan(100));
        }

        [Test]
        public void Query_returns_items_within_radius_sorted_by_key()
        {
            ScatterPlanner p = Planner();
            float3 up = math.normalize(new float3(0.3f, 1f, -0.2f));
            var near = new List<ScatterItem>();
            p.Query(up * 5f, 40f, near);
            Assert.That(near.Count, Is.GreaterThan(0));
            for (int i = 0; i < near.Count; i++)
            {
                Assert.That(math.distance(up, near[i].Direction) * Planet.Radius, Is.LessThanOrEqualTo(40f));
                if (i > 0) Assert.That(near[i].CellKey, Is.GreaterThan(near[i - 1].CellKey));
            }

            // Exhaustive oracle: every item within 40 m is found.
            var cells = new HashSet<long>();
            p.CollectCells(up, 60f, cells);
            int expected = 0;
            foreach (long k in cells)
            {
                if (p.TryGetItem(k, out ScatterItem it) && math.distance(up, it.Direction) * Planet.Radius <= 40f) expected++;
            }

            Assert.That(near.Count, Is.EqualTo(expected));
        }

        [Test]
        public void Ice_planet_crystals_are_ice_and_unknown_types_fall_back()
        {
            int crystal = ScatterBiomes.IndexOf("crystal");
            Assert.That(ScatterBiomes.KindsFor("ice_crystal_planet")[crystal].SubstanceId, Is.EqualTo("ice"));
            Assert.That(ScatterBiomes.KindsFor("verdant_planet")[crystal].SubstanceId, Is.EqualTo("crystal"));
            Assert.That(ScatterBiomes.KindsFor("no_such_planet"), Is.SameAs(ScatterBiomes.KindsFor("verdant_planet")));
            Assert.That(ScatterBiomes.KindsFor(null), Is.SameAs(ScatterBiomes.KindsFor("verdant_planet")));
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => new ScatterPlanner(default, 1, null, Hills, null, null));
            Assert.Throws<ContractException>(() => new ScatterPlanner(Planet, 1, null, null, null, null));
            ScatterPlanner p = Planner();
            Assert.Throws<ContractException>(() => p.TryGetItem(-1, out _));
            Assert.Throws<ContractException>(() => p.TryGetItem(p.KeyCount, out _));
            Assert.Throws<ContractException>(() => p.CollectCells(new float3(0, 1, 0), 1f, null));
            Assert.Throws<ContractException>(() => p.CollectCells(new float3(0, 1, 0), -1f, new HashSet<long>()));
            Assert.Throws<ContractException>(() => p.Query(float3.zero, 1f, new List<ScatterItem>()));
            Assert.Throws<ContractException>(() => p.Query(new float3(0, 1, 0), 1f, null));
            Assert.Throws<ContractException>(() => new ScatterKind(ScatterShape.Rock, 2f, 1f, 1, "stone"));
            Assert.Throws<ContractException>(() => new ScatterKind(ScatterShape.Rock, 1f, 2f, 0, "stone"));
            Assert.Throws<ContractException>(() => new ScatterKind(ScatterShape.Rock, 1f, 2f, 1, ""));
            Assert.Throws<ContractException>(() => new ScatterKind("", ScatterShape.Rock, 1f, 2f, "stone"));
            Assert.Throws<ContractException>(() => new ScatterPlanner(Planet, 1, null, Hills, null, null, _ => 0.5f, null));
            Assert.Throws<ContractException>(() => p.ForestTypeOfCell(6L * p.CellsPerFace * p.CellsPerFace));
            Assert.Throws<ContractException>(() => ScatterBiomes.IndexOf("no_such_species"));
            Assert.Throws<ContractException>(() => ScatterBiomes.PickCanopy(-1, 0u));
            Assert.Throws<ContractException>(() => ScatterBiomes.PickUnderstory(ScatterBiomes.BiomeIds.Count, 0u, false));
            Assert.Throws<ContractException>(() => ScatterBiomes.HasTrees(-1));
            Assert.Throws<ContractException>(() => ScatterBiomes.UnderstoryChance(-1));
            var badBiome = new ScatterPlanner(Planet, 1, null, Hills, null, null, _ => 1f, (d, roll) => 999);
            Assert.Throws<ContractException>(() =>
            {
                for (long k = 0; k < 50; k++) badBiome.TryGetItem(k, out _);
            });
        }
    }
}
