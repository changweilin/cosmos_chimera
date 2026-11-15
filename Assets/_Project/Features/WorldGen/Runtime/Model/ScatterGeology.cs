using Cosmimera.Core.Contracts;

namespace Cosmimera.WorldGen
{
    /// <summary>Bedrock of a landform stamp; decides which country rock its scatter tables grow and the ground's rock colour.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum Geology
    {
        /// <summary>The landform's own rocks, as its ScatterBiomes table lists them.</summary>
        Native = 0,

        /// <summary>石灰岩喀斯特: limestone, stone-forest spires, a little marble.</summary>
        Karst = 1,

        /// <summary>玄武岩: basalt blocks and columnar basalt.</summary>
        Basalt = 2,

        /// <summary>花崗岩圓丘: granite boulders and large rounded tors.</summary>
        Granite = 3,

        /// <summary>板岩: dark slate.</summary>
        Slate = 4,
    }

    /// <summary>
    /// Geology variants, stage (a): the bedrock under a landform swaps its country rock and ground colour; the terrain
    /// shape is untouched. REQ: planet-world.md#地質變體 (P3-24).
    /// </summary>
    /// <remarks>
    /// Only "country rock" (the plain stone a landform table scatters: granite, limestone, marble, slate, sandstone,
    /// basalt) is swapped. Trees, minerals, spires and habitat rocks (reef rock, moraine) stay the landform's own, so a
    /// landform remains recognisable under any bedrock.
    /// Pre: none. Post: pure and deterministic; integer picks only. Side effects: none after type initialisation.
    /// </remarks>
    public static class ScatterGeology
    {
        /// <summary>Number of <see cref="Geology"/> values.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int Count = 5;

        // Weight of each Geology (enum order) per landform; 數值暫定. A landform not listed keeps its own rocks:
        // its lithology is what makes it that landform (sandstone canyon, basalt rift, karst cavern, reef archipelago).
        private static readonly (string id, int[] weights)[] Table =
        {
            ("grassland", new[] { 4, 2, 1, 2, 1 }),
            ("river_valley", new[] { 4, 2, 1, 2, 1 }),
            ("forest", new[] { 4, 2, 1, 2, 1 }),
            ("plateau", new[] { 3, 1, 2, 1, 3 }),
            // No karst on a fjord coast: glacial troughs cut hard rock.
            ("fjord", new[] { 3, 0, 1, 3, 3 }),
            ("mountain", new[] { 2, 1, 1, 3, 3 }),
            ("eternal_day", new[] { 4, 1, 1, 1, 1 }),
            ("eternal_night", new[] { 4, 1, 1, 1, 1 }),
            // Glaciers cut hard rock, like the fjords they leave behind.
            ("cirque", new[] { 2, 0, 0, 3, 3 }),
            ("moraine", new[] { 3, 0, 1, 3, 2 }),
            // P3-24 (b): a bedrock landform is its bedrock.
            ("karst", new[] { 0, 1, 0, 0, 0 }),
            ("basalt_terrace", new[] { 0, 0, 1, 0, 0 }),
            ("granite_domes", new[] { 0, 0, 0, 1, 0 }),
            ("slate_cliffs", new[] { 0, 0, 0, 0, 1 }),
        };

        private static readonly int[][] WeightsByBiome = BuildWeights();
        private static readonly bool[] CountryRock = BuildCountryRock();

        private static readonly int Limestone = ScatterBiomes.IndexOf("limestone");
        private static readonly int Marble = ScatterBiomes.IndexOf("marble");
        private static readonly int StoneSpire = ScatterBiomes.IndexOf("stone_spire");
        private static readonly int BasaltRock = ScatterBiomes.IndexOf("basalt");
        private static readonly int BasaltColumn = ScatterBiomes.IndexOf("basalt_column");
        private static readonly int GraniteRock = ScatterBiomes.IndexOf("granite");
        private static readonly int GraniteTor = ScatterBiomes.IndexOf("granite_tor");
        private static readonly int SlateRock = ScatterBiomes.IndexOf("slate");

