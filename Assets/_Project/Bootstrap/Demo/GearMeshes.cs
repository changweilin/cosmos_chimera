using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Colours of one gear item, already shifted, aged and rusted.</summary>
    /// <remarks>Pre: built by <see cref="GearMeshes.PaletteFor"/>. Post: immutable; every channel in [0, 1]. Side effects: none.</remarks>
    public readonly struct GearPalette
    {
        internal GearPalette(Color main, Color second, Color metal, Color accent, Color edge, Color wrap)
        {
            Main = main;
            Second = second;
            Metal = metal;
            Accent = accent;
            Edge = edge;
            Wrap = wrap;
        }

        /// <summary>Cloth, lacquer or main wood.</summary>
        /// <remarks>Pre: none. Post: opaque colour. Side effects: none.</remarks>
        public Color Main { get; }

        /// <summary>Hafts, grips, linings.</summary>
        /// <remarks>Pre: none. Post: opaque colour. Side effects: none.</remarks>
        public Color Second { get; }

        /// <summary>Blades, heads, rims (bone or stone in the primal era, composite in the future era).</summary>
        /// <remarks>Pre: none. Post: opaque colour. Side effects: none.</remarks>
        public Color Metal { get; }

        /// <summary>Motif bands, gems, cores.</summary>
        /// <remarks>Pre: none. Post: opaque colour. Side effects: none.</remarks>
        public Color Accent { get; }

        /// <summary>Honed edges and worn high points of <see cref="Metal"/>: brighter, or duller once rusted.</summary>
        /// <remarks>Pre: none. Post: opaque colour, never darker than Metal for undamaged gear. Side effects: none.</remarks>
        public Color Edge { get; }

        /// <summary>Grip leather, cord and lashing of the parts era.</summary>
        /// <remarks>Pre: none. Post: opaque colour. Side effects: none.</remarks>
        public Color Wrap { get; }
    }

    /// <summary>
    /// Placeholder gear meshes built from a <see cref="GearLookSpec"/> alone, until the hand-made part library (P1-16)
    /// replaces the mesh source. Each item is modelled at life size in metres with its grip (or wearing point) at the
    /// origin and its length along +Y.
    /// </summary>
    /// <remarks>
    /// REQ: docs/design/gear-looks.md#美術路線分段 (step 2). Vertex-coloured, faceted, for the ToonScenery shader.
    /// Every part slot of <see cref="GearLooks.SlotCount"/> picks one of <see cref="GearLooks.VariantsPerSlot"/> shapes, and
    /// motif slots engrave the region's signature motif (gear-looks.md#地區與時代的器物語彙), so two seeds of one kind
    /// differ in silhouette, not only in tint. Split by item family: GearMeshes.Arms.cs, GearMeshes.Wear.cs, and the
    /// shared parts in GearMeshes.Parts.cs.
    /// Pre: main thread (creates Meshes). Post: same spec → identical mesh. Side effects: allocates Meshes.
    /// </remarks>
    public static partial class GearMeshes
    {
        private const float FacetCos = 0.8f; // same crease angle as SceneryMeshes and PropMeshes
        private const ulong Salt = 0x6765_6172_6D65_7368ul; // "gearmesh"
        private const ulong MotifSalt = 0x6765_6172_6D6F_7466ul; // "gearmotf"

        /// <summary>Colourways per region in the placeholder palette (地區色票).</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int ColourwaysPerRegion = 3;

        private static readonly Color Rust = new Color(0.46f, 0.25f, 0.14f);
        private static readonly Color Grime = new Color(0.33f, 0.30f, 0.26f);
        private static readonly Color Soot = new Color(0.1f, 0.09f, 0.09f);

        /// <summary>Nominal length (m) of each kind before the spec's Length multiplier.</summary>
        /// <remarks>Pre: kind defined. Post: &gt; 0. Side effects: none.</remarks>
        public static float NominalLength(GearKind kind)
        {
            Contract.Requires(Enum.IsDefined(typeof(GearKind), kind), "kind must be defined");
            return kind switch
            {
                GearKind.Dagger => 0.38f,
                GearKind.Sword => 0.95f,
                GearKind.Axe => 0.72f,
                GearKind.Mace => 0.7f,
                GearKind.Twinblade => 1.8f,
                GearKind.Spear => 2.1f,
                GearKind.Greatsword => 1.55f,
                GearKind.GreataxeHammer => 1.4f,
                GearKind.ThrowingBlade => 0.26f,
                GearKind.HandCrossbow => 0.45f,
                GearKind.Wand => 0.42f,
                GearKind.Javelin => 1.45f,
                GearKind.RepeaterBow => 0.9f,
                GearKind.Longbow => 1.65f,
                GearKind.MagicStaff => 1.75f,
                GearKind.HeavyCrossbow => 1.1f,
                GearKind.Shield => 0.75f,
                GearKind.Upper => 0.7f,
                GearKind.Lower => 0.95f,
                GearKind.Headwear => 0.3f,
                _ => 0.14f,
            };
        }

        /// <summary>Which of the <see cref="ColourwaysPerRegion"/> colourways <paramref name="spec"/> is drawn in.</summary>
        /// <remarks>
        /// Derived from the part variants so it adds no draw to the sampled stream (GearLooks' snapshot stays the only
        /// sampling contract). Pre: spec non-null. Post: in [0, ColourwaysPerRegion). Side effects: none.
        /// </remarks>
        public static int ColourwayOf(GearLookSpec spec)
        {
            Contract.Requires(spec != null, "spec required");
            ulong h = 1469598103934665603ul ^ (ulong)spec.Kind;
            foreach (int v in spec.PartVariants) h = (h ^ (ulong)v) * 1099511628211ul;
            h ^= math.asuint(spec.Width);
            h *= 1099511628211ul;
            return (int)((h >> 33) % ColourwaysPerRegion);
        }

        /// <summary>Colours of <paramref name="spec"/>: palette region for cloth, parts era for metal and wrap, motif region for accents.</summary>
        /// <remarks>
        /// REQ: gear-looks.md#地區與時代的器物語彙. Pre: spec non-null.
        /// Post: deterministic; Age darkens toward grime, Damage rusts metal (except composite and bone). Side effects: none.
        /// </remarks>
        public static GearPalette PaletteFor(GearLookSpec spec)
        {
            Contract.Requires(spec != null, "spec required");
            return PaletteFrom(spec.Palette, spec.Motif, spec.Parts, spec.HueShift, spec.ValueShift, spec.Age, spec.Damage, ColourwayOf(spec));
        }

        private static GearPalette PaletteFrom(GearStyle palette, GearStyle motif, GearStyle parts,
            float hueShift, float valueShift, float age, float damage, int colourway)
        {
            (Color main, Color second, _) = RegionColours(palette.Region, colourway);
            (_, _, Color accent) = RegionColours(motif.Region, colourway);
            Color metal = EraMetal(parts.Era);
            Color wrap = EraWrap(parts.Era);
            main = Shift(main, hueShift, valueShift);
            second = Shift(second, hueShift * 0.5f, valueShift);
            if (parts.Era == ArchEra.Future) accent = new Color(0.45f, 0.92f, 1f);
            else if (parts.Era == ArchEra.Ancient) accent = new Color(0.55f, 1f, 0.78f);
            bool corrodes = parts.Era != ArchEra.Future && parts.Era != ArchEra.Primal;
            if (corrodes) metal = Color.Lerp(metal, Rust, damage * 0.6f);
            // Honed edges catch the light; bone, stone and rust do not, so their edge only lifts a little.
            float hone = parts.Era == ArchEra.Primal || parts.Era == ArchEra.Ancient ? 0.15f : 0.5f * (1f - damage);
            Color edge = Color.Lerp(metal, Color.white, hone);
            return new GearPalette(Age(main, age), Age(second, age), Age(metal, age * 0.7f), Age(accent, age * 0.5f),
                Age(edge, age * 0.7f), Age(wrap, age * 0.5f));
        }

        /// <summary>Builds the placeholder mesh of one item.</summary>
        /// <remarks>
        /// Pre: spec non-null; main thread.
        /// Post: non-empty mesh named "Gear_&lt;kind&gt;"; same spec → identical vertices, colours and triangles; every vertex
        /// within 1.7 × NominalLength × Length of the origin. Side effects: allocates a Mesh.
        /// </remarks>
        public static Mesh Build(GearLookSpec spec)
        {
            Contract.Requires(spec != null, "spec required");
            var data = new SceneryMeshes.MeshData();
            data.EmitFaceted(BuildPart(spec), 0, FacetCos, null);
            return data.ToMesh("Gear_" + spec.Kind);
        }

        internal static SceneryMeshes.Part BuildPart(GearLookSpec spec)
        {
            Contract.Requires(spec != null, "spec required");
            var g = new Kit(spec, PaletteFor(spec));
            switch (spec.Kind)
            {
                case GearKind.Dagger:
                case GearKind.Sword:
                case GearKind.Greatsword:
                    BuildSword(g);
                    break;
                case GearKind.Axe:
                    BuildAxe(g);
                    break;
                case GearKind.Mace:
                case GearKind.GreataxeHammer:
                    BuildBlunt(g);
                    break;
                case GearKind.Twinblade:
                    BuildTwinblade(g);
                    break;
                case GearKind.Spear:
                    BuildSpear(g);
                    break;
                case GearKind.Javelin:
                    BuildJavelin(g);
                    break;
                case GearKind.ThrowingBlade:
                    BuildThrowingBlade(g);
                    break;
                case GearKind.Longbow:
                case GearKind.RepeaterBow:
                    BuildBow(g);
                    break;
                case GearKind.HandCrossbow:
                case GearKind.HeavyCrossbow:
                    BuildCrossbow(g);
                    break;
                case GearKind.Wand:
                case GearKind.MagicStaff:
                    BuildStaff(g);
                    break;
                case GearKind.Shield:
                    BuildShield(g);
                    break;
                case GearKind.Upper:
                    BuildUpper(g);
                    break;
                case GearKind.Lower:
                    BuildLower(g);
                    break;
                case GearKind.Headwear:
                    BuildHeadwear(g);
                    break;
                default:
                    BuildCharm(g);
                    break;
            }

            Shade(g);
            return g.Part;
        }

        // Baked light: a soft top-to-bottom gradient gives the toon shader a second tone inside each lit band, and aged
        // gear collects grime toward the end that touches the ground (hems, butts, lower limbs).
        private static void Shade(Kit g)
        {
            List<float3> p = g.Part.P;
            if (p.Count == 0) return;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float3 v in p)
            {
                lo = math.min(lo, v.y);
                hi = math.max(hi, v.y);
            }

            float span = math.max(hi - lo, 1e-4f);
            float grime = g.Spec.Age * 0.45f;
            for (int i = 0; i < p.Count; i++)
            {
                float t = (p[i].y - lo) / span;
                Color c = g.Part.C[i] * math.lerp(0.86f, 1.04f, t);
                if (grime > 0f && t < 0.3f) c = Color.Lerp(c, Grime, grime * (1f - t / 0.3f));
                c.r = math.saturate(c.r);
                c.g = math.saturate(c.g);
                c.b = math.saturate(c.b);
                c.a = 1f;
                g.Part.C[i] = c;
            }
        }

        // ---------------------------------------------------------------- block shield (P1-17d)

        /// <summary>Builds a creature block shield VFX mesh from <paramref name="spec"/> (P1-17d).</summary>
        /// <remarks>
        /// REQ: docs/design/gear-looks.md#宙獸格擋護盾.
        /// Pre: main thread.
        /// Post: non-empty mesh named "BlockShield_Patterned" or "BlockShield_Plain"; facing +Z.
        /// Side effects: allocates a Mesh.
        /// </remarks>
        public static Mesh BuildBlockShield(in BlockShieldSpec spec)
        {
            var part = new SceneryMeshes.Part();
            const float w = 0.45f;
            const float h = 0.55f;
            const int n = 6;
            Color faceCol;
            Color rimCol;
            Color motifCol;
            if (spec.HasPattern)
            {
                GearPalette pal = PaletteFrom(spec.Palette, spec.Motif, spec.Motif,
                    spec.HueShift, spec.ValueShift, spec.Age, spec.Damage, 0);
                (_, _, Color borderAccent) = RegionColours(spec.Border.Region, 0);
                faceCol = pal.Main;
                rimCol = Color.Lerp(pal.Metal, borderAccent, 0.45f);
                motifCol = pal.Accent;
            }
            else
            {
                faceCol = Color.Lerp(spec.ElementColor, Color.white, 0.25f);
                rimCol = Color.Lerp(spec.ElementColor, Color.white, 0.55f);
                motifCol = faceCol;
            }

            var rim = new float3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * math.PI / n;
                rim[i] = new float3(math.sin(a) * w, math.cos(a) * h, 0f);
            }

            int cf = part.Add(new float3(0f, 0f, 0.035f), faceCol, float2.zero);
            int cb = part.Add(new float3(0f, 0f, -0.02f), Color.Lerp(faceCol, Color.black, 0.2f), float2.zero);
            int first = part.P.Count;
            for (int i = 0; i < n; i++)
            {
                part.Add(rim[i] + new float3(0f, 0f, 0.01f), faceCol, float2.zero);
                part.Add(rim[i] - new float3(0f, 0f, 0.02f), rimCol, float2.zero);
            }
            var inside = new float3(0f, 0f, -0.005f);
            for (int i = 0; i < n; i++)
            {
                int a = first + i * 2, b = first + ((i + 1) % n) * 2;
                part.Tri(cf, a, b, inside);
                part.Tri(cb, a + 1, b + 1, inside);
                part.Tri(a, b, a + 1, inside);
                part.Tri(b, b + 1, a + 1, inside);
            }

            if (spec.HasPattern)
            {
                // Border trim from Lower's region/era; era emblem on the front; cracks when Damage is high.
                for (int i = 0; i < n; i++)
                {
                    AddSegmentBox(part, rim[i], rim[(i + 1) % n], spec.Border.Era == ArchEra.Primal ? 0.022f : 0.015f, rimCol);
                }

                int spokes = spec.Motif.Era switch
                {
                    ArchEra.Primal => 2,
                    ArchEra.Agrarian => 4,
                    ArchEra.Mercantile => 4,
                    ArchEra.Industrial => 6,
                    ArchEra.Steampunk => 6,
                    ArchEra.Future => 3,
                    ArchEra.Wasteland => 3,
                    _ => 5,
                };
                for (int k = 0; k < spokes; k++)
                {
                    float a = k * math.PI / spokes;
                    float3 dir = new float3(math.cos(a), math.sin(a), 0f) * (w * 0.42f);
                    AddSegmentBox(part, -dir + new float3(0f, 0f, 0.036f), dir + new float3(0f, 0f, 0.036f), 0.014f, motifCol);
                }

                if (spec.Damage >= 0.35f)
                {
                    Color crackCol = new Color(0.12f, 0.1f, 0.1f);
                    int cracks = spec.Damage >= 0.7f ? 3 : 2;
                    for (int c = 0; c < cracks; c++)
                    {
                        float ang = 0.5f + c * 2.1f;
                        float3 p0 = new float3(math.cos(ang) * 0.05f, math.sin(ang) * 0.05f, 0.042f);
                        float3 p1 = new float3(math.cos(ang + 0.35f) * w * 0.72f, math.sin(ang + 0.35f) * h * 0.72f, 0.028f);
                        AddSegmentBox(part, p0, p1, 0.01f, crackCol);
                    }
                }
            }

            var data = new SceneryMeshes.MeshData();
            data.EmitFaceted(part, 0, FacetCos, null);
            return data.ToMesh(spec.HasPattern ? "BlockShield_Patterned" : "BlockShield_Plain");
        }

        private static void AddSegmentBox(SceneryMeshes.Part part, float3 a, float3 b, float radius, Color col)
        {
            float3 axis = math.normalizesafe(b - a, new float3(0f, 1f, 0f));
            float3 side = math.normalize(math.cross(axis, math.abs(axis.z) < 0.9f ? new float3(0f, 0f, 1f) : new float3(1f, 0f, 0f))) * radius;
            float3 front = math.cross(axis, side);
            int p0 = part.Add(a - side - front, col, float2.zero);
            int p1 = part.Add(a + side - front, col, float2.zero);
            int p2 = part.Add(a + side + front, col, float2.zero);
            int p3 = part.Add(a - side + front, col, float2.zero);
            int q0 = part.Add(b - side - front, col, float2.zero);
            int q1 = part.Add(b + side - front, col, float2.zero);
            int q2 = part.Add(b + side + front, col, float2.zero);
            int q3 = part.Add(b - side + front, col, float2.zero);
            float3 mid = (a + b) * 0.5f;
            int[] f = { p0, p1, q1, p0, q1, q0, p1, p2, q2, p1, q2, q1, p2, p3, q3, p2, q3, q2, p3, p0, q0, p3, q0, q3, p0, p3, p2, p0, p2, p1, q0, q1, q2, q0, q2, q3 };
            for (int i = 0; i < f.Length; i += 3) part.Tri(f[i], f[i + 1], f[i + 2], mid);
        }

        // ---------------------------------------------------------------- palette

        // REQ: gear-looks.md#地區與時代的器物語彙 — each tradition's swatch book; colourway 0 is the table's headline palette.
        // Every Ice main stays far brighter than every Lava main so the regions never swap identities across colourways.
        private static (Color main, Color second, Color accent) RegionColours(ArchRegion region, int colourway)
        {
            int k = ((colourway % ColourwaysPerRegion) + ColourwaysPerRegion) % ColourwaysPerRegion;
            switch (region)
            {
                case ArchRegion.Verdant:
                    return k switch
                    {
                        0 => (new Color(0.36f, 0.52f, 0.28f), new Color(0.52f, 0.36f, 0.22f), new Color(0.86f, 0.82f, 0.68f)), // moss, oak, linen
                        1 => (new Color(0.22f, 0.46f, 0.4f), new Color(0.42f, 0.28f, 0.18f), new Color(0.86f, 0.74f, 0.36f)),  // fern, walnut, leaf gold
                        _ => (new Color(0.55f, 0.62f, 0.4f), new Color(0.7f, 0.58f, 0.4f), new Color(0.74f, 0.26f, 0.24f)),    // sage, birch, berry
                    };
                case ArchRegion.Lava:
                    return k switch
                    {
                        0 => (new Color(0.2f, 0.17f, 0.16f), new Color(0.66f, 0.24f, 0.14f), new Color(0.8f, 0.62f, 0.28f)),   // charcoal, ochre red, brass
                        1 => (new Color(0.36f, 0.12f, 0.1f), new Color(0.25f, 0.24f, 0.24f), new Color(0.96f, 0.5f, 0.16f)),   // oxblood, basalt, ember
                        _ => (new Color(0.28f, 0.2f, 0.16f), new Color(0.58f, 0.3f, 0.16f), new Color(0.92f, 0.8f, 0.3f)),     // soot, rust, sulphur
                    };
                case ArchRegion.Ice:
                    return k switch
                    {
                        0 => (new Color(0.88f, 0.92f, 0.96f), new Color(0.3f, 0.22f, 0.17f), new Color(0.55f, 0.75f, 0.9f)),   // snow, dark wood, ice blue
                        1 => (new Color(0.74f, 0.85f, 0.94f), new Color(0.36f, 0.38f, 0.42f), new Color(0.25f, 0.42f, 0.74f)), // glacier, seal, deep blue
                        _ => (new Color(0.92f, 0.88f, 0.8f), new Color(0.46f, 0.32f, 0.22f), new Color(0.38f, 0.7f, 0.66f)),   // fur, reindeer, lichen
                    };
                case ArchRegion.Storm:
                    return k switch
                    {
                        0 => (new Color(0.38f, 0.46f, 0.58f), new Color(0.42f, 0.43f, 0.46f), new Color(0.78f, 0.84f, 0.9f)),  // slate, iron, pale
                        1 => (new Color(0.26f, 0.42f, 0.5f), new Color(0.22f, 0.24f, 0.27f), new Color(0.96f, 0.86f, 0.36f)),  // storm teal, tar, volt
                        _ => (new Color(0.3f, 0.32f, 0.5f), new Color(0.6f, 0.58f, 0.54f), new Color(0.6f, 0.8f, 0.96f)),      // indigo, ash, sky
                    };
                case ArchRegion.Crystal:
                    return k switch
                    {
                        0 => (new Color(0.8f, 0.66f, 0.46f), new Color(0.5f, 0.38f, 0.28f), new Color(0.6f, 0.4f, 0.76f)),     // sandstone, umber, amethyst
                        1 => (new Color(0.76f, 0.46f, 0.32f), new Color(0.7f, 0.6f, 0.44f), new Color(0.3f, 0.72f, 0.7f)),     // terracotta, tan, turquoise
                        _ => (new Color(0.88f, 0.82f, 0.7f), new Color(0.42f, 0.26f, 0.36f), new Color(0.92f, 0.56f, 0.68f)),  // bone, plum, rose quartz
                    };
                default:
                    return k switch
                    {
                        0 => (new Color(0.36f, 0.24f, 0.46f), new Color(0.26f, 0.18f, 0.24f), new Color(0.88f, 0.7f, 0.3f)),   // dusk, wine, gold
                        1 => (new Color(0.18f, 0.2f, 0.38f), new Color(0.6f, 0.16f, 0.18f), new Color(0.92f, 0.76f, 0.36f)),   // midnight, lacquer, gold
                        _ => (new Color(0.56f, 0.42f, 0.62f), new Color(0.16f, 0.14f, 0.2f), new Color(0.92f, 0.9f, 0.96f)),   // mauve, ink, pearl
                    };
            }
        }

        private static Color EraMetal(ArchEra era) => era switch
        {
            ArchEra.Primal => new Color(0.84f, 0.8f, 0.68f),     // bone and knapped stone
            ArchEra.Agrarian => new Color(0.45f, 0.46f, 0.48f),  // dark forged iron
            ArchEra.Mercantile => new Color(0.72f, 0.75f, 0.8f), // polished steel
            ArchEra.Industrial => new Color(0.56f, 0.58f, 0.6f),
            ArchEra.Steampunk => new Color(0.8f, 0.6f, 0.26f),   // brass
            ArchEra.Future => new Color(0.88f, 0.9f, 0.94f),     // pale composite
            ArchEra.Wasteland => new Color(0.5f, 0.38f, 0.28f),  // scrap
            _ => new Color(0.58f, 0.56f, 0.5f),                  // weathered stone
        };

        private static Color EraWrap(ArchEra era) => era switch
        {
            ArchEra.Primal => new Color(0.66f, 0.54f, 0.38f),    // rawhide
            ArchEra.Agrarian => new Color(0.42f, 0.3f, 0.2f),    // tanned leather
            ArchEra.Mercantile => new Color(0.44f, 0.18f, 0.16f), // dyed oxblood leather
            ArchEra.Industrial => new Color(0.18f, 0.18f, 0.2f), // rubberised
            ArchEra.Steampunk => new Color(0.34f, 0.21f, 0.14f), // oiled strap leather
            ArchEra.Future => new Color(0.22f, 0.25f, 0.3f),     // polymer grip
            ArchEra.Wasteland => new Color(0.58f, 0.52f, 0.4f),  // rag wrap
            _ => new Color(0.4f, 0.43f, 0.38f),                  // lichened cord
        };

        private static Color Shift(Color c, float hue, float value)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h + hue, 1f), s, Mathf.Clamp01(v + value));
        }

        private static Color Age(Color c, float age)
        {
            Color aged = Color.Lerp(c, Grime, age * 0.35f) * (1f - age * 0.2f);
            aged.a = 1f;
            return aged;
        }

        private static Color Mul(Color c, float k)
        {
            var o = new Color(math.saturate(c.r * k), math.saturate(c.g * k), math.saturate(c.b * k), 1f);
            return o;
        }

        // ---------------------------------------------------------------- geometry kit

        private static (float3, float3, float3) Sec(float3 centre, float3 side, float3 face) => (centre, side, face);

        private static float3 Dir(float a) => new float3(math.cos(a), 0f, math.sin(a));

        private sealed class Kit
        {
            public readonly SceneryMeshes.Part Part = new SceneryMeshes.Part();
            public readonly GearLookSpec Spec;
            public readonly GearPalette Pal;
            public readonly float Len;
            public readonly int Sides;
            public readonly ArchEra Era;
            public Pcg32 Rng;
            public Pcg32 MotifRng;

            public Kit(GearLookSpec spec, GearPalette palette)
            {
                Spec = spec;
                Pal = palette;
                Era = spec.Parts.Era;
                Len = NominalLength(spec.Kind) * spec.Length;
                // Primal gear is crude (few facets); future gear is turned smooth.
                Sides = Era == ArchEra.Primal ? 5 : Era == ArchEra.Future ? 10 : 7;
                // Damage notches and motif layout draw from their own streams so neither perturbs the other.
                ulong seed = Salt;
                foreach (int v in spec.PartVariants) seed = seed * 31ul + (ulong)v;
                Rng = new Pcg32(seed ^ (ulong)math.asuint(spec.Length) ^ ((ulong)math.asuint(spec.Damage) << 32), (ulong)spec.Kind);
                MotifRng = new Pcg32(seed ^ ((ulong)math.asuint(spec.MotifDensity) << 16) ^ math.asuint(spec.Curvature), MotifSalt);
            }

            /// <summary>Part variant of slot <paramref name="slot"/>, in [0, VariantsPerSlot).</summary>
            public int V(int slot) => Spec.PartVariants[slot] % GearLooks.VariantsPerSlot;

            // Fraction of a dimension lost to a nick here; 0 for undamaged gear.
            public float Nick(float max) => Spec.Damage > 0f && Rng.NextFloat() < Spec.Damage * 0.6f ? max * (0.3f + 0.7f * Rng.NextFloat()) : 0f;

            // Knapped stone and bone are never symmetric: primal lofts wobble by a few percent.
            public float Wobble() => Era == ArchEra.Primal ? 1f + (Rng.NextFloat() - 0.5f) * 0.16f : 1f;

            public void Tube(float3 a, float3 b, float ra, float rb, int sides, Color c)
            {
                float3 axis = math.normalizesafe(b - a, new float3(0f, 1f, 0f));
                float3 u = math.normalize(math.cross(axis, math.abs(axis.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f)));
                float3 v = math.cross(axis, u);
                int first = Part.P.Count;
                for (int i = 0; i < sides; i++)
                {
                    float ang = i * 2f * math.PI / sides;
                    float3 d = u * math.cos(ang) + v * math.sin(ang);
                    Part.Add(a + d * ra, c, float2.zero);
                    Part.Add(b + d * rb, c, float2.zero);
                }
                int ca = Part.Add(a, c, float2.zero), cb = Part.Add(b, c, float2.zero);
                float3 mid = (a + b) * 0.5f;
                for (int i = 0; i < sides; i++)
                {
                    int i0 = first + i * 2, i1 = first + ((i + 1) % sides) * 2;
                    Part.Tri(i0, i1, i0 + 1, mid);
                    Part.Tri(i1, i1 + 1, i0 + 1, mid);
                    Part.Tri(ca, i0, i1, mid);
                    Part.Tri(cb, i0 + 1, i1 + 1, mid);
                }
            }

            // A pointed cone: tips, spikes, crystals.
            public void Spike(float3 a, float3 tip, float r, int sides, Color c) => Tube(a, tip, r, r * 0.04f, sides, c);

            // Two cones back to back along `axis`: a cut gem.
            public void Gem(float3 centre, float3 axis, float r, float height, Color c)
            {
                float3 n = math.normalizesafe(axis, new float3(0f, 0f, 1f));
                Spike(centre, centre + n * height, r, 6, c);
                Spike(centre, centre - n * height * 0.45f, r, 6, Mul(c, 0.8f));
            }

            // Closed loop of `segs` straight tubes around `axis`.
            public void Ring(float3 centre, float3 axis, float radius, float thick, int segs, Color c, float arcFrom = 0f, float arcTo = 2f * math.PI)
            {
                float3 n = math.normalizesafe(axis, new float3(0f, 1f, 0f));
                float3 u = math.normalize(math.cross(n, math.abs(n.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f)));
                float3 v = math.cross(n, u);
                for (int i = 0; i < segs; i++)
                {
                    float a0 = math.lerp(arcFrom, arcTo, (float)i / segs), a1 = math.lerp(arcFrom, arcTo, (float)(i + 1) / segs);
                    Tube(centre + (u * math.cos(a0) + v * math.sin(a0)) * radius, centre + (u * math.cos(a1) + v * math.sin(a1)) * radius,
                        thick, thick, 4, c);
                }
            }

            // Flat strip from a to b lying on a surface whose outward normal is `normal`: engraved lines and inlays.
            public void Bar(float3 a, float3 b, float halfWidth, float halfDepth, float3 normal, Color c)
            {
                float3 axis = math.normalizesafe(b - a, new float3(0f, 1f, 0f));
                float3 n = math.normalizesafe(normal - axis * math.dot(normal, axis), new float3(0f, 0f, 1f));
                float3 w = math.cross(n, axis) * halfWidth;
                float3 d = n * halfDepth;
                int first = Part.P.Count;
                float3[] corners = { a - w - d, a + w - d, a + w + d, a - w + d, b - w - d, b + w - d, b + w + d, b - w + d };
                foreach (float3 p in corners) Part.Add(p, c, float2.zero);
                float3 mid = (a + b) * 0.5f;
                int[] f = { 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7, 0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7 };
                for (int i = 0; i < f.Length; i += 3) Part.Tri(first + f[i], first + f[i + 1], first + f[i + 2], mid);
            }

            // Oriented box: `yaw` turns about +Y, `roll` about +Z.
            public void Box(float3 centre, float3 half, Color c, float yaw = 0f, float roll = 0f)
            {
                quaternion q = math.mul(quaternion.RotateY(yaw), quaternion.RotateZ(roll));
                int first = Part.P.Count;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new float3((i & 1) != 0 ? half.x : -half.x, (i & 2) != 0 ? half.y : -half.y, (i & 4) != 0 ? half.z : -half.z);
                    Part.Add(centre + math.mul(q, corner), c, float2.zero);
                }
                int[][] faces = { new[] { 0, 2, 4, 6 }, new[] { 1, 3, 5, 7 }, new[] { 0, 1, 4, 5 }, new[] { 2, 3, 6, 7 }, new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 } };
                foreach (int[] f in faces)
                {
                    Part.Tri(first + f[0], first + f[1], first + f[2], centre);
                    Part.Tri(first + f[1], first + f[3], first + f[2], centre);
                }
            }

            public void Ball(float3 centre, float r, Color c) => Ellipsoid(centre, new float3(r), c);

            public void Ellipsoid(float3 centre, float3 radii, Color c)
            {
                SceneryMeshes.Ico ico = SceneryMeshes.Ico.Get(1);
                int first = Part.P.Count;
                foreach (float3 v in ico.Vertices) Part.Add(centre + v * radii, c, float2.zero);
                for (int t = 0; t < ico.Triangles.Length; t += 3)
                    Part.Tri(first + ico.Triangles[t], first + ico.Triangles[t + 1], first + ico.Triangles[t + 2], centre);
            }

            // Lozenge sections (centre ± side, centre ± face) joined into a closed solid. `notch` nicks the side edges by
            // damage; `square` uses a rounded-square section instead (cloth shells); `edge` recolours the two side
            // corners so a honed edge reads bright against the blade's flat (the faceted emit blends the two).
            public void Loft((float3 c, float3 side, float3 face)[] sections, Color col, bool notch, bool square = false, Color? edge = null)
            {
                int ring = square ? 8 : 4;
                int first = Part.P.Count;
                for (int s = 0; s < sections.Length; s++)
                {
                    (float3 c, float3 side, float3 face) = sections[s];
                    bool end = s == sections.Length - 1;
                    for (int k = 0; k < ring; k++)
                    {
                        float ang = k * 2f * math.PI / ring;
                        float cs = math.cos(ang), sn = math.sin(ang);
                        if (square) { cs = math.sign(cs) * math.pow(math.abs(cs), 0.5f); sn = math.sign(sn) * math.pow(math.abs(sn), 0.5f); }
                        bool sideCorner = !square && (k == 0 || k == 2);
                        float bite = notch && !end && sideCorner ? Nick(0.45f) : 0f;
                        Color vc = sideCorner && edge.HasValue ? edge.Value : col;
                        Part.Add(c + side * cs * (1f - bite) + face * sn, vc, float2.zero);
                    }
                }
                for (int s = 0; s + 1 < sections.Length; s++)
                {
                    float3 mid = (sections[s].c + sections[s + 1].c) * 0.5f;
                    for (int k = 0; k < ring; k++)
                    {
                        int a = first + s * ring + k, b = first + s * ring + (k + 1) % ring;
                        Part.Tri(a, b, a + ring, mid);
                        Part.Tri(b, b + ring, a + ring, mid);
                    }
                }
                CapRing(first, ring, sections[0].c, sections.Length > 1 ? sections[1].c : sections[0].c + new float3(0f, 0.01f, 0f), col);
                int last = first + (sections.Length - 1) * ring;
                CapRing(last, ring, sections[sections.Length - 1].c,
                    sections.Length > 1 ? sections[sections.Length - 2].c : sections[0].c - new float3(0f, 0.01f, 0f), col);
            }

            private void CapRing(int first, int ring, float3 centre, float3 inward, Color col)
            {
                int c = Part.Add(centre, col, float2.zero);
                float3 inside = math.lerp(centre, inward, 0.5f);
                for (int k = 0; k < ring; k++) Part.Tri(c, first + k, first + (k + 1) % ring, inside);
            }
        }
    }
}
