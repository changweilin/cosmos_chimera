using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class ClimateFloraTests
    {
        private static int Id(string s) => ScatterBiomes.IndexOf(s);

        [Test]
        public void New_species_are_in_the_catalog_on_the_right_layer()
        {
            foreach (string tree in new[] { "baobab", "tree_fern", "krummholz", "cypress", "mangrove" })
            {
                Assert.That(Id(tree), Is.LessThan(ScatterBiomes.CanopySpeciesCount), tree);
                Assert.That(ScatterBiomes.Species[Id(tree)].Shape, Is.EqualTo(ScatterShape.Tree), tree);
            }

            foreach (string plant in new[] { "succulent", "cushion_moss", "cotton_grass", "heather" })
            {
                Assert.That(Id(plant), Is.GreaterThanOrEqualTo(ScatterBiomes.CanopySpeciesCount), plant);
            }
        }

        [Test]
        public void Climate_bound_trees_are_swapped_out_of_their_band()
        {
            Assert.That(ScatterBiomes.ForClimate(Id("baobab"), ForestClimate.Tropical, false), Is.EqualTo(Id("baobab")));
            Assert.That(ScatterBiomes.ForClimate(Id("baobab"), ForestClimate.Temperate, false), Is.EqualTo(Id("umbrella")));
            Assert.That(ScatterBiomes.ForClimate(Id("baobab"), ForestClimate.Cold, false), Is.EqualTo(Id("krummholz")));
            Assert.That(ScatterBiomes.ForClimate(Id("mangrove"), ForestClimate.Temperate, false), Is.EqualTo(Id("swamp_tree")));
            Assert.That(ScatterBiomes.ForClimate(Id("cypress"), ForestClimate.Temperate, false), Is.EqualTo(Id("cypress")));
            Assert.That(ScatterBiomes.ForClimate(Id("cypress"), ForestClimate.Cold, false), Is.EqualTo(Id("conifer")));
            Assert.That(ScatterBiomes.ForClimate(Id("krummholz"), ForestClimate.Tropical, false), Is.EqualTo(Id("conifer")));
            Assert.That(ScatterBiomes.ForClimate(Id("krummholz"), ForestClimate.Tropical, true), Is.EqualTo(Id("krummholz")));
            Assert.That(ScatterBiomes.ForClimate(Id("granite"), ForestClimate.Cold, true), Is.EqualTo(Id("granite")));
            Assert.Throws<ContractException>(() => ScatterBiomes.ForClimate(Id("heather"), ForestClimate.Cold, false));
        }

        [Test]
        public void Temperate_lowland_trees_match_the_climate_free_overload()
        {
            for (int type = 0; type <= (int)ForestType.Secondary; type++)
            {
                for (uint roll = 0; roll < 5000; roll += 7)
                {
                    int a = ScatterBiomes.PickTree((ForestType)type, false, roll * 31u, roll * 2654435761u, out float lo1, out float hi1);
                    int b = ScatterBiomes.PickTree((ForestType)type, ForestClimate.Temperate, false, false, roll * 31u,
                        roll * 2654435761u, out float lo2, out float hi2);
                    Assert.That(b, Is.EqualTo(a));
                    Assert.That(lo2, Is.EqualTo(lo1));
                    Assert.That(hi2, Is.EqualTo(hi1));
                }
            }
        }

        [Test]
        public void Tropics_grow_tree_ferns_and_no_conifers()
        {
            bool fern = false;
            for (int type = 0; type <= (int)ForestType.Secondary; type++)
            {
                for (uint roll = 0; roll < 20000; roll += 13)
                {
                    int t = ScatterBiomes.PickTree((ForestType)type, ForestClimate.Tropical, false, false, roll * 17u,
                        roll * 2246822519u, out _, out _);
                    Assert.That(t, Is.Not.EqualTo(Id("conifer")));
                    fern |= t == Id("tree_fern");
                }
            }

            Assert.That(fern, Is.True);
        }

        [Test]
        public void Above_the_treeline_broadleaves_give_way_to_krummholz()
        {
            bool krummholz = false;
            for (uint roll = 0; roll < 20000; roll += 11)
            {
                int t = ScatterBiomes.PickTree(ForestType.Mixed, ForestClimate.Temperate, true, true, roll, roll * 2246822519u,
                    out _, out _);
                Assert.That(t, Is.Not.EqualTo(Id("broadleaf")));
                krummholz |= t == Id("krummholz");
            }

            Assert.That(krummholz, Is.True);
        }

        [Test]
        public void Cold_and_dry_landforms_list_their_new_understory()
        {
            bool cushion = false, succulent = false;
            int tundra = ScatterBiomes.BiomeOf("tundra");
            int desert = ScatterBiomes.BiomeOf("desert");
            for (uint roll = 0; roll < 2000; roll++)
            {
                cushion |= ScatterBiomes.PickUnderstory(tundra, roll, false) == Id("cushion_moss");
                succulent |= ScatterBiomes.PickUnderstory(desert, roll, false) == Id("succulent");
            }

            Assert.That(cushion, Is.True);
            Assert.That(succulent, Is.True);
        }
    }
}
