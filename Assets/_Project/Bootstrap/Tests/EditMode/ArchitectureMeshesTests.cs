using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: planet-world.md#道路與建築; vision.md#畫風與沉浸感 — buildings are built in code, identically on every client.
    public sealed class ArchitectureMeshesTests
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();

        // Footprints and storeys as SettlementPlanner.Footprint hands them out.
        private static ArchSpec Spec(BuildingKind kind, ArchRegion region, ArchEra era, ulong seed = 7, float foundation = 0.6f) => kind switch
        {
            BuildingKind.Tent => new ArchSpec(kind, region, era, 3f, 3f, 1, foundation, seed),
            BuildingKind.Hall => new ArchSpec(kind, region, era, 7f, 6f, 2, foundation, seed),
            BuildingKind.Tower => new ArchSpec(kind, region, era, 3f, 3f, 4, foundation, seed),
            BuildingKind.Pillar => new ArchSpec(kind, region, era, 1.2f, 1.2f, 3, foundation, seed),
            _ => new ArchSpec(kind, region, era, 5f, 4f, 2, foundation, seed),
        };

        private static IEnumerable<TestCaseData> Every()
        {
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
            {
                foreach (ArchRegion region in Enum.GetValues(typeof(ArchRegion)))
                {
                    foreach (ArchEra era in Enum.GetValues(typeof(ArchEra)))
                    {
                        yield return new TestCaseData(kind, region, era).SetName($"{kind}_{region}_{era}");
                    }
                }
            }
        }

        private Mesh Build(in ArchSpec spec)
        {
            Mesh mesh = ArchitectureMeshes.Build(spec);
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
        public void Same_spec_builds_the_same_mesh(BuildingKind kind, ArchRegion region, ArchEra era)
        {
            Mesh a = Build(Spec(kind, region, era));
            Mesh b = Build(Spec(kind, region, era));
            Assert.That(b.vertices, Is.EqualTo(a.vertices));
            Assert.That(b.normals, Is.EqualTo(a.normals));
            Assert.That(b.colors, Is.EqualTo(a.colors));
            for (int s = 0; s < a.subMeshCount; s++)
            {
                Assert.That(b.GetTriangles(s), Is.EqualTo(a.GetTriangles(s)));
            }
        }

        [TestCaseSource(nameof(Every))]
        public void Mesh_is_well_formed_and_keeps_its_material_slots(BuildingKind kind, ArchRegion region, ArchEra era)
        {
            Mesh mesh = Build(Spec(kind, region, era));
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Color[] colors = mesh.colors;
            Assert.That(vertices.Length, Is.GreaterThan(0).And.LessThan(65536), "fits a 16-bit index buffer");
            Assert.That(normals.Length, Is.EqualTo(vertices.Length));
            Assert.That(colors.Length, Is.EqualTo(vertices.Length));
            Assert.That(mesh.subMeshCount, Is.EqualTo(ArchitectureMeshes.SubMeshCount));
            Assert.That(mesh.GetTriangles(ArchitectureMeshes.WallSubMesh).Length, Is.GreaterThan(0), "every building has walls");
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.That(float.IsFinite(vertices[i].x + vertices[i].y + vertices[i].z), Is.True, $"vertex {i}");
                Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(1e-3f), $"normal {i}");
                Assert.That(colors[i].a, Is.InRange(0f, 1f), $"weathering mask {i}");
                Assert.That(colors[i].r, Is.InRange(0.2f, 1.3f), $"tone {i}");
            }
        }

        [TestCaseSource(nameof(Every))]
        public void Mesh_stands_on_its_lot_and_covers_its_collider(BuildingKind kind, ArchRegion region, ArchEra era)
        {
            ArchSpec spec = Spec(kind, region, era, foundation: 1.4f);
            Mesh mesh = Build(spec);
            Vector3 body = ArchitectureMeshes.BodySize(spec);
            Assert.That(body.x, Is.LessThanOrEqualTo(spec.WidthM), "never wider than the rule layer's footprint");
            Assert.That(body.z, Is.LessThanOrEqualTo(spec.DepthM));
            Assert.That(body.y, Is.GreaterThan(spec.FoundationM));

            float minY = float.MaxValue, maxY = float.MinValue, reachX = 0f, reachZ = 0f;
            foreach (Vector3 v in mesh.vertices)
            {
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
                reachX = Mathf.Max(reachX, Mathf.Abs(v.x));
                reachZ = Mathf.Max(reachZ, Mathf.Abs(v.z));
            }

            Assert.That(minY, Is.LessThan(0f), "walls start below the origin, so a slope never shows under them");
            Assert.That(minY, Is.GreaterThanOrEqualTo(-0.5f - 1e-3f));
            Assert.That(maxY, Is.GreaterThanOrEqualTo(body.y - 1e-3f), "nothing invisible blocks the character from above");
            Assert.That(reachX, Is.LessThanOrEqualTo(spec.WidthM * 0.5f + ArchitectureMeshes.MaxOverhangM));
            Assert.That(reachZ, Is.LessThanOrEqualTo(spec.DepthM * 0.5f + ArchitectureMeshes.MaxOverhangM));
            Assert.That(reachX, Is.GreaterThan(spec.WidthM * 0.25f), "the mesh fills its lot");
        }

        [TestCaseSource(nameof(Every))]
        public void Winding_agrees_with_the_shading_normal(BuildingKind kind, ArchRegion region, ArchEra era)
        {
            Mesh mesh = Build(Spec(kind, region, era));
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int total = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    Assert.That(face.sqrMagnitude, Is.GreaterThan(1e-12f), "no sliver triangles");
                    Assert.That(Vector3.Dot(face, normals[triangles[t]]), Is.GreaterThan(0f), $"sub-mesh {s} triangle {t / 3}");
                    total++;
                }
            }

            Assert.That(total, Is.GreaterThan(20));
        }

        [TestCaseSource(nameof(Every))]
        public void Outer_shell_faces_away_from_the_building(BuildingKind kind, ArchRegion region, ArchEra era)
        {
            // An inside-out face is culled and leaves a hole. Walls far from the axis and well above the footing must
            // look outward; trim, glow and roof undersides legitimately face every way, so only walls are judged.
            ArchSpec spec = Spec(kind, region, era);
            if (ArchitectureMeshes.IsRuin(spec))
            {
                Assert.Pass("a ruin's free-standing walls show their inner faces too");
            }

            Mesh mesh = Build(spec);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.GetTriangles(ArchitectureMeshes.WallSubMesh);
            int outward = 0, judged = 0;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a).normalized;
                Vector3 centroid = (a + b + c) / 3f;
                var radial = new Vector3(centroid.x, 0f, centroid.z);
                if (Mathf.Abs(face.y) > 0.5f || radial.magnitude < 0.35f)
                {
                    continue;
                }

                judged++;
                if (Vector3.Dot(face, radial.normalized) > -0.2f) outward++;
            }

            Assert.That(judged, Is.GreaterThan(4));
            Assert.That(outward, Is.GreaterThanOrEqualTo(judged * 0.6f), "most upright wall faces look away from the axis");
        }

        [Test]
        public void Seed_region_and_era_each_change_the_mesh()
        {
            Mesh basis = Build(Spec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Mercantile));
            Assert.That(Build(Spec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Mercantile, seed: 8)).vertices, Is.Not.EqualTo(basis.vertices));
            Assert.That(Build(Spec(BuildingKind.House, ArchRegion.Twilight, ArchEra.Mercantile)).vertices, Is.Not.EqualTo(basis.vertices));
            Assert.That(Build(Spec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Industrial)).vertices, Is.Not.EqualTo(basis.vertices));
        }

        [Test]
        public void Lived_in_buildings_are_lit_and_roofed_and_ruins_are_neither()
        {
            foreach (ArchRegion region in Enum.GetValues(typeof(ArchRegion)))
            {
                Mesh town = Build(Spec(BuildingKind.House, region, ArchEra.Mercantile));
                Assert.That(town.GetTriangles(ArchitectureMeshes.RoofSubMesh).Length, Is.GreaterThan(0), region + " roof");
                Assert.That(town.GetTriangles(ArchitectureMeshes.GlowSubMesh).Length, Is.GreaterThan(0), region + " door lamp");

                ArchSpec ruinSpec = Spec(BuildingKind.House, region, ArchEra.Ancient);
                Assert.That(ArchitectureMeshes.IsRuin(ruinSpec), Is.True);
                Mesh ruin = Build(ruinSpec);
                Assert.That(ruin.GetTriangles(ArchitectureMeshes.RoofSubMesh).Length, Is.EqualTo(0), region + " ruin has no roof");
                Assert.That(ruin.GetTriangles(ArchitectureMeshes.GlowSubMesh).Length, Is.EqualTo(0), region + " ruin is dark");
                Assert.That(ruin.bounds.max.y, Is.LessThan(town.bounds.max.y), region + " ruin is lower than the house it was");
            }
        }

        [Test]
        public void A_megalith_glows_and_is_never_a_ruin()
        {
            ArchSpec spec = Spec(BuildingKind.Pillar, ArchRegion.Lava, ArchEra.Ancient);
            Assert.That(ArchitectureMeshes.IsRuin(spec), Is.False);
            Assert.That(Build(spec).GetTriangles(ArchitectureMeshes.GlowSubMesh).Length, Is.GreaterThan(0));
            Assert.That(ArchitectureMeshes.BodySize(spec).y, Is.EqualTo(spec.FoundationM + 3 * ArchitectureMeshes.StoreyM).Within(1e-4f));
        }

        [Test]
        public void Every_settlement_kind_and_planet_type_maps_to_a_style()
        {
            foreach (SettlementKind kind in Enum.GetValues(typeof(SettlementKind)))
            {
                Assert.That(Enum.IsDefined(typeof(ArchEra), ArchitectureMeshes.EraFor(kind)), Is.True, kind.ToString());
            }

            Assert.That(ArchitectureMeshes.EraFor(SettlementKind.Ruins), Is.EqualTo(ArchEra.Ancient));
            var seen = new HashSet<ArchRegion>();
            foreach (string id in new[] { "verdant_planet", "lava_planet", "ice_crystal_planet", "storm_planet", "mineral_planet", "twilight_planet" })
            {
                seen.Add(ArchitectureMeshes.RegionFor(id));
            }

            Assert.That(seen.Count, Is.EqualTo(6), "each planet type has its own tradition");
            Assert.That(ArchitectureMeshes.RegionFor(null), Is.EqualTo(ArchRegion.Verdant));
            Assert.That(ArchitectureMeshes.RegionFor("unknown"), Is.EqualTo(ArchRegion.Verdant));
        }

        [Test]
        public void Palettes_are_opaque_and_distinct_per_region()
        {
            var roofs = new HashSet<Color>();
            foreach (ArchRegion region in Enum.GetValues(typeof(ArchRegion)))
            {
                foreach (ArchEra era in Enum.GetValues(typeof(ArchEra)))
                {
                    ArchPalette p = ArchitectureMeshes.PaletteFor(region, era, BuildingKind.House);
                    Assert.That(p.Wall.a * p.Roof.a * p.Trim.a * p.Glow.a * p.Weathering.a, Is.EqualTo(1f));
                    Assert.That(p.WeatheringStrength, Is.InRange(0f, 1f));
                }

                roofs.Add(ArchitectureMeshes.PaletteFor(region, ArchEra.Mercantile, BuildingKind.House).Roof);
            }

            Assert.That(roofs.Count, Is.EqualTo(6));
        }

        [Test]
        public void Out_of_range_spec_violates_the_contract()
        {
            Assert.Throws<ContractException>(() => new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Agrarian, 0.5f, 4f, 1, 0f, 0));
            Assert.Throws<ContractException>(() => new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Agrarian, 4f, 41f, 1, 0f, 0));
            Assert.Throws<ContractException>(() => new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Agrarian, 4f, 4f, 0, 0f, 0));
            Assert.Throws<ContractException>(() => new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Agrarian, 4f, 4f, 13, 0f, 0));
            Assert.Throws<ContractException>(() => new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Agrarian, 4f, 4f, 1, -0.1f, 0));
            Assert.Throws<ContractException>(() => new ArchSpec((BuildingKind)99, ArchRegion.Verdant, ArchEra.Agrarian, 4f, 4f, 1, 0f, 0));
            Assert.Throws<ContractException>(() => new ArchSpec(BuildingKind.House, (ArchRegion)99, ArchEra.Agrarian, 4f, 4f, 1, 0f, 0));
            Assert.Throws<ContractException>(() => new ArchSpec(BuildingKind.House, ArchRegion.Verdant, (ArchEra)99, 4f, 4f, 1, 0f, 0));
        }
    }
}
