using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>One baked geo-template (tools/geodata output) in memory.</summary>
    /// <remarks>Pre: build with the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class TemplateSource
    {
        /// <summary>Creates a source.</summary>
        /// <remarks>
        /// Pre: size >= 2; heights and water have size² entries (row-major, row 0 = north, column 0 = west);
        ///      0 &lt;= seaNorm &lt; 1; reliefM >= 0 (elev_max_m − max(elev_min_m, 0)).
        /// Post: properties equal the arguments. Side effects: none (arrays are referenced, not copied).
        /// </remarks>
        public TemplateSource(string id, int size, ushort[] heights, byte[] water, float seaNorm, float reliefM)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "id must be non-empty");
            Contract.Requires(size >= 2 && heights != null && water != null &&
                heights.Length == size * size && water.Length == size * size, "heights/water must be size² entries");
            Contract.Requires(seaNorm >= 0f && seaNorm < 1f && reliefM >= 0f, "seaNorm in [0,1), reliefM >= 0");
            Id = id;
            Size = size;
            Heights = heights;
            Water = water;
            SeaNorm = seaNorm;
            ReliefM = reliefM;
        }

        /// <summary>Template id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Grid edge length.</summary>
        /// <remarks>Pre: none. Post: >= 2. Side effects: none.</remarks>
        public int Size { get; }

        /// <summary>Normalized heights (0 ↔ elev_min, 65535 ↔ elev_max).</summary>
        /// <remarks>Pre: none. Post: Size² entries. Side effects: none.</remarks>
        public ushort[] Heights { get; }

        /// <summary>0 land, 1 sea, 2 river.</summary>
        /// <remarks>Pre: none. Post: Size² entries. Side effects: none.</remarks>
        public byte[] Water { get; }

        /// <summary>Normalized height of sea level.</summary>
        /// <remarks>Pre: none. Post: [0,1). Side effects: none.</remarks>
        public float SeaNorm { get; }

        /// <summary>Real relief above sea level (m), used to scale the stamp.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public float ReliefM { get; }
    }

    /// <summary>Where one template is stamped on the sphere.</summary>
    /// <remarks>Pre: none. Post: plain values. Side effects: none.</remarks>
    public readonly struct TemplatePlacement
    {
        /// <summary>Creates a placement.</summary>
        /// <remarks>
        /// Pre: source >= 0; center unit length; 0 &lt; halfAngleRad &lt;= 0.6 (gnomonic distortion stays &lt; 1.5×).
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public TemplatePlacement(int source, float3 center, float rotationRad, float halfAngleRad)
        {
            Contract.Requires(source >= 0, "source index must be >= 0");
            Contract.Requires(math.abs(math.lengthsq(center) - 1f) <= 2e-3f, "center must be unit length");
            Contract.Requires(halfAngleRad > 0f && halfAngleRad <= 0.6f, "halfAngleRad must be in (0, 0.6]");
            Source = source;
            Center = center;
            RotationRad = rotationRad;
            HalfAngleRad = halfAngleRad;
        }

        /// <summary>Index into the source list.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Source { get; }

        /// <summary>Stamp center direction.</summary>
        /// <remarks>Pre: none. Post: unit. Side effects: none.</remarks>
        public float3 Center { get; }

        /// <summary>Rotation of the template about Center (rad).</summary>
        /// <remarks>Pre: none. Post: finite. Side effects: none.</remarks>
        public float RotationRad { get; }

        /// <summary>Angular half extent (rad) from Center to the template's edge midpoints.</summary>
        /// <remarks>Pre: none. Post: (0, 0.6]. Side effects: none.</remarks>
        public float HalfAngleRad { get; }
    }

    /// <summary>Burst-side description of one placed stamp.</summary>
    /// <remarks>Pre: built by PlanetTemplates. Post: blittable. Side effects: none.</remarks>
    public struct TemplateStamp
    {
        /// <summary>Center direction.</summary>
        public float3 Center;
        /// <summary>Template +x (east) tangent at Center.</summary>
        public float3 Right;
        /// <summary>Template +y (north) tangent at Center.</summary>
        public float3 Up;
        /// <summary>tan(half angle): gnomonic plane coordinate of the template edge.</summary>
        public float TanHalf;
        /// <summary>First sample index in the shared arrays.</summary>
        public int Offset;
        /// <summary>Grid edge length.</summary>
        public int Size;
        /// <summary>Planet metres of the template's full land relief.</summary>
        public float Amplitude;
        /// <summary>Normalized sea level.</summary>
        public float SeaNorm;
    }

    /// <summary>Read-only, Burst-compatible view of the placed templates; default == no templates.</summary>
    /// <remarks>
    /// Pre: obtained from <see cref="PlanetTemplates.View"/> (or default). Post: valid while the owner is not disposed.
    /// Side effects: none.
    /// </remarks>
    public readonly struct TemplateView
    {
        // Inside a stamp the template drives the shape; a quarter of the noise keeps small-scale texture so stamps do
        // not look smoother than the procedural land around them.
        private const float NoiseKeep = 0.25f;
        private const float BlendStart = 0.65f;
        private const float SeaDepthFraction = 0.12f;
        private const float RiverDepthM = 1.2f;

        // FENCE: job safety rejects a default (unconstructed) container even when Count == 0 makes it unread, which
        //        broke every terrain job without templates. The buffers are immutable after Create and PlanetRig frees
        //        them only after the terrain completed its jobs, so skipping the safety handle loses no protection.
        //        Verify: remove the attributes and run ChunkMeshTests (schedules ChunkMeshJob with a default view).
        //        Ref: docs/roadmap/demo.md P3-03.
        [NativeDisableContainerSafetyRestriction] private readonly NativeArray<TemplateStamp>.ReadOnly _stamps;
        [NativeDisableContainerSafetyRestriction] private readonly NativeArray<ushort>.ReadOnly _heights;
        [NativeDisableContainerSafetyRestriction] private readonly NativeArray<byte>.ReadOnly _water;
        private readonly int _count;

        internal TemplateView(NativeArray<TemplateStamp> stamps, NativeArray<ushort> heights, NativeArray<byte> water)
        {
            _stamps = stamps.AsReadOnly();
            _heights = heights.AsReadOnly();
            _water = water.AsReadOnly();
            _count = stamps.Length;
        }

        /// <summary>Number of stamps.</summary>
        /// <remarks>Pre: none. Post: >= 0; 0 for default. Side effects: none.</remarks>
        public int Count => _count;

        /// <summary>Height at <paramref name="dir"/> given the procedural height there.</summary>
        /// <remarks>
        /// Pre: dir unit length. Post: equals noiseHeight outside every stamp and where Count == 0; inside a stamp it
        ///      blends (smoothstep over the outer 35 % of the half extent) to template + 0.25·noise, so there is no
        ///      cliff at the seam. |result| &lt;= |noiseHeight| + 1.25 · max Amplitude + 1.2 m.
        /// Side effects: none. Burst-compatible.
        /// </remarks>
        public float Apply(float3 dir, float noiseHeight)
        {
            float h = noiseHeight;
            for (int i = 0; i < _count; i++)
            {
                TemplateStamp s = _stamps[i];
                float c = math.dot(dir, s.Center);
                if (c <= 0.5f)
                {
                    continue; // beyond 60°: outside any stamp (half angle ≤ 0.6 rad ≈ 34°, corners ≤ ~45°)
                }

                // Gnomonic projection onto the tangent plane at Center: great circles stay straight, so real
                // rivers and coastlines keep their shape (planet-world.md: 模板 gnomonic 投影).
                float3 p = dir / c;
                float u = math.dot(p, s.Right) / s.TanHalf;
                float v = math.dot(p, s.Up) / s.TanHalf;
                float m = math.max(math.abs(u), math.abs(v));
                if (m >= 1f)
                {
                    continue;
                }

                float w = 1f - math.smoothstep(BlendStart, 1f, m);
                float tpl = SampleStamp(s, u, v);
                h = math.lerp(h, tpl + noiseHeight * NoiseKeep, w);
            }

            return h;
        }

        /// <summary>Whether <paramref name="dir"/> lies on a stamp's sea (nearest sample).</summary>
        /// <remarks>Pre: dir unit length. Post: false outside every stamp. Side effects: none.</remarks>
        public bool IsSea(float3 dir)
        {
            for (int i = 0; i < _count; i++)
            {
                TemplateStamp s = _stamps[i];
                float c = math.dot(dir, s.Center);
                if (c <= 0.5f)
                {
                    continue;
                }

                float3 p = dir / c;
                float u = math.dot(p, s.Right) / s.TanHalf;
                float v = math.dot(p, s.Up) / s.TanHalf;
                if (math.max(math.abs(u), math.abs(v)) >= 1f)
                {
                    continue;
                }

                int x = (int)math.round((u * 0.5f + 0.5f) * (s.Size - 1));
                int y = (int)math.round((0.5f - v * 0.5f) * (s.Size - 1));
                if (_water[s.Offset + y * s.Size + x] == 1)
                {
                    return true;
                }
            }

            return false;
        }

        private float SampleStamp(in TemplateStamp s, float u, float v)
        {
            float fx = math.clamp((u * 0.5f + 0.5f) * (s.Size - 1), 0f, s.Size - 1.001f);
            float fy = math.clamp((0.5f - v * 0.5f) * (s.Size - 1), 0f, s.Size - 1.001f);
            int x0 = (int)fx;
            int y0 = (int)fy;
            float tx = fx - x0;
            float ty = fy - y0;
            int i00 = s.Offset + y0 * s.Size + x0;
            int i01 = i00 + 1;
            int i10 = i00 + s.Size;
            int i11 = i10 + 1;
            float n = math.lerp(math.lerp(_heights[i00], _heights[i01], tx), math.lerp(_heights[i10], _heights[i11], tx), ty)
                      * (1f / 65535f);
            float sea = math.lerp(math.lerp(Is(i00, 1), Is(i01, 1), tx), math.lerp(Is(i10, 1), Is(i11, 1), tx), ty);
            float river = math.lerp(math.lerp(Is(i00, 2), Is(i01, 2), tx), math.lerp(Is(i10, 2), Is(i11, 2), tx), ty);
            float land = (n - s.SeaNorm) / (1f - s.SeaNorm) * s.Amplitude;
            // Bilinear masks carve smooth basins/channels instead of 1-sample steps at every coastline.
            return land - sea * s.Amplitude * SeaDepthFraction - river * RiverDepthM;
        }

        private float Is(int index, byte value) => _water[index] == value ? 1f : 0f;
    }

    /// <summary>Owns the native buffers of the templates placed on one planet.</summary>
    /// <remarks>
    /// Pre: create with <see cref="Create"/> on the main thread; Dispose after every job using <see cref="View"/> finished.
    /// Post: View valid until Dispose. Side effects: allocates persistent native memory.
    /// REQ: docs/roadmap/demo.md (P3-03 D0), docs/design/planet-world.md#1. 真實世界地圖骨架.
    /// </remarks>
    public sealed class PlanetTemplates : IDisposable
    {
        /// <summary>Full land relief of a template with ≥ 2500 m real relief, as a fraction of R.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float AmplitudeFractionOfRadius = 0.045f;

        private NativeArray<TemplateStamp> _stamps;
        private NativeArray<ushort> _heights;
        private NativeArray<byte> _water;

        private PlanetTemplates(NativeArray<TemplateStamp> stamps, NativeArray<ushort> heights, NativeArray<byte> water,
            float maxAmplitude)
        {
            _stamps = stamps;
            _heights = heights;
            _water = water;
            MaxAbsContribution = 1.25f * maxAmplitude + 1.2f;
        }

        /// <summary>Burst view of the stamps.</summary>
        /// <remarks>Pre: not disposed. Post: Count == placements. Side effects: none.</remarks>
        public TemplateView View => new TemplateView(_stamps, _heights, _water);

        /// <summary>Bound on how far templates move the surface beyond the noise bound (m).</summary>
        /// <remarks>Pre: none. Post: >= 0; |View.Apply(d, n)| &lt;= |n| + this. Side effects: none.</remarks>
        public float MaxAbsContribution { get; }

        /// <summary>Places templates.</summary>
        /// <remarks>
        /// Pre: config valid; sources non-empty; each placement's Source indexes sources.
        /// Post: one stamp per placement; amplitude = 0.045 R · clamp(relief / 2500 m, 0.35, 1).
        /// Side effects: allocates persistent native arrays (Dispose required).
        /// </remarks>
        public static PlanetTemplates Create(in PlanetConfig config, IReadOnlyList<TemplateSource> sources,
            IReadOnlyList<TemplatePlacement> placements)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(sources != null && sources.Count > 0 && placements != null, "sources and placements required");
            int total = 0;
            foreach (TemplatePlacement p in placements)
            {
                Contract.Requires(p.Source < sources.Count, "placement source out of range");
                total += sources[p.Source].Size * sources[p.Source].Size;
            }

            var stamps = new NativeArray<TemplateStamp>(placements.Count, Allocator.Persistent);
            var heights = new NativeArray<ushort>(math.max(total, 1), Allocator.Persistent);
            var water = new NativeArray<byte>(math.max(total, 1), Allocator.Persistent);
            int offset = 0;
            float maxAmp = 0f;
            for (int i = 0; i < placements.Count; i++)
            {
                TemplatePlacement p = placements[i];
                TemplateSource src = sources[p.Source];
                NativeArray<ushort>.Copy(src.Heights, 0, heights, offset, src.Heights.Length);
                NativeArray<byte>.Copy(src.Water, 0, water, offset, src.Water.Length);
                PlanetFrame f = PlanetFrame.FromUp(p.Center);
                // Q-T5 (open): PlanetFrame's East lies to the screen-left of North in Unity's left-handed space, which
                // would mirror real maps. D0 un-mirrors here by using −East as the template's +x; revisit when Q-T5 is
                // decided (if PlanetFrame flips to cross(North, East) == Up, drop the minus).
                float3 east = -f.East;
                float cr = math.cos(p.RotationRad);
                float sr = math.sin(p.RotationRad);
                float amp = config.Radius * AmplitudeFractionOfRadius * math.clamp(src.ReliefM / 2500f, 0.35f, 1f);
                maxAmp = math.max(maxAmp, amp);
                stamps[i] = new TemplateStamp
                {
                    Center = p.Center,
                    Right = east * cr + f.North * sr,
                    Up = f.North * cr - east * sr,
                    TanHalf = math.tan(p.HalfAngleRad),
                    Offset = offset,
                    Size = src.Size,
                    Amplitude = amp,
                    SeaNorm = src.SeaNorm,
                };
                offset += src.Heights.Length;
            }

            return new PlanetTemplates(stamps, heights, water, maxAmp);
        }

        /// <summary>Surface height at <paramref name="dir"/> with templates over <paramref name="noise"/>.</summary>
        /// <remarks>Pre: not disposed; noise valid; dir unit. Post: View.Apply(dir, noise.Sample(dir)). Side effects: none.</remarks>
        public float Sample(in PlanetHeight noise, float3 dir) => View.Apply(math.normalize(dir), noise.Sample(dir));

        /// <summary>Releases native memory.</summary>
        /// <remarks>Pre: no job reads View. Post: View invalid. Side effects: frees memory; idempotent.</remarks>
        public void Dispose()
        {
            if (_stamps.IsCreated)
            {
                _stamps.Dispose();
                _heights.Dispose();
                _water.Dispose();
            }
        }
    }
}
