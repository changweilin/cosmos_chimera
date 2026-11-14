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

        // Snapshot (testing.md): update only with a ScatterPlanner.GeneratorVersion change. The canopy digest predates
        // version 2 and must stay as it is: the understory layer was added without moving a single tree or rock.
        [Test]
        public void Snapshot_seed_12345()
        {
            Assert.That(ScatterPlanner.GeneratorVersion, Is.EqualTo(2), "new version: re-pin the digests below");
            float3 up = math.normalize(new float3(1, 1, 1));
            Assert.That(Digest(Planner(), up), Is.EqualTo(SnapshotDigest));
            Assert.That(Digest(Planner(), up, true), Is.EqualTo(UnderstoryDigest));
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
                var seen = new HashSet<int>();
                var near = new List<ScatterItem>();
                int checkedForCanopy = 0;
                for (long k = 6L * p.CellsPerFace * p.CellsPerFace; k < p.KeyCount; k += 5)
                {
                    if (!p.TryGetItem(k, out ScatterItem it)) continue;
                    seen.Add(it.KindIndex);
                    Assert.That(it.KindIndex, Is.GreaterThanOrEqualTo(p.CanopyKindCount));
                    ScatterKind kind = p.Kinds[it.KindIndex];
                    bool highland = it.AltitudeM >= ScatterPlanner.HighlandFractionOfRadius * Planet.Radius;
                    Assert.That(kind.Habitat, Is.Not.EqualTo(highland ? ScatterHabitat.Lowland : ScatterHabitat.Highland));
                    Assert.That(it.SizeM, Is.InRange(kind.MinSizeM, kind.MaxSizeM));
                    Assert.That(it.Blocks, Is.EqualTo(it.Shape != ScatterShape.TallGrass));
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

                Assert.That(seen.Count, Is.EqualTo(p.Kinds.Count - p.CanopyKindCount), type + ": every understory kind appears");
            }
        }

        [Test]
        public void Every_planet_type_has_an_understory_with_a_kind_for_any_altitude()
        {
            foreach (string type in new[] { "verdant_planet", "lava_planet", "ice_crystal_planet", "storm_planet",
                         "mineral_planet", "twilight_planet", null })
            {
                ScatterKind[] kinds = ScatterPlanner.UnderstoryKindsFor(type);
                Assert.That(System.Array.Exists(kinds, k => k.Habitat == ScatterHabitat.Any), Is.True, type);
                foreach (ScatterKind k in kinds)
                {
                    Assert.That(k.Shape >= ScatterShape.Shrub, Is.True, "understory shapes only");
                }

                var p = new ScatterPlanner(Planet, 1, type, Hills, null, null);
                Assert.That(p.Kinds.Count, Is.EqualTo(ScatterPlanner.KindsFor(type).Length + kinds.Length));
                Assert.That(p.KeyCount, Is.EqualTo(6L * p.CellsPerFace * p.CellsPerFace +
                                                    6L * p.UnderstoryCellsPerFace * p.UnderstoryCellsPerFace));
            }

            Assert.That(ScatterPlanner.UnderstoryKindsFor("no_such_planet"),
                Is.SameAs(ScatterPlanner.UnderstoryKindsFor("verdant_planet")));
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
            Assert.That(ScatterPlanner.KindsFor("ice_crystal_planet")[0].SubstanceId, Is.EqualTo("ice"));
            Assert.That(ScatterPlanner.KindsFor("no_such_planet"), Is.SameAs(ScatterPlanner.KindsFor("verdant_planet")));
            Assert.That(ScatterPlanner.KindsFor(null), Is.SameAs(ScatterPlanner.KindsFor("verdant_planet")));
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
        }
    }
}
