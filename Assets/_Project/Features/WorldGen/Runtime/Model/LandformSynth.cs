using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Synthesizes the landform stamp grids that replaced the real-world geo-templates (user 2026-11-11: 地貌全部隨機生成).
    /// REQ: docs/design/planet-world.md#1-骨架層程序化地貌.
    /// </summary>
    /// <remarks>
    /// Integer-only on purpose: the water mask feeds IsSea (spawn/landing land checks, ADR-0007 seaCount), which must be
    /// identical on every client. Float noise here would let Mono and IL2CPP disagree on a coastline sample; integer hash
    /// noise in Q16 fixed point cannot. The only doubles are literal constants converted once (IEEE exact).
    /// Grid coordinates use half-cells: px = 2x − 255, py = 255 − 2y (row 0 = north), so the centre is (0, 0), edge
    /// midpoints sit at ±255 and every value stays an integer.
    /// </remarks>
    public static class LandformSynth
    {
        /// <summary>Bump whenever the same (kind, seed, index) would produce different grids.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        /// <summary>Grid edge length of every synthesized stamp.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// Same as the retired DEM bake (256), so stamp detail per metre is unchanged (≈ 2.3 m/sample at R = 640).
        /// </remarks>
        public const int Size = 256;

        /// <summary>Largest stamp index accepted by <see cref="Generate"/> (TemplatePlanner places at most 8).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxIndex = 63;

        private const int One = 65536;
        private const int Max = 65535;
        // Water and rivers only inside Chebyshev 210/255 ≈ 0.82 of the half extent: beyond it the stamp fades into the
        // procedural noise (TemplateView blends over the outer 35 %), so a mask there would mark noise land as sea.
        private const int WaterLimit = 210;
        private const int RiverHalfWidth = 4;

        // cos/sin of k · 22.5° scaled by 1024: integer rotations without trig.
        private static readonly int[] Cos16 = { 1024, 946, 724, 392, 0, -392, -724, -946, -1024, -946, -724, -392, 0, 392, 724, 946 };
        private static readonly int[] Sin16 = { 0, 392, 724, 946, 1024, 946, 724, 392, 0, -392, -724, -946, -1024, -946, -724, -392 };

        // REQ: Data/schemas/planet-type.schema.json terrain tags — one generator per tag.
        private static readonly string[] KindIds =
        {
            "forest", "river_valley", "volcano", "rift_valley", "ice_field", "fjord",
            "plateau", "archipelago", "canyon", "cave", "eternal_day", "eternal_night",
        };

        /// <summary>Landform kinds (terrain tags) this synthesizer can build, in a fixed order.</summary>
        /// <remarks>Pre: none. Post: 12 distinct ids, same order every call. Side effects: none.</remarks>
        public static IReadOnlyList<string> Kinds => KindIds;

        /// <summary>Builds one stamp grid of landform <paramref name="kind"/>.</summary>
        /// <remarks>
        /// Pre: kind is one of <see cref="Kinds"/>; 0 &lt;= index &lt;= MaxIndex.
        /// Post: Size × Size grid; same (kind, seed, index, GeneratorVersion) → bit-identical arrays on every backend;
        ///       water 1 (sea) only where height &lt; SeaNorm · 65536; water 2 (river) only where height >= that; no water
        ///       outside Chebyshev 210/255 of the centre; kinds with SeaNorm 0 have no sea.
        /// Side effects: allocates the arrays.
        /// </remarks>
        public static TemplateSource Generate(string kind, ulong seed, int index)
        {
            int k = System.Array.IndexOf(KindIds, kind);
            Contract.Requires(k >= 0, "kind must be one of LandformSynth.Kinds");
            Contract.Requires(index >= 0 && index <= MaxIndex, "index must be within [0, MaxIndex]");

            // Own stream per stamp ('L','F' + index) so stamps differ and never shift TemplatePlanner's 'T' sequence.
            var rng = new Pcg32(seed, 0x4C460000UL + (ulong)index);
            var p = new Params(ref rng);
            Recipe r = RecipeFor(kind);
            var heights = new ushort[Size * Size];
            var water = new byte[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int px = 2 * x - (Size - 1);
                    int py = (Size - 1) - 2 * y;
                    int h = Height(kind, px, py, in p, r, out bool river);
                    // Lift sea floor to just above sea level toward the rim (radius 175 → 205 half-cells), so seas close
                    // with a rounded shore inside WaterLimit instead of being cut off by its square.
                    int rim = Smooth(175, 205, ISqrt((long)px * px + (long)py * py));
                    int shore = r.SeaQ + Q(0.02);
                    if (h < shore)
                    {
                        h += Mul(shore - h, rim);
                    }

                    h = Clamp(h, 0, Max);
                    int cheb = System.Math.Max(System.Math.Abs(px), System.Math.Abs(py));
                    byte w = 0;
                    if (cheb <= WaterLimit)
                    {
                        if (h < r.SeaQ)
                        {
                            w = 1;
                        }
                        else if (river)
                        {
                            w = 2;
                        }
                    }

                    heights[y * Size + x] = (ushort)h;
                    water[y * Size + x] = w;
                }
            }

            return new TemplateSource(kind + "#" + index, Size, heights, water, r.SeaQ / (float)One, r.ReliefM);
        }

        private readonly struct Recipe
        {
            public readonly int SeaQ;
            public readonly float ReliefM;

            public Recipe(double sea, float reliefM)
            {
                SeaQ = Q(sea);
                ReliefM = reliefM;
            }
        }

        // ReliefM sets amplitude through PlanetTemplates (0.045 R · clamp(relief / 2500, 0.35, 1)); values echo the
        // retired real templates of the same tag so planets keep their former vertical character.
        private static Recipe RecipeFor(string kind)
        {
            switch (kind)
            {
                case "forest": return new Recipe(0.0, 1300f);
                case "river_valley": return new Recipe(0.0, 2600f);
                case "volcano": return new Recipe(0.0, 3000f);
                case "rift_valley": return new Recipe(0.09, 1500f);
                case "ice_field": return new Recipe(0.0, 1800f);
                case "fjord": return new Recipe(0.10, 1700f);
                case "plateau": return new Recipe(0.0, 2400f);
                case "archipelago": return new Recipe(0.55, 400f);
                case "canyon": return new Recipe(0.0, 2100f);
                case "cave": return new Recipe(0.0, 1000f);
                case "eternal_day": return new Recipe(0.20, 1100f);
                default: return new Recipe(0.20, 1300f); // eternal_night
            }
        }

        // Every random choice of a stamp, drawn up front in a fixed order so recipes cannot shift each other's stream.
        private readonly struct Params
        {
            public readonly uint Salt;
            public readonly int Dir;
            public readonly int Dir2;
            public readonly int Dir3;
            public readonly int Peaks;
            public readonly int CraterR;

            public Params(ref Pcg32 rng)
            {
                Salt = rng.NextUInt();
                Dir = (int)rng.NextUInt(16);
                // Fjord inlets: three distinct headings at least 3 steps (67.5°) apart.
                Dir2 = (Dir + 5 + (int)rng.NextUInt(3)) & 15;
                Dir3 = (Dir + 11 + (int)rng.NextUInt(2)) & 15;
                Peaks = 3 + (int)rng.NextUInt(3);
                CraterR = 20 + (int)rng.NextUInt(12);
            }
        }

        private static int Height(string kind, int px, int py, in Params p, Recipe r, out bool river)
        {
            river = false;
            int u = (px * Cos16[p.Dir] + py * Sin16[p.Dir]) >> 10;
            int v = (-px * Sin16[p.Dir] + py * Cos16[p.Dir]) >> 10;
            int rad = ISqrt((long)px * px + (long)py * py);
            int n1 = Fbm(p.Salt, px, py, 6, 4);
            int n2 = Fbm(p.Salt ^ 0xA511E9B3u, px, py, 3, 3);
            int wobble = (n1 - 32768) >> 9; // ±64 half-cells
            switch (kind)
            {
                case "volcano":
                {
                    int rn = rad + (wobble >> 1);
                    int cone = One - Smooth(0, 215, rn);
                    int h = Q(0.04) + Mul(Q(0.80), (cone + Mul(cone, cone)) >> 1) + Mul(Q(0.06), n1) + Mul(Q(0.05), n2);
                    if (rn < p.CraterR)
                    {
                        // Crater floor sits ~15 % below the rim; linear wall is steep enough to read as a caldera.
                        h -= Mul(Q(0.15), (p.CraterR - rn) * One / p.CraterR);
                    }

                    return h;
                }
                case "rift_valley":
                {
                    int a = System.Math.Abs(u + (wobble >> 1));
                    int h = Q(0.12) + Mul(Q(0.73), Smooth(55, 95, a)) - Mul(Q(0.30), Smooth(95, 240, a)) + Mul(Q(0.08), n2);
                    if (a < 45 && n1 < Q(0.42))
                    {
                        h = r.SeaQ - Q(0.04); // rift lakes
                    }

                    return h;
                }
                case "forest":
                {
                    int across = System.Math.Abs(v - Meander(p.Salt, u, 90));
                    river = across < RiverHalfWidth;
                    int dome = One - Smooth(120, 255, rad);
                    return Q(0.12) + Mul(Q(0.50), n1) + Mul(Q(0.15), n2) + Mul(Q(0.15), dome)
                           - Mul(Q(0.20), One - Smooth(0, 50, across));
                }
                case "river_valley":
                {
                    int across = System.Math.Abs(v - Meander(p.Salt, u, 70));
                    river = across < RiverHalfWidth + 1;
                    return Q(0.06) + Mul(Q(0.75), Smooth(10, 180, across)) + Mul(Q(0.12), n1) + Mul(Q(0.06), n2);
                }
                case "fjord":
                {
                    int h = Q(0.35) + Mul(Q(0.45), n1) + Mul(Q(0.10), n2);
                    if (Inlet(px, py, p.Dir, p.Salt) || Inlet(px, py, p.Dir2, p.Salt + 1u) || Inlet(px, py, p.Dir3, p.Salt + 2u))
                    {
                        h = r.SeaQ - Q(0.05);
                    }

                    return h;
                }
                case "archipelago":
                {
                    int ns = Fbm(p.Salt ^ 0x3C6EF372u, px, py, 5, 4);
                    return Q(0.20) + Mul(Q(0.65), ns) + Mul(Q(0.15), n2) - Mul(Q(0.25), Smooth(150, 240, rad));
                }
                case "ice_field":
                {
                    int h = Q(0.25) + Mul(Q(0.50), One - Smooth(0, 250, rad)) + Mul(Q(0.06), n1);
                    for (int i = 0; i < p.Peaks; i++)
                    {
                        int angle = (int)(Hash(p.Salt, i, 7) & 15u);
                        int dist = 40 + (int)(Hash(p.Salt, i, 9) % 110u);
                        int cx = Cos16[angle] * dist >> 10;
                        int cy = Sin16[angle] * dist >> 10;
                        int d = ISqrt((long)(px - cx) * (px - cx) + (long)(py - cy) * (py - cy));
                        h += Mul(Q(0.35), One - Smooth(0, 22, d)); // nunataks piercing the ice
                    }

                    return h;
                }
                case "plateau":
                {
                    int edge = 170 + ((n1 - 32768) * 80 >> 16);
                    int top = One - Smooth(edge - 18, edge + 18, rad);
                    return Q(0.15) + Mul(Q(0.12), n1) + Mul(Q(0.65), top) + Mul(Q(0.05), n2);
                }
                case "canyon":
                {
                    int a = System.Math.Abs(v - Meander(p.Salt, u, 60));
                    river = a < RiverHalfWidth;
                    // Three stepped terraces down to the river: 0.08 → 0.32 → 0.56 → 0.82 mesa.
                    return Q(0.08) + Mul(Q(0.24), Smooth(10, 18, a)) + Mul(Q(0.24), Smooth(26, 34, a))
                           + Mul(Q(0.26), Smooth(42, 50, a)) + Mul(Q(0.06), n2);
                }
                case "cave":
                {
                    int across = System.Math.Abs(v - Meander(p.Salt, u, 80));
                    river = across < RiverHalfWidth;
                    int h = Q(0.08) + Mul(Q(0.06), n1);
                    if (across >= 12)
                    {
                        h = System.Math.Max(h, Towers(p.Salt, px, py));
                    }

                    return h;
                }
                default: // eternal_day / eternal_night: polar coast — sea on one side, rising ice-scoured land on the other
                {
                    int coast = u + Meander(p.Salt, v, 50);
                    // Night is the rugged variant: coarse noise (n1) makes bays and headlands; fine noise near sea level
                    // would only speckle the coast with one-sample islands.
                    int rough = kind == "eternal_night" ? Mul(Q(0.14), n1) : Mul(Q(0.08), n2);
                    return Q(0.10) + Mul(Q(0.80), Smooth(-30, 160, coast)) + rough;
                }
            }
        }

        // One fjord arm: a tapering, meandering channel from 205 half-cells out to 40 from the centre along heading dir.
        private static bool Inlet(int px, int py, int dir, uint salt)
        {
            int along = (px * Cos16[dir] + py * Sin16[dir]) >> 10;
            if (along < 40 || along > 205)
            {
                return false;
            }

            int lateral = (-px * Sin16[dir] + py * Cos16[dir]) >> 10;
            int width = 6 + (along - 40) * 14 / 165;
            return System.Math.Abs(lateral - Meander(salt, along, 30)) < width;
        }

        // Karst towers: one candidate per 32-half-cell Worley cell (70 % present), steep cylinder-ish profile.
        private static int Towers(uint salt, int px, int py)
        {
            const int Cell = 32;
            int gx = (px + 1024) / Cell;
            int gy = (py + 1024) / Cell;
            int best = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = gx + dx;
                    int cy = gy + dy;
                    uint hsh = Hash(salt ^ 0x7F4A7C15u, cx, cy);
                    if (hsh % 100u >= 70u)
                    {
                        continue;
                    }

                    int ox = cx * Cell - 1024 + (int)((hsh >> 8) % Cell);
                    int oy = cy * Cell - 1024 + (int)((hsh >> 16) % Cell);
                    int radius = 8 + (int)((hsh >> 24) % 7u);
                    int top = Q(0.50) + (int)((hsh >> 4) % (uint)Q(0.45));
                    int d = ISqrt((long)(px - ox) * (px - ox) + (long)(py - oy) * (py - oy));
                    best = System.Math.Max(best, Mul(top, One - Smooth(radius * 6 / 10, radius, d)));
                }
            }

            return best;
        }

        // Lateral offset (half-cells, ±amplitude) of a river/coast along coordinate t.
        private static int Meander(uint salt, int t, int amplitude)
        {
            int n = (Noise1(salt, t + 1024, 6) + Noise1(salt ^ 0x9E3779B9u, t + 1024, 5)) >> 1;
            return (int)((long)(n - 32768) * amplitude * 2 >> 16);
        }

        private static int Noise1(uint salt, int t, int log2Cell)
        {
            int i = t >> log2Cell;
            int f = SmoothFrac(t, log2Cell);
            int a = (int)(Hash(salt, i, 0) & 0xFFFFu);
            int b = (int)(Hash(salt, i + 1, 0) & 0xFFFFu);
            return a + ((b - a) * f >> 8);
        }

        // Fractal value noise in Q16 [0, 65535]; octaves halve cell size and amplitude.
        private static int Fbm(uint salt, int px, int py, int log2Cell, int octaves)
        {
            long sum = 0;
            long total = 0;
            int amp = 256;
            for (int o = 0; o < octaves && log2Cell - o >= 1; o++)
            {
                sum += (long)Value(salt + (uint)o * 0x9E37u, px + 1024, py + 1024, log2Cell - o) * amp;
                total += amp;
                amp >>= 1;
            }

            return (int)(sum / total);
        }

        private static int Value(uint salt, int x, int y, int log2Cell)
        {
            int ix = x >> log2Cell;
            int iy = y >> log2Cell;
            int fx = SmoothFrac(x, log2Cell);
            int fy = SmoothFrac(y, log2Cell);
            int a = (int)(Hash(salt, ix, iy) & 0xFFFFu);
            int b = (int)(Hash(salt, ix + 1, iy) & 0xFFFFu);
            int c = (int)(Hash(salt, ix, iy + 1) & 0xFFFFu);
            int d = (int)(Hash(salt, ix + 1, iy + 1) & 0xFFFFu);
            int top = a + ((b - a) * fx >> 8);
            int bottom = c + ((d - c) * fx >> 8);
            return top + ((bottom - top) * fy >> 8);
        }

        // Position inside a lattice cell as 0..255, eased with 3t² − 2t³ so value noise has no visible creases.
        private static int SmoothFrac(int x, int log2Cell)
        {
            int t = ((x & ((1 << log2Cell) - 1)) << 8) >> log2Cell;
            return t * t * (768 - 2 * t) >> 16;
        }

        private static uint Hash(uint salt, int x, int y)
        {
            unchecked
            {
                uint h = salt ^ ((uint)x * 0x27D4EB2Du) ^ ((uint)y * 0x165667B1u);
                h ^= h >> 15;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        // Q16 smoothstep: 0 at x <= e0, One at x >= e1.
        private static int Smooth(int e0, int e1, int x)
        {
            if (x <= e0)
            {
                return 0;
            }

            if (x >= e1)
            {
                return One;
            }

            long t = (long)(x - e0) * One / (e1 - e0);
            return (int)(t * t / One * (3L * One - 2L * t) / One);
        }

        private static int Mul(int a, int b) => (int)((long)a * b >> 16);

        private static int Q(double f) => (int)(f * One);

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        private static int ISqrt(long n)
        {
            long r = 0;
            long bit = 1L << 40; // correct for n < 2^42; inputs here stay below 2^19 (squared half-cell distances)
            while (bit > n)
            {
                bit >>= 2;
            }

            while (bit != 0)
            {
                if (n >= r + bit)
                {
                    n -= r + bit;
                    r = (r >> 1) + bit;
                }
                else
                {
                    r >>= 1;
                }

                bit >>= 2;
            }

            return (int)r;
        }
    }
}
