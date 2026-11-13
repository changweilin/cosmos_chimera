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

        private static ulong Digest(ScatterPlanner p, float3 up)
        {
            var cells = new HashSet<long>();
            p.CollectCells(up, 90f, cells);
            var keys = new List<long>(cells);
            keys.Sort();
            ulong h = 14695981039346656037ul;
            foreach (long k in keys)
            {
                if (!p.TryGetItem(k, out ScatterItem it)) continue;
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
            Assert.That(Digest(a, up), Is.EqualTo(Digest(a, up)), "cached answers equal fresh ones");
        }

        // Snapshot (testing.md): placement moved here from ScatterField unchanged; update only with a generator change.
        [Test]
        public void Snapshot_seed_12345()
        {
            Assert.That(Digest(Planner(), math.normalize(new float3(1, 1, 1))), Is.EqualTo(SnapshotDigest));
        }

        private const ulong SnapshotDigest = 1236021966666354270ul;

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
            Assert.Throws<ContractException>(() => p.TryGetItem(6L * p.CellsPerFace * p.CellsPerFace, out _));
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
