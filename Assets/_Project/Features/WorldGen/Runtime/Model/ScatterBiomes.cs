using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.WorldGen
{
    /// <summary>What kind of woodland a forest stand is; decides which trees fill a landform's tree slot.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ForestType
    {
        /// <summary>闊葉林: broadleaf trees with a little deadwood.</summary>
        Broadleaf = 0,

        /// <summary>針葉林: conifers.</summary>
        Conifer = 1,

        /// <summary>混合林: broadleaf and conifer side by side.</summary>
        Mixed = 2,

        /// <summary>人工林: one species, one age, planted in rows, denser than natural woodland.</summary>
        Plantation = 3,

        /// <summary>次生林: young regrowth among snags and fallen logs.</summary>
        Secondary = 4,
    }

    /// <summary>Latitude band a forest stand grows in.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ForestClimate
    {
        Tropical = 0,
        Temperate = 1,
        Cold = 2,
    }

    /// <summary>
    /// The scatter species catalog and the "landform → species" tables.
    /// REQ: planet-world.md#散佈 (Q-W15: 地貌為主，星球類型只換配色與物質; 下層密度也依地貌).
    /// </summary>
    /// <remarks>
    /// One catalog for every planet, canopy species first, so a species index means the same thing everywhere; a
    /// planet type only swaps a species' substance (<see cref="KindsFor"/>) and, in the presentation, its colours.
    /// Each landform (biome) weights catalog species; its canopy table may hold a "tree slot" that the stand's
    /// <see cref="ForestType"/> resolves into an actual tree, snag or log.
    /// Pre: none. Post: pure and deterministic; integer picks only. Side effects: none after type initialisation.
    /// </remarks>
    public static class ScatterBiomes
    {
        /// <summary>Returned by <see cref="PickCanopy"/> when the landform's tree slot was picked.</summary>
        /// <remarks>Pre: none. Post: constant, never a valid species index. Side effects: none.</remarks>
        public const int TreeSlot = -1;

        private const string DefaultType = "verdant_planet";
        private const string DefaultBiome = "grassland";

        // Size ranges in metres. Substances per destructibles.md#物質: ores are metal, coal burns down like charcoal,
        // oil shale is kerosene (爆燃 when lit), geodes and crystals are crystal.
        private static readonly ScatterKind[] Catalog =
        {
            // ---- canopy ----
            new ScatterKind("broadleaf", ScatterShape.Tree, 3.5f, 7f, "wood"),
            new ScatterKind("conifer", ScatterShape.Tree, 4f, 8f, "wood"),
            new ScatterKind("umbrella", ScatterShape.Tree, 3.5f, 7f, "wood"),
            new ScatterKind("swamp_tree", ScatterShape.Tree, 4f, 7.5f, "wood"),
            new ScatterKind("snag", ScatterShape.Tree, 3f, 6f, "wood"),
            new ScatterKind("log", ScatterShape.Log, 2f, 4.5f, "wood"),
            new ScatterKind("cactus", ScatterShape.Cactus, 1.5f, 4f, "sap"),
            new ScatterKind("giant_mushroom", ScatterShape.Mushroom, 2f, 5f, "plant"),
            new ScatterKind("granite", ScatterShape.Rock, 0.8f, 2.4f, "stone"),
            new ScatterKind("limestone", ScatterShape.Rock, 0.8f, 2.2f, "stone"),
            new ScatterKind("marble", ScatterShape.Rock, 0.8f, 2f, "stone"),
            new ScatterKind("slate", ScatterShape.Rock, 0.8f, 2.2f, "stone"),
            new ScatterKind("sandstone", ScatterShape.Rock, 1f, 2.8f, "stone"),
            new ScatterKind("basalt", ScatterShape.Rock, 1f, 3f, "stone"),
            new ScatterKind("reef_rock", ScatterShape.Rock, 1f, 2.6f, "stone"),
            new ScatterKind("moraine", ScatterShape.Rock, 1.5f, 3.6f, "stone"),
            new ScatterKind("basalt_column", ScatterShape.Spire, 2f, 5f, "stone"),
            new ScatterKind("stone_spire", ScatterShape.Spire, 2f, 6f, "stone"),
            new ScatterKind("ice_spire", ScatterShape.Spire, 2f, 6f, "ice"),
            new ScatterKind("crystal", ScatterShape.Crystal, 1.5f, 5f, "crystal"),
            new ScatterKind("ore_gold", ScatterShape.OreVein, 0.8f, 1.8f, "metal"),
            new ScatterKind("ore_silver", ScatterShape.OreVein, 0.8f, 2f, "metal"),
            new ScatterKind("ore_copper", ScatterShape.OreVein, 0.8f, 2.2f, "metal"),
            new ScatterKind("ore_iron", ScatterShape.OreVein, 0.8f, 2.2f, "metal"),
            new ScatterKind("coal_seam", ScatterShape.OreVein, 1f, 2.4f, "charcoal"),
            new ScatterKind("oil_shale", ScatterShape.OreVein, 1f, 2.2f, "kerosene"),
            new ScatterKind("gem_geode", ScatterShape.OreVein, 0.7f, 1.6f, "crystal"),
            // Only bedrock puts it there (ScatterGeology, planet-world.md#地質變體): no landform table lists it.
            new ScatterKind("granite_tor", ScatterShape.Rock, 2.6f, 5f, "stone"),

            // ---- understory ----
            new ScatterKind("shrub", ScatterShape.Shrub, 0.8f, 1.8f, "plant"),
            new ScatterKind("dwarf_shrub", ScatterShape.Shrub, 0.4f, 0.9f, "plant"),
            new ScatterKind("bamboo", ScatterShape.Bamboo, 1.5f, 3f, "wood", ScatterHabitat.Highland),
            new ScatterKind("tall_grass", ScatterShape.TallGrass, 1.2f, 2.4f, "plant"),
            new ScatterKind("fern", ScatterShape.Fern, 0.7f, 1.4f, "plant"),
            new ScatterKind("reed", ScatterShape.Reed, 1.2f, 2.2f, "plant", ScatterHabitat.Lowland),
            new ScatterKind("flowers", ScatterShape.Flowers, 0.6f, 1.3f, "plant"),
        };

        private const int CanopyCount = 28;

        private static readonly Dictionary<string, int> SpeciesIndex = BuildSpeciesIndex();

        private readonly struct Entry
        {
            public readonly int Species;
            public readonly int Weight;

            public Entry(int species, int weight)
            {
                Species = species;
                Weight = weight;
            }
        }

        private sealed class Biome
        {
            public string Id;
            public Entry[] Canopy;
            public int CanopyTotal;
            public bool HasTrees;
            public float UnderChance;
            public Entry[] Under;
        }

        // REQ: planet-world.md#散佈 (地貌 → 物種表; 數值暫定). "tree" is the slot a stand's ForestType fills.
        // Minerals follow where they form: placer gold in river valleys, copper and coal in canyon strata, iron and
        // oil along rifts, silver and gems in karst and high mountains. Cold ground gets ice spires and moraine.
        // Every understory table keeps at least one ScatterHabitat.Any species, so every altitude has something to pick.
        private static readonly Biome[] Biomes =
        {
            B("grassland", 0.45f,
                C(("tree", 2), ("umbrella", 1), ("granite", 2), ("limestone", 1)),
                C(("tall_grass", 6), ("flowers", 4), ("shrub", 2))),
            B("ice_field", 0.15f,
                C(("ice_spire", 5), ("moraine", 3), ("granite", 2), ("crystal", 1)),
                C(("dwarf_shrub", 3), ("tall_grass", 1))),
            B("desert", 0.12f,
                C(("cactus", 5), ("sandstone", 4), ("snag", 1), ("granite", 1)),
                C(("dwarf_shrub", 2), ("tall_grass", 1))),
            B("river_valley", 0.5f,
                C(("tree", 6), ("granite", 2), ("log", 1), ("limestone", 1), ("ore_gold", 1)),
                C(("reed", 5), ("fern", 3), ("shrub", 3), ("flowers", 2), ("bamboo", 4))),
            B("plateau", 0.35f,
                C(("umbrella", 3), ("tree", 1), ("sandstone", 3), ("slate", 2), ("ore_iron", 1), ("ore_copper", 1)),
                C(("tall_grass", 5), ("shrub", 3), ("flowers", 1), ("bamboo", 3))),
            B("swamp", 0.6f,
                C(("swamp_tree", 6), ("log", 2), ("snag", 2), ("giant_mushroom", 1)),
                C(("reed", 6), ("fern", 4), ("shrub", 2))),
            B("archipelago", 0.35f,
                C(("reef_rock", 4), ("umbrella", 3), ("tree", 2), ("granite", 1)),
                C(("tall_grass", 4), ("shrub", 3), ("flowers", 2), ("fern", 1))),
            B("fjord", 0.3f,
                C(("tree", 4), ("slate", 3), ("moraine", 2), ("reef_rock", 2), ("ore_silver", 1)),
                C(("fern", 3), ("dwarf_shrub", 3), ("tall_grass", 1))),
            B("forest", 0.6f,
                C(("tree", 9), ("granite", 1), ("log", 1), ("giant_mushroom", 1)),
                C(("fern", 5), ("shrub", 5), ("flowers", 1), ("tall_grass", 1), ("bamboo", 5))),
            B("mountain", 0.3f,
                C(("tree", 3), ("granite", 4), ("slate", 2), ("moraine", 1), ("ore_iron", 1), ("ore_silver", 1),
                    ("ore_copper", 1), ("gem_geode", 1)),
                C(("dwarf_shrub", 3), ("tall_grass", 2), ("bamboo", 5))),
            B("canyon", 0.2f,
                C(("sandstone", 4), ("stone_spire", 2), ("cactus", 2), ("snag", 1), ("ore_copper", 2), ("ore_gold", 1),
                    ("coal_seam", 1), ("gem_geode", 1)),
                C(("shrub", 3), ("tall_grass", 2))),
            B("rift_valley", 0.3f,
                C(("basalt", 4), ("basalt_column", 2), ("umbrella", 2), ("ore_iron", 1), ("coal_seam", 1),
                    ("oil_shale", 1), ("gem_geode", 1)),
                C(("tall_grass", 4), ("shrub", 3))),
            B("volcano_foothills", 0.2f,
                C(("basalt", 5), ("basalt_column", 3), ("snag", 2), ("crystal", 1), ("ore_iron", 1), ("coal_seam", 1)),
                C(("tall_grass", 5), ("shrub", 2), ("fern", 1))),
            B("cavern_outer", 0.3f,
                C(("stone_spire", 5), ("limestone", 3), ("giant_mushroom", 2), ("marble", 1), ("crystal", 1),
                    ("ore_silver", 1), ("ore_copper", 1), ("ore_gold", 1), ("gem_geode", 2), ("coal_seam", 1)),
                C(("fern", 5), ("shrub", 2), ("flowers", 1))),
            B("eternal_day", 0.4f,
                C(("umbrella", 3), ("tree", 2), ("reef_rock", 2), ("marble", 2), ("crystal", 1)),
                C(("flowers", 4), ("tall_grass", 3), ("shrub", 3))),
            B("eternal_night", 0.3f,
                C(("giant_mushroom", 3), ("tree", 2), ("moraine", 2), ("ice_spire", 2), ("crystal", 2), ("slate", 1)),
                C(("fern", 3), ("shrub", 3), ("dwarf_shrub", 1))),
        };

        private static readonly Dictionary<string, int> BiomeIndex = BuildBiomeIndex();
        private static readonly string[] BiomeIdList = BuildBiomeIds();
        private static readonly Dictionary<string, ScatterKind[]> KindsByType = new Dictionary<string, ScatterKind[]>();

        // Share of each ForestType per climate, in the enum's order (闊葉, 針葉, 混合, 人工, 次生), out of 100.
        private static readonly int[][] ClimateWeights =
        {
            new[] { 50, 0, 10, 15, 25 },
            new[] { 30, 12, 28, 12, 18 },
            new[] { 6, 54, 24, 8, 8 },
        };

        /// <summary>Largest factor <see cref="DensityBoost"/> returns.</summary>
        /// <remarks>Pre: none. Post: constant ≥ 1. Side effects: none.</remarks>
        public const float MaxDensityBoost = 1.5f;

        /// <summary>Largest value <see cref="UnderstoryChance"/> returns.</summary>
        /// <remarks>Pre: none. Post: constant in (0, 1]. Side effects: none.</remarks>
        public const float MaxUnderstoryChance = 0.6f;

        /// <summary>Every scatter species, canopy species first.</summary>
        /// <remarks>Pre: none. Post: non-empty; ids distinct; same instance every call. Side effects: none.</remarks>
        public static IReadOnlyList<ScatterKind> Species => Catalog;

        /// <summary>Number of canopy species at the front of <see cref="Species"/>.</summary>
        /// <remarks>Pre: none. Post: 1 ≤ value &lt; Species.Count. Side effects: none.</remarks>
        public static int CanopySpeciesCount => CanopyCount;

        /// <summary>Landform ids that have a species table, indexed by biome.</summary>
        /// <remarks>Pre: none. Post: the general landforms of <see cref="TerrainProfiles"/>; distinct. Side effects: none.</remarks>
        public static IReadOnlyList<string> BiomeIds => BiomeIdList;

        /// <summary>Catalog index of species <paramref name="speciesId"/>.</summary>
        /// <remarks>Pre: speciesId is a catalog id. Post: valid index into <see cref="Species"/>. Side effects: none.</remarks>
        public static int IndexOf(string speciesId)
        {
            Contract.Requires(speciesId != null && SpeciesIndex.ContainsKey(speciesId), "unknown scatter species");
            return SpeciesIndex[speciesId];
        }

        /// <summary>Biome index of landform <paramref name="landformId"/> (grassland for null or unknown ids).</summary>
        /// <remarks>
        /// Pre: none. Post: valid index into <see cref="BiomeIds"/>; `volcano` and `cave` (advanced dungeons) answer
        /// as the open landform around them (planet-world.md#1-骨架層程序化地貌). Side effects: none.
        /// </remarks>
        public static int BiomeOf(string landformId)
        {
            string id = landformId switch
            {
                "volcano" => "volcano_foothills",
                "cave" => "cavern_outer",
                _ => landformId,
            };
            return id != null && BiomeIndex.TryGetValue(id, out int b) ? b : BiomeIndex[DefaultBiome];
        }

        /// <summary>The catalog as planet type <paramref name="planetTypeId"/> builds it: same species, its substances.</summary>
        /// <remarks>
        /// Pre: none (null or unknown ids answer as verdant_planet).
        /// Post: same length, order, shapes and sizes as <see cref="Species"/>; only substances differ (the ice planet's
        ///       crystals are ice); the same array instance for the same id.
        /// Side effects: caches the array.
        /// </remarks>
        public static ScatterKind[] KindsFor(string planetTypeId)
        {
            string type = planetTypeId ?? DefaultType;
            lock (KindsByType)
            {
                if (KindsByType.TryGetValue(type, out ScatterKind[] kinds))
                {
                    return kinds;
                }

                if (type != "ice_crystal_planet")
                {
                    if (!KindsByType.TryGetValue(DefaultType, out kinds))
                    {
                        kinds = (ScatterKind[])Catalog.Clone();
                        KindsByType[DefaultType] = kinds;
                    }

                    return kinds;
                }

                kinds = (ScatterKind[])Catalog.Clone();
                int crystal = SpeciesIndex["crystal"];
                kinds[crystal] = kinds[crystal].WithSubstance("ice");
                KindsByType[type] = kinds;
                return kinds;
            }
        }

        /// <summary>Whether biome <paramref name="biome"/>'s canopy table has a tree slot.</summary>
        /// <remarks>Pre: valid biome index. Post: true iff <see cref="PickCanopy"/> can return <see cref="TreeSlot"/>. Side effects: none.</remarks>
        public static bool HasTrees(int biome)
        {
            Contract.Requires(biome >= 0 && biome < Biomes.Length, "biome out of range");
            return Biomes[biome].HasTrees;
        }

        /// <summary>Understory placement probability of biome <paramref name="biome"/>.</summary>
        /// <remarks>Pre: valid biome index. Post: in (0, <see cref="MaxUnderstoryChance"/>]. Side effects: none.</remarks>
        public static float UnderstoryChance(int biome)
        {
            Contract.Requires(biome >= 0 && biome < Biomes.Length, "biome out of range");
            return Biomes[biome].UnderChance;
        }

        /// <summary>Picks a canopy species of <paramref name="biome"/> from <paramref name="roll"/>.</summary>
        /// <remarks>
        /// Pre: valid biome index.
        /// Post: a canopy species index, or <see cref="TreeSlot"/> (only when <see cref="HasTrees"/>); each entry is
        ///       picked in proportion to its weight over uniformly distributed rolls.
        /// Side effects: none.
        /// </remarks>
        public static int PickCanopy(int biome, uint roll)
        {
            Contract.Requires(biome >= 0 && biome < Biomes.Length, "biome out of range");
            Biome b = Biomes[biome];
            int pick = (int)(roll % (uint)b.CanopyTotal);
            foreach (Entry e in b.Canopy)
            {
                pick -= e.Weight;
                if (pick < 0)
                {
                    return e.Species;
                }
            }

            return b.Canopy[b.Canopy.Length - 1].Species;
        }

        /// <summary>Picks an understory species of <paramref name="biome"/> that grows in the given altitude band.</summary>
        /// <remarks>
        /// Pre: valid biome index.
        /// Post: an understory species index whose habitat is Any or matches <paramref name="highland"/>.
        /// Side effects: none.
        /// </remarks>
        public static int PickUnderstory(int biome, uint roll, bool highland)
        {
            Contract.Requires(biome >= 0 && biome < Biomes.Length, "biome out of range");
            Entry[] under = Biomes[biome].Under;
            ScatterHabitat excluded = highland ? ScatterHabitat.Lowland : ScatterHabitat.Highland;
            int total = 0;
            foreach (Entry e in under)
            {
                total += Catalog[e.Species].Habitat == excluded ? 0 : e.Weight;
            }

            int pick = (int)(roll % (uint)total);
            foreach (Entry e in under)
            {
                if (Catalog[e.Species].Habitat == excluded)
                {
                    continue;
                }

                pick -= e.Weight;
                if (pick < 0)
                {
                    return e.Species;
                }
            }

            return under[0].Species;
        }

        /// <summary>The forest type of the stand with hash <paramref name="standHash"/> in <paramref name="climate"/>.</summary>
        /// <remarks>
        /// Pre: climate defined.
        /// Post: tropical stands are never conifer; cold stands are mostly conifer; every type occurs in temperate
        ///       stands. The same arguments give the same type.
        /// Side effects: none.
        /// </remarks>
        public static ForestType ForestTypeFor(uint standHash, ForestClimate climate)
        {
            Contract.Requires(climate >= ForestClimate.Tropical && climate <= ForestClimate.Cold, "climate must be defined");
            int[] weights = ClimateWeights[(int)climate];
            int pick = (int)(standHash % 100u);
            for (int t = 0; t < weights.Length; t++)
            {
                pick -= weights[t];
                if (pick < 0)
                {
                    return (ForestType)t;
                }
            }

            return ForestType.Mixed;
        }

        /// <summary>How much denser than its landform a stand of <paramref name="type"/> stands.</summary>
        /// <remarks>Pre: none. Post: in [1, <see cref="MaxDensityBoost"/>]; &gt; 1 only for plantation and secondary. Side effects: none.</remarks>
        public static float DensityBoost(ForestType type) => type switch
        {
            ForestType.Plantation => MaxDensityBoost,
            ForestType.Secondary => 1.25f,
            _ => 1f,
        };

        /// <summary>Resolves a tree slot: the tree, snag or log a stand of <paramref name="type"/> grows there.</summary>
        /// <remarks>
        /// Pre: type defined.
        /// Post: a canopy species index (broadleaf, conifer, snag or log); [sizeLo, sizeHi] ⊂ [0, 1] is the part of the
        ///       species' size range the stand uses (plantation: one age; secondary: young growth). On highland a
        ///       broadleaf stand answers as mixed, so conifers climb the hills. A plantation is one species per stand.
        /// Side effects: none.
        /// </remarks>
        public static int PickTree(ForestType type, bool highland, uint standHash, uint roll, out float sizeLo, out float sizeHi)
        {
            Contract.Requires(type >= ForestType.Broadleaf && type <= ForestType.Secondary, "type must be defined");
            sizeLo = 0f;
            sizeHi = 1f;
            int broadleaf = SpeciesIndex["broadleaf"];
            int conifer = SpeciesIndex["conifer"];
            int snag = SpeciesIndex["snag"];
            int log = SpeciesIndex["log"];
            int pick = (int)(roll % 100u);
            if (type == ForestType.Broadleaf && highland)
            {
                type = ForestType.Mixed;
            }

            switch (type)
            {
                case ForestType.Broadleaf:
                    return pick < 84 ? broadleaf : pick < 90 ? snag : log;
                case ForestType.Conifer:
                    return pick < 88 ? conifer : pick < 94 ? snag : log;
                case ForestType.Mixed:
                    return pick < 44 ? broadleaf : pick < 88 ? conifer : pick < 93 ? snag : log;
                case ForestType.Plantation:
                    sizeLo = 0.45f;
                    sizeHi = 0.6f;
                    // Bit 8: the low bits already chose the forest type (standHash % 100).
                    return ((standHash >> 8) & 1u) == 0u ? conifer : broadleaf;
                default:
                    if (pick < 74)
                    {
                        sizeHi = 0.45f;
                        return pick < 62 ? broadleaf : conifer;
                    }

                    return pick < 86 ? snag : log;
            }
        }

        private static Dictionary<string, int> BuildSpeciesIndex()
        {
            var index = new Dictionary<string, int>(Catalog.Length);
            for (int i = 0; i < Catalog.Length; i++)
            {
                index.Add(Catalog[i].Id, i);
            }

            return index;
        }

        private static Dictionary<string, int> BuildBiomeIndex()
        {
            var index = new Dictionary<string, int>(Biomes.Length);
            for (int i = 0; i < Biomes.Length; i++)
            {
                index.Add(Biomes[i].Id, i);
            }

            return index;
        }

        private static string[] BuildBiomeIds()
        {
            var ids = new string[Biomes.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = Biomes[i].Id;
            }

            return ids;
        }

        private static (string, int)[] C(params (string, int)[] entries) => entries;

        // Static field initialisers run in textual order: Catalog exists, SpeciesIndex may not yet, so look ids up by scan.
        private static Biome B(string id, float underChance, (string id, int weight)[] canopy, (string id, int weight)[] under)
        {
            var biome = new Biome
            {
                Id = id,
                UnderChance = underChance,
                Canopy = new Entry[canopy.Length],
                Under = new Entry[under.Length],
            };
            for (int i = 0; i < canopy.Length; i++)
            {
                bool slot = canopy[i].id == "tree";
                int species = slot ? TreeSlot : Find(canopy[i].id);
                Contract.Requires(slot || species < CanopyCount, id + ": " + canopy[i].id + " is not a canopy species");
                biome.Canopy[i] = new Entry(species, canopy[i].weight);
                biome.CanopyTotal += canopy[i].weight;
                biome.HasTrees |= slot;
            }

            for (int i = 0; i < under.Length; i++)
            {
                int species = Find(under[i].id);
                Contract.Requires(species >= CanopyCount, id + ": " + under[i].id + " is not an understory species");
                biome.Under[i] = new Entry(species, under[i].weight);
            }

            return biome;
        }

        private static int Find(string speciesId)
        {
            for (int i = 0; i < Catalog.Length; i++)
            {
                if (Catalog[i].Id == speciesId)
                {
                    return i;
                }
            }

            Contract.Requires(false, "unknown scatter species " + speciesId);
            return 0;
        }
    }
}
