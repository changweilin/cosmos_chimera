using System.Collections.Generic;
using Cosmimera.Core.Random;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    // Parts shared by several item families: grips, pommels, guards, blades, region motifs, era craft and wear.
    // Each switch over a part variant has exactly GearLooks.VariantsPerSlot cases; append new shapes at the end and
    // raise VariantsPerSlot (and GearLooks.GeneratorVersion) together.
    public static partial class GearMeshes
    {
        // ---------------------------------------------------------------- grips

        // REQ: gear-looks.md#部件槽 (握柄／握把纏繩).
        private static void Grip(Kit g, int variant, float y0, float y1, float r)
        {
            Color wood = g.Pal.Second;
            // Hands polish the middle of an old grip (gear-looks.md#五軸 新舊: 握把磨亮).
            Color wrap = Color.Lerp(g.Pal.Wrap, g.Pal.Edge, g.Spec.Age * 0.35f);
            float len = y1 - y0;
            switch (variant)
            {
                case 0: // leather rings
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r, r * 1.08f, g.Sides, wood);
                    Wraps(g, y0, len, r, wrap);
                    break;
                case 1: // spiral cord
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r * 1.02f, r * 1.06f, g.Sides, wrap);
                    for (int k = 0; k < 6; k++)
                    {
                        float a0 = k * 1.9f, a1 = a0 + 1.9f;
                        float ya = y0 + len * k / 6f, yb = y0 + len * (k + 1) / 6f;
                        g.Tube(new float3(math.cos(a0) * r * 1.1f, ya, math.sin(a0) * r * 1.1f),
                            new float3(math.cos(a1) * r * 1.1f, yb, math.sin(a1) * r * 1.1f), r * 0.22f, r * 0.22f, 4, Mul(wrap, 0.8f));
                    }
                    break;
                case 2: // ribbed, metal ferrules at both ends
                    for (int k = 0; k < 5; k++)
                    {
                        float ya = y0 + len * k / 5f, yb = y0 + len * (k + 0.8f) / 5f;
                        g.Tube(new float3(0f, ya, 0f), new float3(0f, yb, 0f), r * 1.05f, r * 1.12f, g.Sides, wrap);
                    }
                    g.Tube(new float3(0f, y0 - 0.01f, 0f), new float3(0f, y0 + 0.008f, 0f), r * 1.25f, r * 1.25f, g.Sides, g.Pal.Metal);
                    g.Tube(new float3(0f, y1 - 0.008f, 0f), new float3(0f, y1 + 0.01f, 0f), r * 1.25f, r * 1.25f, g.Sides, g.Pal.Metal);
                    break;
                case 3: // diamond cross-wrap over a lighter core
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r, r, g.Sides, g.Pal.Accent);
                    for (int k = 0; k < 4; k++)
                    {
                        float ya = y0 + len * k / 4f, yb = y0 + len * (k + 1) / 4f;
                        g.Bar(new float3(-r, ya, r * 0.6f), new float3(r, yb, r * 0.6f), r * 0.25f, r * 0.12f, new float3(0f, 0f, 1f), wrap);
                        g.Bar(new float3(r, ya, r * 0.6f), new float3(-r, yb, r * 0.6f), r * 0.25f, r * 0.12f, new float3(0f, 0f, 1f), wrap);
                        g.Bar(new float3(-r, ya, -r * 0.6f), new float3(r, yb, -r * 0.6f), r * 0.25f, r * 0.12f, new float3(0f, 0f, -1f), wrap);
                        g.Bar(new float3(r, ya, -r * 0.6f), new float3(-r, yb, -r * 0.6f), r * 0.25f, r * 0.12f, new float3(0f, 0f, -1f), wrap);
                    }
                    break;
                case 4: // bare turned wood, swelling toward the hand
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y0 + len * 0.5f, 0f), r, r * 1.18f, g.Sides, wood);
                    g.Tube(new float3(0f, y0 + len * 0.5f, 0f), new float3(0f, y1, 0f), r * 1.18f, r, g.Sides, wood);
                    g.Tube(new float3(0f, y1 - 0.006f, 0f), new float3(0f, y1 + 0.012f, 0f), r * 1.22f, r * 1.22f, g.Sides, g.Pal.Metal);
                    break;
                default: // fine wire binding
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r, r, g.Sides, wood);
                    int rings = 8 + (int)(len / 0.03f);
                    for (int k = 0; k < rings && k < 18; k++)
                    {
                        float y = y0 + len * (k + 0.5f) / math.min(rings, 18);
                        g.Tube(new float3(0f, y - 0.0025f, 0f), new float3(0f, y + 0.0025f, 0f), r * 1.1f, r * 1.1f, 6, g.Pal.Edge);
                    }
                    break;
            }

            if (g.Era == ArchEra.Primal) Lashing(g, y0 + len * 0.5f, r);
        }

        // Binding rings: denser with ornament.
        private static void Wraps(Kit g, float y0, float length, float r, Color c)
        {
            int rings = 1 + (int)math.round(g.Spec.MotifDensity * 3f) + (g.Era == ArchEra.Primal ? 2 : 0);
            for (int k = 0; k < rings; k++)
            {
                float y = y0 + length * (k + 0.5f) / rings;
                g.Tube(new float3(0f, y - 0.006f, 0f), new float3(0f, y + 0.006f, 0f), r * 1.18f, r * 1.18f, g.Sides, c);
            }
        }

        // Crossed sinew lashing: primal gear is tied together, not riveted.
        private static void Lashing(Kit g, float y, float r)
        {
            g.Bar(new float3(-r * 1.2f, y - r * 1.6f, r * 1.05f), new float3(r * 1.2f, y + r * 1.6f, r * 1.05f), r * 0.3f, r * 0.15f, new float3(0f, 0f, 1f), g.Pal.Wrap);
            g.Bar(new float3(r * 1.2f, y - r * 1.6f, r * 1.05f), new float3(-r * 1.2f, y + r * 1.6f, r * 1.05f), r * 0.3f, r * 0.15f, new float3(0f, 0f, 1f), g.Pal.Wrap);
        }

        // ---------------------------------------------------------------- pommels and butt caps

        private static void Pommel(Kit g, int variant, float3 at, float r)
        {
            switch (variant)
            {
                case 0: g.Ball(at, r, g.Pal.Metal); break;
                case 1: g.Box(at, new float3(r, r * 0.8f, r), g.Pal.Metal, math.PI / 4f); break;
                case 2: g.Spike(at + new float3(0f, r * 0.6f, 0f), at - new float3(0f, r * 1.6f, 0f), r, 6, g.Pal.Metal); break;
                case 3:
                    g.Tube(at + new float3(0f, 0f, -r * 0.4f), at + new float3(0f, 0f, r * 0.4f), r * 1.15f, r * 1.15f, 8, g.Pal.Metal);
                    g.Gem(at + new float3(0f, 0f, r * 0.4f), new float3(0f, 0f, 1f), r * 0.45f, r * 0.4f, g.Pal.Accent);
                    break;
                case 4: // ring loop with a hanging tassel
                    g.Ring(at - new float3(0f, r * 0.9f, 0f), new float3(0f, 0f, 1f), r * 0.9f, r * 0.22f, 8, g.Pal.Metal);
                    g.Tube(at - new float3(0f, r * 1.8f, 0f), at - new float3(r * 0.4f, r * 4f, 0f), r * 0.35f, r * 0.55f, 5, g.Pal.Accent);
                    break;
                default: // fishtail
                    g.Tube(at, at + new float3(-r * 1.3f, -r * 1.2f, 0f), r * 0.45f, r * 0.2f, 5, g.Pal.Metal);
                    g.Tube(at, at + new float3(r * 1.3f, -r * 1.2f, 0f), r * 0.45f, r * 0.2f, 5, g.Pal.Metal);
                    g.Ball(at, r * 0.55f, g.Pal.Accent);
                    break;
            }
        }

        // ---------------------------------------------------------------- guards

        private static void Guard(Kit g, int variant, float y, float width)
        {
            Color m = g.Pal.Metal;
            switch (variant)
            {
                case 0: // straight cross
                    g.Box(new float3(0f, y, 0f), new float3(width * 0.5f, 0.012f, 0.018f), m);
                    break;
                case 1: // crescent, tips toward the blade
                    g.Loft(new[]
                    {
                        Sec(new float3(-width * 0.55f, y + 0.035f, 0f), new float3(0.008f, 0f, 0f), new float3(0f, 0f, 0.012f)),
                        Sec(new float3(0f, y, 0f), new float3(0f, 0.014f, 0f), new float3(0f, 0f, 0.018f)),
                        Sec(new float3(width * 0.55f, y + 0.035f, 0f), new float3(0.008f, 0f, 0f), new float3(0f, 0f, 0.012f)),
                    }, m, notch: false);
                    break;
                case 2: // disc
                    g.Tube(new float3(0f, y - 0.006f, 0f), new float3(0f, y + 0.006f, 0f), width * 0.32f, width * 0.32f, 10, m);
                    g.Ring(new float3(0f, y, 0f), new float3(0f, 1f, 0f), width * 0.32f, 0.004f, 10, g.Pal.Accent);
                    break;
                case 3: // stub
                    g.Box(new float3(0f, y, 0f), new float3(width * 0.25f, 0.016f, 0.02f), m);
                    break;
                case 4: // swept quillons curling back toward the hand, with a knuckle ring
                    for (int side = -1; side <= 1; side += 2)
                    {
                        g.Tube(new float3(0f, y, 0f), new float3(side * width * 0.4f, y + 0.012f, 0f), 0.008f, 0.007f, 5, m);
                        g.Tube(new float3(side * width * 0.4f, y + 0.012f, 0f), new float3(side * width * 0.52f, y - 0.03f, 0f), 0.007f, 0.004f, 5, m);
                    }
                    g.Ring(new float3(0f, y - 0.01f, 0.025f), new float3(1f, 0f, 0f), 0.02f, 0.004f, 6, m, 0f, math.PI);
                    break;
                default: // flared wings with a set stone
                    for (int side = -1; side <= 1; side += 2)
                        g.Loft(new[]
                        {
                            Sec(new float3(0f, y, 0f), new float3(0f, 0.016f, 0f), new float3(0f, 0f, 0.014f)),
                            Sec(new float3(side * width * 0.3f, y + 0.012f, 0f), new float3(0f, 0.02f, 0f), new float3(0f, 0f, 0.008f)),
                            Sec(new float3(side * width * 0.55f, y + 0.05f, 0f), new float3(0f, 0.004f, 0f), new float3(0f, 0f, 0.003f)),
                        }, m, notch: false);
                    g.Gem(new float3(0f, y + 0.004f, 0.012f), new float3(0f, 0f, 1f), 0.011f, 0.01f, g.Pal.Accent);
                    break;
            }
        }

        // ---------------------------------------------------------------- blades

        // A lozenge-section blade lofted along `dir`, bending sideways by `curve`; its profile comes from `shape` and its
        // flat carries the `face` treatment (fuller, region motif, ridge).
        private static void BladeAlong(Kit g, float3 root, float3 dir, float length, float width, int shape, float curve, int face)
        {
            int segs = shape == 4 ? 14 : 8;
            float3 side = new float3(1f, 0f, 0f);
            float thick = width * 0.22f * g.Spec.Thickness;
            var sections = new List<(float3, float3, float3)>();
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                float taper;
                float offset = 0f;
                switch (shape)
                {
                    case 0: taper = 1f + 0.18f * math.sin(t * math.PI) - t * t * 1.13f; break;       // leaf
                    case 1: taper = t < 0.78f ? 1f : (1f - t) / 0.22f; break;                       // clipped point
                    case 2: taper = math.sqrt(math.max(0f, 1f - t * t)); break;                     // round
                    case 3: taper = 1f - t * 0.55f; break;                                           // chisel
                    case 4: taper = (1f - t * t * 0.9f) * (1f + 0.2f * math.sin(t * math.PI * 7f)); break; // flamberge
                    default:                                                                         // cleaver: widens to an angled tip
                        taper = t < 0.82f ? 0.8f + 0.5f * t : (1f - t) / 0.18f * 1.21f;
                        offset = 0.3f * t;
                        break;
                }
                taper = math.max(0f, taper) * g.Wobble();
                if (i == segs && shape != 3) taper = 0f;
                float3 c = root + dir * (t * length) + side * (curve * length * 0.25f * t * t + offset * width);
                sections.Add(Sec(c, side * width * taper, new float3(0f, 0f, thick * math.max(taper, 0.15f))));
            }
            g.Loft(sections.ToArray(), g.Pal.Metal, notch: true, edge: g.Pal.Edge);

            // REQ: gear-looks.md#部件槽 (刃面紋樣).
            float3 mid = root + dir * (length * 0.42f) + side * (curve * length * 0.04f);
            float hu = length * 0.3f, hv = width * 0.42f;
            for (int f = -1; f <= 1; f += 2)
            {
                var n = new float3(0f, 0f, f);
                float3 o = mid + n * thick * 0.75f;
                switch (face)
                {
                    case 0: // single fuller
                        g.Bar(o - dir * hu, o + dir * hu, width * 0.1f, 0.0012f, n, Mul(g.Pal.Metal, 0.7f));
                        break;
                    case 1:
                        Engrave(g, new Plate(o, dir, side, n, hu, hv), g.Spec.Motif.Region, 0, g.Pal.Accent);
                        break;
                    case 2:
                        Engrave(g, new Plate(o, dir, side, n, hu, hv), g.Spec.Motif.Region, 1, g.Pal.Accent);
                        break;
                    case 3: // twin fullers
                        g.Bar(o - dir * hu + side * width * 0.2f, o + dir * hu * 0.8f + side * width * 0.2f, width * 0.06f, 0.0012f, n, Mul(g.Pal.Metal, 0.7f));
                        g.Bar(o - dir * hu - side * width * 0.2f, o + dir * hu * 0.8f - side * width * 0.2f, width * 0.06f, 0.0012f, n, Mul(g.Pal.Metal, 0.7f));
                        break;
                    case 4: // fuller with a motif plate at the root
                        g.Bar(o, o + dir * hu, width * 0.09f, 0.0012f, n, Mul(g.Pal.Metal, 0.7f));
                        Engrave(g, new Plate(o - dir * hu * 0.6f, dir, side, n, hu * 0.35f, hv), g.Spec.Motif.Region, 0, g.Pal.Accent);
                        break;
                    default: // raised spine with an inlaid motif beside it
                        g.Bar(root + dir * 0.01f + n * thick * 0.7f, root + dir * (length * 0.85f) + n * thick * 0.35f, width * 0.05f, 0.002f, n, g.Pal.Edge);
                        Engrave(g, new Plate(o + side * width * 0.25f, dir, side, n, hu * 0.8f, hv * 0.45f), g.Spec.Motif.Region, 1, g.Pal.Accent);
                        break;
                }
            }

            Cracks(g, new Plate(mid + new float3(0f, 0f, thick * 0.8f), dir, side, new float3(0f, 0f, 1f), hu, hv));
        }

        // ---------------------------------------------------------------- region motifs

        // A flat patch of surface: centre, along (U), across (V), outward normal, half extents in metres.
        private readonly struct Plate
        {
            public readonly float3 O, U, V, N;
            public readonly float HalfU, HalfV;

            public Plate(float3 o, float3 u, float3 v, float3 n, float halfU, float halfV)
            {
                O = o;
                U = math.normalizesafe(u, new float3(0f, 1f, 0f));
                V = math.normalizesafe(v, new float3(1f, 0f, 0f));
                N = math.normalizesafe(n, new float3(0f, 0f, 1f));
                HalfU = halfU;
                HalfV = halfV;
            }

            public float3 At(float2 p) => O + U * p.x + V * p.y;
        }

        private static readonly List<(float2 a, float2 b)> MotifScratch = new List<(float2, float2)>();

        // REQ: gear-looks.md#地區與時代的器物語彙 — the region's signature motif, two forms each, inlaid on a plate.
        // Element count follows MotifDensity, layout jitter follows the look's own motif stream.
        private static void Engrave(Kit g, in Plate p, ArchRegion region, int form, Color col)
        {
            MotifScratch.Clear();
            MotifSegments(region, form & 1, p.HalfU, p.HalfV, g.Spec.MotifDensity, ref g.MotifRng, MotifScratch);
            float lw = math.clamp(math.min(p.HalfU, p.HalfV) * 0.09f, 0.0012f, 0.012f);
            float depth = lw * 0.45f;
            float3 lift = p.N * depth * 0.6f;
            foreach ((float2 a, float2 b) in MotifScratch)
                g.Bar(p.At(a) + lift, p.At(b) + lift, lw * 0.5f, depth, p.N, col);
        }

        private static void MotifSegments(ArchRegion region, int form, float hu, float hv, float density, ref Pcg32 rng,
            List<(float2 a, float2 b)> o)
        {
            int n = 2 + (int)math.round(density * 5f);
            switch (region)
            {
                case ArchRegion.Verdant when form == 0: // leaf veins: a midrib with alternating side veins
                    o.Add((new float2(-hu * 0.9f, 0f), new float2(hu * 0.9f, 0f)));
                    for (int i = 0; i < n; i++)
                    {
                        float x = math.lerp(-0.75f, 0.7f, (i + 0.5f) / n) * hu;
                        float s = i % 2 == 0 ? 1f : -1f;
                        o.Add((new float2(x, 0f), new float2(x + hu * 0.2f, s * hv * 0.75f)));
                    }
                    break;
                case ArchRegion.Verdant: // creeping vine with leaves at the crests
                {
                    float phase = rng.NextFloat() * math.PI;
                    const int segs = 10;
                    float2 prev = Vine(-1f, hu, hv, phase);
                    for (int i = 1; i <= segs; i++)
                    {
                        float2 next = Vine(-1f + 2f * i / segs, hu, hv, phase);
                        o.Add((prev, next));
                        if (i % (segs / math.min(n, segs)) == 0 && i < segs) Diamond(o, next + new float2(0f, math.sign(next.y + 1e-4f) * hv * 0.2f), math.min(hu, hv) * 0.2f);
                        prev = next;
                    }
                    break;
                }
                case ArchRegion.Lava when form == 0: // sawtooth band
                {
                    int teeth = 3 + n;
                    for (int i = 0; i < teeth; i++)
                    {
                        float x0 = math.lerp(-0.9f, 0.9f, (float)i / teeth) * hu, x1 = math.lerp(-0.9f, 0.9f, (float)(i + 1) / teeth) * hu;
                        float y0 = (i % 2 == 0 ? -0.6f : 0.6f) * hv, y1 = -y0;
                        o.Add((new float2(x0, y0), new float2(x1, y1)));
                    }
                    break;
                }
                case ArchRegion.Lava: // molten flow lines that fork like cooling cracks
                    for (int line = -1; line <= 1; line += 2)
                    {
                        float2 prev = new float2(-hu * 0.9f, line * hv * 0.35f);
                        for (int i = 1; i <= 6; i++)
                        {
                            var next = new float2(math.lerp(-0.9f, 0.9f, i / 6f) * hu, line * hv * 0.35f + (rng.NextFloat() - 0.5f) * hv * 0.45f);
                            o.Add((prev, next));
                            if (i <= n / 2) o.Add((next, next + new float2(hu * 0.12f, -line * hv * 0.3f)));
                            prev = next;
                        }
                    }
                    break;
                case ArchRegion.Ice when form == 0: // snow crystals
                {
                    int stars = 1 + n / 3;
                    float r = math.min(hv * 0.85f, hu / stars * 0.85f);
                    for (int s = 0; s < stars; s++)
                    {
                        var c = new float2(math.lerp(-hu, hu, (s + 0.5f) / stars), 0f);
                        for (int arm = 0; arm < 3; arm++)
                        {
                            float a = arm * math.PI / 3f;
                            var d = new float2(math.cos(a), math.sin(a)) * r;
                            o.Add((c - d, c + d));
                            if (density > 0.4f)
                                for (int end = -1; end <= 1; end += 2)
                                {
                                    float2 at = c + d * end * 0.6f;
                                    var tick = new float2(math.cos(a + 0.9f), math.sin(a + 0.9f)) * r * 0.25f;
                                    o.Add((at, at + tick * end));
                                }
                        }
                    }
                    break;
                }
                case ArchRegion.Ice: // overlapping scales
                {
                    int cols = 1 + n;
                    float s = math.min(hu / cols, hv * 0.5f);
                    for (int row = -1; row <= 1; row++)
                        for (int col = 0; col < cols; col++)
                        {
                            float x = math.lerp(-hu, hu, (col + 0.5f + (row & 1) * 0.5f) / (cols + 0.5f));
                            float y = row * s * 1.1f;
                            o.Add((new float2(x - s * 0.8f, y + s * 0.4f), new float2(x, y - s * 0.4f)));
                            o.Add((new float2(x, y - s * 0.4f), new float2(x + s * 0.8f, y + s * 0.4f)));
                        }
                    break;
                }
                case ArchRegion.Storm when form == 0: // lightning bolt
                {
                    int kinks = 3 + n / 2;
                    float2 prev = new float2(-hu * 0.9f, hv * 0.55f);
                    for (int i = 1; i <= kinks; i++)
                    {
                        float x = math.lerp(-0.9f, 0.9f, (float)i / kinks) * hu;
                        float y = (i % 2 == 0 ? 0.55f : -0.55f) * hv * (0.6f + 0.4f * rng.NextFloat());
                        var next = new float2(x, y);
                        o.Add((prev, next));
                        prev = next;
                    }
                    break;
                }
                case ArchRegion.Storm: // whirlwind spirals
                {
                    int count = math.clamp((int)math.round(hu / math.max(hv, 1e-4f)), 1, 3);
                    float r = math.min(hv * 0.9f, hu / count * 0.9f);
                    float turns = 1.4f + density;
                    for (int s = 0; s < count; s++)
                    {
                        var c = new float2(math.lerp(-hu, hu, (s + 0.5f) / count), 0f);
                        float2 prev = c;
                        for (int i = 1; i <= 14; i++)
                        {
                            float t = i / 14f;
                            float a = t * turns * 2f * math.PI * (s % 2 == 0 ? 1f : -1f);
                            float2 next = c + new float2(math.cos(a), math.sin(a)) * r * t;
                            o.Add((prev, next));
                            prev = next;
                        }
                    }
                    break;
                }
                case ArchRegion.Crystal when form == 0: // lozenge lattice
                {
                    int cells = 1 + n;
                    float cw = hu / cells;
                    for (int i = 0; i < cells; i++)
                        Diamond(o, new float2(-hu + cw * (2 * i + 1), 0f), cw, hv * 0.8f);
                    break;
                }
                case ArchRegion.Crystal: // cut-gem facets
                {
                    float r = math.min(hu, hv) * 0.9f;
                    int rings = 1 + n / 3;
                    for (int k = 1; k <= rings; k++) Diamond(o, float2.zero, r * k / rings, r * k / rings);
                    o.Add((new float2(-r, 0f), new float2(r, 0f)));
                    o.Add((new float2(0f, -r), new float2(0f, r)));
                    if (hu > hv * 2f)
                    {
                        o.Add((new float2(-hu * 0.9f, 0f), new float2(-r, 0f)));
                        o.Add((new float2(r, 0f), new float2(hu * 0.9f, 0f)));
                    }
                    break;
                }
                case ArchRegion.Twilight when form == 0: // cloud scrolls
                {
                    int clouds = 1 + n / 3;
                    float r = math.min(hv * 0.4f, hu / clouds * 0.3f);
                    for (int s = 0; s < clouds; s++)
                    {
                        var c = new float2(math.lerp(-hu, hu, (s + 0.5f) / clouds), -hv * 0.2f);
                        Arc(o, c + new float2(-r * 1.4f, 0f), r, 0f, math.PI, 4);
                        Arc(o, c + new float2(0f, r * 0.5f), r * 1.2f, 0f, math.PI, 5);
                        Arc(o, c + new float2(r * 1.4f, 0f), r, 0f, math.PI, 4);
                        o.Add((c + new float2(-r * 2.4f, 0f), c + new float2(r * 2.4f, 0f)));
                        Arc(o, c + new float2(r * 2.4f, -r * 0.5f), r * 0.5f, math.PI * 0.5f, -math.PI, 4);
                    }
                    break;
                }
                default: // upturned eaves: a band whose ends curl up
                {
                    float y = -hv * 0.3f;
                    float r = math.min(hv * 0.8f, hu * 0.25f);
                    o.Add((new float2(-hu + r, y), new float2(hu - r, y)));
                    Arc(o, new float2(-hu + r, y + r), r, -math.PI * 0.5f, -math.PI, 4);
                    Arc(o, new float2(hu - r, y + r), r, -math.PI * 0.5f, 0f, 4);
                    if (n > 3) o.Add((new float2(-hu * 0.5f, y + hv * 0.35f), new float2(hu * 0.5f, y + hv * 0.35f)));
                    break;
                }
            }
        }

        private static float2 Vine(float t, float hu, float hv, float phase) =>
            new float2(t * hu * 0.9f, math.sin(phase + t * math.PI * 2f) * hv * 0.5f);

        private static void Diamond(List<(float2, float2)> o, float2 c, float rx, float ry = -1f)
        {
            if (ry < 0f) ry = rx * 0.6f;
            var n = new float2(0f, ry);
            var e = new float2(rx, 0f);
            o.Add((c - e, c + n));
            o.Add((c + n, c + e));
            o.Add((c + e, c - n));
            o.Add((c - n, c - e));
        }

        private static void Arc(List<(float2, float2)> o, float2 c, float r, float from, float to, int segs)
        {
            float2 prev = c + new float2(math.cos(from), math.sin(from)) * r;
            for (int i = 1; i <= segs; i++)
            {
                float a = math.lerp(from, to, (float)i / segs);
                float2 next = c + new float2(math.cos(a), math.sin(a)) * r;
                o.Add((prev, next));
                prev = next;
            }
        }

        // ---------------------------------------------------------------- era craft

        // REQ: gear-looks.md#地區與時代的器物語彙 (時代表) — each era's tell-tale construction detail at a joint.
        private static void EraFlourish(Kit g, float3 at, float r, float span)
        {
            switch (g.Era)
            {
                case ArchEra.Primal:
                    Lashing(g, at.y, r);
                    g.Tube(at + new float3(r * 1.1f, 0f, 0f), at + new float3(r * 1.6f, -span * 1.5f - 0.02f, 0f), r * 0.18f, r * 0.12f, 4, g.Pal.Wrap);
                    g.Ball(at + new float3(r * 1.6f, -span * 1.5f - 0.03f, 0f), r * 0.35f, g.Pal.Accent);
                    break;
                case ArchEra.Agrarian:
                    g.Tube(at - new float3(0f, 0.006f, 0f), at + new float3(0f, 0.006f, 0f), r * 1.15f, r * 1.15f, g.Sides, Mul(g.Pal.Metal, 0.85f));
                    break;
                case ArchEra.Mercantile:
                    g.Tube(at - new float3(0f, 0.008f, 0f), at + new float3(0f, 0.008f, 0f), r * 1.25f, r * 1.25f, g.Sides, g.Pal.Accent);
                    for (int k = 0; k < 2; k++)
                        g.Ball(at + new float3(0f, (k * 2 - 1) * 0.012f, r * 1.3f), r * 0.22f, g.Pal.Edge);
                    break;
                case ArchEra.Steampunk:
                    g.Tube(at - new float3(0f, 0.008f, 0f), at + new float3(0f, 0.008f, 0f), r * 2.2f, r * 2.2f, 12, g.Pal.Metal);
                    for (int k = 0; k < 8; k++)
                        g.Box(at + Dir(k * math.PI / 4f) * r * 2.4f, new float3(r * 0.3f, 0.007f, r * 0.3f), g.Pal.Metal, k * math.PI / 4f);
                    // Pressure line running up the piece to a gauge.
                    g.Tube(at + new float3(r * 1.3f, 0f, r * 0.6f), at + new float3(r * 1.3f, span + 0.03f, r * 0.6f), r * 0.2f, r * 0.2f, 5, g.Pal.Edge);
                    g.Tube(at + new float3(r * 1.3f, span + 0.03f, r * 0.2f), at + new float3(r * 1.3f, span + 0.03f, r * 1.1f), r * 0.55f, r * 0.55f, 8, g.Pal.Accent);
                    break;
                case ArchEra.Future:
                    g.Tube(at - new float3(0f, 0.004f, 0f), at + new float3(0f, 0.004f, 0f), r * 1.25f, r * 1.25f, 12, g.Pal.Accent);
                    g.Tube(at + new float3(0f, span * 0.6f - 0.002f, 0f), at + new float3(0f, span * 0.6f + 0.002f, 0f), r * 1.15f, r * 1.15f, 12, g.Pal.Accent);
                    g.Box(at + new float3(0f, span * 0.3f, -r * 1.2f), new float3(0.002f, span * 0.4f + 0.008f, r * 0.6f), g.Pal.Metal);
                    break;
                case ArchEra.Wasteland:
                    g.Box(at + new float3(r * 0.6f, span * 0.3f, 0f), new float3(r * 0.5f, span * 0.4f + 0.01f, r * 1.25f), g.Pal.Metal, 0f, 0.2f);
                    g.Ball(at + new float3(r * 1.1f, span * 0.6f, r * 0.6f), r * 0.2f, g.Pal.Edge);
                    // Rag strips tied on and left hanging.
                    g.Box(at + new float3(-r * 1.2f, -span - 0.02f, 0f), new float3(r * 0.35f, span + 0.02f, 0.0015f), g.Pal.Wrap, 0.3f, 0.15f);
                    break;
                case ArchEra.Ancient:
                    g.Tube(at - new float3(0f, 0.01f, 0f), at + new float3(0f, 0.01f, 0f), r * 1.5f, r * 1.4f, 6, g.Pal.Accent);
                    for (int k = 0; k < 3; k++) // rune glyph ticks around the band
                    {
                        float3 d = Dir(k * 2f * math.PI / 3f + 0.3f) * r * 1.52f;
                        g.Bar(at + d + new float3(0f, 0.014f, 0f), at + d + new float3(0f, 0.03f, 0f), r * 0.12f, r * 0.08f, d, g.Pal.Accent);
                    }
                    break;
                default: // Industrial: hex bolts at the quarters
                    for (int k = 0; k < 4; k++)
                        g.Tube(at + Dir(k * math.PI / 2f) * r * 0.9f + new float3(0f, span * 0.5f, 0f),
                            at + Dir(k * math.PI / 2f) * r * 1.25f + new float3(0f, span * 0.5f, 0f), r * 0.28f, r * 0.28f, 6, g.Pal.Edge);
                    break;
            }
        }

        // ---------------------------------------------------------------- wear

        // REQ: gear-looks.md#五軸 (毀損: 裂紋) — dark forked crack lines across a plate, more with damage.
        private static void Cracks(Kit g, in Plate p)
        {
            if (g.Spec.Damage < 0.3f) return;
            int count = (int)math.floor(g.Spec.Damage * 3.5f);
            float lw = math.clamp(math.min(p.HalfU, p.HalfV) * 0.06f, 0.001f, 0.006f);
            for (int c = 0; c < count; c++)
            {
                var prev = new float2((g.Rng.NextFloat() - 0.5f) * p.HalfU * 1.4f, (g.Rng.NextFloat() > 0.5f ? 1f : -1f) * p.HalfV * 0.95f);
                for (int s = 0; s < 3; s++)
                {
                    var next = prev + new float2((g.Rng.NextFloat() - 0.5f) * p.HalfU * 0.5f, -math.sign(prev.y + 1e-5f) * p.HalfV * 0.35f);
                    g.Bar(p.At(prev) + p.N * lw * 0.3f, p.At(next) + p.N * lw * 0.3f, lw * 0.5f, lw * 0.4f, p.N, Soot);
                    prev = next;
                }
            }
        }

        // Damaged cloth: patches in the secondary colour and moth holes.
        private static void Patches(Kit g, float y0, float y1, float rx, float rz)
        {
            int patches = (int)math.floor(g.Spec.Damage * 4f);
            for (int k = 0; k < patches; k++)
            {
                float a = g.Rng.NextFloat() * math.PI * 2f;
                float y = math.lerp(y0, y1, 0.15f + 0.7f * g.Rng.NextFloat());
                var at = new float3(math.sin(a) * rx * 1.03f, y, math.cos(a) * rz * 1.03f);
                if (k % 2 == 0)
                {
                    g.Box(at, new float3(0.032f, 0.032f, 0.004f), g.Pal.Second, a);
                    g.Bar(at + new float3(-0.02f * math.cos(a), 0.03f, 0.02f * math.sin(a)), at + new float3(0.02f * math.cos(a), 0.03f, -0.02f * math.sin(a)),
                        0.002f, 0.003f, new float3(math.sin(a), 0f, math.cos(a)), g.Pal.Wrap);
                }
                else
                {
                    g.Tube(at - new float3(math.sin(a), 0f, math.cos(a)) * 0.004f, at + new float3(math.sin(a), 0f, math.cos(a)) * 0.004f,
                        0.012f + 0.01f * g.Spec.Damage, 0.012f, 6, Soot);
                }
            }
        }

        // Badly damaged hafted gear loses its head: the haft ends short.
        private static float Broken(Kit g, float len) => g.Spec.Damage > 0.85f ? len * 0.62f : len;
    }
}
