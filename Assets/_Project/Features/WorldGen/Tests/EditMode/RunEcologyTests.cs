using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    // REQ: docs/design/creature-ecology.md (P3-31; Q-C62 competitive exclusion).
    public sealed class RunEcologyTests
    {
        // Pinned digest of RunEcology.Hash for seed 12345 on the fixture below; update only with a
        // RunEcology.GeneratorVersion bump (testing.md).
        private const ulong SnapshotSeed12345V1 = 11313807179483107622UL;

        private const float Radius = 640f;
        private readonly List<Object> _created = new List<Object>();
        private PlanetTypeDef _type;
        private List<CreatureDef> _pool;
        private RunPlanVocabulary _vocab;
        private NicheDef _shared;
        private NicheDef _forestOnly;

        [SetUp]
        public void SetUp()
        {
            _type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(_type);
            _type.SetId("verdant_planet");
            _type.Set(new List<string> { "forest", "river_valley" }, new List<Element> { Element.Wood, Element.Water });
            // Every line shares one wide niche: lots of same-niche, different-line conflicts to resolve.
            _shared = Niche("niche_shared", WanderMode.BoundedRandom, 12000, new[] { "grassland", "forest" },
                new HabitWeight(Habit.Vigilant, 3), new HabitWeight(Habit.Territorial, 1));
            _forestOnly = Niche("niche_forest", WanderMode.Loop, 9000, new[] { "forest" }, new HabitWeight(Habit.Tolerant, 1));
            _pool = new List<CreatureDef>();
            foreach (string line in new[] { "wood", "fire", "water" })
            {
                for (int stage = 0; stage < 3; stage++)
                {
                    Element e = line == "wood" ? Element.Wood : line == "fire" ? Element.Fire : Element.Water;
                    _pool.Add(Form(line + "_" + stage, line, (CreatureStage)stage, e,
                        new NicheCandidate(_shared, 2), new NicheCandidate(_forestOnly, 1)));
                }
            }

            _vocab = new RunPlanVocabulary(
                new[] { "ruins", "forest", "riverbank", "highland", "coast", "crater", "ice_shelf", "canyon" },
                new[] { "affix_frenzy", "affix_pack", "affix_fog", "affix_bounty" },
                new[] { "chain_a", "chain_b", "chain_c" });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private NicheDef Niche(string id, WanderMode mode, int radiusMm, string[] habitats, params HabitWeight[] habits)
        {
            NicheDef n = ScriptableObject.CreateInstance<NicheDef>();
            _created.Add(n);
            n.SetId(id);
            bool unbounded = mode == WanderMode.UnboundedRandom;
            n.Set(id, new List<string>(habitats), new List<HabitWeight>(habits), mode, unbounded ? 0 : radiusMm,
                unbounded ? radiusMm : 0, new WanderDistribution(300, 40, 50, 50, 900),
                new WanderDistribution(5000, 1000, 1500, 500, 60000), new WanderDistribution(5000, 1200, 2000, 250, 60000));
            return n;
        }

        private CreatureDef Form(string id, string line, CreatureStage stage, Element e, params NicheCandidate[] niches)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set(line, stage, CreatureSizeClass.M, AiStyle.Charge, new List<NicheCandidate>(niches), null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(e, 15) },
                new CreatureStats(25, 25, 25, 25), null, null, "{}", false);
            return c;
        }

        // Northern hemisphere is forest, southern grassland: the habitat filter has something to bite on.
        private static string Habitat(float3 d) => d.y >= 0f ? "forest" : "grassland";

        private RunPlan Ecology(ulong seed)
        {
            RunPlan raw = RunPlanner.Generate(new PlanetConfig(Radius, float3.zero), seed, _type, CreatureStage.Adult, _pool,
                _vocab, null);
            return RunEcology.Apply(raw, _pool, Radius, Habitat, null);
        }

        [Test]
        public void BoundedNormal_stays_in_bounds_and_is_exact_at_zero_sd()
        {
            var rng = new Pcg32(1, 2);
            Assert.That(BoundedNormal.Sample(ref rng, 500, 0, 1, 1000), Is.EqualTo(500));
            Assert.That(BoundedNormal.Sample(ref rng, 5000, 0, 1, 1000), Is.EqualTo(1000), "clamped");
            for (int i = 0; i < 2000; i++)
            {
                Assert.That(BoundedNormal.Sample(ref rng, 300, 400, 50, 900), Is.InRange(50, 900));
            }

            Assert.Throws<ContractException>(() => BoundedNormal.Sample(ref rng, 0, -1, 0, 1));
            Assert.Throws<ContractException>(() => BoundedNormal.Sample(ref rng, 0, 1, 2, 1));
        }

        [Test]
        public void BoundedNormal_consumes_twelve_draws_whatever_the_sd()
        {
            var a = new Pcg32(9, 9);
            var b = new Pcg32(9, 9);
            BoundedNormal.Sample(ref a, 10, 0, 1, 100);
            for (int i = 0; i < 12; i++)
            {
                b.NextUInt();
            }

            Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()));
        }

        [Test]
        public void BoundedNormal_mean_and_spread_are_roughly_right()
        {
            var rng = new Pcg32(3, 4);
            const int n = 20000;
            double sum = 0, sq = 0;
            for (int i = 0; i < n; i++)
            {
                int v = BoundedNormal.Sample(ref rng, 10000, 1000, 0, 20000);
                sum += v;
                sq += (double)v * v;
            }

            double mean = sum / n;
            double sd = System.Math.Sqrt(sq / n - mean * mean);
            Assert.That(mean, Is.EqualTo(10000).Within(30));
            Assert.That(sd, Is.EqualTo(1000).Within(40));
        }

        [Test]
        public void Draw_respects_the_habitat_filter_and_the_allowed_habits()
        {
            CreatureDef form = _pool[0];
            for (ulong k = 0; k < 300; k++)
            {
                WildIndividual g = WildIndividual.Draw(form, "grassland", k);
                Assert.That(g.Niche, Is.SameAs(_shared), "the forest-only niche is filtered out in grassland");
                Assert.That(g.Habit, Is.EqualTo(Habit.Vigilant).Or.EqualTo(Habit.Territorial));
                Assert.That(g.SpeedPermilleMean, Is.InRange(50, 900));
                Assert.That(g.StreamKey, Is.EqualTo(k));
            }

            int forest = 0;
            for (ulong k = 0; k < 3000; k++)
            {
                if (WildIndividual.Draw(form, "forest", k).Niche == _forestOnly)
                {
                    forest++;
                }
            }

            Assert.That(forest, Is.InRange(850, 1150), "weights 2:1 → about a third");
        }

        [Test]
        public void Draw_is_deterministic_and_fails_loudly_without_candidates()
        {
            WildIndividual a = WildIndividual.Draw(_pool[3], "forest", 77);
            WildIndividual b = WildIndividual.Draw(_pool[3], "forest", 77);
            Assert.That((a.Niche, a.Habit, a.SpeedPermilleMean, a.MoveMsMean, a.PauseMsMean),
                Is.EqualTo((b.Niche, b.Habit, b.SpeedPermilleMean, b.MoveMsMean, b.PauseMsMean)));
            Assert.Throws<ContractException>(() => WildIndividual.Draw(_pool[3], "glacier", 1));
            Assert.Throws<ContractException>(() => WildIndividual.Draw(null, "forest", 1));
            Assert.Throws<ContractException>(() =>
                _ = new WildIndividual(_forestOnly, Habit.Dominant, 300, 5000, 5000, 0), "habit outside the niche");
            Assert.Throws<ContractException>(() =>
                _ = new WildIndividual(_forestOnly, Habit.Tolerant, 9999, 5000, 5000, 0), "mean outside the distribution");
        }

        [Test]
        public void KeyFor_separates_nearby_keys_and_seeds()
        {
            Assert.That(WildIndividual.KeyFor(1, 0), Is.Not.EqualTo(WildIndividual.KeyFor(1, 1)));
            Assert.That(WildIndividual.KeyFor(1, 0), Is.Not.EqualTo(WildIndividual.KeyFor(2, 0)));
            Assert.That(WildIndividual.KeyFor(5, 9), Is.EqualTo(WildIndividual.KeyFor(5, 9)));
        }

        [Test]
        public void Same_niche_different_species_never_share_an_activity_area()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                RunPlan p = Ecology(seed);
                for (int i = 0; i < p.Spawns.Count; i++)
                {
                    RunSpawn a = p.Spawns[i];
                    Assert.That(a.Individual, Is.Not.Null);
                    Assert.That(a.Individual.Niche.Allows(Habitat(a.Direction)), Is.True, $"seed {seed} spawn {i}: habitat");
                    for (int j = 0; j < i; j++)
                    {
                        RunSpawn b = p.Spawns[j];
                        if (a.Individual.Niche != b.Individual.Niche || LineOf(a) == LineOf(b))
                        {
                            continue;
                        }

                        float arc = math.acos(math.clamp(math.dot(a.Direction, b.Direction), -1f, 1f)) * Radius;
                        float limit = (a.Individual.Niche.ActivityRadiusMm + b.Individual.Niche.ActivityRadiusMm) * 0.001f;
                        Assert.That(arc, Is.GreaterThanOrEqualTo(limit - 1e-2f), $"seed {seed}: spawns {j} and {i} overlap");
                    }
                }
            }
        }

        [Test]
        public void Apply_keeps_everything_but_ecology_and_moved_directions()
        {
            RunPlan raw = RunPlanner.Generate(new PlanetConfig(Radius, float3.zero), 5, _type, CreatureStage.Adult, _pool, _vocab, null);
            RunPlan eco = RunEcology.Apply(raw, _pool, Radius, Habitat, null);
            Assert.That(eco.Spawns.Count, Is.EqualTo(raw.Spawns.Count));
            int moved = 0;
            for (int i = 0; i < raw.Spawns.Count; i++)
            {
                Assert.That(eco.Spawns[i].CreatureId, Is.EqualTo(raw.Spawns[i].CreatureId));
                Assert.That(eco.Spawns[i].Role, Is.EqualTo(raw.Spawns[i].Role));
                Assert.That(eco.Spawns[i].Region, Is.EqualTo(raw.Spawns[i].Region));
                Assert.That(eco.Spawns[i].Group, Is.EqualTo(raw.Spawns[i].Group));
                moved += math.any(eco.Spawns[i].Direction != raw.Spawns[i].Direction) ? 1 : 0;
            }

            Assert.That(moved, Is.GreaterThan(0), "packs mix lines within 5 m, so some same-niche members must move");
            Assert.That(eco.Quest, Is.EqualTo(raw.Quest));
            Assert.That(raw.Spawns[0].Individual, Is.Null, "the input plan is not mutated");
        }

        [Test]
        public void Apply_is_deterministic_and_pinned()
        {
            ulong a = RunEcology.Hash(Ecology(12345));
            Assert.That(RunEcology.Hash(Ecology(12345)), Is.EqualTo(a));
            Assert.That(RunEcology.Hash(Ecology(12346)), Is.Not.EqualTo(a));
            Assert.That(RunEcology.GeneratorVersion, Is.EqualTo(1));
            Assert.That(a, Is.EqualTo(SnapshotSeed12345V1));
        }

        [Test]
        public void Apply_preconditions()
        {
            RunPlan raw = RunPlanner.Generate(new PlanetConfig(Radius, float3.zero), 5, _type, CreatureStage.Adult, _pool, _vocab, null);
            Assert.Throws<ContractException>(() => RunEcology.Apply(null, _pool, Radius, Habitat, null));
            Assert.Throws<ContractException>(() => RunEcology.Apply(raw, _pool, 0f, Habitat, null));
            Assert.Throws<ContractException>(() => RunEcology.Apply(raw, _pool, Radius, null, null));
            Assert.Throws<ContractException>(() => RunEcology.Apply(raw, _pool, Radius, d => "glacier", null),
                "a habitat no candidate allows is a data error");
        }

        private string LineOf(RunSpawn s) => _pool.Find(c => c.Id == s.CreatureId).LineId;
    }
}
