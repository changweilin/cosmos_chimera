using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Silhouette family of a procedural scenery mesh.</summary>
    /// <remarks>Pre: none. Post: stable values (they seed the mesh streams). Side effects: none.</remarks>
    public enum SceneryArchetype
    {
        /// <summary>Round-crowned tree: clustered foliage lobes on a flared, slightly bent trunk.</summary>
        Broadleaf = 0,

        /// <summary>Flat-topped tree: a thin layered canopy on a tall forked trunk.</summary>
        Umbrella = 1,

        /// <summary>Tiered needle tree.</summary>
        Conifer = 2,

        /// <summary>Chiselled boulder with one or two half-buried companions.</summary>
        Boulder = 3,

        /// <summary>Cluster of pointed hexagonal shafts.</summary>
        CrystalCluster = 4,

        /// <summary>Low bush (灌木): short stems under merged foliage lobes.</summary>
        Shrub = 5,

        /// <summary>Clump of thin jointed canes with drooping leaf whorls (箭竹).</summary>
        CaneClump = 6,

        /// <summary>Tussock of arching blades under plumed flower stalks (芒草).</summary>
        PlumeGrass = 7,

        /// <summary>Dead standing tree (枯木): a bare leaning trunk with broken limbs.</summary>
        Snag = 8,

        /// <summary>Fallen trunk (倒木) with one snapped limb standing up from it.</summary>
        Log = 9,

        /// <summary>Swamp tree: buttress roots, a wide low crown and hanging aerial roots (板根、氣根).</summary>
        SwampTree = 10,

        /// <summary>Ribbed columnar cactus with arms and a few flowers.</summary>
        Cactus = 11,

        /// <summary>Giant mushroom: a thick stalk under a spotted cap.</summary>
        Mushroom = 12,

        /// <summary>Tapering rock pillar with smaller ones at its foot (石筍、石林、冰柱).</summary>
        Spire = 13,

        /// <summary>Cluster of flat-topped hexagonal prisms (玄武岩柱).</summary>
        Columns = 14,

        /// <summary>Rock outcrop studded with mineral shards (礦脈、晶洞).</summary>
        Outcrop = 15,

        /// <summary>Rosette of arching fronds with leaflets (蕨類).</summary>
        Fern = 16,

        /// <summary>Stand of upright stems with strap leaves and seed heads (蘆葦).</summary>
        Reed = 17,

        /// <summary>Cushion of low leaves under small blossoms (花叢).</summary>
        FlowerPatch = 18,

        /// <summary>Slender pale tree with dark bark bands and a narrow, airy crown (樺樹).</summary>
        Birch = 19,

        /// <summary>Weeping tree: a short stout trunk under a low dome whose leaf strands hang most of the way down (柳).</summary>
        Willow = 20,

        /// <summary>Pine: a tall bare trunk carrying a few flat needle pads near the top (松).</summary>
        Pine = 21,

        /// <summary>Palm: a curved, ringed trunk under a crown of long arching fronds (棕櫚).</summary>
        Palm = 22,

        /// <summary>Granite tor: rounded cushion blocks stacked on each other (花崗岩堆).</summary>
        Tor = 23,

        /// <summary>Slate: thin plates leaning together at one dip (板岩片).</summary>
        Slabs = 24,

        /// <summary>Sandstone ledge: a flat-topped block whose soft beds have weathered back between hard ones (砂岩層).</summary>
        Ledge = 25,

        /// <summary>Pile of rounded cobbles (卵石堆、冰磧).</summary>
        Cobbles = 26,

        /// <summary>Limestone with vertical solution flutes running down from its crest (溶溝).</summary>
        Fluted = 27,

        /// <summary>Baobab: a swollen bottle trunk under short stubby limbs tipped with small leaf pads (猴麵包樹).</summary>
        Baobab = 28,

        /// <summary>Tree fern: a slender scaly trunk under a parasol of long lacy fronds (筆筒樹).</summary>
        TreeFern = 29,

        /// <summary>Krummholz: a wind-bent dwarf conifer whose needles stream to one side (高山矮曲林).</summary>
        Krummholz = 30,

        /// <summary>Cypress: a narrow flame-shaped evergreen column (柏).</summary>
        Cypress = 31,

        /// <summary>Mangrove: arching stilt roots under a dense low crown (紅樹林).</summary>
        Mangrove = 32,

        /// <summary>Succulent rosette of thick pointed leaves with a flower spike (龍舌蘭／蘆薈).</summary>
        Succulent = 33,

        /// <summary>Cushion plant: a low dome of tiny leaves dotted with flowers (高山墊狀植物、苔原苔蘚).</summary>
        Cushion = 34,
    }

    /// <summary>
    /// Asset-free meshes for scattered scenery (P3-09 presentation): trees, boulders, crystal clusters and understory plants shaped in code,
    /// so every planet draws them without an art import and every client builds the same ones.
    /// REQ: vision.md#畫風與沉浸感; planet-world.md (7. 散佈). Drawn with Cosmimera/ToonScenery, whose channel layout
    /// (colour rgb = tint, a = 1 − accent mask; UV1 = sway weight, flutter phase) these meshes fill.
    /// </summary>
    /// <remarks>
    /// Every mesh is one unit tall-ish (a tree's, cane clump's or grass tussock's top is near y = 1, a boulder or
    /// shrub is ~1 wide, a crystal's main shaft is 1 tall) with its ground contact at y = 0 and a skirt below it, so a prop scaled by ScatterItem.SizeM matches the
    /// rule layer's collider and volume and never shows a gap on a slope.
    /// Pre: main thread (creates Unity objects). Post: results depend only on (archetype, variant). Side effects: allocates.
    /// </remarks>
    public static class SceneryMeshes
    {
        private const ulong Salt = 0x5CE7E12Dul;
        private const float FacetCos = 0.8f; // faces within ~37° share a smooth normal; sharper edges stay crisp

        /// <summary>Number of distinct meshes of <paramref name="archetype"/>.</summary>
        /// <remarks>Pre: defined archetype. Post: ≥ 1, constant per archetype. Side effects: none.</remarks>
        public static int VariantCount(SceneryArchetype archetype) => archetype switch
        {
            SceneryArchetype.Broadleaf => 4,
            SceneryArchetype.Umbrella => 3,
            SceneryArchetype.Conifer => 3,
            SceneryArchetype.Boulder => 5,
            SceneryArchetype.CrystalCluster => 4,
            SceneryArchetype.Shrub => 4,
            SceneryArchetype.CaneClump => 3,
            SceneryArchetype.PlumeGrass => 4,
            SceneryArchetype.Snag => 3,
            SceneryArchetype.Log => 3,
            SceneryArchetype.SwampTree => 3,
            SceneryArchetype.Cactus => 4,
            SceneryArchetype.Mushroom => 4,
            SceneryArchetype.Spire => 4,
            SceneryArchetype.Columns => 3,
            SceneryArchetype.Outcrop => 4,
            SceneryArchetype.Fern => 3,
            SceneryArchetype.Reed => 3,
            SceneryArchetype.FlowerPatch => 4,
            SceneryArchetype.Birch => 3,
            SceneryArchetype.Willow => 3,
            SceneryArchetype.Pine => 3,
            SceneryArchetype.Palm => 3,
            SceneryArchetype.Tor => 4,
            SceneryArchetype.Slabs => 4,
            SceneryArchetype.Ledge => 3,
            SceneryArchetype.Cobbles => 4,
            SceneryArchetype.Fluted => 3,
            SceneryArchetype.Baobab => 3,
            SceneryArchetype.TreeFern => 3,
            SceneryArchetype.Krummholz => 3,
            SceneryArchetype.Cypress => 3,
            SceneryArchetype.Mangrove => 3,
            SceneryArchetype.Succulent => 3,
            SceneryArchetype.Cushion => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
        };

        /// <summary>Builds variant <paramref name="variant"/> of <paramref name="archetype"/>.</summary>
        /// <remarks>
        /// Pre: 0 ≤ variant &lt; VariantCount(archetype); main thread.
        /// Post: a new mesh with normals, colours and UV1; two sub-meshes for trees (broadleaf, umbrella, conifer, birch,
        ///       willow, pine, palm, baobab, tree fern, krummholz, cypress, mangrove), swamp trees and shrubs (0 = wood,
        ///       1 = foliage), cane clumps (0 = canes, 1 = leaves), plume grass (0 = blades and stalks, 1 = plumes),
        ///       cacti (0 = body, 1 = flowers), mushrooms (0 = stalk, 1 = cap), outcrops (0 = rock, 1 = mineral), reeds
        ///       (0 = stems and leaves, 1 = seed heads), flower patches and cushions (0 = leaves and stems, 1 = blossoms)
        ///       and succulents (0 = leaves, 1 = flower spike); one for
        ///       boulders, the other rock shapes (tor, slabs, ledge, cobbles, fluted), crystals, snags, logs, spires, columns
        ///       and ferns. Rocks, crystals, logs, cacti and mushrooms
        ///       carry no wind sway. The same arguments always give the same vertices; bounds include the wind sway.
        /// Side effects: allocates a Mesh the caller owns (and must destroy).
        /// </remarks>
        public static Mesh Build(SceneryArchetype archetype, int variant)
        {
            Contract.Requires(variant >= 0 && variant < VariantCount(archetype), "variant out of range");
            var rng = new Pcg32(Salt ^ ((ulong)archetype << 32), (ulong)variant);
            var data = new MeshData();
            switch (archetype)
            {
                case SceneryArchetype.Broadleaf:
                    BuildBroadleaf(data, ref rng);
                    break;
                case SceneryArchetype.Umbrella:
                    BuildUmbrella(data, ref rng);
                    break;
                case SceneryArchetype.Conifer:
                    BuildConifer(data, ref rng);
                    break;
                case SceneryArchetype.Boulder:
                    BuildBoulder(data, ref rng, variant);
                    break;
                case SceneryArchetype.Shrub:
                    BuildShrub(data, ref rng);
                    break;
                case SceneryArchetype.CaneClump:
                    BuildCaneClump(data, ref rng);
                    break;
                case SceneryArchetype.PlumeGrass:
                    BuildPlumeGrass(data, ref rng);
                    break;
                case SceneryArchetype.Snag:
                    BuildSnag(data, ref rng);
                    break;
                case SceneryArchetype.Log:
                    BuildLog(data, ref rng);
                    break;
                case SceneryArchetype.SwampTree:
                    BuildSwampTree(data, ref rng);
                    break;
                case SceneryArchetype.Cactus:
                    BuildCactus(data, ref rng);
                    break;
                case SceneryArchetype.Mushroom:
                    BuildMushroom(data, ref rng);
                    break;
                case SceneryArchetype.Spire:
                    BuildSpire(data, ref rng);
                    break;
                case SceneryArchetype.Columns:
                    BuildColumns(data, ref rng);
                    break;
                case SceneryArchetype.Outcrop:
                    BuildOutcrop(data, ref rng);
                    break;
                case SceneryArchetype.Fern:
                    BuildFern(data, ref rng);
                    break;
                case SceneryArchetype.Reed:
                    BuildReed(data, ref rng);
                    break;
                case SceneryArchetype.FlowerPatch:
                    BuildFlowerPatch(data, ref rng);
                    break;
                case SceneryArchetype.Birch:
                    BuildBirch(data, ref rng);
                    break;
                case SceneryArchetype.Willow:
                    BuildWillow(data, ref rng);
                    break;
                case SceneryArchetype.Pine:
                    BuildPine(data, ref rng);
                    break;
                case SceneryArchetype.Palm:
                    BuildPalm(data, ref rng);
                    break;
                case SceneryArchetype.Tor:
                    BuildTor(data, ref rng, variant);
                    break;
                case SceneryArchetype.Slabs:
                    BuildSlabs(data, ref rng);
                    break;
                case SceneryArchetype.Ledge:
                    BuildLedge(data, ref rng);
                    break;
                case SceneryArchetype.Cobbles:
                    BuildCobbles(data, ref rng);
                    break;
                case SceneryArchetype.Fluted:
                    BuildFluted(data, ref rng);
                    break;
                case SceneryArchetype.Baobab:
                    BuildBaobab(data, ref rng);
                    break;
                case SceneryArchetype.TreeFern:
                    BuildTreeFern(data, ref rng);
                    break;
                case SceneryArchetype.Krummholz:
                    BuildKrummholz(data, ref rng);
                    break;
                case SceneryArchetype.Cypress:
                    BuildCypress(data, ref rng);
                    break;
                case SceneryArchetype.Mangrove:
                    BuildMangrove(data, ref rng);
                    break;
                case SceneryArchetype.Succulent:
                    BuildSucculent(data, ref rng);
                    break;
                case SceneryArchetype.Cushion:
                    BuildCushion(data, ref rng);
                    break;
                default:
                    BuildCrystals(data, ref rng);
                    break;
            }

            return data.ToMesh("Scenery_" + archetype + "_" + variant);
        }

        // ---- trees ---------------------------------------------------------------------------------------------

        private static void BuildBroadleaf(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.07f, 0f, Signed(ref rng) * 0.07f);
            float3[] spine = TrunkSpine(lean, 0.66f, ref rng);
            float3 top = spine[spine.Length - 1];
            float3 crownCentre = top + new float3(0f, 0.06f, 0f);

            var lobes = new List<Lobe> { new Lobe(crownCentre, new float3(0.29f, 0.23f, 0.29f), 2) };
            int ring = 4 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.25f) * math.PI * 2f / ring;
                float r = 0.18f + rng.NextFloat() * 0.05f;
                float reach = 0.17f + rng.NextFloat() * 0.05f;
                lobes.Add(new Lobe(top + new float3(math.cos(a) * reach, -0.07f + rng.NextFloat() * 0.09f, math.sin(a) * reach),
                    new float3(r, r * 0.8f, r), 2));
            }

            lobes.Add(new Lobe(top + new float3(Signed(ref rng) * 0.05f, 0.24f, Signed(ref rng) * 0.05f),
                new float3(0.16f, 0.135f, 0.16f), 2));

            var wood = new Part();
            Tube(wood, spine, 7, (i, k) => TrunkRadius(0.062f, i / (float)(spine.Length - 1), k), BarkColor);
            for (int b = 0; b < 3; b++)
            {
                Limb(wood, spine, 0.5f + b * 0.12f, lobes[1 + b * (ring - 1) / 2].Centre, 0.03f);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.42f, 1.02f, 0.17f, ref rng);
        }

        private static void BuildUmbrella(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.1f, 0f, Signed(ref rng) * 0.1f);
            float3[] spine = TrunkSpine(lean, 0.74f, ref rng);
            float3 top = spine[spine.Length - 1];
            float3 crownCentre = top + new float3(0f, 0.08f, 0f);

            var lobes = new List<Lobe> { new Lobe(crownCentre + new float3(0f, 0.03f, 0f), new float3(0.27f, 0.1f, 0.27f), 2) };
            int ring = 5 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.2f) * math.PI * 2f / ring;
                float r = 0.17f + rng.NextFloat() * 0.06f;
                float reach = 0.23f + rng.NextFloat() * 0.06f;
                lobes.Add(new Lobe(top + new float3(math.cos(a) * reach, 0.04f + rng.NextFloat() * 0.06f, math.sin(a) * reach),
                    new float3(r, r * 0.45f, r), 2));
            }

            var wood = new Part();
            Tube(wood, spine, 7, (i, k) => TrunkRadius(0.05f, i / (float)(spine.Length - 1), k), BarkColor);
            // The canopy is thin, so the fork under it shows: one limb per other lobe.
            for (int b = 1; b < lobes.Count; b += 2)
            {
                Limb(wood, spine, 0.55f + 0.06f * (b % 3), lobes[b].Centre, 0.028f);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.68f, 0.98f, 0.1f, ref rng);
        }

        private static void BuildConifer(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.03f, 0f, Signed(ref rng) * 0.03f);
            float3[] spine = TrunkSpine(lean, 0.96f, ref rng);
            var wood = new Part();
            Tube(wood, spine, 6, (i, k) => TrunkRadius(0.048f, i / (float)(spine.Length - 1), k), BarkColor);
            data.EmitSmooth(wood, 0, null);

            // Bug fix: the old crown hung needle blades only along ten radial boughs per tier, so from most angles the
            // spaces between them read as a bare trunk with spokes. Each tier is now a closed, jagged needle skirt
            // (lit top + shaded underside) that overlaps the tier below; a light fringe of blades breaks its rim.
            int tiers = 6 + (int)rng.NextUInt(3);
            var needles = new Part();
            var under = new Part();
            var fringe = new Part();
            float spin = rng.NextFloat() * math.PI * 2f;
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)(tiers - 1);
                float rimY = math.lerp(0.16f, 0.82f, f);
                // Spruce profile: the lower skirts are broad, the top narrows quickly to a spire.
                float radius = math.lerp(0.36f, 0.07f, math.pow(f, 0.85f)) * (0.92f + rng.NextFloat() * 0.14f);
                float rise = radius * (0.95f + rng.NextFloat() * 0.25f);
                float3 rimCentre = SpineAt(spine, rimY / 0.96f);
                float3 apex = SpineAt(spine, math.min(1f, (rimY + rise) / 0.96f)) + new float3(0f, 0.01f, 0f);
                int teeth = 9 + (int)(radius * 30f);
                float tone = math.lerp(0.74f, 1.02f, f);
                Skirt(needles, under, apex, rimCentre, radius, teeth, spin + t * GoldenAngle, tone, rng.NextFloat(), ref rng);

                int tufts = teeth;
                for (int k = 0; k < tufts; k++)
                {
                    float a = spin + t * GoldenAngle + (k + 0.5f) * math.PI * 2f / tufts;
                    float3 root = rimCentre + new float3(math.cos(a) * radius * 0.72f, rise * 0.18f, math.sin(a) * radius * 0.72f);
                    CrownBlade(fringe, root, a + Signed(ref rng) * 0.3f, radius * 0.42f, -0.55f, 0.22f, tone * 1.06f,
                        rng.NextFloat());
                }
            }

            // Leader: a last small cone that sheathes the trunk tip, so no bare wood shows above the top whorl.
            float3 top = spine[spine.Length - 1];
            Skirt(needles, under, top + new float3(0f, 0.09f, 0f), top - new float3(0f, 0.08f, 0f), 0.06f, 7, spin, 1.05f,
                rng.NextFloat(), ref rng);
            data.EmitFaceted(needles, 1, 0.55f, null);
            data.EmitSmooth(under, 1, null);
            data.EmitSmooth(fringe, 1, null);
        }

        // One conifer whorl: a cone of needles from `apex` down to a jagged rim around `rimCentre`. Teeth alternate long
        // (drooping) and short (lifted) so the silhouette saws like stylised spruce boughs; the underside is its own
        // darker shell, so looking up through the tree shows shaded needles, not back-face holes.
        private static void Skirt(Part top, Part under, float3 apex, float3 rimCentre, float radius, int teeth, float turn,
            float tone, float phase, ref Pcg32 rng)
        {
            float3 axisMid = (apex + rimCentre) * 0.5f;
            float height = apex.y - rimCentre.y;
            var jitter = new float[teeth];
            for (int k = 0; k < teeth; k++)
            {
                jitter[k] = 0.94f + rng.NextFloat() * 0.12f;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                Part part = pass == 0 ? top : under;
                float shade = pass == 0 ? 1f : 0.62f;
                // The underside sits a little inside the top shell so the two never z-fight at the rim.
                float shrink = pass == 0 ? 1f : 0.9f;
                float lift = pass == 0 ? 0f : height * 0.22f;
                int apexIndex = part.Add(apex, Tint(tone * shade * 1.08f, 0.7f), Wind(apex.y, 0f, phase));
                int ring = part.P.Count;
                var midR = new float[teeth];
                var rimR = new float[teeth];
                var rimDrop = new float[teeth];
                for (int k = 0; k < teeth; k++)
                {
                    bool longTooth = (k & 1) == 0;
                    midR[k] = radius * 0.55f * shrink;
                    rimR[k] = radius * (longTooth ? 1f : 0.74f) * jitter[k] * shrink;
                    rimDrop[k] = longTooth ? radius * 0.16f : -radius * 0.05f;
                }

                for (int k = 0; k < teeth; k++)
                {
                    float a = turn + k * math.PI * 2f / teeth;
                    var dir = new float3(math.cos(a), 0f, math.sin(a));
                    float3 mid = math.lerp(rimCentre, apex, 0.48f) + dir * midR[k] - new float3(0f, lift * 0.5f, 0f);
                    float3 rim = rimCentre + dir * rimR[k] - new float3(0f, rimDrop[k] + lift, 0f);
                    part.Add(mid, Tint(tone * shade, 0.82f), Wind(mid.y, midR[k], phase));
                    part.Add(rim, Tint(tone * shade * (pass == 0 ? 0.86f : 1f), 0.9f), Wind(rim.y, rimR[k], 0.05f + 0.9f * math.frac(phase + k * 0.07f)));
                }

                // Oriented as the top shell (away from the axis below it); the underside then reverses each triangle,
                // since no single "inside" point faces every underside triangle downward.
                float3 inside = axisMid - new float3(0f, height * 0.4f, 0f);
                int firstIndex = part.T.Count;
                for (int k = 0; k < teeth; k++)
                {
                    int m0 = ring + k * 2, r0 = m0 + 1;
                    int m1 = ring + ((k + 1) % teeth) * 2, r1 = m1 + 1;
                    part.Tri(apexIndex, m0, m1, inside);
                    part.Tri(m0, r0, m1, inside);
                    part.Tri(m1, r0, r1, inside);
                }

                if (pass == 1)
                {
                    for (int i = firstIndex; i < part.T.Count; i += 3)
                    {
                        (part.T[i + 1], part.T[i + 2]) = (part.T[i + 2], part.T[i + 1]);
                    }
                }
            }
        }

        private static void BuildBirch(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.09f, 0f, Signed(ref rng) * 0.09f);
            // Finer rings than other trunks: the dark bands are vertex colours and need rows to sit on.
            float3[] spine = Resample(TrunkSpine(lean, 0.82f, ref rng), 16);
            float3 top = spine[spine.Length - 1];
            var lobes = new List<Lobe> { new Lobe(top + new float3(0f, -0.04f, 0f), new float3(0.16f, 0.24f, 0.16f), 2) };
            float turn = rng.NextFloat() * math.PI * 2f;
            int tiers = 3 + (int)rng.NextUInt(2);
            var forks = new float[tiers];
            for (int i = 0; i < tiers; i++)
            {
                float a = turn + i * GoldenAngle;
                forks[i] = 0.5f + i * 0.1f;
                float r = 0.11f + rng.NextFloat() * 0.04f;
                lobes.Add(new Lobe(SpineAt(spine, forks[i] + 0.06f) + new float3(math.cos(a) * 0.11f, 0.02f, math.sin(a) * 0.11f),
                    new float3(r, r * 0.85f, r), 2));
            }

            var wood = new Part();
            Tube(wood, spine, 7, (i, k) => TrunkRadius(0.038f, i / (float)(spine.Length - 1), k), BirchBark);
            for (int b = 0; b < tiers; b++)
            {
                Limb(wood, spine, forks[b], lobes[b + 1].Centre, 0.016f);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.38f, 1.06f, 0.2f, ref rng);
        }

        // Chalk-white bark: the material colour is pale, and these tones lay the dark lenticel dashes and the
        // blackened, fissured foot over it.
        private static Color BirchBark(float3 p, int side)
        {
            float row = p.y * 19f;
            float dash = math.frac(row + math.sin(side * 2.3f) * 0.4f);
            bool mark = dash < 0.22f && ((int)math.floor(row) + side) % 3 != 0;
            float tone = (mark ? 0.32f : 1f) * math.lerp(0.38f, 1f, math.saturate((p.y - 0.02f) / 0.16f));
            return Tint(tone, math.saturate(0.35f + p.y / 0.16f));
        }

        private static float3[] Resample(float3[] spine, int rings)
        {
            var fine = new float3[rings];
            for (int i = 0; i < rings; i++)
            {
                fine[i] = SpineAt(spine, i / (float)(rings - 1));
            }

            return fine;
        }

        private static void BuildWillow(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.08f, 0f, Signed(ref rng) * 0.08f);
            float3[] spine = TrunkSpine(lean, 0.5f, ref rng);
            float3 top = spine[spine.Length - 1];
            var lobes = new List<Lobe> { new Lobe(top + new float3(0f, 0.2f, 0f), new float3(0.26f, 0.15f, 0.26f), 2) };
            int ring = 5 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.2f) * math.PI * 2f / ring;
                float r = 0.14f + rng.NextFloat() * 0.04f;
                lobes.Add(new Lobe(top + new float3(math.cos(a) * 0.25f, 0.1f + rng.NextFloat() * 0.06f, math.sin(a) * 0.25f),
                    new float3(r, r * 0.6f, r), 2));
            }

            var wood = new Part();
            Tube(wood, spine, 8, (i, k) => TrunkRadius(0.075f, i / (float)(spine.Length - 1), k), BarkColor);
            for (int b = 1; b < lobes.Count; b++)
            {
                Limb(wood, spine, 0.8f, lobes[b].Centre, 0.032f);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.5f, 0.86f, 0.1f, ref rng);

            // Strands hang from the rim of the dome; Wind's radial term makes them sway more than the dome itself.
            var strands = new Part();
            int count = 30 + (int)rng.NextUInt(8);
            for (int s = 0; s < count; s++)
            {
                float a = turn + s * GoldenAngle;
                float rim = 0.2f + rng.NextFloat() * 0.17f;
                float3 root = top + new float3(math.cos(a) * rim, 0.1f + rng.NextFloat() * 0.06f, math.sin(a) * rim);
                float length = 0.32f + rng.NextFloat() * 0.26f;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                var hang = new float3[5];
                for (int i = 0; i < hang.Length; i++)
                {
                    float t = i / (float)(hang.Length - 1);
                    hang[i] = root + outward * (0.06f * math.sin(t * math.PI * 0.5f)) - new float3(0f, length * t, 0f);
                }

                float tone = 0.85f + rng.NextFloat() * 0.2f;
                Blade(strands, hang, Across(a), 0.022f, rng.NextFloat(), t => Tint(tone * math.lerp(0.9f, 1.08f, t), 0.8f));
            }

            data.EmitFaceted(strands, 1, FacetCos, null);
        }

        private static void BuildPine(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.12f, 0f, Signed(ref rng) * 0.12f);
            float3[] spine = TrunkSpine(lean, 0.9f, ref rng);
            var wood = new Part();
            Tube(wood, spine, 7, (i, k) => TrunkRadius(0.042f, i / (float)(spine.Length - 1), k), PineBark);
            float3 top = spine[spine.Length - 1];
            var lobes = new List<Lobe> { new Lobe(top + new float3(0f, 0.02f, 0f), new float3(0.15f, 0.07f, 0.15f), 2) };
            int pads = 4 + (int)rng.NextUInt(3);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < pads; i++)
            {
                float t = 0.62f + rng.NextFloat() * 0.3f;
                float a = turn + i * GoldenAngle;
                float reach = 0.14f + rng.NextFloat() * 0.12f;
                float3 hub = SpineAt(spine, t);
                float r = 0.15f + rng.NextFloat() * 0.07f;
                float3 centre = hub + new float3(math.cos(a) * reach, 0.05f + rng.NextFloat() * 0.04f, math.sin(a) * reach);
                // A bough rises from the trunk and kinks out flat under its pad: the layered, windswept pine look.
                Tube(wood, new[] { hub, math.lerp(hub, centre, 0.55f) + new float3(0f, 0.03f, 0f), centre - new float3(0f, 0.03f, 0f) },
                    4, (j, _) => 0.016f * math.lerp(1f, 0.5f, j * 0.5f), (p, _) => Tint(0.85f, 1f));
                lobes.Add(new Lobe(centre, new float3(r, r * 0.38f, r), 2));
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.55f, 1.02f, 0.12f, ref rng, 0.12f);
        }

        // Grey-brown and furrowed low on the trunk, thin orange plates higher up.
        private static Color PineBark(float3 p, int side)
        {
            Color c = BarkColor(p, side);
            float upper = math.smoothstep(0.35f, 0.7f, p.y);
            c.r *= 1f + 0.35f * upper;
            c.g *= 1f + 0.05f * upper;
            c.b *= 1f - 0.15f * upper;
            return c;
        }

        private static void BuildPalm(MeshData data, ref Pcg32 rng)
        {
            float bendA = rng.NextFloat() * math.PI * 2f;
            float bend = 0.16f + rng.NextFloat() * 0.1f;
            var away = new float3(math.cos(bendA), 0f, math.sin(bendA));
            const int rings = 15;
            var spine = new float3[rings];
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                spine[i] = away * (bend * t * t) + new float3(0f, -0.05f + t * 0.87f, 0f);
            }

            var wood = new Part();
            // Leaf-scar rings: every other ring of the fine spine bulges and darkens a little.
            Tube(wood, spine, 8,
                (i, k) => 0.045f * (1f + 0.6f * math.exp(-i / (float)(rings - 1) * 10f)) *
                          math.lerp(1f, 0.72f, i / (float)(rings - 1)) * ((i & 1) == 0 ? 1.06f : 1f),
                (p, k) => Tint(math.lerp(0.62f, 1f, math.saturate(p.y / 0.2f)) * ((((int)math.floor(p.y * 14f)) & 1) == 0 ? 1f : 0.82f),
                    math.saturate(0.35f + p.y / 0.16f)));
            data.EmitSmooth(wood, 0, null);

            float3 top = spine[rings - 1];
            var fronds = new Part();
            int count = 9 + (int)rng.NextUInt(3);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int f = 0; f < count; f++)
            {
                float a = turn + (f + Signed(ref rng) * 0.2f) * math.PI * 2f / count;
                float length = 0.4f + rng.NextFloat() * 0.1f;
                float3[] rib = Arc(top, a, length, 0.25f + rng.NextFloat() * 0.35f, 0.9f + rng.NextFloat() * 0.3f, 7);
                float phase = rng.NextFloat();
                float tone = 0.9f + rng.NextFloat() * 0.15f;
                Blade(fronds, rib, Across(a), 0.03f, phase, t => Tint(tone * math.lerp(0.85f, 1.05f, t), 1f));
                // Leaflets comb out to both sides of the rib and droop; the dry tone (accent) creeps in at their tips.
                for (int j = 1; j < rib.Length - 1; j++)
                {
                    float u = j / (float)(rib.Length - 1);
                    float leaflet = 0.13f * (1f - 0.6f * u);
                    CrownBlade(fronds, rib[j], a + 1.15f, leaflet, -0.55f, 0.16f, tone, phase);
                    CrownBlade(fronds, rib[j], a - 1.15f, leaflet, -0.55f, 0.16f, tone, phase);
                }
            }

            data.EmitFaceted(fronds, 1, FacetCos, null);
        }

        private static float3[] TrunkSpine(float3 lean, float height, ref Pcg32 rng)
        {
            const int rings = 7;
            var spine = new float3[rings];
            float wobble = Signed(ref rng) * 0.025f;
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                // Starts below the ground: the item is sunk 0.1 m, and a slope must never show under the root flare.
                spine[i] = lean * (t * t) + new float3(wobble * math.sin(t * math.PI), -0.05f + t * (height + 0.05f),
                    -wobble * math.sin(t * math.PI * 1.5f));
            }

            return spine;
        }

        private static float3 SpineAt(float3[] spine, float t)
        {
            float x = math.saturate(t) * (spine.Length - 1);
            int i = math.min((int)x, spine.Length - 2);
            return math.lerp(spine[i], spine[i + 1], x - i);
        }

        // Root flare with three buttress lobes at the ground, tapering to under half at the top.
        private static float TrunkRadius(float radius, float t, int side)
        {
            float flare = math.exp(-t * 9f);
            float lobes = 0.5f + 0.5f * math.sin(side * 2.7f + 0.6f);
            return radius * (1f + flare * (0.55f + 0.6f * lobes)) * math.lerp(1f, 0.42f, t);
        }

        private static Color BarkColor(float3 p, int side)
        {
            // Contact darkening at the ground, then lighter/darker ridges running up the trunk.
            float tone = math.lerp(0.66f, 1f, math.saturate(p.y / 0.2f)) * (1f + 0.07f * math.sin(side * 2.1f + p.y * 9f));
            // Accent (moss / ground colour) creeps up the root flare so the trunk grows out of the ground.
            return Tint(tone, math.saturate(0.35f + p.y / 0.16f));
        }

        private static void Limb(Part wood, float3[] spine, float t, float3 toward, float radius)
        {
            float3 from = SpineAt(spine, t);
            float3 end = math.lerp(from, toward, 0.78f);
            var limb = new[]
            {
                from - new float3(0f, 0.02f, 0f),
                math.lerp(from, end, 0.5f) + new float3(0f, -0.025f, 0f),
                end,
            };
            Tube(wood, limb, 5, (i, k) => radius * math.lerp(1f, 0.4f, i * 0.5f), (p, k) => Tint(0.82f, 1f));
        }

        // A closed tube along `spine`, ending in a point. Sway weight grows with height² so roots stay planted.
        private static void Tube(Part part, float3[] spine, int sides, Func<int, int, float> radius, Func<float3, int, Color> color,
            float sway = 1f)
        {
            int first = part.P.Count;
            float3 reference = new float3(1f, 0f, 0f);
            // Limbs can run along X; any axis not parallel to the tangent gives a stable frame. Chosen once per tube:
            // switching axis between two rings turns the frame a quarter and twists the faces between them inside out.
            float3 axis = reference;
            for (int i = 0; i < spine.Length; i++)
            {
                float3 t = math.normalize(spine[math.min(i + 1, spine.Length - 1)] - spine[math.max(i - 1, 0)]);
                if (math.abs(math.dot(t, reference)) > 0.9f)
                {
                    axis = new float3(0f, 0f, 1f);
                }
            }

            for (int i = 0; i < spine.Length; i++)
            {
                float3 tangent = math.normalize(spine[math.min(i + 1, spine.Length - 1)] - spine[math.max(i - 1, 0)]);
                float3 side = math.normalize(math.cross(tangent, axis));
                float3 forward = math.cross(side, tangent);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * math.PI * 2f / sides;
                    float3 p = spine[i] + (side * math.cos(a) + forward * math.sin(a)) * radius(i, k);
                    part.Add(p, color(p, k), new float2(WoodSway(p.y) * sway, 0f));
                }
            }

            for (int i = 0; i < spine.Length - 1; i++)
            {
                float3 inside = (spine[i] + spine[i + 1]) * 0.5f;
                for (int k = 0; k < sides; k++)
                {
                    int a = first + i * sides + k;
                    int b = first + i * sides + (k + 1) % sides;
                    part.Tri(a, b, a + sides, inside);
                    part.Tri(b, b + sides, a + sides, inside);
                }
            }

            float3 last = spine[spine.Length - 1];
            float3 tipDir = math.normalize(last - spine[spine.Length - 2]);
            int tip = part.Add(last + tipDir * 0.02f, color(last, 0), new float2(WoodSway(last.y) * sway, 0f));
            int ring = first + (spine.Length - 1) * sides;
            for (int k = 0; k < sides; k++)
            {
                part.Tri(ring + k, ring + (k + 1) % sides, tip, last - tipDir * 0.05f);
            }
        }

        private static float WoodSway(float y) => math.saturate(y) * math.saturate(y);

        private static float2 Wind(float y, float radial, float phase) =>
            new float2(math.saturate(y) * math.saturate(y) + 0.6f * radial, phase);

        // Sample the crown envelope in equal-area bands so no pole gathers a tuft of overlapping blades.
        private static void EmitCrown(MeshData data, List<Lobe> lobes, float bottomY, float topY,
            float bump, ref Pcg32 rng, float widthRatio = 0.31f)
        {
            var leaves = new Part();
            var twigs = new Part();
            float3 fork = lobes[0].Centre - new float3(0f, lobes[0].Radii.y * 0.56f, 0f);
            foreach (Lobe lobe in lobes)
            {
                Twig(twigs, fork, lobe.Centre, lobe.Radii.x * 0.047f);
                float turn = rng.NextFloat() * math.PI * 2f;
                int shoots = lobe.Detail * 7;
                for (int i = 0; i < shoots; i++)
                {
                    float y = 1f - 2f * (i + 0.5f) / shoots;
                    float a = turn + GoldenAngle * i + Signed(ref rng) * 0.18f;
                    float r = math.sqrt(1f - y * y);
                    float3 shell = new float3(r * math.cos(a), y < 0f ? y * 0.65f : y, r * math.sin(a));
                    float3 shoot = lobe.Radii * shell * (0.91f + bump * Signed(ref rng));
                    Twig(twigs, lobe.Centre, lobe.Centre + shoot, lobe.Radii.x * 0.024f);
                    float phase = rng.NextFloat();
                    for (int j = 0; j < 6; j++)
                    {
                        float u = 0.20f + j * 0.15f;
                        float3 root = lobe.Centre + shoot * u;
                        float bearing = a + ((j & 1) == 0 ? 1f : -1f) * (0.88f - 0.38f * u);
                        float length = lobe.Radii.x * (0.58f - u * 0.19f + Signed(ref rng) * 0.06f);
                        float tone = math.lerp(0.72f, 1.05f, math.saturate((root.y - bottomY) / (topY - bottomY)));
                        CrownBlade(leaves, root, bearing, length, y * 0.44f - u * 0.38f, widthRatio, tone, phase);
                    }
                }
            }
            data.EmitSmooth(twigs, 0, null);
            data.EmitSmooth(leaves, 1, null);
        }

        private static void Twig(Part wood, float3 start, float3 end, float radius)
        {
            Tube(wood, new[] { start, end }, 3, (i, _) => radius * (i == 0 ? 1f : 0.35f), BarkColor);
        }

        // Separate upper and lower vertices preserve back-face culling and lighting at ten vertices per leaf.
        private static void CrownBlade(Part part, float3 root, float angle, float length, float droop,
            float widthRatio, float tone, float phase)
        {
            float3 axis = new float3(math.cos(angle), droop, math.sin(angle)) * length;
            float3 middle = root + axis * 0.44f;
            float3 wide = Across(angle) * (length * widthRatio);
            float3 fold = new float3(0f, length * 0.11f, 0f);
            float2 wind = Wind(root.y, math.length(root.xz), phase);
            for (int face = 0; face < 2; face++)
            {
                float3 inside = middle + fold * (face == 0 ? -3f : 3f);
                int stem = part.Add(root, Tint(tone * 0.85f, 1f), wind);
                int left = part.Add(middle - wide, Tint(tone, 0.88f), wind);
                int ridge = part.Add(middle + fold, Tint(tone * 1.08f, 0.65f), wind);
                int right = part.Add(middle + wide, Tint(tone, 0.88f), wind);
                int tip = part.Add(root + axis, Tint(tone * 1.04f, 0.62f), Wind(root.y + axis.y, math.length(root.xz), phase));
                part.Tri(stem, left, ridge, inside);
                part.Tri(stem, ridge, right, inside);
                part.Tri(tip, ridge, left, inside);
                part.Tri(tip, right, ridge, inside);
            }
        }

        // ---- climate trees (planet-world.md#散佈 緯度×海拔) ------------------------------------------------------

        private static void BuildBaobab(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.03f, 0f, Signed(ref rng) * 0.03f);
            float3[] spine = Resample(TrunkSpine(lean, 0.64f, ref rng), 10);
            int rings = spine.Length - 1;
            float girth = 0.15f + rng.NextFloat() * 0.04f;
            var wood = new Part();
            // Bottle profile: widest a third of the way up, pinched under the limbs.
            Tube(wood, spine, 12, (i, k) =>
            {
                float t = i / (float)rings;
                float swell = 1f + 0.35f * math.sin(math.saturate(t * 1.4f) * math.PI) - 0.55f * t * t;
                return girth * swell * (1f + 0.04f * math.sin(k * 2.1f + i));
            }, (p, k) => Tint(math.lerp(0.78f, 1.05f, math.saturate(p.y / 0.5f)) * (0.95f + 0.05f * math.sin(k * 3f)), 1f));
            float3 top = spine[rings];
            var lobes = new List<Lobe> { new Lobe(top + new float3(0f, 0.1f, 0f), new float3(0.12f, 0.06f, 0.12f), 1) };
            int limbs = 6 + (int)rng.NextUInt(3);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int b = 0; b < limbs; b++)
            {
                float a = turn + b * math.PI * 2f / limbs + Signed(ref rng) * 0.2f;
                float reach = 0.18f + rng.NextFloat() * 0.12f;
                float3 tip = top + new float3(math.cos(a) * reach, 0.1f + rng.NextFloat() * 0.14f, math.sin(a) * reach);
                // Stubby, root-like limbs: thick at the trunk, kinking upward.
                float3 knee = math.lerp(top, tip, 0.55f) + new float3(0f, -0.03f, 0f);
                Tube(wood, new[] { top - new float3(0f, 0.04f, 0f), knee, tip }, 6,
                    (i, _) => 0.045f * math.lerp(1f, 0.35f, i * 0.5f), (p, _) => Tint(0.9f, 1f));
                float r = 0.12f + rng.NextFloat() * 0.04f;
                lobes.Add(new Lobe(tip + new float3(0f, 0.03f, 0f), new float3(r, r * 0.5f, r), 2));
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.7f, 1.0f, 0.12f, ref rng, 0.26f);
        }

        private static void BuildTreeFern(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.08f, 0f, Signed(ref rng) * 0.08f);
            float3[] spine = Resample(TrunkSpine(lean, 0.74f, ref rng), 14);
            var wood = new Part();
            // Shaggy trunk: old frond bases leave a diamond pattern, darker toward the foot.
            Tube(wood, spine, 8, (i, k) => 0.04f * (1f + 0.5f * math.exp(-i * 0.5f)) * ((i + k) % 2 == 0 ? 1.08f : 0.96f),
                (p, k) => Tint(math.lerp(0.55f, 0.9f, math.saturate(p.y / 0.7f)) * (((int)math.floor(p.y * 18f) + k) % 2 == 0 ? 1f : 0.85f), 1f));
            data.EmitSmooth(wood, 0, null);

            float3 top = spine[spine.Length - 1];
            var fronds = new Part();
            int count = 11 + (int)rng.NextUInt(4);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int f = 0; f < count; f++)
            {
                float a = turn + f * GoldenAngle;
                float length = 0.42f + rng.NextFloat() * 0.12f;
                // Young fronds stand up, old ones arch down past the crown.
                float lift = 0.5f + rng.NextFloat() * 0.9f;
                float3[] rib = Arc(top, a, length, lift, 1f + rng.NextFloat() * 0.5f, 7);
                float tone = 0.86f + rng.NextFloat() * 0.18f;
                float phase = rng.NextFloat();
                Blade(fronds, rib, Across(a), 0.012f, phase, t => Tint(tone * 0.7f, 1f));
                for (int j = 1; j < rib.Length - 1; j++)
                {
                    float u = j / (float)(rib.Length - 1);
                    float leaflet = 0.12f * (1f - 0.55f * u) * (0.7f + 0.3f * math.sin(u * math.PI));
                    CrownBlade(fronds, rib[j], a + 1.3f, leaflet, -0.25f, 0.2f, tone, phase);
                    CrownBlade(fronds, rib[j], a - 1.3f, leaflet, -0.25f, 0.2f, tone, phase);
                }
            }

            data.EmitFaceted(fronds, 1, FacetCos, null);
        }

        private static void BuildKrummholz(MeshData data, ref Pcg32 rng)
        {
            // The prevailing wind of the summit, frozen into the tree: every branch on the windward side died back.
            float a = rng.NextFloat() * math.PI * 2f;
            var lee = new float3(math.cos(a), 0f, math.sin(a));
            float3[] spine = Resample(new[]
            {
                new float3(0f, -0.05f, 0f),
                lee * 0.06f + new float3(0f, 0.2f, 0f),
                lee * 0.2f + new float3(0f, 0.38f, 0f),
                lee * 0.4f + new float3(0f, 0.5f, 0f),
            }, 8);
            var wood = new Part();
            Tube(wood, spine, 7, (i, k) => TrunkRadius(0.05f, i / (float)(spine.Length - 1), k), PineBark);
            var needles = new Part();
            var under = new Part();
            int pads = 5 + (int)rng.NextUInt(3);
            for (int i = 0; i < pads; i++)
            {
                float t = 0.3f + 0.7f * i / (pads - 1f);
                float3 hub = SpineAt(spine, t);
                float3 rim = hub + lee * (0.08f + 0.14f * rng.NextFloat()) + new float3(0f, 0.04f, 0f);
                Tube(wood, new[] { hub, math.lerp(hub, rim, 0.6f), rim }, 4, (j, _) => 0.016f * math.lerp(1f, 0.5f, j * 0.5f),
                    PineBark);
                float r = 0.13f + rng.NextFloat() * 0.07f;
                // A flat, wind-pressed skirt: apex barely above the rim.
                Skirt(needles, under, rim + new float3(0f, r * 0.45f, 0f), rim, r, 10 + (int)(r * 30f), a, 0.8f + 0.2f * t,
                    rng.NextFloat(), ref rng);
            }

            data.EmitSmooth(wood, 0, null);
            data.EmitFaceted(needles, 1, 0.55f, null);
            data.EmitSmooth(under, 1, null);
        }

        private static void BuildCypress(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.02f, 0f, Signed(ref rng) * 0.02f);
            float3[] spine = TrunkSpine(lean, 0.95f, ref rng);
            var wood = new Part();
            Tube(wood, spine, 6, (i, k) => TrunkRadius(0.04f, i / (float)(spine.Length - 1), k), BarkColor);
            data.EmitSmooth(wood, 0, null);
            // A flame of tightly stacked narrow needle skirts: widest a quarter up, pointed at the top, no gaps.
            var needles = new Part();
            var under = new Part();
            int stack = 11 + (int)rng.NextUInt(3);
            float spin = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < stack; i++)
            {
                float t = i / (stack - 1f);
                float rimY = math.lerp(0.12f, 0.9f, t);
                float r = 0.15f * math.sin(math.lerp(0.5f, 2.9f, t)) + 0.025f;
                float3 rim = SpineAt(spine, rimY / 0.95f);
                float3 apex = SpineAt(spine, math.min(1f, (rimY + 0.16f) / 0.95f)) + new float3(0f, 0.02f, 0f);
                Skirt(needles, under, apex, rim, r, 9 + (int)(r * 40f), spin + i * GoldenAngle, math.lerp(0.78f, 1.05f, t),
                    rng.NextFloat(), ref rng);
            }

            float3 top = spine[spine.Length - 1];
            Skirt(needles, under, top + new float3(0f, 0.08f, 0f), top - new float3(0f, 0.08f, 0f), 0.04f, 7, spin, 1.05f,
                rng.NextFloat(), ref rng);
            data.EmitFaceted(needles, 1, 0.55f, null);
            data.EmitSmooth(under, 1, null);
        }

        private static void BuildMangrove(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.05f, 0f, Signed(ref rng) * 0.05f);
            float3[] spine = TrunkSpine(lean, 0.6f, ref rng);
            var wood = new Part();
            Tube(wood, spine, 8, (i, k) => 0.05f * math.lerp(1f, 0.6f, i / (float)(spine.Length - 1)), BarkColor);
            // Stilt roots arch out from a third of the way up and plunge into the mud, the tree's signature.
            int roots = 7 + (int)rng.NextUInt(4);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int r = 0; r < roots; r++)
            {
                float a = turn + r * math.PI * 2f / roots + Signed(ref rng) * 0.2f;
                var dir = new float3(math.cos(a), 0f, math.sin(a));
                float3 from = SpineAt(spine, 0.25f + rng.NextFloat() * 0.25f);
                float reach = 0.24f + rng.NextFloat() * 0.14f;
                float3 foot = dir * reach + new float3(0f, -0.06f, 0f);
                float3 arch = math.lerp(from, foot, 0.5f) + dir * 0.04f + new float3(0f, 0.08f, 0f);
                Tube(wood, new[] { from, arch, foot }, 5, (i, _) => 0.018f * math.lerp(1f, 0.75f, i * 0.5f),
                    (p, _) => Tint(math.lerp(0.55f, 0.85f, math.saturate(p.y / 0.3f)), 1f), 0f);
            }

            float3 top = spine[spine.Length - 1];
            var lobes = new List<Lobe> { new Lobe(top + new float3(0f, 0.08f, 0f), new float3(0.3f, 0.15f, 0.3f), 2) };
            int ring = 5 + (int)rng.NextUInt(2);
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + 0.5f) * math.PI * 2f / ring;
                float rr = 0.16f + rng.NextFloat() * 0.05f;
                lobes.Add(new Lobe(top + new float3(math.cos(a) * 0.24f, 0.0f + rng.NextFloat() * 0.06f, math.sin(a) * 0.24f),
                    new float3(rr, rr * 0.7f, rr), 2));
            }

            for (int b = 1; b < lobes.Count; b += 2)
            {
                Limb(wood, spine, 0.75f, lobes[b].Centre, 0.025f);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.48f, 0.92f, 0.12f, ref rng);
        }

        // ---- climate understory ------------------------------------------------------------------------------

        private static void BuildSucculent(MeshData data, ref Pcg32 rng)
        {
            // Unit = width. Thick, channelled leaves in a spiral rosette; inner leaves stand up, outer ones splay.
            var leaves = new Part();
            int count = 16 + (int)rng.NextUInt(6);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                float u = i / (float)(count - 1);
                float a = turn + i * GoldenAngle;
                float length = math.lerp(0.5f, 0.28f, u);
                float lift = math.lerp(0.55f, 2.4f, u);
                float3[] rib = Arc(new float3(0f, -0.02f, 0f), a, length, lift, math.lerp(0.5f, 0.1f, u), 5);
                float tone = 0.86f + rng.NextFloat() * 0.18f;
                Blade(leaves, rib, Across(a), length * 0.16f, 0f, t => Tint(tone * math.lerp(0.85f, 1.08f, t), 1f - 0.7f * t * t));
            }

            data.EmitFaceted(leaves, 0, FacetCos, null);
            // A flower spike with a few bloom clusters (every rosette: the bloom material slot must always be filled).
            var spike = new Part();
            float h = 0.6f + rng.NextFloat() * 0.35f;
            float3 bend = new float3(Signed(ref rng) * 0.06f, 0f, Signed(ref rng) * 0.06f);
            float3[] stalk = { new float3(0f, 0.1f, 0f), bend * 0.4f + new float3(0f, h * 0.55f, 0f), bend + new float3(0f, h, 0f) };
            Tube(spike, stalk, 5, (i, _) => 0.018f * math.lerp(1f, 0.5f, i * 0.5f), (p, _) => Tint(0.7f, 0.2f), 0.3f);
            for (int k = 0; k < 5; k++)
            {
                float3 c = SpineAt(stalk, 0.6f + 0.1f * k);
                Ball(spike, c, 0.05f - 0.006f * k, Tint(1f, 0f));
            }

            data.EmitFaceted(spike, 1, FacetCos, null);
        }

        private static void BuildCushion(MeshData data, ref Pcg32 rng)
        {
            // Unit = width. A domed mat: an icosphere's upper half pressed flat, its facets toned like tiny leaf rosettes.
            Ico ico = Ico.Get(3);
            var mat = new Part();
            float squash = 0.4f + rng.NextFloat() * 0.06f;
            var radii = new float3(0.5f, squash, 0.5f);
            float wobble = rng.NextFloat() * 10f;
            for (int i = 0; i < ico.Vertices.Length; i++)
            {
                float3 v = ico.Vertices[i];
                float lump = 1f + 0.08f * noise.snoise(v * 4f + wobble);
                float3 p = new float3(v.x, math.max(v.y, -0.15f), v.z) * radii * lump;
                float tone = 0.82f + 0.25f * math.saturate(v.y) + 0.06f * noise.snoise(v * 9f + wobble);
                mat.Add(p, Tint(tone, 1f), new float2(0f, 0f));
            }

            for (int t = 0; t < ico.Triangles.Length; t += 3)
            {
                mat.Tri(ico.Triangles[t], ico.Triangles[t + 1], ico.Triangles[t + 2], new float3(0f, -0.1f, 0f));
            }

            data.EmitFaceted(mat, 0, 0.9f, null);
            var blooms = new Part();
            int dots = 14 + (int)rng.NextUInt(12);
            for (int d = 0; d < dots; d++)
            {
                float a = d * GoldenAngle + wobble;
                float r = 0.42f * math.sqrt((d + 0.5f) / dots);
                float y = squash * math.sqrt(math.max(0f, 1f - (r / 0.5f) * (r / 0.5f))) + 0.005f;
                Ball(blooms, new float3(math.cos(a) * r, y, math.sin(a) * r), 0.028f, Tint(0.95f + rng.NextFloat() * 0.1f, 0f));
            }

            data.EmitFaceted(blooms, 1, FacetCos, null);
        }

        // A tiny faceted ball (bloom, bud): an octahedron is enough at this size.
        private static void Ball(Part part, float3 centre, float radius, Color color)
        {
            int first = part.P.Count;
            float3[] dirs =
            {
                new float3(1f, 0f, 0f), new float3(-1f, 0f, 0f), new float3(0f, 1f, 0f),
                new float3(0f, -1f, 0f), new float3(0f, 0f, 1f), new float3(0f, 0f, -1f),
            };
            foreach (float3 d in dirs)
            {
                part.Add(centre + d * radius, color, new float2(0f, 0f));
            }

            int[] faces = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };
            for (int f = 0; f < faces.Length; f += 3)
            {
                part.Tri(first + faces[f], first + faces[f + 1], first + faces[f + 2], centre);
            }
        }

        // ---- understory ----------------------------------------------------------------------------------------

        private const float GoldenAngle = 2.39996323f; // successive feet at this turn fill a clump without rows

        private static void BuildShrub(MeshData data, ref Pcg32 rng)
        {
            // Unit = width: the foliage fills ScatterItem's shrub ellipsoid (semi-axes 0.5, 0.4, 0.5).
            float3 crownCentre = new float3(0f, 0.3f, 0f);
            var lobes = new List<Lobe> { new Lobe(crownCentre, new float3(0.32f, 0.27f, 0.32f), 2) };
            int ring = 4 + (int)rng.NextUInt(3);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.25f) * math.PI * 2f / ring;
                float r = 0.17f + rng.NextFloat() * 0.07f;
                float reach = 0.22f + rng.NextFloat() * 0.08f;
                lobes.Add(new Lobe(new float3(math.cos(a) * reach, 0.13f + rng.NextFloat() * 0.14f, math.sin(a) * reach),
                    new float3(r, r * 0.85f, r), 1));
            }

            lobes.Add(new Lobe(new float3(Signed(ref rng) * 0.08f, 0.5f, Signed(ref rng) * 0.08f),
                new float3(0.18f, 0.15f, 0.18f), 1));

            // The lobes almost reach the ground; the stems only show in the gaps between them.
            var wood = new Part();
            for (int s = 0; s < 3; s++)
            {
                float3 end = lobes[1 + s * ring / 3].Centre;
                var spine = new[]
                {
                    new float3(end.x * 0.15f, -0.06f, end.z * 0.15f),
                    new float3(end.x * 0.5f, end.y * 0.45f, end.z * 0.5f),
                    end,
                };
                Tube(wood, spine, 5, (i, k) => 0.03f * math.lerp(1f, 0.45f, i * 0.5f), BarkColor);
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.02f, 0.68f, 0.2f, ref rng);
        }

        private static void BuildCaneClump(MeshData data, ref Pcg32 rng)
        {
            const int nodes = 5;
            const float buried = 0.06f;
            int canes = 9 + (int)rng.NextUInt(5);
            float turn = rng.NextFloat() * math.PI * 2f;
            var wood = new Part();
            var leaves = new Part();
            for (int c = 0; c < canes; c++)
            {
                float a = turn + c * GoldenAngle;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                // Feet stay inside the rule layer's 0.2 clump radius; outer canes lean further out.
                float foot = 0.03f + 0.15f * math.sqrt((c + 0.5f) / canes);
                float height = 0.62f + rng.NextFloat() * 0.36f;
                float lean = 0.05f + foot * 0.9f + rng.NextFloat() * 0.06f;
                float radius = 0.011f + rng.NextFloat() * 0.006f;

                // Two rings per joint: one just under it at the cane's width, one on it, swollen and dark. Vertex
                // colour alone cannot draw a joint between rings a fifth of the cane apart.
                var spine = new float3[nodes * 2];
                var swell = new float[nodes * 2];
                for (int i = 0; i < spine.Length; i++)
                {
                    bool joint = (i & 1) == 0 && i > 0;
                    float t = i == 0 ? 0f : i == spine.Length - 1 ? 1f : ((i + 1) / 2) / (float)nodes - (joint ? 0f : 0.035f);
                    spine[i] = outward * (foot + lean * t * t) + new float3(0f, -buried + t * (height + buried), 0f);
                    swell[i] = (joint ? 1.3f : 1f) * math.lerp(1f, 0.5f, t);
                }

                Tube(wood, spine, 5, (i, k) => radius * swell[i], (p, k) =>
                {
                    float along = (p.y + buried) / (height + buried) * nodes;
                    bool joint = along > 0.5f && math.abs(along - math.round(along)) < 0.1f;
                    float tone = math.lerp(0.72f, 1f, math.saturate(p.y / 0.15f)) * (joint ? 0.66f : 1f);
                    return Tint(tone, joint ? 0.35f : 1f);
                });

                float phase = 0.1f + 0.9f * rng.NextFloat();
                for (int w = 0; w < 3; w++)
                {
                    float3 node = SpineAt(spine, 0.55f + w * 0.17f + rng.NextFloat() * 0.05f);
                    int count = 2 + (int)rng.NextUInt(2);
                    float spin = rng.NextFloat() * math.PI * 2f;
                    for (int l = 0; l < count; l++)
                    {
                        float la = spin + l * math.PI * 2f / count + Signed(ref rng) * 0.3f;
                        float length = 0.13f + rng.NextFloat() * 0.07f;
                        float tone = 0.9f + rng.NextFloat() * 0.16f;
                        Blade(leaves, Arc(node, la, length, 0.35f, 0.75f, 4), Across(la), length * 0.13f, phase,
                            t => Tint(tone * math.lerp(0.82f, 1.06f, t), 1f - 0.5f * t));
                    }
                }
            }

            data.EmitSmooth(wood, 0, null);
            data.EmitFaceted(leaves, 1, FacetCos, null);
        }

        private static void BuildPlumeGrass(MeshData data, ref Pcg32 rng)
        {
            var blades = new Part();
            var stalkWood = new Part();
            var plumes = new Part();
            float turn = rng.NextFloat() * math.PI * 2f;
            int count = 26 + (int)rng.NextUInt(9);
            for (int b = 0; b < count; b++)
            {
                float a = turn + b * GoldenAngle + Signed(ref rng) * 0.2f;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                float rise = 0.42f + rng.NextFloat() * 0.3f;
                float reach = 0.22f + rng.NextFloat() * 0.26f;
                float foot = 0.02f + rng.NextFloat() * 0.05f;
                const int points = 5;
                var spine = new float3[points];
                for (int i = 0; i < points; i++)
                {
                    float s = i / (float)(points - 1);
                    // A parabola peaking at s = 0.62: the outer third of the blade droops under its own weight.
                    float fall = (s - 0.62f) / 0.62f;
                    spine[i] = outward * (foot + reach * (0.5f * s + 0.5f * s * s)) +
                               new float3(0f, -0.05f + (rise + 0.05f) * (1f - fall * fall), 0f);
                }

                float tone = 0.88f + rng.NextFloat() * 0.2f;
                // Accent = the dry tone: tips bleach first.
                Blade(blades, spine, Across(a), 0.03f + rng.NextFloat() * 0.014f, 0.1f + 0.9f * rng.NextFloat(),
                    t => Tint(tone * math.lerp(0.74f, 1.06f, t), 1f - 0.85f * math.smoothstep(0.55f, 1f, t)));
            }

            int stalks = 4 + (int)rng.NextUInt(3);
            for (int s = 0; s < stalks; s++)
            {
                float a = turn + 1.1f + s * math.PI * 2f / stalks + Signed(ref rng) * 0.5f;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                float height = 0.74f + rng.NextFloat() * 0.2f;
                float lean = 0.06f + rng.NextFloat() * 0.12f;
                var spine = new float3[5];
                for (int i = 0; i < spine.Length; i++)
                {
                    float t = i / (float)(spine.Length - 1);
                    spine[i] = outward * (0.02f + lean * t * t) + new float3(0f, -0.05f + t * (height + 0.05f), 0f);
                }

                Tube(stalkWood, spine, 4, (i, k) => 0.008f * math.lerp(1f, 0.6f, i / (float)(spine.Length - 1)),
                    (p, k) => Tint(0.9f, 0.6f));

                // The plume: a fan of drooping racemes, all swept to the side the stalk leans to.
                int racemes = 7 + (int)rng.NextUInt(3);
                float phase = 0.1f + 0.9f * rng.NextFloat();
                for (int r = 0; r < racemes; r++)
                {
                    float ra = a + (r - (racemes - 1) * 0.5f) * 0.36f + Signed(ref rng) * 0.1f;
                    float length = 0.2f + rng.NextFloat() * 0.09f;
                    Blade(plumes, Arc(spine[spine.Length - 1], ra, length, 0.3f + rng.NextFloat() * 0.5f, 0.9f, 4),
                        Across(ra), 0.024f, phase, t => Tint(math.lerp(0.9f, 1.1f, t), 1f - 0.7f * t));
                }
            }

            data.EmitFaceted(blades, 0, FacetCos, null);
            data.EmitSmooth(stalkWood, 0, null);
            data.EmitFaceted(plumes, 1, FacetCos, null);
        }

        // Horizontal unit vector square to azimuth `a`: the width axis of a blade that grows outward along `a`.
        private static float3 Across(float a) => new float3(-math.sin(a), 0f, math.cos(a));

        // `points` along a leaf of `length` leaving `from` toward azimuth `a`: it lifts by `lift` and then falls by `droop`.
        private static float3[] Arc(float3 from, float a, float length, float lift, float droop, int points)
        {
            var outward = new float3(math.cos(a), 0f, math.sin(a));
            var spine = new float3[points];
            for (int i = 0; i < points; i++)
            {
                float s = i / (float)(points - 1);
                spine[i] = from + outward * (length * s) + new float3(0f, length * (lift * s - droop * s * s), 0f);
            }

            return spine;
        }

        // A closed, flattened blade along `spine` (whose last point is the tip): a diamond cross-section `halfWidth`
        // across `across` and about a quarter of that thick. Two broad faces, yet nothing is single-sided, so the
        // scenery shader needs no cull-off variant. `across` must not be parallel to the spine. Emit it faceted: the
        // two halves of a broad face are within the crease angle and shade as one, while a smooth edge vertex would
        // average the upper and lower faces into a normal lying in the blade's plane.
        private static void Blade(Part part, float3[] spine, float3 across, float halfWidth, float phase, Func<float, Color> color)
        {
            int rings = spine.Length - 1;
            int first = part.P.Count;
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)rings;
                float3 tangent = math.normalize(spine[i + 1] - spine[math.max(i - 1, 0)]);
                float3 wide = math.normalize(across - tangent * math.dot(across, tangent));
                float3 thick = math.cross(tangent, wide);
                // Widest a third of the way up, like a leaf; the root keeps some width or its ring collapses.
                float w = halfWidth * math.lerp(0.55f, 1f, math.saturate(t * 3f)) * (1f - 0.55f * t * t);
                for (int k = 0; k < 4; k++)
                {
                    float3 offset = k switch { 0 => wide * w, 1 => thick * (w * 0.28f), 2 => -wide * w, _ => -thick * (w * 0.28f) };
                    float3 p = spine[i] + offset;
                    part.Add(p, color(t), Wind(p.y, math.length(p.xz), phase));
                }
            }

            for (int i = 0; i < rings - 1; i++)
            {
                float3 inside = (spine[i] + spine[i + 1]) * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    int a = first + i * 4 + k;
                    int b = first + i * 4 + (k + 1) % 4;
                    part.Tri(a, b, a + 4, inside);
                    part.Tri(b, b + 4, a + 4, inside);
                }
            }

            float3 end = spine[rings];
            int tip = part.Add(end, color(1f), Wind(end.y, math.length(end.xz), phase));
            int last = first + (rings - 1) * 4;
            for (int k = 0; k < 4; k++)
            {
                part.Tri(last + k, last + (k + 1) % 4, tip, spine[rings - 1]);
            }
        }

        // ---- rocks ---------------------------------------------------------------------------------------------

        private static void BuildBoulder(MeshData data, ref Pcg32 rng, int variant)
        {
            // One variant stands taller than it is wide; the rest lie low (ScatterItem's collider is a low ellipsoid).
            bool standing = variant == 3;
            float3 radii = standing ? new float3(0.4f, 0.46f, 0.36f) : new float3(0.5f, 0.34f, 0.42f);
            radii *= new float3(0.92f + rng.NextFloat() * 0.16f, 0.92f + rng.NextFloat() * 0.16f, 0.92f + rng.NextFloat() * 0.16f);
            float3 centre = new float3(0f, radii.y * 0.62f, 0f);
            Rock(data, centre, radii, 2, 6 + (int)rng.NextUInt(3), ref rng);

            int companions = 1 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < companions; i++)
            {
                float a = turn + i * (2.2f + rng.NextFloat());
                float r = 0.19f + rng.NextFloat() * 0.09f;
                float reach = radii.x * 0.78f + r * 0.5f;
                Rock(data, new float3(math.cos(a) * reach, r * 0.3f, math.sin(a) * reach * 0.85f),
                    new float3(r, r * 0.72f, r * 0.88f), 1, 4, ref rng);
            }
        }

        private static void BuildTor(MeshData data, ref Pcg32 rng, int variant)
        {
            // Joint-bounded granite weathers into cushions: wide, flat-bedded and rounded at every edge, so few and
            // mostly upward cuts.
            float3 r = new float3(0.48f, 0.18f, 0.4f) * (0.92f + rng.NextFloat() * 0.12f);
            float cy = r.y * 0.55f;
            Rock(data, new float3(0f, cy, 0f), r, quaternion.RotateY(rng.NextFloat() * 6.28f), 2, 3, ref rng, 1.2f, 0f, 0);
            float y = cy + r.y * 0.8f;
            int stack = variant % 2 == 0 ? 2 : 1;
            for (int i = 0; i < stack; i++)
            {
                r *= new float3(0.72f, 0.92f, 0.74f);
                cy = y + r.y * 0.7f;
                Rock(data, new float3(Signed(ref rng) * 0.08f, cy, Signed(ref rng) * 0.08f), r,
                    quaternion.RotateY(rng.NextFloat() * 6.28f), 2, 3, ref rng, 1.2f, 0f, 0);
                y = cy + r.y * 0.75f;
            }

            // One block has toppled off and lies at the foot.
            float a = rng.NextFloat() * 6.28f;
            Rock(data, new float3(math.cos(a) * 0.5f, 0.06f, math.sin(a) * 0.45f), new float3(0.14f, 0.1f, 0.12f),
                quaternion.RotateY(a), 1, 3, ref rng, 0.6f, 0f, 0);
        }

        private static void BuildSlabs(MeshData data, ref Pcg32 rng)
        {
            // Cleaved plates all share one dip, stacked along the cleavage normal like books fallen against each other.
            float strike = rng.NextFloat() * math.PI * 2f;
            float dip = math.radians(28f + rng.NextFloat() * 22f);
            quaternion q = math.mul(quaternion.RotateY(strike), quaternion.RotateX(-dip));
            float3 normal = math.mul(q, new float3(0f, 1f, 0f));
            int plates = 3 + (int)rng.NextUInt(3);
            for (int i = 0; i < plates; i++)
            {
                float f = i - (plates - 1) * 0.5f;
                float3 r = new float3(0.36f + rng.NextFloat() * 0.1f, 0.05f + rng.NextFloat() * 0.025f, 0.24f + rng.NextFloat() * 0.06f) *
                           (1f - 0.08f * math.abs(f));
                float3 c = normal * (f * 0.11f) + new float3(Signed(ref rng) * 0.05f, 0.12f + 0.03f * Signed(ref rng), 0f);
                Rock(data, c, r, q, 2, 5, ref rng, 0f, 0f, 0);
            }
        }

        private static void BuildLedge(MeshData data, ref Pcg32 rng)
        {
            float3 radii = new float3(0.5f, 0.36f, 0.42f) *
                           new float3(0.92f + rng.NextFloat() * 0.12f, 0.9f + rng.NextFloat() * 0.15f, 0.92f + rng.NextFloat() * 0.12f);
            // Upward cuts leave the flat cap rock; the beds step its sides.
            Rock(data, new float3(0f, radii.y * 0.5f, 0f), radii, quaternion.RotateY(rng.NextFloat() * 6.28f), 3, 5, ref rng, 1.6f, 0.085f, 0);
            float a = rng.NextFloat() * 6.28f;
            Rock(data, new float3(math.cos(a) * 0.5f, 0.05f, math.sin(a) * 0.42f), new float3(0.16f, 0.09f, 0.13f),
                quaternion.RotateY(a), 2, 4, ref rng, 1.6f, 0.085f, 0);
        }

        private static void BuildCobbles(MeshData data, ref Pcg32 rng)
        {
            // Water- or ice-rounded stones: no flat cut faces, just squashed swollen ellipsoids of mixed sizes.
            float turn = rng.NextFloat() * math.PI * 2f;
            Rock(data, new float3(0f, 0.12f, 0f), new float3(0.27f, 0.19f, 0.23f), quaternion.RotateY(turn), 2, 0, ref rng, 0.2f, 0f, 0);
            int count = 6 + (int)rng.NextUInt(4);
            for (int i = 0; i < count; i++)
            {
                float a = turn + i * GoldenAngle;
                float reach = 0.24f + 0.2f * math.sqrt((i + 0.5f) / count);
                float r = 0.09f + rng.NextFloat() * 0.08f;
                Rock(data, new float3(math.cos(a) * reach, r * 0.35f, math.sin(a) * reach), new float3(r, r * 0.62f, r * 0.82f),
                    quaternion.RotateY(a), r > 0.13f ? 2 : 1, 0, ref rng, 0.2f, 0f, 0);
            }

            Rock(data, new float3(0.05f, 0.33f, 0.02f), new float3(0.13f, 0.09f, 0.11f), quaternion.RotateY(turn + 1f), 1, 0, ref rng,
                0.2f, 0f, 0);
        }

        private static void BuildFluted(MeshData data, ref Pcg32 rng)
        {
            float3 radii = new float3(0.46f, 0.4f, 0.4f) * (0.92f + rng.NextFloat() * 0.12f);
            Rock(data, new float3(0f, radii.y * 0.55f, 0f), radii, quaternion.RotateY(rng.NextFloat() * 6.28f), 3, 2, ref rng, 0.6f, 0f,
                9 + (int)rng.NextUInt(4));
            float a = rng.NextFloat() * 6.28f;
            Rock(data, new float3(math.cos(a) * 0.46f, 0.08f, math.sin(a) * 0.4f), new float3(0.18f, 0.15f, 0.16f),
                quaternion.RotateY(a), 2, 1, ref rng, 0.6f, 0f, 7);
        }

        private static void Rock(MeshData data, float3 centre, float3 radii, int detail, int cuts, ref Pcg32 rng,
            float upBias = 0.2f) =>
            Rock(data, centre, radii, quaternion.identity, detail, cuts, ref rng, upBias, 0f, 0);

        // A noise-displaced ellipsoid cut by random planes: the cuts are the flat chiselled faces, what is left
        // between them stays rounded, and the angle-limited normals keep the edges between the two crisp.
        // bedM > 0 steps the sides into strata of that thickness; flutes > 0 runs that many grooves down from the crest.
        // The shape is built in the rock's own axes and turned by `rotation` before it is placed.
        private static void Rock(MeshData data, float3 centre, float3 radii, quaternion rotation, int detail, int cuts,
            ref Pcg32 rng, float upBias, float bedM, int flutes)
        {
            Ico ico = Ico.Get(detail);
            var local = new float3[ico.Vertices.Length];
            float3 offset = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 53f;
            for (int i = 0; i < local.Length; i++)
            {
                float3 d = ico.Vertices[i];
                float swell = 1f + 0.2f * noise.snoise(d * 1.3f + offset) + 0.07f * noise.snoise(d * 3.4f + offset);
                if (flutes > 0)
                {
                    // Rain runs off the crest, so the grooves are deepest high on the rock and die out toward its foot.
                    float around = math.atan2(d.z, d.x);
                    float groove = math.pow(0.5f + 0.5f * math.cos(around * flutes + offset.x), 6f);
                    swell -= 0.26f * groove * math.smoothstep(-0.3f, 0.5f, d.y) * math.saturate(math.length(d.xz) * 3f);
                }

                local[i] = d * radii * swell;
                if (bedM > 0f)
                {
                    // Soft beds weather back between hard ones: the block reads as stacked strata, not a lump.
                    float bed = math.frac((local[i].y + radii.y) / bedM + offset.y * 0.01f);
                    float soft = math.smoothstep(0.5f, 0.7f, bed) * (1f - math.smoothstep(0.88f, 1f, bed));
                    local[i].x *= 1f - 0.2f * soft;
                    local[i].z *= 1f - 0.2f * soft;
                }
            }

            for (int c = 0; c < cuts; c++)
            {
                float3 dir = math.normalize(new float3(Signed(ref rng), Signed(ref rng) * 0.8f + upBias, Signed(ref rng)) + 1e-3f);
                float reach = 0f;
                foreach (float3 v in local)
                {
                    reach = math.max(reach, math.dot(v, dir));
                }

                float keep = reach * (0.7f + rng.NextFloat() * 0.2f);
                for (int i = 0; i < local.Length; i++)
                {
                    float over = math.dot(local[i], dir) - keep;
                    if (over > 0f)
                    {
                        local[i] -= dir * over;
                    }
                }
            }

            bool turned = !rotation.Equals(quaternion.identity);
            var part = new Part();
            float top = centre.y + radii.y;
            foreach (float3 v in local)
            {
                float3 p = centre + (turned ? math.mul(rotation, v) : v);
                // Contact darkening where the rock meets the ground, and soft strata bands up its side.
                float tone = math.lerp(0.62f, 1f, math.saturate(p.y / (top * 0.45f)));
                float bed = math.frac(p.y / top * 3.2f + noise.snoise(p * 2.3f + offset) * 0.25f);
                tone *= math.lerp(0.93f, 1.05f, math.smoothstep(0.4f, 0.6f, bed));
                part.Add(p, Tint(tone, 1f), float2.zero);
            }

            for (int t = 0; t < ico.Triangles.Length; t += 3)
            {
                part.Tri(ico.Triangles[t], ico.Triangles[t + 1], ico.Triangles[t + 2], centre);
            }

            for (int f = 0; f < part.T.Count / 3; f++)
            {
                part.FaceTone.Add(0.97f + rng.NextFloat() * 0.06f);
            }

            data.EmitFaceted(part, 0, FacetCos, (p, n, c) =>
            {
                // Accent (moss, snow, ash) settles in patches on faces that look up.
                float patch = noise.snoise(p * 2.6f + offset) * 0.5f + 0.5f;
                float settled = math.smoothstep(0.55f, 0.8f, n.y) * math.smoothstep(0.3f, 0.5f, patch);
                c.a = 1f - settled;
                return (n, c);
            });
        }

        // ---- crystals ------------------------------------------------------------------------------------------

        private static void BuildCrystals(MeshData data, ref Pcg32 rng)
        {
            float turn = rng.NextFloat() * math.PI * 2f;
            // The main shaft keeps the old placeholder's footprint and tilt (ScatterItem.BlockRadiusM, VolumeL).
            Shaft(data, float3.zero, turn, math.radians(6f + rng.NextFloat() * 5f), 0.135f, 1f, ref rng);
            int count = 3 + (int)rng.NextUInt(3);
            for (int i = 0; i < count; i++)
            {
                float a = turn + 0.9f + (i + Signed(ref rng) * 0.3f) * math.PI * 2f / count;
                float reach = 0.13f + rng.NextFloat() * 0.08f;
                float height = 0.3f + rng.NextFloat() * 0.34f;
                Shaft(data, new float3(math.cos(a) * reach, 0f, math.sin(a) * reach), a,
                    math.radians(18f + rng.NextFloat() * 22f), 0.055f + rng.NextFloat() * 0.04f, height, ref rng);
            }
        }

        // A hexagonal prism with an off-centre point, tilted away from the cluster along azimuth `away`.
        private static void Shaft(MeshData data, float3 foot, float away, float tilt, float radius, float height, ref Pcg32 rng,
            int subMesh = 0, float buried = 0.14f)
        {
            const int sides = 6;
            float3 outward = new float3(math.cos(away), 0f, math.sin(away));
            quaternion lean = quaternion.AxisAngle(math.normalize(math.cross(new float3(0f, 1f, 0f), outward)), tilt);
            float spin = rng.NextFloat() * math.PI;
            float shoulder = height * (0.74f + rng.NextFloat() * 0.1f);
            var part = new Part();
            for (int ring = 0; ring < 2; ring++)
            {
                for (int k = 0; k < sides; k++)
                {
                    float a = spin + k * math.PI * 2f / sides;
                    float r = radius * (ring == 0 ? 1.05f : 0.92f);
                    // Buried foot: the shaft leans, so its low side must still reach under the ground.
                    float3 p = new float3(math.cos(a) * r, ring == 0 ? -buried : shoulder, math.sin(a) * r);
                    part.Add(foot + math.mul(lean, p), Tint(ring == 0 ? 0.6f : 1.02f, ring == 0 ? 1f : 0.55f), float2.zero);
                }
            }

            float3 point = new float3(Signed(ref rng) * radius * 0.3f, height, Signed(ref rng) * radius * 0.3f);
            int tip = part.Add(foot + math.mul(lean, point), Tint(1.18f, 0f), float2.zero);
            float3 inside = foot + math.mul(lean, new float3(0f, shoulder * 0.5f, 0f));
            for (int k = 0; k < sides; k++)
            {
                int next = (k + 1) % sides;
                float tone = 0.88f + rng.NextFloat() * 0.24f; // each facet catches the light differently
                part.Tri(k, next, sides + k, inside);
                part.FaceTone.Add(tone);
                part.Tri(next, sides + next, sides + k, inside);
                part.FaceTone.Add(tone);
                part.Tri(sides + k, sides + next, tip, inside);
                part.FaceTone.Add(tone * 1.06f);
            }

            data.EmitFaceted(part, subMesh, FacetCos, null);
        }

        // ---- deadwood, swamp and dry-land plants ---------------------------------------------------------------

        private static void BuildSnag(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.12f, 0f, Signed(ref rng) * 0.12f);
            float3[] spine = TrunkSpine(lean, 0.72f + rng.NextFloat() * 0.22f, ref rng);
            var wood = new Part();
            Tube(wood, spine, 7, (i, k) => TrunkRadius(0.06f, i / (float)(spine.Length - 1), k), BarkColor);
            int limbs = 2 + (int)rng.NextUInt(3);
            for (int b = 0; b < limbs; b++)
            {
                float t = 0.42f + rng.NextFloat() * 0.45f;
                float a = rng.NextFloat() * math.PI * 2f;
                float reach = 0.16f + rng.NextFloat() * 0.16f;
                float3 from = SpineAt(spine, t);
                Limb(wood, spine, t, from + new float3(math.cos(a) * reach, 0.04f + rng.NextFloat() * 0.14f, math.sin(a) * reach), 0.024f);
            }

            data.EmitSmooth(wood, 0, null);
        }

        private static void BuildLog(MeshData data, ref Pcg32 rng)
        {
            // Unit = length. Matches ScatterItem's log: a cylinder of radius 0.1 lying along local X.
            const float radius = 0.1f;
            const int rings = 7;
            float bend = Signed(ref rng) * 0.05f;
            float taper = 0.72f + rng.NextFloat() * 0.16f;
            var spine = new float3[rings + 1];
            // A near-zero first ring closes the butt end: Tube leaves its first ring open (a trunk's is underground).
            spine[0] = new float3(-0.4f, radius * 0.7f, 0f);
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                spine[i + 1] = new float3(-0.396f + 0.796f * t, radius * (0.7f - 0.15f * t), bend * math.sin(t * math.PI));
            }

            var wood = new Part();
            Tube(wood, spine, 8,
                (i, k) => i == 0 ? 0.004f : radius * math.lerp(1f, taper, (i - 1) / (float)(rings - 1)) * (1f + 0.06f * math.sin(k * 2.4f + i)),
                // Accent (moss) settles along the top of the trunk; the underside darkens where it meets the ground.
                (p, k) => Tint(math.lerp(0.62f, 1f, math.saturate(p.y / 0.12f)) * (1f + 0.07f * math.sin(k * 2.1f + p.x * 9f)),
                    1f - 0.7f * math.saturate((p.y - 0.1f) / 0.08f)), 0f);

            // One snapped limb still stands up from the trunk, so the log reads from a distance over tall grass.
            float sx = -0.15f + rng.NextFloat() * 0.3f;
            float sz = Signed(ref rng) * 0.14f;
            var stub = new[]
            {
                new float3(sx, radius * 0.6f, 0f),
                new float3(sx + 0.03f, 0.22f, sz * 0.45f),
                new float3(sx + 0.05f, 0.36f + rng.NextFloat() * 0.08f, sz),
            };
            Tube(wood, stub, 5, (i, k) => 0.03f * math.lerp(1f, 0.5f, i * 0.5f), (p, k) => Tint(0.85f, 1f), 0f);
            data.EmitSmooth(wood, 0, null);
        }

        private static void BuildSwampTree(MeshData data, ref Pcg32 rng)
        {
            float3 lean = new float3(Signed(ref rng) * 0.06f, 0f, Signed(ref rng) * 0.06f);
            float3[] spine = TrunkSpine(lean, 0.62f, ref rng);
            float3 top = spine[spine.Length - 1];
            float3 crownCentre = top + new float3(0f, 0.08f, 0f);

            var lobes = new List<Lobe> { new Lobe(crownCentre, new float3(0.3f, 0.16f, 0.3f), 2) };
            int ring = 5 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < ring; i++)
            {
                float a = turn + (i + Signed(ref rng) * 0.2f) * math.PI * 2f / ring;
                float r = 0.17f + rng.NextFloat() * 0.06f;
                float reach = 0.24f + rng.NextFloat() * 0.06f;
                lobes.Add(new Lobe(top + new float3(math.cos(a) * reach, 0.04f + rng.NextFloat() * 0.06f, math.sin(a) * reach),
                    new float3(r, r * 0.72f, r), 2));
            }

            var wood = new Part();
            int rings = spine.Length - 1;
            Tube(wood, spine, 10, (i, k) =>
            {
                float t = i / (float)rings;
                // Plank roots (板根): only alternate sides flare, so the base reads as five fins rather than a cone.
                float fin = (k & 1) == 0 ? 1f : 0.45f;
                return 0.058f * (1f + math.exp(-t * 6f) * (0.4f + 1.7f * fin)) * math.lerp(1f, 0.5f, t);
            }, BarkColor);
            for (int b = 1; b < lobes.Count; b += 2)
            {
                Limb(wood, spine, 0.58f + 0.06f * (b % 3), lobes[b].Centre, 0.028f);
            }

            // Aerial roots and hanging moss (氣根): thin strands dropping from the underside of the crown.
            var strands = new Part();
            int count = 6 + (int)rng.NextUInt(4);
            for (int v = 0; v < count; v++)
            {
                Lobe lobe = lobes[1 + v % (lobes.Count - 1)];
                float a = rng.NextFloat() * math.PI * 2f;
                float3 from = lobe.Centre + new float3(math.cos(a) * lobe.Radii.x * 0.6f, -lobe.Radii.y * 0.3f, math.sin(a) * lobe.Radii.z * 0.6f);
                float length = 0.16f + rng.NextFloat() * 0.2f;
                float drift = Signed(ref rng) * 0.03f;
                var strand = new float3[4];
                for (int i = 0; i < strand.Length; i++)
                {
                    float s = i / 3f;
                    strand[i] = from + new float3(drift * s * s, -length * s, drift * s);
                }

                float tone = 0.7f + rng.NextFloat() * 0.2f;
                Blade(strands, strand, Across(a), 0.012f, 0.1f + 0.9f * rng.NextFloat(), t => Tint(tone * math.lerp(1f, 0.8f, t), 1f));
            }

            data.EmitSmooth(wood, 0, null);
            EmitCrown(data, lobes, 0.52f, 0.92f, 0.14f, ref rng);
            data.EmitFaceted(strands, 1, FacetCos, null);
        }

        private static void BuildCactus(MeshData data, ref Pcg32 rng)
        {
            var body = new Part();
            var flowers = new Part();
            float height = 0.8f + rng.NextFloat() * 0.16f;
            CactusStem(body, new float3(0f, -0.06f, 0f), float3.zero, height + 0.06f, 0.11f);
            CactusFlower(flowers, new float3(0f, height, 0f));
            int arms = 1 + (int)rng.NextUInt(3);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int a = 0; a < arms; a++)
            {
                float az = turn + a * math.PI * 2f / arms + Signed(ref rng) * 0.4f;
                // Tube switches its frame axis when a tangent runs along X; keep arms off that heading so an elbow
                // never straddles the switch (a twisted ring).
                if (math.abs(math.cos(az)) > 0.85f)
                {
                    az += 0.6f;
                }

                var outward = new float3(math.cos(az), 0f, math.sin(az));
                float y = 0.28f + rng.NextFloat() * 0.26f;
                float reach = 0.2f + rng.NextFloat() * 0.07f;
                float rise = 0.2f + rng.NextFloat() * 0.2f;
                CactusStem(body, new float3(0f, y, 0f), outward * reach, rise, 0.062f);
                if (rng.NextFloat() < 0.7f)
                {
                    CactusFlower(flowers, new float3(0f, y + 0.1f + rise, 0f) + outward * reach);
                }
            }

            data.EmitSmooth(body, 0, null);
            data.EmitSmooth(flowers, 1, null);
        }

        // A ribbed stem with a rounded top: straight up from `from`, or first out along `run` and then up (an arm).
        private static void CactusStem(Part part, float3 from, float3 run, float rise, float radius)
        {
            bool arm = math.lengthsq(run) > 0f;
            var points = new List<float3>();
            if (arm)
            {
                points.Add(from);
                points.Add(from + run * 0.55f - new float3(0f, 0.01f, 0f));
                points.Add(from + run * 0.92f + new float3(0f, 0.035f, 0f));
            }

            const int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                points.Add(from + run + new float3(0f, (arm ? 0.1f : 0f) + rise * i / steps, 0f));
            }

            float3[] spine = points.ToArray();
            int last = spine.Length - 1;
            Tube(part, spine, 10,
                // Alternate sides pinch in to make five ribs; the last two rings round the top off.
                (i, k) => radius * (i == last ? 0.5f : i == last - 1 ? 0.86f : 1f) * ((k & 1) == 0 ? 1f : 0.76f),
                (p, k) => Tint(((k & 1) == 0 ? 1.06f : 0.78f) * math.lerp(0.7f, 1f, math.saturate(p.y / 0.15f)), (k & 1) == 0 ? 0.6f : 1f),
                0f);
        }

        private static void CactusFlower(Part part, float3 at) =>
            Blob(part, at + new float3(0f, 0.012f, 0f), new float3(0.04f, 0.022f, 0.04f), 1,
                d => Tint(math.lerp(0.9f, 1.08f, math.saturate(math.length(d.xz))), d.y > 0.6f ? 0f : 1f), -1f);

        private static void BuildMushroom(MeshData data, ref Pcg32 rng)
        {
            float bend = Signed(ref rng) * 0.05f;
            float bendTurn = rng.NextFloat() * math.PI * 2f;
            float stalkTop = 0.6f + rng.NextFloat() * 0.08f;
            var spine = new float3[6];
            for (int i = 0; i < spine.Length; i++)
            {
                float t = i / 5f;
                spine[i] = new float3(math.cos(bendTurn) * bend * t * t, -0.06f + t * (stalkTop + 0.06f), math.sin(bendTurn) * bend * t * t);
            }

            var stalk = new Part();
            Tube(stalk, spine, 9, (i, k) => 0.075f * (1f + 0.55f * math.exp(-i / 5f * 7f)) * math.lerp(1f, 0.78f, i / 5f),
                (p, k) => Tint(math.lerp(0.7f, 1f, math.saturate(p.y / 0.2f)) * (1f + 0.05f * math.sin(k * 2.3f)), 1f), 0f);
            data.EmitSmooth(stalk, 0, null);

            float3 hub = spine[spine.Length - 1];
            float capR = 0.3f + rng.NextFloat() * 0.07f;
            float capH = 0.2f + rng.NextFloat() * 0.1f;
            float3 offset = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 29f;
            const int sides = 16;
            // (radius, height) fractions from the rim up to just under the apex.
            var profile = new[]
            {
                new float2(1f, 0f), new float2(0.97f, 0.22f), new float2(0.8f, 0.55f), new float2(0.52f, 0.84f), new float2(0.22f, 0.97f),
            };
            var top = new Part();
            var gills = new Part();
            float3 inside = hub + new float3(0f, capH * 0.3f, 0f);
            float rimY = hub.y - 0.03f;
            var rim = new int[sides];
            int[] previous = null;
            for (int r = 0; r < profile.Length; r++)
            {
                var current = new int[sides];
                for (int k = 0; k < sides; k++)
                {
                    float a = k * math.PI * 2f / sides;
                    float wobble = 1f + 0.07f * noise.snoise(new float3(math.cos(a), math.sin(a), r * 0.7f) * 1.7f + offset);
                    float radius = capR * profile[r].x * wobble;
                    float3 p = new float3(hub.x + math.cos(a) * radius, rimY + capH * profile[r].y, hub.z + math.sin(a) * radius);
                    // Accent = the cap's spots.
                    bool spot = noise.snoise(p * 9f + offset) > 0.35f;
                    current[k] = top.Add(p, Tint(math.lerp(0.82f, 1.06f, profile[r].y), spot ? 0.1f : 1f), float2.zero);
                    if (r == 0)
                    {
                        rim[k] = gills.Add(p, Tint(0.6f, 1f), float2.zero);
                    }
                }

                if (previous != null)
                {
                    for (int k = 0; k < sides; k++)
                    {
                        int next = (k + 1) % sides;
                        top.Tri(previous[k], previous[next], current[k], inside);
                        top.Tri(previous[next], current[next], current[k], inside);
                    }
                }

                previous = current;
            }

            int apex = top.Add(new float3(hub.x, rimY + capH, hub.z), Tint(1.08f, 1f), float2.zero);
            int centre = gills.Add(hub - new float3(0f, 0.01f, 0f), Tint(0.42f, 1f), float2.zero);
            for (int k = 0; k < sides; k++)
            {
                int next = (k + 1) % sides;
                top.Tri(previous[k], previous[next], apex, inside);
                gills.Tri(centre, rim[k], rim[next], inside);
            }

            // Separate parts so the rim between the lit cap and the dark gills stays a hard crease.
            data.EmitSmooth(top, 1, null);
            data.EmitSmooth(gills, 1, null);
        }

        // ---- pillars and mineral outcrops ----------------------------------------------------------------------

        private static void BuildSpire(MeshData data, ref Pcg32 rng)
        {
            float lean = Signed(ref rng) * 0.04f;
            // Sideways and downward cuts only: they plane the flanks into facets and leave the point standing.
            Rock(data, new float3(lean, 0.42f, -lean), new float3(0.19f, 0.52f + rng.NextFloat() * 0.06f, 0.18f), 2,
                5 + (int)rng.NextUInt(3), ref rng, -0.25f);
            int companions = 2 + (int)rng.NextUInt(2);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int i = 0; i < companions; i++)
            {
                float a = turn + i * (1.9f + rng.NextFloat());
                float h = 0.16f + rng.NextFloat() * 0.14f;
                Rock(data, new float3(math.cos(a) * 0.22f, h * 0.5f, math.sin(a) * 0.2f), new float3(0.09f, h, 0.085f), 1, 3, ref rng, -0.25f);
            }
        }

        private static readonly int[] ColumnCells = { 0, 0, 1, 0, 0, 1, -1, 1, -1, 0, 0, -1, 1, -1, 2, -1, -2, 1, 1, 1 };

        private static void BuildColumns(MeshData data, ref Pcg32 rng)
        {
            const int sides = 6;
            const float radius = 0.085f;
            // Columnar joints pack as a honeycomb: every prism shares one orientation and sits on a hex lattice.
            float spacing = radius * math.sqrt(3f);
            float turn = rng.NextFloat() * math.PI * 2f;
            int count = 6 + (int)rng.NextUInt(5);
            var part = new Part();
            for (int c = 0; c < count; c++)
            {
                float q = ColumnCells[c * 2];
                float r = ColumnCells[c * 2 + 1];
                var cell = new float2(q + r * 0.5f, r * 0.8660254f) * spacing;
                var foot = new float3(cell.x * math.cos(turn) - cell.y * math.sin(turn), 0f, cell.x * math.sin(turn) + cell.y * math.cos(turn));
                float height = c == 0
                    ? 0.84f + rng.NextFloat() * 0.16f
                    : math.max(0.18f, math.lerp(0.8f, 0.3f, math.length(cell) / (spacing * 1.8f)) + Signed(ref rng) * 0.14f);
                int first = part.P.Count;
                for (int ring = 0; ring < 3; ring++)
                {
                    for (int k = 0; k < sides; k++)
                    {
                        float a = turn + math.PI / 6f + k * math.PI * 2f / sides;
                        float3 p = foot + new float3(math.cos(a) * radius, ring == 0 ? -0.12f : height, math.sin(a) * radius);
                        // Ring 2 repeats the top ring for the cap: accent (moss, snow, ash) settles on the flat tops.
                        part.Add(p, Tint(ring == 0 ? 0.6f : 1f, ring == 2 ? 0.25f : 1f), float2.zero);
                    }
                }

                int hub = part.Add(foot + new float3(0f, height + 0.012f, 0f), Tint(1.06f, 0.25f), float2.zero);
                float3 inside = foot + new float3(0f, height * 0.5f, 0f);
                for (int k = 0; k < sides; k++)
                {
                    int next = (k + 1) % sides;
                    float tone = 0.86f + rng.NextFloat() * 0.2f;
                    part.Tri(first + k, first + next, first + sides + k, inside);
                    part.FaceTone.Add(tone);
                    part.Tri(first + next, first + sides + next, first + sides + k, inside);
                    part.FaceTone.Add(tone);
                    part.Tri(first + 2 * sides + k, first + 2 * sides + next, hub, inside);
                    part.FaceTone.Add(1.08f);
                }
            }

            data.EmitFaceted(part, 0, FacetCos, null);
        }

        private static void BuildOutcrop(MeshData data, ref Pcg32 rng)
        {
            // The host rock fills ScatterItem's vein ellipsoid (semi-axes 0.45, 0.35, 0.4).
            float3 radii = new float3(0.45f, 0.35f, 0.4f) *
                           new float3(0.92f + rng.NextFloat() * 0.14f, 0.92f + rng.NextFloat() * 0.14f, 0.92f + rng.NextFloat() * 0.14f);
            float3 centre = new float3(0f, radii.y * 0.6f, 0f);
            Rock(data, centre, radii, 2, 7 + (int)rng.NextUInt(3), ref rng);

            // The mineral: shards breaking out of the upper faces, each pointing away from the rock's heart.
            int shards = 6 + (int)rng.NextUInt(5);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int s = 0; s < shards; s++)
            {
                float az = turn + s * GoldenAngle;
                float polar = math.radians(18f + rng.NextFloat() * 58f);
                var d = new float3(math.sin(polar) * math.cos(az), math.cos(polar), math.sin(polar) * math.sin(az));
                Shaft(data, centre + d * radii * 0.72f, az, polar, 0.035f + rng.NextFloat() * 0.03f, 0.13f + rng.NextFloat() * 0.11f,
                    ref rng, 1, 0.06f);
            }
        }

        // ---- ground plants -------------------------------------------------------------------------------------

        private static void BuildFern(MeshData data, ref Pcg32 rng)
        {
            // Unit = width. Each frond is a rib with paired leaflets swept toward its tip.
            var fronds = new Part();
            int count = 9 + (int)rng.NextUInt(4);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int f = 0; f < count; f++)
            {
                float a = turn + f * GoldenAngle + Signed(ref rng) * 0.15f;
                // The first frond is the tallest, so every variant keeps the rosette's height.
                float length = f == 0 ? 0.56f : 0.42f + rng.NextFloat() * 0.14f;
                float lift = f == 0 ? 2.2f : 1.5f + rng.NextFloat() * 0.6f;
                float3[] rib = Arc(new float3(0f, -0.05f, 0f), a, length, lift, 1.25f, 6);
                float tone = 0.86f + rng.NextFloat() * 0.2f;
                float phase = 0.1f + 0.9f * rng.NextFloat();
                Blade(fronds, rib, Across(a), 0.02f, phase, t => Tint(tone * 0.78f, 1f));
                for (int i = 1; i <= 4; i++)
                {
                    float leaflet = length * (0.3f - 0.045f * i);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float la = a + side * 1.2f;
                        Blade(fronds, Arc(rib[i], la, leaflet, 0.25f, 0.55f, 3), Across(la), leaflet * 0.3f, phase,
                            t => Tint(tone * math.lerp(0.9f, 1.08f, t), 1f - 0.6f * t));
                    }
                }
            }

            data.EmitFaceted(fronds, 0, FacetCos, null);
        }

        private static void BuildReed(MeshData data, ref Pcg32 rng)
        {
            var stems = new Part();
            var leaves = new Part();
            var heads = new Part();
            int count = 12 + (int)rng.NextUInt(7);
            float turn = rng.NextFloat() * math.PI * 2f;
            for (int c = 0; c < count; c++)
            {
                float a = turn + c * GoldenAngle;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                // Feet stay inside the rule layer's 0.2 stand radius.
                float foot = 0.02f + 0.16f * math.sqrt((c + 0.5f) / count);
                float height = c == 0 ? 0.96f : 0.66f + rng.NextFloat() * 0.3f;
                float lean = 0.03f + rng.NextFloat() * 0.08f;
                var spine = new float3[5];
                for (int i = 0; i < spine.Length; i++)
                {
                    float t = i / 4f;
                    spine[i] = outward * (foot + lean * t * t) + new float3(0f, -0.05f + t * (height + 0.05f), 0f);
                }

                Tube(stems, spine, 4, (i, k) => 0.009f * math.lerp(1f, 0.6f, i / 4f),
                    (p, k) => Tint(math.lerp(0.75f, 1f, math.saturate(p.y / 0.2f)), 1f));
                float phase = 0.1f + 0.9f * rng.NextFloat();
                for (int l = 0; l < 2; l++)
                {
                    float la = a + 1.3f + l * 2.6f + Signed(ref rng) * 0.4f;
                    float3 node = SpineAt(spine, 0.3f + l * 0.22f + rng.NextFloat() * 0.1f);
                    float length = 0.26f + rng.NextFloat() * 0.14f;
                    float tone = 0.88f + rng.NextFloat() * 0.18f;
                    Blade(leaves, Arc(node, la, length, 1.5f, 0.9f, 4), Across(la), 0.016f, phase,
                        t => Tint(tone * math.lerp(0.85f, 1.05f, t), 1f - 0.6f * math.smoothstep(0.5f, 1f, t)));
                }

                if ((c & 1) == 0)
                {
                    Blob(heads, SpineAt(spine, 0.9f), new float3(0.02f, 0.075f, 0.02f), 0,
                        d => Tint(math.lerp(0.82f, 1.05f, d.y * 0.5f + 0.5f), 1f), phase);
                }
            }

            data.EmitSmooth(stems, 0, null);
            data.EmitFaceted(leaves, 0, FacetCos, null);
            data.EmitSmooth(heads, 1, null);
        }

        private static void BuildFlowerPatch(MeshData data, ref Pcg32 rng)
        {
            // Unit = width: a low cushion of leaves with blossoms held just above it.
            var leaves = new Part();
            var stems = new Part();
            var blossoms = new Part();
            float turn = rng.NextFloat() * math.PI * 2f;
            int tufts = 14 + (int)rng.NextUInt(6);
            for (int b = 0; b < tufts; b++)
            {
                float a = turn + b * GoldenAngle;
                float foot = 0.05f + 0.3f * math.sqrt((b + 0.5f) / tufts);
                float la = a + Signed(ref rng) * 0.8f;
                float length = 0.14f + rng.NextFloat() * 0.1f;
                float tone = 0.86f + rng.NextFloat() * 0.2f;
                Blade(leaves, Arc(new float3(math.cos(a) * foot, -0.05f, math.sin(a) * foot), la, length, 1.3f, 0.9f, 4), Across(la),
                    0.035f, 0.1f + 0.9f * rng.NextFloat(), t => Tint(tone * math.lerp(0.8f, 1.05f, t), 1f));
            }

            int flowers = 9 + (int)rng.NextUInt(6);
            for (int f = 0; f < flowers; f++)
            {
                float a = turn + 0.7f + f * GoldenAngle;
                var outward = new float3(math.cos(a), 0f, math.sin(a));
                float foot = 0.04f + 0.34f * math.sqrt((f + 0.5f) / flowers);
                float height = f == 0 ? 0.4f : 0.2f + rng.NextFloat() * 0.2f;
                float lean = rng.NextFloat() * 0.04f;
                var spine = new[]
                {
                    outward * foot + new float3(0f, -0.05f, 0f),
                    outward * (foot + lean * 0.4f) + new float3(0f, height * 0.5f, 0f),
                    outward * (foot + lean) + new float3(0f, height, 0f),
                };
                Tube(stems, spine, 4, (i, k) => 0.006f, (p, k) => Tint(0.9f, 1f));
                float size = 0.04f + rng.NextFloat() * 0.02f;
                float tone = 0.9f + rng.NextFloat() * 0.18f;
                // Accent = the blossom's eye.
                Blob(blossoms, spine[2], new float3(size, size * 0.45f, size), 1,
                    d => Tint(tone * math.lerp(0.85f, 1.08f, math.saturate(math.length(d.xz))), d.y > 0.6f ? 0f : 1f),
                    0.1f + 0.9f * rng.NextFloat());
            }

            data.EmitFaceted(leaves, 0, FacetCos, null);
            data.EmitSmooth(stems, 0, null);
            data.EmitSmooth(blossoms, 1, null);
        }

        // A small icosphere scaled by `radii`; phase < 0 = rigid (no wind).
        private static void Blob(Part part, float3 centre, float3 radii, int detail, Func<float3, Color> color, float phase)
        {
            Ico ico = Ico.Get(detail);
            int first = part.P.Count;
            foreach (float3 d in ico.Vertices)
            {
                float3 p = centre + d * radii;
                part.Add(p, color(d), phase < 0f ? float2.zero : Wind(p.y, math.length(p.xz), phase));
            }

            for (int t = 0; t < ico.Triangles.Length; t += 3)
            {
                part.Tri(first + ico.Triangles[t], first + ico.Triangles[t + 1], first + ico.Triangles[t + 2], centre);
            }
        }

        // ---- shared --------------------------------------------------------------------------------------------

        private static float Signed(ref Pcg32 rng) => rng.NextFloat() * 2f - 1f;

        // rgb = multiplicative tone, a = 1 − accent mask (ToonScenery's vertex colour layout).
        private static Color Tint(float tone, float accentFree) => new Color(tone, tone, tone, accentFree);

        private readonly struct Lobe
        {
            public readonly float3 Centre;
            public readonly float3 Radii;
            public readonly int Detail;

            public Lobe(float3 centre, float3 radii, int detail)
            {
                Centre = centre;
                Radii = radii;
                Detail = detail;
            }
        }

        /// <summary>An indexed piece of geometry before its normals are decided.</summary>
        internal sealed class Part
        {
            public readonly List<float3> P = new List<float3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<float2> W = new List<float2>();
            public readonly List<int> T = new List<int>();
            public readonly List<float> FaceTone = new List<float>(); // per triangle; empty = 1

            public int Add(float3 position, Color color, float2 wind)
            {
                P.Add(position);
                C.Add(color);
                W.Add(wind);
                return P.Count - 1;
            }

            // Winds the triangle so it faces away from `inside` (Unity: clockwise is front, cross(b−a, c−a) is its
            // normal), which spares every caller from tracking handedness through mirrored and rotated pieces.
            public void Tri(int a, int b, int c, float3 inside)
            {
                float3 normal = math.cross(P[b] - P[a], P[c] - P[a]);
                float3 centroid = (P[a] + P[b] + P[c]) / 3f;
                if (math.dot(normal, centroid - inside) < 0f)
                {
                    (b, c) = (c, b);
                }

                T.Add(a);
                T.Add(b);
                T.Add(c);
            }
        }

        internal delegate (float3 normal, Color color) Finish(float3 position, float3 normal, Color color);

        internal sealed class MeshData
        {
            private readonly List<Vector3> _positions = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<Vector2> _wind = new List<Vector2>();
            private readonly List<int>[] _triangles;
            private readonly int _fixedSubMeshes;

            // fixedSubMeshes 0 = scenery props: wood, plus foliage only when a tree filled it. ArchitectureMeshes passes
            // its own count so every building has the same material slots, used or not.
            public MeshData(int fixedSubMeshes = 0)
            {
                _fixedSubMeshes = fixedSubMeshes;
                _triangles = new List<int>[math.max(2, fixedSubMeshes)];
                for (int i = 0; i < _triangles.Length; i++)
                {
                    _triangles[i] = new List<int>();
                }
            }

            // Shared vertices, area-weighted normals: one continuous surface.
            public void EmitSmooth(Part part, int subMesh, Finish finish)
            {
                var normals = new float3[part.P.Count];
                for (int t = 0; t < part.T.Count; t += 3)
                {
                    float3 n = FaceNormal(part, t);
                    normals[part.T[t]] += n;
                    normals[part.T[t + 1]] += n;
                    normals[part.T[t + 2]] += n;
                }

                int first = _positions.Count;
                for (int i = 0; i < part.P.Count; i++)
                {
                    Push(part.P[i], math.normalizesafe(normals[i], new float3(0f, 1f, 0f)), part.C[i], part.W[i], finish);
                }

                foreach (int index in part.T)
                {
                    _triangles[subMesh].Add(first + index);
                }
            }

            // Three vertices per triangle; a corner's normal averages only the neighbouring faces within the crease
            // angle, so flat cuts shade flat and rounded stretches shade round.
            public void EmitFaceted(Part part, int subMesh, float creaseCos, Finish finish)
            {
                int faces = part.T.Count / 3;
                var faceNormals = new float3[faces];
                var around = new List<int>[part.P.Count];
                for (int f = 0; f < faces; f++)
                {
                    faceNormals[f] = FaceNormal(part, f * 3);
                    for (int k = 0; k < 3; k++)
                    {
                        int v = part.T[f * 3 + k];
                        (around[v] ??= new List<int>()).Add(f);
                    }
                }

                for (int f = 0; f < faces; f++)
                {
                    float3 flat = math.normalizesafe(faceNormals[f], new float3(0f, 1f, 0f));
                    float tone = part.FaceTone.Count > 0 ? part.FaceTone[f] : 1f;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = part.T[f * 3 + k];
                        float3 sum = float3.zero;
                        foreach (int other in around[v])
                        {
                            if (math.dot(math.normalizesafe(faceNormals[other]), flat) >= creaseCos)
                            {
                                sum += faceNormals[other];
                            }
                        }

                        Color c = part.C[v];
                        c.r *= tone;
                        c.g *= tone;
                        c.b *= tone;
                        _triangles[subMesh].Add(_positions.Count);
                        Push(part.P[v], math.normalizesafe(sum, flat), c, part.W[v], finish);
                    }
                }
            }

            // Area-weighted (unnormalised) so slivers barely steer the vertex normal.
            private static float3 FaceNormal(Part part, int t) =>
                math.cross(part.P[part.T[t + 1]] - part.P[part.T[t]], part.P[part.T[t + 2]] - part.P[part.T[t]]);

            private void Push(float3 position, float3 normal, Color color, float2 wind, Finish finish)
            {
                if (finish != null)
                {
                    (normal, color) = finish(position, normal, color);
                }

                _positions.Add(position);
                _normals.Add(normal);
                _colors.Add(color);
                _wind.Add(wind);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                mesh.SetUVs(1, _wind);
                mesh.subMeshCount = _fixedSubMeshes > 0 ? _fixedSubMeshes : _triangles[1].Count > 0 ? 2 : 1;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    mesh.SetTriangles(_triangles[s], s);
                }

                mesh.RecalculateBounds();
                // The vertex shader leans crowns downwind; static bounds must already contain that, or a tree at the
                // screen edge is culled while its swaying crown is still in view.
                Bounds bounds = mesh.bounds;
                bounds.Expand(0.35f);
                mesh.bounds = bounds;
                return mesh;
            }
        }

        /// <summary>Unit icosphere, subdivided; shared read-only data.</summary>
        internal sealed class Ico
        {
            private static readonly Dictionary<int, Ico> Cache = new Dictionary<int, Ico>();

            public float3[] Vertices;
            public int[] Triangles;

            public static Ico Get(int detail)
            {
                if (Cache.TryGetValue(detail, out Ico ico))
                {
                    return ico;
                }

                float g = (1f + math.sqrt(5f)) * 0.5f;
                var vertices = new List<float3>
                {
                    new float3(-1, g, 0), new float3(1, g, 0), new float3(-1, -g, 0), new float3(1, -g, 0),
                    new float3(0, -1, g), new float3(0, 1, g), new float3(0, -1, -g), new float3(0, 1, -g),
                    new float3(g, 0, -1), new float3(g, 0, 1), new float3(-g, 0, -1), new float3(-g, 0, 1),
                };
                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices[i] = math.normalize(vertices[i]);
                }

                var triangles = new List<int>
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
                };
                for (int d = 0; d < detail; d++)
                {
                    var midpoints = new Dictionary<long, int>();
                    var next = new List<int>(triangles.Count * 4);
                    for (int t = 0; t < triangles.Count; t += 3)
                    {
                        int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        int ab = Midpoint(vertices, midpoints, a, b);
                        int bc = Midpoint(vertices, midpoints, b, c);
                        int ca = Midpoint(vertices, midpoints, c, a);
                        next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                    }

                    triangles = next;
                }

                ico = new Ico { Vertices = vertices.ToArray(), Triangles = triangles.ToArray() };
                Cache[detail] = ico;
                return ico;
            }

            private static int Midpoint(List<float3> vertices, Dictionary<long, int> midpoints, int a, int b)
            {
                long key = ((long)math.min(a, b) << 32) | (uint)math.max(a, b);
                if (!midpoints.TryGetValue(key, out int index))
                {
                    index = vertices.Count;
                    vertices.Add(math.normalize(vertices[a] + vertices[b]));
                    midpoints[key] = index;
                }

                return index;
            }
        }
    }
}
