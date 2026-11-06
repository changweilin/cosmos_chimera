using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>Terrain height above sea level as a function of direction only: <c>height(dir)</c>.</summary>
    /// <remarks>
    /// Pre: construct through <see cref="PlanetHeight(ulong, float, float, int)"/> or <see cref="ForPlanet"/>;
    ///      <c>default</c> is invalid.
    /// Post: |Sample(dir)| ≤ <see cref="MaxAbsHeight"/> for every unit dir; same seed + same arguments +
    ///       same <see cref="GeneratorVersion"/> → same heights.
    /// Side effects: none (immutable, blittable value; safe to copy into Burst jobs).
    /// Sampling only the unit direction makes cube-face seams seamless by construction: both faces pass the
    /// bit-identical direction (CubeSphere seam contract) and get the bit-identical height.
    /// P1 placeholder: fBm of 3D simplex noise. P3 adds real-map templates on top (planet-world.md 骨架層).
    /// REQ: docs/architecture/planet-tech.md#幾何：Cube-sphere 四叉樹 (高度)
    /// </remarks>
    public readonly struct PlanetHeight
    {
        /// <summary>Version of the height algorithm; bump whenever the same inputs would produce different heights.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none. REQ: docs/design/multiplayer.md (版本不符拒絕加入).</remarks>
        // v2 (2026-11-06): Sample normalises its input (sol review F2); SampleCore and meshes are unchanged.
        // v3 (2026-11-06): SampleCore normalises too, so queries and meshes evaluate one graph (sol r2 B4).
        public const int GeneratorVersion = 3;

        /// <summary>Largest supported octave count.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxOctaves = 8;

        /// <summary>Largest accepted <see cref="Amplitude"/> in metres.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: docs/reviews/P1-review-r2-sol.md#B2 — float.MaxValue was accepted and made MaxAbsHeight infinite.
        /// 1e5 m is 1500× the default at R = 1280 m and keeps 2·Amplitude and every partial sum finite.
        /// </remarks>
        public const float MaxAmplitude = 1e5f;

        /// <summary>Largest accepted frequency of the highest octave, BaseFrequency · 2^(Octaves − 1).</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: docs/reviews/P1-review-r2-sol.md#B2 — an unbounded frequency overflowed to ∞ inside snoise and the
        /// clamp hid the NaN as a peak. At 1024 the noise domain stays below ≈ 1300 cells, where a float still
        /// resolves 1.2e-4 of a cell; the default top octave is 64.
        /// </remarks>
        public const float MaxTopFrequency = 1024f;

        // Per-octave noise-domain offsets decorrelate octaves and planets. Separate fields rather than an array
        // keep the struct blittable for Burst without pulling Collections into the Api.
        private readonly float3 _o0, _o1, _o2, _o3, _o4, _o5, _o6, _o7;

        /// <summary>Seed the offsets were derived from.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public readonly ulong Seed;

        /// <summary>Amplitude of the first octave in metres.</summary>
        /// <remarks>Pre: none. Post: ≥ 0 for a constructed value. Side effects: none.</remarks>
        public readonly float Amplitude;

        /// <summary>First-octave frequency in noise cycles per unit of direction (independent of radius).</summary>
        /// <remarks>Pre: none. Post: &gt; 0 for a constructed value. Side effects: none.</remarks>
        public readonly float BaseFrequency;

        /// <summary>Number of octaves; each doubles the frequency and halves the amplitude.</summary>
        /// <remarks>Pre: none. Post: within [1, MaxOctaves] for a constructed value. Side effects: none.</remarks>
        public readonly int Octaves;

        /// <summary>Bound on |Sample|: the geometric sum of octave amplitudes, enforced by clamping the output.</summary>
        /// <remarks>
        /// Pre: none. Post: == Amplitude · (2 − 2^(1−Octaves)); finite. Side effects: none.
        /// Not a bound on the raw fBm: snoise slightly exceeds 1 (≈ 1.028 found by sol r2 B3), and Sample clips there.
        /// Feed this into <c>ChunkLodSettings.HeightMargin</c> so displaced terrain is never under-refined.
        /// </remarks>
        public readonly float MaxAbsHeight;

        /// <summary>Creates a height field.</summary>
        /// <remarks>
        /// Pre: 0 ≤ amplitude ≤ MaxAmplitude; 1 ≤ octaves ≤ MaxOctaves;
        ///      0 &lt; baseFrequency and baseFrequency · 2^(octaves − 1) ≤ MaxTopFrequency.
        /// Post: fields equal the arguments; offsets are a pure function of seed.
        /// Side effects: none.
        /// </remarks>
        public PlanetHeight(ulong seed, float amplitude, float baseFrequency, int octaves)
        {
            Contract.Requires(amplitude >= 0f && amplitude <= MaxAmplitude, "amplitude must be within [0, MaxAmplitude]");
            Contract.Requires(octaves >= 1 && octaves <= MaxOctaves, "octaves must be within [1, MaxOctaves]");
            // NaN fails both comparisons, so no separate isfinite check is needed.
            Contract.Requires(baseFrequency > 0f && baseFrequency * (1 << (math.clamp(octaves, 1, MaxOctaves) - 1)) <= MaxTopFrequency,
                "top-octave frequency must be within (0, MaxTopFrequency]");
            Seed = seed;
            Amplitude = amplitude;
            BaseFrequency = baseFrequency;
            Octaves = octaves;
            MaxAbsHeight = amplitude * (2f - 1f / (1 << (octaves - 1)));

            // Stream 0x48 ('H') keeps terrain offsets independent of other generators seeded from the same planet seed.
            var rng = new Pcg32(seed, 0x48UL);
            _o0 = NextOffset(ref rng);
            _o1 = NextOffset(ref rng);
            _o2 = NextOffset(ref rng);
            _o3 = NextOffset(ref rng);
            _o4 = NextOffset(ref rng);
            _o5 = NextOffset(ref rng);
            _o6 = NextOffset(ref rng);
            _o7 = NextOffset(ref rng);
        }

        /// <summary>Default placeholder terrain for a planet: amplitude scales with radius.</summary>
        /// <remarks>
        /// Pre: config.IsValid.
        /// Post: Amplitude == 0.03 · Radius (19.2 m at R = 640); 6 octaves; BaseFrequency 2.
        /// Side effects: none.
        /// REQ: CLAUDE.md#硬性規則 — sizes derive from PlanetConfig.Radius. Values are a P1 placeholder for the
        /// vertical slice, not tuned art direction.
        /// </remarks>
        public static PlanetHeight ForPlanet(in PlanetConfig config, ulong seed)
        {
            Contract.Requires(config.IsValid, "PlanetConfig is default/uninitialised");
            return new PlanetHeight(seed, 0.03f * config.Radius, 2f, 6);
        }

        /// <summary>True for a constructed value.</summary>
        /// <remarks>Pre: none. Post: result == (Octaves ≥ 1). Side effects: none.</remarks>
        public bool IsValid => Octaves >= 1;

        /// <summary>Height in metres above sea level at <paramref name="direction"/>.</summary>
        /// <remarks>
        /// Pre: IsValid; direction finite and unit length (±1e-3).
        /// Post: |result| ≤ MaxAbsHeight; equals the field at normalize(direction) (the tolerance is for the caller's
        ///       input, not a 0.1 % shift of the sample point — same rule as PlanetFrame.FromUp, sol review F2).
        /// Side effects: none.
        /// </remarks>
        public float Sample(float3 direction)
        {
            Contract.Requires(IsValid, "PlanetHeight is default/uninitialised");
            Contract.Requires(math.all(math.isfinite(direction)) && math.abs(math.lengthsq(direction) - 1f) <= 2e-3f,
                "direction must be a finite unit vector");
            return SampleCore(direction);
        }

        // Burst entry point (ADR-0006): no Contract calls. Callers validate this value once before scheduling.
        // Normalises here rather than in Sample so the public query and the mesh evaluate the same operation graph
        // (sol r2 B4: they differed by up to 1 mm on 1202/4096 directions). Mesh directions are already unit, so the
        // extra normalize only re-rounds them; seams stay bit-identical because both faces pass the same bits.
        internal float SampleCore(float3 direction)
        {
            direction = math.normalize(direction);
            float sum = 0f;
            float amp = Amplitude;
            float freq = BaseFrequency;
            for (int i = 0; i < Octaves; i++)
            {
                sum += amp * noise.snoise(direction * freq + Offset(i));
                amp *= 0.5f;
                freq *= 2f;
            }

            // snoise is only approximately bounded by 1; the clamp turns the documented bound into a guarantee
            // (it rarely engages, so it does not visibly flatten peaks).
            return math.clamp(sum, -MaxAbsHeight, MaxAbsHeight);
        }

        private float3 Offset(int i)
        {
            switch (i)
            {
                case 0: return _o0;
                case 1: return _o1;
                case 2: return _o2;
                case 3: return _o3;
                case 4: return _o4;
                case 5: return _o5;
                case 6: return _o6;
                default: return _o7;
            }
        }

        // ±256 noise cells: far enough apart to decorrelate, small enough that snoise's mod-289 lattice keeps
        // full float precision (offsets near 1e4 would quantise the domain at 1e-3).
        private static float3 NextOffset(ref Pcg32 rng)
        {
            float x = rng.NextFloat() * 512f - 256f;
            float y = rng.NextFloat() * 512f - 256f;
            float z = rng.NextFloat() * 512f - 256f;
            return new float3(x, y, z);
        }
    }
}
