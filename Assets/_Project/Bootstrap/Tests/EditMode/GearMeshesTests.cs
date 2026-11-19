using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: docs/design/gear-looks.md#美術路線分段 — placeholder gear meshes come from the sampled look alone.
    public sealed class GearMeshesTests
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();

        private static ArchMix Worn() =>
            new ArchMix(ArchRegion.Storm, ArchEra.Steampunk, 0.5f, 0.7f, 0.6f).WithTertiary(ArchRegion.Twilight, ArchEra.Future, 0.3f);

        private static IEnumerable<TestCaseData> EveryKindAndEra()
        {
            foreach (GearKind kind in Enum.GetValues(typeof(GearKind)))
                foreach (ArchEra era in Enum.GetValues(typeof(ArchEra)))
                    yield return new TestCaseData(kind, era).SetName($"{kind}_{era}");
        }

        private Mesh Build(GearLookSpec spec)
        {
            Mesh mesh = GearMeshes.Build(spec);
            _meshes.Add(mesh);
            return mesh;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Mesh mesh in _meshes) UnityEngine.Object.DestroyImmediate(mesh);
            _meshes.Clear();
        }

        [TestCaseSource(nameof(EveryKindAndEra))]
        public void Every_kind_builds_a_bounded_deterministic_mesh(GearKind kind, ArchEra era)
        {
            ArchMix mix = Worn();
            for (ulong seed = 1; seed <= 6; seed++)
            {
                GearLookSpec spec = GearLooks.Sample(kind, GearContext.Combat, ArchRegion.Verdant, era, mix, seed * 104729);
                Mesh a = Build(spec), b = Build(spec);
                Assert.That(a.vertexCount, Is.GreaterThan(0));
                Assert.That(a.triangles.Length, Is.GreaterThan(0));
                Assert.That(b.vertices, Is.EqualTo(a.vertices));
                Assert.That(b.colors, Is.EqualTo(a.colors));
                Assert.That(b.triangles, Is.EqualTo(a.triangles));
                float limit = 1.7f * GearMeshes.NominalLength(kind) * spec.Length;
                foreach (Vector3 v in a.vertices)
                {
                    Assert.That(v.magnitude, Is.LessThanOrEqualTo(limit), $"vertex {v} beyond {limit}");
                    Assert.That(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z), Is.False);
                }
                foreach (Vector3 n in a.normals) Assert.That(float.IsNaN(n.x), Is.False);
            }
        }

        [Test]
        public void Palette_follows_region_and_ages_toward_grime()
        {
            var fresh = new ArchMix(ArchRegion.Lava, ArchEra.Agrarian, 0f, 0.01f, 0.01f);
            var old = new ArchMix(ArchRegion.Lava, ArchEra.Agrarian, 0f, 0.95f, 0.01f);
            GearPalette lava = GearMeshes.PaletteFor(GearLooks.Sample(GearKind.Upper, GearContext.Labor, ArchRegion.Lava, ArchEra.Agrarian, fresh, 3));
            GearPalette ice = GearMeshes.PaletteFor(GearLooks.Sample(GearKind.Upper, GearContext.Labor, ArchRegion.Ice, ArchEra.Agrarian, fresh, 3));
            GearPalette aged = GearMeshes.PaletteFor(GearLooks.Sample(GearKind.Upper, GearContext.Labor, ArchRegion.Lava, ArchEra.Agrarian, old, 3));
            Assert.That(ice.Main.grayscale, Is.GreaterThan(lava.Main.grayscale + 0.3f), "snow-white ice cloth vs charcoal lava cloth");
            Assert.That(Saturation(aged.Second), Is.LessThan(Saturation(lava.Second)));
        }

        [Test]
        public void Heavy_damage_rusts_metal_and_breaks_hafts()
        {
            var intact = new ArchMix(ArchRegion.Storm, ArchEra.Industrial, 0f, 0.01f, 0.001f);
            var wrecked = new ArchMix(ArchRegion.Storm, ArchEra.Industrial, 0f, 0.01f, 1f);
            GearLookSpec a = GearLooks.Sample(GearKind.Spear, GearContext.Combat, ArchRegion.Storm, ArchEra.Industrial, intact, 9);
            GearLookSpec b = GearLooks.Sample(GearKind.Spear, GearContext.Combat, ArchRegion.Storm, ArchEra.Industrial, wrecked, 9);
            Assume.That(b.Damage, Is.GreaterThan(0.85f));
            Assert.That(GearMeshes.PaletteFor(b).Metal.r - GearMeshes.PaletteFor(b).Metal.b,
                Is.GreaterThan(GearMeshes.PaletteFor(a).Metal.r - GearMeshes.PaletteFor(a).Metal.b), "rust is warmer");
            Assert.That(Height(Build(b)), Is.LessThan(Height(Build(a)) * 0.8f));
        }

        [Test]
        public void Preconditions_are_enforced()
        {
            Assert.Throws<ContractException>(() => GearMeshes.Build(null));
            Assert.Throws<ContractException>(() => GearMeshes.PaletteFor(null));
            Assert.Throws<ContractException>(() => GearMeshes.NominalLength((GearKind)99));
        }

        [Test]
        public void Block_shield_mesh_adds_pattern_and_cracks_when_damaged()
        {
            Mesh BuildShield(in BlockShieldSpec spec)
            {
                Mesh mesh = GearMeshes.BuildBlockShield(spec);
                _meshes.Add(mesh);
                return mesh;
            }

            var elem = new Color(0.2f, 0.6f, 0.9f);
            Mesh plain = BuildShield(GearLooks.BlockShieldFor(null, null, elem));
            GearLookSpec upperClean = GearLooks.Sample(GearKind.Upper, GearContext.Combat, ArchRegion.Lava, ArchEra.Agrarian,
                ArchMix.Pure(ArchRegion.Lava, ArchEra.Agrarian), 3);
            GearLookSpec lowerClean = GearLooks.Sample(GearKind.Lower, GearContext.Combat, ArchRegion.Ice, ArchEra.Industrial,
                ArchMix.Pure(ArchRegion.Ice, ArchEra.Industrial), 4);
            Mesh clean = BuildShield(GearLooks.BlockShieldFor(upperClean, lowerClean, elem));

            var dmg = new ArchMix(ArchRegion.Storm, ArchEra.Wasteland, 0f, 0f, 0.9f);
            GearLookSpec upperDamaged = GearLooks.Sample(GearKind.Upper, GearContext.Combat, ArchRegion.Storm, ArchEra.Wasteland, dmg, 5);
            Assume.That(upperDamaged.Damage, Is.GreaterThanOrEqualTo(0.35f));
            Mesh cracked = BuildShield(GearLooks.BlockShieldFor(upperDamaged, null, elem));

            Assert.That(plain.vertexCount, Is.GreaterThan(0));
            Assert.That(clean.vertexCount, Is.GreaterThan(plain.vertexCount));
            Assert.That(cracked.vertexCount, Is.GreaterThan(clean.vertexCount));
        }

        private static float Height(Mesh mesh)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vector3 v in mesh.vertices) { lo = Mathf.Min(lo, v.y); hi = Mathf.Max(hi, v.y); }
            return hi - lo;
        }

        private static float Saturation(Color c)
        {
            Color.RGBToHSV(c, out _, out float s, out _);
            return s;
        }
    }
}
