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
        /// Pre: every length in 1..1,000,000 mm, except heightMm in 2..1,000,000 (so HalfHeightMm >= 1 after halving);
        ///      sweepAngleDeg and coneAngleDeg in 1..360.
        /// Post: properties return the arguments. Side effects: allocates this instance.
        /// </remarks>
        public HitVolumeDefaults(int heightMm, int frontWidthMm, int sweepAngleDeg, int coneAngleDeg, int lineWidthMm,
            int arcLandingRadiusMm, int fieldRadiusMm, int targetCastMidMm, int targetCastLongMm)
        {
            RequireMm(heightMm, "height_mm");
            Contract.Requires(heightMm >= 2, "height_mm must be >= 2 so the half height stays positive");
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

        // ResolveAnchor runs in double: at or below this sin(angle) on the far side, the caster→aim great circle is not
        // determined even in double (cross of near-opposite unit vectors cancels to ~1e-16).
        private const double DegenerateSin = 1e-9;

        // Positions produced by ResolveAnchor are rounded to float world coordinates, an error that scales with the
        // world coordinate (center + 2R), not with the shell radius: at a 16 km center the 320 m inner edge moved
        // 0.07 mm, past a radius-relative slack (SOL r5 R1). Slack in meters = this × (max |center component| + 2R),
        // ~10× the worst float rounding (≈1.8e-7 × largest component), so ResolveAnchor → Overlaps always composes.
        private const double ShellSlack = 1e-6;

        /// <summary>Smallest planet radius the combat geometry supports.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: docs/design/derived-stats.md — ranged_distance.cap_mm is a fixed 36 m, approved for R >= 640 only
        /// (at R = 320 the 1.7 m eye-height horizon is ~33 m, so the cap would reach past it).
        /// </remarks>
        public const float MinSupportedRadiusM = 640f;

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
        /// Pre: planet.IsValid; planet.Radius >= MinSupportedRadiusM; volume.Anchor == AimPoint; caster and aimPoint
        ///      inside the combat shell (planet.Radius / 2 &lt;= |p − planet.Center| &lt;= 2 · planet.Radius, each bound
        ///      widened by 1e-6 · (max |planet.Center component| + 2 · planet.Radius) m for float rounding).
        /// Post: result is finite and inside the combat shell (same slack), so it can be passed to Overlaps as anchor;
        ///       geodesic distance (at the caster's radius) from caster to result &lt;= CastDistanceMm + 1 mm +
        ///       1e-6 · (largest |component| of result), the only error left being the float rounding of the returned
        ///       world position; result keeps the aim point's radius (clamped into the strict shell) within the same
        ///       slack and lies on the caster→aim great circle; result == aimPoint when already within range.
        ///       Degenerate aim: when the aim is on the far side with sin(angle(caster, aim)) &lt;= 1e-9 (within ~6 µm
        ///       of the caster's antipode at R = 640) the great circle is not determined, and the result heads towards
        ///       PlanetFrame.FromUp(caster up).North instead — deterministic on every peer. Every tangent field on a
        ///       sphere has a zero (hairy-ball theorem), so some jump at this threshold is unavoidable.
        /// Side effects: none.
        /// </remarks>
        public static float3 ResolveAnchor(in HitVolume volume, in PlanetConfig planet, float3 caster, float3 aimPoint)
        {
            RequirePlanet(planet);
            Contract.Requires(volume.Anchor == HitVolumeAnchor.AimPoint, "only aim-point volumes have a resolvable anchor");
            // Double throughout: in float, the cross of two near-opposite unit vectors cancels to noise and turned a
            // 12 m cast ~1 m off its great circle (SOL r4 Q4); subtracting a 16 km center in float also cost ~mm (Q1).
            double3 center = planet.Center;
            double3 dc = (double3)caster - center;
            double3 da = (double3)aimPoint - center;
            RequireInDomain(dc, planet, "caster");
            RequireInDomain(da, planet, "aimPoint");
            double rc = math.length(dc);
            double ra = math.length(da);

            double3 uc = dc / rc;
            double3 ua = da / ra;
            double3 axis = math.cross(uc, ua);
            double sinAngle = math.length(axis);
            double cosAngle = math.dot(uc, ua);
            double angle = math.atan2(sinAngle, cosAngle);
            double maxAngle = volume.CastDistanceMm * 0.001 / rc;
            if (angle <= maxAngle)
            {
                return aimPoint;
            }

            // Only the far side is degenerate: a same-side aim with tiny sin is barely farther than the cast distance,
            // so its cross direction is still the right one (SOL r4 Q2 sent a near-same-direction aim North).
            double3 tangent = cosAngle < 0.0 && sinAngle <= DegenerateSin
                ? (double3)PlanetFrame.FromUp((float3)uc).North
                : math.normalize(math.cross(axis / sinAngle, uc));
            double3 dir = math.normalize(math.cos(maxAngle) * uc + math.sin(maxAngle) * tangent);
            double radius = math.clamp(ra, planet.Radius * 0.5, planet.Radius * 2.0);
            return (float3)(center + dir * radius);
        }

        /// <summary>Whether a target sphere overlaps the volume placed at <paramref name="anchor"/> facing <paramref name="forward"/>.</summary>
        /// <remarks>
        /// Pre: planet.IsValid; planet.Radius >= MinSupportedRadiusM; volume.Kind != Caster; anchor and targetCenter
        ///      inside the combat shell (planet.Radius / 2 &lt;= |p − planet.Center| &lt;= 2 · planet.Radius, same
        ///      rounding slack as ResolveAnchor);
        ///      targetRadiusM >= 0 and finite; for Rect / Sector, forward is unit (±1e-3) and tangent at anchor
        ///      (|dot(forward, up)| &lt;= 1e-3). For Ally, anchor is the caster and the test is "ally within cast distance".
        /// Post: true iff the target sphere's geodesic footprint touches the volume's footprint and the radial height
        ///       difference is within HalfHeightMm + targetRadius (Ally ignores height). Only forward's tangent
        ///       direction matters, not its length within the tolerance. A target more than a quarter circumference
        ///       (angle > π/2) from the anchor never overlaps, whatever the volume's size: the local projection is only
        ///       used on the anchor's own hemisphere, where bearings are well conditioned (SOL r3 P3).
        ///       Monotone: a larger volume (any dimension) never turns true into false.
        /// Side effects: none.
        /// </remarks>
        public static bool Overlaps(in HitVolume volume, in PlanetConfig planet, float3 anchor, float3 forward,
            float3 targetCenter, float targetRadiusM)
        {
            RequirePlanet(planet);
            Contract.Requires(volume.Kind != HitVolumeKind.Caster, "self moves have no geometric test");
            Contract.Requires(targetRadiusM >= 0f && math.isfinite(targetRadiusM), "targetRadiusM must be finite and >= 0");

            bool onAnchorHemisphere = Local(planet, anchor, forward, targetCenter,
                volume.Kind == HitVolumeKind.Rect || volume.Kind == HitVolumeKind.Sector, out float x, out float z, out float height);
            if (!onAnchorHemisphere)
            {
                return false;
            }

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
        // combat uses (1 mm at R = 640 m is 1.6e-6 rad). Returns false (x = z = 0) when the point is on the far
        // hemisphere: near the antipode every bearing is float noise, and no volume reaches a quarter circumference.
        private static bool Local(in PlanetConfig planet, float3 anchor, float3 forward, float3 point, bool needsForward,
            out float x, out float z, out float height)
        {
            float3 da = anchor - planet.Center;
            float3 dp = point - planet.Center;
            RequireInDomain((double3)anchor - (double3)planet.Center, planet, "anchor");
            RequireInDomain((double3)point - (double3)planet.Center, planet, "target");
            float ra = math.length(da);
            float rp = math.length(dp);
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
            x = 0f;
            z = 0f;
            if (angle > math.PI / 2f)
            {
                return false;
            }

            float distance = angle * ra;
            if (!needsForward)
            {
                x = distance;
                return true;
            }

            // Re-orthonormalize: the Pre tolerance lets |forward| be 0.9995, which would otherwise shrink the measured
            // forward distance by 0.05 % and let a target 24.005 m away into a 24 m line (SOL G5).
            float3 f = math.normalize(forward - math.dot(forward, up) * up);
            float3 right = math.cross(up, f);

            // Bearing from components in the tangent basis, not from normalizing up2 − dot(up2, up)·up: near the
            // antipode that difference is a pure radial float residue, which projected to (0, 0) and put a target
            // 2010 m away at distance 0 (SOL r2 N1). On the near hemisphere |(px, pz)| = sin(angle), so the bearing
            // is only ill-conditioned when the points (nearly) coincide, where distance ~0 makes it irrelevant.
            float px = math.dot(up2, right);
            float pz = math.dot(up2, f);
            float bearingLength = math.sqrt(px * px + pz * pz);
            if (bearingLength < 1e-12f)
            {
                return true;
            }

            x = px / bearingLength * distance;
            z = pz / bearingLength * distance;
            return true;
        }

        private static void RequirePlanet(in PlanetConfig planet)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(planet.Radius >= MinSupportedRadiusM, "combat geometry supports planets with R >= 640 m only");
        }

        // Combat happens near the ground, so positions must lie within R/2..2R of the center. The bounded radius ratio
        // is what lets ResolveAnchor promise a landing precision at all: an aim point 2 m from the center seen from a
        // caster on the surface amplified float quantization ~4000x (SOL r3 P1). Checked in double: any float3
        // squared fits (3.4e38² ≪ 1.8e308), so neither overflow (SOL G6) nor underflow (r2 N4) can fool it, and NaN
        // fails both comparisons.
        private static void RequireInDomain(double3 fromCenter, in PlanetConfig planet, string name)
        {
            double r2 = math.lengthsq(fromCenter);
            double slackM = ShellSlack * (math.cmax(math.abs((double3)planet.Center)) + 2.0 * planet.Radius);
            double inner = planet.Radius * 0.5 - slackM;
            double outer = planet.Radius * 2.0 + slackM;
            Contract.Requires(r2 >= inner * inner && r2 <= outer * outer,
                name + " must be within R/2..2R of the planet center (combat shell)");
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
