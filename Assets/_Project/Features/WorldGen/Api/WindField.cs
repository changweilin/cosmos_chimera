using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>Scale family of a wind vortex.</summary>
    /// <remarks>Pre: none. Post: stable values (they salt the spawn streams). Side effects: none.</remarks>
    public enum VortexKind
    {
        /// <summary>塵捲風: a few metres wide, short-lived; dry open ground only (WeatherService filters).</summary>
        DustDevil = 0,

        /// <summary>龍捲風: tens of metres, violent; only where the tornado exotic is active (WeatherService filters).</summary>
        Tornado = 1,

        /// <summary>渦流: a mesoscale eddy tens of metres across that turns the local wind without much force.</summary>
        Eddy = 2,

        /// <summary>颱風／颶風: a fraction of the planet across with a calm eye; spins with the hemisphere.</summary>
        Cyclone = 3,
    }

    /// <summary>One vortex at one instant.</summary>
    /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
    public readonly struct WindVortex
    {
        /// <summary>Scale family.</summary>
        public readonly VortexKind Kind;

        /// <summary>Unit direction of the eye.</summary>
        public readonly float3 Center;

        /// <summary>Radius of strongest wind (m); inside it the wind falls linearly to calm at the eye.</summary>
        public readonly float CoreM;

        /// <summary>Radius past which the vortex adds nothing (m).</summary>
        public readonly float OuterM;

        /// <summary>+1 turns from east toward north (counter-clockwise seen from above), −1 the other way.</summary>
        public readonly int Spin;

        /// <summary>Peak swirl speed now, 0..1000 (already scaled by the birth/decay envelope).</summary>
        public readonly int PeakPermille;

        /// <summary>Angle the flow spirals inward, tenths of a degree.</summary>
        public readonly int InflowTenths;

        /// <summary>Creates a vortex snapshot.</summary>
        /// <remarks>Pre: center unit; 0 &lt; coreM &lt; outerM; spin ±1; peak in [0, 1000]. Post: fields set. Side effects: none.</remarks>
        public WindVortex(VortexKind kind, float3 center, float coreM, float outerM, int spin, int peakPermille, int inflowTenths)
        {
            Kind = kind;
            Center = center;
            CoreM = coreM;
            OuterM = outerM;
            Spin = spin;
            PeakPermille = peakPermille;
            InflowTenths = inflowTenths;
        }
    }

    /// <summary>The wind at one surface point.</summary>
    /// <remarks>Pre: none. Post: plain data. Side effects: none.</remarks>
    public readonly struct WindSample
    {
        /// <summary>Heading, tenths of a degree from local east toward north, in [0, 3600).</summary>
        public readonly int HeadingTenths;

        /// <summary>Extra wind the vortices add here, 0..1000 (0 away from every vortex).</summary>
        public readonly int VortexPermille;

        /// <summary>Creates a sample.</summary>
        /// <remarks>Pre: heading in [0, 3600); vortex in [0, 1000]. Post: fields set. Side effects: none.</remarks>
        public WindSample(int headingTenths, int vortexPermille)
        {
            HeadingTenths = headingTenths;
            VortexPermille = vortexPermille;
        }
    }

    /// <summary>
    /// Deterministic wind over the whole planet surface: a prevailing flow by latitude band (trade winds, westerlies,
    /// polar easterlies) that meanders in space and time, plus drifting vortices of four scales (dust devils,
    /// tornadoes, eddies, cyclones with an eye). Gusts stay per region.
    /// REQ: docs/design/weather.md#風向.
    /// </summary>
    /// <remarks>
    /// Pre: none. Post: pure; the same arguments give bit-identical results on the same backend (ADR-0007 §1). Headings
    /// are evaluated in float from unit directions, like every other direction-keyed generator; across backends a
    /// heading may differ by a tenth of a degree at rounding boundaries, which is geometry, not content (ADR-0007 §2).
    /// Vortex births, sizes and paths come from integer Pcg32 streams only.
    /// Side effects: none.
    /// </remarks>
    public static class WindField
    {
        /// <summary>Version of the wind generator; bump whenever the same seed would give different wind.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 2;

        /// <summary>Most vortices <see cref="ActiveVortices"/> can add for one query.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxVortices = 48;

        // Attribute slot past WeatherField.AttrCount, so the shared lattice never hands wind the same stream as weather.
        private const int GustAttr = 41;
        private const int GustSlowPeriodMs = 7_000;
        private const int GustFastPeriodMs = 2_300;

        // REQ: weather.md#風向 (數值暫定) — prevailing meander: ±70° over ~180 m and ~5 minutes.
        private const float MeanderScaleM = 180f;
        private const float MeanderPeriodMs = 300_000f;
        private const float MeanderRad = 1.22f;

        // Per kind (DustDevil, Tornado, Eddy): 3D spawn cell (m), time slot (ms), spawn chance per cell and slot (%),
        // lifetime (ms), outer and core radius (m), peak (‰), drift speed (m/s), inflow (tenths). drift·life + outer
        // stays below the cell, so the 27 cells around a point hold every vortex that can reach it.
        private static readonly float[] CellM = { 90f, 180f, 220f };
        private static readonly int[] SlotMs = { 20_000, 30_000, 60_000 };
        private static readonly int[] ChancePct = { 30, 55, 30 };
        private static readonly int[] LifeMinMs = { 25_000, 60_000, 120_000 };
        private static readonly int[] LifeMaxMs = { 45_000, 120_000, 200_000 };
        private static readonly float[] OuterMinM = { 5f, 14f, 25f };
        private static readonly float[] OuterMaxM = { 10f, 22f, 55f };
        private static readonly float[] CoreShare = { 0.25f, 0.28f, 0.22f };
        private static readonly int[] PeakPermilleOf = { 550, 1000, 420 };
        private static readonly float[] DriftMaxMps = { 1.5f, 1.2f, 0.8f };
        private static readonly int[] InflowOf = { 120, 200, 80 };

        // Cyclones: global, born in the tropics (8–25° latitude), steered west then poleward.
        private const int CycloneSlotMs = 300_000;
        private const int CycloneLifeMinMs = 600_000;
        private const int CycloneLifeMaxMs = 900_000;
        private const float CycloneDriftMps = 0.5f;

        /// <summary>Wind heading at a surface point, ignoring vortices.</summary>
        /// <remarks>
        /// Pre: up unit length; radiusM &gt; 0; timeMs &gt;= 0.
        /// Post: result in [0, 3600); continuous in space and time (a few degrees per metre or second at most).
        /// Side effects: none.
        /// </remarks>
        public static int PrevailingHeadingTenths(ulong seed, float radiusM, float3 up, long timeMs)
        {
            Contract.Requires(radiusM > 0f, "radiusM must be > 0");
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            float2 flow = Prevailing(seed, radiusM, up, timeMs);
            return ToTenths(flow);
        }

        /// <summary>Collects the vortices that can reach <paramref name="near"/> at <paramref name="timeMs"/>.</summary>
        /// <remarks>
        /// Pre: near unit length; radiusM &gt; 0; timeMs &gt;= 0; into non-null; allow may be null (= every kind everywhere).
        /// Post: appends every live vortex whose outer radius can cover a point within <paramref name="reachM"/> of near
        ///       (cyclones: all live cyclones), at most <see cref="MaxVortices"/>, ordered by kind then birth; peak
        ///       already eased in and out. Same arguments, same list.
        /// Side effects: appends to into; calls allow.
        /// </remarks>
        public static void ActiveVortices(ulong seed, float radiusM, float3 near, long timeMs, float reachM,
            Func<VortexKind, float3, bool> allow, List<WindVortex> into)
        {
            Contract.Requires(radiusM > 0f, "radiusM must be > 0");
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            Contract.Requires(reachM >= 0f, "reachM must be >= 0");
            Contract.Requires(into != null, "into required");
            int start = into.Count;
            float3 p = near * radiusM;
            for (int kind = 0; kind < 3; kind++)
            {
                float cell = CellM[kind];
                int3 home = (int3)math.floor(p / cell);
                int ring = 1 + (int)math.ceil(reachM / cell);
                int slot = SlotMs[kind];
                long lastSlot = timeMs / slot;
                long firstSlot = math.max(0, (timeMs - LifeMaxMs[kind]) / slot);
                for (long s = firstSlot; s <= lastSlot; s++)
                {
                    for (int dz = -ring; dz <= ring; dz++)
                    for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (into.Count - start >= MaxVortices) return;
                        int3 c = home + new int3(dx, dy, dz);
                        if (!TrySpawnLocal(seed, radiusM, (VortexKind)kind, c, s, timeMs, out WindVortex v)) continue;
                        if (math.distance(v.Center, near) * radiusM > v.OuterM + reachM) continue;
                        if (allow != null && !allow(v.Kind, v.Center)) continue;
                        into.Add(v);
                    }
                }
            }

            long lastCyclone = timeMs / CycloneSlotMs;
            long firstCyclone = math.max(0, (timeMs - CycloneLifeMaxMs) / CycloneSlotMs);
            for (long s = firstCyclone; s <= lastCyclone; s++)
            {
                int births = radiusM >= 960f ? 2 : 1;
                for (int b = 0; b < births; b++)
                {
                    if (into.Count - start >= MaxVortices) return;
                    if (!TrySpawnCyclone(seed, radiusM, s, b, timeMs, out WindVortex v)) continue;
                    if (allow != null && !allow(v.Kind, v.Center)) continue;
                    into.Add(v);
                }
            }
        }

        /// <summary>The wind at a surface point: prevailing flow turned and strengthened by nearby vortices.</summary>
        /// <remarks>
        /// Pre: up unit length; radiusM &gt; 0; timeMs &gt;= 0; vortices non-null (from <see cref="ActiveVortices"/>).
        /// Post: heading in [0, 3600); vortex permille in [0, 1000]; with no vortex in reach equals the prevailing heading
        ///       and 0; inside a core the heading runs around the eye (spin decides which way). Pure.
        /// Side effects: none.
        /// </remarks>
        public static WindSample Sample(ulong seed, float radiusM, float3 up, long timeMs, IReadOnlyList<WindVortex> vortices)
        {
            Contract.Requires(radiusM > 0f, "radiusM must be > 0");
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            Contract.Requires(vortices != null, "vortices required");
            PlanetFrame frame = PlanetFrame.FromUp(up);
            float2 flow = Prevailing(seed, radiusM, frame.Up, timeMs);
            float swirl = 0f;
            for (int i = 0; i < vortices.Count; i++)
            {
                float2 v = Swirl(frame, radiusM, vortices[i]);
                flow += v * 2.5f;
                swirl += math.length(v);
            }

            return new WindSample(ToTenths(flow), (int)math.round(math.saturate(swirl) * 1000f));
        }

        /// <summary>Gust strength 0..1000 under an effective wind of <paramref name="windPermille"/>.</summary>
        /// <remarks>
        /// Pre: region &gt;= 0; timeMs &gt;= 0; windPermille in [0, 1000].
        /// Post: result in [0, windPermille]; 0 when windPermille == 0 (gusts only matter in a real wind).
        /// Side effects: none.
        /// </remarks>
        public static int GustPermille(ulong seed, int region, long timeMs, int windPermille)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            Contract.Requires(windPermille >= 0 && windPermille <= 1000, "windPermille must be in [0, 1000]");
            ulong key = RegionKey(seed, region);
            int raw = (WeatherField.ValueNoise(key, GustAttr, 0, timeMs, GustSlowPeriodMs) * 600
                       + WeatherField.ValueNoise(key, GustAttr, 1, timeMs, GustFastPeriodMs) * 400) / 1000;
            // Only the positive half: gusts are bursts above the steady wind, not lulls below it.
            return math.max(0, raw) * windPermille / 1000;
        }

        /// <summary>Unit tangent the wind blows along at <paramref name="up"/> for a heading.</summary>
        /// <remarks>
        /// Pre: up unit length (PlanetFrame.FromUp's Pre); headingTenths any integer (wrapped).
        /// Post: unit vector orthogonal to up; uses integer sine so equal inputs give equal results.
        /// Side effects: none.
        /// </remarks>
        public static float3 Tangent(float3 up, int headingTenths)
        {
            PlanetFrame frame = PlanetFrame.FromUp(up);
            float c = SolarClock.CosPermille(headingTenths) / 1000f;
            float s = SolarClock.SinPermille(headingTenths) / 1000f;
            return math.normalize(frame.East * c + frame.North * s);
        }

        // (east, north) components of a vortex's wind at the frame's point, in peak units (≤ 1).
        private static float2 Swirl(in PlanetFrame frame, float radiusM, in WindVortex v)
        {
            float3 offset = frame.Up - v.Center;
            float3 radial = offset - frame.Up * math.dot(offset, frame.Up);
            float rM = math.length(offset) * radiusM;
            if (rM >= v.OuterM || rM < 1e-3f) return float2.zero;
            float3 er = radial / math.max(math.length(radial), 1e-6f);
            // REQ: weather.md#風向 — Rankine profile: solid-body inside the core, ~1/r^0.55 outside, faded to 0 at the rim.
            float speed = rM < v.CoreM ? rM / v.CoreM : math.pow(v.CoreM / rM, 0.55f);
            speed *= 1f - math.smoothstep(0.6f * v.OuterM, v.OuterM, rM);
            speed *= v.PeakPermille / 1000f;
            // cross(v, up) turns a tangent a quarter from east toward north (cross(North, East) == Up).
            float3 et = math.cross(er, frame.Up) * v.Spin;
            float inflow = v.InflowTenths * (math.PI / 1800f);
            float3 w = (et * math.cos(inflow) - er * math.sin(inflow)) * speed;
            return new float2(math.dot(w, frame.East), math.dot(w, frame.North));
        }

        // REQ: weather.md#風向 — three cells per hemisphere. Headings (from east toward north): trades blow toward the
        // equator and west (N 210°, S 150°), westerlies toward the pole and east (N 30°, S 330°), polar easterlies
        // like the trades. Bands blend over ±6°, so the convergence belts are calm and turn smoothly.
        private static float2 Prevailing(ulong seed, float radiusM, float3 up, long timeMs)
        {
            float lat = math.degrees(math.asin(math.clamp(up.y, -1f, 1f)));
            float a = math.abs(lat);
            float north = lat >= 0f ? 1f : -1f;
            float2 trades = Dir(north > 0f ? 210f : 150f);
            float2 westerly = Dir(north > 0f ? 30f : 330f);
            float2 polar = trades;
            float toWest = math.smoothstep(24f, 36f, a);
            float toPolar = math.smoothstep(54f, 66f, a);
            float2 band = math.lerp(math.lerp(trades, westerly, toWest), polar, toPolar);
            // Near the equator the two hemispheres' trades meet head-on in the east-west sense: blend them to due west.
            float equator = 1f - math.smoothstep(0f, 6f, a);
            band = math.lerp(band, Dir(180f) * 0.85f, equator * 0.5f);

            // Per-seed spatial meander so neighbouring valleys do not all point the same way.
            float3 offset = new float3((seed & 0xFFFF) * 0.0137f, ((seed >> 16) & 0xFFFF) * 0.0113f, ((seed >> 32) & 0xFFFF) * 0.0091f);
            float3 p = up * (radiusM / MeanderScaleM) + offset;
            float turn = noise.snoise(new float4(p, timeMs / MeanderPeriodMs)) * MeanderRad
                         + noise.snoise(new float4(p * 2.3f + 17.1f, timeMs / (MeanderPeriodMs * 0.3f))) * MeanderRad * 0.3f;
            float c = math.cos(turn), s = math.sin(turn);
            float2 turned = new float2(band.x * c - band.y * s, band.x * s + band.y * c);
            // Never quite calm: a belt with a vanishing band still has a heading the meander decides.
            float floor = 0.35f;
            float len = math.length(turned);
            return len < floor ? (len > 1e-5f ? turned / len : Dir(math.degrees(turn))) * floor : turned;
        }

        private static float2 Dir(float degrees)
        {
            float r = math.radians(degrees);
            return new float2(math.cos(r), math.sin(r));
        }

        private static int ToTenths(float2 flow)
        {
            float deg = math.degrees(math.atan2(flow.y, flow.x));
            int tenths = (int)math.round(deg * 10f);
            return ((tenths % 3600) + 3600) % 3600;
        }

        private static bool TrySpawnLocal(ulong seed, float radiusM, VortexKind kind, int3 cell, long slot, long timeMs,
            out WindVortex vortex)
        {
            vortex = default;
            int k = (int)kind;
            float cellM = CellM[k];
            // Cells whose centre is far from the shell cannot hold a surface point; skip before drawing.
            float3 centre = ((float3)cell + 0.5f) * cellM;
            if (math.abs(math.length(centre) - radiusM) > cellM) return false;
            ulong key = seed ^ 0x564F5254UL ^ ((ulong)(uint)cell.x * 0x9E3779B97F4A7C15UL)
                        ^ ((ulong)(uint)cell.y * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)(uint)cell.z * 0x165667B19E3779F9UL);
            var rng = new Pcg32(key, ((ulong)slot << 2) | (uint)k);
            // Every draw first, so no decision shifts another.
            int chance = rng.NextInt(0, 100);
            float fx = rng.NextFloat(), fy = rng.NextFloat(), fz = rng.NextFloat();
            int bornOffset = rng.NextInt(0, SlotMs[k]);
            int life = rng.NextInt(LifeMinMs[k], LifeMaxMs[k] + 1);
            float outer = math.lerp(OuterMinM[k], OuterMaxM[k], rng.NextFloat());
            float driftDeg = rng.NextFloat() * 360f;
            float drift = DriftMaxMps[k] * (0.5f + 0.5f * rng.NextFloat());
            int spin = rng.NextInt(0, 2) == 0 ? 1 : -1;
            if (chance >= ChancePct[k]) return false;
            long born = slot * SlotMs[k] + bornOffset;
            long age = timeMs - born;
            if (age < 0 || age >= life) return false;
            float3 birth = math.normalize((float3)cell * cellM + new float3(fx, fy, fz) * cellM);
            float3 center = Travel(birth, driftDeg, drift * age / 1000f / radiusM);
            int peak = (int)(PeakPermilleOf[k] * Envelope(age, life));
            vortex = new WindVortex(kind, center, outer * CoreShare[k], outer, spin, peak, InflowOf[k]);
            return true;
        }

        private static bool TrySpawnCyclone(ulong seed, float radiusM, long slot, int birthIndex, long timeMs, out WindVortex vortex)
        {
            vortex = default;
            var rng = new Pcg32(seed ^ 0x43594300UL, ((ulong)slot << 1) | (uint)birthIndex);
            int chance = rng.NextInt(0, 100);
            int bornOffset = rng.NextInt(0, CycloneSlotMs);
            int life = rng.NextInt(CycloneLifeMinMs, CycloneLifeMaxMs + 1);
            float latDeg = 8f + rng.NextFloat() * 17f;
            bool north = rng.NextInt(0, 2) == 0;
            float lonDeg = rng.NextFloat() * 360f;
            float outerShare = 0.2f + rng.NextFloat() * 0.1f;
            if (chance >= 45) return false;
            long born = slot * CycloneSlotMs + bornOffset;
            long age = timeMs - born;
            if (age < 0 || age >= life) return false;
            // Steered west by the trades, recurving poleward as it ages (the classic parabolic track).
            float lonNow = math.radians(lonDeg) - CycloneDriftMps * age / 1000f / radiusM;
            float latNow = math.radians(latDeg) + 0.0035f * math.radians(1f) * (age / 1000f) * (age / (float)life);
            latNow *= north ? 1f : -1f;
            float3 center = new float3(math.cos(latNow) * math.cos(lonNow), math.sin(latNow), math.cos(latNow) * math.sin(lonNow));
            float outer = outerShare * radiusM;
            int peak = (int)(1000 * Envelope(age, life));
            vortex = new WindVortex(VortexKind.Cyclone, center, outer * 0.14f, outer, north ? 1 : -1, peak, 250);
            return true;
        }

        // Grows over the first 15 % of a life and dies over the last 20 %.
        private static float Envelope(long age, int life)
        {
            float t = age / (float)life;
            return math.smoothstep(0f, 0.15f, t) * (1f - math.smoothstep(0.8f, 1f, t));
        }

        // Moves `from` along the great circle leaving at `headingDeg` (from local east toward north) by `angle` radians.
        private static float3 Travel(float3 from, float headingDeg, float angle)
        {
            PlanetFrame frame = PlanetFrame.FromUp(from);
            float h = math.radians(headingDeg);
            float3 t = frame.East * math.cos(h) + frame.North * math.sin(h);
            return math.normalize(from * math.cos(angle) + t * math.sin(angle));
        }

        private static ulong RegionKey(ulong seed, int region) =>
            seed ^ ((ulong)(region + 1) * 0x9E3779B97F4A7C15UL) ^ 0x57494E44UL; // "WIND"
    }
}
