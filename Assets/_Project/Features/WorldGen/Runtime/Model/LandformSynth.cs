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
        // v2 (2026-11-15, Q-W18 / P3-24 b / P3-28): twelve more kinds (the four table landforms that had no generator,
        //     four cold landforms, four bedrock landforms). The grids of the v1 kinds are bit-identical to v1; the
        //     version moves because the catalog TemplatePlanner draws from changed, so every planet's stamps did.
        public const int GeneratorVersion = 2;

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

        // REQ: planet-world.md#1-骨架層程序化地貌 — one generator per landform tag. The first twelve are the planet
        // types' own terrain tags (Data/schemas/planet-type.schema.json); new kinds go at the end so indices stay put.
        private static readonly string[] KindIds =
        {
            "forest", "river_valley", "volcano", "rift_valley", "ice_field", "fjord",
            "plateau", "archipelago", "canyon", "cave", "eternal_day", "eternal_night",
            "grassland", "desert", "swamp", "mountain",
            "tundra", "glacier", "cirque", "moraine",
            "karst", "basalt_terrace", "granite_domes", "slate_cliffs",
        };

        /// <summary>Landform kinds (terrain tags) this synthesizer can build, in a fixed order.</summary>
        /// <remarks>Pre: none. Post: 24 distinct ids, same order every call. Side effects: none.</remarks>
        public static IReadOnlyList<string> Kinds => KindIds;

        /// <summary>The landform kind a stamp made by <see cref="Generate"/> belongs to.</summary>
        /// <remarks>
        /// Pre: sourceId is the <see cref="TemplateSource.Id"/> of a <see cref="Generate"/> result ("kind#index").
        /// Post: one of <see cref="Kinds"/>. Side effects: allocates the substring.
        /// </remarks>
        public static string KindOf(string sourceId)
        {
            int hash = sourceId != null ? sourceId.IndexOf('#') : -1;
            string kind = hash > 0 ? sourceId.Substring(0, hash) : null;
            Contract.Requires(kind != null && System.Array.IndexOf(KindIds, kind) >= 0,
                "sourceId must be the id of a LandformSynth.Generate result");
            return kind;
        }

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
                case "grassland": return new Recipe(0.0, 900f);
                case "desert": return new Recipe(0.0, 1100f);
                case "swamp": return new Recipe(0.20, 500f);
                case "mountain": return new Recipe(0.0, 3000f);
                // Lakes need a sea level: thaw lakes, tarns and kettles sit just under it, the land just above.
                case "tundra": return new Recipe(0.10, 600f);
                case "glacier": return new Recipe(0.0, 2800f);
                case "cirque": return new Recipe(0.28, 2600f);
                case "moraine": return new Recipe(0.10, 700f);
                case "karst": return new Recipe(0.0, 1500f);
                case "basalt_terrace": return new Recipe(0.0, 1600f);
                case "granite_domes": return new Recipe(0.0, 1900f);
                case "slate_cliffs": return new Recipe(0.0, 2000f);
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
                case "grassland":
                {
                    int broad = Fbm(p.Salt ^ 0x51ED270Bu, px, py, 7, 3);
                    int across = System.Math.Abs(v - Meander(p.Salt, u, 55));
                    river = across < RiverHalfWidth - 1; // a brook, not a river
                    return Q(0.14) + Mul(Q(0.52), broad) + Mul(Q(0.10), n1) + Mul(Q(0.02), n2)
                           - Mul(Q(0.09), One - Smooth(0, 36, across));
                }
                case "desert":
                {
                    int broad = Fbm(p.Salt ^ 0x51ED270Bu, px, py, 7, 2);
                    // Crests run across the wind (u). Coarse noise bends the phase by up to a full wavelength, so
                    // crests fork and merge like real transverse dunes instead of ruling the stamp.
                    int dune = DuneProfile((u * 5 + ((n1 - 32768) >> 7) + 8192) & 255);
                    int cross = DuneProfile((v * 8 + ((n2 - 32768) >> 8) + 8192) & 255);
                    // Dunes thin out into flat interdune pans where the broad field is low.
                    int field = Q(0.30) + Mul(Q(0.70), Smooth(Q(0.36), Q(0.62), broad));
                    int wadi = System.Math.Abs(v - Meander(p.Salt, u, 80));
                    return Q(0.12) + Mul(Q(0.24), broad) + Mul(Mul(Q(0.34), dune), field) + Mul(Q(0.06), cross)
                           - Mul(Q(0.10), One - Smooth(4, 22, wadi)); // dry bed: carved, never flagged as river
                }
                case "swamp":
                {
                    // Hummocks at two scales around the water table: the hollows flood into many small pools.
                    int pools = Fbm(p.Salt ^ 0x1B873593u, px, py, 5, 3);
                    int h = Q(0.082) + Mul(Q(0.10), n1) + Mul(Q(0.12), pools) + Mul(Q(0.03), n2)
                            + Mul(Q(0.50), Smooth(140, 250, rad));
                    int bayou = System.Math.Abs(v - Meander(p.Salt, u, 55));
                    // The slow main channel is open water like the pools, so it is sea, not a river overlay.
                    return Lerp(h, r.SeaQ - Q(0.03), One - Smooth(5, 12, bayou));
                }
                case "mountain":
                {
                    int spine = One - Smooth(0, 170, System.Math.Abs(v - Meander(p.Salt, u, 60)));
                    // Two ridged octaves: long crest lines, then spurs running off them. Squaring keeps crests
                    // narrow and valleys broad, which is what separates a range from lumpy hills.
                    int crest = Ridge(Fbm(p.Salt ^ 0x51ED270Bu, px, py, 7, 2));
                    int spur = Ridge(Fbm(p.Salt ^ 0x1B873593u, px, py, 5, 2));
                    crest = Mul(crest, crest);
                    // Ridges only where the range stands: foothills stay walkable.
                    return Q(0.05) + Mul(spine, Q(0.22) + Mul(Q(0.42), crest) + Mul(Q(0.22), Mul(crest, spur)))
                           + Mul(Q(0.08), spur) + Mul(Q(0.03), n2);
                }
                case "tundra":
                {
                    int broad = Fbm(p.Salt ^ 0x51ED270Bu, px, py, 7, 2);
                    int h = Q(0.15) + Mul(Q(0.12), broad) + Mul(Q(0.03), n2) + Mul(Q(0.22), Smooth(170, 255, rad));
                    // Pingos (凍脹丘): ice-cored mounds with a slumped summit.
                    int pingo = Mounds(p.Salt ^ 0x2C1B3C6Du, px, py, 64, 32, 9, 9, 35, out uint ph);
                    int top = Q(0.20) + (int)(ph % (uint)Q(0.16));
                    h += Mul(top, pingo) - Mul(Q(0.07), Smooth(Q(0.86), One, pingo));
                    // Lakes stop short of the rim, where a basin could not hold water (see the rim lift in Generate).
                    int lake = Mul(Mounds(p.Salt ^ 0x297A2D39u, px + (wobble >> 3), py, 72, 50, 10, 13, 70, out _),
                        One - Smooth(150, 178, rad));
                    return Lerp(h, r.SeaQ - Q(0.03), lake);
                }
                case "glacier":
                {
                    int axis = v - Meander(p.Salt, u, 28);
                    int a = System.Math.Abs(axis);
                    int half = 46 + ((u + 255) * 16 >> 9); // the trough widens downstream
                    // U profile: flat floor, steep walls. The headwall closes the trough into its accumulation basin.
                    int wall = System.Math.Max(Smooth(half - 6, half + 30, a), One - Smooth(-225, -180, u));
                    int floor = Q(0.50) - Clamp(u + 255, 0, 510) * Q(0.36) / 510;
                    // Shoulders rise into ridged summits away from the trough, so the valley reads as cut through a range.
                    int upland = Q(0.62) + Mul(Q(0.12), n1) + Mul(Q(0.04), n2)
                                 + Mul(Mul(Q(0.26), Ridge(Fbm(p.Salt ^ 0x51ED270Bu, px, py, 6, 2))), Smooth(half, half + 110, a));
                    int h = Lerp(floor, upland, wall);
                    int snout = 30 + (int)(Hash(p.Salt, 3, 5) % 60u);
                    int along = One - Smooth(snout - 34, snout, u);
                    int t = Clamp(a * One / half, 0, One);
                    // Ice tongue: convex across, steep at the snout. Transverse crevasses open only on the last
                    // stretch before the snout, where the ice speeds up, and bow downstream like the flow.
                    int crevasse = Mul(Tri(u + (a * a >> 7), 14), Smooth(snout - 120, snout - 40, u));
                    h += Mul(Mul(Q(0.13), One - Mul(t, t)) + Mul(Q(0.007), crevasse),
                        Mul(along, One - Smooth(half - 8, half, a)));
                    h += Mul(Q(0.035), Mul(Bump(a, half - 2, 6), along)); // lateral moraines
                    // Terminal and recessional moraines: arcs around the snout, breached by the meltwater stream.
                    int d = ISqrt((long)(u - snout + 30) * (u - snout + 30) + (long)axis * axis);
                    int ahead = Smooth(snout - 6, snout + 8, u);
                    int breach = Smooth(3, 9, a);
                    h += Mul(Mul(Q(0.08), Bump(d, 48, 8)) + Mul(Q(0.05), Bump(d, 82, 7)), Mul(ahead, breach));
                    river = u > snout + 2 && a < RiverHalfWidth - 1;
                    return h;
                }
                case "cirque":
                {
                    int massif = One - Smooth(20, 200, rad + (wobble >> 1));
                    int h = Q(0.33) + Mul(Q(0.50), massif) + Mul(Q(0.14), Mul(massif, Ridge(n1))) + Mul(Q(0.03), n2);
                    int bowls = System.Math.Min(p.Peaks, 4);
                    int warp = (n1 - 32768) >> 11; // ±16 half-cells: no bowl is a circle
                    for (int i = 0; i < bowls; i++)
                    {
                        int angle = (p.Dir + i * 16 / bowls + (int)(Hash(p.Salt, i, 11) % 3u)) & 15;
                        int dist = 104 + (int)(Hash(p.Salt, i, 13) % 22u);
                        int radius = 40 + (int)(Hash(p.Salt, i, 17) % 22u);
                        int cx = Cos16[angle] * dist >> 10;
                        int cy = Sin16[angle] * dist >> 10;
                        int d = ISqrt((long)(px - cx) * (px - cx) + (long)(py - cy) * (py - cy)) + warp;
                        // Only ever lowers, and to a floor that sits just above the massif's outer foot: the hollow
                        // bites an armchair (high headwall toward the horn, a low rock lip outward) rather than a
                        // crater, and leaves arêtes between neighbours.
                        h = System.Math.Min(h, Lerp(Q(0.345), h, Smooth(radius * 40 / 100, radius, d)));
                        // The tarn lies against the headwall, a third of the way in from the bowl's centre.
                        int tx = cx - (Cos16[angle] * radius / 3 >> 10);
                        int ty = cy - (Sin16[angle] * radius / 3 >> 10);
                        int td = ISqrt((long)(px - tx) * (px - tx) + (long)(py - ty) * (py - ty)) + warp;
                        h = Lerp(h, r.SeaQ - Q(0.03), One - Smooth(radius * 18 / 100, radius * 30 / 100, td));
                    }

                    return h;
                }
                case "moraine":
                {
                    int h = Q(0.16) + Mul(Q(0.10), n1) + Mul(Q(0.04), n2) + Mul(Q(0.15), Smooth(180, 255, rad));
                    // Drumlins: mounds stretched 3× along the old ice flow (u).
                    h += Mul(Q(0.26), Mounds(p.Salt ^ 0x2C1B3C6Du, u / 3, v, 40, 75, 12, 8, 20, out _));
                    // End moraines: arcs centred far upstream, one every 120 half-cells.
                    // Coarse noise pushes each arc back and forth and breaches it, as meltwater did.
                    int arc = (ISqrt((long)(u + 420) * (u + 420) + (long)v * v) + ((n1 - 32768) >> 9) + 240) % 120;
                    int standing = Smooth(Q(0.38), Q(0.52), Fbm(p.Salt ^ 0x1B873593u, px, py, 6, 2));
                    h += Mul(Mul(Q(0.22), Bump(arc, 60, 15)), standing);
                    int kettle = Mul(Mounds(p.Salt ^ 0x297A2D39u, px, py, 64, 45, 8, 9, 55, out _),
                        One - Smooth(150, 178, rad));
                    return Lerp(h, r.SeaQ - Q(0.03), kettle);
                }
                case "karst":
                {
                    int h = Q(0.58) + Mul(Q(0.14), n1) + Mul(Q(0.05), n2);
                    h -= Mul(Q(0.10), Mounds(p.Salt ^ 0x2C1B3C6Du, px, py, 36, 60, 8, 8, 0, out _)); // doline funnels
                    // Tiankeng (天坑): a few great collapse shafts with sheer walls and flat floors.
                    int pit = Mounds(p.Salt ^ 0x297A2D39u, px, py, 110, 65, 22, 16, 82, out _);
                    h = Lerp(h, Q(0.10) + Mul(Q(0.04), n2), pit);
                    if (pit == 0)
                    {
                        // Stone forest: pinnacles in groves, not evenly sown.
                        int grove = Smooth(Q(0.50), Q(0.62), Fbm(p.Salt ^ 0x1B873593u, px, py, 6, 2));
                        h += Mul(Mul(Q(0.26), Mounds(p.Salt ^ 0x7F4A7C15u, px, py, 20, 80, 6, 4, 55, out _)), grove);
                    }

                    return h;
                }
                case "basalt_terrace":
                {
                    // Voronoi mosaic: every site is one flat-topped column cluster whose level is read from a smooth
                    // field at the site, so neighbours differ by at most a step or two and the flow reads as a stair.
                    const int Cell = 26;
                    int gx = (px + 1024) / Cell;
                    int gy = (py + 1024) / Cell;
                    long best = long.MaxValue;
                    int sx = 0;
                    int sy = 0;
                    uint sh = 0u;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            uint hsh = Hash(p.Salt ^ 0x2C1B3C6Du, gx + dx, gy + dy);
                            int ox = (gx + dx) * Cell - 1024 + (int)((hsh >> 8) % Cell);
                            int oy = (gy + dy) * Cell - 1024 + (int)((hsh >> 16) % Cell);
                            long dd = (long)(px - ox) * (px - ox) + (long)(py - oy) * (py - oy);
                            if (dd < best)
                            {
                                best = dd;
                                sx = ox;
                                sy = oy;
                                sh = hsh;
                            }
                        }
                    }

                    int dome = One - Smooth(0, 240, ISqrt((long)sx * sx + (long)sy * sy));
                    int field = Mul(Q(0.55), dome) + Mul(Q(0.45), Fbm(p.Salt, sx, sy, 6, 3));
                    int level = field * 9 >> 16;
                    return Q(0.10) + level * Q(0.085) + (int)((sh >> 24) % (uint)Q(0.02)) + Mul(Q(0.015), n2);
                }
                case "granite_domes":
                {
                    int h = Q(0.14) + Mul(Q(0.10), n1) + Mul(Q(0.04), n2);
                    h += Mul(Q(0.07), Mounds(p.Salt ^ 0x297A2D39u, px, py, 30, 35, 7, 5, 30, out _)); // core-stone piles
                    return h + Domes(p.Salt ^ 0x2C1B3C6Du, px, py, (n1 - 32768) >> 11);
                }
                case "slate_cliffs":
                {
                    // Cuestas: each bed dips gently up-wind (u) and breaks off in a scarp; the fronts wander.
                    const int Period = 120;
                    int t = u + Meander(p.Salt, v, 26) + ((n1 - 32768) >> 9) + ((n2 - 32768) >> 12) + 1200;
                    int phase = t % Period;
                    int saw = phase < 110 ? phase * One / 110 : One - Smooth(110, Period, phase);
                    int bed = Q(0.26) + (int)(Hash(p.Salt, t / Period, 21) % (uint)Q(0.22));
                    return Q(0.10) + Mul(bed, saw) + Clamp(u + 255, 0, 510) * Q(0.18) / 510 + Mul(Q(0.07), n1)
                           + Mul(Q(0.03), n2);
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

        // Mask in [0, One] of round features sown one candidate per Worley cell (presentPct % present): One within
        // innerPct % of a feature's radius, falling to 0 at the radius (innerPct 0 = a cone). siteHash is the winning
        // feature's hash, for per-feature variation.
        private static int Mounds(uint salt, int px, int py, int cell, uint presentPct, int minRadius, uint radiusSpan,
            int innerPct, out uint siteHash)
        {
            int gx = (px + 1024) / cell;
            int gy = (py + 1024) / cell;
            int best = 0;
            siteHash = 0u;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = gx + dx;
                    int cy = gy + dy;
                    uint hsh = Hash(salt, cx, cy);
                    if (hsh % 100u >= presentPct)
                    {
                        continue;
                    }

                    int ox = cx * cell - 1024 + (int)((hsh >> 8) % (uint)cell);
                    int oy = cy * cell - 1024 + (int)((hsh >> 16) % (uint)cell);
                    int radius = minRadius + (int)((hsh >> 24) % radiusSpan);
                    int d = ISqrt((long)(px - ox) * (px - ox) + (long)(py - oy) * (py - oy));
                    int m = One - Smooth(radius * innerPct / 100, radius, d);
                    if (m > best)
                    {
                        best = m;
                        siteHash = hsh;
                    }
                }
            }

            return best;
        }

        // Bornhardts: steep-sided granite domes (radius 34–57 half-cells), one candidate per 100-half-cell cell.
        // Each is an ellipse on its own axis and `warp` (half-cells) dents the outline, so no two are the same ball.
        private static int Domes(uint salt, int px, int py, int warp)
        {
            const int Cell = 100;
            int gx = (px + 1024) / Cell;
            int gy = (py + 1024) / Cell;
            int best = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = gx + dx;
                    int cy = gy + dy;
                    uint hsh = Hash(salt, cx, cy);
                    if (hsh % 100u >= 55u)
                    {
                        continue;
                    }

                    int ox = cx * Cell - 1024 + (int)((hsh >> 8) % Cell);
                    int oy = cy * Cell - 1024 + (int)((hsh >> 16) % Cell);
                    int radius = 34 + (int)((hsh >> 24) % 24u);
                    int dir = (int)((hsh >> 12) & 15u);
                    int along = ((px - ox) * Cos16[dir] + (py - oy) * Sin16[dir]) >> 10;
                    int lateral = ((ox - px) * Sin16[dir] + (py - oy) * Cos16[dir]) >> 10;
                    // Long axis 1×, short axis 0.62–1×: whalebacks as well as round domes.
                    lateral = lateral * 16 / (10 + (int)((hsh >> 20) % 7u));
                    int e = ISqrt((long)along * along + (long)lateral * lateral) + warp;
                    long dd = e <= 0 ? 0L : (long)e * e;
                    long rr = (long)radius * radius;
                    if (dd >= rr)
                    {
                        continue;
                    }

                    int top = Q(0.42) + (int)((hsh >> 4) % (uint)Q(0.30));
                    best = System.Math.Max(best, (int)((long)top * ISqrt(rr - dd) / radius));
                }
            }

            return best;
        }

        // Dune cross-section over one wavelength (phase 0..255): long windward ramp, short slip face.
        private static int DuneProfile(int phase) =>
            phase < 184 ? Smooth(0, 184, phase) : One - Smooth(184, 256, phase);

        // Ridged noise: One along the n = 0.5 contour, 0 at the extremes.
        private static int Ridge(int n) => One - System.Math.Abs(2 * n - One);

        // One where x == centre, 0 beyond halfWidth away.
        private static int Bump(int x, int centre, int halfWidth) => One - Smooth(0, halfWidth, System.Math.Abs(x - centre));

        // Triangle wave in [-One, One] with the given period (half-cells).
        private static int Tri(int x, int period)
        {
            int ph = ((x % period) + period) % period;
            return System.Math.Abs(ph * 4 * One / period - 2 * One) - One;
        }

        private static int Lerp(int a, int b, int t) => a + Mul(b - a, t);

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
