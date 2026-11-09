using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Demo
{
    internal static class DemoNavigation
    {
        // A same-point or antipodal target has no unique tangent bearing; do not invent a direction for the HUD.
        internal static bool TryBearing(float3 up, float3 target, float3 heading, out float angle)
        {
            angle = 0f;
            if (!math.all(math.isfinite(up)) || !math.all(math.isfinite(target)) ||
                !math.all(math.isfinite(heading)) || math.lengthsq(up) < 1e-6f || math.lengthsq(target) < 1e-6f)
            {
                return false;
            }

            up = math.normalize(up);
            target = math.normalize(target);
            float3 toward = target - up * math.dot(up, target);
            float3 forward = heading - up * math.dot(up, heading);
            if (math.lengthsq(toward) < 1e-6f || math.lengthsq(forward) < 1e-6f)
            {
                return false;
            }

            PlanetFrame frame = PlanetFrame.FromUp(up);
            forward = frame.ProjectOnTangent(forward);
            toward = frame.ProjectOnTangent(toward);
            angle = math.degrees(math.atan2(math.dot(toward, math.cross(up, forward)), math.dot(toward, forward)));
            return true;
        }
    }
}
