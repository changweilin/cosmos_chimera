using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>What a destructible dungeon prop at a wall foot is.</summary>
    /// <remarks>Pre: none. Post: stable values (index the name keys and the body table). Side effects: none.</remarks>
    internal enum DungeonPropKind
    {
        Totem = 0,
        Sacks = 1,
        Crate = 2,
        Barrel = 3,
        MetalCrate = 4,
        Boiler = 5,
        Console = 6,
        OilDrum = 7,
        Statue = 8,
        Rubble = 9,
    }

    /// <summary>One destructible prop standing at a dungeon wall foot, in the geometry's flat local frame.</summary>
    /// <remarks>Pre: built by <see cref="DungeonDressing.PlanProps"/>. Post: immutable. Side effects: none.</remarks>
    internal readonly struct DungeonProp
    {
        public DungeonProp(DungeonPropKind kind, float2 local, float2 facing, float yaw, float scale)
        {
            Kind = kind;
            Local = local;
            Facing = facing;
            Yaw = yaw;
            Scale = scale;
        }

        /// <summary>What it is.</summary>
        public DungeonPropKind Kind { get; }

        /// <summary>Foot centre in RoomDungeonGeometry's local plane (x east, y north, metres).</summary>
        public float2 Local { get; }

        /// <summary>Unit direction out of the wall it stands against (local plane).</summary>
        public float2 Facing { get; }

        /// <summary>Turn about the local up, radians, from facing out of the wall.</summary>
        public float Yaw { get; }

        /// <summary>Uniform size factor on the kind's body.</summary>
        public float Scale { get; }

        /// <summary>Rule-layer body of a kind at scale 1: hit/block radius, height, volume, substance (destructibles.md#物質).</summary>
        /// <remarks>Pre: defined kind. Post: radius &gt; 0, height &gt; 0, volume &gt; 0, substance an env-materials id. Side effects: none.</remarks>
        public static (float radiusM, float heightM, int volumeL, string substance) Body(DungeonPropKind kind) => kind switch
        {
            DungeonPropKind.Totem => (0.6f, 2.9f, 60, "wood"),
            DungeonPropKind.Sacks => (0.6f, 0.85f, 150, "cloth"),
            DungeonPropKind.Crate => (0.55f, 1.4f, 450, "wood"),
            DungeonPropKind.Barrel => (0.38f, 0.9f, 300, "wood"),
            DungeonPropKind.MetalCrate => (0.55f, 0.9f, 500, "metal"),
            DungeonPropKind.Boiler => (0.55f, 1.7f, 900, "metal"),
            DungeonPropKind.Console => (0.5f, 1.05f, 300, "chip"),
            DungeonPropKind.OilDrum => (0.36f, 0.9f, 250, "kerosene"),
            DungeonPropKind.Statue => (0.6f, 2.1f, 700, "stone"),
            _ => (0.75f, 0.5f, 400, "stone"),
        };

        /// <summary>UI copy key of the kind (target HUD line).</summary>
        /// <remarks>Pre: defined kind. Post: ui.object.dungeon_&lt;kind&gt; (Data/Copy/strings.json). Side effects: none.</remarks>
        public static string NameKey(DungeonPropKind kind) => kind switch
        {
            DungeonPropKind.Totem => "ui.object.dungeon_totem",
            DungeonPropKind.Sacks => "ui.object.dungeon_sacks",
            DungeonPropKind.Crate => "ui.object.dungeon_crate",
            DungeonPropKind.Barrel => "ui.object.dungeon_barrel",
            DungeonPropKind.MetalCrate => "ui.object.dungeon_metal_crate",
            DungeonPropKind.Boiler => "ui.object.dungeon_boiler",
            DungeonPropKind.Console => "ui.object.dungeon_console",
            DungeonPropKind.OilDrum => "ui.object.dungeon_oil_drum",
            DungeonPropKind.Statue => "ui.object.dungeon_statue",
            _ => "ui.object.dungeon_rubble",
        };
    }

    // REQ: planet-world.md#地城文化 (牆腳物件可破壞) — the culture's wall-foot props are rule-layer destructibles; the
    // face-mounted props stay part of the wall mesh. Shares 暫定.
    internal static partial class DungeonDressing
    {
        // Share of era-prop faces that also stand something at the wall foot.
        private const float FootPropShare = 0.6f;

        /// <summary>The destructible wall-foot props of one dungeon: the era props of its culture and rubble where it is damaged.</summary>
        /// <remarks>
        /// REQ: planet-world.md#地城文化.
        /// Pre: geometry and terrainId non-null.
        /// Post: same arguments → same list on every client (each module face draws from its own stream, keyed like the
        ///       wall decor, so a face's era and damage match what its wall shows); every prop stands clear of the wall
        ///       panels (its body ≥ PanelFootHalfM from the wall line it faces). Side effects: allocates the list.
        /// </remarks>
        public static List<DungeonProp> PlanProps(RoomDungeonGeometry geometry, string terrainId, in DungeonCulture culture)
        {
            Contract.Requires(geometry != null && terrainId != null, "geometry and terrainId required");
            Style style = StyleFor(terrainId);
            var props = new List<DungeonProp>();
            // Same module walk as WallPiece and Ring, so the hash of each face is the one its decor drew from.
            foreach (WallRun run in geometry.WallRuns)
            {
                float len = math.distance(run.From, run.To);
                float2 u2 = (run.To - run.From) / len;
                var ex = new float3(u2.x, 0f, u2.y);
                var ez = new float3(-u2.y, 0f, u2.x);
                float2 mid2 = 0.5f * (run.From + run.To);
                var mid = new float3(mid2.x, 0f, mid2.y);
                int modules = math.max(1, (int)math.round(len / ModuleM(style)));
                float step = len / modules;
                for (int m = 0; m < modules; m++)
                {
                    float3 c = mid + ex * (-0.5f * len + (m + 0.5f) * step);
                    uint hash = Hash(c);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        FootProps(geometry, style, culture,
                            new Face(c, ex, new float3(0f, 1f, 0f), ez * side, step, FaceOut(style)), hash + (uint)(side + 1), props);
                    }
                }
            }

            float r = geometry.ArenaRadiusM;
            float2 centre = geometry.ArenaCenterLocal;
            float2 gate = centre + geometry.ArenaGateAxis * r;
            float gap = RoomDungeonGeometry.ArenaGateOuterM + 0.6f;
            int n = math.max(12, (int)math.ceil(2f * math.PI * r / RingStepM));
            for (int i = 0; i < n; i += 2)
            {
                float a0 = i * 2f * math.PI / n, a1 = (i + 1) * 2f * math.PI / n;
                float2 d0 = new float2(math.cos(a0), math.sin(a0)), d1 = new float2(math.cos(a1), math.sin(a1));
                float2 m = centre + math.normalize(d0 + d1) * r;
                if (math.distance(m, gate) < gap)
                {
                    continue;
                }

                float2 t2 = math.normalize(d1 - d0);
                float2 o2 = math.normalize(d0 + d1);
                var mid = new float3(m.x, 0f, m.y);
                FootProps(geometry, style, culture, new Face(mid, new float3(t2.x, 0f, t2.y), new float3(0f, 1f, 0f),
                    -new float3(o2.x, 0f, o2.y), math.distance(d0, d1) * r, FaceOut(style)), Hash(mid), props);
            }

            return props;
        }

        private static void FootProps(RoomDungeonGeometry g, Style style, in DungeonCulture k, in Face f, uint hash,
            List<DungeonProp> into)
        {
            FaceStream(k, hash, out FaceRoll roll);
            var rng = new Pcg32(k.Seed ^ (hash * 0xC2B2AE3D27D4EB4Ful), 0x464F4F54ul);
            float footRoll = rng.NextFloat();
            float a = Along(ref rng, f, 0.5f);
            float side = rng.NextFloat() < 0.5f ? -1f : 1f;
            if (roll.EraProp && footRoll < FootPropShare)
            {
                DungeonPropKind kind = roll.Era switch
                {
                    ArchEra.Primal => DungeonPropKind.Totem,
                    ArchEra.Agrarian => DungeonPropKind.Sacks,
                    ArchEra.Mercantile => rng.NextFloat() < 0.6f ? DungeonPropKind.Crate : DungeonPropKind.Barrel,
                    ArchEra.Industrial => DungeonPropKind.MetalCrate,
                    ArchEra.Steampunk => DungeonPropKind.Boiler,
                    ArchEra.Future => DungeonPropKind.Console,
                    ArchEra.Wasteland => DungeonPropKind.OilDrum,
                    _ => DungeonPropKind.Statue,
                };
                Place(g, kind, a, ref rng, f, into);
                // Drums and barrels come in pairs often enough that a lone one reads as chosen, not as a rule.
                if ((kind == DungeonPropKind.OilDrum || kind == DungeonPropKind.Barrel) && rng.NextFloat() < 0.5f)
                {
                    Place(g, kind, a + side * 0.9f, ref rng, f, into);
                }
            }

            if (roll.Damaged)
            {
                Place(g, DungeonPropKind.Rubble, math.clamp(a - side * 1.7f, -0.45f * f.Len, 0.45f * f.Len), ref rng, f, into);
            }
        }

        private static void Place(RoomDungeonGeometry g, DungeonPropKind kind, float along, ref Pcg32 rng, in Face f,
            List<DungeonProp> into)
        {
            float scale = Range(ref rng, 0.85f, 1.15f);
            float yaw = Range(ref rng, -0.45f, 0.45f);
            float radius = DungeonProp.Body(kind).radiusM * scale;
            // Its body clear of the panel face, plus a hand's width so the collider never starts inside the wall.
            float3 foot = f.Foot(along, radius + 0.15f);
            var at = new float2(foot.x, foot.z);
            if (!Fits(g, at, radius))
            {
                return;
            }

            into.Add(new DungeonProp(kind, at, math.normalize(new float2(f.Ez.x, f.Ez.z)), yaw, scale));
        }

        /// <summary>Clearance every prop keeps from any wall line (panel half width plus a margin, plus its own radius).</summary>
        public const float PropWallMarginM = PanelFootHalfM + 0.1f;

        // Inside a room (the outer faces of an indoor grid look onto nothing) and clear of every wall: near a corner the
        // perpendicular wall, a pillar or a door jamb would otherwise swallow it.
        private static bool Fits(RoomDungeonGeometry g, float2 at, float radius)
        {
            if (g.RoomAt(g.LocalToDirection(at)) < 0)
            {
                return false;
            }

            float need = PropWallMarginM + radius;
            foreach (WallRun run in g.WallRuns)
            {
                if (SegmentDistance(at, run.From, run.To) < need)
                {
                    return false;
                }
            }

            return math.abs(math.distance(at, g.ArenaCenterLocal) - g.ArenaRadiusM) >= need;
        }

        private static float SegmentDistance(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-6f));
            return math.distance(p, a + ab * t);
        }

        /// <summary>Mesh of one prop kind at scale 1 in its own frame (y up, +z out of the wall, origin on the floor).</summary>
        /// <remarks>
        /// Pre: defined kind. Post: SubCount sub-meshes (body / trim / glow), bounds within the kind's Body radius and
        /// height (±10 %). Side effects: allocates a Mesh (the caller owns it).
        /// </remarks>
        public static Mesh BuildProp(DungeonPropKind kind)
        {
            var s = new Sculptor("DungeonProp_" + kind, l => l);
            float3 x = new float3(1f, 0f, 0f), y = new float3(0f, 1f, 0f), z = new float3(0f, 0f, 1f);
            Color warm = Rgb(1f, 0.86f, 0.68f);
            Color rust = Rgb(0.9f, 0.62f, 0.45f);
            Color brass = Rgb(1f, 0.82f, 0.48f);
            switch (kind)
            {
                case DungeonPropKind.Totem:
                    s.Prism(TrimSub, float3.zero, y, 0.12f, 0.09f, 2.4f, 6, warm);
                    s.Box(BodySub, new float3(0f, 2.55f, 0f), x, y, z, new float3(0.22f, 0.2f, 0.24f), 0.8f, 0.8f, 0.97f);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        s.Prism(TrimSub, new float3(k * 0.18f, 2.6f, 0f), math.normalize(new float3(k, 0.8f, 0f)), 0.07f, 0.01f,
                            0.45f, 4, 0.95f);
                    }

                    s.Box(TrimSub, new float3(0f, 1.6f, 0f), x, y, z, new float3(0.32f, 0.04f, 0.05f), 1f, 1f, Dim(warm, 0.7f));
                    break;
                case DungeonPropKind.Sacks:
                    s.Box(BodySub, new float3(-0.22f, 0.27f, 0f), x, y, z, new float3(0.3f, 0.27f, 0.25f), 0.7f, 0.7f,
                        Rgb(0.95f, 0.88f, 0.68f));
                    s.Box(BodySub, new float3(0.25f, 0.22f, 0.08f), x, y, z, new float3(0.26f, 0.22f, 0.22f), 0.7f, 0.7f,
                        Rgb(0.9f, 0.82f, 0.62f));
                    s.Box(BodySub, new float3(0.02f, 0.55f, 0.02f), x, y, z, new float3(0.22f, 0.12f, 0.2f), 0.7f, 0.7f,
                        Rgb(0.95f, 0.86f, 0.66f));
                    break;
                case DungeonPropKind.Crate:
                    s.Box(TrimSub, new float3(0f, 0.42f, 0f), x, y, z, new float3(0.42f, 0.42f, 0.42f), 1f, 1f, warm);
                    s.Box(TrimSub, new float3(0f, 0.42f, 0.43f), x, y, z, new float3(0.44f, 0.05f, 0.02f), 1f, 1f, Dim(warm, 0.75f));
                    s.Box(TrimSub, new float3(0.04f, 1.1f, -0.02f), math.normalize(new float3(1f, 0f, 0.3f)), y,
                        math.normalize(new float3(-0.3f, 0f, 1f)), new float3(0.3f, 0.26f, 0.3f), 1f, 1f, Dim(warm, 0.9f));
                    break;
                case DungeonPropKind.Barrel:
                    s.Prism(TrimSub, float3.zero, y, 0.32f, 0.32f, 0.9f, 8, Rgb(0.85f, 0.68f, 0.5f));
                    s.Prism(TrimSub, new float3(0f, 0.2f, 0f), y, 0.34f, 0.34f, 0.06f, 8, 0.5f);
                    s.Prism(TrimSub, new float3(0f, 0.64f, 0f), y, 0.34f, 0.34f, 0.06f, 8, 0.5f);
                    break;
                case DungeonPropKind.MetalCrate:
                    s.Box(TrimSub, new float3(0f, 0.45f, 0f), x, y, z, new float3(0.48f, 0.45f, 0.38f), 1f, 1f, Rgb(0.72f, 0.76f, 0.8f));
                    for (int k = -1; k <= 1; k += 2)
                    {
                        s.Box(TrimSub, new float3(k * 0.36f, 0.45f, 0f), x, y, z, new float3(0.05f, 0.46f, 0.4f), 1f, 1f, 0.5f);
                    }

                    s.Box(GlowSub, new float3(0f, 0.7f, 0.39f), x, y, z, new float3(0.12f, 0.05f, 0.01f), 1f, 1f, 1f);
                    break;
                case DungeonPropKind.Boiler:
                    s.Prism(TrimSub, float3.zero, y, 0.45f, 0.45f, 1.3f, 10, brass);
                    s.Prism(TrimSub, new float3(0f, 1.3f, 0f), y, 0.45f, 0.15f, 0.3f, 10, Dim(brass, 0.85f));
                    s.Prism(TrimSub, new float3(0f, 1.55f, 0f), y, 0.08f, 0.08f, 0.15f, 6, 0.6f);
                    s.Prism(GlowSub, new float3(0f, 0.85f, 0.43f), z, 0.14f, 0.14f, 0.05f, 10, 1f, y);
                    for (int k = 0; k < 2; k++)
                    {
                        s.Prism(TrimSub, new float3(0f, 0.3f + k * 0.7f, 0f), y, 0.48f, 0.48f, 0.06f, 10, 0.55f);
                    }

                    break;
                case DungeonPropKind.Console:
                    s.Box(BodySub, new float3(0f, 0.5f, 0f), x, y, z, new float3(0.42f, 0.5f, 0.32f), 1f, 0.6f, 0.95f);
                    s.Box(GlowSub, new float3(0f, 1.01f, 0f), x, y, z, new float3(0.32f, 0.02f, 0.12f), 1f, 1f, 1f);
                    s.Box(TrimSub, new float3(0f, 0.06f, 0f), x, y, z, new float3(0.45f, 0.06f, 0.35f), 1f, 1f, 0.6f);
                    break;
                case DungeonPropKind.OilDrum:
                    s.Prism(BodySub, float3.zero, y, 0.32f, 0.32f, 0.9f, 8, rust);
                    s.Prism(BodySub, new float3(0f, 0.3f, 0f), y, 0.335f, 0.335f, 0.05f, 8, Dim(rust, 0.7f));
                    s.Prism(BodySub, new float3(0f, 0.6f, 0f), y, 0.335f, 0.335f, 0.05f, 8, Dim(rust, 0.7f));
                    break;
                case DungeonPropKind.Statue:
                {
                    s.Box(TrimSub, new float3(0f, 0.3f, 0f), x, y, z, new float3(0.5f, 0.3f, 0.5f), 0.9f, 0.9f, 0.85f);
                    float3 up = math.normalize(new float3(0.25f, 1f, 0f));
                    float3 side = math.normalize(math.cross(up, z));
                    s.Box(BodySub, new float3(0f, 0.6f, 0f) + up * 0.6f, side, up, z, new float3(0.3f, 0.6f, 0.22f), 0.8f, 0.8f, 0.85f);
                    s.Box(BodySub, new float3(0f, 0.6f, 0f) + up * 1.38f, side, up, z, new float3(0.17f, 0.18f, 0.17f), 0.85f, 0.85f,
                        0.85f);
                    break;
                }
                default:
                    // Rubble: a heap of fallen blocks.
                    for (int k = 0; k < 4; k++)
                    {
                        float t = k * 1.9f;
                        float size = 0.38f - 0.05f * k;
                        Lump(s, new float3(math.cos(t) * 0.35f, 0f, math.sin(t) * 0.3f), x, y, z, size, (uint)(k * 2654435761u));
                    }

                    break;
            }

            return s.Finish()[0];
        }
    }
}
