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
        public void Heads_vary_within_a_race_while_its_silhouette_holds()
        {
            NpcRace[] featured =
            {
                NpcRace.Elf, NpcRace.Dwarf, NpcRace.Goblin, NpcRace.Orc, NpcRace.Avian, NpcRace.Merfolk, NpcRace.Lizardfolk,
                NpcRace.Dragonkin, NpcRace.Zombie, NpcRace.Skeleton, NpcRace.Werewolf, NpcRace.Demon,
            };
            float humanBehind = -Build(Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian)).bounds.min.z;
            float humanWidth = Build(Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian)).bounds.size.x;
            foreach (NpcRace race in featured)
            {
                var heads = new HashSet<(int, int)>();
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    NpcSpec spec = Spec(race, NpcRole.None, ArchEra.Agrarian, seed: seed);
                    Mesh mesh = Build(spec);
                    AssertContracts(mesh, spec);
                    // Top of the head region, to the centimetre: horns, crests, ears and hair all change it.
                    heads.Add((mesh.vertexCount, Mathf.RoundToInt(mesh.bounds.max.y * 100f)));
                    if (race == NpcRace.Lizardfolk)
                    {
                        Assert.That(-mesh.bounds.min.z, Is.GreaterThan(humanBehind + 0.3f), "every lizardfolk keeps its tail");
                    }

                    if (race == NpcRace.Demon)
                    {
                        Assert.That(mesh.bounds.size.x, Is.GreaterThan(humanWidth + 0.3f), "every demon keeps its wings");
                    }
                }

                Assert.That(heads.Count, Is.GreaterThanOrEqualTo(5), $"{race} heads should not come from a single mould");
            }
        }

        [Test]
        public void Faces_and_builds_spread_around_the_race_mean_at_equal_height_and_weight()
        {
            // Same race, sex, age, height and weight: only the person's seed differs, so every difference below comes from
            // the per-person normal draws of face shape and muscle/fat split.
            var faces = new HashSet<int>();
            var depths = new List<float>();
            for (ulong seed = 1; seed <= 16; seed++)
            {
                NpcSpec spec = Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, seed: seed);
                Mesh mesh = Build(spec);
                AssertContracts(mesh, spec);
                float neck = NpcMeshes.HeightM(spec) * 0.82f;
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                foreach (Vector3 v in mesh.vertices)
                {
                    if (v.y > neck && v.z > 0f)
                    {
                        lo = Vector3.Min(lo, v);
                        hi = Vector3.Max(hi, v);
                    }
                }

                // Front half of the head to the millimetre: chin, nose, cheeks and brows all move it.
                faces.Add(Mathf.RoundToInt((hi.z - lo.z) * 1000f) * 1000 + Mathf.RoundToInt((neck - lo.y) * 1000f));
                depths.Add(mesh.bounds.size.z);
            }

            Assert.That(faces.Count, Is.GreaterThanOrEqualTo(12), "faces of one people should not repeat");
            depths.Sort();
            Assert.That(depths[depths.Count - 1] - depths[0], Is.GreaterThan(0.02f), "equal weight still splits into different builds");
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

        [Test]
        public void A_settlement_shares_one_culture_and_another_settlement_has_its_own()
        {
            ArchMix mixed = new ArchMix(ArchRegion.Lava, ArchEra.Industrial, 0.4f, 0.4f, 0.1f);
            var byHome = new Dictionary<int, ulong>();
            foreach (RunResident r in SampleResidents())
            {
                ulong culture = NpcMeshes.SpecFor(r, ArchRegion.Ice, ArchEra.Agrarian, mixed).CultureSeed;
                Assert.That(culture, Is.Not.Zero);
                if (byHome.TryGetValue(r.Settlement, out ulong seen))
                {
                    Assert.That(culture, Is.EqualTo(seen), "neighbours share their town's culture");
                }
                else
                {
                    byHome[r.Settlement] = culture;
                }
            }

            Assert.That(new HashSet<ulong>(byHome.Values).Count, Is.EqualTo(byHome.Count), "no two towns share a culture");
        }

        [Test]
        public void A_culture_changes_the_crowd_but_keeps_the_mesh_contracts()
        {
            // One person, many towns: the same seed must still dress differently under different cultures.
            var colours = new HashSet<Color32>();
            var sizes = new HashSet<int>();
            for (ulong culture = 1; culture <= 24; culture++)
            {
                NpcSpec baseSpec = Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian);
                var spec = new NpcSpec(baseSpec.Race, baseSpec.Role, baseSpec.Sex, baseSpec.Age01, baseSpec.Health01, baseSpec.StatureZ,
                    baseSpec.BulkZ, baseSpec.Region, baseSpec.Era, baseSpec.BorrowRegion, baseSpec.BorrowEra, baseSpec.BorrowShare,
                    baseSpec.Seed, 0.5f, culture);
                Mesh mesh = Build(spec);
                AssertContracts(mesh, spec);
                Color mean = Color.clear;
                foreach (Color c in mesh.colors)
                {
                    mean += c / mesh.vertexCount;
                }

                colours.Add(mean);
                sizes.Add(mesh.vertexCount);
            }

            Assert.That(colours.Count, Is.GreaterThan(8), "dye shifts reach the cloth");
            Assert.That(sizes.Count, Is.GreaterThan(3), "favoured cuts and hairstyles change the silhouette");
        }

        [Test]
        public void Builds_vary_in_proportion_not_only_in_size()
        {
            // Same height: a heavy and a lean frame of one stature differ in width far more than ±1 σ of girth alone would.
            NpcSpec lean = Spec(NpcRace.Human, NpcRole.Scholar, ArchEra.Agrarian, bulk: -1.5f);
            NpcSpec heavy = Spec(NpcRace.Human, NpcRole.Scholar, ArchEra.Agrarian, bulk: 1.5f);
            Assert.That(NpcMeshes.HeightM(heavy), Is.EqualTo(NpcMeshes.HeightM(lean)));
            Assert.That(Build(heavy).bounds.size.z, Is.GreaterThan(Build(lean).bounds.size.z * 1.05f));
        }

        private static NpcSpec WithHeritage(in NpcSpec s, NpcHeritage heritage, ulong seed) =>
            new NpcSpec(s.Race, s.Role, s.Sex, s.Age01, s.Health01, s.StatureZ, s.BulkZ, s.Region, s.Era, s.BorrowRegion, s.BorrowEra,
                s.BorrowShare, seed, s.Textile01, s.CultureSeed, heritage);

        [Test]
        public void Heritage_grows_rarer_with_every_added_line_and_keeps_its_invariants()
        {
            const int N = 6000;
            var blood = new int[NpcHeritage.MaxLines + 1];
            var culture = new int[NpcHeritage.MaxLines + 1];
            for (ulong seed = 0; seed < N; seed++)
            {
                NpcHeritage h = NpcMeshes.HeritageFor(NpcRace.Human, ArchRegion.Verdant, ArchEra.Agrarian, seed);
                blood[h.BloodLines]++;
                culture[h.CultureLines]++;
                Assert.That(h.BloodShareTotal, Is.LessThanOrEqualTo(NpcHeritage.MaxBloodShare + 1e-5f));
                Assert.That(h.CultureShareTotal, Is.LessThanOrEqualTo(NpcHeritage.MaxCultureShare + 1e-5f));
                var races = new HashSet<NpcRace>();
                for (int i = 0; i < h.BloodLines; i++)
                {
                    Assert.That(h.BloodShare(i), Is.GreaterThanOrEqualTo(1f / 16f - 1e-5f));
                    Assert.That(i == 0 || h.BloodShare(i) <= h.BloodShare(i - 1), Is.True, "strongest line first");
                    Assert.That(h.BloodRace(i), Is.Not.EqualTo(NpcRace.Human).And.Not.EqualTo(NpcRace.Zombie).And.Not.EqualTo(NpcRace.Skeleton));
                    Assert.That(races.Add(h.BloodRace(i)), Is.True, "each blood line once");
                }

                for (int i = 0; i < h.CultureLines; i++)
                {
                    Assert.That(h.CultureEra(i), Is.Not.EqualTo(ArchEra.Ancient));
                    Assert.That((h.CultureRegion(i), h.CultureEra(i)), Is.Not.EqualTo((ArchRegion.Verdant, ArchEra.Agrarian)));
                }

                Assert.That(NpcMeshes.HeritageFor(NpcRace.Skeleton, ArchRegion.Verdant, ArchEra.Agrarian, seed).BloodLines, Is.Zero,
                    "the undead carry no blood");
            }

            Assert.That((N - blood[0]) / (float)N, Is.EqualTo(NpcMeshes.MixedBloodOdds).Within(0.02f));
            Assert.That((N - culture[0]) / (float)N, Is.EqualTo(NpcMeshes.FamilyCultureOdds).Within(0.02f));
            for (int k = 1; k < NpcHeritage.MaxLines; k++)
            {
                Assert.That(blood[k + 1], Is.LessThan(blood[k]), $"{k + 1} blood lines rarer than {k}");
                Assert.That(culture[k + 1], Is.LessThan(culture[k]), $"{k + 1} family cultures rarer than {k}");
            }

            Assert.That(blood[NpcHeritage.MaxLines] + culture[NpcHeritage.MaxLines], Is.GreaterThan(0), "the rarest mixes still occur");
            Assert.That(NpcMeshes.HeritageFor(NpcRace.Elf, ArchRegion.Ice, ArchEra.Future, 7),
                Is.EqualTo(NpcMeshes.HeritageFor(NpcRace.Elf, ArchRegion.Ice, ArchEra.Future, 7)), "reproducible");
        }

        [Test]
        public void Mixed_blood_blends_the_frame_and_keeps_the_mesh_contracts_for_every_pair()
        {
            NpcSpec human = Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian);
            NpcSpec halfDwarf = WithHeritage(human, NpcHeritage.Pure.WithBlood(NpcRace.Dwarf, 0.5f), human.Seed);
            NpcSpec halfElf = WithHeritage(human, NpcHeritage.Pure.WithBlood(NpcRace.Elf, 0.5f), human.Seed);
            float pure = NpcMeshes.HeightM(human);
            Assert.That(NpcMeshes.HeightM(halfDwarf), Is.InRange(NpcMeshes.HeightM(Spec(NpcRace.Dwarf, NpcRole.None, ArchEra.Agrarian)), pure));
            Assert.That(NpcMeshes.HeightM(halfElf), Is.GreaterThan(pure));
            Assert.That(Build(halfElf).bounds, Is.Not.EqualTo(Build(human).bounds), "the elf line shows");

            foreach (NpcRace own in Enum.GetValues(typeof(NpcRace)))
            {
                if (own == NpcRace.Zombie || own == NpcRace.Skeleton)
                {
                    continue;
                }

                foreach (NpcRace other in Enum.GetValues(typeof(NpcRace)))
                {
                    if (other == own || other == NpcRace.Zombie || other == NpcRace.Skeleton)
                    {
                        continue;
                    }

                    NpcSpec spec = WithHeritage(Spec(own, NpcRole.None, ArchEra.Mercantile, sex: (NpcSex)((int)other & 1)),
                        NpcHeritage.Pure.WithBlood(other, 0.5f), (ulong)((int)own * 31 + (int)other));
                    AssertContracts(Build(spec), spec);
                }
            }

            NpcSpec three = WithHeritage(human, NpcHeritage.Pure.WithBlood(NpcRace.Orc, 0.25f).WithBlood(NpcRace.Avian, 0.15f)
                .WithBlood(NpcRace.Angel, 0.1f).WithCulture(ArchRegion.Lava, ArchEra.Future, 0.3f), 99);
            AssertContracts(Build(three), three);
        }

        [Test]
        public void A_family_culture_dresses_some_garments_in_its_own_era()
        {
            // Future light bands are the one garment trace a primal village never makes on its own.
            int lit = 0, plainLit = 0;
            const int Count = 40;
            for (ulong seed = 1; seed <= Count; seed++)
            {
                NpcSpec plain = Spec(NpcRace.Human, NpcRole.None, ArchEra.Primal, seed: seed);
                NpcSpec family = WithHeritage(plain, NpcHeritage.Pure.WithCulture(ArchRegion.Crystal, ArchEra.Future, 0.6f), seed);
                plainLit += Build(plain).GetTriangles(NpcMeshes.GlowSubMesh).Length > 0 ? 1 : 0;
                lit += Build(family).GetTriangles(NpcMeshes.GlowSubMesh).Length > 0 ? 1 : 0;
            }

            Assert.That(plainLit, Is.Zero);
            Assert.That(lit, Is.InRange(Count / 5, Count - 1), "some garments, not the whole wardrobe");
        }

        [Test]
        public void All_six_face_shapes_occur_and_lean_with_sex_fat_and_race()
        {
            int Count(NpcRace race, NpcSex sex, float bulk, NpcMeshes.FaceShape shape)
            {
                int n = 0;
                for (ulong seed = 0; seed < 400; seed++)
                {
                    n += NpcMeshes.ShapeOf(Spec(race, NpcRole.None, ArchEra.Agrarian, sex: sex, bulk: bulk, seed: seed)) == shape ? 1 : 0;
                }

                return n;
            }

            foreach (NpcMeshes.FaceShape shape in Enum.GetValues(typeof(NpcMeshes.FaceShape)))
            {
                Assert.That(Count(NpcRace.Human, NpcSex.Female, 0f, shape) + Count(NpcRace.Human, NpcSex.Male, 0f, shape), Is.GreaterThan(10),
                    $"{shape} faces occur");
            }

            Assert.That(Count(NpcRace.Human, NpcSex.Male, 0f, NpcMeshes.FaceShape.Oval),
                Is.GreaterThan(Count(NpcRace.Human, NpcSex.Male, 0f, NpcMeshes.FaceShape.Triangle)));
            Assert.That(Count(NpcRace.Human, NpcSex.Male, 0f, NpcMeshes.FaceShape.Square),
                Is.GreaterThan(Count(NpcRace.Human, NpcSex.Female, 0f, NpcMeshes.FaceShape.Square)));
            Assert.That(Count(NpcRace.Human, NpcSex.Male, 3f, NpcMeshes.FaceShape.Round),
                Is.GreaterThan(Count(NpcRace.Human, NpcSex.Male, -3f, NpcMeshes.FaceShape.Round)));
            Assert.That(Count(NpcRace.Orc, NpcSex.Male, 0f, NpcMeshes.FaceShape.Square),
                Is.GreaterThan(Count(NpcRace.Elf, NpcSex.Male, 0f, NpcMeshes.FaceShape.Square)));
        }

        [Test]
        public void Face_shapes_are_the_heads_own_outline()
        {
            // Mean half widths (temple, cheekbone, jaw, chin) and chin-to-crown height per shape over many people: each
            // shape must hold its defining proportion on average while individuals still vary around it.
            var sum = new Dictionary<NpcMeshes.FaceShape, (int n, float temple, float cheek, float jaw, float chin, float height)>();
            for (ulong seed = 0; seed < 1500; seed++)
            {
                var head = NpcMeshes.HeadOf(Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, sex: (NpcSex)(seed & 1), seed: seed));
                Assert.That(head.temple > 0f && head.cheek > 0f && head.jaw > 0f && head.chin > 0f, Is.True);
                sum.TryGetValue(head.shape, out var s);
                sum[head.shape] = (s.n + 1, s.temple + head.temple, s.cheek + head.cheek, s.jaw + head.jaw, s.chin + head.chin, s.height + head.height);
            }

            (float temple, float cheek, float jaw, float chin, float height) Mean(NpcMeshes.FaceShape shape)
            {
                var s = sum[shape];
                return (s.temple / s.n, s.cheek / s.n, s.jaw / s.n, s.chin / s.n, s.height / s.n);
            }

            var oval = Mean(NpcMeshes.FaceShape.Oval);
            var round = Mean(NpcMeshes.FaceShape.Round);
            var longFace = Mean(NpcMeshes.FaceShape.Long);
            var square = Mean(NpcMeshes.FaceShape.Square);
            var triangle = Mean(NpcMeshes.FaceShape.Triangle);
            var heart = Mean(NpcMeshes.FaceShape.InvertedTriangle);
            Assert.That(longFace.height, Is.GreaterThan(oval.height * 1.08f), "long: taller face");
            Assert.That(round.height, Is.LessThan(oval.height * 0.95f), "round: shorter face");
            Assert.That(round.jaw / round.height, Is.GreaterThan(longFace.jaw / longFace.height * 1.2f), "round: wide for its length");
            Assert.That(square.jaw / square.cheek, Is.GreaterThan(oval.jaw / oval.cheek * 1.12f), "square: the jaw keeps the cheek's width");
            Assert.That(square.chin, Is.GreaterThan(oval.chin * 1.25f), "square: broad chin");
            Assert.That(triangle.jaw, Is.GreaterThan(triangle.temple), "triangle: jaw wider than the brow");
            Assert.That(heart.temple, Is.GreaterThan(heart.jaw * 1.3f), "inverted triangle: brow far wider than the jaw");
            Assert.That(heart.chin, Is.LessThan(oval.chin * 0.75f), "inverted triangle: pointed chin");
        }

        [Test]
        public void Eyes_sit_in_the_face_instead_of_standing_out_of_it()
        {
            // Whites and iris are told apart by hue ratio: facet shading scales a vertex colour, never its ratios.
            static bool Like(Color c, int rgb)
            {
                float r = ((rgb >> 16) & 0xFF) / 255f, g = ((rgb >> 8) & 0xFF) / 255f, b = (rgb & 0xFF) / 255f;
                return c.r > 1e-3f && math.abs(c.g / c.r - g / r) < 0.004f && math.abs(c.b / c.r - b / r) < 0.004f;
            }

            int[] iris = { 0x2A1A12, 0x3E2616, 0x5A4022, 0x3A5A7A, 0x3E6A44, 0x5A6066 };
            for (ulong seed = 1; seed <= 8; seed++)
            {
                NpcSpec spec = Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian, seed: seed);
                Mesh mesh = Build(spec);
                float h = NpcMeshes.HeightM(spec), whiteLo = float.MaxValue, whiteHi = float.MinValue, irisHi = float.MinValue;
                Vector3[] v = mesh.vertices;
                Color[] c = mesh.colors;
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i].y < h * 0.82f || v[i].z <= 0f)
                    {
                        continue;
                    }

                    if (Like(c[i], 0xEEE2CF))
                    {
                        whiteLo = math.min(whiteLo, v[i].z);
                        whiteHi = math.max(whiteHi, v[i].z);
                    }
                    else if (Array.Exists(iris, t => Like(c[i], t)))
                    {
                        irisHi = math.max(irisHi, v[i].z);
                    }
                }

                Assert.That(whiteHi, Is.GreaterThan(whiteLo), "whites found");
                Assert.That(irisHi, Is.GreaterThan(float.MinValue), "iris found");
                Assert.That(irisHi - whiteHi, Is.InRange(-0.001f, 0.0025f), "the iris is painted on the eye, not a bead in front of it");
                Assert.That(whiteHi - whiteLo, Is.LessThan(0.014f * h), "whites are shallow lenses, not marbles");
            }
        }

        [Test]
        public void Weight_and_fat_reshape_the_limbs_not_only_the_waist()
        {
            // Two people of one height: the heavier one's upper arms and thighs widen the figure more than its joints do.
            NpcSpec lean = Spec(NpcRace.Human, NpcRole.Scholar, ArchEra.Primal, sex: NpcSex.Female, bulk: -2.5f, seed: 5);
            NpcSpec heavy = Spec(NpcRace.Human, NpcRole.Scholar, ArchEra.Primal, sex: NpcSex.Female, bulk: 2.5f, seed: 5);
            Mesh a = Build(lean), b = Build(heavy);
            AssertContracts(a, lean);
            AssertContracts(b, heavy);
            Assert.That(b.bounds.size.x, Is.GreaterThan(a.bounds.size.x * 1.08f));
            Assert.That(b.bounds.size.z, Is.GreaterThan(a.bounds.size.z * 1.05f));
        }

        [Test]
        public void Heritage_contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => NpcHeritage.Pure.WithBlood(NpcRace.Elf, 0.6f));
            Assert.Throws<ContractException>(() => NpcHeritage.Pure.WithBlood(NpcRace.Elf, 0.2f).WithBlood(NpcRace.Elf, 0.1f));
            Assert.Throws<ContractException>(() => NpcHeritage.Pure.WithBlood(NpcRace.Elf, 0f));
            Assert.Throws<ContractException>(() => NpcHeritage.Pure.WithCulture(ArchRegion.Ice, ArchEra.Future, 0.7f));
            Assert.Throws<ContractException>(() => NpcHeritage.Pure.WithBlood(NpcRace.Elf, 0.1f).WithBlood(NpcRace.Orc, 0.1f)
                .WithBlood(NpcRace.Dwarf, 0.1f).WithBlood(NpcRace.Angel, 0.1f));
            Assert.Throws<ContractException>(() => NpcHeritage.Pure.BloodRace(0));
            NpcSpec human = Spec(NpcRace.Human, NpcRole.None, ArchEra.Agrarian);
            Assert.Throws<ContractException>(() => WithHeritage(human, NpcHeritage.Pure.WithBlood(NpcRace.Human, 0.2f), 1));
            Assert.Throws<ContractException>(() => NpcMeshes.HeritageFor((NpcRace)99, ArchRegion.Ice, ArchEra.Future, 1));
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
