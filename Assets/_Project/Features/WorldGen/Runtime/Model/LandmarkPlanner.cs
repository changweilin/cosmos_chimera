using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Places the wonders of a run: towers, temples, sky islands, a space elevator with its station, and the surface
    /// marks of labyrinths, mines and dungeons.
    /// REQ: docs/design/planet-world.md#奇觀地標.
    /// </summary>
    /// <remarks>
    /// Determinism: one Pcg32(seed, 'L') stream. A landmark's kind, variant and size are drawn before its site is
    /// searched, and sizes come from integer draws only, so the discrete content (what <see cref="Hash"/> covers)
    /// depends on float math solely through which site attempt is accepted. Decisions read the region graph, the
    /// layout and isBuildable, never terrain height (ADR-0007 rule 3).
    /// The layout is separate from <see cref="RunLayout"/>, so adding wonders does not move a road or a building.
    /// </remarks>
    public static class LandmarkPlanner
    {
        /// <summary>Bump when any output for the same inputs changes (snapshot tests pin it).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 4;

        /// <summary>Free ground kept between a solid landmark's footprint and any road or building (m).</summary>
        /// <remarks>Pre: none. Post: constant > 0. Side effects: none.</remarks>
        public const float LayoutGapM = 3f;

        /// <summary>Free ground kept between a landmark's footprint and any spawn point (m).</summary>
        /// <remarks>Pre: none. Post: constant > 0. Side effects: none.</remarks>
        public const float SpawnGapM = 8f;

        /// <summary>Free ground kept between two landmarks' footprints (m), so each one reads as its own silhouette.</summary>
        /// <remarks>Pre: none. Post: constant > 0. Side effects: none.</remarks>
        public const float LandmarkGapM = 24f;

        private const float LandingGapM = 25f;
        private const int SiteAttempts = 40;
        // The entrance must exist (one per planet), so its site search keeps going far longer than an ordinary wonder's.
        private const int EntranceSiteAttempts = 400;
        private const int RimSamples = 8;

        // Dealt in shuffled order and repeated, so a small planet still shows as many different kinds as it has slots.
        private static readonly LandmarkKind[] GroundKinds =
        {
            LandmarkKind.Tower, LandmarkKind.Temple, LandmarkKind.SkyIsland, LandmarkKind.Labyrinth, LandmarkKind.MineShaft,
            LandmarkKind.DungeonGate, LandmarkKind.ComputeCenter, LandmarkKind.GiantTree, LandmarkKind.TrenchMouth,
            LandmarkKind.Crater, LandmarkKind.IceCastle,
        };

        /// <summary>Number of variants of <paramref name="kind"/>.</summary>
        /// <remarks>Pre: kind defined. Post: ≥ 2. Side effects: none.</remarks>
        public static int VariantCount(LandmarkKind kind)
        {
            switch (kind)
            {
                case LandmarkKind.Tower:
                    return 6;
                case LandmarkKind.Temple:
                case LandmarkKind.SkyIsland:
                    return 3;
                case LandmarkKind.SpaceElevator:
                case LandmarkKind.SpaceStation:
                case LandmarkKind.Labyrinth:
                case LandmarkKind.MineShaft:
                case LandmarkKind.DungeonGate:
                case LandmarkKind.ComputeCenter:
                case LandmarkKind.GiantTree:
                case LandmarkKind.TrenchMouth:
                case LandmarkKind.Crater:
                case LandmarkKind.IceCastle:
                    return 2;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "undefined LandmarkKind");
            }
        }

        /// <summary>Number of ground wonders (everything but the elevator and its station) planned for a planet of <paramref name="radiusM"/>.</summary>
        /// <remarks>
        /// REQ: planet-world.md#奇觀地標 (Q-W41) — 2／2／3／4 for R = 320／640／960／1280 m (asteroid keeps the
        /// earlier 2; dwarf planet / planet / supergiant 2／3／4).
        /// Pre: radiusM > 0. Post: ≥ 2. Side effects: none.
        /// </remarks>
        public static int GroundCountFor(float radiusM)
        {
            Contract.Requires(radiusM > 0f, "radiusM must be > 0");
            return math.max(2, (int)math.round(radiusM / 320f));
        }

        /// <summary>Generates the wonders of <paramref name="plan"/> around the roads and buildings of <paramref name="layout"/>.</summary>
        /// <remarks>
        /// Pre: planet valid and the one plan was generated for; plan non-null; layout non-null and generated from plan;
        ///      isBuildable null (everywhere) or deterministic (false on sea and on ground something else owns).
        /// Post: at most GroundCountFor(R) ground wonders, plus one SpaceElevator on Planet+ (R >= 960 m, or whenever the
        ///       entrance is space) and one
        ///       SpaceStation at 1.25 R when the elevator is placed (above the 1.0 R stratosphere limit). SkyIsland
        ///       wonders stay within &lt;= 1.0 R. A wonder whose site search fails is dropped. For every landmark with a
        ///       footprint: the centre is buildable, and for Solid ones so are 8 points on the footprint rim; a Solid
        ///       footprint keeps LayoutGapM from every road and building; every footprint keeps SpawnGapM from every
        ///       spawn, 25 m from the landing point and LandmarkGapM from every other footprint. Unless the planet is an
        ///       asteroid, DungeonEntrance.TryDraw(plan.Seed, size, plan.Elements) picks the entrance terrain and its
        ///       landmark is placed before every ordinary ground wonder (the elevator itself for space), taking one of the
        ///       GroundCountFor(R) ground slots; EntranceIndex is −1 only if that site search fails. Same inputs → same layout.
        /// Side effects: allocates; calls isBuildable.
        /// </remarks>
        public static LandmarkLayout Generate(in PlanetConfig planet, RunPlan plan, RunLayout layout, Func<float3, bool> isBuildable)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(plan != null, "plan must not be null");
            Contract.Requires(layout != null, "layout must not be null");
            var rng = new Pcg32(plan.Seed, 'L');
            float radius = planet.Radius;
            IReadOnlyList<RunRegion> regions = plan.Regions;

            var deck = (LandmarkKind[])GroundKinds.Clone();
            for (int i = deck.Length - 1; i > 0; i--)
            {
                int j = (int)rng.NextUInt((uint)(i + 1));
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            int ground = GroundCountFor(radius);
            bool hasEntrance = DungeonEntrance.TryDraw(plan.Seed, RunPlanner.SizeForRadius(radius), plan.Elements,
                out AdvancedTerrainSpec entrance);
            LandmarkKind entranceKind = hasEntrance ? DungeonEntrance.KindFor(entrance.Id) : LandmarkKind.DungeonGate;
            bool entranceIsElevator = hasEntrance && entranceKind == LandmarkKind.SpaceElevator;
            bool elevator = radius >= SettlementPlanner.PlanetMinRadiusM || entranceIsElevator;

            // Slot order: elevator (the one wonder that reaches the 1.25 R station), then the entrance, then the deck.
            var slots = new List<LandmarkKind>(ground + 1);
            int entranceSlot = -1;
            if (elevator)
            {
                if (entranceIsElevator)
                {
                    entranceSlot = slots.Count;
                }

                slots.Add(LandmarkKind.SpaceElevator);
            }

            int ordinary = ground;
            if (hasEntrance && !entranceIsElevator)
            {
                entranceSlot = slots.Count;
                slots.Add(entranceKind);
                ordinary--;
            }

            for (int k = 0; k < ordinary; k++)
            {
                slots.Add(deck[k % deck.Length]);
            }

            var placed = new List<RunLandmark>(slots.Count + 1);
            int entranceIndex = -1;
            for (int slot = 0; slot < slots.Count; slot++)
            {
                LandmarkKind kind = slots[slot];
                int variant = (int)rng.NextUInt((uint)VariantCount(kind));
                Size(kind, radius, ref rng, out float footprint, out float height, out float altitude);
                bool solid = kind != LandmarkKind.SkyIsland;
                LayoutClearance clearance = solid ? new LayoutClearance(planet, layout, footprint + LayoutGapM) : null;
                int attempts = slot == entranceSlot ? EntranceSiteAttempts : SiteAttempts;
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    int region = (int)rng.NextUInt((uint)regions.Count);
                    float angle = rng.NextFloat() * 2f * math.PI;
                    float t = rng.NextFloat();
                    float3 site = regions[region].Center;
                    // Region sites carry the road junction and usually a settlement, so start beyond them; the far end
                    // stays inside the region (under half the gap to its nearest neighbour).
                    float near = footprint + 18f;
                    float far = math.max(near + 10f, 0.45f * NeighbourGapM(regions, region, radius));
                    PlanetFrame frame = PlanetFrame.FromUp(site);
                    float3 pos = math.normalize(site + (frame.East * math.cos(angle) + frame.North * math.sin(angle)) *
                        (math.lerp(near, far, t) / radius));
                    if (!Fits(pos, footprint, solid, plan, placed, clearance, isBuildable, radius))
                    {
                        continue;
                    }

                    int owner = Nearest(regions, pos);
                    float3 facing = math.normalize(regions[owner].Center - pos * math.dot(regions[owner].Center, pos));
                    if (slot == entranceSlot)
                    {
                        entranceIndex = placed.Count;
                    }

                    placed.Add(new RunLandmark(placed.Count, kind, variant, owner, pos, facing, footprint, height, altitude, solid));
                    if (kind == LandmarkKind.SpaceElevator)
                    {
                        int stationVariant = (int)rng.NextUInt((uint)VariantCount(LandmarkKind.SpaceStation));
                        placed.Add(new RunLandmark(placed.Count, LandmarkKind.SpaceStation, stationVariant, owner, pos, facing,
                            0f, StationHeightM, height, false));
                    }

                    break;
                }
            }

            return new LandmarkLayout(placed.ToArray(), radius, entranceIndex, entranceIndex >= 0 ? entrance.Id : null);
        }

        /// <summary>Order-sensitive FNV-1a digest of the layout's discrete content (no float directions).</summary>
        /// <remarks>
        /// Pre: layout non-null. Post: same layout → same value; covers count and each landmark's kind, variant, region,
        ///      solidity and sizes in centimetres, and the entrance index.
        /// Side effects: none.
        /// </remarks>
        public static ulong Hash(LandmarkLayout layout)
        {
            Contract.Requires(layout != null, "layout must not be null");
            ulong h = 14695981039346656037UL;
            Mix(ref h, (ulong)layout.Landmarks.Count);
            Mix(ref h, (ulong)(layout.EntranceIndex + 1));
            foreach (RunLandmark l in layout.Landmarks)
            {
                Mix(ref h, (ulong)l.Kind);
                Mix(ref h, (ulong)l.Variant);
                Mix(ref h, (ulong)l.Region);
                Mix(ref h, l.Solid ? 1UL : 0UL);
                Mix(ref h, (ulong)math.round(l.FootprintRadiusM * 100f));
                Mix(ref h, (ulong)math.round(l.HeightM * 100f));
                Mix(ref h, (ulong)math.round(l.AltitudeM * 100f));
            }

            return h;
        }

        private const float StationHeightM = 24f;

        // REQ: planet-world.md#奇觀地標 (尺寸表，暫定). Human scale in metres, except the tether, which is a share of
        // the planet's radius so it always clears the same fraction of the sky.
        private static void Size(LandmarkKind kind, float radius, ref Pcg32 rng, out float footprint, out float height,
            out float altitude)
        {
            altitude = 0f;
            switch (kind)
            {
                case LandmarkKind.Tower:
                    footprint = 5f + 0.5f * rng.NextUInt(5);
                    height = 24f + 4f * rng.NextUInt(10);
                    break;
                case LandmarkKind.Temple:
                    footprint = 12f + rng.NextUInt(5);
                    height = 10f + rng.NextUInt(9);
                    break;
                case LandmarkKind.SkyIsland:
                    footprint = 14f + 2f * rng.NextUInt(5);
                    height = 12f + 2f * rng.NextUInt(5);
                    int minSkyM = (int)math.round(math.min(50f, radius * 0.25f));
                    int maxSkyM = (int)math.floor(radius * 0.90f);
                    altitude = maxSkyM > minSkyM
                        ? minSkyM + rng.NextUInt((uint)(maxSkyM - minSkyM + 1))
                        : minSkyM;
                    break;
                case LandmarkKind.SpaceElevator:
                    footprint = 9f;
                    height = math.round(radius * 1.25f);
                    break;
                case LandmarkKind.Labyrinth:
                    footprint = 14f + rng.NextUInt(5);
                    height = 9f + 2f * rng.NextUInt(3);
                    break;
                case LandmarkKind.MineShaft:
                    footprint = 6f + 0.5f * rng.NextUInt(5);
                    height = 8f + rng.NextUInt(5);
                    break;
                case LandmarkKind.DungeonGate:
                    footprint = 6f + 0.5f * rng.NextUInt(5);
                    height = 7f + rng.NextUInt(4);
                    break;
                case LandmarkKind.ComputeCenter:
                    footprint = 10f + 0.75f * rng.NextUInt(5);
                    height = 14f + 2f * rng.NextUInt(5);
                    break;
                case LandmarkKind.GiantTree:
                    footprint = 8f + 0.75f * rng.NextUInt(5);
                    height = 30f + 3f * rng.NextUInt(6);
                    break;
                case LandmarkKind.TrenchMouth:
                    footprint = 9f + 0.75f * rng.NextUInt(5);
                    height = 4f + rng.NextUInt(4);
                    break;
                case LandmarkKind.Crater:
                    footprint = 14f + rng.NextUInt(5);
                    height = 10f + 1.5f * rng.NextUInt(5);
                    break;
                case LandmarkKind.IceCastle:
                    footprint = 11f + rng.NextUInt(5);
                    height = 18f + 2f * rng.NextUInt(6);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "kind has no ground size");
            }
        }

        private static bool Fits(float3 pos, float footprint, bool solid, RunPlan plan, List<RunLandmark> placed,
            LayoutClearance clearance, Func<float3, bool> isBuildable, float radius)
        {
            if (math.distance(pos, plan.LandingDirection) * radius < footprint + LandingGapM)
            {
                return false;
            }

            foreach (RunLandmark other in placed)
            {
                if (math.distance(pos, other.Direction) * radius < footprint + other.FootprintRadiusM + LandmarkGapM)
                {
                    return false;
                }
            }

            foreach (RunSpawn spawn in plan.Spawns)
            {
                if (math.distance(pos, spawn.Direction) * radius < footprint + SpawnGapM)
                {
                    return false;
                }
            }

            if (clearance != null && !clearance.IsClear(pos))
            {
                return false;
            }

            if (isBuildable == null)
            {
                return true;
            }

            if (!isBuildable(pos))
            {
                return false;
            }

            if (!solid)
            {
                return true;
            }

            PlanetFrame frame = PlanetFrame.FromUp(pos);
            for (int i = 0; i < RimSamples; i++)
            {
                float a = i * (2f * math.PI / RimSamples);
                float3 rim = math.normalize(pos + (frame.East * math.cos(a) + frame.North * math.sin(a)) * (footprint / radius));
                if (!isBuildable(rim))
                {
                    return false;
                }
            }

            return true;
        }

        private static float NeighbourGapM(IReadOnlyList<RunRegion> regions, int region, float radius)
        {
            float best = math.PI;
            float3 c = regions[region].Center;
            foreach (int n in regions[region].Neighbors)
            {
                best = math.min(best, math.acos(math.clamp(math.dot(c, regions[n].Center), -1f, 1f)));
            }

            return best * radius;
        }

        private static int Nearest(IReadOnlyList<RunRegion> regions, float3 pos)
        {
            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < regions.Count; i++)
            {
                float d = math.dot(pos, regions[i].Center);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = i;
                }
            }

            return best;
        }

        private static void Mix(ref ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++)
            {
                h ^= (v >> (8 * i)) & 0xFF;
                h *= 1099511628211UL;
            }
        }
    }
}
