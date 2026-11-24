using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    // Shields, garments and ornaments. Slot indices follow gear-looks.md#部件槽 in table order.
    public static partial class GearMeshes
    {
        // ---------------------------------------------------------------- Shield: 盾體, 盾心, 盾緣, 紋章

        private static void BuildShield(Kit g)
        {
            GearLookSpec s = g.Spec;
            float h = g.Len * 0.5f;
            float w = h * (0.75f + 0.25f * s.Width);
            float bulge = 0.02f + s.Curvature * 0.06f;
            int shape = g.V(0);
            const int n = 20;
            var rim = new float3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * math.PI / n;
                float x = math.sin(a) * w, y = math.cos(a) * h;
                switch (shape)
                {
                    case 1: if (y < 0f) x *= 1f + y / h * 0.85f; break;                                     // kite
                    case 2: y = y > 0f ? h * 0.75f : y; if (y < 0f) x *= 1f + y / h * 0.4f; break;          // heater
                    case 3: x = math.sign(x) * w * 0.85f; y = math.clamp(y * 1.3f, -h * 1.15f, h * 1.15f); break; // tower
                    case 4: x *= 0.72f; y *= 1.12f; break;                                                   // tall oval
                    case 5: float scallop = 1f + 0.07f * math.cos(a * 8f); x *= scallop; y = math.cos(a) * w * scallop; break; // lotus round
                }
                rim[i] = new float3(x, y, 0f);
            }

            // Front face as a fan to a raised centre; back is flat. Damage dents the rim inward.
            int cf = g.Part.Add(new float3(0f, 0f, bulge), g.Pal.Main, float2.zero);
            int cb = g.Part.Add(new float3(0f, 0f, -0.012f), g.Pal.Second, float2.zero);
            int first = g.Part.P.Count;
            for (int i = 0; i < n; i++)
            {
                float dent = g.Nick(0.12f);
                rim[i] *= 1f - dent;
                g.Part.Add(rim[i] + new float3(0f, 0f, 0.006f), Mul(g.Pal.Main, 0.92f), float2.zero);
                g.Part.Add(rim[i] - new float3(0f, 0f, 0.012f), g.Pal.Second, float2.zero);
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

            float extentY = 0f;
            foreach (float3 p in rim) extentY = math.max(extentY, math.abs(p.y));
            float FaceZ(float r) => bulge * (1f - r) + 0.006f * r + 0.003f;

            // Plank seams on wooden shields (the eras that build in timber).
            if (g.Era == ArchEra.Primal || g.Era == ArchEra.Agrarian || g.Era == ArchEra.Wasteland)
                for (int k = -2; k <= 2; k++)
                {
                    float x = k * w * 0.32f;
                    float r = math.abs(x) / w;
                    // Clip each seam to the rim at its x so it never pokes past a tapering or scalloped outline.
                    float top = float.MaxValue, bottom = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        float3 p0 = rim[i], p1 = rim[(i + 1) % n];
                        if ((p0.x - x) * (p1.x - x) > 0f || math.abs(p1.x - p0.x) < 1e-5f) continue;
                        float y = math.lerp(p0.y, p1.y, (x - p0.x) / (p1.x - p0.x));
                        if (y >= 0f) top = math.min(top, y);
                        else bottom = math.min(bottom, -y);
                    }
                    if (top == float.MaxValue || bottom == float.MaxValue) continue;
                    g.Bar(new float3(x, -bottom * 0.9f, FaceZ(r)), new float3(x, top * 0.9f, FaceZ(r)), 0.0025f, 0.0015f, Front, Mul(g.Pal.Main, 0.62f));
                }

            ShieldEmblem(g, g.V(3), w, extentY, FaceZ(0.4f));
            ShieldRim(g, g.V(2), rim);
            ShieldBoss(g, g.V(1), bulge);
            Cracks(g, new Plate(new float3(0f, 0f, FaceZ(0.3f)), Up, new float3(1f, 0f, 0f), Front, extentY * 0.7f, w * 0.7f));

            // Arm strap and handle on the back so the mesh reads as held.
            g.Box(new float3(0f, 0f, -0.04f), new float3(0.015f, h * 0.35f, 0.012f), g.Pal.Wrap);
            g.Box(new float3(0f, h * 0.2f, -0.03f), new float3(w * 0.4f, 0.012f, 0.008f), g.Pal.Wrap);
        }

        private static void ShieldEmblem(Kit g, int variant, float w, float hy, float z)
        {
            var face = new Plate(new float3(0f, 0f, z), Up, new float3(1f, 0f, 0f), Front, hy * 0.62f, w * 0.62f);
            Color a = g.Pal.Accent;
            float bw = math.max(0.008f, w * 0.06f);
            switch (variant)
            {
                case 0: // cross
                    g.Bar(new float3(0f, -hy * 0.85f, z), new float3(0f, hy * 0.85f, z), bw, 0.002f, Front, a);
                    g.Bar(new float3(-w * 0.85f, hy * 0.15f, z), new float3(w * 0.85f, hy * 0.15f, z), bw, 0.002f, Front, a);
                    break;
                case 1: Engrave(g, face, g.Spec.Motif.Region, 0, a); break;
                case 2: Engrave(g, face, g.Spec.Motif.Region, 1, a); break;
                case 3: // chevron
                    g.Bar(new float3(-w * 0.8f, -hy * 0.45f, z), new float3(0f, hy * 0.25f, z), bw, 0.002f, Front, a);
                    g.Bar(new float3(w * 0.8f, -hy * 0.45f, z), new float3(0f, hy * 0.25f, z), bw, 0.002f, Front, a);
                    g.Bar(new float3(-w * 0.8f, -hy * 0.15f, z), new float3(0f, hy * 0.55f, z), bw * 0.5f, 0.002f, Front, g.Pal.Second);
                    g.Bar(new float3(w * 0.8f, -hy * 0.15f, z), new float3(0f, hy * 0.55f, z), bw * 0.5f, 0.002f, Front, g.Pal.Second);
                    break;
                case 4: // bordure and roundel
                    g.Ring(new float3(0f, 0f, z), Front, math.min(w, hy) * 0.7f, bw * 0.6f, 14, a);
                    g.Ring(new float3(0f, 0f, z), Front, math.min(w, hy) * 0.45f, bw * 0.35f, 12, g.Pal.Second);
                    break;
                default: // saltire with a small motif plate between the arms
                    g.Bar(new float3(-w * 0.75f, -hy * 0.75f, z), new float3(w * 0.75f, hy * 0.75f, z), bw, 0.002f, Front, a);
                    g.Bar(new float3(w * 0.75f, -hy * 0.75f, z), new float3(-w * 0.75f, hy * 0.75f, z), bw, 0.002f, Front, a);
                    Engrave(g, new Plate(new float3(0f, hy * 0.55f, z), new float3(1f, 0f, 0f), Up, Front, w * 0.3f, hy * 0.16f), g.Spec.Motif.Region, 1, g.Pal.Edge);
                    break;
            }
        }

        private static void ShieldRim(Kit g, int variant, float3[] rim)
        {
            int n = rim.Length;
            var lift = new float3(0f, 0f, 0.008f);
            switch (variant)
            {
                case 1: // metal band
                    for (int i = 0; i < n; i++) g.Tube(rim[i] + lift, rim[(i + 1) % n] + lift, 0.012f, 0.012f, 4, g.Pal.Metal);
                    break;
                case 2: // studded
                    for (int i = 0; i < n; i += 2) g.Ball(rim[i] * 0.93f + lift, 0.011f, g.Pal.Edge);
                    break;
                case 3: // double band
                    for (int i = 0; i < n; i++)
                    {
                        g.Tube(rim[i] + lift, rim[(i + 1) % n] + lift, 0.01f, 0.01f, 4, g.Pal.Metal);
                        g.Tube(rim[i] * 0.88f + lift * 1.6f, rim[(i + 1) % n] * 0.88f + lift * 1.6f, 0.005f, 0.005f, 4, g.Pal.Accent);
                    }
                    break;
                case 4: // spiked rim
                    for (int i = 0; i < n; i++) g.Tube(rim[i] + lift, rim[(i + 1) % n] + lift, 0.009f, 0.009f, 4, g.Pal.Metal);
                    for (int i = 0; i < n; i += 4)
                        g.Spike(rim[i] + lift, rim[i] * 1.12f + lift, 0.012f, 4, g.Pal.Edge);
                    break;
                case 5: // rawhide wrap
                    for (int i = 0; i < n; i++) g.Tube(rim[i] + lift * 0.5f, rim[(i + 1) % n] + lift * 0.5f, 0.016f, 0.016f, 5, g.Pal.Wrap);
                    for (int i = 0; i < n; i += 3) g.Bar(rim[i] * 0.94f + lift, rim[i] * 1.04f + lift, 0.004f, 0.012f, rim[i], Mul(g.Pal.Wrap, 0.75f));
                    break;
            }
        }

        private static void ShieldBoss(Kit g, int variant, float bulge)
        {
            float boss = 0.05f + 0.02f * (g.Spec.Width - 1f) * 2f;
            var c = new float3(0f, 0f, bulge * 0.6f);
            switch (variant)
            {
                case 0: g.Ellipsoid(c + Front * 0.012f, new float3(boss, boss, boss * 0.6f), g.Pal.Metal); break;
                case 1: g.Spike(c, c + Front * (bulge + 0.08f), boss * 0.8f, g.Sides, g.Pal.Metal); break;
                case 2: // flat plate with rivets
                    g.Tube(c, c + Front * 0.02f, boss, boss, g.Sides, g.Pal.Metal);
                    for (int k = 0; k < 6; k++)
                        g.Ball(c + Front * 0.022f + new float3(math.cos(k * math.PI / 3f), math.sin(k * math.PI / 3f), 0f) * boss * 0.75f, boss * 0.1f, g.Pal.Edge);
                    break;
                case 3: // open umbo ring
                    g.Ring(c + Front * 0.012f, Front, boss * 0.9f, boss * 0.14f, 10, g.Pal.Metal);
                    break;
                case 4: // set stone
                    g.Tube(c, c + Front * 0.018f, boss * 0.8f, boss * 0.7f, g.Sides, g.Pal.Metal);
                    g.Gem(c + Front * 0.018f, Front, boss * 0.45f, boss * 0.4f, g.Pal.Accent);
                    break;
                default: // beast boss: a snout with horns
                    g.Ellipsoid(c + Front * 0.015f, new float3(boss * 0.9f, boss * 1.1f, boss * 0.6f), g.Pal.Metal);
                    for (int side = -1; side <= 1; side += 2)
                        g.Spike(c + new float3(side * boss * 0.6f, boss * 0.6f, 0.02f), c + new float3(side * boss * 1.5f, boss * 1.6f, 0.05f), boss * 0.22f, 5, g.Pal.Edge);
                    g.Ball(c + new float3(0f, -boss * 0.5f, boss * 0.6f), boss * 0.2f, g.Pal.Accent);
                    break;
            }
        }

        // ---------------------------------------------------------------- Garments: 剪裁底型, 領, 袖／褲管, 下襬, 鑲邊, 護片

        private static void BuildUpper(Kit g)
        {
            GearLookSpec s = g.Spec;
            float h = g.Len;
            float sw = 0.2f * s.Width, waist = sw * (0.8f + s.Curvature * 0.2f);
            int cut = g.V(0);
            (float hemY, float flare) = cut switch
            {
                0 => (-0.05f * h, 1.05f), // tunic
                1 => (-0.45f * h, 1.35f), // long coat
                2 => (0f, 1.02f),         // vest
                3 => (-0.6f * h, 1.5f),   // robe
                4 => (0.12f * h, 0.98f),  // cropped jacket
                _ => (-0.08f * h, 1.08f), // tunic under a cape
            };
            g.Loft(new[]
            {
                Sec(new float3(0f, hemY, 0f), new float3(waist * flare, 0f, 0f), new float3(0f, 0f, waist * 0.72f * math.min(flare, 1.2f))),
                Sec(new float3(0f, h * 0.45f, 0f), new float3(waist, 0f, 0f), new float3(0f, 0f, waist * 0.62f)),
                Sec(new float3(0f, h * 0.78f, 0f), new float3(sw * 0.98f, 0f, 0f), new float3(0f, 0f, sw * 0.6f)),
                Sec(new float3(0f, h * 0.92f, 0f), new float3(sw, 0f, 0f), new float3(0f, 0f, sw * 0.52f)),
                Sec(new float3(0f, h, 0f), new float3(sw * 0.45f, 0f, 0f), new float3(0f, 0f, sw * 0.4f)),
            }, g.Pal.Main, notch: false, square: true);
            if (cut == 5) // cape panel falling behind from the shoulders
                g.Loft(new[]
                {
                    Sec(new float3(0f, h * 0.95f, -sw * 0.45f), new float3(sw * 1.05f, 0f, 0f), new float3(0f, 0f, 0.006f)),
                    Sec(new float3(0f, h * 0.3f, -sw * 0.75f), new float3(sw * 1.25f, 0f, 0f), new float3(0f, 0f, 0.006f)),
                    Sec(new float3(0f, -0.3f * h, -sw * 0.85f), new float3(sw * 1.35f * (0.9f + s.Curvature * 0.3f), 0f, 0f), new float3(0f, 0f, 0.006f)),
                }, g.Pal.Second, notch: false, square: true);
            if (cut != 2) Sleeves(g, g.V(2), sw, h, cut == 3);
            Collar(g, g.V(1), sw, h);
            Hem(g, g.V(3), hemY, waist * flare, waist * 0.72f * math.min(flare, 1.2f));
            GarmentTrim(g, g.V(4), hemY, h, waist, sw, upper: true);
            UpperPlates(g, g.V(5), sw, waist, h);
            Patches(g, hemY, h * 0.9f, waist, waist * 0.65f);
        }

        private static void Sleeves(Kit g, int variant, float sw, float h, bool robe)
        {
            float bell = robe ? 1.4f : 1f;
            for (int side = -1; side <= 1; side += 2)
            {
                var sh = new float3(side * sw * 0.95f, h * 0.88f, 0f);
                var cuff = new float3(side * (sw + 0.07f), h * 0.3f, 0f);
                var elbow = math.lerp(sh, cuff, 0.5f);
                float r0 = 0.06f, r1 = 0.05f * (1f + g.Spec.Curvature * 0.5f) * bell;
                switch (variant)
                {
                    case 0: g.Tube(sh, cuff, r0, r1, 6, g.Pal.Main); break;
                    case 1: // short
                        g.Tube(sh, elbow, r0, r0 * 1.05f, 6, g.Pal.Main);
                        break;
                    case 2: // bell
                        g.Tube(sh, elbow, r0, r0 * 1.15f, 6, g.Pal.Main);
                        g.Tube(elbow, cuff - Up * 0.04f, r0 * 1.15f, r1 * 1.9f, 6, g.Pal.Main);
                        g.Tube(cuff - Up * 0.04f, cuff - Up * 0.055f, r1 * 1.92f, r1 * 1.92f, 6, g.Pal.Accent);
                        break;
                    case 3: // puffed shoulder
                        g.Ellipsoid(sh + new float3(side * 0.02f, -0.02f, 0f), new float3(0.085f, 0.08f, 0.08f), g.Pal.Main);
                        g.Tube(sh, cuff, r0 * 0.8f, r1 * 0.75f, 6, g.Pal.Second);
                        break;
                    case 4: // rolled cuffs
                        g.Tube(sh, math.lerp(sh, cuff, 0.7f), r0, r1, 6, g.Pal.Main);
                        g.Tube(math.lerp(sh, cuff, 0.62f), math.lerp(sh, cuff, 0.74f), r1 * 1.3f, r1 * 1.3f, 6, g.Pal.Second);
                        break;
                    default: // short sleeve and a bracer
                        g.Tube(sh, elbow, r0, r0, 6, g.Pal.Main);
                        g.Tube(math.lerp(sh, cuff, 0.6f), cuff, r1 * 1.05f, r1 * 1.12f, 6, g.Pal.Metal);
                        g.Bar(math.lerp(sh, cuff, 0.62f) + Front * r1 * 1.1f, cuff + Front * r1 * 1.15f, r1 * 0.25f, 0.003f, Front, g.Pal.Edge);
                        break;
                }
            }
        }

        private static void Collar(Kit g, int variant, float sw, float h)
        {
            Color c = g.Pal.Second;
            switch (variant)
            {
                case 0: g.Tube(new float3(0f, h * 0.97f, 0f), new float3(0f, h * 1.03f, 0f), sw * 0.5f, sw * 0.42f, 8, c); break;
                case 1: // high stand collar
                    g.Tube(new float3(0f, h * 0.96f, 0f), new float3(0f, h * 1.12f, 0f), sw * 0.46f, sw * 0.5f, 8, c);
                    g.Ring(new float3(0f, h * 1.12f, 0f), Up, sw * 0.5f, 0.005f, 8, g.Pal.Accent);
                    break;
                case 2: // hood bunched behind the neck
                    g.Ellipsoid(new float3(0f, h * 0.98f, -sw * 0.45f), new float3(sw * 0.6f, sw * 0.35f, sw * 0.32f), c);
                    break;
                case 3: // shawl lapels
                    for (int side = -1; side <= 1; side += 2)
                        g.Bar(new float3(side * sw * 0.42f, h * 0.99f, sw * 0.35f), new float3(side * sw * 0.05f, h * 0.6f, sw * 0.58f), sw * 0.14f, 0.006f,
                            new float3(side * 0.3f, 0.2f, 1f), c);
                    break;
                case 4: // V neck edged in the accent colour
                    for (int side = -1; side <= 1; side += 2)
                        g.Bar(new float3(side * sw * 0.38f, h * 1.0f, sw * 0.32f), new float3(0f, h * 0.72f, sw * 0.56f), 0.01f, 0.004f, Front, g.Pal.Accent);
                    break;
                default: // fur ruff
                    g.Ring(new float3(0f, h * 0.97f, 0f), Up, sw * 0.62f, sw * 0.16f, 10, Color.Lerp(g.Pal.Wrap, Color.white, 0.25f));
                    break;
            }
        }

        // `rx`, `rz`: half extents of the hem's rounded-square section.
        private static void Hem(Kit g, int variant, float y, float rx, float rz)
        {
            float r = math.max(rx, rz);
            switch (variant)
            {
                case 0:
                    g.Tube(new float3(0f, y, 0f), new float3(0f, y + 0.02f, 0f), rx * 1.04f, rx * 1.04f, 10, g.Pal.Second);
                    break;
                case 1: // swallow tails behind
                    for (int side = -1; side <= 1; side += 2)
                        g.Spike(new float3(side * rx * 0.35f, y + 0.04f, -rz * 0.9f), new float3(side * rx * 0.5f, y - r * 0.9f, -rz * 1.1f), rx * 0.3f, 4, g.Pal.Main);
                    break;
                case 2: // side slits
                    for (int side = -1; side <= 1; side += 2)
                        g.Bar(new float3(side * rx * 1.02f, y, 0f), new float3(side * rx * 1.0f, y + r * 0.6f, 0f), 0.012f, 0.004f, new float3(side, 0f, 0f), Soot);
                    break;
                case 3: // dagged edge
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * math.PI / 4f;
                        var at = new float3(math.sin(a) * rx * 0.98f, y + 0.01f, math.cos(a) * rz * 0.98f);
                        g.Spike(at, at - Up * (0.05f + 0.03f * (k % 2)), 0.028f, 3, g.Pal.Main);
                    }
                    break;
                case 4: // layered underskirt
                    g.Tube(new float3(0f, y - 0.05f, 0f), new float3(0f, y + 0.02f, 0f), rx * 1.08f, rx * 1.0f, 10, g.Pal.Second);
                    g.Tube(new float3(0f, y - 0.055f, 0f), new float3(0f, y - 0.04f, 0f), rx * 1.1f, rx * 1.1f, 10, g.Pal.Accent);
                    break;
                default: // asymmetric wrap panel
                    g.Box(new float3(rx * 0.45f, y + r * 0.35f, rz * 1.02f), new float3(rx * 0.55f, r * 0.4f, 0.004f), g.Pal.Second, 0f, 0.35f);
                    break;
            }
        }

        private static void GarmentTrim(Kit g, int variant, float hemY, float topY, float waist, float sw, bool upper)
        {
            switch (variant)
            {
                case 0: // bands
                {
                    int bands = (int)math.round(g.Spec.MotifDensity * 3f);
                    for (int k = 0; k < bands; k++)
                    {
                        float y = hemY + 0.03f + k * 0.035f;
                        g.Tube(new float3(0f, y, 0f), new float3(0f, y + 0.018f, 0f), waist * 1.1f, waist * 1.1f, 8, g.Pal.Accent);
                    }
                    break;
                }
                case 1: // motif row across the front of the hem
                    Engrave(g, new Plate(new float3(0f, hemY + 0.07f, waist * 0.8f), new float3(1f, 0f, 0f), Up, Front, waist * 0.8f, 0.045f),
                        g.Spec.Motif.Region, 0, g.Pal.Accent);
                    break;
                case 2: // motif panel on the chest or thigh
                {
                    float y = upper ? topY * 0.68f : topY * 0.6f;
                    float z = upper ? sw * 0.62f : waist * 0.5f;
                    float x = upper ? sw * 0.38f : waist * 0.48f;
                    Engrave(g, new Plate(new float3(x, y, z), Up, new float3(1f, 0f, 0f), Front, 0.06f, 0.05f), g.Spec.Motif.Region, 1, g.Pal.Accent);
                    break;
                }
                case 3: // placket and buttons
                {
                    float z = waist * 0.72f;
                    g.Bar(new float3(0f, hemY + 0.02f, z), new float3(0f, topY * 0.95f, upper ? waist * 0.7f : z), 0.012f, 0.003f, Front, g.Pal.Second);
                    int buttons = 3 + (int)math.round(g.Spec.MotifDensity * 3f);
                    for (int k = 0; k < buttons; k++)
                    {
                        float y = math.lerp(hemY + 0.06f, topY * 0.9f, (k + 0.5f) / buttons);
                        g.Ball(new float3(0.0f, y, z + 0.006f), 0.008f, g.Era == ArchEra.Primal ? g.Pal.Wrap : g.Pal.Edge);
                    }
                    break;
                }
                case 4: // diagonal sash
                    g.Bar(new float3(-waist * 1.0f, upper ? topY * 0.9f : topY * 0.9f, waist * 0.5f), new float3(waist * 1.0f, upper ? topY * 0.25f : topY * 0.65f, waist * 0.6f),
                        0.03f, 0.006f, Front, g.Pal.Accent);
                    break;
                default: // side piping
                    for (int side = -1; side <= 1; side += 2)
                        g.Bar(new float3(side * waist * 1.02f, hemY + 0.02f, 0f), new float3(side * (upper ? sw * 1.0f : waist * 0.95f), topY * 0.85f, 0f),
                            0.007f, 0.004f, new float3(side, 0f, 0f), g.Pal.Accent);
                    break;
            }
        }

        private static void UpperPlates(Kit g, int variant, float sw, float waist, float h)
        {
            switch (variant)
            {
                case 1: // pauldrons
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var at = new float3(side * sw * 1.0f, h * 0.9f, 0f);
                        g.Ellipsoid(at, new float3(0.09f, 0.055f, 0.08f), g.Pal.Metal);
                        g.Ellipsoid(at + new float3(side * 0.02f, -0.045f, 0f), new float3(0.075f, 0.035f, 0.07f), Mul(g.Pal.Metal, 0.9f));
                        g.Ring(at + new float3(0f, 0.015f, 0f), new float3(side * 0.6f, 1f, 0f), 0.075f, 0.004f, 8, g.Pal.Edge);
                    }
                    break;
                case 2: // breastplate
                    g.Loft(new[]
                    {
                        Sec(new float3(0f, h * 0.42f, waist * 0.64f), new float3(waist * 0.85f, 0f, 0f), new float3(0f, 0f, 0.01f)),
                        Sec(new float3(0f, h * 0.7f, sw * 0.68f), new float3(sw * 0.85f, 0f, 0f), new float3(0f, 0f, 0.014f)),
                        Sec(new float3(0f, h * 0.88f, sw * 0.56f), new float3(sw * 0.7f, 0f, 0f), new float3(0f, 0f, 0.01f)),
                    }, g.Pal.Metal, notch: false, square: true);
                    g.Bar(new float3(0f, h * 0.45f, waist * 0.68f), new float3(0f, h * 0.86f, sw * 0.6f), 0.006f, 0.004f, Front, g.Pal.Edge);
                    break;
                case 3: // scale lames
                    for (int row = 0; row < 4; row++)
                        for (int col = -2; col <= 2; col++)
                        {
                            float y = h * (0.5f + row * 0.09f);
                            float x = (col + (row % 2) * 0.5f) * sw * 0.3f;
                            g.Box(new float3(x, y, sw * 0.62f + 0.004f), new float3(sw * 0.14f, h * 0.05f, 0.004f), row % 2 == 0 ? g.Pal.Metal : Mul(g.Pal.Metal, 0.88f));
                        }
                    break;
                case 4: // belt with pouches
                    g.Tube(new float3(0f, h * 0.06f, 0f), new float3(0f, h * 0.12f, 0f), waist * 1.06f, waist * 1.06f, 10, g.Pal.Wrap);
                    g.Box(new float3(0f, h * 0.09f, waist * 0.78f), new float3(0.022f, 0.02f, 0.006f), g.Pal.Edge);
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(new float3(side * waist * 0.75f, h * 0.03f, waist * 0.45f), new float3(0.04f, 0.045f, 0.025f), g.Pal.Wrap, side * 0.6f);
                    break;
                case 5: // cross straps with a buckle
                    g.Bar(new float3(-sw * 0.9f, h * 0.95f, sw * 0.5f), new float3(waist * 0.9f, h * 0.15f, waist * 0.68f), 0.018f, 0.004f, Front, g.Pal.Wrap);
                    g.Bar(new float3(sw * 0.9f, h * 0.95f, -sw * 0.5f), new float3(-waist * 0.9f, h * 0.15f, -waist * 0.68f), 0.018f, 0.004f, -Front, g.Pal.Wrap);
                    g.Box(new float3(0f, h * 0.55f, waist * 0.72f), new float3(0.02f, 0.02f, 0.006f), g.Pal.Edge);
                    break;
            }
        }

        private static void BuildLower(Kit g)
        {
            GearLookSpec s = g.Spec;
            float h = g.Len;
            float hip = 0.17f * s.Width;
            float beltY = h - 0.08f;
            int cut = g.V(0);
            float hemY = 0f, hemR = hip;
            switch (cut)
            {
                case 1: // skirt
                case 4: // pleated kilt
                {
                    float flare = 1.2f + s.Curvature * 0.5f;
                    float bottom = cut == 4 ? h * 0.42f : h * 0.15f;
                    g.Loft(new[]
                    {
                        Sec(new float3(0f, bottom, 0f), new float3(hip * flare, 0f, 0f), new float3(0f, 0f, hip * flare * 0.8f)),
                        Sec(new float3(0f, beltY, 0f), new float3(hip, 0f, 0f), new float3(0f, 0f, hip * 0.7f)),
                    }, g.Pal.Main, notch: false, square: true);
                    if (cut == 4)
                    {
                        for (int k = 0; k < 10; k++) // box pleats
                        {
                            float a = k * math.PI / 5f;
                            var lo = new float3(math.sin(a) * hip * flare * 1.01f, bottom + 0.01f, math.cos(a) * hip * flare * 0.81f);
                            var hi = new float3(math.sin(a) * hip * 1.01f, beltY - 0.02f, math.cos(a) * hip * 0.71f);
                            g.Bar(lo, hi, 0.006f, 0.003f, new float3(math.sin(a), 0f, math.cos(a)), Mul(g.Pal.Main, 0.78f));
                        }
                        Legs(g, hip, beltY * 0.5f, 0f, 0.8f, g.Pal.Second, 0f);
                    }
                    hemY = bottom;
                    hemR = hip * flare;
                    break;
                }
                case 2: // hakama: wide flared legs
                    Legs(g, hip, beltY, 0f, 1.5f + s.Curvature * 0.4f, g.Pal.Main, 0.4f);
                    hemR = hip * 1.2f;
                    break;
                case 3: // shorts over leggings
                    Legs(g, hip, beltY, h * 0.5f, 1.05f, g.Pal.Main, 0f);
                    Legs(g, hip * 0.85f, h * 0.55f, 0f, 0.85f, g.Pal.Second, 0f);
                    break;
                case 5: // baggy, gathered at the ankle
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var top = new float3(side * hip * 0.48f, beltY, 0f);
                        var knee = new float3(side * hip * 0.6f, h * 0.35f, 0f);
                        var ankle = new float3(side * hip * 0.52f, 0.04f, 0f);
                        g.Tube(top, knee, hip * 0.58f, hip * 0.62f, 6, g.Pal.Main);
                        g.Tube(knee, ankle, hip * 0.62f, hip * 0.3f, 6, g.Pal.Main);
                        g.Tube(ankle, ankle - Up * 0.04f, hip * 0.32f, hip * 0.32f, 6, g.Pal.Second);
                    }
                    break;
                default: // trousers
                    Legs(g, hip, beltY, 0f, 1f, g.Pal.Main, 0f);
                    break;
            }

            Waistband(g, g.V(1), hip, beltY, h);
            if (cut != 1 && cut != 4) Legwear(g, g.V(2), hip, h);
            LowerHem(g, g.V(3), hemY, hemR, cut == 1 || cut == 4, hip);
            GarmentTrim(g, g.V(4), hemY, beltY, hip, hip, upper: false);
            LowerPlates(g, g.V(5), hip, beltY, h);
            Patches(g, hemY, h * 0.8f, hip, hip * 0.7f);
        }

        private static void Legs(Kit g, float hip, float top, float bottom, float flare, Color c, float pleat)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var a = new float3(side * hip * 0.48f, top, 0f);
                var b = new float3(side * hip * (0.5f + g.Spec.Curvature * 0.2f) * math.max(1f, flare * 0.8f), bottom, 0f);
                g.Tube(a, b, hip * 0.55f, hip * 0.38f * flare, 6, c);
                if (pleat > 0f)
                    for (int k = -1; k <= 1; k++)
                        g.Bar(a + new float3(k * hip * 0.15f, 0f, hip * 0.5f), b + new float3(k * hip * 0.3f * flare, 0f, hip * 0.38f * flare),
                            0.004f, 0.003f, Front, Mul(c, 0.8f));
            }
        }

        private static void Waistband(Kit g, int variant, float hip, float beltY, float h)
        {
            float top = h;
            switch (variant)
            {
                case 0: g.Tube(new float3(0f, beltY, 0f), new float3(0f, top, 0f), hip * 1.02f, hip, 8, g.Pal.Second); break;
                case 1: // sash with hanging tails
                    g.Tube(new float3(0f, beltY, 0f), new float3(0f, top, 0f), hip * 1.05f, hip * 1.02f, 8, g.Pal.Accent);
                    for (int k = 0; k < 2; k++)
                        g.Box(new float3(hip * 0.55f + k * 0.03f, beltY - 0.12f, hip * 0.75f), new float3(0.018f, 0.12f, 0.003f), g.Pal.Accent, 0f, 0.1f + k * 0.12f);
                    break;
                case 2: // wide obi
                    g.Tube(new float3(0f, beltY - 0.06f, 0f), new float3(0f, top, 0f), hip * 1.06f, hip * 1.04f, 8, g.Pal.Second);
                    g.Tube(new float3(0f, beltY - 0.01f, 0f), new float3(0f, beltY + 0.01f, 0f), hip * 1.08f, hip * 1.08f, 8, g.Pal.Accent);
                    g.Box(new float3(0f, beltY - 0.02f, -hip * 0.78f), new float3(0.07f, 0.05f, 0.03f), g.Pal.Second);
                    break;
                case 3: // rope cord with a knot
                    g.Tube(new float3(0f, beltY, 0f), new float3(0f, top, 0f), hip, hip * 0.98f, 8, g.Pal.Main);
                    g.Ring(new float3(0f, beltY + 0.03f, 0f), Up, hip * 1.04f, 0.008f, 10, g.Pal.Wrap);
                    g.Ball(new float3(hip * 0.4f, beltY + 0.03f, hip * 0.72f), 0.016f, g.Pal.Wrap);
                    g.Tube(new float3(hip * 0.4f, beltY + 0.02f, hip * 0.74f), new float3(hip * 0.45f, beltY - 0.14f, hip * 0.76f), 0.005f, 0.008f, 4, g.Pal.Wrap);
                    break;
                case 4: // buckle belt
                    g.Tube(new float3(0f, beltY, 0f), new float3(0f, top, 0f), hip * 1.03f, hip * 1.01f, 8, g.Pal.Wrap);
                    g.Box(new float3(0f, (beltY + top) * 0.5f, hip * 0.74f), new float3(0.03f, 0.03f, 0.006f), g.Pal.Edge);
                    g.Box(new float3(0f, (beltY + top) * 0.5f, hip * 0.745f), new float3(0.018f, 0.018f, 0.006f), g.Pal.Wrap);
                    break;
                default: // belt with pouches
                    g.Tube(new float3(0f, beltY, 0f), new float3(0f, top, 0f), hip * 1.03f, hip * 1.01f, 8, g.Pal.Wrap);
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(new float3(side * hip * 0.8f, beltY - 0.04f, hip * 0.35f), new float3(0.035f, 0.04f, 0.022f), g.Pal.Wrap, side * 0.9f);
                    break;
            }
        }

        private static void Legwear(Kit g, int variant, float hip, float h)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * hip * 0.52f;
                var ankle = new float3(x, 0.02f, 0f);
                var shin = new float3(x, h * 0.32f, 0f);
                float r = hip * 0.42f;
                switch (variant)
                {
                    case 1: // puttee wraps
                        for (int k = 0; k < 5; k++)
                        {
                            float y = math.lerp(ankle.y, shin.y, k / 5f);
                            g.Tube(new float3(x, y, 0f), new float3(x, y + 0.035f, 0f), r * 1.05f, r * 1.08f, 6, k % 2 == 0 ? g.Pal.Wrap : Mul(g.Pal.Wrap, 0.85f));
                        }
                        break;
                    case 2: // boot tops
                        g.Tube(ankle, shin, r * 1.12f, r * 1.25f, 7, g.Pal.Wrap);
                        g.Tube(shin, shin + Up * 0.03f, r * 1.32f, r * 1.32f, 7, Mul(g.Pal.Wrap, 0.8f));
                        break;
                    case 3: // rolled cuffs
                        g.Tube(ankle + Up * 0.03f, ankle + Up * 0.08f, r * 1.12f, r * 1.12f, 6, g.Pal.Second);
                        break;
                    case 4: // flared bell bottoms
                        g.Tube(ankle + Up * 0.12f, ankle - Up * 0.01f, r * 1.0f, r * 1.5f, 6, g.Pal.Main);
                        break;
                    case 5: // greaves
                        g.Box(math.lerp(ankle, shin, 0.55f) + Front * r * 1.05f, new float3(r * 0.8f, (shin.y - ankle.y) * 0.45f, 0.008f), g.Pal.Metal);
                        g.Bar(ankle + Up * 0.03f + Front * r * 1.12f, shin + Front * r * 1.12f, 0.004f, 0.003f, Front, g.Pal.Edge);
                        break;
                }
            }
        }

        private static void LowerHem(Kit g, int variant, float y, float r, bool skirt, float hip)
        {
            if (skirt)
            {
                Hem(g, variant, y, r, r * 0.8f);
                return;
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var c = new float3(side * hip * 0.52f, y, 0f);
                float rr = hip * 0.42f;
                switch (variant)
                {
                    case 0: g.Tube(c, c + Up * 0.02f, rr * 1.12f, rr * 1.12f, 6, g.Pal.Second); break;
                    case 1: g.Tube(c, c + Up * 0.05f, rr * 1.15f, rr * 1.12f, 6, g.Pal.Accent); break;
                    case 2: g.Bar(c + new float3(side * rr, 0f, 0f), c + new float3(side * rr, 0.12f, 0f), 0.008f, 0.004f, new float3(side, 0f, 0f), Soot); break;
                    case 3:
                        for (int k = 0; k < 4; k++)
                        {
                            float a = k * math.PI / 2f + 0.4f;
                            var at = c + new float3(math.sin(a) * rr, 0.03f, math.cos(a) * rr);
                            g.Spike(at, at - Up * 0.05f, 0.022f, 3, g.Pal.Main);
                        }
                        break;
                    case 4:
                        g.Tube(c, c + Up * 0.015f, rr * 1.14f, rr * 1.14f, 6, g.Pal.Second);
                        g.Tube(c + Up * 0.03f, c + Up * 0.042f, rr * 1.12f, rr * 1.12f, 6, g.Pal.Accent);
                        break;
                    default:
                        g.Box(c + new float3(0f, 0.08f, rr * 1.02f), new float3(rr * 0.7f, 0.06f, 0.004f), g.Pal.Second, 0f, side * 0.4f);
                        break;
                }
            }
        }

        private static void LowerPlates(Kit g, int variant, float hip, float beltY, float h)
        {
            switch (variant)
            {
                case 1: // tassets
                    for (int side = -1; side <= 1; side += 2)
                        for (int k = 0; k < 3; k++)
                            g.Box(new float3(side * hip * 0.55f, beltY - 0.05f - k * 0.055f, hip * 0.72f + k * 0.004f), new float3(hip * 0.38f, 0.03f, 0.005f),
                                k % 2 == 0 ? g.Pal.Metal : Mul(g.Pal.Metal, 0.88f), 0f, side * 0.08f);
                    break;
                case 2: // knee guards
                    for (int side = -1; side <= 1; side += 2)
                        g.Ellipsoid(new float3(side * hip * 0.55f, h * 0.38f, hip * 0.42f), new float3(hip * 0.3f, hip * 0.32f, hip * 0.16f), g.Pal.Metal);
                    break;
                case 3: // thigh pouch
                    g.Box(new float3(hip * 0.85f, h * 0.6f, hip * 0.1f), new float3(0.03f, 0.05f, 0.04f), g.Pal.Wrap);
                    g.Tube(new float3(hip * 0.55f, h * 0.62f, 0f), new float3(hip * 0.55f, h * 0.65f, 0f), hip * 0.62f, hip * 0.62f, 8, g.Pal.Wrap);
                    break;
                case 4: // chaps panels
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(new float3(side * hip * 0.55f, h * 0.45f, hip * 0.5f), new float3(hip * 0.36f, h * 0.3f, 0.005f), g.Pal.Wrap, 0f, side * 0.05f);
                    break;
                case 5: // loincloth panel
                    g.Box(new float3(0f, beltY - h * 0.22f, hip * 0.78f), new float3(hip * 0.42f, h * 0.22f, 0.004f), g.Pal.Accent);
                    Engrave(g, new Plate(new float3(0f, beltY - h * 0.2f, hip * 0.79f), Up, new float3(1f, 0f, 0f), Front, h * 0.15f, hip * 0.3f), g.Spec.Motif.Region, 0, g.Pal.Main);
                    break;
            }
        }

        // ---------------------------------------------------------------- Ornaments: 底型, 主飾, 垂飾

        private static void BuildHeadwear(Kit g)
        {
            GearLookSpec s = g.Spec;
            float r = 0.11f * s.Width;
            float top; // height of the crown, where the main ornament sits
            switch (g.V(0))
            {
                case 0: // brimmed hat
                    g.Tube(new float3(0f, 0f, 0f), new float3(0f, 0.012f, 0f), r * (1.5f + s.Curvature * 0.8f), r * (1.5f + s.Curvature * 0.8f), 12, g.Pal.Main);
                    g.Tube(new float3(0f, 0.01f, 0f), new float3(0f, g.Len * 0.5f, 0f), r, r * 0.85f, 10, g.Pal.Main);
                    g.Tube(new float3(0f, 0.012f, 0f), new float3(0f, 0.04f, 0f), r * 1.02f, r * 1.0f, 10, g.Pal.Second);
                    top = g.Len * 0.5f;
                    break;
                case 1: // helm with nasal and cheek guards
                    g.Ellipsoid(new float3(0f, r * 0.4f, 0f), new float3(r * 1.05f, r * 1.1f, r * 1.05f), g.Pal.Metal);
                    g.Box(new float3(0f, r * 0.2f, r * 0.95f), new float3(0.012f, r * 0.5f, 0.01f), g.Pal.Edge);
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(new float3(side * r * 0.85f, -r * 0.25f, r * 0.35f), new float3(0.006f, r * 0.4f, r * 0.35f), g.Pal.Metal, side * 0.4f);
                    top = r * 1.45f;
                    break;
                case 2: // hood or cap
                    g.Tube(new float3(0f, 0f, -0.02f), new float3(0f, g.Len * 0.8f, -r * s.Curvature), r * 1.05f, 0.01f, 8, g.Pal.Main);
                    g.Ring(new float3(0f, 0.01f, -0.02f), Up, r * 1.07f, 0.008f, 10, g.Pal.Second);
                    top = g.Len * 0.5f;
                    break;
                case 3: // circlet
                    g.Ring(new float3(0f, 0.03f, 0f), Up, r, 0.008f, 10, g.Pal.Metal);
                    top = 0.05f;
                    break;
                case 4: // wrapped turban
                    for (int k = 0; k < 4; k++)
                        g.Ellipsoid(new float3(0f, 0.02f + k * 0.03f, 0f), new float3(r * (1.12f - k * 0.1f), 0.028f, r * (1.08f - k * 0.1f)), k % 2 == 0 ? g.Pal.Main : g.Pal.Second);
                    top = 0.13f;
                    break;
                default: // conical hat
                    g.Tube(new float3(0f, 0f, 0f), new float3(0f, g.Len * (0.45f + s.Curvature * 0.3f), 0f), r * (1.4f + s.Curvature * 0.5f), 0.004f, 12, g.Pal.Main);
                    g.Ring(new float3(0f, 0.02f, 0f), Up, r * 1.1f, 0.006f, 10, g.Pal.Second);
                    top = g.Len * (0.45f + s.Curvature * 0.3f) * 0.6f;
                    break;
            }

            int bands = (int)math.round(s.MotifDensity * 2f);
            for (int k = 0; k < bands; k++)
                g.Tube(new float3(0f, 0.02f + k * 0.03f, 0f), new float3(0f, 0.035f + k * 0.03f, 0f), r * 1.02f, r * 1.02f, 10, g.Pal.Accent);

            // 主飾.
            var front = new float3(0f, math.min(top, 0.06f), r * 1.02f);
            switch (g.V(1))
            {
                case 1: // plume
                    g.Loft(new[]
                    {
                        Sec(new float3(r * 0.5f, top * 0.6f, 0f), new float3(0.004f, 0f, 0f), new float3(0f, 0f, 0.003f)),
                        Sec(new float3(r * 0.7f, top + 0.06f, -0.03f), new float3(0.022f, 0f, 0f), new float3(0f, 0f, 0.004f)),
                        Sec(new float3(r * 0.8f, top + 0.12f, -0.09f), new float3(0.003f, 0f, 0f), new float3(0f, 0f, 0.002f)),
                    }, g.Pal.Accent, notch: false);
                    break;
                case 2: g.Gem(front, Front, 0.016f, 0.014f, g.Pal.Accent); break;
                case 3: // horns
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float3 b = new float3(side * r * 0.7f, top * 0.7f, 0f);
                        float3 m = b + new float3(side * 0.05f, 0.04f, 0f);
                        g.Tube(b, m, 0.016f, 0.011f, 6, g.Pal.Edge);
                        g.Spike(m, m + new float3(side * 0.01f, 0.06f, -0.02f), 0.011f, 6, g.Pal.Edge);
                    }
                    break;
                case 4: // crest ridge
                    g.Box(new float3(0f, top + 0.02f, 0f), new float3(0.006f, 0.025f, r * 0.8f), g.Pal.Accent);
                    break;
                case 5: // motif badge
                    g.Tube(front - Front * 0.003f, front + Front * 0.004f, 0.024f, 0.024f, 8, g.Pal.Metal);
                    Engrave(g, new Plate(front + Front * 0.005f, Up, new float3(1f, 0f, 0f), Front, 0.018f, 0.018f), s.Motif.Region, 0, g.Pal.Accent);
                    break;
            }

            // 垂飾.
            switch (g.V(2))
            {
                case 1: // side tassels
                    for (int side = -1; side <= 1; side += 2)
                        g.Tube(new float3(side * r * 1.02f, 0.02f, 0f), new float3(side * r * 1.08f, -0.08f, 0f), 0.006f, 0.014f, 5, g.Pal.Accent);
                    break;
                case 2: // chin cords
                    g.Ring(new float3(0f, -0.04f, 0.02f), new float3(0f, 0.2f, 1f), r * 0.95f, 0.003f, 8, g.Pal.Wrap, math.PI * 0.05f, math.PI * 0.95f);
                    break;
                case 3: // back ribbons
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(new float3(side * 0.02f, -0.04f, -r * 1.02f), new float3(0.012f, 0.07f, 0.0015f), g.Pal.Accent, 0f, side * 0.18f);
                    break;
                case 4: // stepped neck curtain behind
                    for (int k = 0; k < 2; k++)
                        g.Box(new float3(0f, -0.015f - k * 0.035f, -r * (0.98f + k * 0.08f)), new float3(r * (0.75f + k * 0.1f), 0.022f, 0.003f),
                            k == 0 ? g.Pal.Main : g.Pal.Second, 0f, 0f);
                    break;
                case 5: // ear beads
                    for (int side = -1; side <= 1; side += 2)
                        for (int k = 0; k < 3; k++)
                            g.Ball(new float3(side * r * 1.0f, 0.0f - k * 0.02f, 0f), 0.007f, k == 2 ? g.Pal.Accent : g.Pal.Metal);
                    break;
            }
        }

        private static void BuildCharm(Kit g)
        {
            GearLookSpec s = g.Spec;
            // REQ: gear-looks.md#宙獸飾品 — a small charm: a hanger, a pendant below it, something dangling.
            float size = g.Len * 0.5f;
            var hang = new float3(0f, size * 0.25f, 0f);
            switch (g.V(0)) // 底型: the hanger
            {
                case 0: g.Ring(hang, Front, size * 0.25f, 0.003f, 8, g.Pal.Metal); break;
                case 1: // chain links
                    for (int k = 0; k < 3; k++)
                        g.Ring(hang + Up * (size * 0.5f - k * size * 0.22f), k % 2 == 0 ? Front : new float3(1f, 0f, 0f), size * 0.1f, 0.0022f, 6, g.Pal.Metal);
                    break;
                case 2: // cord knot
                    g.Tube(hang + Up * size * 0.6f, hang - Up * size * 0.1f, 0.0025f, 0.0025f, 4, g.Pal.Wrap);
                    g.Ball(hang, size * 0.1f, g.Pal.Wrap);
                    break;
                case 3: // clasp bar
                    g.Box(hang + Up * size * 0.1f, new float3(size * 0.28f, size * 0.05f, size * 0.05f), g.Pal.Metal);
                    break;
                case 4: // bead string
                    for (int k = 0; k < 5; k++)
                    {
                        float a = math.PI * (0.15f + k * 0.175f);
                        g.Ball(hang + new float3(math.cos(a) * size * 0.35f, math.sin(a) * size * 0.35f, 0f), size * 0.06f, k % 2 == 0 ? g.Pal.Accent : g.Pal.Main);
                    }
                    break;
                default: // open torc
                    g.Ring(hang + Up * size * 0.1f, Front, size * 0.4f, 0.004f, 10, g.Pal.Metal, 0.5f, 2f * math.PI - 0.5f);
                    break;
            }

            float3 c = new float3(0f, -size * 0.45f, 0f);
            switch (g.V(1)) // 主飾: the pendant
            {
                case 0: g.Ball(c, size * 0.42f, g.Pal.Accent); break;
                case 1: g.Spike(c + new float3(0f, size * 0.4f, 0f), c - new float3(0f, size * 0.6f, 0f), size * 0.35f, 6, g.Pal.Accent); break;
                case 2: g.Box(c, new float3(size * 0.32f, size * 0.42f, 0.004f), g.Pal.Main, 0f, math.PI / 4f); break;
                case 3: // medallion carrying the region motif
                    g.Tube(c + new float3(0f, 0f, -0.004f), c + new float3(0f, 0f, 0.004f), size * 0.4f, size * 0.4f, 8, g.Pal.Metal);
                    Engrave(g, new Plate(c + Front * 0.005f, Up, new float3(1f, 0f, 0f), Front, size * 0.3f, size * 0.3f), s.Motif.Region, g.V(2) & 1, g.Pal.Accent);
                    break;
                case 4: // crescent
                    for (int k = 0; k < 6; k++)
                    {
                        float a0 = math.lerp(-2.4f, 2.4f, k / 6f), a1 = math.lerp(-2.4f, 2.4f, (k + 1) / 6f);
                        g.Tube(c + new float3(math.sin(a0), -math.cos(a0), 0f) * size * 0.38f, c + new float3(math.sin(a1), -math.cos(a1), 0f) * size * 0.38f,
                            size * 0.1f * math.cos(a0 * 0.5f) + 0.002f, size * 0.1f * math.cos(a1 * 0.5f) + 0.002f, 5, g.Pal.Accent);
                    }
                    break;
                default: // star
                    g.Ball(c, size * 0.16f, g.Pal.Accent);
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k * 2f * math.PI / 5f;
                        g.Spike(c, c + new float3(math.sin(a), math.cos(a), 0f) * size * 0.45f, size * 0.1f, 4, g.Pal.Accent);
                    }
                    break;
            }
            if (s.MotifDensity > 0.5f) g.Ball(c + new float3(0f, 0f, 0.006f), size * 0.14f, g.Pal.Main);

            var tip = c - Up * size * 0.45f;
            switch (g.V(2)) // 垂飾
            {
                case 1: g.Tube(tip, tip - Up * size * 0.5f, size * 0.05f, size * 0.14f, 5, g.Pal.Main); break;
                case 2:
                    for (int k = 0; k < 3; k++) g.Ball(tip - Up * size * (0.12f + k * 0.14f), size * 0.06f, k == 1 ? g.Pal.Main : g.Pal.Metal);
                    break;
                case 3: g.Box(tip - Up * size * 0.35f, new float3(size * 0.08f, size * 0.32f, 0.0012f), g.Pal.Second, 0f, 0.2f); break;
                case 4: // bell
                    g.Tube(tip - Up * size * 0.05f, tip - Up * size * 0.3f, size * 0.05f, size * 0.16f, 8, g.Pal.Metal);
                    g.Ball(tip - Up * size * 0.32f, size * 0.04f, g.Pal.Edge);
                    break;
                case 5: // twin chains
                    for (int side = -1; side <= 1; side += 2)
                        g.Tube(tip + new float3(side * size * 0.1f, 0f, 0f), tip + new float3(side * size * 0.2f, -size * 0.5f, 0f), 0.0018f, 0.0018f, 4, g.Pal.Metal);
                    break;
            }
        }
    }
}
