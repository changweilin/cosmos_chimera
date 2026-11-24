using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: planet-world.md#奇觀地標 — wonders are built in code, identically on every client, and stand on the
    // footprint circle the rule layer blocks.
    public sealed class LandmarkMeshesTests
    {
        private static readonly ArchEra[] Eras = { ArchEra.Agrarian, ArchEra.Industrial, ArchEra.Steampunk, ArchEra.Future, ArchEra.Ancient };
        private readonly List<Mesh> _meshes = new List<Mesh>();

        private static (float footprint, float height) SizeOf(LandmarkKind kind) => kind switch
        {
            LandmarkKind.Tower => (6f, 40f),
            LandmarkKind.Temple => (14f, 14f),
            LandmarkKind.SkyIsland => (18f, 16f),
            LandmarkKind.SpaceElevator => (9f, 480f),
            LandmarkKind.SpaceStation => (0f, 24f),
            LandmarkKind.Labyrinth => (16f, 11f),
            LandmarkKind.MineShaft => (7f, 10f),
            LandmarkKind.ComputeCenter => (11f, 18f),
            LandmarkKind.GiantTree => (9f, 36f),
            LandmarkKind.TrenchMouth => (10f, 5f),
            LandmarkKind.Crater => (16f, 13f),
            LandmarkKind.IceCastle => (13f, 23f),
            _ => (7f, 8f),
        };

        private static LandmarkSpec Spec(LandmarkKind kind, int variant, ArchEra era, ArchRegion region = ArchRegion.Verdant,
            float age = 0.4f, ulong seed = 11)
        {
            (float footprint, float height) = SizeOf(kind);
            return new LandmarkSpec(kind, variant, region, era, age, footprint, height, seed);
        }

        private static IEnumerable<TestCaseData> Every()
        {
            foreach (LandmarkKind kind in Enum.GetValues(typeof(LandmarkKind)))
            {
                for (int v = 0; v < LandmarkPlanner.VariantCount(kind); v++)
                {
                    foreach (ArchEra era in Eras)
                    {
                        yield return new TestCaseData(kind, v, era).SetName($"{kind}_{v}_{era}");
                    }
                }
            }
        }

        private Mesh Build(in LandmarkSpec spec)
        {
            Mesh mesh = LandmarkMeshes.Build(spec);
            _meshes.Add(mesh);
            return mesh;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Mesh mesh in _meshes) UnityEngine.Object.DestroyImmediate(mesh);
            _meshes.Clear();
        }

        [TestCaseSource(nameof(Every))]
        public void Every_spec_builds_deterministically_and_keeps_every_mesh_contract(LandmarkKind kind, int variant, ArchEra era)
        {
            LandmarkSpec spec = Spec(kind, variant, era);
            Mesh a = Build(spec);
            Mesh b = Build(spec);

            Vector3[] vertices = a.vertices;
            Vector3[] normals = a.normals;
            Color[] colors = a.colors;
            Assert.That(b.vertices, Is.EqualTo(vertices));
            Assert.That(b.colors, Is.EqualTo(colors));
            Assert.That(b.triangles, Is.EqualTo(a.triangles));

            Assert.That(vertices.Length, Is.GreaterThan(0).And.LessThan(65536), "fits a 16-bit index buffer");
            Assert.That(normals.Length, Is.EqualTo(vertices.Length));
            Assert.That(colors.Length, Is.EqualTo(vertices.Length));
            Assert.That(a.subMeshCount, Is.EqualTo(ArchitectureMeshes.SubMeshCount));
            Assert.That(a.GetTriangles(ArchitectureMeshes.GlowSubMesh).Length, Is.GreaterThan(0), "every wonder carries a light");
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.That(float.IsFinite(vertices[i].x + vertices[i].y + vertices[i].z), Is.True, $"vertex {i}");
                Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(1e-3f), $"normal {i}");
                Assert.That(colors[i].a, Is.InRange(0f, 1f), $"accent mask {i}");
            }

            if (kind != LandmarkKind.SkyIsland && kind != LandmarkKind.SpaceStation)
            {
                float reachLow = 0f, top = float.MinValue, bottom = float.MaxValue;
                foreach (Vector3 p in vertices)
                {
                    top = Mathf.Max(top, p.y);
                    bottom = Mathf.Min(bottom, p.y);
                    if (p.y < 2f)
                    {
                        reachLow = Mathf.Max(reachLow, new Vector2(p.x, p.z).magnitude);
                    }
                }

                // Past the circle a wall would look solid where the character can walk; far short of it, the reverse.
                Assert.That(reachLow, Is.InRange(spec.FootprintRadiusM * 0.85f, spec.FootprintRadiusM * 1.03f));
                Assert.That(bottom, Is.EqualTo(-LandmarkMeshes.SinkM).Within(0.01f), "sunk base hides the foot on slopes");
                Assert.That(top, Is.InRange(spec.HeightM * 0.6f, spec.HeightM + 4f));
                Assert.That(LandmarkMeshes.BodyHeightM(spec), Is.GreaterThan(0f).And.LessThanOrEqualTo(spec.HeightM));
            }
        }

        [Test]
        public void Floating_wonders_are_built_around_their_origin()
        {
            foreach (LandmarkKind kind in new[] { LandmarkKind.SkyIsland, LandmarkKind.SpaceStation })
            {
                for (int v = 0; v < LandmarkPlanner.VariantCount(kind); v++)
                {
                    Bounds b = TightBounds(Build(Spec(kind, v, ArchEra.Future)));
                    Assert.That(b.min.y, Is.LessThan(-1f), kind + " hangs below its origin");
                    Assert.That(b.max.y, Is.GreaterThan(1f), kind + " rises above its origin");
                    Assert.That(Mathf.Abs(b.center.x) + Mathf.Abs(b.center.z), Is.LessThan(b.size.x * 0.5f), kind + " is centred");
                }
            }
        }

        [Test]
        public void Variants_eras_regions_and_seeds_change_the_shape()
        {
            Vector3[] baseline = Build(Spec(LandmarkKind.Tower, 0, ArchEra.Agrarian)).vertices;
            Assert.That(Build(Spec(LandmarkKind.Tower, 1, ArchEra.Agrarian)).vertices, Is.Not.EqualTo(baseline), "function");
            Assert.That(Build(Spec(LandmarkKind.Tower, 0, ArchEra.Future)).vertices, Is.Not.EqualTo(baseline), "era");
            Assert.That(Build(Spec(LandmarkKind.Tower, 0, ArchEra.Agrarian, ArchRegion.Ice)).vertices, Is.Not.EqualTo(baseline), "region");
            Assert.That(Build(Spec(LandmarkKind.Tower, 0, ArchEra.Agrarian, seed: 12)).colors,
                Is.Not.EqualTo(Build(Spec(LandmarkKind.Tower, 0, ArchEra.Agrarian)).colors), "seed");
        }

        [Test]
        public void Age_spreads_weathering_over_the_walls()
        {
            float Weathered(float age)
            {
                Mesh mesh = Build(Spec(LandmarkKind.Temple, 0, ArchEra.Mercantile, age: age));
                Color[] colors = mesh.colors;
                float sum = 0f;
                int count = 0;
                foreach (int i in mesh.GetTriangles(ArchitectureMeshes.WallSubMesh))
                {
                    sum += 1f - colors[i].a;
                    count++;
                }

                return sum / count;
            }

            Assert.That(Weathered(1f), Is.GreaterThan(Weathered(0f) + 0.1f));
        }

        [Test]
        public void Palette_follows_region_and_era_and_sky_islands_are_rock_and_turf()
        {
            ArchPalette verdant = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.Tower, 0, ArchEra.Mercantile));
            ArchPalette lava = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.Tower, 0, ArchEra.Mercantile, ArchRegion.Lava));
            ArchPalette future = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.Tower, 0, ArchEra.Future));
            Assert.That(lava.Wall, Is.Not.EqualTo(verdant.Wall));
            Assert.That(future.Wall, Is.Not.EqualTo(verdant.Wall));
            ArchPalette island = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.SkyIsland, 0, ArchEra.Mercantile));
            Assert.That(island.Roof, Is.EqualTo(verdant.Weathering), "turf is the region's growth colour");
            ArchPalette old = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.Tower, 0, ArchEra.Mercantile, age: 1f));
            Assert.That(old.WeatheringStrength, Is.GreaterThanOrEqualTo(verdant.WeatheringStrength));
        }

        [Test]
        public void Nature_entrances_read_by_their_material()
        {
            ArchPalette tree = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.GiantTree, 0, ArchEra.Mercantile));
            Assert.That(tree.Roof.g, Is.GreaterThan(tree.Roof.r).And.GreaterThan(tree.Roof.b), "leaves are green");
            ArchPalette crater = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.Crater, 0, ArchEra.Future, ArchRegion.Ice));
            Assert.That(crater.Glow.r, Is.GreaterThan(crater.Glow.b), "lava glows warm even on an ice world");
            ArchPalette ice = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.IceCastle, 0, ArchEra.Mercantile, ArchRegion.Lava));
            Assert.That(ice.Wall.b, Is.GreaterThan(ice.Wall.r), "ice stays cold on a lava world");
            ArchPalette trench = LandmarkMeshes.PaletteFor(Spec(LandmarkKind.TrenchMouth, 0, ArchEra.Mercantile));
            Assert.That(trench.Glow.b, Is.GreaterThan(trench.Glow.r), "the deep glows blue");
        }

        [Test]
        public void Spec_for_a_landmark_is_stable_and_pairs_the_station_with_its_elevator()
        {
            ArchPlanetStyle style = ArchitectureMeshes.PlanetStyleFor("storm_planet", 5);
            var eras = new HashSet<ArchEra>();
            for (ulong seed = 0; seed < 200; seed++)
            {
                // Hand-made landmarks: SpecFor reads only the fields below.
                LandmarkSpec elevator = default, station = default;
                for (int i = 0; i < 4; i++)
                {
                    LandmarkKind kind = i == 0 ? LandmarkKind.SpaceElevator : i == 1 ? LandmarkKind.SpaceStation : LandmarkKind.Tower;
                    (float footprint, float height) = SizeOf(kind);
                    var l = new RunLandmark(i, kind, 0, 0, new float3(0f, 1f, 0f), new float3(0f, 0f, 1f), footprint, height, 0f, true);
                    LandmarkSpec a = LandmarkMeshes.SpecFor(style, l, seed), b = LandmarkMeshes.SpecFor(style, l, seed);
                    Assert.That(a.Era, Is.EqualTo(b.Era));
                    Assert.That(a.Age, Is.EqualTo(b.Age).And.InRange(0f, 1f));
                    Assert.That(a.Seed, Is.EqualTo(b.Seed));
                    Assert.That(a.Region, Is.EqualTo(ArchRegion.Storm));
                    Assert.That(a.Kind, Is.EqualTo(kind));
                    if (i == 0) elevator = a;
                    else if (i == 1) station = a;
                    else eras.Add(a.Era);
                }

                Assert.That(station.Era, Is.EqualTo(elevator.Era), "one builder for the tether and what hangs on it");
                Assert.That(elevator.Era == ArchEra.Future || elevator.Era == ArchEra.Ancient, Is.True);
            }

            Assert.That(eras.Contains(style.Era) && eras.Contains(ArchEra.Ancient), Is.True);
            Assert.That(eras.Count, Is.GreaterThanOrEqualTo(4), "some wonders come from unrelated eras");
        }

        [Test]
        public void Bad_specs_violate_the_contract()
        {
            Assert.Throws<ContractException>(() => new LandmarkSpec(LandmarkKind.Tower, 6, ArchRegion.Verdant, ArchEra.Future, 0.5f, 6f, 30f, 1));
            Assert.Throws<ContractException>(() => new LandmarkSpec(LandmarkKind.Tower, -1, ArchRegion.Verdant, ArchEra.Future, 0.5f, 6f, 30f, 1));
            Assert.Throws<ContractException>(() => new LandmarkSpec(LandmarkKind.Tower, 0, ArchRegion.Verdant, ArchEra.Future, 1.5f, 6f, 30f, 1));
            Assert.Throws<ContractException>(() => new LandmarkSpec(LandmarkKind.Tower, 0, ArchRegion.Verdant, ArchEra.Future, 0.5f, 6f, 0f, 1));
            Assert.Throws<ContractException>(() => new LandmarkSpec((LandmarkKind)99, 0, ArchRegion.Verdant, ArchEra.Future, 0.5f, 6f, 30f, 1));
        }

        // mesh.bounds is padded for wind sway (SceneryMeshes.MeshData.ToMesh); measure the vertices themselves.
        private static Bounds TightBounds(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 v in vertices) bounds.Encapsulate(v);
            return bounds;
        }
    }
}
