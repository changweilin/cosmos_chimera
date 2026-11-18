using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Answers "is this ground free of roads and buildings?" for a <see cref="RunLayout"/>, so scatter and other
    /// decoration keep off paved and built ground.
    /// REQ: docs/design/planet-world.md#道路與建築 (P3-11: 散佈避開道路／建築).
    /// </summary>
    /// <remarks>
    /// Pure function of (planet radius, layout, margin): every client gets the same answer for the same direction, so
    /// scatter stays reproducible in co-op. Distances are straight-line in metres to straight road segments: a ≤ 8 m
    /// segment sags ≤ 3 cm below the sphere at R ≥ 320 m, which moves a lateral distance of ≥ 1 m by under a millimetre.
    /// </remarks>
    public sealed class LayoutClearance
    {
        private readonly struct Segment
        {
            public readonly float3 A;
            public readonly float3 B;
            public readonly float HalfWidthM;

            public Segment(float3 a, float3 b, float halfWidthM)
            {
                A = a;
                B = b;
                HalfWidthM = halfWidthM;
            }
        }

        private readonly struct Footprint
        {
            public readonly float3 Center;
            public readonly float3 Facing;
            public readonly float3 Across;
            public readonly float HalfWidthM;
            public readonly float HalfDepthM;

            public Footprint(float3 center, float3 facing, float3 across, float halfWidthM, float halfDepthM)
            {
                Center = center;
                Facing = facing;
                Across = across;
                HalfWidthM = halfWidthM;
                HalfDepthM = halfDepthM;
            }
        }

        // Per-road bounding cap (centre direction + chord radius in metres) for an early reject: a query only walks the
        // segments of roads that can reach it.
        private readonly float3[] _roadCenters;
        private readonly float[] _roadReachM;
        private readonly int[] _roadFirst;
        private readonly Segment[] _segments;
        private readonly Footprint[] _buildings;
        private readonly float3[] _settlementCenters;
        private readonly float[] _settlementRadiiM;
        private readonly float _radius;
        private readonly float _marginM;
        private bool[] _removed;

        /// <summary>Clearance ring added around the farthest building corner of an inhabited settlement (m).</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float SettlementApronM = 4.5f;

        /// <summary>Minimum bounding radius of a non-empty inhabited settlement (m).</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float MinSettlementRadiusM = 12f;

        /// <summary>True when <paramref name="kind"/> is an inhabited settlement (城內); false for Ruins and Lair.</summary>
        /// <remarks>Pre: none. Post: true iff kind is Tribe, Town, City, CityState or Village. Side effects: none.</remarks>
        public static bool IsInhabited(SettlementKind kind) =>
            kind != SettlementKind.Ruins && kind != SettlementKind.Lair;

        /// <summary>Indexes <paramref name="layout"/> on <paramref name="planet"/> with a clearance of <paramref name="marginM"/>.</summary>
        /// <remarks>
        /// Pre: planet valid and the one the layout was generated for; layout non-null; marginM finite and ≥ 0.
        /// Post: IsClear answers for this layout and margin. Side effects: allocates.
        /// </remarks>
        public LayoutClearance(in PlanetConfig planet, RunLayout layout, float marginM)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(layout != null, "layout must not be null");
            Contract.Requires(math.isfinite(marginM) && marginM >= 0f, "marginM must be finite and >= 0");
            _radius = planet.Radius;
            _marginM = marginM;

            int roads = layout.Roads.Count;
            _roadCenters = new float3[roads];
            _roadReachM = new float[roads];
            _roadFirst = new int[roads + 1];
            var segments = new List<Segment>();
            for (int r = 0; r < roads; r++)
            {
                RunRoad road = layout.Roads[r];
                _roadFirst[r] = segments.Count;
                float3 sum = float3.zero;
                foreach (float3 p in road.Points)
                {
                    sum += p;
                }

                float3 center = math.normalize(sum);
                float reach = 0f;
                for (int i = 0; i < road.Points.Count; i++)
                {
                    reach = math.max(reach, math.distance(center, road.Points[i]) * _radius);
                    if (i + 1 < road.Points.Count)
                    {
                        segments.Add(new Segment(road.Points[i], road.Points[i + 1], road.WidthM * 0.5f));
                    }
                }

                _roadCenters[r] = center;
                _roadReachM[r] = reach + road.WidthM * 0.5f;
            }

            _roadFirst[roads] = segments.Count;
            _segments = segments.ToArray();

            _buildings = new Footprint[layout.Buildings.Count];
            for (int i = 0; i < _buildings.Length; i++)
            {
                RunBuilding b = layout.Buildings[i];
                float3 across = math.normalize(math.cross(b.Direction, b.Facing));
                _buildings[i] = new Footprint(b.Direction, b.Facing, across, b.WidthM * 0.5f, b.DepthM * 0.5f);
            }

            _settlementCenters = new float3[layout.Settlements.Count];
            _settlementRadiiM = new float[layout.Settlements.Count];
            for (int s = 0; s < layout.Settlements.Count; s++)
            {
                RunSettlement settlement = layout.Settlements[s];
                _settlementCenters[s] = settlement.Center;
                if (!IsInhabited(settlement.Kind) || settlement.BuildingCount <= 0)
                {
                    continue;
                }

                float reachM = MinSettlementRadiusM;
                int end = settlement.FirstBuilding + settlement.BuildingCount;
                for (int b = settlement.FirstBuilding; b < end && b < layout.Buildings.Count; b++)
                {
                    RunBuilding building = layout.Buildings[b];
                    float distM = math.distance(settlement.Center, building.Direction) * _radius;
                    float halfDiagM = 0.5f * math.sqrt(building.WidthM * building.WidthM + building.DepthM * building.DepthM);
                    reachM = math.max(reachM, distM + halfDiagM + SettlementApronM);
                }

                _settlementRadiiM[s] = reachM;
            }
        }

        /// <summary>True when <paramref name="direction"/> is farther than the margin from every road and building.</summary>
        /// <remarks>
        /// Pre: direction finite and non-zero (normalized internally).
        /// Post: false iff the point lies within (half road width + margin) of a road's centre line, or inside a building
        ///       footprint grown by the margin on every side. Bridge vertices count as road (the deck still needs room).
        /// Side effects: none.
        /// </remarks>
        public bool IsClear(float3 direction)
        {
            Contract.Requires(math.all(math.isfinite(direction)) && math.lengthsq(direction) > 0f,
                "direction must be finite and non-zero");
            float3 p = math.normalize(direction);
            if (InsideBuilding(p, _marginM, false))
            {
                return false;
            }

            for (int r = 0; r < _roadCenters.Length; r++)
            {
                if (math.distance(p, _roadCenters[r]) * _radius > _roadReachM[r] + _marginM)
                {
                    continue;
                }

                for (int s = _roadFirst[r]; s < _roadFirst[r + 1]; s++)
                {
                    Segment seg = _segments[s];
                    if (DistanceToSegment(p, seg.A, seg.B) * _radius <= seg.HalfWidthM + _marginM)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>True when <paramref name="direction"/> lies inside a building footprint grown by <paramref name="marginM"/>.</summary>
        /// <remarks>
        /// REQ: planet-world.md#道路與建築 (建築與角色、宙獸碰撞): a body of radius marginM centred here would overlap a wall.
        /// Pre: direction finite and non-zero (normalized internally); marginM finite and ≥ 0.
        /// Post: independent of the constructor margin and of roads; same footprint test IsClear uses.
        /// Side effects: none.
        /// </remarks>
        public bool IsInsideBuilding(float3 direction, float marginM)
        {
            Contract.Requires(math.all(math.isfinite(direction)) && math.lengthsq(direction) > 0f,
                "direction must be finite and non-zero");
            Contract.Requires(math.isfinite(marginM) && marginM >= 0f, "marginM must be finite and >= 0");
            return InsideBuilding(math.normalize(direction), marginM, true);
        }

        /// <summary>Bounding radius (m) of settlement <paramref name="settlementIndex"/>, or 0 if uninhabited or empty.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= settlementIndex &lt; layout.Settlements.Count.
        /// Post: 0 for Ruins, Lair or empty settlements; &gt;= MinSettlementRadiusM for inhabited settlements with buildings.
        /// Side effects: none.
        /// </remarks>
        public float SettlementRadiusM(int settlementIndex)
        {
            Contract.Requires(settlementIndex >= 0 && settlementIndex < _settlementRadiiM.Length, "settlementIndex out of range");
            return _settlementRadiiM[settlementIndex];
        }

        /// <summary>True when a body of radius <paramref name="marginM"/> at <paramref name="direction"/> overlaps any inhabited settlement.</summary>
        /// <remarks>
        /// REQ: docs/design/planet-world.md#道路與建築, docs/design/creature-ecology.md, docs/design/trainer-battles.md —
        /// wild creatures and trainers never spawn in or enter inhabited settlements.
        /// Pre: direction finite and non-zero (normalized internally); marginM finite and &gt;= 0.
        /// Post: true iff for some inhabited non-empty settlement s, distance(p, s.Center) * R &lt;= SettlementRadiusM(s) + marginM.
        /// Side effects: none.
        /// </remarks>
        public bool IsInsideSettlement(float3 direction, float marginM = 0f)
        {
            Contract.Requires(math.all(math.isfinite(direction)) && math.lengthsq(direction) > 0f,
                "direction must be finite and non-zero");
            Contract.Requires(math.isfinite(marginM) && marginM >= 0f, "marginM must be finite and >= 0");
            float3 p = math.normalize(direction);
            for (int s = 0; s < _settlementCenters.Length; s++)
            {
                float r = _settlementRadiiM[s];
                if (r <= 0f)
                {
                    continue;
                }

                if (math.dot(p, _settlementCenters[s]) > 0f &&
                    math.distance(p, _settlementCenters[s]) * _radius <= r + marginM)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when a body of radius <paramref name="bodyRadiusM"/> at <paramref name="direction"/> stays wholly inside settlement <paramref name="settlementIndex"/>.</summary>
        /// <remarks>
        /// REQ: docs/design/characters.md#路人-npc — settlement NPCs never wander outside their town.
        /// Pre: 0 &lt;= settlementIndex &lt; layout.Settlements.Count; direction finite and non-zero; bodyRadiusM finite and &gt;= 0.
        /// Post: true iff SettlementRadiusM(settlementIndex) &gt; 0 and distance(p, center) * R + bodyRadiusM &lt;= SettlementRadiusM(settlementIndex).
        /// Side effects: none.
        /// </remarks>
        public bool IsContainedInSettlement(int settlementIndex, float3 direction, float bodyRadiusM = 0f)
        {
            Contract.Requires(settlementIndex >= 0 && settlementIndex < _settlementRadiiM.Length, "settlementIndex out of range");
            Contract.Requires(math.all(math.isfinite(direction)) && math.lengthsq(direction) > 0f,
                "direction must be finite and non-zero");
            Contract.Requires(math.isfinite(bodyRadiusM) && bodyRadiusM >= 0f, "bodyRadiusM must be finite and >= 0");
            float r = _settlementRadiiM[settlementIndex];
            if (r <= 0f)
            {
                return false;
            }

            float3 p = math.normalize(direction);
            float3 center = _settlementCenters[settlementIndex];
            return math.dot(p, center) > 0f && math.distance(p, center) * _radius + bodyRadiusM <= r;
        }

        /// <summary>Stops building <paramref name="index"/> from blocking bodies (it was destroyed).</summary>
        /// <remarks>
        /// REQ: docs/design/destructibles.md#碰撞 — a destroyed building's footprint no longer blocks.
        /// Pre: 0 &lt;= index &lt; layout.Buildings.Count.
        /// Post: IsInsideBuilding ignores that footprint; IsClear is unchanged, because scatter placement reads it and
        ///       must not depend on what was destroyed (same seed → same scatter whenever a cell streams in).
        /// Side effects: mutates this index (idempotent).
        /// </remarks>
        public void RemoveBuilding(int index)
        {
            Contract.Requires(index >= 0 && index < _buildings.Length, "index out of range");
            _removed ??= new bool[_buildings.Length];
            _removed[index] = true;
        }

        private bool InsideBuilding(float3 p, float marginM, bool skipRemoved)
        {
            for (int i = 0; i < _buildings.Length; i++)
            {
                if (skipRemoved && _removed != null && _removed[i])
                {
                    continue;
                }

                Footprint f = _buildings[i];
                float3 d = (p - f.Center) * _radius;
                // The tangent-plane test alone also accepts the antipode (p − c is radial there, with zero tangent
                // components), so the hemisphere check is required.
                if (math.abs(math.dot(d, f.Facing)) <= f.HalfDepthM + marginM &&
                    math.abs(math.dot(d, f.Across)) <= f.HalfWidthM + marginM &&
                    math.dot(p, f.Center) > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static float DistanceToSegment(float3 p, float3 a, float3 b)
        {
            float3 ab = b - a;
            float len2 = math.lengthsq(ab);
            float t = len2 > 0f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
            return math.distance(p, a + ab * t);
        }
    }
}
