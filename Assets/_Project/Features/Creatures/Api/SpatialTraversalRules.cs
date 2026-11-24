using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;

namespace Cosmimera.Creatures
{
    /// <summary>Outcome of evaluating water traversal and submersion access for a team's active field utilities.</summary>
    /// <remarks>Pre: produced by <see cref="SpatialTraversalRules.EvaluateWaterAccess"/>. Post: immutable value. Side effects: none.</remarks>
    public readonly struct WaterAccessResult
    {
        internal WaterAccessResult(
            bool canCrossSurface,
            bool canSubmerge,
            bool canReachTargetDepth,
            int surfaceSpeedPermille,
            int underwaterSpeedPermille)
        {
            CanCrossSurface = canCrossSurface;
            CanSubmerge = canSubmerge;
            CanReachTargetDepth = canReachTargetDepth;
            SurfaceSpeedPermille = surfaceSpeedPermille;
            UnderwaterSpeedPermille = underwaterSpeedPermille;
        }

        /// <summary>True when the team can move on the water surface (shallow wading &lt;= 1 m, Swim, or Dive).</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanCrossSurface { get; }

        /// <summary>True when the team can submerge deeper than 1 m (requires Dive).</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanSubmerge { get; }

        /// <summary>True when the queried water depth is reachable (&lt;= 1 m or Dive is active).</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanReachTargetDepth { get; }

        /// <summary>Water surface movement speed multiplier in permille (Swim 1150 &gt; Dive 850 &gt; Wade 800).</summary>
        /// <remarks>Pre: none. Post: &gt; 0 permille. Side effects: none.</remarks>
        public int SurfaceSpeedPermille { get; }

        /// <summary>Underwater movement speed multiplier in permille (Dive 1000, or 0 without Dive).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0 permille. Side effects: none.</remarks>
        public int UnderwaterSpeedPermille { get; }
    }

    /// <summary>Outcome of evaluating vertical flight/levitation access relative to planet stratosphere and space ceiling.</summary>
    /// <remarks>Pre: produced by <see cref="SpatialTraversalRules.EvaluateAltitudeAccess"/>. Post: immutable value. Side effects: none.</remarks>
    public readonly struct AltitudeAccessResult
    {
        internal AltitudeAccessResult(
            bool canLiftOff,
            bool canReachTargetAltitude,
            bool ignoresStratosphereCap,
            float maxAltitudeM,
            int airSpeedPermille)
        {
            CanLiftOff = canLiftOff;
            CanReachTargetAltitude = canReachTargetAltitude;
            IgnoresStratosphereCap = ignoresStratosphereCap;
            MaxAltitudeM = maxAltitudeM;
            AirSpeedPermille = airSpeedPermille;
        }

        /// <summary>True when either Fly or Levitate is active.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanLiftOff { get; }

        /// <summary>True when the queried altitude is within the team's reachable ceiling.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanReachTargetAltitude { get; }

        /// <summary>True when Levitate allows ascending beyond the 1.0 R stratosphere limit.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool IgnoresStratosphereCap { get; }

        /// <summary>Maximum altitude above sea level (meters) reachable by the team.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0f. Side effects: none.</remarks>
        public float MaxAltitudeM { get; }

        /// <summary>Airborne horizontal speed multiplier in permille at the queried altitude (0 when unreachable).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0 permille. Side effects: none.</remarks>
        public int AirSpeedPermille { get; }
    }

    /// <summary>Outcome of evaluating fast-travel return (<c>return_village</c>) eligibility to a settlement.</summary>
    /// <remarks>Pre: produced by <see cref="SpatialTraversalRules.CanReturnToSettlement"/>. Post: immutable value. Side effects: none.</remarks>
    public readonly struct ReturnSettlementResult
    {
        internal ReturnSettlementResult(bool allowed, bool requiresUtility, bool requiresVisit, bool requiresDive)
        {
            Allowed = allowed;
            RequiresUtility = requiresUtility;
            RequiresVisit = requiresVisit;
            RequiresDive = requiresDive;
        }

        /// <summary>True when all return requirements are satisfied.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool Allowed { get; }

        /// <summary>True when blocked because the team lacks the ReturnVillage field utility.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool RequiresUtility { get; }

        /// <summary>True when blocked because the target settlement is neither starting town nor visited.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool RequiresVisit { get; }

        /// <summary>True when blocked because the target is a DeepSea settlement and Dive is not active.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool RequiresDive { get; }
    }

