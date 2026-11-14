using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>A circular wall with one gate (the boss arena, planet-world.md#地表地牢).</summary>
    /// <remarks>Pre: filled by the caller of PlanetWalls.Create. Post: blittable. Side effects: none.</remarks>
    public struct WallRing
    {
        /// <summary>Ring centre (unit direction).</summary>
        public float3 Center;
        /// <summary>Ring radius (m) from Center to the wall centre line.</summary>
        public float RadiusM;
        /// <summary>Centre of the gate (unit direction) on the ring.</summary>
        public float3 Gate;
        /// <summary>Distance (m) from Gate inside which the ring wall is fully removed.</summary>
        public float GateInnerM;
        /// <summary>Distance (m) from Gate at which the ring wall is back to full height; the lock plug has this radius.</summary>
        public float GateOuterM;
    }

    /// <summary>Read-only, Burst-compatible view of a planet's walls; default == no walls.</summary>
    /// <remarks>
    /// Cell walls are ridges along the Voronoi borders of a site set (the dungeon's room walls); corridors are strips
    /// along polylines (roads) where those ridges are removed; the ring is a separate circular wall with one lockable gate.
    /// Pre: obtained from <see cref="PlanetWalls.View"/> (or default). Post: valid while the owner is not disposed.
    /// Side effects: none.
    /// </remarks>
    public readonly struct WallView
    {
        // Rule-layer bodies are stopped where the ridge is already ~1.2 m high (10 % of 12 m): as high as the character
        // can jump, so nothing can stand on a slope the character cannot reach.
        private const float BlockStrength = 0.1f;

        // FENCE: same reason as TemplateView — job safety rejects a default container even when Count == 0 leaves it
        //        unread. Buffers are immutable after Create and PlanetRig frees them after the terrain jobs completed.
        //        Verify: remove the attributes and run ChunkMeshTests (schedules ChunkMeshJob with a default view).
        [NativeDisableContainerSafetyRestriction] private readonly NativeArray<float3>.ReadOnly _sites;
        [NativeDisableContainerSafetyRestriction] private readonly NativeArray<byte>.ReadOnly _open;
        [NativeDisableContainerSafetyRestriction] private readonly NativeArray<float3x2>.ReadOnly _corridors;
        private readonly int _count;
        private readonly float _heightM;
        private readonly float _halfRad;
        private readonly float _topRad;
        private readonly float _corridorInnerRad;
        private readonly float _corridorOuterRad;
        private readonly float3 _ringCenter;
        private readonly float _ringRad;
        private readonly float3 _ringGate;
        private readonly float _ringGateInnerRad;
        private readonly float _ringGateOuterRad;

        internal WallView(NativeArray<float3> sites, NativeArray<byte> open, NativeArray<float3x2> corridors,
            int corridorCount, float heightM, float halfRad, float topRad, float corridorInnerRad, float corridorOuterRad,
            bool hasRing, in WallRing ring, float radius)
        {
            _sites = sites.AsReadOnly();
            _open = open.AsReadOnly();
            _corridors = corridors.GetSubArray(0, corridorCount).AsReadOnly();
            _count = sites.Length;
            _heightM = heightM;
            _halfRad = halfRad;
            _topRad = topRad;
            _corridorInnerRad = corridorInnerRad;
            _corridorOuterRad = corridorOuterRad;
            _ringCenter = ring.Center;
            // Chord of the ring's angular radius: distances below are chords on the unit sphere, not angles.
            // 0 = no ring (also the default view): a bool field would do, but a float keeps the struct trivially blittable.
            _ringRad = hasRing ? 2f * math.sin(0.5f * ring.RadiusM / radius) : 0f;
            _ringGate = ring.Gate;
            _ringGateInnerRad = ring.GateInnerM / radius;
            _ringGateOuterRad = ring.GateOuterM / radius;
        }

        /// <summary>Number of sites (cells).</summary>
        /// <remarks>Pre: none. Post: >= 0; 0 for default. Side effects: none.</remarks>
        public int Count => _count;

        /// <summary>Wall height (m) added to the terrain at <paramref name="dir"/>.</summary>
        /// <remarks>
        /// Pre: dir unit length. Post: 0 ≤ result ≤ wall height; 0 where Count == 0. A cell wall is 0 farther than the
        ///      half width from every walled border and within the corridor inner width of a corridor; the ring wall is 0
        ///      farther than the half width from the ring and within the gate's inner distance.
        /// Side effects: none. Burst-compatible.
        /// </remarks>
        public float Height(float3 dir) => _count > 0 ? _heightM * Strength(dir, 0f) : 0f;

        /// <summary>Whether a body of angular radius <paramref name="clearanceRad"/> at <paramref name="dir"/> is stopped by a wall.</summary>
        /// <remarks>
        /// Pre: dir unit length; clearanceRad finite and ≥ 0.
        /// Post: true where a wall, grown by the clearance, is at least 10 % of its height; when locksClosed and a ring
        ///       exists, also within the ring gate's outer distance grown by the clearance (the plug). False for default.
        /// Side effects: none.
        /// </remarks>
        public bool IsBlocked(float3 dir, float clearanceRad, bool locksClosed)
        {
            if (_count == 0)
            {
                return false;
            }

            if (locksClosed && _ringRad > 0f && math.distance(dir, _ringGate) <= _ringGateOuterRad + clearanceRad)
            {
                return true;
            }

            return Strength(dir, clearanceRad) >= BlockStrength;
        }

        /// <summary>Angular distance (rad) from <paramref name="dir"/> to the nearest cell border, walled or not.</summary>
        /// <remarks>Pre: dir unit length; Count ≥ 2. Post: ≥ 0. Side effects: none.</remarks>
        public float BorderDistance(float3 dir)
        {
            int a = Nearest(dir);
            float3 sa = _sites[a];
            float best = float.MaxValue;
            for (int k = 0; k < _count; k++)
            {
                if (k != a)
                {
                    float3 diff = sa - _sites[k];
                    best = math.min(best, math.dot(dir, diff) / math.length(diff));
                }
            }

            return best;
        }

        private int Nearest(float3 dir)
        {
            int a = 0;
            float da = -2f;
            for (int i = 0; i < _count; i++)
            {
                float d = math.dot(dir, _sites[i]);
                if (d > da) // ascending scan: ties keep the smaller index, as RunPlanner.RegionAt does
                {
                    da = d;
                    a = i;
                }
            }

            return a;
        }

        private float Strength(float3 dir, float growRad)
        {
            float ring = 0f;
            if (_ringRad > 0f)
            {
                float d = math.abs(math.distance(dir, _ringCenter) - _ringRad) - growRad;
                ring = 1f - math.smoothstep(_topRad, _halfRad, d);
                if (ring > 0f)
                {
                    ring *= math.smoothstep(_ringGateInnerRad - growRad, _ringGateOuterRad - growRad,
                        math.distance(dir, _ringGate));
                }
            }

            // The border between cells a and k is the great circle normal to (sa − sk); for a point in cell a,
            // dot(dir, sa − sk) / |sa − sk| is the sine of its angular distance to that circle — no acos, and exact
            // enough at wall scale (≤ 0.05 rad, error < 0.05 %).
            int a = Nearest(dir);
            float3 sa = _sites[a];
            float wall = 0f;
            for (int k = 0; k < _count; k++)
            {
                if (k == a || _open[a * _count + k] != 0)
                {
                    continue;
                }

                float3 diff = sa - _sites[k];
                float d = math.dot(dir, diff) / math.length(diff) - growRad;
                wall = math.max(wall, 1f - math.smoothstep(_topRad, _halfRad, d));
            }

            if (wall <= ring)
            {
                return ring;
            }

            // A corridor removes every cell wall it meets, not only the one between the two rooms its road joins: next to
            // a three-room corner the neighbouring pair's ridge would otherwise stand in the road (measured: 0.8 % of
            // roads with round, per-pair openings).
            float nearest = float.MaxValue;
            for (int c = 0; c < _corridors.Length; c++)
            {
                float3x2 seg = _corridors[c];
                float3 ab = seg.c1 - seg.c0;
                float len2 = math.lengthsq(ab);
                float t = len2 > 0f ? math.saturate(math.dot(dir - seg.c0, ab) / len2) : 0f;
                nearest = math.min(nearest, math.distancesq(dir, seg.c0 + ab * t));
            }

            wall *= math.smoothstep(_corridorInnerRad - growRad, _corridorOuterRad - growRad, math.sqrt(nearest));
            return math.max(wall, ring);
        }
    }

    /// <summary>Owns the native buffers of one planet's walls.</summary>
    /// <remarks>
    /// Pre: create with <see cref="Create"/> on the main thread; Dispose after every job using <see cref="View"/> finished.
    /// Post: View valid until Dispose. Side effects: allocates persistent native memory.
    /// REQ: docs/design/planet-world.md#地表地牢 (Q-W2: 實體地形牆，只在隘口通過).
    /// </remarks>
    public sealed class PlanetWalls : IDisposable
    {
        private NativeArray<float3> _sites;
        private NativeArray<byte> _open;
        private NativeArray<float3x2> _corridors;
        private readonly int _corridorCount;
        private readonly float _radius;
        private readonly float _halfRad;
        private readonly float _topRad;
        private readonly float _corridorInnerRad;
        private readonly float _corridorOuterRad;
        private readonly bool _hasRing;
        private readonly WallRing _ring;

        private PlanetWalls(NativeArray<float3> sites, NativeArray<byte> open, NativeArray<float3x2> corridors,
            int corridorCount, float radius, float heightM, float halfRad, float topRad, float corridorInnerRad,
            float corridorOuterRad, bool hasRing, WallRing ring)
        {
            _sites = sites;
            _open = open;
            _corridors = corridors;
            _corridorCount = corridorCount;
            _radius = radius;
            HeightM = heightM;
            _halfRad = halfRad;
            _topRad = topRad;
            _corridorInnerRad = corridorInnerRad;
            _corridorOuterRad = corridorOuterRad;
            _hasRing = hasRing;
            _ring = ring;
        }

        /// <summary>Burst view of the walls.</summary>
        /// <remarks>Pre: not disposed. Post: Count == sites. Side effects: none.</remarks>
        public WallView View => new WallView(_sites, _open, _corridors, _corridorCount, HeightM, _halfRad, _topRad,
            _corridorInnerRad, _corridorOuterRad, _hasRing, _ring, _radius);

        /// <summary>Full wall height (m): the bound on how far walls raise the surface.</summary>
        /// <remarks>Pre: none. Post: > 0; View.Height ≤ this. Side effects: none.</remarks>
        public float HeightM { get; }

        /// <summary>Builds the walls of a site set.</summary>
        /// <remarks>
        /// Pre: config valid; sites has ≥ 2 unit directions; openPairs has sites² entries, symmetric
        ///      (openPairs[a·n + b] == true ⇒ no wall between cells a and b); corridors non-null, each a segment between
        ///      two unit directions; 0 &lt; corridorInnerM &lt; corridorOuterM; ring null or with RadiusM > halfWidthM,
        ///      a unit Center and Gate, and 0 &lt; GateInnerM &lt; GateOuterM; heightM > 0; 0 &lt; topHalfWidthM &lt; halfWidthM.
        /// Post: every border between cells whose pair is not open carries a ridge heightM tall, flat within
        ///       topHalfWidthM of the border and down to 0 at halfWidthM, removed within corridorInnerM of a corridor
        ///       and back to full height at corridorOuterM. The ring, when given, is a ridge of the same section on its
        ///       circle, removed only around its gate.
        /// Side effects: allocates persistent native arrays (Dispose required).
        /// </remarks>
        public static PlanetWalls Create(in PlanetConfig config, IReadOnlyList<float3> sites, bool[] openPairs,
            IReadOnlyList<float3x2> corridors, float corridorInnerM, float corridorOuterM, WallRing? ring, float heightM,
            float halfWidthM, float topHalfWidthM)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(sites != null && sites.Count >= 2, "need at least two sites");
            int n = sites.Count;
            Contract.Requires(openPairs != null && openPairs.Length == n * n, "openPairs must have sites² entries");
            Contract.Requires(corridors != null, "corridors must not be null");
            Contract.Requires(corridorInnerM > 0f && corridorOuterM > corridorInnerM, "need 0 < corridorInnerM < corridorOuterM");
            Contract.Requires(heightM > 0f && topHalfWidthM > 0f && halfWidthM > topHalfWidthM,
                "need heightM > 0 and 0 < topHalfWidthM < halfWidthM");
            Contract.Requires(!ring.HasValue || (ring.Value.RadiusM > halfWidthM && ring.Value.GateInnerM > 0f &&
                ring.Value.GateOuterM > ring.Value.GateInnerM), "ring needs RadiusM > halfWidthM and 0 < GateInnerM < GateOuterM");
            for (int a = 0; a < n; a++)
            {
                Contract.Requires(math.abs(math.lengthsq(sites[a]) - 1f) <= 2e-3f, "sites must be unit length");
                for (int b = 0; b < n; b++)
                {
                    Contract.Requires(openPairs[a * n + b] == openPairs[b * n + a], "openPairs must be symmetric");
                }
            }

            var nativeSites = new NativeArray<float3>(n, Allocator.Persistent);
            var open = new NativeArray<byte>(n * n, Allocator.Persistent);
            var nativeCorridors = new NativeArray<float3x2>(math.max(corridors.Count, 1), Allocator.Persistent);
            for (int a = 0; a < n; a++)
            {
                nativeSites[a] = sites[a];
            }

            for (int i = 0; i < openPairs.Length; i++)
            {
                open[i] = openPairs[i] ? (byte)1 : (byte)0;
            }

            for (int c = 0; c < corridors.Count; c++)
            {
                nativeCorridors[c] = corridors[c];
            }

            float r = config.Radius;
            return new PlanetWalls(nativeSites, open, nativeCorridors, corridors.Count, r, heightM, halfWidthM / r,
                topHalfWidthM / r, corridorInnerM / r, corridorOuterM / r, ring.HasValue, ring.GetValueOrDefault());
        }

        /// <summary>Releases native memory.</summary>
        /// <remarks>Pre: no job reads View. Post: View invalid. Side effects: frees memory; idempotent.</remarks>
        public void Dispose()
        {
            if (_sites.IsCreated)
            {
                _sites.Dispose();
                _open.Dispose();
                _corridors.Dispose();
            }
        }
    }
}