        /// <summary>Whether landform <paramref name="biome"/> can have a bedrock other than its own.</summary>
        /// <remarks>
        /// Pre: valid ScatterBiomes biome index.
        /// Post: false iff <see cref="For"/> always answers Native (a bedrock landform always answers its one bedrock,
        ///       and is true here).
        /// Side effects: none.
        /// </remarks>
        public static bool Varies(int biome)
        {
            Contract.Requires(biome >= 0 && biome < WeightsByBiome.Length, "biome out of range");
            return WeightsByBiome[biome] != null;
        }

        /// <summary>The bedrock of a stamp of landform <paramref name="biome"/> with hash <paramref name="hash"/>.</summary>
        /// <remarks>
        /// Pre: valid ScatterBiomes biome index.
        /// Post: Native when !<see cref="Varies"/>; otherwise each geology in proportion to the landform's weights over
        ///       uniformly distributed hashes. The same arguments give the same geology.
        /// Side effects: none.
        /// </remarks>
        public static Geology For(int biome, uint hash)
        {
            Contract.Requires(biome >= 0 && biome < WeightsByBiome.Length, "biome out of range");
            int[] weights = WeightsByBiome[biome];
            if (weights == null)
            {
                return Geology.Native;
            }

            int total = 0;
            foreach (int w in weights)
            {
                total += w;
            }

            int pick = (int)(hash % (uint)total);
            for (int g = 0; g < weights.Length; g++)
            {
                pick -= weights[g];
                if (pick < 0)
                {
                    return (Geology)g;
                }
            }

            return Geology.Native;
        }

        /// <summary>The canopy species that stands where a landform table picked <paramref name="species"/>, on bedrock <paramref name="geology"/>.</summary>
        /// <remarks>
        /// Pre: species is a canopy species index; geology defined.
        /// Post: species itself when geology is Native or species is not country rock; otherwise one of the geology's
        ///       rocks (karst: limestone, stone spire, marble; basalt: basalt, basalt column; granite: granite, granite
        ///       tor; slate: slate), chosen by roll. Always a canopy species index.
        /// Side effects: none.
        /// </remarks>
        public static int Substitute(int species, Geology geology, uint roll)
        {
            Contract.Requires(species >= 0 && species < ScatterBiomes.CanopySpeciesCount, "species must be a canopy species");
            Contract.Requires(geology >= Geology.Native && geology <= Geology.Slate, "geology must be defined");
            if (geology == Geology.Native || !CountryRock[species])
            {
                return species;
            }

            int pick = (int)(roll % 100u);
            switch (geology)
            {
                case Geology.Karst:
                    return pick < 50 ? Limestone : pick < 85 ? StoneSpire : Marble;
                case Geology.Basalt:
                    return pick < 60 ? BasaltRock : BasaltColumn;
                case Geology.Granite:
                    return pick < 55 ? GraniteRock : GraniteTor;
                default:
                    return SlateRock;
            }
        }

        /// <summary>Species id of the rock that gives <paramref name="geology"/> its ground colour.</summary>
        /// <remarks>Pre: geology defined. Post: null for Native; otherwise a ScatterBiomes species id. Side effects: none.</remarks>
        public static string MainRock(Geology geology)
        {
            Contract.Requires(geology >= Geology.Native && geology <= Geology.Slate, "geology must be defined");
            return geology switch
            {
                Geology.Karst => "limestone",
                Geology.Basalt => "basalt",
                Geology.Granite => "granite",
                Geology.Slate => "slate",
                _ => null,
            };
        }

        private static int[][] BuildWeights()
        {
            var byBiome = new int[ScatterBiomes.BiomeIds.Count][];
            foreach ((string id, int[] weights) in Table)
            {
                Contract.Requires(weights.Length == Count, id + ": one weight per geology");
                byBiome[ScatterBiomes.BiomeOf(id)] = weights;
            }

            return byBiome;
        }

        private static bool[] BuildCountryRock()
        {
            var rock = new bool[ScatterBiomes.CanopySpeciesCount];
            foreach (string id in new[] { "granite", "limestone", "marble", "slate", "sandstone", "basalt" })
            {
                rock[ScatterBiomes.IndexOf(id)] = true;
            }

            return rock;
        }
    }
}
