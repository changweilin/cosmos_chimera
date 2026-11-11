using System.Collections.Generic;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>Road class; decides width and whether the road is part of the main route.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum RoadClass
    {
        /// <summary>Main road along RunPlan.MainRoute (landing → boss).</summary>
        Main = 0,
        /// <summary>Branch trail connecting an off-route region to the network.</summary>
        Trail = 1,
    }

    /// <summary>Settlement kind; decides which building kinds it holds.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum SettlementKind
    {
        /// <summary>Camp beside the landing point.</summary>
        Outpost = 0,
        /// <summary>Inhabited cluster on a main-route region.</summary>
        Village = 1,
        /// <summary>Abandoned cluster on an off-route region (聚落遺跡).</summary>
        Ruins = 2,
        /// <summary>Pillar ring around the boss spawn (BOSS 巢穴).</summary>
        Lair = 3,
    }

    /// <summary>Building kind (rule-layer footprint only; art is chosen by the presentation layer).</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum BuildingKind
    {
        /// <summary>Small one-storey tent.</summary>
        Tent = 0,
        /// <summary>Dwelling.</summary>
        House = 1,
        /// <summary>Large communal hall.</summary>
        Hall = 2,
        /// <summary>Narrow multi-storey tower.</summary>
        Tower = 3,
        /// <summary>Free-standing pillar.</summary>
        Pillar = 4,
    }

    /// <summary>One road: a polyline on the unit sphere between two region sites.</summary>
    /// <remarks>Pre: produced by SettlementPlanner. Post: immutable. Side effects: none.</remarks>
    public sealed class RunRoad
    {
        internal RunRoad(RoadClass roadClass, int fromRegion, int toRegion, float widthM, float3[] points, bool[] bridge)
        {
            Class = roadClass;
            FromRegion = fromRegion;
            ToRegion = toRegion;
            WidthM = widthM;
            Points = points;
            Bridge = bridge;
        }

        /// <summary>Road class.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public RoadClass Class { get; }

        /// <summary>Region the road starts at (nearer the landing in graph hops).</summary>
        /// <remarks>Pre: none. Post: valid region index. Side effects: none.</remarks>
        public int FromRegion { get; }

        /// <summary>Region the road ends at; a Gabriel neighbor of FromRegion.</summary>
        /// <remarks>Pre: none. Post: valid region index != FromRegion. Side effects: none.</remarks>
        public int ToRegion { get; }

        /// <summary>Paved width in metres.</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public float WidthM { get; }

        /// <summary>Polyline vertices (unit directions); first = FromRegion site, last = ToRegion site.</summary>
        /// <remarks>Pre: none. Post: >= 2 entries, unit length, consecutive ≤ ~8 m apart at the plan's radius. Side effects: none.</remarks>
        public IReadOnlyList<float3> Points { get; }

        /// <summary>Per vertex: true where the vertex is over sea (the presentation draws a bridge or pier).</summary>
        /// <remarks>Pre: none. Post: Count == Points.Count. Side effects: none.</remarks>
        public IReadOnlyList<bool> Bridge { get; }
    }

    /// <summary>One building footprint.</summary>
    /// <remarks>Pre: produced by SettlementPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct RunBuilding
    {
        internal RunBuilding(int settlement, BuildingKind kind, float3 direction, float3 facing, float widthM, float depthM,
            int storeys)
        {
            Settlement = settlement;
            Kind = kind;
            Direction = direction;
            Facing = facing;
            WidthM = widthM;
            DepthM = depthM;
            Storeys = storeys;
        }

        /// <summary>Index of the owning settlement in RunLayout.Settlements.</summary>
        /// <remarks>Pre: none. Post: valid index. Side effects: none.</remarks>
        public int Settlement { get; }

        /// <summary>Building kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public BuildingKind Kind { get; }

        /// <summary>Footprint centre (unit direction).</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>Unit tangent the entrance faces (toward the road, or the lair centre for pillars).</summary>
        /// <remarks>Pre: none. Post: unit length; |dot(Facing, Direction)| &lt; 1e-3. Side effects: none.</remarks>
        public float3 Facing { get; }

        /// <summary>Footprint width across Facing (m).</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public float WidthM { get; }

        /// <summary>Footprint depth along Facing (m).</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public float DepthM { get; }

        /// <summary>Number of storeys.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int Storeys { get; }
    }

    /// <summary>A cluster of buildings belonging to one region.</summary>
    /// <remarks>Pre: produced by SettlementPlanner. Post: immutable. Side effects: none.</remarks>
    public sealed class RunSettlement
    {
        internal RunSettlement(int index, int region, SettlementKind kind, float3 center, int firstBuilding, int buildingCount)
        {
            Index = index;
            Region = region;
            Kind = kind;
            Center = center;
            FirstBuilding = firstBuilding;
            BuildingCount = buildingCount;
        }

        /// <summary>Index in RunLayout.Settlements.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Index { get; }

        /// <summary>Region the settlement serves.</summary>
        /// <remarks>Pre: none. Post: valid region index. Side effects: none.</remarks>
        public int Region { get; }

        /// <summary>Settlement kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public SettlementKind Kind { get; }

        /// <summary>Anchor point (unit direction) the buildings are laid out around.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Center { get; }

        /// <summary>First index of this settlement's buildings in RunLayout.Buildings.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int FirstBuilding { get; }

        /// <summary>Number of buildings (lots on sea are dropped, so it may be 0).</summary>
        /// <remarks>Pre: none. Post: >= 0; Buildings[FirstBuilding .. +BuildingCount) belong here. Side effects: none.</remarks>
        public int BuildingCount { get; }
    }

    /// <summary>Roads and settlements of one planet run, generated from a RunPlan.</summary>
    /// <remarks>
    /// Pre: produced by <see cref="SettlementPlanner.Generate"/>. Post: immutable; same inputs → same layout.
    /// Side effects: none.
    /// </remarks>
    public sealed class RunLayout
    {
        internal RunLayout(RunRoad[] roads, RunSettlement[] settlements, RunBuilding[] buildings)
        {
            Roads = roads;
            Settlements = settlements;
            Buildings = buildings;
        }

        /// <summary>Main roads in route order, then trails in ascending ToRegion order.</summary>
        /// <remarks>Pre: none. Post: Count == regions − 1 (a spanning tree of the region graph). Side effects: none.</remarks>
        public IReadOnlyList<RunRoad> Roads { get; }

        /// <summary>Settlements: outpost, villages in route order, lair, then ruins in region order.</summary>
        /// <remarks>Pre: none. Post: Settlements[i].Index == i. Side effects: none.</remarks>
        public IReadOnlyList<RunSettlement> Settlements { get; }

        /// <summary>All building footprints, grouped by settlement in settlement order.</summary>
        /// <remarks>Pre: none. Post: every entry's Settlement is valid. Side effects: none.</remarks>
        public IReadOnlyList<RunBuilding> Buildings { get; }
    }
}
