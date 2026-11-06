using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.Combat
{
    /// <summary>Geometric family a move's hit test reduces to.</summary>
    public enum HitVolumeKind
    {
        /// <summary>shape=self: applies to the caster, no geometric test.</summary>
        Caster = 0,
        /// <summary>shape=ally: one chosen teammate within <see cref="HitVolume.CastDistanceMm"/>.</summary>
        Ally = 1,
        /// <summary>front / line (incl. dash): rectangle ahead of the anchor.</summary>
        Rect = 2,
        /// <summary>sweep / cone: circular sector centered on forward.</summary>
        Sector = 3,
        /// <summary>burst / arc landing / field: disc around the anchor.</summary>
        Circle = 4,
    }

    /// <summary>Where a volume is placed.</summary>
    public enum HitVolumeAnchor
    {
        /// <summary>At the caster's position, facing the caster's forward.</summary>
        Caster = 0,
        /// <summary>At the aim point, clamped to <see cref="HitVolume.CastDistanceMm"/> (see <see cref="MoveGeometry.ResolveAnchor"/>).</summary>
        AimPoint = 1,
    }

    /// <summary>Global hit-volume dimensions from <c>Data/balance.json#hit_volume</c>, until moves override them.</summary>
    /// <remarks>
    /// REQ: docs/design/combat.md#判定體. Values are 2026-11-06 approved defaults; angles are full angles in degrees.
    /// </remarks>
    public sealed class HitVolumeDefaults
    {
        /// <summary>Builds a validated default set.</summary>
        /// <remarks>
        /// Pre: every length in 1..1,000,000 mm; sweepAngleDeg and coneAngleDeg in 1..360.
        /// Post: properties return the arguments. Side effects: allocates this instance.
        /// </remarks>
        public HitVolumeDefaults(int heightMm, int frontWidthMm, int sweepAngleDeg, int coneAngleDeg, int lineWidthMm,
            int arcLandingRadiusMm, int fieldRadiusMm, int targetCastMidMm, int targetCastLongMm)
        {
            RequireMm(heightMm, "height_mm");
            RequireMm(frontWidthMm, "front_width_mm");
            Contract.Requires(sweepAngleDeg >= 1 && sweepAngleDeg <= 360, "sweep_angle_deg must be 1..360");
            Contract.Requires(coneAngleDeg >= 1 && coneAngleDeg <= 360, "cone_angle_deg must be 1..360");
            RequireMm(lineWidthMm, "line_width_mm");
            RequireMm(arcLandingRadiusMm, "arc_landing_radius_mm");
            RequireMm(fieldRadiusMm, "field_radius_mm");
            RequireMm(targetCastMidMm, "target_cast_mid_mm");
            RequireMm(targetCastLongMm, "target_cast_long_mm");
            HeightMm = heightMm;
            FrontWidthMm = frontWidthMm;
            SweepAngleDeg = sweepAngleDeg;
            ConeAngleDeg = coneAngleDeg;
            LineWidthMm = lineWidthMm;
            ArcLandingRadiusMm = arcLandingRadiusMm;
            FieldRadiusMm = fieldRadiusMm;
            TargetCastMidMm = targetCastMidMm;
            TargetCastLongMm = targetCastLongMm;
        }

        /// <summary>Full vertical extent of every volume before melee scaling.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int HeightMm { get; }

        /// <summary>Width of a front box before melee scaling.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int FrontWidthMm { get; }

        /// <summary>Full angle of a sweep sector.</summary>
        /// <remarks>Pre: none. Post: 1..360. Side effects: none.</remarks>
        public int SweepAngleDeg { get; }

        /// <summary>Full angle of a cone sector.</summary>
        /// <remarks>Pre: none. Post: 1..360. Side effects: none.</remarks>
        public int ConeAngleDeg { get; }

        /// <summary>Width of a line (including dash paths).</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int LineWidthMm { get; }

        /// <summary>Radius of the disc where an arc projectile lands.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int ArcLandingRadiusMm { get; }

        /// <summary>Radius of a deployed field.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int FieldRadiusMm { get; }

        /// <summary>Base cast distance of a mid target-point burst, before g(STR).</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int TargetCastMidMm { get; }

        /// <summary>Base cast distance of a long target-point burst, before g(STR).</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int TargetCastLongMm { get; }

        private static void RequireMm(int value, string name)
        {
            Contract.Requires(value >= 1 && value <= DerivedStatsCoefficients.MaxCoefficient, name + " must be 1..1,000,000 mm");
        }
    }

    /// <summary>A move's STR-scaled hit volume, in integer millimeters.</summary>
    /// <remarks>Pre: build through <see cref="MoveGeometry.Build"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct HitVolume
    {
        internal HitVolume(HitVolumeKind kind, HitVolumeAnchor anchor, int reachMm, int widthMm, int halfAngleDeg10,
            int halfHeightMm, int castDistanceMm, int dashDistanceMm)
        {
            Kind = kind;
            Anchor = anchor;
            ReachMm = reachMm;
            WidthMm = widthMm;
            HalfAngleDeciDeg = halfAngleDeg10;
            HalfHeightMm = halfHeightMm;
            CastDistanceMm = castDistanceMm;
            DashDistanceMm = dashDistanceMm;
        }

        /// <summary>Geometric family.</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public HitVolumeKind Kind { get; }

        /// <summary>Where the volume is placed.</summary>
        /// <remarks>Pre: none. Post: AimPoint iff Kind is Circle with a target / arc / field anchor. Side effects: none.</remarks>
        public HitVolumeAnchor Anchor { get; }

        /// <summary>Rect length, sector radius or circle radius.</summary>
        /// <remarks>Pre: none. Post: >= 0; 0 for Caster / Ally. Side effects: none.</remarks>
        public int ReachMm { get; }

        /// <summary>Full rect width.</summary>
        /// <remarks>Pre: none. Post: > 0 iff Kind is Rect. Side effects: none.</remarks>
        public int WidthMm { get; }

        /// <summary>Sector half angle in tenths of a degree (sweep 180° → 900).</summary>
        /// <remarks>Pre: none. Post: 5..1800 iff Kind is Sector, else 0. Side effects: none.</remarks>
        public int HalfAngleDeciDeg { get; }

        /// <summary>Allowed radial height difference between anchor and target surface, before target radius.</summary>
        /// <remarks>Pre: none. Post: > 0 for Rect / Sector / Circle. Side effects: none.</remarks>
        public int HalfHeightMm { get; }

        /// <summary>Max geodesic distance from the caster to the aim point (AimPoint anchor) or to the ally (Ally).</summary>
        /// <remarks>Pre: none. Post: >= 0; 0 for Caster-anchored geometric volumes. Side effects: none.</remarks>
        public int CastDistanceMm { get; }

        /// <summary>Straight surface displacement of a dash; 0 when the move does not move the caster.</summary>
        /// <remarks>
        /// Pre: none. Post: > 0 iff the move is a dash; equals base_range_mm, never STR-scaled
        /// (derived-stats.md: displacement is separate from the hit volume). Side effects: none.
        /// </remarks>
        public int DashDistanceMm { get; }
    }

    /// <summary>
    /// Builds hit volumes from move data + STR and tests them against target spheres on the planet surface.
    /// REQ: docs/design/combat.md#判定體, docs/design/derived-stats.md (STR scaling).
    /// </summary>
    /// <remarks>
    /// Distances are geodesic along the caster's radius and heights are radial (|p − center| differences), because at
    /// R = 640 m a point 36 m away sits ~1 m below the caster's tangent plane: a flat test would miss level targets.
    /// Scaling rule: melee multiplies every dimension by L(STR) = MeleeLinearPermille; mid/long only extend reach /
    /// cast distance by g(STR) (capped); AoE radii, widths and angles keep their data values; dashes never scale.
    /// </remarks>
    public static class MoveGeometry
    {
        private const float MmToM = 0.001f;

        // Tolerance for caller-supplied unit vectors, matching PlanetFrame.FromUp.
        private const float UnitTolerance = 1e-3f;

        /// <summary>Hit volume of a move cast with attacker strength <paramref name="str"/>.</summary>
        /// <remarks>
        /// Pre: stats != null; defaults != null; baseRangeMm >= 0; str >= 0; movement == Dash implies shape == Line and
        ///      baseRangeMm > 0; shape == Burst with origin == Target implies rangeClass != Melee (combat.md 目標點爆發).
        /// Post: see <see cref="HitVolume"/> property contracts; non-decreasing in str for every dimension.
        /// Side effects: none.
        /// </remarks>
        public static HitVolume Build(MoveShape shape, MoveMovement movement, RangeClass rangeClass, BurstOrigin origin,
            int baseRangeMm, int str, DerivedStats stats, HitVolumeDefaults defaults)
        {
            Contract.Requires(stats != null && defaults != null, "stats and defaults must not be null");
            Contract.Requires(baseRangeMm >= 0, "baseRangeMm must be nonnegative");
            Contract.Requires(str >= 0, "str must be nonnegative");
            Contract.Requires(movement != MoveMovement.Dash || (shape == MoveShape.Line && baseRangeMm > 0),
                "dash requires line shape and positive range");

            bool melee = rangeClass == RangeClass.Melee;
            int linear = stats.MeleeLinearPermille(str);
            int halfHeight = Lateral(defaults.HeightMm, melee, linear) / 2;

            if (movement == MoveMovement.Dash)
            {
                return new HitVolume(HitVolumeKind.Rect, HitVolumeAnchor.Caster, baseRangeMm, defaults.LineWidthMm, 0,
                    defaults.HeightMm / 2, 0, baseRangeMm);
            }

            int reach = melee ? ScaleMm(baseRangeMm, linear) : stats.RangedDistanceMm(baseRangeMm, str);
            switch (shape)
            {
                case MoveShape.Self:
                    return new HitVolume(HitVolumeKind.Caster, HitVolumeAnchor.Caster, 0, 0, 0, 0, 0, 0);
                case MoveShape.Ally:
                    return new HitVolume(HitVolumeKind.Ally, HitVolumeAnchor.Caster, 0, 0, 0, 0, reach, 0);
                case MoveShape.Front:
                    return new HitVolume(HitVolumeKind.Rect, HitVolumeAnchor.Caster, reach,
                        Lateral(defaults.FrontWidthMm, melee, linear), 0, halfHeight, 0, 0);
                case MoveShape.Line:
                    return new HitVolume(HitVolumeKind.Rect, HitVolumeAnchor.Caster, reach,
                        Lateral(defaults.LineWidthMm, melee, linear), 0, halfHeight, 0, 0);
                case MoveShape.Sweep:
                    return new HitVolume(HitVolumeKind.Sector, HitVolumeAnchor.Caster, reach, 0,
                        defaults.SweepAngleDeg * 5, halfHeight, 0, 0);
                case MoveShape.Cone:
                    return new HitVolume(HitVolumeKind.Sector, HitVolumeAnchor.Caster, reach, 0,
                        defaults.ConeAngleDeg * 5, halfHeight, 0, 0);
                case MoveShape.Burst:
                    if (origin == BurstOrigin.Caster)
                    {
                        return new HitVolume(HitVolumeKind.Circle, HitVolumeAnchor.Caster,
                            Lateral(baseRangeMm, melee, linear), 0, 0, halfHeight, 0, 0);
                    }

                    Contract.Requires(!melee, "a target-point burst must be mid or long range");
                    int castBase = rangeClass == RangeClass.Mid ? defaults.TargetCastMidMm : defaults.TargetCastLongMm;
                    return new HitVolume(HitVolumeKind.Circle, HitVolumeAnchor.AimPoint, baseRangeMm, 0, 0, halfHeight,
                        stats.RangedDistanceMm(castBase, str), 0);
                case MoveShape.Arc:
                    return new HitVolume(HitVolumeKind.Circle, HitVolumeAnchor.AimPoint,
                        Lateral(defaults.ArcLandingRadiusMm, melee, linear), 0, 0, halfHeight, reach, 0);
                case MoveShape.Field:
                    return new HitVolume(HitVolumeKind.Circle, HitVolumeAnchor.AimPoint,
                        Lateral(defaults.FieldRadiusMm, melee, linear), 0, 0, halfHeight, reach, 0);
                default:
                    Contract.Requires(false, "undefined MoveShape");
                    return default;
            }
        }

        /// <summary>Where an AimPoint-anchored volume lands for a caster aiming at <paramref name="aimPoint"/>.</summary>
        /// <remarks>
        /// Pre: planet.IsValid; volume.Anchor == AimPoint; caster and aimPoint finite and not at the planet center.
        /// Post: geodesic distance (at the caster's radius) from caster to result &lt;= CastDistanceMm (+1 mm float slack);
        ///       result keeps the aim point's radius; result == aimPoint when already within range.
        /// Side effects: none.
        /// </remarks>
        public static float3 ResolveAnchor(in HitVolume volume, in PlanetConfig planet, float3 caster, float3 aimPoint)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(volume.Anchor == HitVolumeAnchor.AimPoint, "only aim-point volumes have a resolvable anchor");
            float3 dc = caster - planet.Center;
            float3 da = aimPoint - planet.Center;
            float rc = math.length(dc);
            float ra = math.length(da);
            Contract.Requires(rc > 0f && ra > 0f && math.isfinite(rc) && math.isfinite(ra),
                "caster and aimPoint must be finite and off the planet center");

            float3 uc = dc / rc;
            float3 ua = da / ra;
            float angle = math.atan2(math.length(math.cross(uc, ua)), math.dot(uc, ua));
            float maxAngle = volume.CastDistanceMm * MmToM / rc;
            if (angle <= maxAngle)
            {
                return aimPoint;
            }

            // Slerp from the caster's direction towards the aim direction, stopping at the cast distance.
            float3 tangent = math.normalize(ua - math.dot(ua, uc) * uc);
            float3 dir = math.cos(maxAngle) * uc + math.sin(maxAngle) * tangent;
            return planet.Center + dir * ra;
        }

        /// <summary>Whether a target sphere overlaps the volume placed at <paramref name="anchor"/> facing <paramref name="forward"/>.</summary>
        /// <remarks>
        /// Pre: planet.IsValid; volume.Kind != Caster; anchor and targetCenter finite and off the planet center;
        ///      targetRadiusM >= 0 and finite; for Rect / Sector, forward is unit (±1e-3) and tangent at anchor
        ///      (|dot(forward, up)| &lt;= 1e-3). For Ally, anchor is the caster and the test is "ally within cast distance".
        /// Post: true iff the target sphere's geodesic footprint touches the volume's footprint and the radial height
        ///       difference is within HalfHeightMm + targetRadius (Ally ignores height). Monotone: a larger volume (any
        ///       dimension) never turns true into false.
        /// Side effects: none.
        /// </remarks>
        public static bool Overlaps(in HitVolume volume, in PlanetConfig planet, float3 anchor, float3 forward,
            float3 targetCenter, float targetRadiusM)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(volume.Kind != HitVolumeKind.Caster, "self moves have no geometric test");
            Contract.Requires(targetRadiusM >= 0f && math.isfinite(targetRadiusM), "targetRadiusM must be finite and >= 0");

            Local(planet, anchor, forward, targetCenter, volume.Kind == HitVolumeKind.Rect || volume.Kind == HitVolumeKind.Sector,
                out float x, out float z, out float height);
            float r = targetRadiusM;
            if (volume.Kind == HitVolumeKind.Ally)
            {
                return x * x + z * z <= Sq(volume.CastDistanceMm * MmToM + r);
            }

            if (math.abs(height) > volume.HalfHeightMm * MmToM + r)
            {
                return false;
            }

            float reach = volume.ReachMm * MmToM;
            switch (volume.Kind)
            {
                case HitVolumeKind.Rect:
                {
                    float halfWidth = volume.WidthMm * MmToM * 0.5f;
                    float dx = math.max(math.abs(x) - halfWidth, 0f);
                    float dz = z < 0f ? -z : math.max(z - reach, 0f);
                    return dx * dx + dz * dz <= r * r;
                }
                case HitVolumeKind.Sector:
                    return SectorOverlaps(x, z, reach, math.radians(volume.HalfAngleDeciDeg * 0.1f), r);
                default:
                    return x * x + z * z <= Sq(reach + r);
            }
        }

        private static bool SectorOverlaps(float x, float z, float radius, float halfAngle, float r)
        {
            float d = math.sqrt(x * x + z * z);
            if (d <= r)
            {
                return true;
            }

            if (d > radius + r)
            {
                return false;
            }

            // Inside the wedge the nearest sector point is radial, so d <= radius + r is exact.
            if (math.abs(math.atan2(x, z)) <= halfAngle)
            {
                return true;
            }

            // Otherwise the nearest point lies on one of the two straight edges (segments from the apex).
            float s = math.sin(halfAngle);
            float c = math.cos(halfAngle);
            return SegmentDistanceSq(x, z, s, c, radius) <= r * r || SegmentDistanceSq(x, z, -s, c, radius) <= r * r;
        }

        private static float SegmentDistanceSq(float x, float z, float ex, float ez, float length)
        {
            float t = math.clamp(x * ex + z * ez, 0f, length);
            float px = x - t * ex;
            float pz = z - t * ez;
            return px * px + pz * pz;
        }

        // Azimuthal-equidistant coordinates of `point` around `anchor` (meters at the anchor's radius) plus the radial
        // height difference. atan2(|cross|, dot) instead of acos: acos loses ~all precision for the sub-degree angles
        // combat uses (1 mm at R = 640 m is 1.6e-6 rad).
        private static void Local(in PlanetConfig planet, float3 anchor, float3 forward, float3 point, bool needsForward,
            out float x, out float z, out float height)
        {
            float3 da = anchor - planet.Center;
            float3 dp = point - planet.Center;
            float ra = math.length(da);
            float rp = math.length(dp);
            Contract.Requires(ra > 0f && rp > 0f && math.isfinite(ra) && math.isfinite(rp),
                "anchor and target must be finite and off the planet center");
            float3 up = da / ra;
            if (needsForward)
            {
                Contract.Requires(math.abs(math.length(forward) - 1f) <= UnitTolerance
                    && math.abs(math.dot(forward, up)) <= UnitTolerance, "forward must be a unit tangent at the anchor");
            }

            float3 up2 = dp / rp;
            float3 cross = math.cross(up, up2);
            float angle = math.atan2(math.length(cross), math.dot(up, up2));
            height = rp - ra;
            float3 planar = up2 - math.dot(up2, up) * up;
            float planarLength = math.length(planar);
            if (planarLength < 1e-12f)
            {
                x = 0f;
                z = 0f;
                return;
            }

            float distance = angle * ra;
            float3 dir = planar / planarLength;
            if (!needsForward)
            {
                x = distance;
                z = 0f;
                return;
            }

            float3 right = math.cross(up, forward);
            x = math.dot(dir, right) * distance;
            z = math.dot(dir, forward) * distance;
        }

        private static int Lateral(int mm, bool melee, int linearPermille)
        {
            return melee ? ScaleMm(mm, linearPermille) : mm;
        }

        // Floor, per derived-stats.md: collision sizes converted to integer mm round down.
        private static int ScaleMm(int mm, int permille)
        {
            long scaled = (long)mm * permille / DerivedStats.Permille;
            return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }

        private static float Sq(float v) => v * v;
    }
}
