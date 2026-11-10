using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class RunPlannerTests
    {
        // Pinned digest of seed 12345 on the fixture below; update only with a GeneratorVersion bump (testing.md).
        // v1 → v2 left this fixture's digest unchanged: its bias elements are all in the pool, so the quest element and the
        // RNG stream are the same; only twilight/storm-like biases change.
        private const ulong SnapshotSeed12345V2 = 3196599958287516450UL;

        private readonly List<Object> _created = new List<Object>();
        private PlanetTypeDef _type;
        private List<CreatureDef> _pool;
        private RunPlanVocabulary _vocab;
        private PlanetConfig _planet;

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(640f, float3.zero);
            _type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(_type);
            _type.SetId("verdant_planet");
            _type.Set(PlanetSize.Medium, new List<string> { "forest", "river_valley" },
                new List<Element> { Element.Wood, Element.Water });
            _pool = new List<CreatureDef>();
            string[] lines = { "wood", "fire", "water" };
            foreach (string line in lines)
            {
                for (int stage = 0; stage < 3; stage++)
                {
                    var c = ScriptableObject.CreateInstance<CreatureDef>();
                    _created.Add(c);
                    c.SetId(line + "_" + stage);
                    Element e = line == "wood" ? Element.Wood : line == "fire" ? Element.Fire : Element.Water;
                    c.Set(line, (CreatureStage)stage, CreatureSizeClass.M, AiStyle.Charge, Temperament.Normal, null, 0,
                        new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(e, 15) },
                        new CreatureStats(25, 25, 25, 25), null, null, "{}", false);
                    _pool.Add(c);
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

        private RunPlan Plan(ulong seed, float radius = 640f, System.Func<float3, bool> isLand = null) =>
            RunPlanner.Generate(new PlanetConfig(radius, float3.zero), seed, _type, _pool, _vocab, isLand);

        [Test]
        public void Boss_is_reachable_by_the_main_route_for_1000_seeds()
        {
            for (ulong seed = 0; seed < 1000; seed++)
            {
                RunPlan p = Plan(seed);
                Assert.That(p.BossRegion, Is.Not.EqualTo(p.LandingRegion), $"seed {seed}");
                Assert.That(p.MainRoute[0], Is.EqualTo(p.LandingRegion), $"seed {seed}");
                Assert.That(p.MainRoute[p.MainRoute.Count - 1], Is.EqualTo(p.BossRegion), $"seed {seed}");
                for (int i = 1; i < p.MainRoute.Count; i++)
                {
                    CollectionAssert.Contains(p.Regions[p.MainRoute[i - 1]].Neighbors, p.MainRoute[i], $"seed {seed}");
                }

                Assert.That(p.Quest[p.Quest.Count - 1].Kind, Is.EqualTo(QuestStepKind.DefeatBoss), $"seed {seed}");
            }
        }

        [Test]
        public void Region_graph_is_symmetric_and_connected()
        {
            RunPlan p = Plan(7);
            var seen = new HashSet<int> { 0 };
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                foreach (int v in p.Regions[u].Neighbors)
                {
                    CollectionAssert.Contains(p.Regions[v].Neighbors, u);
                    if (seen.Add(v))
                    {
                        stack.Push(v);
                    }
                }
            }

            Assert.That(seen.Count, Is.EqualTo(p.Regions.Count));
        }

        [Test]
        public void Region_count_scales_with_surface_area()
        {
            Assert.That(Plan(1, 640f).Regions.Count, Is.EqualTo(12));
            Assert.That(Plan(1, 1280f).Regions.Count, Is.EqualTo(32));
        }

        [Test]
        public void Exactly_one_boss_spawn_in_the_boss_region_and_an_adult()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                RunPlan p = Plan(seed);
                int bosses = 0;
                foreach (RunSpawn s in p.Spawns)
                {
                    if (s.Role == SpawnRole.Boss)
                    {
                        bosses++;
                        Assert.That(s.Region, Is.EqualTo(p.BossRegion));
                        StringAssert.EndsWith("_2", s.CreatureId);
                    }
                }

                Assert.That(bosses, Is.EqualTo(1));
            }
        }

        [Test]
        public void Landing_has_no_affix_and_boss_region_has_one()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                RunPlan p = Plan(seed);
                Assert.That(p.Regions[p.LandingRegion].AffixId, Is.Null);
                Assert.That(p.Regions[p.BossRegion].AffixId, Is.Not.Null);
            }
        }

        [Test]
        public void Same_seed_same_plan_and_different_seeds_differ()
        {
            Assert.That(RunPlanner.Hash(Plan(42)), Is.EqualTo(RunPlanner.Hash(Plan(42))));
            Assert.That(RunPlanner.Hash(Plan(42)), Is.Not.EqualTo(RunPlanner.Hash(Plan(43))));
        }

        [Test]
        public void Snapshot_seed_12345_is_pinned()
        {
            Assert.That(RunPlanner.GeneratorVersion, Is.EqualTo(2), "bump the snapshot with GeneratorVersion");
            Assert.That(RunPlanner.Hash(Plan(12345)), Is.EqualTo(SnapshotSeed12345V2));
        }

        // REQ: SOL P2-12 D0 calibration (2026-11-09) — twilight (light/shadow) and storm (thunder) biases have no wild
        // forms, which made the DefeatWild step impossible.
        [TestCase(false, TestName = "DefeatWild_is_achievable_when_bias_is_in_the_pool")]
        [TestCase(true, TestName = "DefeatWild_is_achievable_when_bias_is_absent_from_the_pool")]
        public void DefeatWild_is_achievable(bool biasAbsent)
        {
            if (biasAbsent)
            {
                _type.Set(PlanetSize.Medium, new List<string> { "ruins" }, new List<Element> { Element.Light, Element.Shadow });
            }

            var byId = new Dictionary<string, Element>();
            foreach (CreatureDef c in _pool)
            {
                byId[c.Id] = RunPlanner.MainElement(c);
            }

            int inBias = 0;
            for (ulong seed = 0; seed < 300; seed++)
            {
                RunPlan p = Plan(seed);
                RunQuestStep step = default;
                int eliteTarget = -1;
                foreach (RunQuestStep q in p.Quest)
                {
                    if (q.Kind == QuestStepKind.DefeatWild)
                    {
                        step = q;
                    }
                    else if (q.Kind == QuestStepKind.DefeatElite)
                    {
                        eliteTarget = q.TargetSpawn;
                    }
                }

                // SOL P3-10 S1: the next step's elite must survive DefeatWild, so it never counts as available.
                int available = 0;
                for (int i = 0; i < p.Spawns.Count; i++)
                {
                    RunSpawn sp = p.Spawns[i];
                    if (sp.Role != SpawnRole.Boss && i != eliteTarget && byId[sp.CreatureId] == step.Element)
                    {
                        available++;
                    }
                }

                Assert.That(step.Count, Is.InRange(1, 3), $"seed {seed}");
                Assert.That(available, Is.GreaterThanOrEqualTo(step.Count), $"seed {seed}");
                if (step.Element == Element.Wood || step.Element == Element.Water)
                {
                    inBias++;
                }
            }

            if (!biasAbsent)
            {
                // Wood/water are weighted 3:1 over fire, so a bias element almost always has 3 non-boss spawns.
                Assert.That(inBias, Is.GreaterThanOrEqualTo(290));
            }
        }

        [Test]
        public void Spawns_respect_the_land_predicate()
        {
            // Northern hemisphere is "sea": spawns must avoid it whenever a land point exists nearby.
            RunPlan p = Plan(3, 640f, d => d.y < 0.2f);
            int onLand = 0;
            foreach (RunSpawn s in p.Spawns)
            {
                if (s.Direction.y < 0.2f)
                {
                    onLand++;
                }
            }

            Assert.That(onLand, Is.GreaterThan(p.Spawns.Count / 2));
        }

        [Test]
        public void Region_lookup_returns_the_nearest_site()
        {
            RunPlan p = Plan(5);
            foreach (RunRegion r in p.Regions)
            {
                Assert.That(RunPlanner.RegionAt(p, r.Center), Is.EqualTo(r.Index));
            }
        }

        [Test]
        public void Empty_pool_is_rejected()
        {
            Assert.Throws<ContractException>(() =>
                RunPlanner.Generate(_planet, 1, _type, new List<CreatureDef>(), _vocab, null));
        }
    }
}
