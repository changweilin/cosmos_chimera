using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    // Weapons. Slot indices follow gear-looks.md#部件槽 in table order for the kind's family (GearLooks.FamilyOf).
    public static partial class GearMeshes
    {
        private static readonly float3 Up = new float3(0f, 1f, 0f);
        private static readonly float3 Front = new float3(0f, 0f, 1f);

        // ---------------------------------------------------------------- Blade family: 刃身, 護手, 握柄, 柄頭, 刃面紋樣

        private static void BuildSword(Kit g)
        {
            GearLookSpec s = g.Spec;
            bool great = s.Kind == GearKind.Greatsword, dagger = s.Kind == GearKind.Dagger;
            float grip = great ? 0.3f : dagger ? 0.1f : 0.16f;
            float bladeW = (great ? 0.065f : dagger ? 0.028f : 0.04f) * s.Width;
            float gr = great ? 0.02f : 0.016f;
            Grip(g, g.V(2), 0f, grip, gr);
            Pommel(g, g.V(3), new float3(0f, -0.025f, 0f), great ? 0.034f : 0.028f);
            Guard(g, g.V(1), grip + 0.012f, bladeW * 2.6f + 0.04f);
            BladeAlong(g, new float3(0f, grip + 0.03f, 0f), Up, g.Len - grip - 0.03f, bladeW, g.V(0), s.Curvature, g.V(4));
            EraFlourish(g, new float3(0f, grip + 0.024f, 0f), bladeW * 0.45f, 0.02f);
        }

        private static void BuildTwinblade(Kit g)
        {
            GearLookSpec s = g.Spec;
            float half = g.Len * 0.5f;
            float haft = half * 0.45f;
            Grip(g, g.V(2), -haft, haft, 0.018f);
            float w = 0.035f * s.Width;
            for (int end = -1; end <= 1; end += 2)
            {
                float y = end * haft;
                Guard(g, g.V(1), y, w * 2.4f);
                // Only the upper blade carries the face treatment: both would double the triangle count for one look.
                BladeAlong(g, new float3(0f, y, 0f), new float3(0f, end, 0f), half - haft, w, g.V(0), s.Curvature * end, end > 0 ? g.V(4) : 0);
            }
            // The centre grip ornament takes the pommel slot: the twinblade has no butt.
            Pommel(g, g.V(3), new float3(0f, 0f, 0.022f), 0.02f);
            EraFlourish(g, new float3(0f, haft * 0.5f, 0f), 0.022f, 0.06f);
        }

        private static void BuildAxe(Kit g)
        {
            GearLookSpec s = g.Spec;
            float len = Broken(g, g.Len);
            const float r = 0.017f;
            Haft(g, 0, -0.04f, len, r);
            Grip(g, g.V(2), 0f, 0.2f, r);
            Pommel(g, g.V(3), new float3(0f, -0.05f, 0f), r * 1.5f);
            if (len < g.Len * 0.9f) return; // the head broke off with the haft
            float headY = len - 0.11f * s.Width;
            AxeHead(g, g.V(0), headY, s.Width, r, g.V(4));
            Socket(g, g.V(1), headY, r, 0.07f * s.Width);
            EraFlourish(g, new float3(0f, headY - 0.09f, 0f), r * 1.4f, 0.04f);
        }

        // (reach, bite, beard drop, back): back 0 none, 1 hammer poll, 2 second bit, 3 spike.
        private static readonly (float reach, float bite, float beard, int back)[] AxeShapes =
        {
            (0.16f, 0.13f, 0.06f, 0), // bearded
            (0.15f, 0.2f, 0.02f, 0),  // broad crescent
            (0.12f, 0.08f, 0f, 1),    // hatchet with poll
            (0.15f, 0.13f, 0f, 2),    // double bit
            (0.19f, 0.16f, 0.11f, 0), // hooked long beard
            (0.14f, 0.09f, 0f, 3),    // tomahawk with back spike
        };

        private static void AxeHead(Kit g, int shape, float headY, float size, float haftR, int face)
        {
            GearLookSpec s = g.Spec;
            (float reachN, float biteN, float beardN, int back) = AxeShapes[shape];
            float reach = reachN * size, bite = biteN * size * (1f + s.Curvature * 0.6f), beard = beardN * size;
            float thick = 0.02f * size * s.Thickness;
            for (int side = 1; side >= (back == 2 ? -1 : 1); side -= 2)
            {
                float rr = side > 0 ? reach : reach * 0.75f;
                float bb = side > 0 ? bite : bite * 0.75f;
                g.Loft(new[]
                {
                    Sec(new float3(0f, headY, 0f), new float3(0f, 0.06f * size, 0f), new float3(0f, 0f, thick)),
                    Sec(new float3(side * rr * 0.55f, headY - beard * 0.3f, 0f), new float3(0f, 0.045f * size, 0f), new float3(0f, 0f, thick * 0.6f)),
                    Sec(new float3(side * rr, headY - beard, 0f), new float3(0f, bb, 0f), new float3(0f, 0f, 0.002f)),
                }, g.Pal.Metal, notch: true);
                // The honed bit reads as a bright band along the cutting edge.
                g.Bar(new float3(side * rr, headY - beard - bb * 0.95f, 0f), new float3(side * rr, headY - beard + bb * 0.95f, 0f),
                    0.006f * size, 0.0025f, new float3(side, 0f, 0f), g.Pal.Edge);
            }
            if (back == 1) g.Box(new float3(-0.035f * size, headY, 0f), new float3(0.03f * size, 0.028f * size, 0.024f * size), g.Pal.Metal);
            else if (back == 3) g.Spike(new float3(-0.01f, headY, 0f), new float3(-0.1f * size, headY + 0.02f * size, 0f), 0.018f * size, 5, g.Pal.Metal);

            var cheek = new Plate(new float3(reach * 0.5f, headY - beard * 0.4f, thick * 0.65f), new float3(1f, 0f, 0f), Up, Front, reach * 0.28f, bite * 0.45f);
            switch (face)
            {
                case 1: Engrave(g, cheek, s.Motif.Region, 0, g.Pal.Accent); break;
                case 2: Engrave(g, cheek, s.Motif.Region, 1, g.Pal.Accent); break;
                case 3: // pierced eye
                    g.Ring(cheek.O, Front, bite * 0.25f, 0.004f, 8, Mul(g.Pal.Metal, 0.6f));
                    g.Tube(cheek.O - Front * thick, cheek.O + Front * 0.002f, bite * 0.22f, bite * 0.22f, 8, Soot);
                    break;
                case 4: // inlay band across the cheek
                    g.Bar(cheek.O - Up * bite * 0.6f, cheek.O + Up * bite * 0.6f, reach * 0.06f, 0.002f, Front, g.Pal.Accent);
                    break;
                case 5:
                    Engrave(g, cheek, s.Motif.Region, 0, g.Pal.Accent);
                    Engrave(g, new Plate(cheek.O - Front * thick * 1.3f, cheek.U, cheek.V, -Front, cheek.HalfU, cheek.HalfV), s.Motif.Region, 1, g.Pal.Accent);
                    break;
            }
            Cracks(g, cheek);
        }

        // Where the head meets the haft (the axe's 護手 slot).
        private static void Socket(Kit g, int variant, float y, float r, float h)
        {
            switch (variant)
            {
                case 0:
                    g.Box(new float3(0f, y, 0f), new float3(r * 1.6f, h * 0.55f, r * 1.6f), g.Pal.Metal);
                    break;
                case 1: // langets down the haft
                    g.Box(new float3(0f, y, 0f), new float3(r * 1.5f, h * 0.5f, r * 1.5f), g.Pal.Metal);
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(new float3(0f, y - h * 1.4f, side * r * 1.05f), new float3(r * 0.5f, h, 0.002f), g.Pal.Metal);
                    break;
                case 2: // double collar
                    for (int k = -1; k <= 1; k += 2)
                        g.Tube(new float3(0f, y + k * h * 0.6f - 0.006f, 0f), new float3(0f, y + k * h * 0.6f + 0.006f, 0f), r * 1.5f, r * 1.5f, g.Sides, g.Pal.Accent);
                    break;
                case 3: // rivet row
                    g.Box(new float3(0f, y, 0f), new float3(r * 1.5f, h * 0.5f, r * 1.5f), g.Pal.Metal);
                    for (int k = -1; k <= 1; k++) g.Ball(new float3(0f, y + k * h * 0.3f, r * 1.55f), r * 0.3f, g.Pal.Edge);
                    break;
                case 4: // cord-wrapped socket
                    g.Tube(new float3(0f, y - h * 0.8f, 0f), new float3(0f, y + h * 0.6f, 0f), r * 1.4f, r * 1.3f, g.Sides, g.Pal.Wrap);
                    Lashing(g, y - h * 0.2f, r * 1.3f);
                    break;
                default: // finial cap
                    g.Box(new float3(0f, y, 0f), new float3(r * 1.5f, h * 0.5f, r * 1.5f), g.Pal.Metal);
                    g.Spike(new float3(0f, y + h * 0.5f, 0f), new float3(0f, y + h * 1.6f, 0f), r * 1.1f, 6, g.Pal.Metal);
                    break;
            }
        }

        private static void BuildSpear(Kit g)
        {
            GearLookSpec s = g.Spec;
            float len = Broken(g, g.Len);
            const float r = 0.016f;
            float headLen = 0.3f * s.Width;
            // Polearms are gripped near the middle: the origin is the hand, so the butt hangs below it.
            float butt = -g.Len * 0.35f;
            float top = g.Len + butt;
            float socket = top - headLen;
            Haft(g, 0, butt, math.min(socket, len + butt), r);
            Grip(g, g.V(2), -0.1f, 0.12f, r);
            Pommel(g, g.V(3), new float3(0f, butt, 0f), r * 1.3f);
            if (len < g.Len * 0.9f) return;
            g.Tube(new float3(0f, socket - 0.06f, 0f), new float3(0f, socket + 0.01f, 0f), r * 1.2f, r * 1.05f, g.Sides, g.Pal.Metal);
            SpearHead(g, g.V(0), socket, headLen, 0.035f * s.Width, g.V(4));
            SpearDeco(g, g.V(1), socket - 0.06f, r);
            EraFlourish(g, new float3(0f, socket - 0.12f, 0f), r * 1.3f, 0.05f);
        }

        private static void SpearHead(Kit g, int shape, float socket, float headLen, float w, int face)
        {
            GearLookSpec s = g.Spec;
            var root = new float3(0f, socket, 0f);
            switch (shape)
            {
                case 0: // leaf
                    BladeAlong(g, root, Up, headLen, w, 0, 0f, face);
                    break;
                case 1: // long spike
                    BladeAlong(g, root, Up, headLen * 1.25f, w * 0.55f, 1, 0f, face);
                    break;
                case 2: // winged: lugs under a broad head
                    BladeAlong(g, root + Up * 0.03f, Up, headLen * 0.9f, w * 1.2f, 0, 0f, face);
                    for (int side = -1; side <= 1; side += 2)
                        g.Spike(root + Up * 0.03f, root + new float3(side * w * 2.2f, -0.02f, 0f), 0.008f, 4, g.Pal.Metal);
                    break;
                case 3: // halberd: point, axe blade and back hook
                    BladeAlong(g, root, Up, headLen, w * 0.6f, 1, 0f, face);
                    g.Loft(new[]
                    {
                        Sec(root + new float3(0f, 0.04f, 0f), new float3(0f, 0.05f, 0f), new float3(0f, 0f, 0.012f)),
                        Sec(root + new float3(0.16f * s.Width, 0.06f, 0f), new float3(0f, 0.09f * (1f + s.Curvature), 0f), new float3(0f, 0f, 0.002f)),
                    }, g.Pal.Metal, notch: true);
                    g.Spike(root + new float3(0f, 0.06f, 0f), root + new float3(-0.1f * s.Width, 0.02f, 0f), 0.012f, 4, g.Pal.Metal);
                    break;
                case 4: // trident
                    g.Box(root + Up * 0.02f, new float3(w * 2.2f, 0.01f, 0.01f), g.Pal.Metal);
                    for (int k = -1; k <= 1; k++)
                    {
                        float3 b = root + new float3(k * w * 2f, 0.02f, 0f);
                        BladeAlong(g, b, Up, headLen * (k == 0 ? 1f : 0.75f), w * 0.4f, 1, -k * s.Curvature * 0.3f, k == 0 ? face : 0);
                    }
                    break;
                default: // glaive: a long single-edged curved blade
                    BladeAlong(g, root, Up, headLen * 1.4f, w * 1.1f, 5, 0.6f + s.Curvature * 0.6f, face);
                    break;
            }
        }

        // Below the head (the spear's 護手 slot).
        private static void SpearDeco(Kit g, int variant, float y, float r)
        {
            switch (variant)
            {
                case 1: // crossbar lugs
                    g.Box(new float3(0f, y, 0f), new float3(r * 4f, r * 0.5f, r * 0.6f), g.Pal.Metal);
                    break;
                case 2: // horsehair tassel
                    g.Tube(new float3(0f, y, 0f), new float3(0f, y - 0.16f, 0f), r * 1.4f, r * 3.2f, 8, g.Pal.Accent);
                    break;
                case 3: // paired collars
                    for (int k = 0; k < 2; k++)
                        g.Tube(new float3(0f, y - 0.03f * k - 0.005f, 0f), new float3(0f, y - 0.03f * k + 0.005f, 0f), r * 1.4f, r * 1.4f, g.Sides, g.Pal.Accent);
                    break;
                case 4: // pennant
                    g.Box(new float3(0.07f, y - 0.05f, 0f), new float3(0.065f, 0.04f, 0.0015f), g.Pal.Main, 0f, -0.08f);
                    g.Spike(new float3(0.135f, y - 0.05f, 0f), new float3(0.19f, y - 0.07f, 0f), 0.03f, 3, g.Pal.Main);
                    break;
                case 5: // feather fan
                    for (int k = -1; k <= 1; k++)
                        g.Box(new float3(k * 0.015f, y - 0.06f, 0f), new float3(0.012f, 0.05f, 0.0015f), g.Pal.Accent, 0f, k * 0.35f);
                    break;
            }
        }

        // ---------------------------------------------------------------- Blunt family: 頭部, 柄, 握把纏繩, 配重

        private static void BuildBlunt(Kit g)
        {
            GearLookSpec s = g.Spec;
            bool two = s.Kind == GearKind.GreataxeHammer;
            float len = Broken(g, g.Len);
            float r = two ? 0.022f : 0.017f;
            Haft(g, g.V(1), -0.04f, len, r);
            Grip(g, g.V(2), 0f, two ? 0.45f : 0.22f, r);
            Pommel(g, g.V(3), new float3(0f, -0.05f, 0f), r * 1.5f);
            if (len < g.Len * 0.9f) return;
            float size = (two ? 1.5f : 1f) * s.Width;
            float headY = len - (two ? 0.16f : 0.11f) * s.Width;
            int head = g.V(0);
            if (two && head < 3)
            {
                // REQ: gear-looks.md#部件槽 — the greataxe-hammer type draws as a greataxe for the first half of its head shapes.
                AxeHead(g, head == 0 ? 3 : head == 1 ? 1 : 4, headY, size, r, head + 1);
                Socket(g, 0, headY, r, 0.07f * size);
            }
            else
            {
                BluntHead(g, two ? head - 3 + 6 : head, headY, size);
            }
            EraFlourish(g, new float3(0f, headY - 0.06f * size, 0f), r * 1.4f, 0.04f);
        }

        // Heads 0–5 are mace heads, 6–8 the great hammer heads.
        private static void BluntHead(Kit g, int head, float headY, float size)
        {
            float hr = 0.06f * size;
            Color m = g.Pal.Metal;
            var c = new float3(0f, headY + hr * 0.4f, 0f);
            switch (head)
            {
                case 0:
                    g.Ball(c, hr, m);
                    break;
                case 1: // box hammer
                    g.Box(c, new float3(hr * 1.9f, hr, hr), m);
                    FaceMotif(g, c + Front * hr, new float3(1f, 0f, 0f), hr * 1.6f, hr * 0.8f);
                    break;
                case 2: // flanged
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * math.PI / 3f;
                        g.Box(new float3(math.cos(a) * hr * 0.6f, c.y, math.sin(a) * hr * 0.6f), new float3(hr * 0.5f, hr * 1.1f, 0.006f), m, a);
                    }
                    break;
                case 3: // cylinder with bands
                    g.Tube(c - Up * hr, c + Up * hr * 0.8f, hr, hr * 0.8f, 8, m);
                    g.Tube(c - Up * hr * 0.2f, c, hr * 1.06f, hr * 1.06f, 8, g.Pal.Accent);
                    break;
                case 4: // morningstar
                    g.Ball(c, hr * 0.85f, m);
                    for (int k = 0; k < 10; k++)
                    {
                        float a = k * 2.399963f; // golden angle spreads the spikes evenly
                        float yy = 1f - 2f * (k + 0.5f) / 10f;
                        float rad = math.sqrt(1f - yy * yy);
                        var d = new float3(math.cos(a) * rad, yy, math.sin(a) * rad);
                        g.Spike(c + d * hr * 0.75f, c + d * hr * 1.45f, hr * 0.18f, 4, g.Pal.Edge);
                    }
                    break;
                case 5: // crowned
                    g.Tube(c - Up * hr * 0.6f, c + Up * hr * 0.6f, hr * 0.8f, hr, 8, m);
                    for (int k = 0; k < 6; k++)
                        g.Spike(c + Up * hr * 0.6f + Dir(k * math.PI / 3f) * hr * 0.8f, c + Up * hr * 1.1f + Dir(k * math.PI / 3f) * hr * 0.9f, hr * 0.15f, 4, m);
                    g.Gem(c + Up * hr * 0.8f, Up, hr * 0.3f, hr * 0.5f, g.Pal.Accent);
                    break;
                case 6: // block great hammer
                    g.Box(c, new float3(hr * 1.6f, hr * 0.9f, hr * 0.9f), m);
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(c + new float3(side * hr * 1.65f, 0f, 0f), new float3(hr * 0.1f, hr * 0.95f, hr * 0.95f), g.Pal.Edge);
                    FaceMotif(g, c + Front * hr * 0.9f, new float3(1f, 0f, 0f), hr * 1.3f, hr * 0.7f);
                    break;
                case 7: // war pick: hammer face and a back spike
                    g.Box(c + new float3(hr * 0.6f, 0f, 0f), new float3(hr * 0.9f, hr * 0.7f, hr * 0.7f), m);
                    g.Spike(c - new float3(hr * 0.2f, 0f, 0f), c - new float3(hr * 2.4f, hr * 0.6f, 0f), hr * 0.55f, 5, m);
                    break;
                default: // round maul ringed with bands
                    g.Tube(c - new float3(hr * 1.5f, 0f, 0f), c + new float3(hr * 1.5f, 0f, 0f), hr * 0.95f, hr * 0.95f, 10, m);
                    for (int k = -1; k <= 1; k++)
                        g.Tube(c + new float3(k * hr * 1f - 0.008f, 0f, 0f), c + new float3(k * hr * 1f + 0.008f, 0f, 0f), hr * 1.03f, hr * 1.03f, 10, g.Pal.Accent);
                    break;
            }
        }

        // Region motif on a hammer face, only on gear ornate enough to carry one.
        private static void FaceMotif(Kit g, float3 at, float3 along, float hu, float hv)
        {
            if (g.Spec.MotifDensity < 0.25f) return;
            Engrave(g, new Plate(at, along, Up, Front, hu * 0.8f, hv * 0.8f), g.Spec.Motif.Region, g.V(0) & 1, g.Pal.Accent);
        }

        // REQ: gear-looks.md#部件槽 (柄).
        private static void Haft(Kit g, int variant, float y0, float y1, float r)
        {
            Color wood = g.Pal.Second;
            float len = y1 - y0;
            switch (variant)
            {
                case 0:
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r * 1.1f, r * 0.9f, g.Sides, wood);
                    break;
                case 1: // tapered toward the head
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r * 1.3f, r * 0.7f, g.Sides, wood);
                    break;
                case 2: // knotted branch
                    for (int k = 0; k < 4; k++)
                    {
                        float a = y0 + len * k / 4f, b = y0 + len * (k + 1) / 4f;
                        float3 off = new float3((g.MotifRng.NextFloat() - 0.5f) * r * 0.6f, 0f, (g.MotifRng.NextFloat() - 0.5f) * r * 0.6f);
                        g.Tube(new float3(0f, a, 0f) + off * 0.5f, new float3(0f, b, 0f) + off, r, r * 0.95f, g.Sides, wood);
                        if (k > 0) g.Ball(new float3(0f, a, 0f) + off * 0.5f, r * 1.3f, Mul(wood, 0.85f));
                    }
                    break;
                case 3: // iron-banded
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r, r, g.Sides, wood);
                    for (int k = 1; k < 4; k++)
                    {
                        float y = y0 + len * k / 4f;
                        g.Tube(new float3(0f, y - 0.008f, 0f), new float3(0f, y + 0.008f, 0f), r * 1.2f, r * 1.2f, g.Sides, g.Pal.Metal);
                    }
                    break;
                case 4: // squared and chamfered
                    g.Box(new float3(0f, (y0 + y1) * 0.5f, 0f), new float3(r * 0.85f, len * 0.5f, r * 0.85f), wood, math.PI / 4f);
                    break;
                default: // all-metal shaft
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r * 0.85f, r * 0.85f, g.Sides, g.Pal.Metal);
                    g.Tube(new float3(0f, y1 - len * 0.3f, 0f), new float3(0f, y1, 0f), r * 0.95f, r * 0.95f, g.Sides, Mul(g.Pal.Metal, 0.85f));
                    break;
            }
        }

        // ---------------------------------------------------------------- Thrown family: 刃身, 握段, 尾羽／穗

        private static void BuildJavelin(Kit g)
        {
            GearLookSpec s = g.Spec;
            const float r = 0.011f;
            float headLen = 0.16f * s.Width;
            float butt = -g.Len * 0.45f;
            float top = g.Len + butt;
            float socket = top - headLen;
            float len = Broken(g, g.Len);
            g.Tube(new float3(0f, butt, 0f), new float3(0f, math.min(socket, len + butt), 0f), r, r * 0.9f, g.Sides, g.Pal.Second);
            Grip(g, g.V(1), -0.1f, 0.1f, r);
            Fletching(g, g.V(2), butt, r);
            if (len < g.Len * 0.9f) return;
            g.Tube(new float3(0f, socket - 0.04f, 0f), new float3(0f, socket + 0.01f, 0f), r * 1.25f, r * 1.05f, g.Sides, g.Pal.Metal);
            var root = new float3(0f, socket, 0f);
            float w = 0.022f * s.Width;
            switch (g.V(0))
            {
                case 0: BladeAlong(g, root, Up, headLen, w, 0, 0f, 0); break;
                case 1: BladeAlong(g, root, Up, headLen * 1.3f, w * 0.5f, 1, 0f, 0); break;
                case 2: // barbed
                    BladeAlong(g, root, Up, headLen, w, 1, 0f, 0);
                    for (int side = -1; side <= 1; side += 2)
                        g.Spike(root + Up * headLen * 0.25f, root + new float3(side * w * 1.6f, -0.02f, 0f), 0.005f, 4, g.Pal.Metal);
                    break;
                case 3: // fork
                    for (int side = -1; side <= 1; side += 2)
                        BladeAlong(g, root + new float3(side * w * 0.8f, 0f, 0f), Up, headLen * 0.9f, w * 0.4f, 1, -side * 0.2f, 0);
                    break;
                case 4: // square bodkin
                    g.Spike(root, root + Up * headLen * 1.1f, w * 0.7f, 4, g.Pal.Metal);
                    break;
                default: // finned dart
                    BladeAlong(g, root, Up, headLen, w * 0.8f, 3, 0f, 1);
                    g.Box(root + Up * headLen * 0.3f, new float3(0.002f, headLen * 0.25f, w * 1.1f), g.Pal.Edge);
                    break;
            }
        }

        private static void Fletching(Kit g, int variant, float y, float r)
        {
            switch (variant)
            {
                case 0:
                case 1: // three or four fins
                {
                    int fins = variant == 0 ? 3 : 4;
                    for (int k = 0; k < fins; k++)
                        g.Box(new float3(0f, y + 0.06f, 0f) + Dir(k * 2f * math.PI / fins) * 0.02f,
                            new float3(0.02f, 0.06f, 0.002f), g.Pal.Accent, k * 2f * math.PI / fins);
                    break;
                }
                case 2: // tassel
                    g.Tube(new float3(0f, y + 0.02f, 0f), new float3(0f, y - 0.1f, 0f), r * 1.4f, r * 3f, 6, g.Pal.Accent);
                    break;
                case 3: // ribbons
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(new float3(side * 0.02f, y - 0.05f, 0f), new float3(0.006f, 0.07f, 0.0015f), g.Pal.Main, 0f, side * 0.25f);
                    break;
                case 4: // feather pair
                    for (int side = -1; side <= 1; side += 2)
                        g.Loft(new[]
                        {
                            Sec(new float3(0f, y + 0.02f, 0f), new float3(0.002f, 0f, 0f), new float3(0f, 0f, 0.002f)),
                            Sec(new float3(side * 0.025f, y + 0.08f, 0f), new float3(0.012f, 0f, 0f), new float3(0f, 0f, 0.002f)),
                            Sec(new float3(side * 0.02f, y + 0.14f, 0f), new float3(0.001f, 0f, 0f), new float3(0f, 0f, 0.001f)),
                        }, g.Pal.Accent, notch: false);
                    break;
                default: // ring weights
                    for (int k = 0; k < 3; k++)
                        g.Tube(new float3(0f, y + 0.02f + k * 0.025f, 0f), new float3(0f, y + 0.032f + k * 0.025f, 0f), r * 1.6f, r * 1.6f, g.Sides, g.Pal.Metal);
                    break;
            }
        }

        private static void BuildThrowingBlade(Kit g)
        {
            GearLookSpec s = g.Spec;
            float r = g.Len * 0.5f;
            int body = g.V(0);
            if (body <= 2) // three-, four- or five-point star
            {
                int points = 3 + body;
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
                    }, g.Pal.Metal, notch: true, edge: g.Pal.Edge);
                }
            }
            else if (body == 3) // chakram ring
            {
                g.Ring(float3.zero, Front, r * 0.78f, r * 0.08f, 12, g.Pal.Metal);
                g.Ring(float3.zero, Front, r * 0.92f, r * 0.03f, 12, g.Pal.Edge);
            }
            else if (body == 4) // kunai: a single leaf blade on a ringed handle
            {
                BladeAlong(g, new float3(0f, -r * 0.2f, 0f), Up, r * 1.1f, r * 0.2f, 0, 0f, 1);
                g.Tube(new float3(0f, -r * 0.85f, 0f), new float3(0f, -r * 0.2f, 0f), r * 0.07f, r * 0.08f, 6, g.Pal.Wrap);
                g.Ring(new float3(0f, -r * 0.98f, 0f), Front, r * 0.13f, r * 0.03f, 8, g.Pal.Metal);
            }
            else // crescent
            {
                var arc = new List<(float3, float3, float3)>();
                for (int i = 0; i <= 8; i++)
                {
                    float a = math.lerp(0.25f, math.PI - 0.25f, i / 8f);
                    float w = math.sin(i / 8f * math.PI) * r * 0.22f + 0.002f;
                    var d = new float3(math.cos(a), math.sin(a) - 0.4f, 0f);
                    arc.Add(Sec(d * r * 0.95f, new float3(math.cos(a), math.sin(a), 0f) * w, new float3(0f, 0f, 0.004f)));
                }
                g.Loft(arc.ToArray(), g.Pal.Metal, notch: true, edge: g.Pal.Edge);
            }

            // 握段: the hub.
            switch (g.V(1))
            {
                case 0: g.Tube(new float3(0f, 0f, -0.006f), new float3(0f, 0f, 0.006f), r * 0.22f, r * 0.22f, 8, g.Pal.Second); break;
                case 1: g.Ring(float3.zero, Front, r * 0.16f, r * 0.04f, 8, g.Pal.Metal); break;
                case 2: g.Gem(new float3(0f, 0f, 0.004f), Front, r * 0.14f, r * 0.1f, g.Pal.Accent); break;
                case 3: g.Tube(new float3(0f, 0f, -0.007f), new float3(0f, 0f, 0.007f), r * 0.18f, r * 0.18f, 6, g.Pal.Wrap); break;
                case 4: g.Tube(new float3(0f, 0f, -0.006f), new float3(0f, 0f, 0.006f), r * 0.2f, r * 0.2f, 6, g.Pal.Edge); break;
                default:
                    Engrave(g, new Plate(new float3(0f, 0f, 0.005f), Up, new float3(1f, 0f, 0f), Front, r * 0.25f, r * 0.25f), s.Motif.Region, 0, g.Pal.Accent);
                    break;
            }

            // 尾羽／穗: something trailing from the hub or ring.
            var tie = new float3(0f, -r * 0.4f, 0f);
            switch (g.V(2))
            {
                case 1: g.Tube(tie, tie + new float3(0.01f, -r * 0.5f, 0f), r * 0.04f, r * 0.12f, 5, g.Pal.Accent); break;
                case 2:
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(tie + new float3(side * r * 0.08f, -r * 0.25f, 0f), new float3(r * 0.05f, r * 0.22f, 0.0012f), g.Pal.Accent, 0f, side * 0.3f);
                    break;
                case 3:
                    for (int k = 0; k < 3; k++) g.Ball(tie - Up * (r * 0.12f * (k + 1)), r * 0.05f, k == 1 ? g.Pal.Accent : g.Pal.Metal);
                    break;
                case 4: g.Ring(tie - Up * r * 0.12f, Front, r * 0.1f, r * 0.015f, 6, g.Pal.Wrap); break;
                case 5: g.Box(tie + new float3(r * 0.06f, -r * 0.4f, 0f), new float3(r * 0.04f, r * 0.38f, 0.0012f), g.Pal.Main, 0f, 0.2f); break;
            }
        }

        // ---------------------------------------------------------------- Bow family: 弓臂, 握把, 弦／機括, 箭槽

        private static void BuildBow(Kit g)
        {
            GearLookSpec s = g.Spec;
            float half = g.Len * 0.5f;
            int limb = g.V(0);
            float recurve = limb == 1 || limb == 4 ? 0.08f + s.Curvature * 0.14f : s.Curvature * 0.05f;
            const int segs = 8;
            var tips = new float3[2];
            for (int end = -1; end <= 1; end += 2)
            {
                // The asymmetric bow grips below centre: its upper limb is longer.
                float reach = limb == 3 ? half * (end > 0 ? 1.15f : 0.8f) : half;
                var sections = new List<(float3, float3, float3)>();
                for (int i = 0; i <= segs; i++)
                {
                    float t = (float)i / segs;
                    float y = end * (0.06f + t * (reach - 0.06f));
                    // Limbs bow away from the string; a recurve flips back at the tips; reflex-deflex snakes.
                    float z = -math.sin(t * math.PI * 0.9f) * reach * 0.18f + math.max(0f, t - 0.72f) * recurve * 4f * reach * 0.2f;
                    if (limb == 2) z += math.sin(t * math.PI * 2f) * reach * 0.05f;
                    float w = math.lerp(0.03f, 0.011f, t) * s.Width; // wide enough to read as a bow at play distance
                    sections.Add(Sec(new float3(0f, y, z), new float3(w, 0f, 0f), new float3(0f, 0f, w * 0.6f * s.Thickness)));
                }
                Color limbCol = (end > 0) == (s.PartVariants[1] % 2 == 0) ? g.Pal.Main : g.Pal.Second;
                if (limb == 5) // laminated: alternating strips
                    for (int i = 0; i < segs; i++)
                        g.Loft(new[] { sections[i], sections[i + 1] }, i % 2 == 0 ? g.Pal.Main : g.Pal.Second, notch: false);
                else
                    g.Loft(sections.ToArray(), limbCol, notch: false);
                tips[(end + 1) / 2] = sections[segs].Item1;
                if (limb == 4) // horn siyah and tip caps
                {
                    float3 tip = sections[segs].Item1;
                    g.Tube(tip, tip + new float3(0f, end * 0.06f, 0.05f), 0.007f, 0.004f, 5, g.Pal.Accent);
                    tips[(end + 1) / 2] = tip + new float3(0f, end * 0.06f, 0.05f);
                }
            }

            BowGrip(g, g.V(1));
            BowString(g, g.V(2), tips[0], tips[1]);
            float stringZ = (tips[0].z + tips[1].z) * 0.5f;
            // The repeater's magazine is what makes it a repeater; its 箭槽 slot adds to it instead of replacing it.
            if (s.Kind == GearKind.RepeaterBow) BowRest(g, 3, stringZ, half);
            if (s.Kind == GearKind.RepeaterBow && g.V(3) == 3)
                g.Box(new float3(0f, -0.1f, 0.05f), new float3(0.02f, 0.05f, 0.03f), g.Pal.Metal); // twin drum
            else
                BowRest(g, g.V(3), stringZ, half);
            EraFlourish(g, new float3(0f, 0.08f, 0f), 0.02f, 0.03f);
        }

        private static void BowGrip(Kit g, int variant)
        {
            switch (variant)
            {
                case 0: Grip(g, 0, -0.07f, 0.07f, 0.018f); break;
                case 1: // carved riser with a motif panel
                    g.Box(new float3(0f, 0f, 0.004f), new float3(0.018f, 0.1f, 0.02f), g.Pal.Second);
                    Engrave(g, new Plate(new float3(0f, 0.04f, 0.024f), Up, new float3(1f, 0f, 0f), Front, 0.05f, 0.014f), g.Spec.Motif.Region, 0, g.Pal.Accent);
                    Grip(g, 4, -0.06f, 0.0f, 0.018f);
                    break;
                case 2: // arrow shelf
                    Grip(g, 1, -0.07f, 0.05f, 0.018f);
                    g.Box(new float3(0.016f, 0.06f, 0.008f), new float3(0.008f, 0.004f, 0.016f), g.Pal.Edge);
                    break;
                case 3: // machined metal riser
                    g.Box(new float3(0f, 0f, 0f), new float3(0.016f, 0.12f, 0.026f), g.Pal.Metal);
                    for (int k = -1; k <= 1; k += 2) g.Tube(new float3(-0.018f, k * 0.09f, 0f), new float3(0.018f, k * 0.09f, 0f), 0.005f, 0.005f, 6, g.Pal.Edge);
                    Grip(g, 2, -0.06f, 0.04f, 0.017f);
                    break;
                case 4: // wrapped grip with a hanging tassel
                    Grip(g, 0, -0.07f, 0.07f, 0.018f);
                    g.Tube(new float3(0f, -0.07f, 0.02f), new float3(0.01f, -0.18f, 0.03f), 0.004f, 0.012f, 5, g.Pal.Accent);
                    break;
                default: // set stone
                    Grip(g, 3, -0.07f, 0.06f, 0.018f);
                    g.Gem(new float3(0f, 0.08f, 0.018f), Front, 0.012f, 0.012f, g.Pal.Accent);
                    break;
            }
        }

        private static readonly Color StringCol = new Color(0.9f, 0.88f, 0.8f);

        private static void BowString(Kit g, int variant, float3 a, float3 b)
        {
            Color sc = variant == 5 ? g.Pal.Accent : StringCol;
            g.Tube(a, b, 0.002f, 0.002f, 3, sc);
            float3 mid = (a + b) * 0.5f;
            switch (variant)
            {
                case 1: g.Tube(a + new float3(0.004f, 0f, 0f), b + new float3(0.004f, 0f, 0f), 0.0015f, 0.0015f, 3, sc); break;
                case 2: // silencer puffs
                    for (int k = -1; k <= 1; k += 2) g.Ellipsoid(math.lerp(a, b, k < 0 ? 0.2f : 0.8f), new float3(0.012f, 0.018f, 0.012f), g.Pal.Wrap);
                    break;
                case 3: g.Ball(mid + Up * 0.02f, 0.005f, g.Pal.Accent); break;
                case 4: // cam wheels at the tips
                    g.Ring(a, new float3(1f, 0f, 0f), 0.02f, 0.004f, 8, g.Pal.Metal);
                    g.Ring(b, new float3(1f, 0f, 0f), 0.02f, 0.004f, 8, g.Pal.Metal);
                    break;
                case 5: // served centre
                    g.Tube(mid - Up * 0.06f, mid + Up * 0.06f, 0.004f, 0.004f, 4, g.Pal.Wrap);
                    break;
            }
        }

        private static void BowRest(Kit g, int variant, float stringZ, float half)
        {
            switch (variant)
            {
                case 1: // nocked arrow
                    g.Tube(new float3(0.012f, 0.06f, stringZ), new float3(0.012f, 0.06f, stringZ + half * 0.75f), 0.004f, 0.004f, 4, g.Pal.Second);
                    g.Spike(new float3(0.012f, 0.06f, stringZ + half * 0.75f), new float3(0.012f, 0.06f, stringZ + half * 0.83f), 0.008f, 4, g.Pal.Metal);
                    for (int k = 0; k < 3; k++) // fletching vanes around the shaft
                        g.Box(new float3(0.012f, 0.06f, stringZ + 0.05f), new float3(0.0015f, 0.008f, 0.035f), g.Pal.Accent, 0f, k * 2.094f);
                    break;
                case 2: // side quiver
                    g.Box(new float3(-0.045f, -0.12f, 0f), new float3(0.022f, 0.14f, 0.022f), g.Pal.Wrap, 0f, 0.1f);
                    for (int k = 0; k < 3; k++) g.Tube(new float3(-0.05f + k * 0.008f, 0.02f, 0f), new float3(-0.052f + k * 0.008f, 0.1f, 0f), 0.003f, 0.003f, 4, g.Pal.Accent);
                    break;
                case 3: // magazine on the riser
                    g.Box(new float3(0f, 0.1f, 0.05f), new float3(0.02f, 0.06f, 0.03f), g.Pal.Metal);
                    g.Box(new float3(0f, 0.1f, 0.081f), new float3(0.016f, 0.05f, 0.002f), g.Pal.Accent);
                    break;
                case 4: // sight pin
                    g.Box(new float3(0.02f, 0.1f, 0.02f), new float3(0.004f, 0.025f, 0.012f), g.Pal.Metal);
                    g.Ball(new float3(0.026f, 0.11f, 0.03f), 0.004f, g.Pal.Accent);
                    break;
                case 5: // stabiliser rod
                    g.Tube(new float3(0f, -0.04f, 0.02f), new float3(0f, -0.04f, 0.02f + half * 0.35f), 0.006f, 0.006f, 6, g.Pal.Metal);
                    g.Tube(new float3(0f, -0.04f, 0.02f + half * 0.35f), new float3(0f, -0.04f, 0.03f + half * 0.35f), 0.012f, 0.012f, 8, g.Pal.Edge);
                    break;
            }
        }

        private static void BuildCrossbow(Kit g)
        {
            GearLookSpec s = g.Spec;
            bool heavy = s.Kind == GearKind.HeavyCrossbow;
            float len = g.Len;
            float w = (heavy ? 0.035f : 0.022f) * s.Width;
            float frontY = len * 0.82f;

            // 握把 (stock): lies along +Y from the trigger hand.
            switch (g.V(1))
            {
                case 1: // pistol grip
                    g.Box(new float3(0f, len * 0.4f, 0f), new float3(w, len * 0.46f, w * 1.2f * s.Thickness), g.Pal.Second);
                    g.Box(new float3(0f, -0.02f, -w * 2f), new float3(w * 0.8f, 0.06f, w), g.Pal.Wrap, 0f, 0f);
                    break;
                case 2: // rifle stock with a curved butt
                    g.Box(new float3(0f, len * 0.35f, 0f), new float3(w, len * 0.5f, w * 1.3f * s.Thickness), g.Pal.Second);
                    g.Loft(new[]
                    {
                        Sec(new float3(0f, -0.02f, 0f), new float3(w, 0f, 0f), new float3(0f, 0f, w * 1.3f)),
                        Sec(new float3(0f, -len * 0.25f, -w * 1.5f), new float3(w * 1.1f, 0f, 0f), new float3(0f, 0f, w * 2.2f)),
                    }, g.Pal.Second, notch: false, square: true);
                    break;
                case 3: // skeleton frame
                    for (int side = -1; side <= 1; side += 2)
                        g.Tube(new float3(side * w * 0.7f, -0.04f, 0f), new float3(side * w * 0.7f, frontY, 0f), w * 0.3f, w * 0.3f, 5, g.Pal.Metal);
                    for (int k = 0; k < 4; k++)
                        g.Box(new float3(0f, len * (0.05f + 0.22f * k), 0f), new float3(w, 0.006f, w * 0.4f), g.Pal.Metal);
                    break;
                case 4: // ornate stock with a motif panel
                    g.Box(new float3(0f, len * 0.35f, 0f), new float3(w, len * 0.5f, w * 1.3f * s.Thickness), g.Pal.Main);
                    Engrave(g, new Plate(new float3(w * 1.02f, len * 0.35f, 0f), Up, Front, new float3(1f, 0f, 0f), len * 0.3f, w * 1.1f), s.Motif.Region, 1, g.Pal.Accent);
                    break;
                case 5: // stirrup at the front
                    g.Box(new float3(0f, len * 0.35f, 0f), new float3(w, len * 0.5f, w * 1.3f * s.Thickness), g.Pal.Second);
                    g.Ring(new float3(0f, frontY + w * 2.5f, 0f), Front, w * 1.8f, w * 0.25f, 8, g.Pal.Metal, math.PI, 2f * math.PI);
                    break;
                default:
                    g.Box(new float3(0f, len * 0.35f, 0f), new float3(w, len * 0.5f, w * 1.3f * s.Thickness), g.Pal.Second);
                    g.Box(new float3(0f, -0.05f, -w * 1.6f), new float3(w * 0.8f, 0.05f, w * 1.2f), g.Pal.Second);
                    break;
            }

            // 弓臂 (prod).
            int prod = g.V(0);
            float span = len * (heavy ? 0.45f : 0.55f) * (0.9f + s.Curvature * 0.3f) * (prod == 3 ? 1.12f : prod == 5 ? 0.8f : 1f);
            float sweep = prod == 1 ? 0.9f : prod == 5 ? 1.4f : 0.5f + s.Curvature;
            var tips = new float3[2];
            for (int layer = 0; layer < (prod == 2 ? 2 : 1); layer++)
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = -layer * w * 1.1f;
                    float3 tip = new float3(side * span, frontY - span * 0.25f * sweep, z);
                    if (prod == 1) // recurve: kink the tip back forward
                    {
                        float3 knee = new float3(side * span * 0.8f, frontY - span * 0.3f * sweep, z);
                        g.Loft(new[]
                        {
                            Sec(new float3(0f, frontY, z), new float3(0f, 0.018f, 0f), new float3(0f, 0f, 0.012f)),
                            Sec(knee, new float3(0f, 0.011f, 0f), new float3(0f, 0f, 0.008f)),
                        }, g.Pal.Metal, notch: false);
                        tip = knee + new float3(side * span * 0.2f, span * 0.12f, 0f);
                        g.Loft(new[] { Sec(knee, new float3(0f, 0.011f, 0f), new float3(0f, 0f, 0.008f)), Sec(tip, new float3(0f, 0.007f, 0f), new float3(0f, 0f, 0.005f)) },
                            g.Pal.Metal, notch: false);
                    }
                    else
                    {
                        float thick = prod == 3 ? 1.5f : 1f;
                        g.Loft(new[]
                        {
                            Sec(new float3(0f, frontY, z), new float3(0f, 0.018f * thick, 0f), new float3(0f, 0f, 0.012f)),
                            Sec(tip, new float3(0f, 0.008f * thick, 0f), new float3(0f, 0f, 0.006f)),
                        }, layer == 0 ? g.Pal.Metal : g.Pal.Second, notch: false);
                    }
                    if (prod == 4) g.Ring(tip, Front, 0.022f, 0.004f, 8, g.Pal.Edge);
                    if (layer == 0) tips[(side + 1) / 2] = tip;
                }
            g.Tube(tips[0], tips[1], 0.002f, 0.002f, 3, StringCol);
            if (heavy)
                g.Tube(new float3(0f, frontY, 0f), new float3(0f, frontY + 0.12f, 0f), w * 0.9f, w * 1.2f, g.Sides, g.Pal.Metal);

            // 弦／機括 (mechanism).
            switch (g.V(2))
            {
                case 1: // goat's-foot lever
                    g.Tube(new float3(0f, len * 0.2f, w * 1.4f), new float3(0f, len * 0.55f, w * 2.4f), w * 0.2f, w * 0.2f, 5, g.Pal.Metal);
                    g.Tube(new float3(0f, len * 0.2f, w * 1.4f), new float3(0f, len * 0.42f, w * 3.2f), w * 0.2f, w * 0.2f, 5, g.Pal.Metal);
                    break;
                case 2: // windlass crank
                    g.Tube(new float3(-w * 2f, 0.02f, 0f), new float3(w * 2f, 0.02f, 0f), w * 0.3f, w * 0.3f, 6, g.Pal.Metal);
                    for (int side = -1; side <= 1; side += 2)
                        g.Tube(new float3(side * w * 2f, 0.02f, 0f), new float3(side * w * 2f, -0.03f, w * 1.5f), w * 0.2f, w * 0.2f, 5, g.Pal.Wrap);
                    break;
                case 3: // cranequin cog on the side
                    g.Tube(new float3(w * 1.05f, len * 0.15f, 0f), new float3(w * 1.4f, len * 0.15f, 0f), w * 1.6f, w * 1.6f, 12, g.Pal.Metal);
                    for (int k = 0; k < 8; k++)
                        g.Box(new float3(w * 1.22f, len * 0.15f, 0f) + new float3(0f, math.sin(k * math.PI / 4f), math.cos(k * math.PI / 4f)) * w * 1.75f,
                            new float3(w * 0.17f, w * 0.25f, w * 0.25f), g.Pal.Metal);
                    break;
                case 4: // spring tubes
                    for (int side = -1; side <= 1; side += 2)
                        g.Tube(new float3(side * w * 1.3f, len * 0.3f, 0f), new float3(side * w * 1.3f, len * 0.7f, 0f), w * 0.35f, w * 0.35f, 6, g.Pal.Edge);
                    break;
                case 5: // trigger guard and nut
                    g.Ring(new float3(0f, 0.02f, -w * 1.6f), new float3(1f, 0f, 0f), w * 0.9f, w * 0.12f, 6, g.Pal.Metal, 0f, math.PI);
                    g.Tube(new float3(-w * 1.05f, len * 0.5f, 0f), new float3(w * 1.05f, len * 0.5f, 0f), w * 0.5f, w * 0.5f, 8, g.Pal.Edge);
                    break;
                default: // plain lever trigger
                    g.Box(new float3(0f, len * 0.08f, -w * 1.7f), new float3(w * 0.15f, len * 0.12f, w * 0.15f), g.Pal.Metal, 0f, 0f);
                    break;
            }

            // 箭槽 (bolt groove).
            switch (g.V(3))
            {
                case 0: // bolt loaded
                    g.Tube(new float3(0f, len * 0.4f, w * 1.4f), new float3(0f, frontY + 0.06f, w * 1.4f), 0.004f, 0.004f, 4, g.Pal.Second);
                    g.Spike(new float3(0f, frontY + 0.06f, w * 1.4f), new float3(0f, frontY + 0.1f, w * 1.4f), 0.008f, 4, g.Pal.Metal);
                    break;
                case 1: // top magazine
                    g.Box(new float3(0f, len * 0.6f, w * 2.4f), new float3(w * 0.9f, len * 0.16f, w * 1.1f), g.Pal.Main);
                    break;
                case 2: // iron sight
                    g.Box(new float3(0f, len * 0.75f, w * 1.6f), new float3(w * 0.15f, w * 0.15f, w * 0.4f), g.Pal.Metal);
                    g.Box(new float3(0f, len * 0.15f, w * 1.6f), new float3(w * 0.4f, w * 0.15f, w * 0.4f), g.Pal.Metal);
                    break;
                case 4: // side quiver of bolts
                    g.Box(new float3(-w * 1.6f, len * 0.35f, 0f), new float3(w * 0.5f, len * 0.22f, w * 0.8f), g.Pal.Wrap);
                    for (int k = 0; k < 3; k++)
                        g.Spike(new float3(-w * 1.6f, len * 0.57f, (k - 1) * w * 0.4f), new float3(-w * 1.6f, len * 0.62f, (k - 1) * w * 0.4f), 0.004f, 4, g.Pal.Metal);
                    break;
                case 5: // scope tube
                    g.Tube(new float3(0f, len * 0.3f, w * 2.2f), new float3(0f, len * 0.6f, w * 2.2f), w * 0.5f, w * 0.5f, 8, g.Pal.Metal);
                    g.Ring(new float3(0f, len * 0.6f, w * 2.2f), Up, w * 0.5f, w * 0.12f, 8, g.Pal.Accent);
                    break;
            }
            EraFlourish(g, new float3(0f, len * 0.2f, 0f), w * 1.4f, 0.05f);
        }

        // ---------------------------------------------------------------- Focus family: 杖身, 杖頭／槍口, 核心, 纏飾

        private static void BuildStaff(Kit g)
        {
            GearLookSpec s = g.Spec;
            bool staff = s.Kind == GearKind.MagicStaff;
            float len = Broken(g, g.Len);
            float r = staff ? 0.017f : 0.012f;
            float butt = staff ? -g.Len * 0.4f : -0.04f;
            float top = len + butt;
            StaffShaft(g, g.V(0), butt, top, r);
            Grip(g, 0, -0.08f, 0.08f, r);
            if (len < g.Len * 0.9f) return;
            float core = (staff ? 0.05f : 0.028f) * s.Width;
            float3 coreAt = StaffHead(g, g.V(1), top, core, r);
            StaffCore(g, g.V(2), coreAt, core);
            StaffDressing(g, g.V(3), top, core, r);
            EraFlourish(g, new float3(0f, top - 0.06f, 0f), r * 1.5f, 0.05f);
        }

        private static void StaffShaft(Kit g, int variant, float y0, float y1, float r)
        {
            float len = y1 - y0;
            switch (variant)
            {
                case 0:
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r, r * 1.15f, g.Sides, g.Pal.Second);
                    break;
                case 1: // gnarled
                    Haft(g, 2, y0, y1, r);
                    break;
                case 2: // two strands twisted together
                    for (int strand = 0; strand < 2; strand++)
                    {
                        float3 prev = default;
                        for (int i = 0; i <= 10; i++)
                        {
                            float t = i / 10f;
                            float a = t * math.PI * 5f + strand * math.PI;
                            var p = new float3(math.cos(a) * r * 0.6f, y0 + len * t, math.sin(a) * r * 0.6f);
                            if (i > 0) g.Tube(prev, p, r * 0.6f, r * 0.6f, 5, strand == 0 ? g.Pal.Second : Mul(g.Pal.Second, 0.8f));
                            prev = p;
                        }
                    }
                    break;
                case 3: // segmented metal
                    for (int k = 0; k < 6; k++)
                    {
                        float a = y0 + len * k / 6f, b = y0 + len * (k + 1) / 6f;
                        g.Tube(new float3(0f, a, 0f), new float3(0f, b - 0.01f, 0f), r, r, g.Sides, k % 2 == 0 ? g.Pal.Second : g.Pal.Metal);
                        g.Tube(new float3(0f, b - 0.012f, 0f), new float3(0f, b, 0f), r * 1.25f, r * 1.25f, g.Sides, g.Pal.Metal);
                    }
                    break;
                case 4: // tapering, ringed
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r * 0.7f, r * 1.25f, g.Sides, g.Pal.Second);
                    for (int k = 1; k < 4; k++)
                        g.Ring(new float3(0f, y0 + len * k / 4f, 0f), Up, r * math.lerp(0.7f, 1.25f, k / 4f) * 1.1f, r * 0.2f, 8, g.Pal.Accent);
                    break;
                default: // bone-joint knobs
                    g.Tube(new float3(0f, y0, 0f), new float3(0f, y1, 0f), r * 0.9f, r * 0.9f, g.Sides, g.Pal.Second);
                    for (int k = 1; k < 5; k++) g.Ellipsoid(new float3(0f, y0 + len * k / 5f, 0f), new float3(r * 1.5f, r * 1.2f, r * 1.5f), Mul(g.Pal.Second, 1.1f));
                    break;
            }
        }

        // Returns where the core sits.
        private static float3 StaffHead(Kit g, int variant, float top, float core, float r)
        {
            GearLookSpec s = g.Spec;
            switch (variant)
            {
                case 0: // prongs
                    for (int k = 0; k < 3; k++)
                    {
                        float3 d = Dir(k * 2f * math.PI / 3f);
                        g.Tube(new float3(0f, top, 0f), new float3(0f, top + core * 2.4f, 0f) + d * core * (1.2f + s.Curvature), r * 0.5f, r * 0.25f, 4, g.Pal.Metal);
                    }
                    return new float3(0f, top + core * 1.3f, 0f);
                case 1: // crook
                {
                    var crook = new List<(float3, float3, float3)>();
                    for (int i = 0; i <= 6; i++)
                    {
                        float a = i / 6f * math.PI * (1f + s.Curvature * 0.6f);
                        float3 c = new float3(core * 1.6f * (1f - math.cos(a)), top + core * 1.6f * math.sin(a), 0f);
                        crook.Add(Sec(c, new float3(0f, 0f, r), new float3(r, 0f, 0f)));
                    }
                    g.Loft(crook.ToArray(), g.Pal.Second, notch: false);
                    return new float3(core * 1.6f, top + core * 0.6f, 0f);
                }
                case 2: // cup mount for a spire
                    g.Tube(new float3(0f, top, 0f), new float3(0f, top + core * 0.6f, 0f), r, core * 0.9f, 8, g.Pal.Metal);
                    return new float3(0f, top + core * 1.4f, 0f);
                case 3: // ring
                    g.Ring(new float3(0f, top + core * 1.5f, 0f), Front, core * 1.5f, r * 0.6f, 10, g.Pal.Metal);
                    return new float3(0f, top + core * 1.5f, 0f);
                case 4: // crescent horns
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var horn = new List<(float3, float3, float3)>();
                        for (int i = 0; i <= 5; i++)
                        {
                            float t = i / 5f;
                            float a = t * math.PI * 0.55f;
                            float3 c = new float3(side * core * 1.6f * math.sin(a), top + core * 1.6f * (1f - math.cos(a)) + t * core * 1.2f, 0f);
                            float w = math.lerp(r * 0.9f, r * 0.15f, t);
                            horn.Add(Sec(c, new float3(w, 0f, 0f), new float3(0f, 0f, w)));
                        }
                        g.Loft(horn.ToArray(), g.Pal.Metal, notch: false);
                    }
                    return new float3(0f, top + core * 1.2f, 0f);
                default: // lantern cage
                    for (int k = 0; k < 4; k++)
                    {
                        float3 d = Dir(k * math.PI / 2f + math.PI / 4f) * core * 1.3f;
                        g.Tube(new float3(0f, top, 0f), new float3(0f, top + core * 0.3f, 0f) + d, r * 0.3f, r * 0.3f, 4, g.Pal.Metal);
                        g.Tube(new float3(0f, top + core * 0.3f, 0f) + d, new float3(0f, top + core * 2.5f, 0f) + d, r * 0.3f, r * 0.3f, 4, g.Pal.Metal);
                        g.Tube(new float3(0f, top + core * 2.5f, 0f) + d, new float3(0f, top + core * 2.9f, 0f), r * 0.3f, r * 0.3f, 4, g.Pal.Metal);
                    }
                    g.Ring(new float3(0f, top + core * 2.95f, 0f), Front, core * 0.35f, r * 0.2f, 6, g.Pal.Metal);
                    return new float3(0f, top + core * 1.4f, 0f);
            }
        }

        private static void StaffCore(Kit g, int variant, float3 at, float core)
        {
            Color a = g.Pal.Accent;
            switch (variant)
            {
                case 0: g.Ball(at, core, a); break;
                case 1: g.Gem(at - Up * core * 0.3f, Up, core * 0.8f, core * 1.6f, a); break;
                case 2: // crystal cluster
                    g.Spike(at - Up * core * 0.6f, at + Up * core * 1.6f, core * 0.55f, 5, a);
                    g.Spike(at - Up * core * 0.5f, at + new float3(core * 0.9f, core * 0.6f, 0f), core * 0.35f, 5, Mul(a, 0.85f));
                    g.Spike(at - Up * core * 0.5f, at + new float3(-core * 0.7f, core * 0.8f, core * 0.3f), core * 0.3f, 5, Mul(a, 0.9f));
                    break;
                case 3: // orb with a floating ring
                    g.Ball(at, core * 0.75f, a);
                    g.Ring(at, new float3(0.4f, 1f, 0.2f), core * 1.25f, core * 0.07f, 10, g.Pal.Edge);
                    break;
                case 4: // star
                    g.Ball(at, core * 0.5f, a);
                    for (int k = 0; k < 6; k++)
                    {
                        float ang = k * math.PI / 3f;
                        g.Spike(at, at + new float3(math.cos(ang), math.sin(ang), 0f) * core * 1.4f, core * 0.22f, 4, a);
                    }
                    break;
                default: // lens
                    g.Tube(at - Front * core * 0.2f, at + Front * core * 0.2f, core, core, 10, a);
                    g.Ring(at, Front, core * 1.02f, core * 0.1f, 10, g.Pal.Metal);
                    break;
            }
        }

        private static void StaffDressing(Kit g, int variant, float top, float core, float r)
        {
            float3 tie = new float3(0f, top - core * 0.3f, 0f);
            switch (variant)
            {
                case 1: // ribbons
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(tie + new float3(side * r * 1.4f, -core * 2.2f, 0f), new float3(r * 0.6f, core * 2.2f, 0.0012f), g.Pal.Main, 0f, side * 0.12f);
                    break;
                case 2: // bead string
                    for (int k = 0; k < 5; k++) g.Ball(tie + new float3(r * 1.3f + k * 0.002f, -k * r * 1.4f, 0f), r * 0.5f, k % 2 == 0 ? g.Pal.Accent : g.Pal.Metal);
                    break;
                case 3: // feathers
                    for (int k = -1; k <= 1; k++)
                        g.Box(tie + new float3(r * 1.5f + k * r * 0.4f, -core * 1.4f, 0f), new float3(r * 0.5f, core * 1.2f, 0.0012f), g.Pal.Accent, 0f, 0.25f + k * 0.2f);
                    break;
                case 4: // paper talismans
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k * 2.094f;
                        g.Box(tie + Dir(a) * r * 1.4f + new float3(0f, -core * 1.2f, 0f), new float3(r * 0.9f, core * 1.1f, 0.0008f), new Color(0.92f, 0.88f, 0.76f), -a + math.PI * 0.5f);
                    }
                    break;
                case 5: // cloth band carrying the region motif
                    g.Tube(tie - Up * core * 0.8f, tie, r * 1.35f, r * 1.35f, g.Sides, g.Pal.Main);
                    Engrave(g, new Plate(tie - Up * core * 0.4f + Front * r * 1.35f, new float3(1f, 0f, 0f), Up, Front, r * 1.1f, core * 0.35f), g.Spec.Motif.Region, 0, g.Pal.Accent);
                    break;
            }
        }
    }
}
