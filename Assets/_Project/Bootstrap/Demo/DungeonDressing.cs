using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Procedural wall panels, ceilings and lamps of the ten advanced-terrain dungeons
    /// (planet-world.md#進階地形地城 牆與頂的美術). Presentation only: collision stays the PlanetWalls ridge the panels wrap.
    /// </summary>
    /// <remarks>
    /// Every mesh has three sub-meshes: <see cref="BodySub"/> (wall body), <see cref="TrimSub"/> (frames, pillars,
    /// beams), <see cref="GlowSub"/> (self-lit parts). Pre: main thread (creates Meshes). Post: same inputs → same meshes.
    /// Side effects: allocates Meshes (the caller owns and destroys them).
    /// </remarks>
    internal static class DungeonDressing
    {
        /// <summary>Sub-mesh indices.</summary>
        public const int BodySub = 0;
        public const int TrimSub = 1;
        public const int GlowSub = 2;
        public const int SubCount = 3;

        // The ridge is H·(1 − smoothstep(1.5, 3.5, d)) (PlanetWalls): never wider than 3.5 m at any height, so a near-
        // vertical face at 3.7 → 3.6 m hides it everywhere, stands where the character is stopped, and keeps face decor
        // flush without shearing it to a lean.
        public const float PanelFootHalfM = 3.7f;
        public const float PanelTopHalfM = 3.6f;
        public const float PanelTopM = RoomDungeonGeometry.WallHeightM + 0.15f;

        // Below the floor so uneven terrain under a surface maze never shows a gap at the wall foot.
        private const float SinkM = 0.8f;
        // Longest straight piece before re-sampling the sphere and the floor (curvature and terrain follow).
        private const float SegmentM = 4f;
        // Faceted emission triples vertices; flushing at this many triangles keeps every mesh under 16-bit indices.
        private const int FlushTriangles = 18000;
        private const float FacetCos = 0.96f;
        private const float RingStepM = 3f;

        /// <summary>Colours of one terrain's dressing.</summary>
        /// <remarks>Pre: none. Post: immutable. Side effects: none.</remarks>
        public readonly struct Palette
        {
            public readonly Color Body;
            public readonly Color Trim;
            public readonly Color Glow;

            /// <summary>ToonScenery self-glow of the glow sub-mesh.</summary>
            public readonly float GlowStrength;

            public Palette(int body, int trim, int glow, float glowStrength)
            {
                Body = Hex(body);
                Trim = Hex(trim);
                Glow = Hex(glow);
                GlowStrength = glowStrength;
            }
        }

        /// <summary>Palette for an advanced terrain id (neutral stone for unknown ids). Values 暫定.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public static Palette PaletteFor(string terrainId) => terrainId switch
        {
            "laboratory" => new Palette(0xA3AEB9, 0x3A434D, 0x6FE6FF, 1.6f),
            "deep_sea" => new Palette(0x3F5862, 0x6F8E8A, 0x5CFFD8, 1.6f),
            "cave" => new Palette(0x6E5E50, 0x7A5432, 0xFFA048, 1.8f),
            "tower" => new Palette(0x8C8276, 0xCBC2B1, 0xFFF0A0, 1.4f),
            "space" => new Palette(0xC9D0D8, 0x56606C, 0xA8C8FF, 1.6f),
            "temple" => new Palette(0xC7B189, 0xE6D6AE, 0xFFC860, 1.4f),
            "rainforest" => new Palette(0x5E6A49, 0x4F6E37, 0x9CE070, 0.6f),
            "volcano" => new Palette(0x3B3535, 0x5A4A44, 0xFF6A20, 1.9f),
            "ice_cap" => new Palette(0xBCE0EE, 0xE8F5FA, 0xA8E8FF, 0.5f),
            "labyrinth" => new Palette(0x4A4458, 0x6E6684, 0xB28CFF, 1.5f),
            _ => new Palette(0x7A7570, 0x9A948C, 0xFFFFFF, 1f),
        };

        /// <summary>Whether the terrain's dungeon is indoors (a ceiling over the grid; surface mazes stay open).</summary>
        /// <remarks>Pre: none. Post: false for unknown ids. Side effects: none.</remarks>
        public static bool HasCeiling(string terrainId) =>
            AdvancedTerrains.TryGet(terrainId, out AdvancedTerrainSpec spec) && spec.Form == AdvancedDungeonForm.Rooms;

        /// <summary>Wall panels along every wall run and round the arena ring, plus the ceiling when indoors.</summary>
        /// <remarks>
        /// Pre: geometry non-null; config.Radius == geometry.Radius; floorAltitude(dir) is the ground altitude (m above
        ///      Radius) under a unit direction with the walls left out; origin is the world point the meshes are relative to.
        /// Post: meshes in world space minus origin (place them at origin, identity rotation); at least one mesh.
        /// Side effects: allocates Meshes.
        /// </remarks>
        public static List<Mesh> BuildWalls(RoomDungeonGeometry geometry, string terrainId, in PlanetConfig config,
            Func<float3, float> floorAltitude, float3 origin)
        {
            Contract.Requires(geometry != null && floorAltitude != null, "geometry and floorAltitude required");
            Contract.Requires(math.abs(config.Radius - geometry.Radius) < 1e-3f, "config radius must equal the geometry radius");
            PlanetConfig cfg = config;
            var sculpt = new Sculptor("Dungeon_" + terrainId, l =>
            {
                float3 dir = geometry.LocalToDirection(new float2(l.x, l.z));
                return cfg.ToWorld(dir, floorAltitude(dir) + l.y) - origin;
            });
            Style style = StyleFor(terrainId);
            float cell = geometry.Dungeon.CellSizeM;
            float2 corner0 = -0.5f * new float2(geometry.Dungeon.Width, geometry.Dungeon.Height) * cell;
            var pillars = new HashSet<long>();
            foreach (WallRun run in geometry.WallRuns)
            {
                WallPiece(sculpt, style, run.From, run.To);
                Pillar(sculpt, style, run.From, run.To, corner0, cell, pillars);
                Pillar(sculpt, style, run.To, run.From, corner0, cell, pillars);
            }

            Ring(sculpt, style, geometry);
            if (HasCeiling(terrainId))
            {
                Ceiling(sculpt, style, geometry);
            }

            return sculpt.Finish();
        }

        /// <summary>One room lamp in its own frame (y up, origin on the floor), and the height its light sits at.</summary>
        /// <remarks>
        /// Pre: none. Post: a mesh with SubCount sub-meshes; lightHeightM &gt; 0 (under the ceiling for hanging lamps).
        /// Side effects: allocates a Mesh.
        /// </remarks>
        public static Mesh BuildLamp(string terrainId, out float lightHeightM)
        {
            var s = new Sculptor("DungeonLamp_" + terrainId, l => l);
            float3 x = new float3(1f, 0f, 0f), y = new float3(0f, 1f, 0f), z = new float3(0f, 0f, 1f);
            switch (terrainId)
            {
                case "laboratory":
                case "space":
                {
                    // 吸頂燈: a housing hung from the ceiling on a rod, the panel glowing downward.
                    float top = PanelTopM;
                    float hang = terrainId == "space" ? 1.2f : 1.6f;
                    s.Box(TrimSub, new float3(0f, top - hang * 0.5f, 0f), x, y, z, new float3(0.06f, hang * 0.5f, 0.06f), 1f, 1f, 0.8f);
                    s.Box(BodySub, new float3(0f, top - hang - 0.15f, 0f), x, y, z, new float3(0.9f, 0.15f, 0.35f), 1f, 1f, 0.9f);
                    s.Box(GlowSub, new float3(0f, top - hang - 0.33f, 0f), x, y, z, new float3(0.8f, 0.04f, 0.28f), 1f, 1f, 1f);
                    lightHeightM = top - hang - 0.8f;
                    break;
                }
                case "deep_sea":
                {
                    // Glowing coral: branching cones on a rock.
                    s.Box(BodySub, new float3(0f, 0.25f, 0f), x, y, z, new float3(0.7f, 0.35f, 0.6f), 0.7f, 0.7f, 0.8f);
                    for (int k = 0; k < 7; k++)
                    {
                        float a = k * 2.39996f;
                        float3 axis = math.normalize(new float3(math.cos(a) * 0.45f, 1f, math.sin(a) * 0.45f));
                        s.Prism(GlowSub, new float3(math.cos(a) * 0.25f, 0.5f, math.sin(a) * 0.25f), axis,
                            0.12f, 0.02f, 0.9f + 0.12f * (k % 4), 5, 1f);
                    }

                    lightHeightM = 1.4f;
                    break;
                }
                case "cave":
                {
                    // 火盆: tripod, bowl, flame.
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k * math.PI * 2f / 3f;
                        float3 foot = new float3(math.cos(a) * 0.55f, 0f, math.sin(a) * 0.55f);
                        float3 axis = math.normalize(new float3(0f, 1.3f, 0f) - foot);
                        s.Prism(TrimSub, foot, axis, 0.06f, 0.05f, 1.4f, 4, 0.8f);
                    }

                    s.Prism(BodySub, new float3(0f, 1.15f, 0f), y, 0.35f, 0.6f, 0.35f, 8, 0.75f);
                    s.Prism(GlowSub, new float3(0f, 1.45f, 0f), y, 0.42f, 0.02f, 0.9f, 6, 1f);
                    s.Prism(GlowSub, new float3(0.12f, 1.45f, 0.05f), y, 0.22f, 0.01f, 1.25f, 5, 1f);
                    lightHeightM = 2.2f;
                    break;
                }
                case "tower":
                {
                    // 電弧柱: plinth, column, coil rings, orb.
                    s.Box(TrimSub, new float3(0f, 0.2f, 0f), x, y, z, new float3(0.55f, 0.2f, 0.55f), 0.85f, 0.85f, 0.9f);
                    s.Prism(BodySub, new float3(0f, 0.4f, 0f), y, 0.18f, 0.14f, 2.2f, 8, 0.85f);
                    for (int k = 0; k < 3; k++)
                    {
                        s.Prism(TrimSub, new float3(0f, 1.2f + k * 0.45f, 0f), y, 0.34f - k * 0.05f, 0.34f - k * 0.05f, 0.1f, 10, 1f);
                    }

                    s.Prism(GlowSub, new float3(0f, 2.65f, 0f), y, 0.32f, 0.32f, 0.55f, 8, 1f);
                    lightHeightM = 3.1f;
                    break;
                }
                case "temple":
                {
                    // 符文柱: tapered pillar, glowing rune bands and cap.
                    s.Box(BodySub, new float3(0f, 1.7f, 0f), x, y, z, new float3(0.42f, 1.7f, 0.42f), 0.8f, 0.8f, 0.9f);
                    s.Box(GlowSub, new float3(0f, 1.25f, 0f), x, y, z, new float3(0.4f, 0.07f, 0.4f), 1f, 1f, 1f);
                    s.Box(GlowSub, new float3(0f, 2.25f, 0f), x, y, z, new float3(0.37f, 0.07f, 0.37f), 1f, 1f, 1f);
                    s.Box(TrimSub, new float3(0f, 3.5f, 0f), x, y, z, new float3(0.5f, 0.1f, 0.5f), 1f, 1f, 1f);
                    s.Prism(GlowSub, new float3(0f, 3.6f, 0f), y, 0.28f, 0.02f, 0.6f, 4, 1f);
                    lightHeightM = 3.7f;
                    break;
                }
                default:
                {
                    s.Prism(TrimSub, float3.zero, y, 0.1f, 0.08f, 2.6f, 6, 0.85f);
                    s.Prism(GlowSub, new float3(0f, 2.6f, 0f), y, 0.3f, 0.3f, 0.5f, 8, 1f);
                    lightHeightM = 2.85f;
                    break;
                }
            }

            List<Mesh> meshes = s.Finish();
            return meshes[0];
        }

        // ---- walls ----------------------------------------------------------------------------------------------

        private enum Style
        {
            Plain, Lab, DeepSea, Cave, Tower, Space, Temple, Rainforest, Volcano, IceCap, Labyrinth,
        }

        private static Style StyleFor(string terrainId) => terrainId switch
        {
            "laboratory" => Style.Lab,
            "deep_sea" => Style.DeepSea,
            "cave" => Style.Cave,
            "tower" => Style.Tower,
            "space" => Style.Space,
            "temple" => Style.Temple,
            "rainforest" => Style.Rainforest,
            "volcano" => Style.Volcano,
            "ice_cap" => Style.IceCap,
            "labyrinth" => Style.Labyrinth,
            _ => Style.Plain,
        };

        // Half width of the panel face at height h (it leans in from the foot to the top).
        private static float FaceHalf(float h) =>
            PanelFootHalfM + (PanelTopHalfM - PanelFootHalfM) * math.saturate((h + SinkM) / (PanelTopM + SinkM));

        private static void WallPiece(Sculptor s, Style style, float2 from, float2 to)
        {
            float len = math.distance(from, to);
            float2 u2 = (to - from) / len;
            var ex = new float3(u2.x, 0f, u2.y);
            var ez = new float3(-u2.y, 0f, u2.x);
            var ey = new float3(0f, 1f, 0f);
            float2 mid2 = 0.5f * (from + to);
            var mid = new float3(mid2.x, 0f, mid2.y);
            float h = PanelTopM + SinkM;
            s.Box(BodySub, mid + ey * (0.5f * h - SinkM), ex, ey, ez, new float3(0.5f * len, 0.5f * h, PanelFootHalfM),
                1f, PanelTopHalfM / PanelFootHalfM, 1f);
            Coping(s, style, mid, ex, ey, ez, 0.5f * len);
            int modules = math.max(1, (int)math.round(len / ModuleM(style)));
            float step = len / modules;
            for (int m = 0; m < modules; m++)
            {
                float3 c = mid + ex * (-0.5f * len + (m + 0.5f) * step);
                uint hash = Hash(c);
                for (int side = -1; side <= 1; side += 2)
                {
                    Module(s, style, c, ex, ey, ez * side, step, m, hash + (uint)(side + 1));
                }

                if (m > 0)
                {
                    float3 joint = mid + ex * (-0.5f * len + m * step);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Joint(s, style, joint, ex, ey, ez * side, Hash(joint) + (uint)(side + 1));
                    }
                }
            }
        }

        private static float ModuleM(Style style) => style switch
        {
            Style.Cave => 6f,
            Style.Tower => 7f,
            Style.Temple => 6f,
            Style.Volcano => 2.4f,
            Style.Labyrinth => 5f,
            _ => 5f,
        };

        // Along the wall top: what the eye follows over a maze; under a ceiling it meets the ceiling.
        private static void Coping(Sculptor s, Style style, float3 mid, float3 ex, float3 ey, float3 ez, float halfLen)
        {
            switch (style)
            {
                case Style.Rainforest:
                    // Moss and leaf litter lying on the top.
                    s.Box(TrimSub, mid + ey * (PanelTopM + 0.2f), ex, ey, ez, new float3(halfLen, 0.25f, PanelTopHalfM + 0.2f),
                        1f, 0.85f, 0.75f);
                    break;
                case Style.IceCap:
                    s.Box(TrimSub, mid + ey * (PanelTopM + 0.15f), ex, ey, ez, new float3(halfLen, 0.15f, PanelTopHalfM + 0.25f),
                        1f, 1f, 1f);
                    break;
                case Style.Temple:
                case Style.Tower:
                case Style.Labyrinth:
                    s.Box(TrimSub, mid + ey * (PanelTopM + 0.1f), ex, ey, ez, new float3(halfLen, 0.12f, PanelTopHalfM + 0.3f),
                        1f, 1f, 0.95f);
                    break;
            }
        }

        // One module's dressing on one face; ez points out of that face.
        private static void Module(Sculptor s, Style style, float3 c, float3 ex, float3 ey, float3 ez, float len, int index,
            uint hash)
        {
            float3 Face(float h, float out_) => c + ey * h + ez * (FaceHalf(h) + out_);
            switch (style)
            {
                case Style.Lab:
                    // Conduit tray at shoulder height and a status light strip every other panel.
                    s.Box(TrimSub, Face(2.6f, 0.15f), ex, ey, ez, new float3(0.5f * len, 0.18f, 0.18f), 1f, 1f, 0.9f);
                    s.Box(TrimSub, Face(6.2f, 0.08f), ex, ey, ez, new float3(0.5f * len, 0.08f, 0.1f), 1f, 1f, 0.9f);
                    if ((index & 1) == 0)
                    {
                        s.Box(GlowSub, Face(1.3f, 0.05f), ex, ey, ez, new float3(0.6f, 0.07f, 0.06f), 1f, 1f, 1f);
                    }

                    break;
                case Style.DeepSea:
                    // 舷窗: a trimmed round port in every other panel; rock lumps at the foot.
                    if ((index & 1) == 0)
                    {
                        s.Prism(TrimSub, Face(3.6f, -0.05f), ez, 0.85f, 0.85f, 0.25f, 10, 0.85f, ey);
                        s.Prism(GlowSub, Face(3.6f, 0.15f), ez, 0.62f, 0.62f, 0.12f, 10, 1f, ey);
                    }

                    Lump(s, c + ez * (FaceHalf(0f) + 0.2f) + ex * ((hash % 7) / 7f - 0.5f) * len * 0.6f, ex, ey, ez,
                        0.6f + (hash % 5) * 0.12f, hash);
                    break;
                case Style.Cave:
                    // 木樑支架: two posts and a header against the rock, every module.
                    for (int k = -1; k <= 1; k += 2)
                    {
                        float3 foot = c + ex * (k * 0.5f * len * 0.8f) + ez * (FaceHalf(0f) + 0.25f);
                        s.Box(TrimSub, foot + ey * 3.2f, ex, ey, ez, new float3(0.22f, 3.6f, 0.22f), 1f, 1f, 0.85f);
                    }

                    s.Box(TrimSub, Face(6.6f, 0.3f), ex, ey, ez, new float3(0.5f * len * 0.85f, 0.25f, 0.25f), 1f, 1f, 0.8f);
                    Lump(s, c + ez * (FaceHalf(0f) + 0.15f), ex, ey, ez, 0.5f + (hash % 4) * 0.15f, hash);
                    break;
                case Style.Tower:
                    // 拱窗: a lit window with a sill and a pointed head.
                    s.Box(GlowSub, Face(3.4f, 0.02f), ex, ey, ez, new float3(0.6f, 1.1f, 0.05f), 1f, 1f, 1f);
                    s.Prism(GlowSub, Face(4.5f, 0.02f), ey, 0.6f, 0.02f, 0.6f, 4, 1f, ez);
                    s.Box(TrimSub, Face(2.2f, 0.15f), ex, ey, ez, new float3(0.85f, 0.1f, 0.2f), 1f, 1f, 1f);
                    s.Box(TrimSub, Face(0.3f, 0.12f), ex, ey, ez, new float3(0.5f * len, 0.5f, 0.15f), 1f, 1f, 0.9f);
                    break;
                case Style.Space:
                    // Pipe runs and a light band near the top.
                    s.Prism(TrimSub, Face(1.0f, 0.25f) - ex * 0.5f * len, ex, 0.18f, 0.18f, len, 6, 0.8f, ey);
                    s.Prism(TrimSub, Face(5.4f, 0.2f) - ex * 0.5f * len, ex, 0.13f, 0.13f, len, 6, 0.8f, ey);
                    s.Box(GlowSub, Face(7.2f, 0.04f), ex, ey, ez, new float3(0.5f * len * 0.9f, 0.06f, 0.05f), 1f, 1f, 1f);
                    break;
                case Style.Temple:
                    // Relief band and a gold inlay line.
                    s.Box(TrimSub, Face(5.6f, 0.15f), ex, ey, ez, new float3(0.5f * len, 0.4f, 0.18f), 1f, 1f, 1f);
                    for (int k = 0; k < 3; k++)
                    {
                        s.Box(TrimSub, Face(5.6f, 0.38f) + ex * ((k - 1) * len * 0.28f), ex, ey, ez,
                            new float3(0.35f, 0.25f, 0.08f), 1f, 1f, 0.85f);
                    }

                    s.Box(GlowSub, Face(1.1f, 0.03f), ex, ey, ez, new float3(0.5f * len, 0.04f, 0.04f), 1f, 1f, 1f);
                    break;
                case Style.Rainforest:
                    // Hanging vines (trim green-brown) and a fern tuft at the foot.
                    for (int k = 0; k < 3; k++)
                    {
                        float off = ((hash >> (k * 3)) % 7 / 7f - 0.5f) * len * 0.8f;
                        float drop = 2.5f + ((hash >> (k * 2)) % 5) * 0.6f;
                        s.Box(TrimSub, c + ex * off + ey * (PanelTopM - 0.5f * drop) + ez * (FaceHalf(PanelTopM - drop) + 0.1f),
                            ex, ey, ez, new float3(0.08f, 0.5f * drop, 0.08f), 1f, 1f, 0.8f);
                    }

                    s.Prism(GlowSub, c + ez * (FaceHalf(0f) + 0.4f), math.normalize(ey + ez * 0.4f), 0.45f, 0.02f, 1.1f,
                        5, 0.8f);
                    break;
                case Style.Volcano:
                    // Basalt columns of uneven height and a glowing crack at the foot.
                    float rise = 5.5f + (hash % 6) * 0.5f;
                    s.Prism(BodySub, c + ez * (FaceHalf(0f) - 0.1f) - ey * SinkM, ey, 0.55f * len * 0.5f + 0.4f,
                        0.5f * len * 0.5f + 0.35f, rise + SinkM, 6, 0.85f + (hash % 3) * 0.06f);
                    s.Box(GlowSub, Face(0.2f, 0.06f), ex, ey, ez, new float3(0.5f * len, 0.22f, 0.08f), 1f, 1f, 1f);

                    break;
                case Style.IceCap:
                    // 冰晶垛口: merlons on top; an ice spike leaning out every other module.
                    s.Box(TrimSub, c + ey * (PanelTopM + 0.9f) + ez * (PanelTopHalfM - 0.4f), ex, ey, ez,
                        new float3(0.5f * len * 0.45f, 0.75f, 0.4f), 0.9f, 0.9f, 1f);
                    if ((index & 1) == 1)
                    {
                        s.Prism(GlowSub, c + ey * 1.5f + ez * FaceHalf(1.5f), math.normalize(ez + ey * 0.8f), 0.45f, 0.02f,
                            2.2f + (hash % 3) * 0.4f, 5, 1f);
                    }

                    break;
                case Style.Labyrinth:
                    // Recessed carved panel framed in trim; rune dots in a column.
                    s.Box(TrimSub, Face(4f, 0.1f), ex, ey, ez, new float3(0.5f * len * 0.8f, 2.8f, 0.12f), 1f, 1f, 0.95f);
                    s.Box(BodySub, Face(4f, 0.18f), ex, ey, ez, new float3(0.5f * len * 0.65f, 2.4f, 0.08f), 1f, 1f, 0.8f);
                    for (int k = 0; k < 3; k++)
                    {
                        if (((hash >> k) & 1) != 0)
                        {
                            s.Box(GlowSub, Face(2.6f + k * 1.3f, 0.27f), ex, ey, ez, new float3(0.18f, 0.18f, 0.03f), 1f, 1f, 1f);
                        }
                    }

                    break;
            }
        }

        // Between modules: seams, pilasters, ribs, columns, buttresses.
        private static void Joint(Sculptor s, Style style, float3 c, float3 ex, float3 ey, float3 ez, uint hash)
        {
            float mid = 0.5f * (PanelTopM - SinkM);
            float3 face = c + ez * FaceHalf(mid);
            switch (style)
            {
                case Style.Lab:
                    s.Box(TrimSub, face, ex, ey, ez, new float3(0.06f, 0.5f * (PanelTopM + SinkM), 0.05f),
                        1f, 1f, 0.8f);
                    break;
                case Style.Tower:
                    s.Box(TrimSub, face + ez * 0.2f, ex, ey, ez, new float3(0.45f, 0.5f * (PanelTopM + SinkM), 0.3f), 1f, 1f, 1f);
                    break;
                case Style.Space:
                    s.Box(TrimSub, face + ez * 0.3f, ex, ey, ez, new float3(0.3f, 0.5f * (PanelTopM + SinkM), 0.4f), 1f, 0.6f, 0.85f);
                    break;
                case Style.Temple:
                    s.Prism(TrimSub, c + ez * (FaceHalf(0f) + 0.35f) - ey * SinkM, ey, 0.48f, 0.42f, PanelTopM + SinkM, 8, 1f);
                    break;
                case Style.Rainforest:
                    // 板根: a root buttress flaring from the wall into the floor.
                    float3 foot = c + ez * (FaceHalf(0f) + 1.4f);
                    float3 head = c + ey * 5.5f + ez * FaceHalf(5.5f);
                    float3 axis = math.normalize(head - foot);
                    s.Prism(TrimSub, foot - axis * 0.5f, axis, 0.55f, 0.2f, math.distance(foot, head) + 0.5f, 5, 0.85f);
                    break;
                case Style.DeepSea:
                case Style.Cave:
                    if ((hash & 1) == 0)
                    {
                        Lump(s, c + ez * (FaceHalf(0f) + 0.1f), ex, ey, ez, 0.8f, hash);
                    }

                    break;
            }
        }

        private static void Lump(Sculptor s, float3 at, float3 ex, float3 ey, float3 ez, float size, uint hash)
        {
            float tilt = ((hash >> 4) % 9 / 9f - 0.5f) * 0.6f;
            float3 a = math.normalize(ex + ez * tilt);
            float3 b = math.normalize(math.cross(ey, a));
            s.Box(BodySub, at + ey * (size * 0.4f), a, ey, b, new float3(size, size * 0.8f, size * 0.7f), 0.6f, 0.6f,
                0.82f + (hash % 4) * 0.05f);
        }

        // Corner pillars fill the square the two panels leave at a grid corner; door jambs frame a doorway without
        // reaching into it.
        private static void Pillar(Sculptor s, Style style, float2 end, float2 other, float2 corner0, float cell,
            HashSet<long> done)
        {
            float2 g = (end - corner0) / cell;
            bool corner = math.all(math.abs(g - math.round(g)) < 1e-3f);
            long key = ((long)math.round(end.x * 10f) << 32) ^ (long)(int)math.round(end.y * 10f);
            if (corner && !done.Add(key))
            {
                return;
            }

            float2 inward = math.normalize(other - end);
            var ex = new float3(inward.x, 0f, inward.y);
            var ez = new float3(-inward.y, 0f, inward.x);
            var ey = new float3(0f, 1f, 0f);
            float h = PanelTopM + SinkM + (style == Style.Rainforest || style == Style.IceCap ? 0.2f : 0.35f);
            float half = PanelFootHalfM + 0.25f;
            int sub = style == Style.Rainforest || style == Style.Volcano || style == Style.DeepSea || style == Style.Cave
                ? BodySub
                : TrimSub;
            if (corner)
            {
                var c = new float3(end.x, 0.5f * h - SinkM, end.y);
                s.Box(sub, c, ex, ey, ez, new float3(half, 0.5f * h, half), PanelTopHalfM / PanelFootHalfM,
                    PanelTopHalfM / PanelFootHalfM, 0.92f);
            }
            else
            {
                float along = 0.7f;
                var c = new float3(end.x, 0.5f * h - SinkM, end.y) + ex * along;
                s.Box(sub, c, ex, ey, ez, new float3(along, 0.5f * h, half), 1f, PanelTopHalfM / PanelFootHalfM, 0.95f);
            }
        }

        private static void Ring(Sculptor s, Style style, RoomDungeonGeometry g)
        {
            float r = g.ArenaRadiusM;
            float2 c = g.ArenaCenterLocal;
            float2 gate = c + g.ArenaGateAxis * r;
            float gap = RoomDungeonGeometry.ArenaGateOuterM + 0.6f;
            int n = math.max(12, (int)math.ceil(2f * math.PI * r / RingStepM));
            float h = PanelTopM + SinkM;
            float topScale = PanelTopHalfM / PanelFootHalfM;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * 2f * math.PI / n, a1 = (i + 1) * 2f * math.PI / n;
                float2 d0 = new float2(math.cos(a0), math.sin(a0)), d1 = new float2(math.cos(a1), math.sin(a1));
                float2 m = c + math.normalize(d0 + d1) * r;
                if (math.distance(m, gate) < gap)
                {
                    continue;
                }

                var corners = new float3[8];
                for (int k = 0; k < 8; k++)
                {
                    bool top = k >= 4;
                    int q = k & 3;
                    float2 d = q == 0 || q == 3 ? d0 : d1;
                    float off = (q == 0 || q == 1 ? -1f : 1f) * PanelFootHalfM * (top ? topScale : 1f);
                    float2 p = c + d * (r + off);
                    corners[k] = new float3(p.x, top ? PanelTopM : -SinkM, p.y);
                }

                s.Hexa(BodySub, corners, 1f);
                if ((i & 1) == 0)
                {
                    float2 t2 = math.normalize(d1 - d0);
                    var ex = new float3(t2.x, 0f, t2.y);
                    var ey = new float3(0f, 1f, 0f);
                    var outward = new float3(math.normalize(d0 + d1).x, 0f, math.normalize(d0 + d1).y);
                    float3 mid = new float3(m.x, 0f, m.y);
                    // Inside face only: the side the fight is seen from.
                    Module(s, style, mid, ex, ey, -outward, math.distance(d0, d1) * r, i / 2, Hash(mid));
                }
            }

            // Gate jambs either side of the seal.
            float2 axis = g.ArenaGateAxis;
            float2 across = new float2(-axis.y, axis.x);
            for (int k = -1; k <= 1; k += 2)
            {
                float2 jamb = gate + across * (k * gap);
                var ex = new float3(axis.x, 0f, axis.y);
                var ez = new float3(across.x, 0f, across.y);
                s.Box(TrimSub, new float3(jamb.x, 0.5f * (h + 0.6f) - SinkM, jamb.y), ex, new float3(0f, 1f, 0f), ez,
                    new float3(PanelFootHalfM + 0.2f, 0.5f * (h + 0.6f), 0.6f), topScale, 1f, 1f);
            }
        }

        // ---- ceiling --------------------------------------------------------------------------------------------

        private static void Ceiling(Sculptor s, Style style, RoomDungeonGeometry g)
        {
            RoomDungeon d = g.Dungeon;
            float cell = d.CellSizeM;
            float half = g.HalfExtentM + 2f;
            int tiles = (int)math.ceil(2f * half / SegmentM);
            float size = 2f * half / tiles;
            for (int j = 0; j < tiles; j++)
            {
                for (int i = 0; i < tiles; i++)
                {
                    float x0 = -half + i * size, z0 = -half + j * size;
                    s.CeilingTile(new float2(x0, z0), size, PanelTopM);
                }
            }

            for (int y = 0; y < d.Height; y++)
            {
                for (int x = 0; x < d.Width; x++)
                {
                    if (d.RoomAt(x, y) < 0)
                    {
                        continue;
                    }

                    float2 c2 = new float2(x + 0.5f - 0.5f * d.Width, y + 0.5f - 0.5f * d.Height) * cell;
                    CeilingCell(s, style, new float3(c2.x, 0f, c2.y), cell, (uint)(x * 73856093) ^ (uint)(y * 19349663));
                }
            }
        }

        private static void CeilingCell(Sculptor s, Style style, float3 c, float cell, uint hash)
        {
            var ex = new float3(1f, 0f, 0f);
            var ey = new float3(0f, 1f, 0f);
            var ez = new float3(0f, 0f, 1f);
            float top = PanelTopM;
            float span = 0.5f * cell - PanelTopHalfM;
            switch (style)
            {
                case Style.Lab:
                    for (int k = -1; k <= 1; k += 2)
                    {
                        float3 at = c + ey * (top - 0.12f) + ez * (k * cell * 0.18f);
                        s.Box(TrimSub, at, ex, ey, ez, new float3(cell * 0.28f, 0.12f, 0.45f), 1f, 1f, 0.9f);
                        s.Box(GlowSub, at - ey * 0.13f, ex, ey, ez, new float3(cell * 0.26f, 0.02f, 0.35f), 1f, 1f, 1f);
                    }

                    break;
                case Style.Space:
                    for (int k = -1; k <= 1; k++)
                    {
                        s.Box(TrimSub, c + ey * (top - 0.4f) + ex * (k * cell * 0.25f), ex, ey, ez,
                            new float3(0.3f, 0.4f, span), 1f, 1f, 0.85f);
                    }

                    s.Box(GlowSub, c + ey * (top - 0.08f), ex, ey, ez, new float3(span * 0.9f, 0.04f, 0.18f), 1f, 1f, 1f);
                    break;
                case Style.Tower:
                    for (int k = -1; k <= 1; k += 2)
                    {
                        s.Box(TrimSub, c + ey * (top - 0.45f) + ez * (k * cell * 0.22f), ex, ey, ez,
                            new float3(span, 0.45f, 0.35f), 1f, 1f, 0.8f);
                    }

                    break;
                case Style.Temple:
                    for (int k = -1; k <= 1; k += 2)
                    {
                        s.Box(TrimSub, c + ey * (top - 0.35f) + ez * (k * cell * 0.17f), ex, ey, ez,
                            new float3(span, 0.35f, 0.3f), 1f, 1f, 0.9f);
                        s.Box(TrimSub, c + ey * (top - 0.35f) + ex * (k * cell * 0.17f), ex, ey, ez,
                            new float3(0.3f, 0.35f, span), 1f, 1f, 0.9f);
                    }

                    s.Box(GlowSub, c + ey * (top - 0.05f), ex, ey, ez, new float3(cell * 0.12f, 0.03f, cell * 0.12f), 1f, 1f, 1f);
                    break;
                case Style.Cave:
                case Style.DeepSea:
                {
                    // Stalactites; at depth some tips glow.
                    var rng = new Pcg32(hash, 0x44524553UL);
                    int count = 4 + (int)rng.NextUInt(4);
                    for (int k = 0; k < count; k++)
                    {
                        float px = (rng.NextFloat() * 2f - 1f) * (span - 2.5f);
                        float pz = (rng.NextFloat() * 2f - 1f) * (span - 2.5f);
                        float len = 1.2f + rng.NextFloat() * 2f;
                        float rad = 0.35f + rng.NextFloat() * 0.5f;
                        int sub = style == Style.DeepSea && (k & 1) == 0 ? GlowSub : BodySub;
                        s.Prism(sub, c + new float3(px, top + 0.1f, pz), -ey, rad, 0.03f, len, 6, 0.85f);
                    }

                    break;
                }
            }
        }

        private static uint Hash(float3 p)
        {
            uint x = (uint)(int)math.round(p.x * 4f), z = (uint)(int)math.round(p.z * 4f);
            uint h = x * 0x9E3779B1u ^ (z + 0x7F4A7C15u) * 0x85EBCA77u;
            h ^= h >> 15;
            return h * 0x2C1B3C6Du;
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        // ---- mesh writer ----------------------------------------------------------------------------------------

        // Primitives in a flat local frame (x east, y up, z north, metres), each corner sent through `map` — for walls the
        // sphere-and-floor mapping, so long pieces are cut every SegmentM to follow curvature and terrain.
        private sealed class Sculptor
        {
            private readonly string _name;
            private readonly Func<float3, float3> _map;
            private readonly List<Mesh> _meshes = new List<Mesh>();
            private SceneryMeshes.Part[] _parts = NewParts();
            private int _triangles;

            public Sculptor(string name, Func<float3, float3> map)
            {
                _name = name;
                _map = map;
            }

            public void Box(int sub, float3 c, float3 ex, float3 ey, float3 ez, float3 half, float topScaleX,
                float topScaleZ, float tone)
            {
                int pieces = math.max(1, (int)math.ceil(2f * half.x / SegmentM));
                var corners = new float3[8];
                for (int p = 0; p < pieces; p++)
                {
                    float x0 = -half.x + 2f * half.x * p / pieces;
                    float x1 = -half.x + 2f * half.x * (p + 1) / pieces;
                    for (int k = 0; k < 8; k++)
                    {
                        bool top = k >= 4;
                        int q = k & 3;
                        float x = q == 0 || q == 3 ? x0 : x1;
                        float z = q < 2 ? -half.z : half.z;
                        float sx = top ? topScaleX : 1f, sz = top ? topScaleZ : 1f;
                        corners[k] = c + ex * (x * (pieces == 1 ? sx : 1f)) + ez * (z * sz) + ey * (top ? half.y : -half.y);
                    }

                    Hexa(sub, corners, tone);
                }
            }

            // Eight corners: a ring of four at the bottom, then the ring above it in the same order (a convex solid).
            public void Hexa(int sub, float3[] local, float tone)
            {
                SceneryMeshes.Part part = _parts[sub];
                var color = new Color(tone, tone, tone, 1f);
                int first = part.P.Count;
                float3 inside = float3.zero;
                for (int k = 0; k < 8; k++)
                {
                    float3 p = _map(local[k]);
                    part.Add(p, color, float2.zero);
                    inside += p;
                }

                inside /= 8f;
                for (int k = 0; k < 4; k++)
                {
                    int n = (k + 1) & 3;
                    Quad(part, first + k, first + n, first + 4 + n, first + 4 + k, inside);
                }

                Quad(part, first + 4, first + 5, first + 6, first + 7, inside);
                Quad(part, first + 0, first + 1, first + 2, first + 3, inside);
                _triangles += 12;
                FlushIfFull();
            }

            // Regular n-gon prism (or cone when r1 ≈ 0) along `axis` from `baseAt`; `refPerp` orients the first corner.
            public void Prism(int sub, float3 baseAt, float3 axis, float r0, float r1, float length, int sides, float tone,
                float3 refPerp = default)
            {
                SceneryMeshes.Part part = _parts[sub];
                var color = new Color(tone, tone, tone, 1f);
                float3 a = math.normalize(axis);
                float3 p0 = math.lengthsq(refPerp) > 1e-6f ? refPerp : math.abs(a.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
                float3 u = math.normalize(math.cross(a, p0));
                float3 v = math.cross(u, a);
                int first = part.P.Count;
                float3 inside = _map(baseAt + a * (length * 0.5f));
                for (int k = 0; k < sides; k++)
                {
                    float t = k * 2f * math.PI / sides;
                    float3 dir = u * math.cos(t) + v * math.sin(t);
                    part.Add(_map(baseAt + dir * r0), color, float2.zero);
                    part.Add(_map(baseAt + a * length + dir * math.max(r1, 0.005f)), color, float2.zero);
                }

                int bottomCentre = part.Add(_map(baseAt), color, float2.zero);
                int topCentre = part.Add(_map(baseAt + a * length), color, float2.zero);
                for (int k = 0; k < sides; k++)
                {
                    int n = (k + 1) % sides;
                    int b0 = first + 2 * k, t0 = b0 + 1, b1 = first + 2 * n, t1 = b1 + 1;
                    Quad(part, b0, b1, t1, t0, inside);
                    part.Tri(bottomCentre, b0, b1, inside);
                    part.Tri(topCentre, t0, t1, inside);
                }

                _triangles += sides * 4;
                FlushIfFull();
            }

            // One downward-facing ceiling quad, re-sampled at its corners.
            public void CeilingTile(float2 min, float size, float h)
            {
                SceneryMeshes.Part part = _parts[BodySub];
                var color = new Color(0.62f, 0.62f, 0.62f, 1f);
                int a = part.Add(_map(new float3(min.x, h, min.y)), color, float2.zero);
                int b = part.Add(_map(new float3(min.x + size, h, min.y)), color, float2.zero);
                int c = part.Add(_map(new float3(min.x + size, h, min.y + size)), color, float2.zero);
                int d = part.Add(_map(new float3(min.x, h, min.y + size)), color, float2.zero);
                float3 above = _map(new float3(min.x + 0.5f * size, h + 5f, min.y + 0.5f * size));
                Quad(part, a, b, c, d, above);
                _triangles += 2;
                FlushIfFull();
            }

            public List<Mesh> Finish()
            {
                Flush(true);
                return _meshes;
            }

            private static void Quad(SceneryMeshes.Part part, int a, int b, int c, int d, float3 inside)
            {
                part.Tri(a, b, c, inside);
                part.Tri(a, c, d, inside);
            }

            private void FlushIfFull()
            {
                if (_triangles >= FlushTriangles)
                {
                    Flush(false);
                }
            }

            private void Flush(bool always)
            {
                if (_triangles == 0 && !(always && _meshes.Count == 0))
                {
                    return;
                }

                var data = new SceneryMeshes.MeshData(SubCount);
                for (int s = 0; s < SubCount; s++)
                {
                    data.EmitFaceted(_parts[s], s, FacetCos, null);
                }

                _meshes.Add(data.ToMesh(_name + "_" + _meshes.Count));
                _parts = NewParts();
                _triangles = 0;
            }

            private static SceneryMeshes.Part[] NewParts()
            {
                var parts = new SceneryMeshes.Part[SubCount];
                for (int i = 0; i < SubCount; i++)
                {
                    parts[i] = new SceneryMeshes.Part();
                }

                return parts;
            }
        }
    }
}
