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

        // REQ: gear-looks.md#同類內的隨機性 (部件底型 各槽均勻抽) — every slot's every variant must change the silhouette, or the
        // seed that picked it is wasted. Ice motifs draw no layout jitter and damage is 0, so only the slot itself can differ.
        [TestCaseSource(nameof(EveryKind))]
        public void Every_variant_of_every_slot_changes_the_shape(GearKind kind)
        {
            int slots = GearLooks.SlotCount(GearLooks.FamilyOf(kind));
            for (int slot = 0; slot < slots; slot++)
            {
                var seen = new HashSet<long>();
                for (int v = 0; v < GearLooks.VariantsPerSlot; v++)
                {
                    var variants = new int[slots];
                    variants[slot] = v;
                    seen.Add(ShapeHash(Build(Fixed(kind, ArchRegion.Ice, ArchEra.Mercantile, variants))));
                }
                Assert.That(seen.Count, Is.EqualTo(GearLooks.VariantsPerSlot), $"slot {slot} of {kind} has look-alike variants");
            }
        }

        // The player's own gear differs only by look seed (crafted gear is pure and new), so seeds alone must change the
        // build, not just stretch it: count distinct part counts rather than distinct vertex positions.
        [TestCaseSource(nameof(EveryKind))]
        public void Crafted_copies_from_one_planet_differ_in_structure(GearKind kind)
        {
            ArchMix crafted = GearLooks.Crafted(ArchRegion.Verdant, ArchEra.Mercantile);
            var counts = new HashSet<int>();
            for (ulong seed = 1; seed <= 30; seed++)
                counts.Add(Build(GearLooks.Sample(kind, GearContext.Combat, ArchRegion.Verdant, ArchEra.Mercantile, crafted, seed * 7919)).vertexCount);
            Assert.That(counts.Count, Is.GreaterThanOrEqualTo(8), $"{kind}: 30 seeds produced only {counts.Count} distinct builds");
        }

        [Test]
        public void Region_motifs_are_distinct()
        {
            var seen = new HashSet<long>();
            foreach (ArchRegion region in Enum.GetValues(typeof(ArchRegion)))
                for (int form = 1; form <= 2; form++)
                    seen.Add(ShapeHash(Build(Fixed(GearKind.Shield, region, ArchEra.Mercantile, new[] { 0, 0, 0, form }))));
            Assert.That(seen.Count, Is.EqualTo(Enum.GetValues(typeof(ArchRegion)).Length * 2));
        }

        // NPCs append their gear into one crowd mesh (NpcMeshesTests caps a resident at 30 000 vertices).
        [TestCaseSource(nameof(EveryKind))]
        public void Gear_stays_within_the_triangle_budget(GearKind kind)
        {
            ArchMix mix = Worn();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                Mesh mesh = Build(GearLooks.Sample(kind, (GearContext)(seed % 5), ArchRegion.Twilight, (ArchEra)(seed % 8), mix, seed * 6151));
                Assert.That(mesh.triangles.Length / 3, Is.LessThanOrEqualTo(2400), $"{kind} seed {seed}");
            }
        }

        [Test]
        public void Colourway_is_deterministic_and_in_range()
        {
            var seen = new HashSet<int>();
            for (ulong seed = 0; seed < 60; seed++)
            {
                GearLookSpec spec = GearLooks.Sample(GearKind.Upper, GearContext.Labor, ArchRegion.Crystal, ArchEra.Agrarian, Worn(), seed);
                int c = GearMeshes.ColourwayOf(spec);
                Assert.That(c, Is.InRange(0, GearMeshes.ColourwaysPerRegion - 1));
                Assert.That(GearMeshes.ColourwayOf(spec), Is.EqualTo(c));
                seen.Add(c);
            }
            Assert.That(seen.Count, Is.EqualTo(GearMeshes.ColourwaysPerRegion));
            Assert.Throws<ContractException>(() => GearMeshes.ColourwayOf(null));
        }

        private static IEnumerable<TestCaseData> EveryKind()
        {
            foreach (GearKind kind in Enum.GetValues(typeof(GearKind))) yield return new TestCaseData(kind).SetName($"{kind}");
        }

        private static GearLookSpec Fixed(GearKind kind, ArchRegion region, ArchEra era, int[] variants)
        {
            var style = new GearStyle(region, era);
            return new GearLookSpec(kind, GearContext.Combat, style, style, style, variants, 1f, 1f, 1f, 0.4f, 0.6f, 0f, 0f, 0f, 0f);
        }

        private static long ShapeHash(Mesh mesh)
        {
            long h = 1469598103934665603L ^ mesh.vertexCount;
            foreach (Vector3 v in mesh.vertices)
            {
                h = (h ^ Mathf.RoundToInt(v.x * 2000f)) * 1099511628211L;
                h = (h ^ Mathf.RoundToInt(v.y * 2000f)) * 1099511628211L;
                h = (h ^ Mathf.RoundToInt(v.z * 2000f)) * 1099511628211L;
            }
            return h;
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
