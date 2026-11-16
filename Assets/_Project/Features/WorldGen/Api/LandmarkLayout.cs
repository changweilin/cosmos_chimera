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

    /// <summary>The wonders of one planet run.</summary>
    /// <remarks>
    /// Pre: produced by <see cref="LandmarkPlanner.Generate"/>. Post: immutable; same inputs → same layout.
    /// Side effects: none.
    /// </remarks>
    public sealed class LandmarkLayout
    {
        private readonly RunLandmark[] _landmarks;
        private readonly float _radius;

        internal LandmarkLayout(RunLandmark[] landmarks, float radius)
        {
            _landmarks = landmarks;
            _radius = radius;
        }

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
