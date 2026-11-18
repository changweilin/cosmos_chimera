using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: characters.md#路人-npc — residents are built in code, identically on every client, inside the envelope
    // their collider and the camera budget assume.
    public sealed class NpcMeshesTests
    {
        private static readonly ArchEra[] Eras =
        {
            ArchEra.Primal, ArchEra.Agrarian, ArchEra.Mercantile, ArchEra.Industrial, ArchEra.Steampunk, ArchEra.Future,
            ArchEra.Wasteland, ArchEra.Ancient,
        };

        private readonly List<Mesh> _meshes = new List<Mesh>();

        private static NpcSpec Spec(NpcRace race, NpcRole role, ArchEra era, ArchRegion region = ArchRegion.Verdant,
            NpcSex sex = NpcSex.Male, float age = 0.4f, float health = 0.8f, float stature = 0f, float bulk = 0f,
            float borrow = 0f, ulong seed = 11) =>
            new NpcSpec(race, role, sex, age, health, stature, bulk, region, era, (ArchRegion)(((int)region + 2) % 6),
                ArchEra.Steampunk, borrow, seed);

        private Mesh Build(in NpcSpec spec)
        {
            Mesh mesh = NpcMeshes.Build(spec);
            _meshes.Add(mesh);
            return mesh;
        }

        private Mesh BuildHouse(in ArchSpec spec)
        {
            Mesh mesh = ArchitectureMeshes.Build(spec);
            _meshes.Add(mesh);
            return mesh;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Mesh m in _meshes)
            {
                UnityEngine.Object.DestroyImmediate(m);
            }

            _meshes.Clear();
        }

        private static IEnumerable<TestCaseData> RaceByEra()
        {
            foreach (NpcRace race in Enum.GetValues(typeof(NpcRace)))
            {
                foreach (ArchEra era in Eras)
                {
                    yield return new TestCaseData(race, era).SetName($"{race}_{era}");
                }
            }
        }

        private static void AssertContracts(Mesh mesh, in NpcSpec spec)
        {
            Assert.That(mesh.subMeshCount, Is.EqualTo(NpcMeshes.SubMeshCount));
            Assert.That(mesh.vertexCount, Is.GreaterThan(300));
            Assert.That(mesh.vertexCount, Is.LessThan(30000), "a crowd of these must stay cheap");
            Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.colors.Length, Is.EqualTo(mesh.vertexCount));
            float top = NpcMeshes.HeightM(spec) + 1.4f;
            foreach (Vector3 p in mesh.vertices)
            {
                Assert.That(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z), Is.True);
                Assert.That(p.y, Is.InRange(-1e-4f, top + 1e-3f));
                Assert.That(new Vector2(p.x, p.z).magnitude, Is.LessThanOrEqualTo(NpcMeshes.MaxReachM + 1e-3f));
            }

            foreach (Vector3 n in mesh.normals)
            {
                Assert.That(n.magnitude, Is.EqualTo(1f).Within(1e-2f));
            }
        }

        [TestCase(NpcRace.Human, 0.4f, 0.95f)]
        [TestCase(NpcRace.Merfolk, 0.1f, 0.7f)]
        [TestCase(NpcRace.Dragonkin, 0.9f, 0.2f)]
        public void Stride_plants_one_sole_and_returns_to_rest(NpcRace race, float age, float health)
        {
            var root = new GameObject("Stride");
            try
            {
                NpcSpec spec = Spec(race, NpcRole.Guard, ArchEra.Future, age: age, health: health);
                Mesh mesh = NpcMeshes.Build(spec, root.transform, out ResidentStride gait);
                _meshes.Add(mesh);
                Transform left = gait.Bones[5], right = gait.Bones[6];
                Vector3 restL = left.position, restR = right.position;
                bool lifts = false;
                for (int i = 0; i < 24; i++)
                {
                    gait.Advance(0.04f, true);
                    float riseL = left.position.y - restL.y, riseR = right.position.y - restR.y;
                    Assert.That(math.min(riseL, riseR), Is.EqualTo(0f).Within(1e-5f));
                    Assert.That(Quaternion.Angle(left.rotation, Quaternion.identity), Is.LessThan(0.1f));
                    lifts |= math.max(riseL, riseR) > 0.001f;
                }

                Assert.That(lifts, Is.True);
                for (int i = 0; i < 5; i++) gait.Advance(0f, true);
                Assert.That(Vector3.Distance(left.position, restL), Is.LessThan(1e-5f));
                Assert.That(Vector3.Distance(right.position, restR), Is.LessThan(1e-5f));
                Assert.That(root.transform.position, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCaseSource(nameof(RaceByEra))]
        public void Every_race_in_every_era_keeps_the_mesh_contracts(NpcRace race, ArchEra era)
        {
            foreach (ArchRegion region in new[] { ArchRegion.Verdant, ArchRegion.Ice, ArchRegion.Twilight })
            {
                NpcSpec spec = Spec(race, (NpcRole)(((int)race + (int)era) % PopulacePlanner.RoleCount), era, region,
                    (NpcSex)((int)era & 1), seed: (ulong)((int)race * 17 + (int)era));
                AssertContracts(Build(spec), spec);
            }
        }

        [Test]
        public void Every_role_region_and_extreme_keeps_the_mesh_contracts()
        {
            foreach (NpcRole role in Enum.GetValues(typeof(NpcRole)))
            {
                foreach (ArchRegion region in Enum.GetValues(typeof(ArchRegion)))
                {
                    NpcSpec spec = Spec(NpcRace.Human, role, Eras[((int)role + (int)region) % 7], region, (NpcSex)((int)role & 1),
                        borrow: 0.6f, seed: (ulong)((int)role * 7 + (int)region));
                    AssertContracts(Build(spec), spec);
                }
            }

            foreach (NpcRace race in new[] { NpcRace.Dragonkin, NpcRace.Goblin, NpcRace.Angel, NpcRace.Skeleton })
            {
                // Tallest and widest, smallest child, oldest and sickest: the corners of the bounded draws.
                NpcSpec giant = Spec(race, NpcRole.Guard, ArchEra.Future, stature: 3.46f, bulk: 3.46f, borrow: 1f);
                NpcSpec child = Spec(race, NpcRole.None, ArchEra.Primal, age: 0.04f, stature: -3.46f, bulk: -3.46f);
                NpcSpec frail = Spec(race, NpcRole.Cleric, ArchEra.Wasteland, age: 0.98f, health: 0.05f);
                AssertContracts(Build(giant), giant);
                AssertContracts(Build(child), child);
                AssertContracts(Build(frail), frail);
            }
        }

        [Test]
        public void The_same_spec_builds_the_same_mesh_and_a_new_seed_a_different_one()
        {
            NpcSpec spec = Spec(NpcRace.Elf, NpcRole.Scholar, ArchEra.Mercantile, borrow: 0.5f);
            Mesh a = Build(spec), b = Build(spec);
            Assert.That(b.vertices, Is.EqualTo(a.vertices));
            Assert.That(b.colors, Is.EqualTo(a.colors));
            Mesh c = Build(Spec(NpcRace.Elf, NpcRole.Scholar, ArchEra.Mercantile, borrow: 0.5f, seed: 12));
            Assert.That(c.vertices, Is.Not.EqualTo(a.vertices));
        }

        [Test]
        public void Races_differ_in_height_and_silhouette()
        {
            float Height(NpcRace race) => NpcMeshes.HeightM(Spec(race, NpcRole.None, ArchEra.Agrarian));
            Assert.That(Height(NpcRace.Dwarf), Is.LessThan(Height(NpcRace.Human)));
            Assert.That(Height(NpcRace.Goblin), Is.LessThan(Height(NpcRace.Dwarf)));
            Assert.That(Height(NpcRace.Dragonkin), Is.GreaterThan(Height(NpcRace.Human)));

            // Wings and tails reach behind the body: the mesh bounds are how a silhouette is told apart from afar.
            float Behind(NpcRace race) => -Build(Spec(race, NpcRole.None, ArchEra.Agrarian)).bounds.min.z;
            Assert.That(Behind(NpcRace.Lizardfolk), Is.GreaterThan(Behind(NpcRace.Human) + 0.3f));
            Assert.That(Behind(NpcRace.Angel), Is.GreaterThan(Behind(NpcRace.Human) + 0.15f));
            float Width(NpcRace race) => Build(Spec(race, NpcRole.None, ArchEra.Agrarian)).bounds.size.x;
            Assert.That(Width(NpcRace.Demon), Is.GreaterThan(Width(NpcRace.Human) + 0.3f));
        }

        [Test]
        public void Age_health_and_build_show_in_the_figure()
        {
            NpcSpec adult = Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian);
            Assert.That(NpcMeshes.HeightM(Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, age: 0.06f)),
                Is.LessThan(NpcMeshes.HeightM(adult) * 0.75f));
            Assert.That(NpcMeshes.HeightM(Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, age: 0.95f)),
                Is.LessThan(NpcMeshes.HeightM(adult)));
            Assert.That(NpcMeshes.HeightM(Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, stature: 2f)),
                Is.GreaterThan(NpcMeshes.HeightM(adult)));

            // Ill health stoops: the crown comes down though the legs keep their length.
            Mesh hale = Build(adult), ill = Build(Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, health: 0.1f));
            Assert.That(ill.bounds.max.y, Is.LessThan(hale.bounds.max.y));
            Mesh stout = Build(Spec(NpcRace.Human, NpcRole.None, ArchEra.Future, bulk: 3f));
            Mesh slight = Build(Spec(NpcRace.Human, NpcRole.None, ArchEra.Future, bulk: -3f));
            Assert.That(stout.bounds.size.x, Is.GreaterThan(slight.bounds.size.x));
        }

        [Test]
        public void Glowing_parts_appear_where_the_look_calls_for_them()
        {
            Assert.That(Build(Spec(NpcRace.Angel, NpcRole.None, ArchEra.Agrarian)).GetTriangles(NpcMeshes.GlowSubMesh).Length,
                Is.GreaterThan(0), "halo");
            Assert.That(Build(Spec(NpcRace.Human, NpcRole.Miner, ArchEra.Agrarian)).GetTriangles(NpcMeshes.GlowSubMesh).Length,
                Is.GreaterThan(0), "helmet lamp");
            Assert.That(Build(Spec(NpcRace.Human, NpcRole.None, ArchEra.Future)).GetTriangles(NpcMeshes.GlowSubMesh).Length,
                Is.GreaterThan(0), "light bands");
        }

        [Test]
        public void A_residents_spec_follows_its_settlements_mix()
        {
            ArchMix pure = ArchMix.Pure(ArchRegion.Ice, ArchEra.Agrarian);
            ArchMix mixed = new ArchMix(ArchRegion.Lava, ArchEra.Industrial, 0.4f, 0.4f, 0.1f)
                .WithTertiary(ArchRegion.Storm, ArchEra.Future, 0.3f);
            int third = 0, total = 0;
            float shareSum = 0f;
            foreach (RunResident r in SampleResidents())
            {
                NpcSpec plain = NpcMeshes.SpecFor(r, ArchRegion.Ice, ArchEra.Agrarian, pure);
                Assert.That(plain.BorrowShare, Is.Zero);
                Assert.That((plain.Race, plain.Role, plain.Sex, plain.Seed), Is.EqualTo((r.Race, r.Role, r.Sex, r.Seed)));
                Assert.That(plain.Age01, Is.EqualTo(r.AgePermille / 1000f));
                NpcSpec spec = NpcMeshes.SpecFor(r, ArchRegion.Ice, ArchEra.Agrarian, mixed);
                Assert.That(spec.BorrowShare, Is.InRange(0f, 1f));
                bool isThird = spec.BorrowRegion == ArchRegion.Storm;
                Assert.That(spec.BorrowEra, Is.EqualTo(isThird ? ArchEra.Future : ArchEra.Industrial));
                Assert.That(isThird || spec.BorrowRegion == ArchRegion.Lava, Is.True);
                third += isThird ? 1 : 0;
                total++;
                shareSum += spec.BorrowShare;
            }

            Assert.That(total, Is.GreaterThan(100));
            Assert.That(third, Is.InRange(total * 15 / 100, total * 45 / 100), "about the tertiary share borrows from the third style");
            Assert.That(shareSum / total, Is.EqualTo(0.4f).Within(0.08f), "shares centre on the settlement's weight");
        }

        // Residents can only be made by the planner; the shipped database gives it a real planet type and wild pool.
        private static List<RunResident> SampleResidents()
        {
            var db = UnityEditor.AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            Assert.That(db, Is.Not.Null);
            var wild = new List<CreatureDef>();
            PlanetTypeDef type = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c && !c.LineId.StartsWith("starter_"))
                {
                    wild.Add(c);
                }
                else if (d is PlanetTypeDef t && (type == null || t.Id == "verdant_planet"))
                {
                    type = t;
                }
            }

            var vocab = new RunPlanVocabulary(new[] { "ruins", "forest" }, new[] { "affix_pack" }, new[] { "chain_a" });
            var all = new List<RunResident>();
            var planet = new PlanetConfig(640f, float3.zero);
            for (ulong seed = 0; seed < 6; seed++)
            {
                RunPlan plan = RunPlanner.Generate(planet, seed, type, CreatureStage.Adult, wild, vocab, null);
                RunLayout layout = SettlementPlanner.Generate(planet, plan, null);
                all.AddRange(PopulacePlanner.Generate(planet, plan, layout, default, null).Residents);
            }

            return all;
        }

        [Test]
        public void Eras_temper_age_and_health()
        {
            Assert.That(NpcMeshes.TemperFor(ArchEra.Wasteland).HealthShiftPermille,
                Is.LessThan(NpcMeshes.TemperFor(ArchEra.Future).HealthShiftPermille));
            Assert.That(NpcMeshes.TemperFor(ArchEra.Agrarian).AgeShiftPermille, Is.Zero);
            Assert.That(NpcMeshes.TemperFor(ArchEra.Ancient).HealthShiftPermille, Is.Zero);
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => Spec((NpcRace)99, NpcRole.None, ArchEra.Agrarian));
            Assert.Throws<ContractException>(() => Spec(NpcRace.Human, (NpcRole)99, ArchEra.Agrarian));
            Assert.Throws<ContractException>(() => Spec(NpcRace.Human, NpcRole.None, (ArchEra)42));
            Assert.Throws<ContractException>(() => Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, age: 1.1f));
            Assert.Throws<ContractException>(() => Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, health: -0.1f));
            Assert.Throws<ContractException>(() => Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, stature: 4f));
            Assert.Throws<ContractException>(() => Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, borrow: 1.5f));
            ArchMix pure = ArchMix.Pure(ArchRegion.Ice, ArchEra.Agrarian);
            Assert.Throws<ContractException>(() => pure.WithTertiary(ArchRegion.Lava, ArchEra.Future, 0.6f));
            Assert.Throws<ContractException>(() => pure.WithTertiary((ArchRegion)9, ArchEra.Future, 0.2f));
        }

        [Test]
        public void A_settlements_third_style_is_new_to_it_and_changes_only_some_buildings()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor("storm_planet", seed);
                ArchMix mix = ArchitectureMeshes.MixFor(planet, SettlementKind.City, "coast", seed, (int)(seed % 5));
                Assert.That(mix.TertiaryShare, Is.InRange(0.18f, 0.43f));
                Assert.That(mix.TertiaryRegion, Is.Not.EqualTo(planet.Region));
                Assert.That(mix.TertiaryRegion, Is.Not.EqualTo(mix.SecondaryRegion));
                Assert.That(mix.TertiaryEra, Is.Not.EqualTo(planet.Era));
                Assert.That(mix.TertiaryEra, Is.Not.EqualTo(mix.SecondaryEra));
                Assert.That(mix.TertiaryEra, Is.Not.EqualTo(ArchEra.Ancient));
            }

            var two = new ArchMix(ArchRegion.Lava, ArchEra.Industrial, 0.6f, 0.3f, 0.05f);
            ArchMix three = two.WithTertiary(ArchRegion.Twilight, ArchEra.Future, 0.5f);
            Assert.That((three.SecondaryRegion, three.SecondaryEra, three.Weight, three.AgeMean),
                Is.EqualTo((two.SecondaryRegion, two.SecondaryEra, two.Weight, two.AgeMean)));
            Assert.That(two.TertiaryShare, Is.Zero);
            int changed = 0;
            const int Count = 40;
            for (ulong seed = 0; seed < Count; seed++)
            {
                Mesh a = BuildHouse(new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Agrarian, 8f, 7f, 2, 0.2f, seed, two));
                Mesh b = BuildHouse(new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Agrarian, 8f, 7f, 2, 0.2f, seed, three));
                changed += a.vertexCount != b.vertexCount || a.bounds != b.bounds ? 1 : 0;
            }

            Assert.That(changed, Is.InRange(Count / 5, Count * 4 / 5), "about half the street borrows from the third style");
        }
    }
}