    /// <summary>
    /// Pure rules for aquatic depth gating, stratosphere/orbital altitude bounds, and settlement fast-travel.
    /// REQ: docs/design/field-utilities.md#水域與垂直空間通行.
    /// </summary>
    /// <remarks>Pre: planetRadius within PlanetConfig bounds. Post: pure deterministic results. Side effects: none.</remarks>
    public static class SpatialTraversalRules
    {
        /// <summary>Stratosphere altitude above sea level (1.0 × radius), serving as both Fly ceiling and sky-island upper limit.</summary>
        /// <remarks>Pre: planetRadius in [PlanetConfig.MinSupportedRadius, PlanetConfig.MaxSupportedRadius]. Post: positive meters. Side effects: none.</remarks>
        public static float StratosphereAltitudeM(float planetRadius)
        {
            Contract.Requires(
                planetRadius >= PlanetConfig.MinSupportedRadius && planetRadius <= PlanetConfig.MaxSupportedRadius,
                "planetRadius must be within PlanetConfig supported bounds");
            return planetRadius * FieldUtilityRules.StratosphereRadiusFactor;
        }

        /// <summary>Standard orbital altitude of space stations (1.25 × radius), above the Fly stratosphere cap.</summary>
        /// <remarks>Pre: planetRadius in [PlanetConfig.MinSupportedRadius, PlanetConfig.MaxSupportedRadius]. Post: positive meters &gt; stratosphere. Side effects: none.</remarks>
        public static float SpaceStationAltitudeM(float planetRadius)
        {
            Contract.Requires(
                planetRadius >= PlanetConfig.MinSupportedRadius && planetRadius <= PlanetConfig.MaxSupportedRadius,
                "planetRadius must be within PlanetConfig supported bounds");
            return planetRadius * FieldUtilityRules.SpaceStationRadiusFactor;
        }

        /// <summary>Maximum altitude reachable via Levitate (1.6 × radius).</summary>
        /// <remarks>Pre: planetRadius in [PlanetConfig.MinSupportedRadius, PlanetConfig.MaxSupportedRadius]. Post: positive meters. Side effects: none.</remarks>
        public static float SpaceCeilingAltitudeM(float planetRadius)
        {
            Contract.Requires(
                planetRadius >= PlanetConfig.MinSupportedRadius && planetRadius <= PlanetConfig.MaxSupportedRadius,
                "planetRadius must be within PlanetConfig supported bounds");
            return planetRadius * FieldUtilityRules.SpaceCeilingRadiusFactor;
        }

        /// <summary>Highest altitude (m) reachable by the team under its current Fly/Levitate modifiers.</summary>
        /// <remarks>Pre: planetRadius in [PlanetConfig.MinSupportedRadius, PlanetConfig.MaxSupportedRadius]. Post: &gt;= 0f. Side effects: none.</remarks>
        public static float MaxReachableAltitudeM(in TeamUtilityModifiers modifiers, float planetRadius)
        {
            Contract.Requires(
                planetRadius >= PlanetConfig.MinSupportedRadius && planetRadius <= PlanetConfig.MaxSupportedRadius,
                "planetRadius must be within PlanetConfig supported bounds");
            if (modifiers.CanLevitate)
            {
                return SpaceCeilingAltitudeM(planetRadius);
            }

            if (modifiers.CanFly)
            {
                return StratosphereAltitudeM(planetRadius);
            }

            return 0f;
        }

