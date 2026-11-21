using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>Kind of a wonder (奇觀地標): a large one-off structure meant to be seen from far away.</summary>
    /// <remarks>
    /// REQ: docs/design/planet-world.md#奇觀地標.
    /// Pre: none. Post: stable values (hashed into snapshots and used as mesh stream ids). Side effects: none.
    /// </remarks>
    public enum LandmarkKind
    {
        /// <summary>高塔: a tall tower; Variant is its function (watch, beacon, bell, observatory, signal, pagoda).</summary>
        Tower = 0,
        /// <summary>神廟: a sanctuary on a raised platform.</summary>
        Temple = 1,
        /// <summary>空島: a rock island floating above the ground; never blocks the ground below.</summary>
        SkyIsland = 2,
        /// <summary>太空電梯: an anchor station with a tether rising past the atmosphere.</summary>
        SpaceElevator = 3,
        /// <summary>太空站: the station at the top of the space elevator's tether; never blocks.</summary>
        SpaceStation = 4,
        /// <summary>迷宮: a walled ring compound, the surface mark of a labyrinth.</summary>
        Labyrinth = 5,
        /// <summary>坑道: a mine head (headframe or adit portal).</summary>
        MineShaft = 6,
        /// <summary>地城: a monumental gate over a stair going down.</summary>
        DungeonGate = 7,
        /// <summary>研究室運算中心: server-rack blocks or a ring of cooling towers; entrance of the laboratory terrain.</summary>
        ComputeCenter = 8,
        /// <summary>雨林神木: a buttress-rooted giant tree; entrance of the rainforest terrain.</summary>
        GiantTree = 9,
        /// <summary>深海海溝口: a rimmed rift opening down toward the deep; entrance of the deep-sea terrain.</summary>
        TrenchMouth = 10,
        /// <summary>火山口: a cone with a glowing crater; entrance of the volcano terrain.</summary>
        Crater = 11,
        /// <summary>冰冠城堡: a castle of ice spires; entrance of the ice-cap terrain.</summary>
        IceCastle = 12,
    }

    /// <summary>One wonder placed on the planet.</summary>
    /// <remarks>Pre: produced by LandmarkPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct RunLandmark
    {
        internal RunLandmark(int index, LandmarkKind kind, int variant, int region, float3 direction, float3 facing,
            float footprintRadiusM, float heightM, float altitudeM, bool solid)
        {
            Index = index;
            Kind = kind;
            Variant = variant;
            Region = region;
            Direction = direction;
            Facing = facing;
            FootprintRadiusM = footprintRadiusM;
            HeightM = heightM;
            AltitudeM = altitudeM;
            Solid = solid;
        }

        /// <summary>Index in LandmarkLayout.Landmarks.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Index { get; }

        /// <summary>Kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public LandmarkKind Kind { get; }

        /// <summary>Sub-type within the kind (a tower's function, a temple's plan…).</summary>
        /// <remarks>Pre: none. Post: 0 &lt;= Variant &lt; LandmarkPlanner.VariantCount(Kind). Side effects: none.</remarks>
        public int Variant { get; }

        /// <summary>Region whose site is nearest the landmark.</summary>
        /// <remarks>Pre: none. Post: valid region index of the plan. Side effects: none.</remarks>
        public int Region { get; }

        /// <summary>Centre (unit direction).</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>Unit tangent the entrance faces (toward the region site, where the road runs).</summary>
        /// <remarks>Pre: none. Post: unit length; |dot(Facing, Direction)| &lt; 1e-3. Side effects: none.</remarks>
        public float3 Facing { get; }

        /// <summary>Radius of the ground circle the structure stands on (m); for SkyIsland the island's own radius.</summary>
        /// <remarks>Pre: none. Post: > 0, except SpaceStation (0: it has no ground presence). Side effects: none.</remarks>
        public float FootprintRadiusM { get; }

        /// <summary>Height of the structure above its own base (m).</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public float HeightM { get; }

        /// <summary>Height of the structure's base above the ground (m): 0 for everything that stands on the ground.</summary>
        /// <remarks>Pre: none. Post: >= 0; > 0 only for SkyIsland and SpaceStation. Side effects: none.</remarks>
        public float AltitudeM { get; }

        /// <summary>True when the footprint circle blocks characters, creatures and scatter.</summary>
        /// <remarks>Pre: none. Post: false for SkyIsland and SpaceStation, true otherwise. Side effects: none.</remarks>
        public bool Solid { get; }
    }

    /// <summary>One landmark bound as an advanced terrain dungeon entrance.</summary>
    /// <remarks>Pre: produced by LandmarkPlanner or LobbyPlanetPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct DungeonEntranceSite
    {
        internal DungeonEntranceSite(int landmarkIndex, string terrainId)
        {
            LandmarkIndex = landmarkIndex;
            TerrainId = terrainId;
        }

        /// <summary>Index in <see cref="LandmarkLayout.Landmarks"/>.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int LandmarkIndex { get; }

        /// <summary>Canonical advanced terrain id behind this entrance.</summary>
        /// <remarks>Pre: none. Post: non-empty valid AdvancedTerrains id. Side effects: none.</remarks>
        public string TerrainId { get; }
    }

    /// <summary>The wonders of one planet run.</summary>
    /// <remarks>
    /// Pre: produced by <see cref="LandmarkPlanner.Generate"/> or <see cref="LobbyPlanetPlanner.BuildLandmarks"/>.
    /// Post: immutable; same inputs → same layout.
    /// Side effects: none.
    /// </remarks>
    public sealed class LandmarkLayout
    {
        private readonly RunLandmark[] _landmarks;
        private readonly DungeonEntranceSite[] _entrances;
        private readonly float _radius;

        internal LandmarkLayout(RunLandmark[] landmarks, float radius, int entranceIndex, string entranceTerrainId)
            : this(landmarks, radius,
                entranceIndex >= 0 && !string.IsNullOrEmpty(entranceTerrainId)
                    ? new[] { new DungeonEntranceSite(entranceIndex, entranceTerrainId) }
                    : System.Array.Empty<DungeonEntranceSite>())
        {
        }

        internal LandmarkLayout(RunLandmark[] landmarks, float radius, DungeonEntranceSite[] entrances)
        {
            _landmarks = landmarks;
            _radius = radius;
            _entrances = entrances ?? System.Array.Empty<DungeonEntranceSite>();
            if (_entrances.Length > 0)
            {
                EntranceIndex = _entrances[0].LandmarkIndex;
                EntranceTerrainId = _entrances[0].TerrainId;
            }
            else
            {
                EntranceIndex = -1;
                EntranceTerrainId = null;
            }
        }

        /// <summary>Index of the primary landmark that is a dungeon entrance, or −1 when there is none.</summary>
        /// <remarks>
        /// REQ: planet-world.md#進階地形地城 — one entrance per random planet; fixed lobby planet hosts all 10 in <see cref="Entrances"/>.
        /// Pre: none. Post: −1 iff Entrances is empty; otherwise equals Entrances[0].LandmarkIndex. Side effects: none.
        /// </remarks>
        public int EntranceIndex { get; }

        /// <summary>Canonical advanced terrain id behind the primary entrance, or null when there is none.</summary>
        /// <remarks>Pre: none. Post: null iff Entrances is empty; otherwise equals Entrances[0].TerrainId. Side effects: none.</remarks>
        public string EntranceTerrainId { get; }

        /// <summary>All dungeon entrance bindings on this planet (0–1 on random planets, 10 on the fixed lobby planet).</summary>
        /// <remarks>Pre: none. Post: non-null; each LandmarkIndex indexes <see cref="Landmarks"/>. Side effects: none.</remarks>
        public IReadOnlyList<DungeonEntranceSite> Entrances => _entrances;

        /// <summary>Landmarks in generation order (space elevator and its station first when they were placed).</summary>
        /// <remarks>Pre: none. Post: Landmarks[i].Index == i. Side effects: none.</remarks>
        public IReadOnlyList<RunLandmark> Landmarks => _landmarks;

        /// <summary>True when a body of radius <paramref name="clearanceM"/> centred at <paramref name="direction"/> overlaps a solid landmark.</summary>
        /// <remarks>
        /// Pre: direction finite and non-zero (normalized internally); clearanceM finite and ≥ 0.
        /// Post: true iff the straight-line distance to a Solid landmark's centre ≤ FootprintRadiusM + clearanceM.
        /// Side effects: none.
        /// </remarks>
        public bool IsBlocked(float3 direction, float clearanceM)
        {
            Contract.Requires(math.all(math.isfinite(direction)) && math.lengthsq(direction) > 0f,
                "direction must be finite and non-zero");
            Contract.Requires(math.isfinite(clearanceM) && clearanceM >= 0f, "clearanceM must be finite and >= 0");
            float3 p = math.normalize(direction);
            for (int i = 0; i < _landmarks.Length; i++)
            {
                RunLandmark l = _landmarks[i];
                if (l.Solid && math.distance(p, l.Direction) * _radius <= l.FootprintRadiusM + clearanceM)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
