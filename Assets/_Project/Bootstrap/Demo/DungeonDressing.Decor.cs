using Cosmimera.Core.Random;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    // REQ: planet-world.md#進階地形地城 (地城文化) — the culture's props on and along every wall, the terrain's growths on
    // open walls (no ceiling, both faces and the top), pillar caps by tradition, and damage by wear. Every module face
    // draws from its own Pcg32 stream keyed on its position and the culture seed, so the order modules are visited in
    // never changes what one module gets. Shares and sizes 暫定.
    internal static partial class DungeonDressing
    {
        // Share of module faces that carry an era prop.
        private const float EraPropShare = 0.45f;
        // Share of open-wall growths drawn from the dungeon's own look; the rest borrow from the terrain's other looks.
        private const float OwnLookShare = 0.7f;

        private readonly struct Dress
        {
            public readonly Style Style;
            public readonly DungeonCulture Culture;
            public readonly bool Open;

            public Dress(Style style, DungeonCulture culture, bool open)
            {
                Style = style;
                Culture = culture;
                Open = open;
            }
        }

        // One module face's extras; ez points out of that face. `top` is true for exactly one face of a two-faced wall,
        // so the shared top is dressed once.
        private static void Decor(Sculptor s, in Dress d, float3 c, float3 ex, float3 ey, float3 ez, float len, uint hash,
            bool top)
        {
            DungeonCulture k = d.Culture;
            var rng = new Pcg32(k.Seed ^ (hash * 0x9E3779B97F4A7C15ul), 0x4445434Ful);
            float eraRoll = rng.NextFloat();
            float accentRoll = rng.NextFloat();
            float damageRoll = rng.NextFloat();
            var f = new Face(c, ex, ey, ez, len, FaceOut(d.Style));
            if (d.Open)
            {
                OpenFace(s, d.Style, k.Look, ref rng, f);
                if (top)
                {
                    OpenTop(s, d.Style, k.Look, ref rng, f);
                }
            }

            if (eraRoll < EraPropShare)
            {
                ArchEra era = accentRoll < k.AccentShare ? k.AccentEra : k.Era;
                EraProp(s, era, ref rng, f);
            }

            if (damageRoll < k.Damage)
            {
                Damaged(s, d.Open && top, ref rng, f);
            }
        }

        // Where a module face's surface is, relative to the panel: volcano basalt columns stand ~1 m proud of it.
        private static float FaceOut(Style style) => style == Style.Volcano ? 1f : 0.02f;

        private static float TopY(Style style) => style switch
        {
            Style.Rainforest => PanelTopM + 0.45f,
            Style.IceCap => PanelTopM + 0.3f,
            Style.Temple or Style.Tower or Style.Labyrinth => PanelTopM + 0.22f,
            _ => PanelTopM,
        };

        private readonly struct Face
        {
            public readonly float3 C, Ex, Ey, Ez;
            public readonly float Len, Out;

            public Face(float3 c, float3 ex, float3 ey, float3 ez, float len, float out_)
            {
                C = c;
                Ex = ex;
                Ey = ey;
                Ez = ez;
                Len = len;
                Out = out_;
            }

            // A point on (out = 0) or proud of the face, `along` metres from the module centre.
            public float3 At(float along, float h, float out_) => C + Ex * along + Ey * h + Ez * (FaceHalf(h) + Out + out_);

            public float3 Foot(float along, float out_) => At(along, 0f, out_);

            // On the wall top, `across` metres from the ridge line toward this face.
            public float3 Top(float along, float across, float y) => C + Ex * along + Ey * y + Ez * across;
        }

        private static float Along(ref Pcg32 rng, in Face f, float share = 0.8f) => (rng.NextFloat() - 0.5f) * f.Len * share;

        private static float Range(ref Pcg32 rng, float lo, float hi) => lo + (hi - lo) * rng.NextFloat();

        private static int Kind(ref Pcg32 rng, int look)
        {
            float own = rng.NextFloat();
            int other = (int)rng.NextUInt(DungeonCulture.Looks);
            return own < OwnLookShare ? look : other;
        }

        private static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1f);

        // Color * float would scale alpha too, and alpha < 1 blends toward the material accent.
        private static Color Dim(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        // A box from a to b (its long axis), `w` wide across and `depth` deep toward `faceOut`.
        private static void Stick(Sculptor s, int sub, float3 a, float3 b, float3 faceOut, float w, float depth, Color col)
        {
            float3 dir = b - a;
            float l = math.length(dir);
            if (l < 1e-3f)
            {
                return;
            }

            dir /= l;
            float3 side = math.cross(dir, faceOut);
            side = math.lengthsq(side) < 1e-6f ? math.cross(dir, new float3(1f, 0f, 0f)) : side;
            side = math.normalize(side);
            float3 deep = math.cross(side, dir);
            s.Box(sub, 0.5f * (a + b), side, dir, deep, new float3(w, 0.5f * l, depth), 1f, 1f, col);
        }

        // ---- open walls: the terrain's growths ------------------------------------------------------------------

        private static void OpenFace(Sculptor s, Style style, int look, ref Pcg32 rng, in Face f)
        {
            int n = 1 + (int)rng.NextUInt(3);
            for (int i = 0; i < n; i++)
            {
                int kind = Kind(ref rng, look);
                switch (style)
                {
                    case Style.Rainforest:
                        RainforestFace(s, kind, ref rng, f);
                        break;
                    case Style.Volcano:
                        VolcanoFace(s, kind, ref rng, f);
                        break;
                    case Style.IceCap:
                        IceFace(s, kind, ref rng, f);
                        break;
                    case Style.Labyrinth:
                        LabyrinthFace(s, kind, ref rng, f);
                        break;
                }
            }
        }

        private static void OpenTop(Sculptor s, Style style, int look, ref Pcg32 rng, in Face f)
        {
            float y = TopY(style);
            int n = 1 + (int)rng.NextUInt(2);
            for (int i = 0; i < n; i++)
            {
                int kind = Kind(ref rng, look);
                float a = Along(ref rng, f, 0.7f);
                // Inside the ice merlons (they stand 0.4 m in from both faces) and off the very edge elsewhere.
                float across = Range(ref rng, -1.8f, 1.8f);
                float3 at = f.Top(a, across, y);
                switch (style)
                {
                    case Style.Rainforest:
                        if (kind == 0)
                        {
                            // Sapling rooted in the moss: trunk and three leaf cones.
                            float tall = Range(ref rng, 1.4f, 3.2f);
                            s.Prism(TrimSub, at, f.Ey, 0.12f, 0.07f, tall, 5, Rgb(0.55f, 0.45f, 0.35f));
                            for (int k = 0; k < 3; k++)
                            {
                                float t = k * 2.09f + rng.NextFloat();
                                float3 axis = math.normalize(f.Ex * math.cos(t) + f.Ez * math.sin(t) + f.Ey * 0.6f);
                                s.Prism(TrimSub, at + f.Ey * (tall * (0.65f + 0.12f * k)), axis, 0.45f, 0.02f, 1.2f, 4,
                                    Rgb(0.85f, 1f, 0.75f));
                            }
                        }
                        else
                        {
                            Bromeliad(s, at, f.Ex, f.Ey, f.Ez * 0.2f, Range(ref rng, 0.7f, 1.2f), true);
                        }

                        break;
                    case Style.Volcano:
                        if (kind == 0)
                        {
                            // Fumarole: a squat vent cone with a glowing throat.
                            s.Prism(BodySub, at, f.Ey, 0.95f, 0.4f, 0.9f, 7, 0.6f);
                            s.Prism(GlowSub, at + f.Ey * 0.85f, f.Ey, 0.33f, 0.28f, 0.08f, 7, 1f);
                        }
                        else if (kind == 1)
                        {
                            // A small lava pool in a cooled rim.
                            s.Box(BodySub, at + f.Ey * 0.12f, f.Ex, f.Ey, f.Ez, new float3(0.9f, 0.12f, 0.75f), 0.85f, 0.85f, 0.55f);
                            s.Box(GlowSub, at + f.Ey * 0.25f, f.Ex, f.Ey, f.Ez, new float3(0.65f, 0.03f, 0.5f), 1f, 1f, 1f);
                        }
                        else
                        {
                            for (int k = 0; k < 3; k++)
                            {
                                float t = rng.NextFloat() * 6.283f;
                                float3 axis = math.normalize(f.Ey + (f.Ex * math.cos(t) + f.Ez * math.sin(t)) * 0.5f);
                                s.Prism(TrimSub, at, axis, Range(ref rng, 0.2f, 0.35f), 0.02f, Range(ref rng, 0.8f, 1.6f), 4, 0.35f);
                            }
                        }

                        break;
                    case Style.IceCap:
                        if (kind == 2)
                        {
                            // Wind-packed snow cornice curling over this face.
                            s.Box(TrimSub, f.Top(a, PanelTopHalfM - 0.1f, PanelTopM + 0.45f), f.Ex, f.Ey, f.Ez,
                                new float3(f.Len * 0.35f, 0.3f, 0.9f), 0.8f, 0.6f, 1f);
                        }
                        else
                        {
                            float3 spire = f.Top(a, math.min(across, 1.4f), y);
                            s.Prism(GlowSub, spire, math.normalize(f.Ey + f.Ez * Range(ref rng, -0.2f, 0.2f)), 0.38f, 0.02f,
                                Range(ref rng, 1.2f, 2.6f), 5, 1f);
                            s.Prism(GlowSub, spire + f.Ex * 0.4f, math.normalize(f.Ey + f.Ex * 0.4f), 0.2f, 0.02f, 1f, 5, 0.9f);
                        }

                        break;
                    case Style.Labyrinth:
                        if (kind == 1)
                        {
                            CrystalCluster(s, at, f.Ey, f.Ex, ref rng, 1.3f);
                        }
                        else
                        {
                            // A broken obelisk stub, its top course slipped askew.
                            float h0 = Range(ref rng, 0.8f, 1.8f);
                            s.Box(TrimSub, at + f.Ey * (0.5f * h0), f.Ex, f.Ey, f.Ez, new float3(0.45f, 0.5f * h0, 0.45f), 0.85f, 0.85f,
                                0.9f);
                            float t = Range(ref rng, -0.4f, 0.4f);
                            float3 rx = math.normalize(f.Ex * math.cos(t) + f.Ez * math.sin(t));
                            float3 rz = math.cross(rx, f.Ey);
                            s.Box(TrimSub, at + f.Ey * (h0 + 0.3f) + rx * 0.15f, rx, f.Ey, rz, new float3(0.35f, 0.3f, 0.35f), 0.8f,
                                0.8f, 0.8f);
                        }

                        break;
                }
            }
        }

        private static void Bromeliad(Sculptor s, float3 at, float3 ex, float3 ey, float3 lean, float size, bool flat)
        {
            // Strap leaves fanned from one heart; on a face the fan opens upward and outward (epiphyte on bark).
            int leaves = 7;
            for (int k = 0; k < leaves; k++)
            {
                float t = flat ? k * 6.283f / leaves : 0.15f + k * 2.85f / (leaves - 1);
                float3 spread = flat ? ex * math.cos(t) + math.cross(ey, ex) * math.sin(t) : ex * math.cos(t) + ey * math.sin(t);
                float3 axis = math.normalize(spread + ey * (flat ? 0.9f : 0.35f) + lean);
                s.Prism(TrimSub, at, axis, 0.13f, 0.01f, size, 3, Rgb(0.9f, 1f, 0.72f));
            }

            s.Prism(GlowSub, at, math.normalize(ey + lean), 0.11f, 0.03f, size * 0.45f, 5, 1f);
        }

        private static void RainforestFace(Sculptor s, int kind, ref Pcg32 rng, in Face f)
        {
            float a = Along(ref rng, f);
            switch (kind)
            {
                case 0:
                    // 附生鳳梨 on the wall.
                    Bromeliad(s, f.At(a, Range(ref rng, 1.2f, 6.5f), 0.12f), f.Ex, f.Ey, f.Ez * 0.7f, Range(ref rng, 0.5f, 1f), false);
                    break;
                case 1:
                {
                    // Moss curtain from the top, and a stack of bracket fungi (bioluminescent: glow sub-mesh).
                    float drop = Range(ref rng, 2f, 5.5f);
                    for (int k = -1; k <= 1; k++)
                    {
                        float x = a + k * 0.35f;
                        float dk = drop * (0.7f + 0.15f * (k + 1));
                        Stick(s, TrimSub, f.At(x, PanelTopM - 0.05f, 0.08f), f.At(x, PanelTopM - dk, 0.1f), f.Ez,
                            Range(ref rng, 0.1f, 0.18f), 0.05f, Rgb(0.72f, 0.82f, 0.6f));
                    }

                    float h = Range(ref rng, 0.8f, 4f);
                    float b = Along(ref rng, f);
                    int shelves = 2 + (int)rng.NextUInt(3);
                    for (int k = 0; k < shelves; k++)
                    {
                        float w = 0.5f - 0.07f * k;
                        s.Box(GlowSub, f.At(b + 0.12f * k, h + 0.35f * k, w * 0.6f), f.Ex, f.Ey, f.Ez,
                            new float3(w, 0.06f, w * 0.65f), 0.85f, 0.75f, 0.75f);
                    }

                    break;
                }
                default:
                {
                    // Strangler roots crossing the face, an orchid spray where they part.
                    float h = Range(ref rng, 0.2f, 2f);
                    for (int k = 0; k < 2; k++)
                    {
                        float x0 = a + (k == 0 ? -1.4f : 1.4f);
                        Stick(s, TrimSub, f.At(x0, h, 0.1f), f.At(a, h + Range(ref rng, 3f, 6f), 0.1f), f.Ez, 0.12f, 0.1f,
                            Rgb(0.6f, 0.52f, 0.42f));
                    }

                    float3 orchid = f.At(a, h + 2.4f, 0.25f);
                    for (int k = 0; k < 4; k++)
                    {
                        float t = k * 1.57f + rng.NextFloat();
                        s.Prism(GlowSub, orchid, math.normalize(f.Ez + (f.Ex * math.cos(t) + f.Ey * math.sin(t)) * 0.8f), 0.12f,
                            0.04f, 0.35f, 4, 1f);
                    }

                    break;
                }
            }
        }

        private static void VolcanoFace(Sculptor s, int kind, ref Pcg32 rng, in Face f)
        {
            float a = Along(ref rng, f);
            switch (kind)
            {
                case 0:
                {
                    // Lava seeping from the top: two offset runs and a glowing apron at the foot.
                    float len = Range(ref rng, 2f, 6.5f);
                    float kink = PanelTopM - len * Range(ref rng, 0.3f, 0.6f);
                    float jog = Range(ref rng, -0.4f, 0.4f);
                    float w = Range(ref rng, 0.12f, 0.28f);
                    Stick(s, GlowSub, f.At(a, PanelTopM - 0.1f, 0.06f), f.At(a, kink, 0.06f), f.Ez, w, 0.06f, Color.white);
                    Stick(s, GlowSub, f.At(a, kink + w, 0.06f), f.At(a + jog, PanelTopM - len, 0.06f), f.Ez, w * 0.8f, 0.06f,
                        Color.white);
                    if (len > 5f)
                    {
                        s.Box(GlowSub, f.Foot(a + jog, 0.5f) + f.Ey * 0.04f, f.Ex, f.Ey, f.Ez, new float3(0.6f, 0.04f, 0.45f), 1f,
                            1f, 0.9f);
                    }

                    break;
                }
                case 1:
                {
                    // 凝結岩塊: cooled pillows of lava stuck to the columns, a hot seam between them.
                    int n = 2 + (int)rng.NextUInt(3);
                    float h = Range(ref rng, 0.4f, 5.5f);
                    for (int k = 0; k < n; k++)
                    {
                        float size = Range(ref rng, 0.35f, 0.8f);
                        float3 at = f.At(a + Range(ref rng, -0.8f, 0.8f), h + k * 0.55f, size * 0.25f);
                        Lump(s, at - f.Ey * (size * 0.4f), f.Ex, f.Ey, f.Ez, size, rng.NextUInt());
                    }

                    s.Box(GlowSub, f.At(a, h + 0.3f, 0.35f), f.Ex, f.Ey, f.Ez, new float3(0.7f, 0.03f, 0.05f), 1f, 1f, 1f);
                    break;
                }
                default:
                {
                    // Obsidian shards jutting out of the basalt.
                    float h = Range(ref rng, 0.5f, 6f);
                    int n = 3 + (int)rng.NextUInt(3);
                    for (int k = 0; k < n; k++)
                    {
                        float3 axis = math.normalize(f.Ez + f.Ex * Range(ref rng, -0.7f, 0.7f) + f.Ey * Range(ref rng, -0.3f, 0.8f));
                        s.Prism(TrimSub, f.At(a + Range(ref rng, -0.5f, 0.5f), h + Range(ref rng, -0.4f, 0.4f), -0.1f), axis,
                            Range(ref rng, 0.15f, 0.32f), 0.02f, Range(ref rng, 0.7f, 1.7f), 4, 0.35f);
                    }

                    break;
                }
            }
        }

        private static void IceFace(Sculptor s, int kind, ref Pcg32 rng, in Face f)
        {
            float a = Along(ref rng, f);
            switch (kind)
            {
                case 0:
                {
                    // Icicles under the coping.
                    int n = 3 + (int)rng.NextUInt(5);
                    for (int k = 0; k < n; k++)
                    {
                        float x = a + (k - 0.5f * n) * 0.35f + Range(ref rng, -0.1f, 0.1f);
                        s.Prism(GlowSub, f.At(x, PanelTopM - 0.05f, 0.22f), -f.Ey, Range(ref rng, 0.1f, 0.24f), 0.01f,
                            Range(ref rng, 0.5f, 2.4f), 5, 1f);
                    }

                    break;
                }
                case 1:
                {
                    // A boulder frozen into the wall, frost crystals round it.
                    float h = Range(ref rng, 0.5f, 5f);
                    float size = Range(ref rng, 0.6f, 1.1f);
                    Lump(s, f.At(a, h, size * 0.15f) - f.Ey * (size * 0.4f), f.Ex, f.Ey, f.Ez, size, rng.NextUInt());
                    CrystalCluster(s, f.At(a + size, h + 0.2f, 0f), f.Ez, f.Ey, ref rng, 0.8f);
                    break;
                }
                default:
                {
                    // Snow drift banked at the foot, rime fins grown into the wind above it.
                    s.Box(TrimSub, f.Foot(a, 0.6f) + f.Ey * 0.3f, f.Ex, f.Ey, f.Ez, new float3(Range(ref rng, 0.9f, 1.6f), 0.4f, 0.8f),
                        0.7f, 0.4f, 1f);
                    int fins = 2 + (int)rng.NextUInt(3);
                    for (int k = 0; k < fins; k++)
                    {
                        s.Box(TrimSub, f.At(a + Range(ref rng, -1f, 1f), Range(ref rng, 2f, 6.5f), 0.3f), f.Ex, f.Ey, f.Ez,
                            new float3(0.05f, Range(ref rng, 0.25f, 0.5f), 0.35f), 1f, 0.5f, 0.95f);
                    }

                    break;
                }
            }
        }

        private static void LabyrinthFace(Sculptor s, int kind, ref Pcg32 rng, in Face f)
        {
            float a = Along(ref rng, f);
            switch (kind)
            {
                case 0:
                {
                    // Hanging chain ending in a shackle, and a rune sconce with a violet flame.
                    int links = 6 + (int)rng.NextUInt(7);
                    float h = PanelTopM - 0.3f;
                    for (int k = 0; k < links; k++, h -= 0.26f)
                    {
                        bool flat = (k & 1) == 0;
                        s.Box(TrimSub, f.At(a, h, 0.12f), f.Ex, f.Ey, f.Ez, new float3(flat ? 0.08f : 0.025f, 0.14f, flat ? 0.025f : 0.08f),
                            1f, 1f, 0.5f);
                    }

                    s.Prism(TrimSub, f.At(a, h - 0.1f, 0.05f), f.Ex, 0.16f, 0.16f, 0.06f, 8, 0.5f, f.Ey);
                    float b = Along(ref rng, f);
                    s.Box(TrimSub, f.At(b, 3.2f, 0.22f), f.Ex, f.Ey, f.Ez, new float3(0.13f, 0.25f, 0.22f), 1f, 1f, 0.7f);
                    s.Prism(GlowSub, f.At(b, 3.45f, 0.28f), f.Ey, 0.15f, 0.01f, 0.45f, 5, 1f);
                    break;
                }
                case 1:
                {
                    // Shadow crystals splitting the stone, the crack they grew from still lit.
                    float h = Range(ref rng, 0.6f, 6f);
                    CrystalCluster(s, f.At(a, h, -0.05f), f.Ez, f.Ey, ref rng, 1f);
                    float3 p = f.At(a, h, 0.02f);
                    for (int k = 0; k < 3; k++)
                    {
                        float3 q = p + f.Ex * Range(ref rng, -0.6f, 0.6f) + f.Ey * Range(ref rng, 0.5f, 1.1f) * (k == 1 ? -1f : 1f);
                        Stick(s, GlowSub, p, q, f.Ez, 0.035f, 0.02f, Rgb(0.8f, 0.8f, 0.8f));
                    }

                    break;
                }
                default:
                {
                    // Dark creeper climbing from the foot in zigzags.
                    float3 p = f.Foot(a, 0.06f);
                    int steps = 4 + (int)rng.NextUInt(6);
                    for (int k = 0; k < steps; k++)
                    {
                        float h = (k + 1) * 0.85f;
                        float3 q = f.At(a + ((k & 1) == 0 ? 0.35f : -0.35f), h, 0.06f);
                        Stick(s, BodySub, p, q, f.Ez, 0.07f, 0.05f, Rgb(0.42f, 0.5f, 0.42f));
                        if ((k & 1) == 1)
                        {
                            s.Prism(BodySub, q, math.normalize(f.Ez + f.Ex * 0.6f), 0.18f, 0.01f, 0.4f, 3, Rgb(0.4f, 0.55f, 0.4f));
                        }

                        p = q;
                    }

                    break;
                }
            }
        }

        private static void CrystalCluster(Sculptor s, float3 at, float3 up, float3 side, ref Pcg32 rng, float scale)
        {
            int n = 3 + (int)rng.NextUInt(3);
            float3 other = math.normalize(math.cross(up, side));
            for (int k = 0; k < n; k++)
            {
                float3 axis = math.normalize(up + side * Range(ref rng, -0.6f, 0.6f) + other * Range(ref rng, -0.6f, 0.6f));
                s.Prism(GlowSub, at, axis, Range(ref rng, 0.12f, 0.28f) * scale, 0.01f, Range(ref rng, 0.6f, 1.6f) * scale, 4, 1f);
            }
        }

        // ---- era props: what the builders left -------------------------------------------------------------------

        private static void EraProp(Sculptor s, ArchEra era, ref Pcg32 rng, in Face f)
        {
            float a = Along(ref rng, f, 0.6f);
            Color warm = Rgb(1f, 0.86f, 0.68f);
            Color rust = Rgb(0.9f, 0.62f, 0.45f);
            Color brass = Rgb(1f, 0.82f, 0.48f);
            switch (era)
            {
                case ArchEra.Primal:
                {
                    // Bone totem at the foot, a hide stretched on the wall.
                    float3 foot = f.Foot(a, 0.6f);
                    s.Prism(TrimSub, foot, f.Ey, 0.1f, 0.08f, 2.4f, 6, warm);
                    s.Box(BodySub, foot + f.Ey * 2.45f, f.Ex, f.Ey, f.Ez, new float3(0.22f, 0.2f, 0.24f), 0.8f, 0.8f, 0.97f);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        s.Prism(TrimSub, foot + f.Ey * 2.5f + f.Ex * (k * 0.18f), math.normalize(f.Ex * k + f.Ey * 0.8f), 0.07f,
                            0.01f, 0.55f, 4, 0.95f);
                    }

                    float b = a + (rng.NextFloat() < 0.5f ? -1.3f : 1.3f);
                    s.Box(TrimSub, f.At(b, 3.3f, 0.06f), f.Ex, f.Ey, f.Ez, new float3(0.6f, 0.8f, 0.03f), 1.15f, 1f, Dim(warm, 0.8f));
                    break;
                }
                case ArchEra.Agrarian:
                {
                    // A plank shelf of pots, sacks slumped below it.
                    s.Box(TrimSub, f.At(a, 2.2f, 0.25f), f.Ex, f.Ey, f.Ez, new float3(0.9f, 0.05f, 0.25f), 1f, 1f, warm);
                    int pots = 2 + (int)rng.NextUInt(2);
                    for (int k = 0; k < pots; k++)
                    {
                        s.Prism(BodySub, f.At(a - 0.55f + k * 0.5f, 2.25f, 0.25f), f.Ey, Range(ref rng, 0.13f, 0.2f), 0.09f,
                            Range(ref rng, 0.3f, 0.45f), 7, Rgb(0.95f, 0.75f, 0.6f));
                    }

                    for (int k = 0; k < 2; k++)
                    {
                        Lump(s, f.Foot(a + (k - 0.5f) * 0.7f, 0.45f), f.Ex, f.Ey, f.Ez, 0.35f, rng.NextUInt());
                    }

                    break;
                }
                case ArchEra.Mercantile:
                {
                    // A trade banner with a lit emblem; crates and a barrel at the foot.
                    Stick(s, TrimSub, f.At(a, 6.3f, 0.12f), f.At(a, 3.3f, 0.12f), f.Ez, 0.5f, 0.03f, Rgb(0.95f, 0.85f, 0.85f));
                    s.Box(GlowSub, f.At(a, 5.2f, 0.17f), f.Ex, f.Ey, f.Ez, new float3(0.18f, 0.18f, 0.02f), 1f, 1f, 1f);
                    float b = a + (rng.NextFloat() < 0.5f ? -1.1f : 1.1f);
                    s.Box(TrimSub, f.Foot(b, 0.55f) + f.Ey * 0.42f, f.Ex, f.Ey, f.Ez, new float3(0.42f, 0.42f, 0.42f), 1f, 1f, warm);
                    float t = Range(ref rng, -0.5f, 0.5f);
                    float3 rx = math.normalize(f.Ex * math.cos(t) + f.Ez * math.sin(t));
                    s.Box(TrimSub, f.Foot(b, 0.55f) + f.Ey * 1.1f, rx, f.Ey, math.cross(rx, f.Ey), new float3(0.3f, 0.26f, 0.3f), 1f,
                        1f, Dim(warm, 0.9f));
                    s.Prism(BodySub, f.Foot(b + 0.9f, 0.5f), f.Ey, 0.34f, 0.32f, 0.9f, 8, Rgb(0.85f, 0.68f, 0.5f));
                    break;
                }
                case ArchEra.Industrial:
                {
                    // Riser pipe with flanges, a lit gauge, a diagonal girder brace.
                    float3 foot = f.Foot(a, 0.22f);
                    s.Prism(TrimSub, foot, f.Ey, 0.14f, 0.14f, PanelTopM - 0.6f, 8, 0.75f);
                    for (float h = 1.2f; h < PanelTopM - 1f; h += 1.6f)
                    {
                        s.Prism(TrimSub, foot + f.Ey * h, f.Ey, 0.2f, 0.2f, 0.08f, 8, 0.6f);
                    }

                    s.Prism(GlowSub, f.At(a + 0.45f, 1.6f, 0.05f), f.Ez, 0.18f, 0.18f, 0.05f, 10, 1f, f.Ey);
                    float side = rng.NextFloat() < 0.5f ? -1f : 1f;
                    Stick(s, TrimSub, f.At(a + side * 0.5f, 0.3f, 0.1f), f.At(a + side * 2f, 4f, 0.1f), f.Ez, 0.1f, 0.1f,
                        Rgb(0.7f, 0.72f, 0.78f));
                    break;
                }
                case ArchEra.Steampunk:
                {
                    // Brass main along the wall, a valve wheel, a gear set into the stone.
                    s.Prism(TrimSub, f.At(-0.45f * f.Len, 2.4f, 0.25f), f.Ex, 0.16f, 0.16f, 0.9f * f.Len, 8, brass, f.Ey);
                    s.Prism(TrimSub, f.At(a, 1.4f, 0.25f), f.Ez, 0.36f, 0.36f, 0.06f, 10, brass, f.Ey);
                    s.Prism(TrimSub, f.At(a, 1.4f, 0.05f), f.Ez, 0.06f, 0.06f, 0.3f, 6, brass, f.Ey);
                    s.Prism(TrimSub, f.At(a + (rng.NextFloat() < 0.5f ? -1.4f : 1.4f), Range(ref rng, 3.5f, 5.5f), 0f), f.Ez,
                        0.75f, 0.75f, 0.12f, 12, Dim(brass, 0.8f), f.Ey);
                    s.Prism(GlowSub, f.At(a + 0.6f, 2.9f, 0.05f), f.Ez, 0.16f, 0.16f, 0.05f, 10, 1f, f.Ey);
                    break;
                }
                case ArchEra.Future:
                {
                    // Framed holo panel, a console at the foot, a lit cable run.
                    s.Box(TrimSub, f.At(a, 2.6f, 0.04f), f.Ex, f.Ey, f.Ez, new float3(0.72f, 0.52f, 0.03f), 1f, 1f, 0.9f);
                    s.Box(GlowSub, f.At(a, 2.6f, 0.08f), f.Ex, f.Ey, f.Ez, new float3(0.6f, 0.42f, 0.02f), 1f, 1f, 1f);
                    float b = a + (rng.NextFloat() < 0.5f ? -1.2f : 1.2f);
                    s.Box(BodySub, f.Foot(b, 0.42f) + f.Ey * 0.5f, f.Ex, f.Ey, f.Ez, new float3(0.42f, 0.5f, 0.32f), 1f, 0.6f, 0.95f);
                    s.Box(GlowSub, f.Foot(b, 0.42f) + f.Ey * 1.01f, f.Ex, f.Ey, f.Ez, new float3(0.32f, 0.02f, 0.12f), 1f, 1f, 1f);
                    s.Box(GlowSub, f.At(0f, 0.15f, 0.05f), f.Ex, f.Ey, f.Ez, new float3(0.45f * f.Len, 0.03f, 0.03f), 1f, 1f, 0.8f);
                    break;
                }
                case ArchEra.Wasteland:
                {
                    // Scrap plates bolted on askew, rebar poking out, oil drums (one tipped).
                    int plates = 2 + (int)rng.NextUInt(2);
                    for (int k = 0; k < plates; k++)
                    {
                        float t = Range(ref rng, -0.35f, 0.35f);
                        float3 rx = math.normalize(f.Ex * math.cos(t) + f.Ey * math.sin(t));
                        float3 ry = math.normalize(math.cross(f.Ez, rx));
                        s.Box(BodySub, f.At(a + Range(ref rng, -1f, 1f), Range(ref rng, 1f, 5f), 0.05f), rx, ry, f.Ez,
                            new float3(Range(ref rng, 0.5f, 0.9f), Range(ref rng, 0.4f, 0.7f), 0.04f), 1f, 1f, Dim(rust, 0.75f));
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        float3 axis = math.normalize(f.Ez + f.Ex * Range(ref rng, -0.5f, 0.5f) + f.Ey * Range(ref rng, -0.3f, 0.5f));
                        s.Prism(TrimSub, f.At(a + Range(ref rng, -0.8f, 0.8f), Range(ref rng, 2f, 6f), -0.1f), axis, 0.03f, 0.03f,
                            Range(ref rng, 0.5f, 0.9f), 4, 0.45f);
                    }

                    s.Prism(BodySub, f.Foot(a + 0.8f, 0.45f), f.Ey, 0.32f, 0.32f, 0.9f, 8, rust);
                    s.Prism(BodySub, f.Foot(a - 0.6f, 0.9f) + f.Ey * 0.32f - f.Ex * 0.45f, f.Ex, 0.32f, 0.32f, 0.9f, 8, Dim(rust, 0.85f),
                        f.Ey);
                    break;
                }
                default:
                {
                    // Ancient: a carved relief slab, a toppled statue on its plinth.
                    s.Box(TrimSub, f.At(a, 3f, 0.08f), f.Ex, f.Ey, f.Ez, new float3(0.8f, 1.1f, 0.08f), 1f, 1f, 0.9f);
                    for (int k = 0; k < 3; k++)
                    {
                        s.Box(TrimSub, f.At(a + (k - 1) * 0.45f, 3f + Range(ref rng, -0.5f, 0.5f), 0.18f), f.Ex, f.Ey, f.Ez,
                            new float3(0.15f, 0.3f, 0.05f), 1f, 1f, 0.8f);
                    }

                    float b = a + (rng.NextFloat() < 0.5f ? -1.3f : 1.3f);
                    float3 plinth = f.Foot(b, 0.75f);
                    s.Box(TrimSub, plinth + f.Ey * 0.3f, f.Ex, f.Ey, f.Ez, new float3(0.5f, 0.3f, 0.5f), 0.9f, 0.9f, 0.85f);
                    float lean = Range(ref rng, -0.5f, 0.5f);
                    float3 up = math.normalize(f.Ey + f.Ex * lean);
                    s.Box(BodySub, plinth + f.Ey * 0.6f + up * 0.6f, math.normalize(math.cross(up, f.Ez)), up, f.Ez,
                        new float3(0.3f, 0.6f, 0.22f), 0.8f, 0.8f, 0.85f);
                    Lump(s, plinth + f.Ex * (lean > 0f ? 0.9f : -0.9f) + f.Ez * 0.4f, f.Ex, f.Ey, f.Ez, 0.25f, rng.NextUInt());
                    break;
                }
            }
        }

        // ---- wear -----------------------------------------------------------------------------------------------

        private static void Damaged(Sculptor s, bool top, ref Pcg32 rng, in Face f)
        {
            int rubble = 2 + (int)rng.NextUInt(3);
            float a = Along(ref rng, f);
            for (int k = 0; k < rubble; k++)
            {
                Lump(s, f.Foot(a + Range(ref rng, -1f, 1f), Range(ref rng, 0.3f, 1.4f)), f.Ex, f.Ey, f.Ez,
                    Range(ref rng, 0.25f, 0.65f), rng.NextUInt());
            }

            // A dark crack climbing from the rubble.
            float3 p = f.At(a, Range(ref rng, 1.5f, 3f), 0.01f);
            for (int k = 0; k < 3; k++)
            {
                float3 q = p + f.Ex * Range(ref rng, -0.5f, 0.5f) + f.Ey * Range(ref rng, 0.6f, 1.2f);
                Stick(s, TrimSub, p, q, f.Ez, 0.04f, 0.02f, Rgb(0.3f, 0.3f, 0.3f));
                p = q;
            }

            if (top)
            {
                // A coping block knocked loose and lying askew on the wall top.
                Lump(s, f.Top(a, Range(ref rng, -1.2f, 1.2f), PanelTopM), f.Ex, f.Ey, f.Ez, Range(ref rng, 0.4f, 0.7f),
                    rng.NextUInt());
            }
        }

        // ---- pillar caps by tradition ---------------------------------------------------------------------------

        // `top` is the pillar's top centre; under a ceiling the cap becomes a capital band just below it.
        private static void PillarCap(Sculptor s, in Dress d, float3 top, float3 ex, float3 ey, float3 ez, float half)
        {
            ArchRegion region = d.Culture.Region;
            if (!d.Open)
            {
                float3 band = top - ey * 1.2f;
                if (region == ArchRegion.Storm || region == ArchRegion.Crystal)
                {
                    s.Prism(TrimSub, band - ey * 0.3f, ey, half + 0.3f, half + 0.3f, 0.6f, 8, 0.95f, ex);
                }
                else
                {
                    float flare = region == ArchRegion.Twilight ? 1.25f : 1f;
                    float tall = region == ArchRegion.Lava ? 0.45f : 0.28f;
                    s.Box(TrimSub, band, ex, ey, ez, new float3(half + 0.2f, tall, half + 0.2f), flare, flare, 0.95f);
                }

                return;
            }

            switch (region)
            {
                case ArchRegion.Verdant:
                    // Timber pyramid roof.
                    s.Prism(TrimSub, top, ey, (half + 0.4f) * 1.41f, 0.05f, 1.8f, 4, Rgb(0.85f, 0.7f, 0.55f),
                        math.normalize(ex + ez));
                    break;
                case ArchRegion.Lava:
                    for (int k = 0; k < 4; k++)
                    {
                        float3 corner = (k & 1) == 0 ? ex : -ex;
                        corner += k < 2 ? ez : -ez;
                        s.Box(TrimSub, top + ey * 0.5f + corner * (half - 0.45f), ex, ey, ez, new float3(0.45f, 0.5f, 0.45f), 1f, 1f,
                            0.9f);
                    }

                    break;
                case ArchRegion.Ice:
                    s.Prism(TrimSub, top, ey, half * 0.75f, 0.02f, 3.2f, 6, 1f);
                    break;
                case ArchRegion.Storm:
                    // Octagonal drum and a lightning rod with a lit tip.
                    s.Prism(TrimSub, top, ey, half * 0.85f, half * 0.85f, 0.8f, 8, 0.9f);
                    s.Prism(TrimSub, top + ey * 0.8f, ey, 0.07f, 0.05f, 2.6f, 5, 0.6f);
                    s.Prism(GlowSub, top + ey * 3.4f, ey, 0.18f, 0.02f, 0.4f, 5, 1f);
                    break;
                case ArchRegion.Crystal:
                    // Dome with a gem finial.
                    s.Prism(TrimSub, top, ey, half * 0.9f, half * 0.35f, 1.2f, 8, 0.95f);
                    s.Prism(GlowSub, top + ey * 1.2f, ey, 0.38f, 0.02f, 0.9f, 4, 1f);
                    break;
                default:
                    // Twilight: a slab with four upswept horns at its corners.
                    s.Box(TrimSub, top + ey * 0.2f, ex, ey, ez, new float3(half + 0.3f, 0.2f, half + 0.3f), 1.1f, 1.1f, 0.95f);
                    for (int k = 0; k < 4; k++)
                    {
                        float3 corner = ((k & 1) == 0 ? ex : -ex) + (k < 2 ? ez : -ez);
                        s.Prism(TrimSub, top + ey * 0.35f + corner * (half + 0.1f), math.normalize(corner * 0.7f + ey), 0.28f, 0.02f,
                            1.5f, 4, Rgb(1f, 0.85f, 0.6f));
                    }

                    break;
            }
        }
    }
}
