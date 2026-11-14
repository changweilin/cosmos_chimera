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
            ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor("lava_planet", 5);
            foreach (SettlementKind kind in Enum.GetValues(typeof(SettlementKind)))
            {
                bool old = kind == SettlementKind.Ruins || kind == SettlementKind.Lair;
                Assert.That(ArchitectureMeshes.SettlementEra(planet, kind), Is.EqualTo(old ? ArchEra.Ancient : planet.Era), kind.ToString());
            }

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

        // REQ: settlement-structures.md#建築造型 — 整顆星球同一時代，由 seed 獨立抽。
        [Test]
        public void The_era_of_a_planet_is_drawn_from_its_seed_and_is_never_ancient()
        {
            var eras = new HashSet<ArchEra>();
            var secondaries = new HashSet<ArchRegion>();
            for (ulong seed = 0; seed < 300; seed++)
            {
                ArchPlanetStyle a = ArchitectureMeshes.PlanetStyleFor("storm_planet", seed);
                ArchPlanetStyle b = ArchitectureMeshes.PlanetStyleFor("storm_planet", seed);
                Assert.That((b.Region, b.Era, b.SecondaryRegion, b.SecondaryEra, b.Weight),
                    Is.EqualTo((a.Region, a.Era, a.SecondaryRegion, a.SecondaryEra, a.Weight)));
                Assert.That(a.Region, Is.EqualTo(ArchRegion.Storm));
                Assert.That(a.Era, Is.Not.EqualTo(ArchEra.Ancient));
                Assert.That(a.SecondaryEra, Is.Not.EqualTo(ArchEra.Ancient).And.Not.EqualTo(a.Era));
                Assert.That(a.SecondaryRegion, Is.Not.EqualTo(a.Region));
                Assert.That(a.Weight, Is.InRange(0.15f, 0.45f));
                eras.Add(a.Era);
                secondaries.Add(a.SecondaryRegion);
            }

            Assert.That(eras.Count, Is.EqualTo(7), "every inhabited era turns up");
            Assert.That(secondaries.Count, Is.EqualTo(5), "every other tradition turns up as a secondary");
        }

        // REQ: settlement-structures.md#建築造型 — 星球類型為主、區域主題微調。
        [Test]
        public void The_mix_of_a_settlement_follows_the_planet_and_is_nudged_by_its_theme()
        {
            ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor("verdant_planet", 11);
            int leanIce = 0, differs = 0, ownSecondary = 0, ownSecondaryEra = 0;
            var regions = new HashSet<ArchRegion>();
            var eras = new HashSet<ArchEra>();
            for (int i = 0; i < 200; i++)
            {
                ArchMix plain = ArchitectureMeshes.MixFor(planet, SettlementKind.Village, null, 11, i);
                ArchMix again = ArchitectureMeshes.MixFor(planet, SettlementKind.Village, null, 11, i);
                Assert.That((again.SecondaryRegion, again.SecondaryEra, again.Weight, again.AgeMean, again.DamageMean),
                    Is.EqualTo((plain.SecondaryRegion, plain.SecondaryEra, plain.Weight, plain.AgeMean, plain.DamageMean)));
                Assert.That(plain.Weight, Is.InRange(0f, 0.7f));
                Assert.That(plain.SecondaryEra, Is.Not.EqualTo(ArchEra.Ancient).And.Not.EqualTo(planet.Era));
                Assert.That(plain.SecondaryRegion, Is.Not.EqualTo(planet.Region));
                Assert.That(plain.PlinthAddM, Is.EqualTo(0f));
                regions.Add(plain.SecondaryRegion);
                eras.Add(plain.SecondaryEra);
                if (plain.SecondaryRegion == planet.SecondaryRegion) ownSecondary++;
                if (plain.SecondaryEra == planet.SecondaryEra) ownSecondaryEra++;

                ArchMix high = ArchitectureMeshes.MixFor(planet, SettlementKind.Village, "highland", 11, i);
                Assert.That(high.PitchScale, Is.GreaterThan(1f), "highland roofs are steeper");
                if (high.SecondaryRegion == ArchRegion.Ice) leanIce++;
                if (ArchitectureMeshes.MixFor(planet, SettlementKind.Village, null, 11, i + 1).Weight != plain.Weight) differs++;

                ArchMix wet = ArchitectureMeshes.MixFor(planet, SettlementKind.Village, "riverbank", 11, i);
                Assert.That(wet.PlinthAddM, Is.GreaterThan(0f), "riverbanks build above the flood");
                ArchMix worn = ArchitectureMeshes.MixFor(planet, SettlementKind.Village, "ruins", 11, i);
                Assert.That(worn.AgeMean, Is.GreaterThanOrEqualTo(plain.AgeMean));
            }

            Assert.That(leanIce, Is.GreaterThan(60), "most highland settlements borrow from the ice tradition");
            Assert.That(differs, Is.GreaterThan(150), "settlements of one planet do not share one mix");
            // REQ: settlement-structures.md#建築造型 — 副風格的抽法：多數聚落沿用星球的副風格，少數借自第三個傳統或無淵源的時代。
            Assert.That(regions.Count, Is.EqualTo(5), "every other tradition turns up in some settlement");
            Assert.That(eras.Count, Is.EqualTo(6), "every other inhabited era turns up in some settlement");
            Assert.That(ownSecondary, Is.GreaterThan(120), "most unthemed settlements keep the planet's secondary tradition");
            Assert.That(ownSecondaryEra, Is.GreaterThan(100), "most settlements keep the planet's secondary era");

            ArchMix ruins = ArchitectureMeshes.MixFor(planet, SettlementKind.Ruins, "forest", 11, 3);
            Assert.That(ruins.Weight, Is.EqualTo(0f));
            Assert.That(ruins.SecondaryEra, Is.EqualTo(ArchEra.Ancient));
        }

        private static IEnumerable<TestCaseData> Mixed()
        {
            // Worst cases for the lot contract: every era against an unrelated secondary, old and wrecked.
            foreach (BuildingKind kind in new[] { BuildingKind.Tent, BuildingKind.House, BuildingKind.Hall, BuildingKind.Tower })
            {
                foreach (ArchEra era in Enum.GetValues(typeof(ArchEra)))
                {
                    if (era == ArchEra.Ancient) continue;
                    var region = (ArchRegion)((int)era % 6);
                    var other = (ArchRegion)(((int)era + 3) % 6);
                    ArchEra otherEra = era == ArchEra.Wasteland ? ArchEra.Future : (ArchEra)(((int)era + 2) % 4);
                    yield return new TestCaseData(kind, region, era, new ArchMix(other, otherEra, 0.7f, 1f, 1f, 1.6f, 1f))
                        .SetName($"Mixed_{kind}_{region}_{era}_x_{other}_{otherEra}");
                }
            }
        }

        [TestCaseSource(nameof(Mixed))]
        public void A_mixed_old_wrecked_building_still_keeps_every_mesh_contract(BuildingKind kind, ArchRegion region, ArchEra era, ArchMix mix)
        {
            for (ulong seed = 0; seed < 12; seed++)
            {
                ArchSpec pure = Spec(kind, region, era, seed, 1.4f);
                var spec = new ArchSpec(kind, region, era, pure.WidthM, pure.DepthM, pure.Storeys, pure.FoundationM, seed, mix);
                Mesh mesh = Build(spec);
                Assert.That(Build(spec).vertices, Is.EqualTo(mesh.vertices), "same spec, same mesh");
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Color[] colors = mesh.colors;
                Assert.That(vertices.Length, Is.GreaterThan(0).And.LessThan(65536));
                Assert.That(mesh.subMeshCount, Is.EqualTo(ArchitectureMeshes.SubMeshCount));
                Vector3 body = ArchitectureMeshes.BodySize(spec);
                float minY = float.MaxValue, maxY = float.MinValue, reachX = 0f, reachZ = 0f, tone = 0f, dark = 9f;
                bool sound = true;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    sound &= float.IsFinite(v.x + v.y + v.z) && Mathf.Abs(normals[i].magnitude - 1f) < 1e-3f &&
                             colors[i].a >= 0f && colors[i].a <= 1f;
                    reachX = Mathf.Max(reachX, Mathf.Abs(v.x));
                    reachZ = Mathf.Max(reachZ, Mathf.Abs(v.z));
                    tone = Mathf.Max(tone, colors[i].r);
                    dark = Mathf.Min(dark, colors[i].r);
                    minY = Mathf.Min(minY, v.y);
                    maxY = Mathf.Max(maxY, v.y);
                }

                Assert.That(sound, Is.True, $"seed {seed}: finite vertices, unit normals, masks in [0, 1]");
                Assert.That(tone, Is.LessThanOrEqualTo(1.3f), $"seed {seed}");
                Assert.That(dark, Is.GreaterThanOrEqualTo(0.2f), $"seed {seed}");
                Assert.That(reachX, Is.LessThanOrEqualTo(spec.WidthM * 0.5f + ArchitectureMeshes.MaxOverhangM), $"seed {seed}");
                Assert.That(reachZ, Is.LessThanOrEqualTo(spec.DepthM * 0.5f + ArchitectureMeshes.MaxOverhangM), $"seed {seed}");
                Assert.That(minY, Is.InRange(-0.5f - 1e-3f, 0f));
                Assert.That(maxY, Is.GreaterThanOrEqualTo(body.y - 1e-3f), "nothing invisible blocks the character from above");
                int inverted = 0;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    int[] triangles = mesh.GetTriangles(s);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        Vector3 face = Vector3.Cross(vertices[triangles[t + 1]] - vertices[triangles[t]], vertices[triangles[t + 2]] - vertices[triangles[t]]);
                        if (Vector3.Dot(face, normals[triangles[t]]) <= 0f) inverted++;
                    }
                }

                Assert.That(inverted, Is.EqualTo(0), $"seed {seed}: winding agrees with the shading normal");
            }
        }

        [Test]
        public void Buildings_of_one_style_cluster_around_a_mean_instead_of_repeating()
        {
            // 造型：ridge heights of one (region, era) are neither identical nor evenly spread — most sit near the mean.
            const int n = 240;
            var heights = new float[n];
            var distinct = new HashSet<float>();
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                heights[i] = Build(Spec(BuildingKind.House, ArchRegion.Ice, ArchEra.Primal, (ulong)i * 977 + 1)).bounds.max.y;
                distinct.Add(heights[i]);
                sum += heights[i];
            }

            double mean = sum / n, variance = 0;
            foreach (float h in heights) variance += (h - mean) * (h - mean);
            double sigma = Math.Sqrt(variance / n);
            int near = 0, far = 0;
            foreach (float h in heights)
            {
                if (Math.Abs(h - mean) <= sigma) near++;
                if (Math.Abs(h - mean) > 2 * sigma) far++;
            }

            Assert.That(distinct.Count, Is.GreaterThan(n * 0.9), "no two houses share a roof");
            Assert.That(sigma, Is.GreaterThan(0.05), "the spread is visible");
            // Uniform would put 58 % within one sigma and none beyond 1.73; a bell puts ~68 % and ~5 %.
            Assert.That(near, Is.GreaterThan(n * 0.62));
            Assert.That(far, Is.InRange(1, n * 0.1));
        }

        [Test]
        public void Wrecked_settlements_are_darker_and_holed_and_old_ones_more_weathered()
        {
            // 毀損／新舊：the means of the settlement move the whole street, each building around them.
            var kept = new ArchMix(ArchRegion.Verdant, ArchEra.Mercantile, 0f, 0.1f, 0f);
            var wrecked = new ArchMix(ArchRegion.Verdant, ArchEra.Mercantile, 0f, 0.9f, 0.9f);
            long glowKept = 0, glowWrecked = 0, roofKept = 0, roofWrecked = 0;
            double cleanKept = 0, cleanWrecked = 0;
            for (ulong seed = 0; seed < 40; seed++)
            {
                Mesh a = Build(new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Mercantile, 6f, 5f, 2, 0.6f, seed, kept));
                Mesh b = Build(new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Mercantile, 6f, 5f, 2, 0.6f, seed, wrecked));
                glowKept += a.GetTriangles(ArchitectureMeshes.GlowSubMesh).Length;
                glowWrecked += b.GetTriangles(ArchitectureMeshes.GlowSubMesh).Length;
                roofKept += a.GetTriangles(ArchitectureMeshes.RoofSubMesh).Length;
                roofWrecked += b.GetTriangles(ArchitectureMeshes.RoofSubMesh).Length;
                cleanKept += MeanMask(a);
                cleanWrecked += MeanMask(b);
            }

            Assert.That(glowWrecked, Is.LessThan(glowKept * 0.75), "fewer lit windows");
            Assert.That(roofWrecked, Is.LessThan(roofKept), "holes in the roofs");
            Assert.That(cleanWrecked, Is.LessThan(cleanKept * 0.9), "more of the surface is weathered");
        }

        private static double MeanMask(Mesh mesh)
        {
            // Area-weighted: a wall is two triangles and a window box twelve, and it is the wall the eye reads.
            double sum = 0, area = 0;
            Color[] colors = mesh.colors;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.GetTriangles(ArchitectureMeshes.WallSubMesh);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                double size = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude;
                sum += size * (colors[a].a + colors[b].a + colors[c].a) / 3.0;
                area += size;
            }

            return sum / Math.Max(1e-9, area);
        }

        [Test]
        public void A_secondary_style_changes_meshes_and_washes_the_palette()
        {
            var mix = new ArchMix(ArchRegion.Twilight, ArchEra.Industrial, 0.6f, 0.35f, 0.08f);
            int changed = 0;
            for (ulong seed = 0; seed < 20; seed++)
            {
                Mesh pure = Build(Spec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Mercantile, seed));
                Mesh mixed = Build(new ArchSpec(BuildingKind.House, ArchRegion.Verdant, ArchEra.Mercantile, 5f, 4f, 2, 0.6f, seed, mix));
                if (mixed.vertexCount != pure.vertexCount) changed++;
            }

            Assert.That(changed, Is.GreaterThan(14), "most hybrids borrow at least one feature");

            ArchPalette primary = ArchitectureMeshes.PaletteFor(ArchRegion.Verdant, ArchEra.Mercantile, BuildingKind.House);
            ArchPalette same = ArchitectureMeshes.PaletteFor(ArchRegion.Verdant, ArchEra.Mercantile, BuildingKind.House,
                ArchMix.Pure(ArchRegion.Verdant, ArchEra.Mercantile));
            ArchPalette washed = ArchitectureMeshes.PaletteFor(ArchRegion.Verdant, ArchEra.Mercantile, BuildingKind.House, mix);
            ArchPalette secondary = ArchitectureMeshes.PaletteFor(ArchRegion.Twilight, ArchEra.Industrial, BuildingKind.House);
            Assert.That(same.Roof, Is.EqualTo(primary.Roof));
            Assert.That(same.WeatheringStrength, Is.EqualTo(primary.WeatheringStrength).Within(1e-5f));
            Assert.That(washed.Roof, Is.Not.EqualTo(primary.Roof));
            Assert.That(Distance(washed.Roof, primary.Roof), Is.LessThan(Distance(washed.Roof, secondary.Roof)), "still reads as the primary");
            Assert.That(washed.Wall.a * washed.Roof.a * washed.Trim.a * washed.Glow.a, Is.EqualTo(1f));
        }

        private static float Distance(Color a, Color b) => new Vector3(a.r - b.r, a.g - b.g, a.b - b.b).magnitude;

        [Test]
        public void Out_of_range_mix_violates_the_contract()
        {
            Assert.Throws<ContractException>(() => new ArchMix((ArchRegion)9, ArchEra.Agrarian, 0.2f, 0.2f, 0.2f));
            Assert.Throws<ContractException>(() => new ArchMix(ArchRegion.Ice, (ArchEra)42, 0.2f, 0.2f, 0.2f));
            Assert.Throws<ContractException>(() => new ArchMix(ArchRegion.Ice, ArchEra.Agrarian, 1.1f, 0.2f, 0.2f));
            Assert.Throws<ContractException>(() => new ArchMix(ArchRegion.Ice, ArchEra.Agrarian, 0.2f, -0.1f, 0.2f));
            Assert.Throws<ContractException>(() => new ArchMix(ArchRegion.Ice, ArchEra.Agrarian, 0.2f, 0.2f, 1.2f));
            Assert.Throws<ContractException>(() => new ArchMix(ArchRegion.Ice, ArchEra.Agrarian, 0.2f, 0.2f, 0.2f, 0.4f));
            Assert.Throws<ContractException>(() => new ArchMix(ArchRegion.Ice, ArchEra.Agrarian, 0.2f, 0.2f, 0.2f, 1f, 1.5f));
            ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor(null, 0);
            Assert.Throws<ContractException>(() => ArchitectureMeshes.MixFor(planet, SettlementKind.Town, null, 0, -1));
            Assert.Throws<ContractException>(() => ArchitectureMeshes.SettlementEra(planet, (SettlementKind)77));
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
