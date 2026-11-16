using System;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;
using Geo = Cosmimera.Bootstrap.Demo.ArchitectureMeshes.Geo;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>What one wonder looks like: its rule-layer shape plus the style axes the presentation draws.</summary>
    /// <remarks>
    /// REQ: docs/design/planet-world.md#奇觀地標 (地區 × 時代 × 功能 × 建築年齡).
    /// Pre: built by the constructor or <see cref="LandmarkMeshes.SpecFor"/>. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct LandmarkSpec
    {
        /// <summary>Describes one wonder.</summary>
        /// <remarks>
        /// Pre: kind, region and era defined; 0 ≤ variant &lt; LandmarkPlanner.VariantCount(kind); age in [0, 1];
        ///      footprintRadiusM ≥ 0 and heightM > 0, both finite.
        /// Post: properties return the arguments. Side effects: none.
        /// </remarks>
        public LandmarkSpec(LandmarkKind kind, int variant, ArchRegion region, ArchEra era, float age, float footprintRadiusM,
            float heightM, ulong seed)
        {
            Contract.Requires(Enum.IsDefined(typeof(LandmarkKind), kind) && Enum.IsDefined(typeof(ArchRegion), region) &&
                              Enum.IsDefined(typeof(ArchEra), era), "kind, region and era must be defined");
            Contract.Requires(variant >= 0 && variant < LandmarkPlanner.VariantCount(kind), "variant out of range");
            Contract.Requires(age >= 0f && age <= 1f, "age in [0,1]");
            Contract.Requires(math.isfinite(footprintRadiusM) && footprintRadiusM >= 0f && math.isfinite(heightM) && heightM > 0f,
                "sizes must be finite; footprint >= 0, height > 0");
            Kind = kind;
            Variant = variant;
            Region = region;
            Era = era;
            Age = age;
            FootprintRadiusM = footprintRadiusM;
            HeightM = heightM;
            Seed = seed;
        }

        /// <summary>Kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public LandmarkKind Kind { get; }

        /// <summary>Sub-type (a tower's function, a temple's plan…).</summary>
        /// <remarks>Pre: none. Post: valid for Kind. Side effects: none.</remarks>
        public int Variant { get; }

        /// <summary>Regional tradition (palette and roof shape).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchRegion Region { get; }

        /// <summary>Era it was built in (construction and detailing).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchEra Era { get; }

        /// <summary>Building age: 0 = just finished, 1 = worn, overgrown and discoloured.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Age { get; }

        /// <summary>Radius of the rule layer's footprint circle (m).</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public float FootprintRadiusM { get; }

        /// <summary>Height above the mesh origin (m).</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public float HeightM { get; }

        /// <summary>Stream seed of this wonder's own variation.</summary>
        /// <remarks>Pre: none. Post: as given. Side effects: none.</remarks>
        public ulong Seed { get; }
    }

    /// <summary>
    /// Procedural meshes of the wonders placed by <see cref="LandmarkPlanner"/>: towers by function, temples, sky
    /// islands, the space elevator and its station, and the surface marks of labyrinths, mines and dungeons.
    /// REQ: docs/design/planet-world.md#奇觀地標.
    /// </summary>
    /// <remarks>
    /// Meshes are in metres in the wonder's own frame: +Z = entrance side, +Y = up, origin on the ground at the
    /// footprint centre (SkyIsland: centre of its top surface; SpaceStation: its hub). They use the four material
    /// slots of <see cref="ArchitectureMeshes"/> so one shader and one palette type serve both.
    /// Pre: main thread (creates Unity objects). Post: results depend only on the spec. Side effects: allocates.
    /// </remarks>
    public static class LandmarkMeshes
    {
        private const ulong Salt = 0x1A4D3A2C5ul;
        private const float FacetCos = 0.8f; // same crease as ArchitectureMeshes: hard masonry edges, soft lathe surfaces
        private const int W = ArchitectureMeshes.WallSubMesh;
        private const int R = ArchitectureMeshes.RoofSubMesh;
        private const int T = ArchitectureMeshes.TrimSubMesh;
        private const int G = ArchitectureMeshes.GlowSubMesh;

        /// <summary>How far below the origin solid wonders are built, so a sloped site never shows a gap (m).</summary>
        /// <remarks>Pre: none. Post: constant > 0. Side effects: none.</remarks>
        public const float SinkM = 3f;

        // Construction families: several eras share one way of building, and differ in palette and trim.
        private enum Tech
        {
            Rustic,
            Masonry,
            Machine,
            Future,
            Ancient,
        }

        private static Tech TechOf(ArchEra era) => era switch
        {
            ArchEra.Primal => Tech.Rustic,
            ArchEra.Agrarian => Tech.Rustic,
            ArchEra.Mercantile => Tech.Masonry,
            ArchEra.Industrial => Tech.Masonry,
            ArchEra.Steampunk => Tech.Machine,
            ArchEra.Wasteland => Tech.Machine,
            ArchEra.Future => Tech.Future,
            _ => Tech.Ancient,
        };

        private static readonly ArchEra[] OtherEras =
        {
            ArchEra.Primal, ArchEra.Agrarian, ArchEra.Mercantile, ArchEra.Industrial, ArchEra.Steampunk, ArchEra.Future,
            ArchEra.Wasteland,
        };

        /// <summary>Style of <paramref name="landmark"/> on a planet of <paramref name="planet"/>.</summary>
        /// <remarks>
        /// REQ: planet-world.md#奇觀地標 (時代與年齡的抽法，暫定): the space elevator and its station are Future (70 %)
        /// or Ancient precursor work (30 %) and always agree; every other wonder is the planet's era (55 %), Ancient
        /// (25 %) or an unrelated era (20 %). Age is the era's usual wear plus a bounded normal (σ 0.15).
        /// Pre: landmark produced by LandmarkPlanner. Post: same arguments, same spec on every client; sizes, kind
        ///      and variant are the landmark's. Side effects: none.
        /// </remarks>
        public static LandmarkSpec SpecFor(in ArchPlanetStyle planet, in RunLandmark landmark, ulong seed)
        {
            bool orbital = landmark.Kind == LandmarkKind.SpaceElevator || landmark.Kind == LandmarkKind.SpaceStation;
            // The station follows its elevator in the layout; one stream for both keeps their eras equal.
            int styleIndex = landmark.Kind == LandmarkKind.SpaceStation ? landmark.Index - 1 : landmark.Index;
            var rng = new Pcg32(Salt ^ seed, 0x4C4D4Bul + (ulong)styleIndex);
            uint roll = rng.NextUInt(100);
            ArchEra other = OtherEras[rng.NextUInt((uint)OtherEras.Length)];
            ArchEra era = orbital
                ? roll < 70 ? ArchEra.Future : ArchEra.Ancient
                : roll < 55 ? planet.Era : roll < 80 ? ArchEra.Ancient : other;
            float g = rng.NextFloat() + rng.NextFloat() + rng.NextFloat() + rng.NextFloat() - 2f;
            float age = math.saturate(ArchitectureMeshes.EraWear(era).age + g * 0.26f);
            return new LandmarkSpec(landmark.Kind, landmark.Variant, planet.Region, era, age, landmark.FootprintRadiusM,
                landmark.HeightM, seed ^ ((ulong)(landmark.Index + 1) * 0x9E3779B97F4A7C15ul));
        }

        /// <summary>Material colours of <paramref name="spec"/>, in the slots of <see cref="ArchitectureMeshes"/>.</summary>
        /// <remarks>
        /// Pre: none. Post: sky islands are rock (wall slot), turf (roof slot) and water-blue glow; everything else is
        /// the tower palette of its region and era, weathering more strongly with age. Side effects: none.
        /// </remarks>
        public static ArchPalette PaletteFor(in LandmarkSpec spec)
        {
            ArchPalette p = ArchitectureMeshes.PaletteFor(spec.Region, spec.Era, BuildingKind.Tower);
            float strength = math.saturate(math.max(p.WeatheringStrength, 0.35f + 0.6f * spec.Age));
            if (spec.Kind == LandmarkKind.SkyIsland)
            {
                Color rock = Color.Lerp(new Color(0.42f, 0.4f, 0.38f), p.Wall, 0.3f);
                return new ArchPalette(rock, p.Weathering, p.Trim, new Color(0.72f, 0.93f, 1f), p.Weathering, 0.9f);
            }

            return new ArchPalette(p.Wall, p.Roof, p.Trim, p.Glow, p.Weathering, strength);
        }

        /// <summary>Height of the part of <paramref name="spec"/> that blocks the character (m above the origin).</summary>
        /// <remarks>Pre: none. Post: > 0 and ≤ HeightM. Side effects: none.</remarks>
        public static float BodyHeightM(in LandmarkSpec spec) =>
            spec.Kind == LandmarkKind.SpaceElevator ? math.min(spec.HeightM, 30f) : spec.HeightM;

        /// <summary>Builds the mesh of <paramref name="spec"/>.</summary>
        /// <remarks>
        /// Pre: main thread.
        /// Post: a new mesh with normals and colours and exactly ArchitectureMeshes.SubMeshCount sub-meshes; the same
        ///       spec always gives the same vertices. Solid kinds stay inside the footprint circle below 2 m and reach
        ///       SinkM below the origin; nothing rises above HeightM + 4 m (finials and antennas).
        /// Side effects: allocates a Mesh the caller owns (and must destroy).
        /// </remarks>
        public static Mesh Build(in LandmarkSpec spec)
        {
            var rng = new Pcg32(Salt ^ spec.Seed, ((ulong)spec.Kind << 16) | ((ulong)spec.Variant << 8) | (ulong)spec.Era);
            var g = new Geo[ArchitectureMeshes.SubMeshCount];
            for (int i = 0; i < g.Length; i++)
            {
                g[i] = new Geo();
            }

            Tech tech = TechOf(spec.Era);
            switch (spec.Kind)
            {
                case LandmarkKind.Tower:
                    BuildTower(g, spec, tech, ref rng);
                    break;
                case LandmarkKind.Temple:
                    BuildTemple(g, spec, tech, ref rng);
                    break;
                case LandmarkKind.SkyIsland:
                    BuildSkyIsland(g, spec, ref rng);
                    break;
                case LandmarkKind.SpaceElevator:
                    BuildElevator(g, spec, tech, ref rng);
                    break;
                case LandmarkKind.SpaceStation:
                    BuildStation(g, spec, tech, ref rng);
                    break;
                case LandmarkKind.Labyrinth:
                    BuildLabyrinth(g, spec, tech, ref rng);
                    break;
                case LandmarkKind.MineShaft:
                    BuildMine(g, spec, tech, ref rng);
                    break;
                default:
                    BuildGate(g, spec, tech, ref rng);
                    break;
            }

            float3 offset = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 53f;
            float age = spec.Age;
            bool island = spec.Kind == LandmarkKind.SkyIsland;
            // Old stone goes darker and greyer; a new shell keeps its full tone.
            float3 tint = math.lerp(new float3(1.04f), new float3(0.8f, 0.82f, 0.86f), age);
            var data = new SceneryMeshes.MeshData(ArchitectureMeshes.SubMeshCount);
            data.EmitFaceted(g[W].Part, W, FacetCos, (p, n, c) =>
            {
                float patch = noise.snoise(p * 0.35f + offset) * 0.5f + 0.5f;
                float fine = noise.snoise(p * 1.3f + offset.zxy) * 0.5f + 0.5f;
                // Damp climbs from the foot, growth settles on anything that looks up, and with the years both spread
                // into patches across the whole face.
                float foot = island ? 0f : 1f - math.smoothstep(0.2f, 2.5f + 6f * age, p.y);
                float ledge = math.smoothstep(0.55f, 0.9f, n.y);
                float grime = foot * (0.35f + 0.5f * patch) + ledge * (island ? 1f : 0.3f + 0.6f * age) +
                              age * 1.3f * math.smoothstep(0.52f, 0.78f, patch * 0.7f + fine * 0.3f);
                c.a = math.min(c.a, 1f - math.saturate(grime));
                return (n, Tinted(c, tint));
            });
            data.EmitFaceted(g[R].Part, R, FacetCos, (p, n, c) =>
            {
                float patch = noise.snoise(p * 0.6f + offset) * 0.5f + 0.5f;
                float settled = island ? 0f : math.smoothstep(0.3f, 0.7f, n.y) * math.smoothstep(0.8f - 0.45f * age, 0.95f - 0.45f * age, patch);
                c.a = math.min(c.a, 1f - settled);
                return (n, Tinted(c, math.lerp(new float3(1f), tint, 0.6f)));
            });
            data.EmitFaceted(g[T].Part, T, FacetCos, (p, n, c) => (n, Tinted(c, math.lerp(new float3(1f), tint, 0.5f))));
            data.EmitFaceted(g[G].Part, G, FacetCos, null);
            return data.ToMesh("Landmark_" + spec.Kind + "_" + spec.Variant + "_" + spec.Region + "_" + spec.Era);
        }

        private static Color Tinted(Color c, float3 tint)
        {
            // Same ceiling as ArchitectureMeshes: above it the toon ramp blows out instead of stepping.
            c.r = math.min(c.r * tint.x, 1.29f);
            c.g = math.min(c.g * tint.y, 1.29f);
            c.b = math.min(c.b * tint.z, 1.29f);
            return c;
        }

        // ---- towers ----------------------------------------------------------------------------------------------

        // Variant = function: 0 watch, 1 beacon, 2 bell, 3 observatory, 4 signal, 5 pagoda.
        private static void BuildTower(Geo[] g, in LandmarkSpec s, Tech tech, ref Pcg32 rng)
        {
            float h = s.HeightM;
            float r0 = s.FootprintRadiusM * 0.6f;
            int sides = tech == Tech.Future ? 12 : s.Region == ArchRegion.Storm || s.Region == ArchRegion.Crystal ? 8 : 4;
            // A flat face, not a corner, looks down the entrance axis (+Z).
            float turn = math.PI * 0.5f - math.PI / sides;
            float apothem = math.cos(math.PI / sides);
            float crown = math.clamp(h * 0.22f, 5f, 10f);
            float ys = h - crown;
            float taper = tech == Tech.Future ? 0.58f : tech == Tech.Machine ? 0.82f : 0.72f;
            // Future shells pinch to a waist and flare again; masonry tapers in a straight batter.
            float waist = tech == Tech.Future ? 0.16f : 0f;
            Func<float, float> radiusAt = y =>
            {
                float t = math.saturate(y / ys);
                return r0 * (math.lerp(1f, taper, t) - waist * math.sin(t * math.PI) + waist * 0.5f * t * t);
            };

            const int Courses = 8;
            var shaft = new float2[Courses + 4];
            float plinth = math.min(s.FootprintRadiusM, r0 * 1.22f);
            shaft[0] = new float2(plinth, -SinkM);
            shaft[1] = new float2(plinth, 1.6f);
            shaft[2] = new float2(radiusAt(2.1f), 2.1f);
            for (int i = 0; i <= Courses; i++)
            {
                float y = math.lerp(2.1f, ys, (float)i / Courses);
                shaft[3 + i] = new float2(radiusAt(y), y);
            }

            Lathe(g[W], float3.zero, new float3(0f, 1f, 0f), shaft, sides, turn, 0.86f, 1.06f);
            Terrace(g[W], s.FootprintRadiusM, 0.5f);

            // Door, lamp, and lit windows on alternating faces so the tower reads as inhabited from every side.
            float doorR = plinth * apothem;
            Box(g[T], new float3(0f, 0f, doorR - 0.25f), 1.0f, 0.4f, 2.7f, math.PI * 0.5f, 0.75f, 0.9f);
            Box(g[G], new float3(0f, 3.1f, doorR + 0.05f), 0.28f, 0.2f, 0.5f, math.PI * 0.5f, 1f, 1f);
            int floors = math.max(2, (int)((ys - 6f) / 4.5f));
            for (int f = 0; f < floors; f++)
            {
                float y = 6f + f * 4.5f;
                float ra = radiusAt(y + 0.7f) * apothem;
                for (int k = 0; k < sides; k++)
                {
                    if (((k + f) & 1) != 0 || (sides > 4 && (k + f) % 4 != 0))
                    {
                        continue;
                    }

                    float a = turn + (k + 0.5f) * math.PI * 2f / sides;
                    bool lit = rng.NextUInt(100) < (uint)(85f - 55f * s.Age);
                    Box(g[lit ? G : T], Dir(a) * (ra - 0.2f) + new float3(0f, y, 0f), 0.38f, 0.28f, 1.4f, a, lit ? 1f : 0.45f, lit ? 1f : 0.5f);
                    Box(g[T], Dir(a) * (ra - 0.1f) + new float3(0f, y - 0.22f, 0f), 0.6f, 0.3f, 0.2f, a, 0.8f, 0.9f);
                }
            }

            // String courses: stone belts on masonry, brass hoops on machines, light bands on future shells.
            int belts = math.max(1, (int)(ys / 7f));
            for (int b = 1; b <= belts; b++)
            {
                float y = ys * b / (belts + 1f);
                float rb = radiusAt(y);
                Geo band = tech == Tech.Future ? g[G] : g[T];
                float proud = tech == Tech.Future ? 0.06f : 0.22f;
                Lathe(band, float3.zero, new float3(0f, 1f, 0f), new[]
                {
                    new float2(rb, y - 0.22f), new float2(rb + proud, y - 0.14f), new float2(rb + proud, y + 0.14f), new float2(rb, y + 0.22f),
                }, sides, turn, 0.9f, 1f);
            }

            if (tech == Tech.Machine)
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = turn + (i * 2 + 1.5f) * math.PI * 2f / 6f;
                    Beam(g[T], Dir(a) * (radiusAt(1f) * apothem + 0.25f) + new float3(0f, 0.5f, 0f),
                        Dir(a) * (radiusAt(ys) * apothem + 0.25f) + new float3(0f, ys - 0.5f, 0f), 0.34f, 0.7f, 0.95f);
                }
            }

            float rt = radiusAt(ys);
            if (s.Variant == 5)
            {
                // Pagoda: an eave at every tier of the shaft, all the way down.
                int tiers = math.max(3, (int)(ys / 7f));
                for (int i = 1; i <= tiers; i++)
                {
                    float y = ys * i / (tiers + 1f);
                    Eave(g[R], y, radiusAt(y), 1.6f + 0.6f * (1f - (float)i / tiers), 1.3f, sides, turn, s.Region == ArchRegion.Twilight ? 0.7f : 0.35f);
                }
            }

            if (tech == Tech.Ancient)
            {
                TowerRuinCrown(g, ys, rt, h, sides, turn, ref rng);
                return;
            }

            // Corbelled gallery every function stands on.
            float deck = ys + 0.45f;
            float rg = rt * 1.32f;
            Lathe(g[W], float3.zero, new float3(0f, 1f, 0f), new[]
            {
                new float2(rt, ys - 1.6f), new float2(rg, ys), new float2(rg, deck), new float2(0f, deck),
            }, sides, turn, 0.95f, 1.08f);
            if (s.Variant != 5)
            {
                Merlons(tech == Tech.Machine || tech == Tech.Future ? g[T] : g[W], deck, rg * apothem - 0.2f, sides, turn, tech == Tech.Future ? 0.5f : 0.95f);
            }

            float top = h;
            switch (s.Variant)
            {
                case 0: // watch: a turret under the regional roof
                {
                    float rr = rt * 0.72f, yt = deck + crown * 0.45f;
                    Lathe(g[W], float3.zero, new float3(0f, 1f, 0f), new[] { new float2(rr, deck), new float2(rr, yt) }, sides, turn, 0.95f, 1.05f);
                    for (int k = 0; k < sides; k += sides / 4)
                    {
                        float a = turn + (k + 0.5f) * math.PI * 2f / sides;
                        Box(g[G], Dir(a) * (rr * apothem - 0.15f) + new float3(0f, deck + 0.9f, 0f), 0.35f, 0.2f, 1.2f, a, 1f, 1f);
                    }

                    RoofCap(g, yt, rr, top - yt, s.Region, tech, sides, turn);
                    break;
                }
                case 1: // beacon: a lantern room
                {
                    float rr = rt * 0.62f, yt = deck + 2.8f;
                    Lathe(g[G], float3.zero, new float3(0f, 1f, 0f), new[] { new float2(rr, deck), new float2(rr, yt) }, 12, 0f, 1f, 1f);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * math.PI / 3f;
                        Box(g[T], Dir(a) * (rr + 0.02f) + new float3(0f, deck, 0f), 0.09f, 0.09f, 2.8f, a, 0.7f, 0.8f);
                    }

                    RoofCap(g, yt, rr * 1.15f, math.max(2f, top - yt), s.Region, tech, sides, turn);
                    break;
                }
                case 2: // bell: an open belfry
                {
                    float yt = deck + 3.6f, post = rt * 0.82f;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = math.PI * 0.25f + k * math.PI * 0.5f;
                        Box(g[W], Dir(a) * post + new float3(0f, deck, 0f), 0.32f, 0.32f, 3.6f, a, 0.9f, 1f);
                    }

                    Beam(g[T], new float3(-post * 0.7f, yt - 0.3f, 0f), new float3(post * 0.7f, yt - 0.3f, 0f), 0.26f, 0.7f, 0.7f);
                    float rb = rt * 0.42f;
                    Lathe(g[T], new float3(0f, yt - 2.6f, 0f), new float3(0f, 1f, 0f), new[]
                    {
                        new float2(rb * 1.12f, 0f), new float2(rb, 0.25f), new float2(rb * 0.7f, 1.3f), new float2(rb * 0.3f, 1.9f), new float2(0f, 2f),
                    }, 10, 0f, 1.25f, 1.05f);
                    Lathe(g[W], float3.zero, new float3(0f, 1f, 0f), new[] { new float2(rt * 0.95f, yt), new float2(rt * 0.95f, yt + 0.4f) }, sides, turn, 1f, 1.05f);
                    RoofCap(g, yt + 0.4f, rt * 0.95f, math.max(2f, top - yt - 0.4f), s.Region, tech, sides, turn);
                    break;
                }
                case 3: // observatory: a dome, an armillary and something that should not float
                {
                    float rd = rt * 1.02f;
                    Lathe(g[R], float3.zero, new float3(0f, 1f, 0f), Arc(rd, deck, rd * 0.95f, 6), 14, 0f, 0.92f, 1.1f);
                    float3 orb = new float3(0f, deck + rd * 0.95f + 2.6f, 0f);
                    Lathe(g[G], orb, new float3(0f, 1f, 0f), Ball(0.75f, 5), 8, 0f, 1f, 1f);
                    Torus(g[T], orb, math.normalize(new float3(0.5f, 1f, 0.2f)), 1.7f, 0.09f, 20, 4, 1.15f);
                    Torus(g[T], orb, math.normalize(new float3(-0.6f, 0.6f, 0.5f)), 2.15f, 0.09f, 20, 4, 1.05f);
                    Box(g[G], new float3(0f, deck + 0.3f, rd * 0.86f), 0.3f, 0.25f, rd * 0.6f, math.PI * 0.5f, 1f, 1f);
                    break;
                }
                case 4: // signal: a mast
                {
                    float3 foot = new float3(0f, deck, 0f), tip = new float3(0f, top + 3f, 0f);
                    if (tech == Tech.Future)
                    {
                        Beam(g[T], foot, tip, 0.3f, 0.8f, 1f);
                        for (int i = 0; i < 3; i++)
                        {
                            float a = i * math.PI * 2f / 3f + 0.4f;
                            float3 at = new float3(0f, math.lerp(deck, top, 0.35f + 0.2f * i), 0f) + Dir(a) * 0.9f;
                            Lathe(g[W], at, math.normalize(Dir(a) + new float3(0f, 0.6f, 0f)), new[]
                            {
                                new float2(0.1f, 0f), new float2(0.9f, 0.35f), new float2(1.05f, 0.5f), new float2(0f, 0.3f),
                            }, 10, 0f, 0.95f, 1.1f);
                        }
                    }
                    else
                    {
                        float spread = rt * 0.55f;
                        for (int k = 0; k < 4; k++)
                        {
                            float a = math.PI * 0.25f + k * math.PI * 0.5f;
                            Beam(g[T], foot + Dir(a) * spread, new float3(0f, top, 0f), 0.2f, 0.7f, 0.95f);
                            Beam(g[T], foot + Dir(a) * spread * 0.66f + new float3(0f, (top - deck) * 0.33f, 0f),
                                foot + Dir(a + math.PI * 0.5f) * spread * 0.33f + new float3(0f, (top - deck) * 0.66f, 0f), 0.12f, 0.75f, 0.85f);
                        }

                        Beam(g[T], new float3(0f, top, 0f), tip, 0.14f, 0.9f, 1f);
                        for (int i = 0; i < 3; i++)
                        {
                            float y = math.lerp(deck, top, 0.5f + 0.2f * i), half = 1.6f - 0.4f * i;
                            float a = i * 1.1f;
                            Beam(g[T], new float3(0f, y, 0f) - Dir(a) * half, new float3(0f, y, 0f) + Dir(a) * half, 0.12f, 0.9f, 0.9f);
                        }
                    }

                    Lathe(g[G], tip, new float3(0f, 1f, 0f), Ball(0.4f, 4), 6, 0f, 1f, 1f);
                    break;
                }
                default: // pagoda: the top eave and a ringed finial
                {
                    Eave(g[R], deck, rt, 2.2f, top - deck, sides, turn, s.Region == ArchRegion.Twilight ? 0.8f : 0.4f);
                    Beam(g[T], new float3(0f, top - 0.5f, 0f), new float3(0f, top + 3.2f, 0f), 0.16f, 1f, 1.2f);
                    for (int i = 0; i < 3; i++)
                    {
                        Torus(g[T], new float3(0f, top + 0.6f + i * 0.7f, 0f), new float3(0f, 1f, 0f), 0.5f - 0.1f * i, 0.07f, 10, 4, 1.2f);
                    }

                    Lathe(g[G], new float3(0f, top + 3.3f, 0f), new float3(0f, 1f, 0f), Ball(0.26f, 4), 6, 0f, 1f, 1f);
                    break;
                }
            }
        }

        // A broken wall head, and above it the stones that fell off — still waiting to land.
        private static void TowerRuinCrown(Geo[] g, float ys, float rt, float h, int sides, float turn, ref Pcg32 rng)
        {
            var drop = new float[sides];
            for (int k = 0; k < sides; k++)
            {
                drop[k] = rng.NextFloat();
            }

            Lathe(g[W], float3.zero, new float3(0f, 1f, 0f), new[]
            {
                new float2(rt, ys), new float2(rt, ys + 3.2f), new float2(rt * 0.72f, ys + 3.2f), new float2(rt * 0.72f, ys - 0.5f),
            }, sides, turn, 0.9f, 1.02f, (i, k, p) => i == 1 || i == 2 ? p - new float3(0f, drop[k] * 2.8f, 0f) : p);
            Lathe(g[W], float3.zero, new float3(0f, 1f, 0f), new[] { new float2(rt * 0.72f, ys - 0.5f), new float2(0f, ys - 0.5f) }, sides, turn, 0.6f, 0.6f);

            float span = h - ys;
            Torus(g[G], new float3(0f, ys + span * 0.5f, 0f), new float3(0f, 1f, 0f), rt * 1.25f, 0.1f, 28, 4, 1f);
            Lathe(g[G], new float3(0f, ys + span * 0.5f - 0.9f, 0f), new float3(0f, 1f, 0f), new[]
            {
                new float2(0f, 0f), new float2(0.55f, 0.9f), new float2(0f, 1.8f),
            }, 4, 0f, 1f, 1f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 2.4f + rng.NextFloat();
                float y = ys + 1.5f + span * (i + rng.NextFloat()) / 6f;
                float size = 0.5f + 0.7f * rng.NextFloat();
                int first = g[W].Part.P.Count;
                Box(g[W], float3.zero, size, size * 0.7f, size * 1.2f, a, 0.85f, 1.02f);
                Rotate(g[W], first, math.normalize(new float3(rng.NextFloat() - 0.5f, rng.NextFloat(), rng.NextFloat() - 0.5f)),
                    rng.NextFloat() * 2f, Dir(a) * rt * (0.5f + 0.9f * rng.NextFloat()) + new float3(0f, y, 0f));
            }
        }

        // Regional roof on a turret of radius r at height y, about `rise` tall.
        private static void RoofCap(Geo[] g, float y, float r, float rise, ArchRegion region, Tech tech, int sides, float turn)
        {
            var up = new float3(0f, 1f, 0f);
            if (tech == Tech.Future)
            {
                Lathe(g[R], float3.zero, up, Arc(r * 1.05f, y, math.min(rise, r), 5), 12, 0f, 0.95f, 1.1f);
                Beam(g[T], new float3(0f, y + math.min(rise, r) - 0.2f, 0f), new float3(0f, y + rise + 2.5f, 0f), 0.1f, 0.9f, 1f);
                Lathe(g[G], new float3(0f, y + rise + 2.5f, 0f), up, Ball(0.22f, 4), 6, 0f, 1f, 1f);
                return;
            }

            if (tech == Tech.Machine)
            {
                Lathe(g[R], float3.zero, up, new[]
                {
                    new float2(r + 0.5f, y), new float2(r * 0.8f, y + rise * 0.6f), new float2(r * 0.35f, y + rise * 0.82f), new float2(0f, y + rise * 0.86f),
                }, sides, turn, 0.9f, 1.08f);
                Beam(g[T], new float3(r * 0.35f, y + rise * 0.5f, 0f), new float3(r * 0.35f, y + rise + 1.5f, 0f), 0.5f, 0.75f, 0.9f);
                Box(g[G], new float3(r * 0.35f, y + rise + 1.5f, 0f), 0.2f, 0.2f, 0.18f, 0f, 1f, 1f);
                return;
            }

            switch (region)
            {
                case ArchRegion.Lava:
                    Lathe(g[W], float3.zero, up, new[] { new float2(r + 0.3f, y), new float2(r + 0.3f, y + 0.5f), new float2(0f, y + 0.5f) }, sides, turn, 0.95f, 1.05f);
                    Merlons(g[W], y + 0.5f, (r + 0.3f) * math.cos(math.PI / sides) - 0.2f, sides, turn, 1.1f);
                    Lathe(g[T], new float3(0f, y + 0.5f, 0f), up, new[] { new float2(0.5f, 0f), new float2(0.9f, 0.9f), new float2(0f, 0.8f) }, 8, 0f, 0.6f, 0.8f);
                    Lathe(g[G], new float3(0f, y + 1.3f, 0f), up, new[] { new float2(0.7f, 0f), new float2(0.3f, 0.9f), new float2(0f, 1.5f) }, 6, 0f, 1f, 1f);
                    break;
                case ArchRegion.Ice:
                    Eave(g[R], y, r, 0.5f, math.max(rise, r * 3f), sides, turn, 0f);
                    break;
                case ArchRegion.Storm:
                    Eave(g[R], y, r, 0.7f, rise * 0.55f, sides, turn, 0.1f);
                    Beam(g[T], new float3(0f, y + rise * 0.5f, 0f), new float3(0f, y + rise + 3f, 0f), 0.09f, 0.9f, 1f);
                    Lathe(g[G], new float3(0f, y + rise + 3f, 0f), up, Ball(0.2f, 4), 6, 0f, 1f, 1f);
                    break;
                case ArchRegion.Crystal:
                    Lathe(g[R], float3.zero, up, new[]
                    {
                        new float2(r * 0.9f, y), new float2(r * 1.18f, y + rise * 0.3f), new float2(r * 0.95f, y + rise * 0.58f),
                        new float2(r * 0.35f, y + rise * 0.82f), new float2(0.08f, y + rise), new float2(0f, y + rise + 0.8f),
                    }, 12, 0f, 0.92f, 1.12f);
                    break;
                case ArchRegion.Twilight:
                    Eave(g[R], y, r, 1.5f, rise, sides, turn, 0.9f);
                    Lathe(g[G], new float3(0f, y + rise, 0f), up, Ball(0.24f, 4), 6, 0f, 1f, 1f);
                    break;
                default:
                    Eave(g[R], y, r, 0.8f, rise, sides, turn, tech == Tech.Rustic ? 0.2f : 0.05f);
                    break;
            }
        }

        // A roof skirt from the wall at (r, y): underside, lip, then the covering up to a point `rise` above. `sweep`
        // > 0 hollows the covering so the eave corners lift.
        private static void Eave(Geo geo, float y, float r, float overhang, float rise, int sides, float turn, float sweep)
        {
            float ro = r + overhang;
            float lift = sweep * overhang * 0.45f;
            Lathe(geo, float3.zero, new float3(0f, 1f, 0f), new[]
            {
                new float2(r * 0.96f, y - 0.2f), new float2(ro, y - 0.05f + lift), new float2(ro, y + 0.12f + lift),
                new float2(math.lerp(ro, 0f, 0.4f), y + rise * (0.4f - 0.22f * sweep)),
                new float2(math.lerp(ro, 0f, 0.75f), y + rise * (0.75f - 0.2f * sweep)), new float2(0f, y + rise),
            }, sides, turn, 0.88f, 1.08f);
        }

        private static void Merlons(Geo geo, float y, float apothemM, int sides, float turn, float height)
        {
            float side = 2f * apothemM * math.tan(math.PI / sides);
            int per = math.max(1, (int)(side / 1.5f));
            for (int k = 0; k < sides; k++)
            {
                float a = turn + (k + 0.5f) * math.PI * 2f / sides;
                float3 across = new float3(-math.sin(a), 0f, math.cos(a));
                for (int i = 0; i < per; i++)
                {
                    float t = ((i + 0.5f) / per - 0.5f) * side;
                    Box(geo, Dir(a) * apothemM + across * t + new float3(0f, y, 0f), side / per * 0.3f, 0.2f, height, a, 0.95f, 1.05f);
                }
            }
        }

        // ---- temples ---------------------------------------------------------------------------------------------

        // Variant: 0 stepped pyramid, 1 columned hall, 2 great dome.
        private static void BuildTemple(Geo[] g, in LandmarkSpec s, Tech tech, ref Pcg32 rng)
        {
            float fr = s.FootprintRadiusM, h = s.HeightM;
            var up = new float3(0f, 1f, 0f);
            bool ruin = tech == Tech.Ancient;
            Geo line = tech == Tech.Future ? g[G] : g[T];
            if (s.Variant == 0)
            {
                int tiers = 4;
                float half = fr * 0.7f, step = h * 0.72f / tiers;
                Terrace(g[W], fr, 0.4f);
                for (int i = 0; i < tiers; i++)
                {
                    float hw = half * (1f - 0.19f * i);
                    Box(g[W], new float3(0f, i == 0 ? -SinkM : i * step, 0f), hw, hw, i == 0 ? step + SinkM : step, math.PI * 0.5f, 0.86f + 0.04f * i, 0.98f + 0.04f * i, 0.94f);
                    Box(line, new float3(0f, (i + 1) * step - 0.16f, 0f), hw * 0.95f, hw * 0.95f, 0.16f, math.PI * 0.5f, 1f, 1f);
                }

                // One stair straight up the entrance face.
                int treads = 14;
                float top = tiers * step, run = half * (1f - 0.19f * (tiers - 1)) * 0.94f;
                for (int i = 0; i < treads; i++)
                {
                    float t0 = (float)i / treads;
                    float z = math.lerp(half + 0.6f, run, t0);
                    Box(g[T], new float3(0f, -0.5f, z), 1.5f, (half - run) / treads * 0.6f + 0.2f, top * (i + 1f) / treads + 0.5f, math.PI * 0.5f, 0.8f, 0.95f);
                }

                float sw = half * (1f - 0.19f * (tiers - 1)) * 0.5f, sh = h - top;
                Box(g[W], new float3(0f, top, 0f), sw, sw, sh * 0.72f, math.PI * 0.5f, 0.95f, 1.08f, 0.92f);
                Box(g[G], new float3(0f, top, sw * 0.96f), sw * 0.3f, 0.16f, sh * 0.5f, math.PI * 0.5f, 1f, 1f);
                if (!ruin)
                {
                    Box(g[R], new float3(0f, top + sh * 0.72f, 0f), sw * 1.25f, sw * 1.25f, sh * 0.28f, math.PI * 0.5f, 0.92f, 1.08f, 0.55f);
                }

                for (int k = 0; k < 4; k++)
                {
                    float a = math.PI * 0.25f + k * math.PI * 0.5f;
                    float3 at = Dir(a) * (half * 0.9f * math.SQRT2) + new float3(0f, step, 0f);
                    Lathe(g[T], at, up, new[] { new float2(0.3f, 0f), new float2(0.22f, 1.1f), new float2(0.6f, 1.5f), new float2(0f, 1.4f) }, 8, 0f, 0.7f, 0.9f);
                    Lathe(g[G], at + new float3(0f, 1.45f, 0f), up, new[] { new float2(0.42f, 0f), new float2(0.2f, 0.6f), new float2(0f, 1.1f) }, 6, 0f, 1f, 1f);
                }

                return;
            }

            if (s.Variant == 1)
            {
                float hx = fr * 0.45f, hz = fr * 0.68f, baseH = 1.5f;
                Terrace(g[W], fr, 0.3f);
                for (int i = 0; i < 3; i++)
                {
                    Box(g[W], new float3(0f, i == 0 ? -SinkM : i * 0.5f, 0f), hx + 1.2f - 0.4f * i, hz + 1.2f - 0.4f * i, i == 0 ? 0.5f + SinkM : 0.5f, math.PI * 0.5f, 0.85f + 0.05f * i,
                        0.95f + 0.05f * i);
                }

                float colH = h * 0.6f, colR = math.max(0.45f, fr * 0.045f);
                int nx = 4, nz = 7;
                for (int ix = 0; ix < nx; ix++)
                {
                    for (int iz = 0; iz < nz; iz++)
                    {
                        if (ix != 0 && ix != nx - 1 && iz != 0 && iz != nz - 1)
                        {
                            continue;
                        }

                        float x = math.lerp(-hx, hx, ix / (nx - 1f)), z = math.lerp(-hz, hz, iz / (nz - 1f));
                        // Ruins keep every column's stump and lose the rest at random.
                        float keep = ruin ? 0.25f + 0.75f * rng.NextFloat() : 1f;
                        float ch = colH * keep;
                        Lathe(g[W], new float3(x, baseH, z), up, new[]
                        {
                            new float2(colR * 1.35f, 0f), new float2(colR * 1.35f, 0.3f), new float2(colR, 0.5f), new float2(colR * 0.86f, ch - 0.4f),
                            new float2(colR * 1.3f, ch - 0.15f), new float2(colR * 1.3f, ch), new float2(0f, ch),
                        }, 8, 0f, 0.92f, 1.08f);
                    }
                }

                Box(g[W], new float3(0f, baseH, 0f), hx * 0.62f, hz * 0.72f, colH * (ruin ? 0.6f : 1f), math.PI * 0.5f, 0.82f, 0.98f);
                Box(g[G], new float3(0f, baseH, hz * 0.72f), hx * 0.2f, 0.14f, colH * 0.5f, math.PI * 0.5f, 1f, 1f);
                if (!ruin)
                {
                    float ye = baseH + colH;
                    Box(g[W], new float3(0f, ye, 0f), hx + colR * 1.6f, hz + colR * 1.6f, h * 0.08f, math.PI * 0.5f, 1f, 1.08f);
                    Box(line, new float3(0f, ye + h * 0.08f, 0f), hx + colR * 1.9f, hz + colR * 1.9f, 0.2f, math.PI * 0.5f, 1f, 1f);
                    Gable(g[R], g[W], new float3(0f, ye + h * 0.08f + 0.2f, 0f), hx + colR * 2.2f, hz + colR * 2.4f, h - ye - h * 0.08f - 0.2f, s.Region == ArchRegion.Twilight ? 0.6f : 0f);
                }

                return;
            }

            // Great dome on a round stepped drum, a ringed spire, and four small shrines around it.
            float rd = fr * 0.62f;
            Lathe(g[W], float3.zero, up, new[]
            {
                new float2(fr * 0.92f, -SinkM), new float2(fr * 0.92f, 0.6f), new float2(fr * 0.8f, 0.6f), new float2(fr * 0.8f, 1.3f),
                new float2(rd * 1.08f, 1.3f), new float2(rd * 1.08f, h * 0.22f), new float2(rd, h * 0.22f),
            }, 16, 0f, 0.84f, 1.02f);
            Lathe(line, float3.zero, up, new[] { new float2(rd * 1.1f, h * 0.22f - 0.3f), new float2(rd * 1.14f, h * 0.22f - 0.15f), new float2(rd * 1.1f, h * 0.22f) }, 16, 0f, 1f, 1f);
            float domeH = h * 0.5f;
            if (ruin)
            {
                // A third of the shell is gone; the rest stands as a broken bowl.
                var drop = new float[16];
                for (int k = 0; k < 16; k++)
                {
                    drop[k] = math.smoothstep(0.35f, 0.75f, noise.snoise(new float2(k * 0.45f, rng.NextFloat() * 0.01f + s.Seed % 97)) * 0.5f + 0.5f);
                }

                float2[] arc = Arc(rd, h * 0.22f, domeH, 6, 1.15f);
                Lathe(g[R], float3.zero, up, arc, 16, 0f, 0.9f, 1.1f, (i, k, p) => new float3(p.x, math.lerp(p.y, h * 0.22f + (p.y - h * 0.22f) * 0.35f, drop[k]), p.z));
            }
            else
            {
                Lathe(g[R], float3.zero, up, Arc(rd, h * 0.22f, domeH, 7, 1.15f), 16, 0f, 0.9f, 1.12f);
                float ys = h * 0.22f + domeH;
                Box(g[W], new float3(0f, ys - 0.5f, 0f), rd * 0.2f, rd * 0.2f, 1.2f, math.PI * 0.5f, 1f, 1.1f);
                Beam(g[T], new float3(0f, ys + 0.6f, 0f), new float3(0f, h + 1.5f, 0f), 0.22f, 1f, 1.2f);
                for (int i = 0; i < 4; i++)
                {
                    Torus(g[T], new float3(0f, math.lerp(ys + 1f, h, i / 4f), 0f), up, rd * 0.16f * (1f - 0.18f * i), 0.09f, 12, 4, 1.2f);
                }

                Lathe(g[G], new float3(0f, h + 1.5f, 0f), up, Ball(0.4f, 4), 6, 0f, 1f, 1f);
            }

            Box(g[G], new float3(0f, 1.3f, rd * 1.06f), 1.1f, 0.2f, h * 0.15f, math.PI * 0.5f, 1f, 1f);
            for (int k = 0; k < 4; k++)
            {
                float a = math.PI * 0.25f + k * math.PI * 0.5f;
                float3 at = Dir(a) * fr * 0.8f + new float3(0f, 0.6f, 0f);
                float rs = fr * 0.1f;
                Lathe(g[W], at, up, new[] { new float2(rs, 0f), new float2(rs, rs * 1.2f) }, 8, 0f, 0.9f, 1f);
                Lathe(g[R], at + new float3(0f, rs * 1.2f, 0f), up, Arc(rs, 0f, rs * 1.3f, 4, 1.2f), 8, 0f, 0.95f, 1.1f);
                Lathe(g[G], at + new float3(0f, rs * 2.7f, 0f), up, Ball(0.18f, 3), 6, 0f, 1f, 1f);
            }
        }

        // Gable roof along Z over `centre` (the middle of its eave line) with pediments; `sweep` lifts the eave ends.
        private static void Gable(Geo roof, Geo wall, float3 centre, float hx, float hz, float rise, float sweep)
        {
            const int Rows = 6;
            for (int side = -1; side <= 1; side += 2)
            {
                int first = roof.Part.P.Count;
                for (int j = 0; j <= Rows; j++)
                {
                    float v = (float)j / Rows;
                    float z = math.lerp(-hz, hz, v);
                    float lift = sweep * 1.2f * math.pow(math.abs(v * 2f - 1f), 3f);
                    roof.V(centre + new float3(side * hx, lift, z), 0.9f);
                    roof.V(centre + new float3(0f, rise + lift * 0.4f, z), 1.1f);
                }

                for (int j = 0; j < Rows; j++)
                {
                    int a = first + j * 2;
                    roof.Quad(a, a + 1, a + 3, a + 2, new float3(side * rise, hx, 0f));
                }
            }

            for (int end = -1; end <= 1; end += 2)
            {
                float lift = sweep * 1.2f;
                int a = wall.V(centre + new float3(-hx * 0.96f, 0f, end * hz * 0.97f), 0.95f);
                int b = wall.V(centre + new float3(hx * 0.96f, 0f, end * hz * 0.97f), 0.95f);
                int c = wall.V(centre + new float3(0f, rise * 0.96f + lift * 0.4f, end * hz * 0.97f), 1.08f);
                wall.Tri(a, b, c, new float3(0f, 0f, end));
            }
        }

        // ---- sky islands -----------------------------------------------------------------------------------------

        // Variant: 0 grove, 1 ruin, 2 crystal. Origin = centre of the top surface.
        private static void BuildSkyIsland(Geo[] g, in LandmarkSpec s, ref Pcg32 rng)
        {
            float r = s.FootprintRadiusM, depth = s.HeightM;
            var up = new float3(0f, 1f, 0f);
            float3 seed = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 37f;
            const int Sides = 28;
            Func<float, float> rim = a => 0.78f + 0.22f * noise.snoise(new float2(math.cos(a), math.sin(a)) * 1.3f + seed.xy);

            // Turf: a gently domed top whose edge follows the same wobble as the rock below it.
            Lathe(g[R], float3.zero, up, new[]
            {
                new float2(r, -0.6f), new float2(r, 0f), new float2(r * 0.7f, 0.9f), new float2(r * 0.35f, 1.5f), new float2(0f, 1.7f),
            }, Sides, 0f, 0.86f, 1.06f, (i, k, p) =>
            {
                float a = k * math.PI * 2f / Sides;
                float bump = 0.5f * noise.snoise(new float3(p.x, p.z, 3f) * 0.12f + seed);
                return new float3(p.x * rim(a), p.y + (i >= 2 ? bump : 0f), p.z * rim(a));
            });

            // Rock: listed from the lowest tip up to the rim, so each band faces out and down.
            const int Bands = 7;
            var under = new float2[Bands + 1];
            for (int i = 0; i <= Bands; i++)
            {
                float t = (float)i / Bands;
                under[i] = new float2(r * math.pow(t, 0.62f), -depth * (1f - t) * (1f - t) - 0.5f * t - 0.1f);
            }

            Lathe(g[W], float3.zero, up, under, Sides, 0f, 0.62f, 1f, (i, k, p) =>
            {
                float a = k * math.PI * 2f / Sides;
                float t = (float)i / Bands;
                // Lobes: the underside hangs in several roots rather than one cone.
                float lobe = 1f + 0.38f * (1f - t) * noise.snoise(new float3(math.cos(a) * 1.6f, math.sin(a) * 1.6f, t * 2f) + seed);
                float sag = (1f - t) * depth * 0.3f * noise.snoise(new float2(math.cos(a), math.sin(a)) * 2.2f + seed.yz);
                float edge = math.lerp(lobe, rim(a), t * t);
                return new float3(p.x * edge, p.y + sag, p.z * edge);
            });

            // Broken-off pieces keeping the island company.
            for (int i = 0; i < 7; i++)
            {
                float a = rng.NextFloat() * math.PI * 2f, d = r * (0.9f + 0.7f * rng.NextFloat());
                float size = 0.8f + 2.2f * rng.NextFloat() * rng.NextFloat();
                float3 at = Dir(a) * d + new float3(0f, -depth * (0.15f + 0.7f * rng.NextFloat()), 0f);
                Lathe(g[W], at, math.normalize(new float3(rng.NextFloat() - 0.5f, 1.5f, rng.NextFloat() - 0.5f)), new[]
                {
                    new float2(0f, -size * 1.4f), new float2(size * 0.6f, -size * 0.5f), new float2(size, 0f),
                }, 6, rng.NextFloat(), 0.65f, 1f);
                Lathe(g[R], at, math.normalize(new float3(rng.NextFloat() - 0.5f, 1.5f, rng.NextFloat() - 0.5f)), new[]
                {
                    new float2(size, 0f), new float2(size * 0.5f, size * 0.3f), new float2(0f, size * 0.36f),
                }, 6, rng.NextFloat(), 0.9f, 1.05f);
            }

            // A stream that runs off the edge and never reaches the ground.
            float fa = rng.NextFloat() * math.PI * 2f;
            float3 lip = Dir(fa) * r * rim(fa) * 0.98f;
            float3 across = new float3(-math.sin(fa), 0f, math.cos(fa));
            float fall = depth * 1.6f + 14f;
            int f0 = g[G].Part.P.Count;
            const int FallRows = 8;
            for (int j = 0; j <= FallRows; j++)
            {
                float v = (float)j / FallRows;
                float width = math.lerp(1.3f, 0.15f, v * v);
                float3 c = lip + Dir(fa) * (0.3f + 1.2f * v) - up * (fall * v * (0.4f + 0.6f * v));
                g[G].V(c - across * width, 1f);
                g[G].V(c + across * width, 1f);
            }

            for (int j = 0; j < FallRows; j++)
            {
                int a = f0 + j * 2;
                g[G].Quad(a, a + 1, a + 3, a + 2, Dir(fa));
                g[G].Quad(a, a + 1, a + 3, a + 2, -Dir(fa));
            }

            if (s.Variant == 0)
            {
                for (int i = 0; i < 9; i++)
                {
                    float a = rng.NextFloat() * math.PI * 2f, d = r * 0.62f * math.sqrt(rng.NextFloat());
                    float th = 3f + 4f * rng.NextFloat();
                    float3 at = Dir(a) * d + new float3(0f, 0.8f, 0f);
                    Beam(g[T], at, at + new float3(0f, th, 0f), 0.35f + th * 0.03f, 0.75f, 0.95f);
                    for (int b = 0; b < 3; b++)
                    {
                        float cr = th * (0.42f - 0.08f * b);
                        float3 c = at + new float3((rng.NextFloat() - 0.5f) * cr, th + b * cr * 0.55f, (rng.NextFloat() - 0.5f) * cr);
                        Lathe(g[R], c, up, Ball(cr, 4), 7, rng.NextFloat(), 0.7f, 1.12f);
                    }
                }
            }
            else if (s.Variant == 1)
            {
                float ring = r * 0.42f;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * math.PI * 0.25f;
                    float ch = 2f + 5f * rng.NextFloat();
                    Lathe(g[W], Dir(a) * ring + new float3(0f, 0.6f, 0f), up, new[]
                    {
                        new float2(0.75f, 0f), new float2(0.55f, 0.4f), new float2(0.48f, ch), new float2(0f, ch),
                    }, 8, 0f, 0.95f, 1.1f);
                    if (ch > 5.2f && k < 7)
                    {
                        float3 from = Dir(a) * ring + new float3(0f, 0.6f + ch, 0f);
                        Beam(g[W], from, Dir(a + math.PI * 0.25f) * ring + new float3(0f, 0.6f + ch, 0f), 0.8f, 1.02f, 1.1f);
                    }
                }

                Lathe(g[W], new float3(0f, 0.9f, 0f), up, new[] { new float2(2.2f, 0f), new float2(2f, 0.8f), new float2(0f, 0.8f) }, 8, 0f, 0.95f, 1.08f);
                Lathe(g[G], new float3(0f, 2.6f, 0f), up, new[] { new float2(0f, 0f), new float2(0.7f, 1.1f), new float2(0f, 2.4f) }, 4, 0f, 1f, 1f);
                Torus(g[G], new float3(0f, 1.75f, 0f), up, 1.5f, 0.07f, 16, 4, 1f);
            }
            else
            {
                for (int i = 0; i < 11; i++)
                {
                    bool below = i >= 7;
                    float a = rng.NextFloat() * math.PI * 2f, d = r * (below ? 0.45f : 0.55f) * math.sqrt(rng.NextFloat());
                    float len = (below ? 4f : 3f) + 7f * rng.NextFloat() * rng.NextFloat();
                    float3 lean = math.normalize(new float3(rng.NextFloat() - 0.5f, below ? -2.2f : 2.2f, rng.NextFloat() - 0.5f));
                    float3 foot = Dir(a) * d + new float3(0f, below ? -depth * 0.45f : 0.6f, 0f);
                    Lathe(g[G], foot, lean, new[]
                    {
                        new float2(len * 0.1f, -0.5f), new float2(len * 0.14f, len * 0.72f), new float2(0f, len),
                    }, 6, rng.NextFloat(), 1f, 1f);
                }
            }
        }

        // ---- space elevator and station --------------------------------------------------------------------------

        // Variant: 0 ribbon with a climber, 1 three cables with spacer rings. Ancient: a megalith shooting a pillar of light.
        private static void BuildElevator(Geo[] g, in LandmarkSpec s, Tech tech, ref Pcg32 rng)
        {
            float fr = s.FootprintRadiusM, h = s.HeightM;
            var up = new float3(0f, 1f, 0f);
            float collar = 16f;
            if (tech == Tech.Ancient)
            {
                Lathe(g[W], float3.zero, up, new[]
                {
                    new float2(fr * 0.95f, -SinkM), new float2(fr * 0.95f, 0.8f), new float2(fr * 0.8f, 0.8f), new float2(fr * 0.8f, 1.6f),
                    new float2(fr * 0.42f, 1.6f), new float2(fr * 0.42f, 2.2f), new float2(0f, 2.2f),
                }, 6, 0f, 0.82f, 1f);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * math.PI / 3f + math.PI / 6f;
                    float ph = 9f + 8f * rng.NextFloat();
                    int first = g[W].Part.P.Count;
                    Box(g[W], new float3(0f, -1f, 0f), 1.1f, 0.8f, ph, a, 0.8f, 1.02f, 0.6f);
                    // Megaliths lean inward over the beam, like fingers around a flame.
                    Rotate(g[W], first, new float3(-math.sin(a), 0f, math.cos(a)), 0.16f, Dir(a) * fr * 0.72f + new float3(0f, 0.8f, 0f));
                    Box(g[G], Dir(a) * (fr * 0.72f - 0.85f) + new float3(0f, 2.5f, 0f), 0.2f, 0.08f, ph * 0.4f, a, 1f, 1f);
                }

                Lathe(g[G], float3.zero, up, new[] { new float2(1.5f, 2.2f), new float2(0.9f, 9f), new float2(0.55f, 40f), new float2(0.45f, h) }, 8, 0f, 1f, 1f);
                for (int i = 0; i < 5; i++)
                {
                    Torus(g[G], new float3(0f, 6f + i * i * 7f, 0f), up, 3.2f - 0.4f * i, 0.09f, 24, 4, 1f);
                }

                return;
            }

            // Anchor terminal: a drum, a domed concourse, and three fins carrying the tether collar.
            Lathe(g[W], float3.zero, up, new[]
            {
                new float2(fr * 0.96f, -SinkM), new float2(fr * 0.96f, 1.2f), new float2(fr * 0.86f, 1.6f), new float2(fr * 0.8f, 4.4f),
                new float2(fr * 0.62f, 6.2f), new float2(fr * 0.3f, 7.4f), new float2(1.4f, 8f), new float2(1.1f, collar),
            }, 18, 0f, 0.86f, 1.08f);
            Lathe(g[G], float3.zero, up, new[] { new float2(fr * 0.87f, 2.6f), new float2(fr * 0.855f, 3.3f) }, 18, 0f, 1f, 1f);
            Box(g[T], new float3(0f, 0f, fr * 0.86f), 1.5f, 0.5f, 3f, math.PI * 0.5f, 0.6f, 0.75f);
            Box(g[G], new float3(0f, 0f, fr * 0.9f), 1.1f, 0.3f, 2.5f, math.PI * 0.5f, 1f, 1f);
            for (int k = 0; k < 3; k++)
            {
                float a = math.PI * 0.5f + math.PI / 3f + k * math.PI * 2f / 3f;
                float3 across = new float3(-math.sin(a), 0f, math.cos(a)) * 0.35f;
                float3 foot = Dir(a) * fr * 0.93f, knee = Dir(a) * fr * 0.5f + new float3(0f, 9f, 0f), head = Dir(a) * 1.2f + new float3(0f, collar, 0f);
                Hexa(g[W], new[]
                {
                    foot - across - up * SinkM, foot + across - up * SinkM, Dir(a) * fr * 0.5f + across - up * SinkM, Dir(a) * fr * 0.5f - across - up * SinkM,
                    knee - across + Dir(a) * 0.6f, knee + across + Dir(a) * 0.6f, knee + across - Dir(a) * 2f, knee - across - Dir(a) * 2f,
                }, 0.85f, 1.05f);
                Hexa(g[W], new[]
                {
                    knee - across + Dir(a) * 0.6f, knee + across + Dir(a) * 0.6f, knee + across - Dir(a) * 2f, knee - across - Dir(a) * 2f,
                    head - across * 0.6f + Dir(a) * 0.4f, head + across * 0.6f + Dir(a) * 0.4f, head + across * 0.6f - Dir(a) * 0.4f, head - across * 0.6f - Dir(a) * 0.4f,
                }, 1.05f, 1.12f);
                Box(g[G], foot * 0.97f + new float3(0f, 5f, 0f), 0.12f, 0.1f, 0.5f, a, 1f, 1f);
            }

            Torus(g[T], new float3(0f, collar, 0f), up, 1.7f, 0.35f, 16, 6, 1.1f);
            if (s.Variant == 0)
            {
                Lathe(g[T], float3.zero, up, new[] { new float2(0.55f, collar), new float2(0.42f, h * 0.5f), new float2(0.42f, h) }, 6, 0f, 0.9f, 1.1f);
                // Marker lights thin out with height: dense where the eye can still tell them apart.
                for (float y = collar + 8f; y < h; y += 12f + y * 0.08f)
                {
                    Torus(g[G], new float3(0f, y, 0f), up, 0.6f, 0.1f, 8, 4, 1f);
                }

                float yc = collar + (h - collar) * 0.16f;
                Lathe(g[W], new float3(0f, yc, 0f), up, new[]
                {
                    new float2(0.5f, -2.6f), new float2(1.9f, -1.6f), new float2(2.1f, 0f), new float2(1.9f, 1.6f), new float2(0.5f, 2.6f),
                }, 12, 0f, 0.9f, 1.12f);
                Torus(g[G], new float3(0f, yc, 0f), up, 2.12f, 0.12f, 16, 4, 1f);
            }
            else
            {
                for (int k = 0; k < 3; k++)
                {
                    float a = k * math.PI * 2f / 3f;
                    Beam(g[T], Dir(a) * 1.3f + new float3(0f, collar, 0f), Dir(a) * 1.3f + new float3(0f, h, 0f), 0.22f, 0.9f, 1.1f);
                }

                for (float y = collar + 10f; y < h; y += 24f)
                {
                    Torus(g[T], new float3(0f, y, 0f), up, 1.45f, 0.12f, 12, 4, 1.05f);
                    Torus(g[G], new float3(0f, y + 0.3f, 0f), up, 1.5f, 0.05f, 12, 4, 1f);
                }
            }
        }

        // Variant: 0 wheel, 1 spindle with panel arms. Ancient: a monolith inside rune rings. Origin = hub.
        private static void BuildStation(Geo[] g, in LandmarkSpec s, Tech tech, ref Pcg32 rng)
        {
            float size = s.HeightM;
            var up = new float3(0f, 1f, 0f);
            if (tech == Tech.Ancient)
            {
                Lathe(g[W], float3.zero, up, new[] { new float2(0f, -size * 0.6f), new float2(size * 0.22f, 0f), new float2(0f, size * 0.6f) }, 4, rng.NextFloat(), 0.7f, 1.1f);
                Torus(g[G], float3.zero, up, size * 0.5f, 0.3f, 32, 4, 1f);
                Torus(g[G], float3.zero, math.normalize(new float3(0.7f, 0.5f, 0.2f)), size * 0.66f, 0.22f, 32, 4, 1f);
                Torus(g[T], float3.zero, math.normalize(new float3(-0.3f, 0.6f, 0.7f)), size * 0.8f, 0.4f, 32, 4, 1.1f);
                return;
            }

            if (s.Variant == 0)
            {
                float ring = size * 0.85f;
                Torus(g[W], float3.zero, up, ring, size * 0.1f, 36, 8, 1f);
                Torus(g[G], float3.zero, up, ring + size * 0.1f, 0.25f, 36, 4, 1f);
                Lathe(g[W], float3.zero, up, new[]
                {
                    new float2(0.8f, -size * 0.35f), new float2(size * 0.16f, -size * 0.2f), new float2(size * 0.16f, size * 0.2f), new float2(0.8f, size * 0.35f),
                    new float2(0f, size * 0.4f),
                }, 12, 0f, 0.85f, 1.1f);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * math.PI * 0.5f + 0.4f;
                    Beam(g[T], Dir(a) * size * 0.15f, Dir(a) * (ring - size * 0.08f), 0.9f, 0.9f, 1.05f);
                }

                Box(g[R], new float3(0f, size * 0.42f, 0f), size * 0.5f, size * 0.12f, 0.15f, 0.9f, 0.9f, 1.1f);
                Box(g[R], new float3(0f, -size * 0.5f, 0f), size * 0.5f, size * 0.12f, 0.15f, 2.4f, 0.9f, 1.1f);
            }
            else
            {
                Lathe(g[W], float3.zero, up, new[]
                {
                    new float2(0f, -size * 0.2f), new float2(size * 0.1f, -size * 0.12f), new float2(size * 0.1f, size * 0.7f), new float2(size * 0.14f, size * 0.74f),
                    new float2(size * 0.14f, size * 0.9f), new float2(0f, size * 0.98f),
                }, 12, 0f, 0.85f, 1.12f);
                Torus(g[T], new float3(0f, size * 0.25f, 0f), up, size * 0.3f, size * 0.04f, 24, 6, 1.05f);
                Torus(g[G], new float3(0f, size * 0.82f, 0f), up, size * 0.145f, 0.2f, 16, 4, 1f);
                for (int k = 0; k < 2; k++)
                {
                    float a = k * math.PI + 0.6f;
                    Beam(g[T], new float3(0f, size * 0.5f, 0f), Dir(a) * size * 1.1f + new float3(0f, size * 0.5f, 0f), 0.4f, 0.9f, 1f);
                    for (int i = 0; i < 3; i++)
                    {
                        Box(g[R], Dir(a) * size * (0.4f + 0.3f * i) + new float3(0f, size * 0.5f - size * 0.22f, 0f), size * 0.13f, 0.08f, size * 0.44f, a + math.PI * 0.5f, 0.9f, 1.1f);
                    }
                }
            }

            Lathe(g[G], new float3(0f, -size * 0.36f, 0f), up, Ball(0.5f, 4), 6, 0f, 1f, 1f);
        }

        // ---- labyrinth, mine, dungeon gate -----------------------------------------------------------------------

        // Variant: 0 round rings, 1 square courts. Three concentric walls with staggered gaps around a sealed core.
        private static void BuildLabyrinth(Geo[] g, in LandmarkSpec s, Tech tech, ref Pcg32 rng)
        {
            float fr = s.FootprintRadiusM, h = s.HeightM;
            var up = new float3(0f, 1f, 0f);
            int segments = s.Variant == 0 ? 28 : 4;
            float turn = s.Variant == 0 ? 0f : math.PI * 0.25f;
            Geo line = tech == Tech.Future ? g[G] : g[T];
            Lathe(g[W], float3.zero, up, new[] { new float2(fr * 0.99f, -SinkM), new float2(fr * 0.99f, 0.25f), new float2(0f, 0.25f) }, segments, turn, 0.8f, 0.92f);
            for (int ring = 0; ring < 3; ring++)
            {
                float rr = fr * (0.95f - 0.27f * ring);
                float wallH = h * (0.75f + 0.18f * ring);
                // The outer wall opens on the entrance axis; each inner wall opens a third of a turn further round.
                float gap = math.PI * 0.5f + ring * 2.1f;
                int pieces = s.Variant == 0 ? 28 : 16;
                for (int k = 0; k < pieces; k++)
                {
                    float a0 = k * math.PI * 2f / pieces, a1 = (k + 1) * math.PI * 2f / pieces;
                    float mid = (a0 + a1) * 0.5f;
                    float off = math.abs(math.atan2(math.sin(mid - gap), math.cos(mid - gap)));
                    if (off < 2.2f / rr)
                    {
                        continue;
                    }

                    float worn = tech == Tech.Ancient ? 0.45f + 0.55f * rng.NextFloat() : 1f - 0.12f * s.Age * rng.NextFloat();
                    float3 p0 = RingPoint(a0, rr, s.Variant), p1 = RingPoint(a1, rr, s.Variant);
                    float3 n0 = math.normalize(p0) * 0.55f, n1 = math.normalize(p1) * 0.55f;
                    float hh = wallH * worn;
                    Hexa(g[W], new[]
                    {
                        p0 - n0, p1 - n1, p1 + n1, p0 + n0,
                        p0 - n0 * 0.8f + up * hh, p1 - n1 * 0.8f + up * hh, p1 + n1 * 0.8f + up * hh, p0 + n0 * 0.8f + up * hh,
                    }, 0.84f, 1.04f);
                    if ((k & 1) == 0 && worn > 0.9f)
                    {
                        Hexa(line, new[]
                        {
                            p0 - n0 * 0.9f + up * hh, p1 - n1 * 0.9f + up * hh, p1 + n1 * 0.9f + up * hh, p0 + n0 * 0.9f + up * hh,
                            p0 - n0 * 0.9f + up * (hh + 0.25f), p1 - n1 * 0.9f + up * (hh + 0.25f), p1 + n1 * 0.9f + up * (hh + 0.25f), p0 + n0 * 0.9f + up * (hh + 0.25f),
                        }, 1f, 1.05f);
                    }
                }
            }

            // Gate pylons on the entrance axis and the thing the walls were built around.
            float gateZ = s.Variant == 0 ? fr * 0.9f : fr * 0.66f;
            for (int side = -1; side <= 1; side += 2)
            {
                Box(g[W], new float3(side * 2.6f, 0f, gateZ), 0.9f, 0.9f, h * 1.25f, math.PI * 0.5f, 0.9f, 1.08f, 0.75f);
                Lathe(g[G], new float3(side * 2.6f, h * 1.25f + 0.3f, gateZ), up, Ball(0.4f, 4), 6, 0f, 1f, 1f);
            }

            float core = fr * 0.2f;
            Lathe(g[W], float3.zero, up, new[]
            {
                new float2(core, 0.25f), new float2(core * 0.8f, 1.2f), new float2(core * 0.45f, 1.2f), new float2(core * 0.3f, h * 1.6f), new float2(0f, h * 1.6f + 1.2f),
            }, 4, math.PI * 0.25f, 0.88f, 1.1f);
            Torus(g[G], new float3(0f, h * 1.1f, 0f), up, core * 0.75f, 0.1f, 16, 4, 1f);
            Lathe(g[G], new float3(0f, h * 1.6f + 1.9f, 0f), up, new[] { new float2(0f, 0f), new float2(0.5f, 0.8f), new float2(0f, 1.8f) }, 4, 0f, 1f, 1f);
        }

        private static float3 RingPoint(float a, float r, int variant)
        {
            if (variant == 0)
            {
                return Dir(a) * r;
            }

            // Square court: push the circle out to the square that shares its apothem × 0.8 (corners stay in the footprint).
            float3 d = Dir(a);
            float m = math.max(math.abs(d.x), math.abs(d.z));
            return d * (r * 0.72f / m);
        }

        // Variant: 0 headframe over a vertical shaft, 1 adit into a mound.
        private static void BuildMine(Geo[] g, in LandmarkSpec s, Tech tech, ref Pcg32 rng)
        {
            float fr = s.FootprintRadiusM, h = s.HeightM;
            var up = new float3(0f, 1f, 0f);
            float3 seed = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 29f;
            bool iron = tech == Tech.Machine || tech == Tech.Future || tech == Tech.Masonry;
            float leg = iron ? 0.3f : 0.42f;
            if (s.Variant == 0)
            {
                Lathe(g[W], float3.zero, up, new[]
                {
                    new float2(2.6f, -SinkM), new float2(2.6f, 0.7f), new float2(2f, 0.7f), new float2(2f, -1.2f), new float2(0f, -1.2f),
                }, 8, math.PI / 8f, 0.9f, 0.45f);
                Terrace(g[W], fr, 0.2f);
                float spread = fr * 0.42f;
                float3 head = new float3(0f, h * 0.86f, 0f);
                for (int k = 0; k < 4; k++)
                {
                    float a = math.PI * 0.25f + k * math.PI * 0.5f;
                    float3 foot = Dir(a) * spread * math.SQRT2 - up * 1f, tip = head + Dir(a) * 0.9f;
                    Beam(g[T], foot, tip, leg, 0.75f, 1f);
                    float3 nextFoot = Dir(a + math.PI * 0.5f) * spread * math.SQRT2 - up * 1f, nextTip = head + Dir(a + math.PI * 0.5f) * 0.9f;
                    for (int i = 1; i <= 2; i++)
                    {
                        float t0 = i / 3f, t1 = (i - 0.6f) / 3f;
                        Beam(g[T], math.lerp(foot, tip, t0), math.lerp(nextFoot, nextTip, t0), leg * 0.6f, 0.85f, 0.85f);
                        Beam(g[T], math.lerp(foot, tip, t1), math.lerp(nextFoot, nextTip, t0), leg * 0.5f, 0.8f, 0.9f);
                    }
                }

                // Sheave wheel, its axle, and the rope down into the dark.
                Torus(g[T], head + up * 0.9f, new float3(1f, 0f, 0f), 1.25f, 0.13f, 16, 4, 1.15f);
                Beam(g[T], head + new float3(-1f, 0.9f, 0f), head + new float3(1f, 0.9f, 0f), 0.2f, 1f, 1f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * math.PI * 0.25f;
                    float3 spoke = new float3(0f, math.cos(a), math.sin(a)) * 1.2f;
                    Beam(g[T], head + up * 0.9f - spoke, head + up * 0.9f + spoke, 0.07f, 1.05f, 1.05f);
                }

                Beam(g[T], head + new float3(0f, 0.9f, 1.25f), new float3(0f, -1.2f, 0.4f), 0.06f, 0.6f, 0.4f);
                Box(g[G], head + new float3(0f, -0.9f, 0f), 0.18f, 0.18f, 0.4f, 0f, 1f, 1f);

                // Winding house behind the frame, and the spoil it has hauled up.
                float3 house = new float3(-fr * 0.48f, -1f, -fr * 0.42f);
                Box(g[W], house, fr * 0.26f, fr * 0.22f, 3.6f, math.PI * 0.5f, 0.85f, 1.02f);
                Gable(g[R], g[W], house + new float3(0f, 3.6f, 0f), fr * 0.26f + 0.4f, fr * 0.22f + 0.4f, 1.4f, 0f);
                Box(g[G], house + new float3(0f, 1.9f, fr * 0.22f), 0.35f, 0.1f, 0.8f, math.PI * 0.5f, 1f, 1f);
                Lathe(g[W], new float3(fr * 0.5f, -0.6f, -fr * 0.4f), up, new[]
                {
                    new float2(fr * 0.36f, 0f), new float2(fr * 0.22f, 1.6f), new float2(fr * 0.08f, 2.4f), new float2(0f, 2.5f),
                }, 9, 0f, 0.6f, 0.82f, (i, k, p) => p + new float3(0f, i > 0 ? 0.5f * noise.snoise(p * 0.5f + seed) : 0f, 0f));
            }
            else
            {
                // A mound with its face cut flat on the entrance side for the portal.
                float face = fr * 0.45f;
                Lathe(g[W], float3.zero, up, Arc(fr * 0.97f, -SinkM, h + SinkM, 6, 1.3f), 14, 0f, 0.7f, 1f, (i, k, p) =>
                {
                    float3 q = p + new float3(1f, 0f, 1f) * (0.8f * noise.snoise(p * 0.22f + seed)) * (i > 0 ? 1f : 0f);
                    q.z = math.min(q.z, face + q.y * -0.05f);
                    return q;
                });
                float pw = 1.7f, ph = 3.2f;
                Box(g[W], new float3(0f, -0.5f, face - 1.15f), pw, 1.2f, ph + 0.5f, math.PI * 0.5f, 0.12f, 0.2f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Beam(g[T], new float3(side * pw, -0.5f, face + 0.1f), new float3(side * pw * 0.94f, ph, face + 0.1f), 0.42f, 0.75f, 0.95f);
                    Box(g[G], new float3(side * (pw + 0.6f), 2.2f, face + 0.2f), 0.16f, 0.16f, 0.36f, 0f, 1f, 1f);
                }

                Beam(g[T], new float3(-pw - 0.5f, ph, face + 0.1f), new float3(pw + 0.5f, ph, face + 0.1f), 0.46f, 0.9f, 0.9f);
                for (int i = 0; i < 4; i++)
                {
                    float3 at = Dir(rng.NextFloat() * math.PI * 2f) * fr * (0.3f + 0.4f * rng.NextFloat());
                    at.y = h * 0.55f;
                    Lathe(g[G], at, math.normalize(new float3(at.x, fr, at.z)), new[]
                    {
                        new float2(0.22f, -1.5f), new float2(0.32f, 1.2f), new float2(0f, 2f),
                    }, 6, 0f, 1f, 1f);
                }
            }

            // Rails out of the workings and a tub left on them.
            float z0 = s.Variant == 0 ? 2.2f : fr * 0.45f - 1f, z1 = fr * 0.98f;
            for (int side = -1; side <= 1; side += 2)
            {
                Beam(g[T], new float3(side * 0.5f, 0.12f, z0), new float3(side * 0.5f, 0.12f, z1), 0.1f, 1.1f, 1.1f);
            }

            for (float z = z0 + 0.3f; z < z1; z += 0.8f)
            {
                Beam(g[T], new float3(-0.8f, 0.05f, z), new float3(0.8f, 0.05f, z), 0.16f, 0.6f, 0.6f);
            }

            float zc = math.lerp(z0, z1, 0.6f);
            Box(g[T], new float3(0f, 0.35f, zc), 0.6f, 0.85f, 0.8f, math.PI * 0.5f, 0.8f, 1f, 1.2f);
            Lathe(g[W], new float3(0f, 1.1f, zc), up, new[] { new float2(0.65f, 0f), new float2(0.3f, 0.4f), new float2(0f, 0.45f) }, 6, 0f, 0.6f, 0.8f);
        }

        // Variant: 0 a great arch over a sealed portal, 1 a carved beast's maw.
        private static void BuildGate(Geo[] g, in LandmarkSpec s, Tech tech, ref Pcg32 rng)
        {
            float fr = s.FootprintRadiusM, h = s.HeightM;
            var up = new float3(0f, 1f, 0f);
            Geo line = tech == Tech.Future ? g[G] : g[T];
            Lathe(g[W], float3.zero, up, new[]
            {
                new float2(fr * 0.98f, -SinkM), new float2(fr * 0.98f, 0.35f), new float2(fr * 0.86f, 0.35f), new float2(fr * 0.86f, 0.7f), new float2(0f, 0.7f),
            }, 10, math.PI / 10f, 0.8f, 0.95f);
            if (s.Variant == 0)
            {
                float span = fr * 0.36f, spring = h * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Box(g[W], new float3(side * (span + 1f), 0.7f, 0f), 1f, 1.3f, spring, math.PI * 0.5f, 0.85f, 1.04f, 0.9f);
                    Box(line, new float3(side * (span + 1f), 0.7f + spring * 0.3f, 1.28f), 0.14f, 0.06f, spring * 0.45f, math.PI * 0.5f, 1f, 1f);
                }

                // Voussoirs: the arch is stones, and old arches lose some.
                const int Stones = 9;
                float rise = h - 0.7f - spring - 1f;
                for (int i = 0; i < Stones; i++)
                {
                    if (tech == Tech.Ancient && i != 0 && i != Stones - 1 && rng.NextUInt(100) < 22)
                    {
                        continue;
                    }

                    float a0 = math.PI * i / Stones, a1 = math.PI * (i + 1) / Stones;
                    float3 c = new float3(0f, 0.7f + spring, 0f);
                    float3 i0 = c + new float3(math.cos(a0) * span, math.sin(a0) * rise, 0f), i1 = c + new float3(math.cos(a1) * span, math.sin(a1) * rise, 0f);
                    float3 o0 = c + new float3(math.cos(a0) * (span + 2f), math.sin(a0) * (rise + 1.8f), 0f), o1 = c + new float3(math.cos(a1) * (span + 2f), math.sin(a1) * (rise + 1.8f), 0f);
                    float3 d = new float3(0f, 0f, i == Stones / 2 ? 1.5f : 1.2f);
                    Hexa(g[W], new[] { i0 - d, i1 - d, i1 + d, i0 + d, o0 - d, o1 - d, o1 + d, o0 + d }, 0.9f, i == Stones / 2 ? 1.15f : 1.02f);
                }

                // The sealed way down: a sheet of light, and the first steps of the stair behind it.
                int p0 = g[G].Part.P.Count;
                const int Cols = 8;
                for (int i = 0; i <= Cols; i++)
                {
                    float a = math.PI * i / Cols;
                    g[G].V(new float3(math.cos(a) * span * 0.98f, 0.7f, 0f), 1f);
                    g[G].V(new float3(math.cos(a) * span * 0.98f, 0.7f + spring + math.sin(a) * rise * 0.98f, 0f), 1f);
                }

                for (int i = 0; i < Cols; i++)
                {
                    int a = p0 + i * 2;
                    g[G].Quad(a, a + 1, a + 3, a + 2, new float3(0f, 0f, 1f));
                    g[G].Quad(a, a + 1, a + 3, a + 2, new float3(0f, 0f, -1f));
                }

                for (int i = 0; i < 5; i++)
                {
                    Box(g[W], new float3(0f, 0.7f - 0.3f * (i + 1), 1.6f + i * 0.9f), span * 1.2f + i * 0.25f, 0.5f, 0.3f, math.PI * 0.5f, 0.8f, 0.92f);
                }

                for (int side = -1; side <= 1; side += 2)
                {
                    float3 at = new float3(side * fr * 0.62f, 0.7f, fr * 0.5f);
                    Lathe(g[W], at, up, new[] { new float2(0.6f, 0f), new float2(0.42f, 0.4f), new float2(0.24f, h * 0.55f), new float2(0f, h * 0.55f + 0.7f) }, 4, math.PI * 0.25f, 0.88f, 1.08f);
                    Lathe(g[G], at + new float3(0f, h * 0.55f + 1.1f, 0f), up, Ball(0.3f, 4), 6, 0f, 1f, 1f);
                }
            }
            else
            {
                float3 seed = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 31f;
                float rr = fr * 0.8f, mouth = fr * 0.3f;
                Lathe(g[W], new float3(0f, 0.7f, -fr * 0.12f), up, Arc(rr, 0f, h - 0.7f, 7, 1.25f), 14, 0f, 0.78f, 1.05f, (i, k, p) =>
                {
                    float3 q = p + math.normalize(new float3(p.x, 0.01f, p.z + fr * 0.12f)) * (0.5f * noise.snoise(p * 0.3f + seed));
                    // Flatten the muzzle so the mouth has a face to open in.
                    q.z = math.min(q.z, mouth + 0.6f - 0.12f * q.y);
                    return q;
                });
                float jaw = h * 0.42f, zf = mouth + 0.3f;
                Box(g[W], new float3(0f, 0.7f, zf - 0.95f), fr * 0.34f, 1.1f, jaw, math.PI * 0.5f, 0.1f, 0.16f);
                Box(g[G], new float3(0f, 0.7f, zf), fr * 0.18f, 0.2f, jaw * 0.6f, math.PI * 0.5f, 1f, 1f);
                for (int i = 0; i < 6; i++)
                {
                    float x = math.lerp(-fr * 0.3f, fr * 0.3f, (i + 0.5f) / 6f);
                    float len = jaw * (0.3f + 0.12f * math.abs(i - 2.5f));
                    Lathe(g[T], new float3(x, 0.7f + jaw, zf + 0.3f), new float3(0f, -1f, 0f), new[] { new float2(0.34f, 0f), new float2(0f, len) }, 5, 0f, 1.2f, 1.25f);
                    Lathe(g[T], new float3(x * 0.9f, 0.7f, zf + 0.3f), up, new[] { new float2(0.3f, 0f), new float2(0f, len * 0.7f) }, 5, 0f, 1.2f, 1.25f);
                }

                for (int side = -1; side <= 1; side += 2)
                {
                    float3 eye = new float3(side * fr * 0.3f, 0.7f + h * 0.62f, mouth + 0.55f - 0.12f * h * 0.62f);
                    Lathe(g[G], eye, math.normalize(new float3(side * 0.2f, 0.2f, 1f)), new[] { new float2(0.75f, 0f), new float2(0.5f, 0.3f), new float2(0f, 0.38f) }, 6, 0f, 1f, 1f);
                    // Horns in three straight pieces sweeping back and up.
                    float3 p = new float3(side * fr * 0.42f, 0.7f + h * 0.78f, -fr * 0.1f);
                    float3 step = new float3(side * 0.9f, 0.9f, -0.5f);
                    for (int j = 0; j < 3; j++)
                    {
                        float3 next = p + step * (1.5f - 0.2f * j);
                        Beam(g[T], p, next, 0.7f - 0.2f * j, 1.1f, 1.2f);
                        p = next;
                        step = math.normalize(step + new float3(-side * 0.5f, 0.5f, -0.2f)) * math.length(step);
                    }
                }
            }
        }

        // ---- primitives ------------------------------------------------------------------------------------------

        // A low round terrace out to the footprint's edge: the rule layer blocks the whole circle, so the ground it
        // blocks has to look built on.
        private static void Terrace(Geo geo, float footprintM, float height)
        {
            Lathe(geo, float3.zero, new float3(0f, 1f, 0f), new[]
            {
                new float2(footprintM * 0.98f, -SinkM), new float2(footprintM * 0.98f, height * 0.5f), new float2(footprintM * 0.9f, height * 0.5f),
                new float2(footprintM * 0.9f, height), new float2(0f, height),
            }, 20, 0f, 0.78f, 0.92f);
        }

        private static float3 Dir(float a) => new float3(math.cos(a), 0f, math.sin(a));

        // Quarter-circle dome from (r, y) to (0, y + rise); `bulge` > 1 swells the shoulder.
        private static float2[] Arc(float r, float y, float rise, int steps, float bulge = 1f)
        {
            var p = new float2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = math.PI * 0.5f * i / steps;
                p[i] = new float2(r * math.pow(math.cos(a), 1f / bulge), y + rise * math.sin(a));
            }

            p[steps].x = 0f;
            return p;
        }

        // Sphere profile about its own centre, pole to pole.
        private static float2[] Ball(float r, int steps)
        {
            var p = new float2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = -math.PI * 0.5f + math.PI * i / steps;
                p[i] = new float2(i == 0 || i == steps ? 0f : r * math.cos(a), r * math.sin(a));
            }

            return p;
        }

        // Surface of revolution about `axis` through `centre`. `profile` is (radius, height along the axis), listed so
        // the outside is on the right when walking it: up a wall, in over a roof, out along an underside.
        private static void Lathe(Geo geo, float3 centre, float3 axis, float2[] profile, int sides, float turn, float tone0, float tone1,
            Func<int, int, float3, float3> warp = null)
        {
            float3 u = math.abs(axis.y) > 0.99f ? new float3(1f, 0f, 0f) : math.normalize(math.cross(new float3(0f, 1f, 0f), axis));
            float3 v = math.cross(u, axis);
            int first = geo.Part.P.Count;
            for (int i = 0; i < profile.Length; i++)
            {
                float tone = math.lerp(tone0, tone1, profile.Length > 1 ? (float)i / (profile.Length - 1) : 0f);
                for (int k = 0; k < sides; k++)
                {
                    float a = turn + k * math.PI * 2f / sides;
                    float3 radial = u * math.cos(a) + v * math.sin(a);
                    // Warps are written for the upright case (x, z around +Y); tilted lathes do not use them.
                    float3 p = radial * profile[i].x + axis * profile[i].y;
                    geo.V(centre + (warp != null ? warp(i, k, p) : p), tone);
                }
            }

            for (int i = 0; i + 1 < profile.Length; i++)
            {
                float dr = profile[i + 1].x - profile[i].x, dy = profile[i + 1].y - profile[i].y;
                for (int k = 0; k < sides; k++)
                {
                    int next = (k + 1) % sides;
                    float a = turn + (k + 0.5f) * math.PI * 2f / sides;
                    float3 radial = u * math.cos(a) + v * math.sin(a);
                    geo.Quad(first + i * sides + k, first + i * sides + next, first + (i + 1) * sides + next, first + (i + 1) * sides + k,
                        radial * dy - axis * dr);
                }
            }
        }

        // A box on `baseCentre` whose depth axis points along Dir(a): halfW across it, halfD along it.
        private static void Box(Geo geo, float3 baseCentre, float halfW, float halfD, float height, float a, float tone0, float tone1,
            float topScale = 1f)
        {
            float3 n = Dir(a), t = new float3(-math.sin(a), 0f, math.cos(a));
            float3 top = new float3(0f, height, 0f);
            Hexa(geo, new[]
            {
                baseCentre - t * halfW - n * halfD, baseCentre + t * halfW - n * halfD, baseCentre + t * halfW + n * halfD, baseCentre - t * halfW + n * halfD,
                baseCentre + top + (-t * halfW - n * halfD) * topScale, baseCentre + top + (t * halfW - n * halfD) * topScale,
                baseCentre + top + (t * halfW + n * halfD) * topScale, baseCentre + top + (-t * halfW + n * halfD) * topScale,
            }, tone0, tone1);
        }

        private static void Beam(Geo geo, float3 from, float3 to, float width, float tone0, float tone1)
        {
            float3 axis = math.normalize(to - from);
            float3 s = math.normalize(math.cross(axis, math.abs(axis.y) > 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f))) * (width * 0.5f);
            float3 d = math.cross(axis, s);
            Hexa(geo, new[] { from - s - d, from + s - d, from + s + d, from - s + d, to - s - d, to + s - d, to + s + d, to - s + d }, tone0, tone1);
        }

        // Eight corners: a ring of four, then the ring above it in the same order; all six faces are closed.
        private static void Hexa(Geo geo, float3[] c, float tone0, float tone1)
        {
            float3 inside = float3.zero;
            foreach (float3 p in c)
            {
                inside += p;
            }

            inside /= c.Length;
            for (int k = 0; k < 4; k++)
            {
                int n = (k + 1) & 3;
                float3 hint = (c[k] + c[n] + c[4 + n] + c[4 + k]) * 0.25f - inside;
                // Opposite pairs differ slightly, so a box still reads as a solid with the sun straight overhead.
                geo.Quad(geo.V(c[k], tone0), geo.V(c[n], tone0), geo.V(c[4 + n], tone1), geo.V(c[4 + k], tone1), hint, (k & 1) == 0 ? 1f : 0.94f);
            }

            geo.Quad(geo.V(c[4], tone1), geo.V(c[5], tone1), geo.V(c[6], tone1), geo.V(c[7], tone1), (c[4] + c[5] + c[6] + c[7]) * 0.25f - inside, 1.04f);
            geo.Quad(geo.V(c[0], tone0), geo.V(c[1], tone0), geo.V(c[2], tone0), geo.V(c[3], tone0), (c[0] + c[1] + c[2] + c[3]) * 0.25f - inside, 0.8f);
        }

        private static void Torus(Geo geo, float3 centre, float3 axis, float ringR, float tubeR, int segments, int sides, float tone)
        {
            float3 u = math.abs(axis.y) > 0.99f ? new float3(1f, 0f, 0f) : math.normalize(math.cross(new float3(0f, 1f, 0f), axis));
            float3 v = math.cross(u, axis);
            int first = geo.Part.P.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = i * math.PI * 2f / segments;
                float3 radial = u * math.cos(a) + v * math.sin(a);
                for (int k = 0; k < sides; k++)
                {
                    float b = (k + 0.5f) * math.PI * 2f / sides;
                    geo.V(centre + radial * (ringR + tubeR * math.cos(b)) + axis * (tubeR * math.sin(b)), tone);
                }
            }

            for (int i = 0; i < segments; i++)
            {
                int ni = (i + 1) % segments;
                float a = (i + 0.5f) * math.PI * 2f / segments;
                float3 radial = u * math.cos(a) + v * math.sin(a);
                for (int k = 0; k < sides; k++)
                {
                    int nk = (k + 1) % sides;
                    float b = (k + 1f) * math.PI * 2f / sides;
                    geo.Quad(first + i * sides + k, first + ni * sides + k, first + ni * sides + nk, first + i * sides + nk,
                        radial * math.cos(b) + axis * math.sin(b));
                }
            }
        }

        // Rotates the vertices appended since `first` about `axis` through the origin, then moves them by `offset`.
        private static void Rotate(Geo geo, int first, float3 axis, float angle, float3 offset)
        {
            quaternion q = quaternion.AxisAngle(math.normalize(axis), angle);
            for (int i = first; i < geo.Part.P.Count; i++)
            {
                geo.Part.P[i] = math.mul(q, geo.Part.P[i]) + offset;
            }
        }
    }
}
