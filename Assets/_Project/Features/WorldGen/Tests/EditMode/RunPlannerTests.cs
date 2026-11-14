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
        // v2 → v3 (D0-B2) also left it unchanged: the fixture plans an adult boss, where the stage cap never binds.
        // v3 → v4 (Q-D1-R1) left both digests unchanged: the fixture has no land predicate, so the sea sweep never runs.
        // v4 → v5: increased wild pack density and guaranteed an opening-sight pack within ~18 m of LandingDirection.
        // v5 → v6: PlanetFrame.FromUp flipped East (Q-T5), changing Scatter directions.
        private const ulong SnapshotSeed12345V6 = 9633294100616350916UL;

        // SOL W-B2-SOL-05: RunPlanner.Hash quantizes directions to 1e-4 and skips LandingDirection, so it cannot back the
        // "adult plan bit-identical to v2" claim. This digest covers every direction's raw float bits, landing included.
        // Pinned on this EditMode backend only; cross-backend float drift is ADR-0007's handshake, not this test's job.
        private const ulong RawBitsSeed12345V6 = 3926658488885016152UL;

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
            _type.Set(new List<string> { "forest", "river_valley" },
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
            RunPlanner.Generate(new PlanetConfig(radius, float3.zero), seed, _type, CreatureStage.Adult, _pool, _vocab, isLand);

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
            Assert.That(RunPlanner.GeneratorVersion, Is.EqualTo(6), "bump the snapshot with GeneratorVersion");
            Assert.That(RunPlanner.Hash(Plan(12345)), Is.EqualTo(SnapshotSeed12345V6));
        }

        [Test]
        public void Raw_bits_snapshot_seed_12345_is_pinned()
        {
            Assert.That(RunPlanner.GeneratorVersion, Is.EqualTo(6), "bump the snapshot with GeneratorVersion");
            Assert.That(RawBitsDigest(Plan(12345)), Is.EqualTo(RawBitsSeed12345V6));
        }

        [Test]
        public void Landing_sight_pack_spawns_within_visible_range()
        {
            for (ulong seed = 0; seed < 20; seed++)
            {
                RunPlan p = Plan(seed, 320f);
                float nearest = float.MaxValue;
                foreach (RunSpawn s in p.Spawns)
                {
                    float d = math.acos(math.clamp(math.dot(s.Direction, p.LandingDirection), -1f, 1f)) * 320f;
                    if (d < nearest)
                    {
                        nearest = d;
                    }
                }

                Assert.That(nearest, Is.LessThanOrEqualTo(25f), $"seed {seed} nearest spawn {nearest:F1}m");
            }
        }

        private static ulong RawBitsDigest(RunPlan plan)
        {
            ulong h = 14695981039346656037UL;
            void Mix(uint v)
            {
                for (int i = 0; i < 4; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFF;
                    h *= 1099511628211UL;
                }
            }

            void MixDir(float3 d)
            {
                uint3 b = math.asuint(d);
                Mix(b.x);
                Mix(b.y);
                Mix(b.z);
            }

            Mix((uint)RunPlanner.Hash(plan));
            Mix((uint)(RunPlanner.Hash(plan) >> 32));
            MixDir(plan.LandingDirection);
            foreach (RunRegion r in plan.Regions)
            {
                MixDir(r.Center);
            }

            foreach (RunSpawn s in plan.Spawns)
            {
                MixDir(s.Direction);
            }

            return h;
        }

        // REQ: SOL P2-12 D0 calibration (2026-11-09) — twilight (light/shadow) and storm (thunder) biases have no wild
        // forms, which made the DefeatWild step impossible.
        [TestCase(false, TestName = "DefeatWild_is_achievable_when_bias_is_in_the_pool")]
        [TestCase(true, TestName = "DefeatWild_is_achievable_when_bias_is_absent_from_the_pool")]
        public void DefeatWild_is_achievable(bool biasAbsent)
        {
            if (biasAbsent)
            {
                _type.Set(new List<string> { "ruins" }, new List<Element> { Element.Light, Element.Shadow });
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
            // Northern cap is "sea". SOL Q-D1-R320 witness: v3 left 6 spawns (the boss among them) at sea for seed 1.
            System.Func<float3, bool> land = d => d.y < 0.2f;
            foreach (ulong seed in new ulong[] { 1, 3 })
            {
                foreach (float radius in new[] { 320f, 640f, 1280f })
                {
                    RunPlan p = Plan(seed, radius, land);
                    foreach (RunSpawn s in p.Spawns)
                    {
                        Assert.That(land(s.Direction), Is.True, $"seed {seed} R {radius} {s.Role} region {s.Region}");
                    }

                    Assert.That(land(p.LandingDirection), Is.True, $"seed {seed} R {radius} landing");
                }
            }
        }

        [Test]
        public void Sea_spawns_move_to_the_nearest_land_and_land_spawns_stay_put()
        {
            // Sites and the region graph never read isLand, so a sea disc centred on the dry plan's boss site keeps the
            // boss region and puts its site (maxAngle 0, the v3 stuck case) at sea. The moved boss must sit just past
            // the coast: within one sweep ring (0.01 rad) plus float slack of the disc edge.
            const float SeaRadius = 0.3f;
            RunPlan dry = Plan(1, 320f, d => true);
            Assert.That(RunPlanner.Hash(dry), Is.EqualTo(RunPlanner.Hash(Plan(1, 320f))), "an always-land predicate changes nothing");
            float3 site = dry.Regions[dry.BossRegion].Center;
            System.Func<float3, bool> land = d => math.dot(d, site) < math.cos(SeaRadius);
            RunPlan wet = Plan(1, 320f, land);
            Assert.That(wet.BossRegion, Is.EqualTo(dry.BossRegion));
            RunSpawn boss = wet.Spawns[wet.Quest[wet.Quest.Count - 1].TargetSpawn];
            Assert.That(boss.Role, Is.EqualTo(SpawnRole.Boss));
            Assert.That(land(boss.Direction), Is.True);
            float moved = math.acos(math.clamp(math.dot(boss.Direction, site), -1f, 1f));
            Assert.That(moved, Is.GreaterThanOrEqualTo(SeaRadius - 1e-3f).And.LessThanOrEqualTo(SeaRadius + 0.02f));
        }

        [Test]
        public void All_sea_predicate_fails_loudly()
        {
            Assert.Throws<ContractException>(() => Plan(1, 320f, d => false));
        }

        // SOL Q-D1-IMPL-R2-01: a 0.001 rad island slipped between sweep rings and generation threw "all sea". The Pre now
        // promises only caps ≥ MinLandCapRad are found; check that promise for lone islands at many offsets and bearings
        // from a sea-locked boss site, which forces every spawn through the sweep.
        [Test]
        public void A_lone_island_of_the_minimum_cap_is_always_found()
        {
            RunPlan dry = Plan(1, 320f, d => true);
            float3 site = dry.Regions[dry.BossRegion].Center;
            float3 t = PlanetFrame.FromUp(site).East;
            float3 b = math.cross(site, t);
            float cosCap = math.cos(RunPlanner.MinLandCapRad);
            int cases = 0;
            foreach (float offset in new[] { 0.004f, 0.017f, 1.3f, 3.1f })
            {
                for (int k = 0; k < 5; k++)
                {
                    float a = 0.37f + k * 1.3f;
                    float3 island = math.normalize(site * math.cos(offset)
                        + (t * math.cos(a) + b * math.sin(a)) * math.sin(offset));
                    System.Func<float3, bool> land = d => math.dot(d, island) >= cosCap;
                    RunPlan p = Plan(1, 320f, land);
                    foreach (RunSpawn s in p.Spawns)
                    {
                        Assert.That(land(s.Direction), Is.True, $"offset {offset} bearing {a}");
                    }

                    cases++;
                }
            }

            Assert.That(cases, Is.EqualTo(20));
        }

        // The coverage argument behind MinLandCapRad, checked directly on the sweep: seeded random search centres and caps,
        // offsets weighted towards the first rings (where few points per ring make gaps likeliest) plus some far ones.
        [Test]
        public void The_sweep_finds_every_minimum_cap()
        {
            var rng = new Cosmimera.Core.Random.Pcg32(20261111UL, 0x4C414E44UL);
            float cosCap = math.cos(RunPlanner.MinLandCapRad);
            for (int i = 0; i < 3000; i++)
            {
                float3 center = RandomUnit(ref rng);
                float3 side = math.normalize(math.cross(center, RandomUnit(ref rng)));
                float offset = i < 2900 ? rng.NextFloat() * 0.3f : rng.NextFloat() * math.PI;
                float3 island = math.normalize(center * math.cos(offset) + side * math.sin(offset));
                float3 found = RunPlanner.NearestLand(center, d => math.dot(d, island) >= cosCap);
                Assert.That(math.dot(found, island), Is.GreaterThanOrEqualTo(cosCap), $"case {i}");
            }
        }

        private static float3 RandomUnit(ref Cosmimera.Core.Random.Pcg32 rng)
        {
            float z = rng.NextFloat() * 2f - 1f;
            float a = rng.NextFloat() * 2f * math.PI;
            float r = math.sqrt(math.max(0f, 1f - z * z));
            return new float3(r * math.cos(a), r * math.sin(a), z);
        }

        [Test]
        public void Land_narrower_than_the_minimum_cap_violates_the_pre()
        {
            RunPlan dry = Plan(1, 320f, d => true);
            float3 c = dry.Regions[0].Center;
            float3 island = math.normalize(c * math.cos(0.005f) + PlanetFrame.FromUp(c).East * math.sin(0.005f));
            Assert.Throws<ContractException>(() => Plan(1, 320f, d => math.dot(d, island) > math.cos(0.001f)));
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

        // REQ: planet-world.md#boss-尺寸 (user 2026-11-10): spawn forms are capped at the boss stage.
        [TestCase(CreatureStage.Juvenile)]
        [TestCase(CreatureStage.Adolescent)]
        [TestCase(CreatureStage.Adult)]
        public void Boss_has_the_requested_stage_and_no_spawn_outranks_it(CreatureStage bossStage)
        {
            var stageOf = new Dictionary<string, CreatureStage>();
            foreach (CreatureDef c in _pool)
            {
                stageOf[c.Id] = c.Stage;
            }

            for (ulong seed = 1; seed <= 20; seed++)
            {
                RunPlan p = RunPlanner.Generate(_planet, seed, _type, bossStage, _pool, _vocab, null);
                int bosses = 0;
                foreach (RunSpawn s in p.Spawns)
                {
                    Assert.That(stageOf[s.CreatureId], Is.LessThanOrEqualTo(bossStage), $"seed {seed} {s.Role} {s.CreatureId}");
                    if (s.Role == SpawnRole.Boss)
                    {
                        bosses++;
                        Assert.AreEqual(bossStage, stageOf[s.CreatureId], $"seed {seed}");
                    }
                }

                Assert.AreEqual(1, bosses);
            }
        }

        [Test]
        public void Lower_boss_stage_changes_spawns_but_not_the_region_graph()
        {
            RunPlan adult = RunPlanner.Generate(_planet, 7, _type, CreatureStage.Adult, _pool, _vocab, null);
            RunPlan juvenile = RunPlanner.Generate(_planet, 7, _type, CreatureStage.Juvenile, _pool, _vocab, null);
            Assert.AreEqual(adult.Regions.Count, juvenile.Regions.Count);
            Assert.AreEqual(adult.BossRegion, juvenile.BossRegion);
            Assert.That(RunPlanner.Hash(juvenile), Is.Not.EqualTo(RunPlanner.Hash(adult)));
        }

        [Test]
        public void Pool_without_a_boss_stage_form_is_rejected()
        {
            List<CreatureDef> noJuvenile = _pool.FindAll(c => c.Stage != CreatureStage.Juvenile);
            Assert.Throws<ContractException>(() =>
                RunPlanner.Generate(_planet, 1, _type, CreatureStage.Juvenile, noJuvenile, _vocab, null));
            Assert.Throws<ContractException>(() =>
                RunPlanner.Generate(_planet, 1, _type, (CreatureStage)3, _pool, _vocab, null));
        }

        [Test]
        public void Empty_pool_is_rejected()
        {
            Assert.Throws<ContractException>(() =>
                RunPlanner.Generate(_planet, 1, _type, CreatureStage.Adult, new List<CreatureDef>(), _vocab, null));
        }
    }
}
