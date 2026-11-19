using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Main, secondary, metal and accent colours of one gear item, already shifted, aged and rusted.</summary>
    /// <remarks>Pre: built by <see cref="GearMeshes.PaletteFor"/>. Post: immutable; every channel in [0, 1]. Side effects: none.</remarks>
    public readonly struct GearPalette
    {
        internal GearPalette(Color main, Color second, Color metal, Color accent)
        {
            Main = main;
            Second = second;
            Metal = metal;
            Accent = accent;
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
    }

    /// <summary>
    /// Placeholder gear meshes built from a <see cref="GearLookSpec"/> alone, until the hand-made part library (P1-16)
    /// replaces the mesh source. Each item is modelled at life size in metres with its grip (or wearing point) at the
    /// origin and its length along +Y.
    /// </summary>
    /// <remarks>
    /// REQ: docs/design/gear-looks.md#美術路線分段 (step 2). Vertex-coloured, faceted, for the ToonScenery shader.
    /// Pre: main thread (creates Meshes). Post: same spec → identical mesh. Side effects: allocates Meshes.
    /// </remarks>
    public static class GearMeshes
    {
        private const float FacetCos = 0.8f; // same crease angle as SceneryMeshes and PropMeshes
        private const ulong Salt = 0x6765_6172_6D65_7368ul; // "gearmesh"
        private static readonly Color Rust = new Color(0.46f, 0.25f, 0.14f);
        private static readonly Color Grime = new Color(0.33f, 0.30f, 0.26f);

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

        /// <summary>Colours of <paramref name="spec"/>: palette region for cloth, parts era for metal, motif region for accents.</summary>
        /// <remarks>
        /// REQ: gear-looks.md#地區與時代的器物語彙. Pre: spec non-null.
        /// Post: deterministic; Age darkens toward grime, Damage rusts metal (except composite). Side effects: none.
        /// </remarks>
        public static GearPalette PaletteFor(GearLookSpec spec)
        {
            Contract.Requires(spec != null, "spec required");
            (Color main, Color second, _) = RegionColours(spec.Palette.Region);
            (_, _, Color accent) = RegionColours(spec.Motif.Region);
            Color metal = EraMetal(spec.Parts.Era);
            main = Shift(main, spec.HueShift, spec.ValueShift);
            second = Shift(second, spec.HueShift * 0.5f, spec.ValueShift);
            if (spec.Parts.Era == ArchEra.Future) accent = new Color(0.45f, 0.92f, 1f);
            else if (spec.Parts.Era == ArchEra.Ancient) accent = new Color(0.55f, 1f, 0.78f);
            if (spec.Parts.Era != ArchEra.Future && spec.Parts.Era != ArchEra.Primal)
                metal = Color.Lerp(metal, Rust, spec.Damage * 0.6f);
            return new GearPalette(Age(main, spec.Age), Age(second, spec.Age), Age(metal, spec.Age * 0.7f), Age(accent, spec.Age * 0.5f));
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
            var g = new Kit(spec, PaletteFor(spec));
            switch (spec.Kind)
            {
                case GearKind.Dagger:
                case GearKind.Sword:
                case GearKind.Greatsword:
                    BuildSword(g);
                    break;
                case GearKind.Axe:
                    BuildHafted(g, axe: true, twoHanded: false);
                    break;
                case GearKind.Mace:
                    BuildHafted(g, axe: false, twoHanded: false);
                    break;
                case GearKind.GreataxeHammer:
                    BuildHafted(g, axe: spec.PartVariants[0] < 2, twoHanded: true);
                    break;
                case GearKind.Twinblade:
                    BuildTwinblade(g);
                    break;
                case GearKind.Spear:
                case GearKind.Javelin:
                    BuildPolearm(g);
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

            var data = new SceneryMeshes.MeshData();
            data.EmitFaceted(g.Part, 0, FacetCos, null);
            return data.ToMesh("Gear_" + spec.Kind);
        }

        // ---------------------------------------------------------------- kinds

        private static void BuildSword(Kit g)
        {
            GearLookSpec s = g.Spec;
            float len = g.Len;
            float grip = s.Kind == GearKind.Greatsword ? 0.3f : s.Kind == GearKind.Dagger ? 0.1f : 0.16f;
            float bladeW = (s.Kind == GearKind.Greatsword ? 0.065f : s.Kind == GearKind.Dagger ? 0.028f : 0.04f) * s.Width;
            Grip(g, 0f, grip, 0.016f);
            Pommel(g, s.PartVariants[3], new float3(0f, -0.025f, 0f), 0.028f);
            Guard(g, s.PartVariants[1], grip + 0.012f, bladeW * 2.6f + 0.04f);
            Blade(g, grip + 0.03f, len - grip - 0.03f, bladeW, s.PartVariants[0], s.Curvature);
            EraFlourish(g, new float3(0f, grip * 0.5f, 0f), 0.02f, grip);
        }

        private static void BuildHafted(Kit g, bool axe, bool twoHanded)
        {
            GearLookSpec s = g.Spec;
            float len = Broken(g, g.Len);
            float r = twoHanded ? 0.022f : 0.017f;
            g.Tube(new float3(0f, -0.04f, 0f), new float3(0f, len, 0f), r * 1.1f, r * 0.9f, g.Sides, g.Pal.Second);
            Pommel(g, s.PartVariants[3], new float3(0f, -0.05f, 0f), r * 1.5f);
            Wraps(g, 0f, twoHanded ? 0.45f : 0.22f, r);
            if (len < g.Len * 0.9f) return; // the head broke off with the haft
            float headY = len - (twoHanded ? 0.16f : 0.11f) * s.Width;
            float size = (twoHanded ? 1.5f : 1f) * s.Width;
            if (axe)
            {
                float reach = 0.16f * size;
                float bite = 0.13f * size * (1f + s.Curvature * 0.6f);
                // Wedge: thick at the haft, sharp at the bit; the bit flares by curvature.
                g.Loft(new[]
                {
                    Sec(new float3(0f, headY, 0f), new float3(0f, 0.06f * size, 0f), new float3(0f, 0f, 0.02f * size * s.Thickness)),
                    Sec(new float3(reach * 0.55f, headY, 0f), new float3(0f, 0.045f * size, 0f), new float3(0f, 0f, 0.012f * size)),
                    Sec(new float3(reach, headY, 0f), new float3(0f, bite, 0f), new float3(0f, 0f, 0.002f)),
                }, g.Pal.Metal, notch: true);
                if (s.PartVariants[1] % 2 == 1) // bearded double head
                    g.Loft(new[]
                    {
                        Sec(new float3(0f, headY, 0f), new float3(0f, 0.05f * size, 0f), new float3(0f, 0f, 0.018f * size)),
                        Sec(new float3(-reach * 0.7f, headY, 0f), new float3(0f, bite * 0.7f, 0f), new float3(0f, 0f, 0.002f)),
                    }, g.Pal.Metal, notch: true);
            }
            else
            {
                float hr = 0.06f * size;
                switch (s.PartVariants[0] % 4)
                {
                    case 0:
                        g.Ball(new float3(0f, headY + hr * 0.4f, 0f), hr, g.Pal.Metal);
                        break;
                    case 1:
                        g.Box(new float3(0f, headY + hr * 0.3f, 0f), new float3(hr * 1.9f, hr, hr), g.Pal.Metal);
                        break;
                    case 2:
                        for (int k = 0; k < 6; k++) // flanged mace
                        {
                            float a = k * math.PI / 3f;
                            g.Box(new float3(math.cos(a) * hr * 0.6f, headY + hr * 0.4f, math.sin(a) * hr * 0.6f),
                                new float3(hr * 0.5f, hr * 1.1f, 0.006f), g.Pal.Metal, a);
                        }
                        break;
                    default:
                        g.Tube(new float3(0f, headY - hr * 0.6f, 0f), new float3(0f, headY + hr * 1.2f, 0f), hr, hr * 0.8f, 8, g.Pal.Metal);
                        break;
                }
            }
            EraFlourish(g, new float3(0f, headY - 0.05f, 0f), r * 1.4f, 0.04f);
        }

        private static void BuildTwinblade(Kit g)
        {
            GearLookSpec s = g.Spec;
            float half = g.Len * 0.5f;
            float haft = half * 0.45f;
            g.Tube(new float3(0f, -haft, 0f), new float3(0f, haft, 0f), 0.018f, 0.018f, g.Sides, g.Pal.Second);
            Wraps(g, -0.12f, 0.24f, 0.018f);
            float w = 0.035f * s.Width;
            for (int end = -1; end <= 1; end += 2)
            {
                float y = end * haft;
                Guard(g, s.PartVariants[1], y, w * 2.4f);
                BladeAlong(g, new float3(0f, y, 0f), new float3(0f, end, 0f), half - haft, w, s.PartVariants[0], s.Curvature * end);
            }
            EraFlourish(g, float3.zero, 0.024f, 0.1f);
        }

        private static void BuildPolearm(Kit g)
        {
            GearLookSpec s = g.Spec;
            bool javelin = s.Kind == GearKind.Javelin;
            float len = Broken(g, g.Len);
            float r = javelin ? 0.011f : 0.016f;
            float headLen = (javelin ? 0.16f : 0.3f) * s.Width;
            float shaftTop = javelin ? g.Len - headLen : len;
            // Polearms are gripped near the middle: the origin is the hand, so the butt hangs below it.
            float butt = -g.Len * (javelin ? 0.45f : 0.35f);
            g.Tube(new float3(0f, butt, 0f), new float3(0f, Math.Min(shaftTop, len + butt), 0f), r, r * 0.9f, g.Sides, g.Pal.Second);
            Wraps(g, -0.1f, 0.2f, r);
            if (len < g.Len * 0.9f) return;
            float top = g.Len + butt;
            float socket = top - headLen;
            g.Tube(new float3(0f, socket - 0.06f, 0f), new float3(0f, socket + 0.01f, 0f), r * 1.2f, r * 1.05f, g.Sides, g.Pal.Metal);
            BladeAlong(g, new float3(0f, socket, 0f), new float3(0f, 1f, 0f), headLen, (javelin ? 0.022f : 0.035f) * s.Width,
                s.PartVariants[0], 0f);
            if (!javelin && s.PartVariants[1] % 2 == 1) // halberd: an axe blade under the point
                g.Loft(new[]
                {
                    Sec(new float3(0f, socket + 0.04f, 0f), new float3(0f, 0.05f, 0f), new float3(0f, 0f, 0.012f)),
                    Sec(new float3(0.16f * s.Width, socket + 0.06f, 0f), new float3(0f, 0.09f * (1f + s.Curvature), 0f), new float3(0f, 0f, 0.002f)),
                }, g.Pal.Metal, notch: true);
            if (javelin) // fletching at the butt
                for (int k = 0; k < 3; k++)
                    g.Box(new float3(0f, butt + 0.06f, 0f) + Dir(k * 2f * math.PI / 3f) * 0.02f,
                        new float3(0.02f, 0.06f, 0.002f), g.Pal.Accent, k * 2f * math.PI / 3f);
            EraFlourish(g, new float3(0f, socket - 0.1f, 0f), r * 1.3f, 0.05f);
        }

        private static void BuildThrowingBlade(Kit g)
        {
            GearLookSpec s = g.Spec;
            int points = 3 + s.PartVariants[0] % 3;
            float r = g.Len * 0.5f;
            g.Tube(new float3(0f, 0f, -0.006f), new float3(0f, 0f, 0.006f), r * 0.22f, r * 0.22f, 8, g.Pal.Second);
            for (int k = 0; k < points; k++)
            {
                float a = k * 2f * math.PI / points;
                float3 dir = new float3(math.cos(a), math.sin(a), 0f);
                float3 side = new float3(-dir.y, dir.x, 0f);
                g.Loft(new[]
                {
                    Sec(dir * r * 0.15f, side * r * 0.22f, new float3(0f, 0f, 0.005f)),
                    Sec(dir * r * 0.6f + side * r * s.Curvature * 0.2f, side * r * 0.12f, new float3(0f, 0f, 0.003f)),
                    Sec(dir * r + side * r * s.Curvature * 0.35f, float3.zero, float3.zero),
                }, g.Pal.Metal, notch: true);
            }
        }

        private static void BuildBow(Kit g)
        {
            GearLookSpec s = g.Spec;
            float half = g.Len * 0.5f;
            float recurve = 0.05f + s.Curvature * 0.12f;
            Grip(g, -0.06f, 0.12f, 0.018f);
            const int segs = 6;
            var tips = new float3[2];
            for (int end = -1; end <= 1; end += 2)
            {
                var sections = new List<(float3, float3, float3)>();
                for (int i = 0; i <= segs; i++)
                {
                    float t = (float)i / segs;
                    float y = end * (0.06f + t * (half - 0.06f));
                    // Limbs bow away from the string; a recurve flips back at the tips.
                    float z = -math.sin(t * math.PI * 0.9f) * half * 0.18f + math.max(0f, t - 0.75f) * recurve * 4f * half * 0.2f;
                    float w = math.lerp(0.022f, 0.008f, t) * s.Width;
                    sections.Add(Sec(new float3(0f, y, z), new float3(w, 0f, 0f), new float3(0f, 0f, w * 0.6f * s.Thickness)));
                }
                g.Loft(sections.ToArray(), (end > 0) == (s.PartVariants[0] % 2 == 0) ? g.Pal.Main : g.Pal.Second, notch: false);
                tips[(end + 1) / 2] = sections[segs].Item1;
            }
            g.Tube(tips[0], tips[1], 0.002f, 0.002f, 3, new Color(0.9f, 0.88f, 0.8f));
            if (s.Kind == GearKind.RepeaterBow) // magazine on the riser
                g.Box(new float3(0f, 0.1f, 0.05f), new float3(0.02f, 0.06f, 0.03f), g.Pal.Metal);
            EraFlourish(g, float3.zero, 0.022f, 0.06f);
        }

        private static void BuildCrossbow(Kit g)
        {
            GearLookSpec s = g.Spec;
            bool heavy = s.Kind == GearKind.HeavyCrossbow;
            float len = g.Len;
            float w = (heavy ? 0.035f : 0.022f) * s.Width;
            // Stock lies along +Y from the trigger hand, prod across the front.
            g.Box(new float3(0f, len * 0.35f, 0f), new float3(w, len * 0.5f, w * 1.3f * s.Thickness), g.Pal.Second);
            g.Box(new float3(0f, -0.05f, -w * 1.6f), new float3(w * 0.8f, 0.05f, w * 1.2f), g.Pal.Second);
            float span = len * (heavy ? 0.45f : 0.55f) * (0.9f + s.Curvature * 0.3f);
            float frontY = len * 0.82f;
            var tips = new float3[2];
            for (int side = -1; side <= 1; side += 2)
            {
                float3 tip = new float3(side * span, frontY - span * 0.25f * (0.5f + s.Curvature), 0f);
                g.Loft(new[]
                {
                    Sec(new float3(0f, frontY, 0f), new float3(0f, 0.018f, 0f), new float3(0f, 0f, 0.012f)),
                    Sec(tip, new float3(0f, 0.008f, 0f), new float3(0f, 0f, 0.006f)),
                }, g.Pal.Metal, notch: false);
                tips[(side + 1) / 2] = tip;
            }
            g.Tube(tips[0], tips[1], 0.002f, 0.002f, 3, new Color(0.9f, 0.88f, 0.8f));
            if (heavy)
                g.Tube(new float3(0f, frontY, 0f), new float3(0f, frontY + 0.12f, 0f), w * 0.9f, w * 1.2f, g.Sides, g.Pal.Metal);
            EraFlourish(g, new float3(0f, len * 0.2f, 0f), w * 1.4f, 0.05f);
        }

        private static void BuildStaff(Kit g)
        {
            GearLookSpec s = g.Spec;
            bool staff = s.Kind == GearKind.MagicStaff;
            float len = Broken(g, g.Len);
            float r = staff ? 0.017f : 0.012f;
            float butt = staff ? -g.Len * 0.4f : -0.04f;
            float top = len + butt;
            g.Tube(new float3(0f, butt, 0f), new float3(0f, top, 0f), r, r * 1.15f, g.Sides, g.Pal.Second);
            Wraps(g, -0.08f, 0.16f, r);
            if (len < g.Len * 0.9f) return;
            float core = (staff ? 0.05f : 0.028f) * s.Width;
            switch (s.PartVariants[0] % 4)
            {
                case 0: // orb in prongs
                    g.Ball(new float3(0f, top + core * 1.3f, 0f), core, g.Pal.Accent);
                    for (int k = 0; k < 3; k++)
                    {
                        float3 d = Dir(k * 2f * math.PI / 3f);
                        g.Tube(new float3(0f, top, 0f), new float3(0f, top + core * 2.4f, 0f) + d * core * (1.2f + s.Curvature),
                            r * 0.5f, r * 0.25f, 4, g.Pal.Metal);
                    }
                    break;
                case 1: // crook
                    var crook = new List<(float3, float3, float3)>();
                    for (int i = 0; i <= 6; i++)
                    {
                        float a = i / 6f * math.PI * (1f + s.Curvature * 0.6f);
                        float3 c = new float3(core * 1.6f * (1f - math.cos(a)), top + core * 1.6f * math.sin(a), 0f);
                        crook.Add(Sec(c, new float3(0f, 0f, r), new float3(r, 0f, 0f)));
                    }
                    g.Loft(crook.ToArray(), g.Pal.Second, notch: false);
                    g.Ball(new float3(core * 1.6f, top + core * 0.8f, 0f), core * 0.5f, g.Pal.Accent);
                    break;
                case 2: // crystal
                    g.Tube(new float3(0f, top, 0f), new float3(0f, top + core * 3f, 0f), core * 0.9f, 0.001f, 5, g.Pal.Accent);
                    break;
                default: // ring
                    for (int i = 0; i < 8; i++)
                    {
                        float a0 = i * math.PI / 4f, a1 = (i + 1) * math.PI / 4f;
                        g.Tube(new float3(math.cos(a0) * core * 1.5f, top + core * 1.5f + math.sin(a0) * core * 1.5f, 0f),
                            new float3(math.cos(a1) * core * 1.5f, top + core * 1.5f + math.sin(a1) * core * 1.5f, 0f),
                            r * 0.6f, r * 0.6f, 4, g.Pal.Metal);
                    }
                    g.Ball(new float3(0f, top + core * 1.5f, 0f), core * 0.6f, g.Pal.Accent);
                    break;
            }
            EraFlourish(g, new float3(0f, top - 0.06f, 0f), r * 1.5f, 0.05f);
        }

        private static void BuildShield(Kit g)
        {
            GearLookSpec s = g.Spec;
            float h = g.Len * 0.5f;
            float w = h * (0.75f + 0.25f * s.Width);
            float bulge = 0.02f + s.Curvature * 0.06f;
            int shape = s.PartVariants[0] % 4; // round, kite, heater, tower
            int n = 16;
            var rim = new float3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * math.PI / n;
                float x = math.sin(a) * w, y = math.cos(a) * h;
                if (shape == 1 && y < 0f) x *= 1f + y / h * 0.85f;            // kite: tapers to a point
                if (shape == 2) y = y > 0f ? h * 0.75f : y;                  // heater: flat top
                if (shape == 3) { x = math.sign(x) * w * 0.85f; y = math.clamp(y * 1.3f, -h * 1.15f, h * 1.15f); } // tower
                rim[i] = new float3(x, y, 0f);
            }
            // Front face as a fan to a raised centre; back is flat. Damage dents the rim inward.
            float3 centreFront = new float3(0f, 0f, bulge);
            int cf = g.Part.Add(centreFront, g.Pal.Main, float2.zero);
            int cb = g.Part.Add(new float3(0f, 0f, -0.012f), g.Pal.Second, float2.zero);
            int first = g.Part.P.Count;
            for (int i = 0; i < n; i++)
            {
                float dent = g.Nick(0.12f);
                float3 p = rim[i] * (1f - dent);
                g.Part.Add(p + new float3(0f, 0f, 0.006f), g.Pal.Main, float2.zero);
                g.Part.Add(p - new float3(0f, 0f, 0.012f), g.Pal.Second, float2.zero);
            }
            var inside = new float3(0f, 0f, -0.003f);
            for (int i = 0; i < n; i++)
            {
                int a = first + i * 2, b = first + ((i + 1) % n) * 2;
                g.Part.Tri(cf, a, b, inside);
                g.Part.Tri(cb, a + 1, b + 1, inside);
                g.Part.Tri(a, b, a + 1, inside);
                g.Part.Tri(b, b + 1, a + 1, inside);
            }
            // Rim band, boss, emblem bands.
            if (s.PartVariants[2] % 2 == 0)
                for (int i = 0; i < n; i++)
                    g.Tube(rim[i] + new float3(0f, 0f, 0.008f), rim[(i + 1) % n] + new float3(0f, 0f, 0.008f), 0.012f, 0.012f, 4, g.Pal.Metal);
            float boss = 0.05f + 0.02f * s.PartVariants[1];
            g.Tube(new float3(0f, 0f, bulge * 0.6f), new float3(0f, 0f, bulge + 0.03f), boss, boss * (s.PartVariants[1] == 3 ? 0.2f : 0.7f),
                g.Sides, g.Pal.Metal);
            int bands = (int)math.round(s.MotifDensity * 4f);
            for (int k = 0; k < bands; k++)
            {
                float a = k * math.PI / math.max(1, bands);
                g.Box(new float3(0f, 0f, bulge * 0.7f), new float3(w * 0.9f, 0.012f, 0.004f), g.Pal.Accent, 0f, a);
            }
            // Arm strap on the back so the mesh reads as held.
            g.Box(new float3(0f, 0f, -0.04f), new float3(0.015f, h * 0.35f, 0.012f), g.Pal.Second);
        }

        private static void BuildUpper(Kit g)
        {
            GearLookSpec s = g.Spec;
            float h = g.Len;
            float sw = 0.2f * s.Width, waist = sw * (0.8f + s.Curvature * 0.2f);
            int cut = s.PartVariants[0] % 3; // tunic, coat (long hem), vest (no sleeves)
            float hem = cut == 1 ? -h * 0.45f : 0f;
            g.Loft(new[]
            {
                Sec(new float3(0f, hem, 0f), new float3(waist * (cut == 1 ? 1.35f : 1.05f), 0f, 0f), new float3(0f, 0f, waist * 0.7f)),
                Sec(new float3(0f, h * 0.45f, 0f), new float3(waist, 0f, 0f), new float3(0f, 0f, waist * 0.62f)),
                Sec(new float3(0f, h * 0.9f, 0f), new float3(sw, 0f, 0f), new float3(0f, 0f, sw * 0.55f)),
                Sec(new float3(0f, h, 0f), new float3(sw * 0.45f, 0f, 0f), new float3(0f, 0f, sw * 0.4f)),
            }, g.Pal.Main, notch: false, square: true);
            if (cut != 2)
                for (int side = -1; side <= 1; side += 2)
                    g.Tube(new float3(side * sw * 0.95f, h * 0.88f, 0f), new float3(side * (sw + 0.07f), h * 0.3f, 0f), 0.06f, 0.05f * (1f + s.Curvature * 0.5f),
                        6, g.Pal.Main);
            // Collar by variant, trim bands by motif density, patches by damage.
            g.Tube(new float3(0f, h * 0.97f, 0f), new float3(0f, h * 1.03f + 0.03f * s.PartVariants[1], 0f), sw * 0.5f, sw * 0.42f, 8, g.Pal.Second);
            Trim(g, hem, h, waist);
            Patches(g, h, waist);
        }

        private static void BuildLower(Kit g)
        {
            GearLookSpec s = g.Spec;
            float h = g.Len;
            float hip = 0.17f * s.Width;
            bool skirt = s.PartVariants[0] % 2 == 1;
            g.Tube(new float3(0f, h - 0.08f, 0f), new float3(0f, h, 0f), hip * 1.02f, hip, 8, g.Pal.Second); // belt
            if (skirt)
            {
                float flare = 1.2f + s.Curvature * 0.5f;
                g.Loft(new[]
                {
                    Sec(new float3(0f, h * 0.15f, 0f), new float3(hip * flare, 0f, 0f), new float3(0f, 0f, hip * flare * 0.8f)),
                    Sec(new float3(0f, h - 0.08f, 0f), new float3(hip, 0f, 0f), new float3(0f, 0f, hip * 0.7f)),
                }, g.Pal.Main, notch: false, square: true);
            }
            else
            {
                for (int side = -1; side <= 1; side += 2)
                    g.Tube(new float3(side * hip * 0.48f, h - 0.08f, 0f), new float3(side * hip * (0.5f + s.Curvature * 0.2f), 0f, 0f),
                        hip * 0.55f, hip * 0.38f, 6, g.Pal.Main);
            }
            Trim(g, 0f, h - 0.08f, hip);
            Patches(g, h * 0.8f, hip);
        }

        private static void BuildHeadwear(Kit g)
        {
            GearLookSpec s = g.Spec;
            float r = 0.11f * s.Width;
            switch (s.PartVariants[0] % 4)
            {
                case 0: // brimmed hat
                    g.Tube(new float3(0f, 0f, 0f), new float3(0f, 0.012f, 0f), r * (1.5f + s.Curvature * 0.8f), r * (1.5f + s.Curvature * 0.8f), 12, g.Pal.Main);
                    g.Tube(new float3(0f, 0.01f, 0f), new float3(0f, g.Len * 0.5f, 0f), r, r * 0.85f, 10, g.Pal.Main);
                    break;
                case 1: // helm
                    g.Ball(new float3(0f, r * 0.4f, 0f), r * 1.05f, g.Pal.Metal);
                    g.Box(new float3(0f, r * 0.2f, r * 0.95f), new float3(0.012f, r * 0.5f, 0.01f), g.Pal.Metal);
                    break;
                case 2: // hood or cap
                    g.Tube(new float3(0f, 0f, -0.02f), new float3(0f, g.Len * 0.8f, -r * s.Curvature), r * 1.05f, 0.01f, 8, g.Pal.Main);
                    break;
                default: // circlet
                    for (int i = 0; i < 10; i++)
                    {
                        float a0 = i * math.PI / 5f, a1 = (i + 1) * math.PI / 5f;
                        g.Tube(new float3(math.cos(a0) * r, 0.03f, math.sin(a0) * r), new float3(math.cos(a1) * r, 0.03f, math.sin(a1) * r),
                            0.008f, 0.008f, 4, g.Pal.Metal);
                    }
                    g.Ball(new float3(0f, 0.04f, r), 0.018f, g.Pal.Accent);
                    break;
            }
            int bands = (int)math.round(s.MotifDensity * 2f);
            for (int k = 0; k < bands; k++)
                g.Tube(new float3(0f, 0.02f + k * 0.03f, 0f), new float3(0f, 0.035f + k * 0.03f, 0f), r * 1.02f, r * 1.02f, 10, g.Pal.Accent);
        }

        private static void BuildCharm(Kit g)
        {
            GearLookSpec s = g.Spec;
            // REQ: gear-looks.md#宙獸飾品 — a small charm: a loop to hang from a socket and a pendant below it.
            float size = g.Len * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * math.PI / 4f, a1 = (i + 1) * math.PI / 4f;
                g.Tube(new float3(math.cos(a0) * size * 0.25f, size * 0.25f + math.sin(a0) * size * 0.25f, 0f),
                    new float3(math.cos(a1) * size * 0.25f, size * 0.25f + math.sin(a1) * size * 0.25f, 0f), 0.003f, 0.003f, 4, g.Pal.Metal);
            }
            float3 c = new float3(0f, -size * 0.45f, 0f);
            switch (s.PartVariants[0] % 4)
            {
                case 0: g.Ball(c, size * 0.42f, g.Pal.Accent); break;
                case 1: g.Tube(c + new float3(0f, size * 0.4f, 0f), c - new float3(0f, size * 0.6f, 0f), size * 0.35f, 0.001f, 6, g.Pal.Accent); break;
                case 2: g.Box(c, new float3(size * 0.32f, size * 0.42f, 0.004f), g.Pal.Main, 0f, math.PI / 4f); break;
                default: g.Tube(c + new float3(0f, 0f, -0.004f), c + new float3(0f, 0f, 0.004f), size * 0.4f, size * 0.4f, 6, g.Pal.Metal); break;
            }
            if (s.MotifDensity > 0.5f) g.Ball(c + new float3(0f, 0f, 0.006f), size * 0.14f, g.Pal.Main);
        }

        // ---------------------------------------------------------------- parts

        private static void Grip(Kit g, float y0, float y1, float r)
        {
            g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r, r * 1.08f, g.Sides, g.Pal.Second);
            Wraps(g, y0, y1 - y0, r);
        }

        // Binding rings on grips: primal lashing, later eras leather wrap; denser with ornament.
        private static void Wraps(Kit g, float y0, float length, float r)
        {
            int rings = 1 + (int)math.round(g.Spec.MotifDensity * 3f) + (g.Spec.Parts.Era == ArchEra.Primal ? 2 : 0);
            for (int k = 0; k < rings; k++)
            {
                float y = y0 + length * (k + 0.5f) / rings;
                g.Tube(new float3(0f, y - 0.006f, 0f), new float3(0f, y + 0.006f, 0f), r * 1.18f, r * 1.18f, g.Sides, g.Pal.Accent);
            }
        }

        private static void Pommel(Kit g, int variant, float3 at, float r)
        {
            switch (variant % 4)
            {
                case 0: g.Ball(at, r, g.Pal.Metal); break;
                case 1: g.Box(at, new float3(r, r * 0.8f, r), g.Pal.Metal); break;
                case 2: g.Tube(at + new float3(0f, r * 0.6f, 0f), at - new float3(0f, r * 1.4f, 0f), r, 0.002f, 6, g.Pal.Metal); break;
                default: g.Tube(at + new float3(0f, 0f, -r * 0.4f), at + new float3(0f, 0f, r * 0.4f), r * 1.1f, r * 1.1f, 8, g.Pal.Metal); break;
            }
        }

        private static void Guard(Kit g, int variant, float y, float width)
        {
            switch (variant % 4)
            {
                case 0: // straight cross
                    g.Box(new float3(0f, y, 0f), new float3(width * 0.5f, 0.012f, 0.018f), g.Pal.Metal);
                    break;
                case 1: // crescent, tips toward the blade
                    g.Loft(new[]
                    {
                        Sec(new float3(-width * 0.55f, y + 0.035f, 0f), new float3(0.008f, 0f, 0f), new float3(0f, 0f, 0.012f)),
                        Sec(new float3(0f, y, 0f), new float3(0f, 0.014f, 0f), new float3(0f, 0f, 0.018f)),
                        Sec(new float3(width * 0.55f, y + 0.035f, 0f), new float3(0.008f, 0f, 0f), new float3(0f, 0f, 0.012f)),
                    }, g.Pal.Metal, notch: false);
                    break;
                case 2: // disc
                    g.Tube(new float3(0f, y - 0.006f, 0f), new float3(0f, y + 0.006f, 0f), width * 0.32f, width * 0.32f, 10, g.Pal.Metal);
                    break;
                default: // stub
                    g.Box(new float3(0f, y, 0f), new float3(width * 0.25f, 0.016f, 0.02f), g.Pal.Metal);
                    break;
            }
        }

        private static void Blade(Kit g, float y0, float length, float width, int tip, float curve) =>
            BladeAlong(g, new float3(0f, y0, 0f), new float3(0f, 1f, 0f), length, width, tip, curve);

        // A lozenge-section blade lofted along `dir`, bending sideways by `curve`; the tip shape comes from the variant.
        private static void BladeAlong(Kit g, float3 root, float3 dir, float length, float width, int tip, float curve)
        {
            const int segs = 7;
            float3 side = new float3(1f, 0f, 0f);
            float thick = width * 0.22f * g.Spec.Thickness;
            var sections = new List<(float3, float3, float3)>();
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                float taper = tip switch
                {
                    0 => 1f - t * t * 0.95f,                     // leaf point
                    1 => t < 0.8f ? 1f : (1f - t) * 5f,          // clipped point
                    2 => math.sqrt(math.max(0f, 1f - t * t)),    // round
                    _ => 1f - t * 0.6f,                          // chisel
                };
                if (i == segs && tip != 3) taper = 0f;
                float3 c = root + dir * (t * length) + side * (curve * length * 0.25f * t * t);
                sections.Add(Sec(c, side * width * taper, new float3(0f, 0f, thick * math.max(taper, 0.15f))));
            }
            g.Loft(sections.ToArray(), g.Pal.Metal, notch: true);
            // A fuller or motif inlay down the middle of each face.
            if (g.Spec.MotifDensity > 0.35f)
                for (int face = -1; face <= 1; face += 2)
                    g.Box(root + dir * (length * 0.35f) + side * (curve * length * 0.03f) + new float3(0f, 0f, face * thick * 0.9f),
                        new float3(width * 0.12f, length * 0.25f, 0.002f), g.Pal.Accent);
        }

        // Era-specific detail at `at`: steampunk cog, future light band, industrial rivets, wasteland patch plate,
        // ancient rune ring. Other eras add nothing here (their look is in proportions and palette).
        private static void EraFlourish(Kit g, float3 at, float r, float span)
        {
            switch (g.Spec.Parts.Era)
            {
                case ArchEra.Steampunk:
                    g.Tube(at - new float3(0f, 0.008f, 0f), at + new float3(0f, 0.008f, 0f), r * 2.2f, r * 2.2f, 12, g.Pal.Metal);
                    for (int k = 0; k < 8; k++)
                        g.Box(at + Dir(k * math.PI / 4f) * r * 2.4f, new float3(r * 0.3f, 0.007f, r * 0.3f), g.Pal.Metal, k * math.PI / 4f);
                    break;
                case ArchEra.Future:
                    g.Tube(at - new float3(0f, 0.004f, 0f), at + new float3(0f, 0.004f, 0f), r * 1.25f, r * 1.25f, 12, g.Pal.Accent);
                    break;
                case ArchEra.Industrial:
                    for (int k = 0; k < 4; k++)
                        g.Ball(at + Dir(k * math.PI / 2f) * r * 1.1f + new float3(0f, span * 0.5f, 0f), r * 0.25f, g.Pal.Metal);
                    break;
                case ArchEra.Wasteland:
                    g.Box(at + new float3(r * 0.6f, span * 0.3f, 0f), new float3(r * 0.5f, span * 0.4f + 0.01f, r * 1.25f), g.Pal.Metal, 0f, 0.2f);
                    break;
                case ArchEra.Ancient:
                    g.Tube(at - new float3(0f, 0.01f, 0f), at + new float3(0f, 0.01f, 0f), r * 1.5f, r * 1.4f, 6, g.Pal.Accent);
                    break;
            }
        }

        private static void Trim(Kit g, float hem, float top, float r)
        {
            int bands = (int)math.round(g.Spec.MotifDensity * 3f);
            for (int k = 0; k < bands; k++)
            {
                float y = hem + 0.02f + k * 0.035f;
                g.Tube(new float3(0f, y, 0f), new float3(0f, y + 0.018f, 0f), r * 1.08f, r * 1.08f, 8, g.Pal.Accent);
            }
        }

        // Damaged cloth gets patches in the secondary colour.
        private static void Patches(Kit g, float h, float r)
        {
            int patches = (int)math.floor(g.Spec.Damage * 4f);
            for (int k = 0; k < patches; k++)
            {
                float a = g.Rng.NextFloat() * math.PI * 2f;
                float y = h * (0.2f + 0.6f * g.Rng.NextFloat());
                g.Box(new float3(math.sin(a) * r * 1.02f, y, math.cos(a) * r * 0.7f), new float3(0.035f, 0.035f, 0.004f), g.Pal.Second, a);
            }
        }

        // Badly damaged hafted gear loses its head: the haft ends short.
        private static float Broken(Kit g, float len) => g.Spec.Damage > 0.85f ? len * 0.62f : len;

        // ---------------------------------------------------------------- palette

        private static (Color main, Color second, Color accent) RegionColours(ArchRegion region) => region switch
        {
            ArchRegion.Verdant => (new Color(0.36f, 0.52f, 0.28f), new Color(0.52f, 0.36f, 0.22f), new Color(0.86f, 0.82f, 0.68f)),
            ArchRegion.Lava => (new Color(0.2f, 0.17f, 0.16f), new Color(0.66f, 0.24f, 0.14f), new Color(0.8f, 0.62f, 0.28f)),
            ArchRegion.Ice => (new Color(0.88f, 0.92f, 0.96f), new Color(0.3f, 0.22f, 0.17f), new Color(0.55f, 0.75f, 0.9f)),
            ArchRegion.Storm => (new Color(0.38f, 0.46f, 0.58f), new Color(0.42f, 0.43f, 0.46f), new Color(0.78f, 0.84f, 0.9f)),
            ArchRegion.Crystal => (new Color(0.8f, 0.66f, 0.46f), new Color(0.5f, 0.38f, 0.28f), new Color(0.6f, 0.4f, 0.76f)),
            _ => (new Color(0.36f, 0.24f, 0.46f), new Color(0.26f, 0.18f, 0.24f), new Color(0.88f, 0.7f, 0.3f)),
        };

        private static Color EraMetal(ArchEra era) => era switch
        {
            ArchEra.Primal => new Color(0.84f, 0.8f, 0.68f),     // bone and knapped stone
            ArchEra.Agrarian => new Color(0.45f, 0.46f, 0.48f),  // dark forged iron
            ArchEra.Mercantile => new Color(0.74f, 0.76f, 0.8f), // polished steel
            ArchEra.Industrial => new Color(0.56f, 0.58f, 0.6f),
            ArchEra.Steampunk => new Color(0.78f, 0.58f, 0.26f), // brass
            ArchEra.Future => new Color(0.88f, 0.9f, 0.94f),     // pale composite
            ArchEra.Wasteland => new Color(0.5f, 0.38f, 0.28f),  // scrap
            _ => new Color(0.58f, 0.56f, 0.5f),                  // weathered stone
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
            public Pcg32 Rng;

            public Kit(GearLookSpec spec, GearPalette palette)
            {
                Spec = spec;
                Pal = palette;
                Len = NominalLength(spec.Kind) * spec.Length;
                // Primal gear is crude (few facets); future gear is turned smooth.
                Sides = spec.Parts.Era == ArchEra.Primal ? 5 : spec.Parts.Era == ArchEra.Future ? 10 : 7;
                // Damage notches are drawn from their own stream so they never perturb the look's other choices.
                ulong seed = Salt;
                foreach (int v in spec.PartVariants) seed = seed * 31ul + (ulong)v;
                Rng = new Pcg32(seed ^ (ulong)math.asuint(spec.Length) ^ ((ulong)math.asuint(spec.Damage) << 32), (ulong)spec.Kind);
            }

            // Fraction of a dimension lost to a nick here; 0 for undamaged gear.
            public float Nick(float max) => Spec.Damage > 0f && Rng.NextFloat() < Spec.Damage * 0.6f ? max * (0.3f + 0.7f * Rng.NextFloat()) : 0f;

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

            public void Ball(float3 centre, float r, Color c)
            {
                SceneryMeshes.Ico ico = SceneryMeshes.Ico.Get(1);
                int first = Part.P.Count;
                foreach (float3 v in ico.Vertices) Part.Add(centre + v * r, c, float2.zero);
                for (int t = 0; t < ico.Triangles.Length; t += 3)
                    Part.Tri(first + ico.Triangles[t], first + ico.Triangles[t + 1], first + ico.Triangles[t + 2], centre);
            }

            // Lozenge sections (centre ± side, centre ± face) joined into a closed solid. `notch` nicks the side edges by
            // damage; `square` uses a rounded-square section instead (cloth shells).
            public void Loft((float3 c, float3 side, float3 face)[] sections, Color col, bool notch, bool square = false)
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
                        float bite = notch && !end && (k == 0 || k == 2) ? Nick(0.45f) : 0f;
                        Part.Add(c + side * cs * (1f - bite) + face * sn, col, float2.zero);
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
                CapRing(first, ring, sections[0].c, sections.Length > 1 ? sections[1].c : sections[0].c + new float3(0f, 0.01f, 0f));
                int last = first + (sections.Length - 1) * ring;
                CapRing(last, ring, sections[sections.Length - 1].c,
                    sections.Length > 1 ? sections[sections.Length - 2].c : sections[0].c - new float3(0f, 0.01f, 0f));
            }

            private void CapRing(int first, int ring, float3 centre, float3 inward)
            {
                int c = Part.Add(centre, Part.C[first], float2.zero);
                float3 inside = math.lerp(centre, inward, 0.5f);
                for (int k = 0; k < ring; k++) Part.Tri(c, first + k, first + (k + 1) % ring, inside);
            }
        }
    }
}