        /// <summary>Evaluates water-surface crossing, submersion (&gt; 1 m), and swimming vs diving speeds.</summary>
        /// <remarks>Pre: waterDepthM &gt;= 0f. Post: returns immutable <see cref="WaterAccessResult"/>. Side effects: none.</remarks>
        public static WaterAccessResult EvaluateWaterAccess(in TeamUtilityModifiers modifiers, float waterDepthM)
        {
            Contract.Requires(waterDepthM >= 0f, "waterDepthM must be non-negative");

            bool shallowOk = waterDepthM <= FieldUtilityRules.DiveDepthThresholdM;
            bool canDive = modifiers.CanDive;
            bool canSwim = modifiers.CanSwim;

            bool canCrossSurface = shallowOk || canSwim || canDive;
            bool canReachDepth = shallowOk || canDive;
            int surfaceSpeed = canSwim
                ? FieldUtilityRules.SwimSpeedPermille
                : (canDive ? FieldUtilityRules.DiveSurfaceSpeedPermille : FieldUtilityRules.ShallowWadeSpeedPermille);
            int underwaterSpeed = canDive ? FieldUtilityRules.DiveUnderwaterSpeedPermille : 0;

            return new WaterAccessResult(canCrossSurface, canDive, canReachDepth, surfaceSpeed, underwaterSpeed);
        }

        /// <summary>Evaluates whether the team can ascend to <paramref name="targetAltitudeM"/> and at what speed.</summary>
        /// <remarks>Pre: planetRadius valid; targetAltitudeM &gt;= 0f. Post: returns immutable <see cref="AltitudeAccessResult"/>. Side effects: none.</remarks>
        public static AltitudeAccessResult EvaluateAltitudeAccess(
            in TeamUtilityModifiers modifiers,
            float planetRadius,
            float targetAltitudeM)
        {
            Contract.Requires(
                planetRadius >= PlanetConfig.MinSupportedRadius && planetRadius <= PlanetConfig.MaxSupportedRadius,
                "planetRadius must be within PlanetConfig supported bounds");
            Contract.Requires(targetAltitudeM >= 0f, "targetAltitudeM must be non-negative");

            float stratosphereM = StratosphereAltitudeM(planetRadius);
            float maxAltM = MaxReachableAltitudeM(modifiers, planetRadius);
            bool canLiftOff = modifiers.CanFly || modifiers.CanLevitate;
            bool canReach = targetAltitudeM <= maxAltM;

            int airSpeed = 0;
            if (targetAltitudeM > stratosphereM)
            {
                if (modifiers.CanLevitate && canReach)
                {
                    airSpeed = FieldUtilityRules.LevitateSpeedPermille;
                }
            }
            else if (modifiers.CanFly)
            {
                airSpeed = FieldUtilityRules.FlySpeedPermille;
            }
            else if (modifiers.CanLevitate)
            {
                airSpeed = FieldUtilityRules.LevitateSpeedPermille;
            }

            return new AltitudeAccessResult(canLiftOff, canReach, modifiers.CanLevitate, maxAltM, airSpeed);
        }

        /// <summary>Checks whether <c>return_village</c> can teleport the party to a settlement in <paramref name="domain"/>.</summary>
        /// <remarks>Pre: domain is a defined <see cref="SettlementDomain"/>. Post: returns eligibility and block reason. Side effects: none.</remarks>
        public static ReturnSettlementResult CanReturnToSettlement(
            in TeamUtilityModifiers modifiers,
            SettlementDomain domain,
            bool isVisitedOrStart)
        {
            Contract.Requires(System.Enum.IsDefined(typeof(SettlementDomain), domain), "domain must be defined");

            bool needsUtility = !modifiers.CanReturnVillage;
            bool needsVisit = !isVisitedOrStart;
            bool needsDive = domain == SettlementDomain.DeepSea && !modifiers.CanDive;
            bool allowed = !needsUtility && !needsVisit && !needsDive;
            return new ReturnSettlementResult(allowed, needsUtility, needsVisit, needsDive);
        }

        /// <summary>Checks whether Migratory Return (<c>return_village</c>) can teleport the party back to Cradle Moon.</summary>
        /// <remarks>
        /// REQ: docs/design/lobby-meta.md#地球大廳, field-utilities.md#4-主動生活主動技.
        /// Pre: none.
        /// Post: returns eligibility with RequiresUtility = !modifiers.CanReturnVillage and RequiresVisit = !tutorialDone.
        /// Side effects: none.
        /// </remarks>
        public static ReturnSettlementResult CanReturnToCradleMoon(
            in TeamUtilityModifiers modifiers,
            bool tutorialDone)
        {
            bool needsUtility = !modifiers.CanReturnVillage;
            bool needsVisit = !tutorialDone;
            bool allowed = !needsUtility && !needsVisit;
            return new ReturnSettlementResult(allowed, needsUtility, needsVisit, false);
        }
    }
}
