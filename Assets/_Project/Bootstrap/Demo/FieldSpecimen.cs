using System;
using System.Collections.Generic;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Demo
{
    internal enum FieldForm { WindPump, Hoist, Wagon, Sled, MineCart, SteamCar, Skiff, Balloon, Hare, Heron, Beetle, Fish }

    internal sealed class FieldSpecimen
    {
        internal const int GeneratorVersion = 1;
        internal readonly FieldForm Form;
        internal readonly int Style, Age, Damage, Duty;
        internal readonly ArchRegion Region;
        internal readonly ArchEra Era, DetailEra;
        internal readonly ArchMix Mix;
        internal readonly float Scale;
        private readonly float3[] _route;
        private readonly float[] _heights;
        private readonly PlanetConfig _planet;
        private readonly int _legMs, _phaseMs;
        internal float3 Direction, Facing, Position;

        internal bool Animal => Form >= FieldForm.Hare;
        internal bool Solid => !Animal && Form != FieldForm.Balloon && Form != FieldForm.Skiff;
        internal float Radius => (Animal ? 0.65f : 1.55f) * Scale;
        internal float3 Center => Position + Direction * ((Animal ? 0.44f : 1f) * Scale);

        private FieldSpecimen(FieldForm form, ArchPlanetStyle culture, ArchMix mix, int style, int age, int damage,
            int duty, ArchEra detailEra, PlanetConfig planet, float3[] route, float[] heights, float routeLength, int phase)
        {
            Form = form;
            Region = culture.Region;
            Era = culture.Era;
            DetailEra = detailEra;
            Mix = mix;
            Style = style;
            Age = age;
            Damage = damage;
            Duty = duty;
            Scale = form >= FieldForm.Hare ? 0.65f + age * 0.00055f : 0.87f + style * 0.00028f;
            _planet = planet;
            _route = route;
            _heights = heights;
            _legMs = math.max(1000, (int)(1000f * routeLength / (Animal ? 1.3f : 2f)));
            _phaseMs = phase % (2 * _legMs);
            Step(0);
        }

        internal void Step(long nowMs)
        {
            int time = (int)((nowMs + _phaseMs) % (2 * _legMs));
            float t = Form <= FieldForm.Hoist ? 0f : 1f - math.abs(time - _legMs) / (float)_legMs;
            float sample = t * (_route.Length - 1);
            int i = math.min((int)sample, _route.Length - 2);
            Direction = math.normalize(math.lerp(_route[i], _route[i + 1], sample - i));
            float3 tangent = _route[i + 1] - _route[i];
            Facing = math.normalize(tangent - Direction * math.dot(tangent, Direction));
            if (time >= _legMs && Form > FieldForm.Hoist) Facing = -Facing;
            Position = _planet.ToWorld(Direction, math.lerp(_heights[i], _heights[i + 1], sample - i));
        }

        private static int Nearby(ref Pcg32 rng, int mean, int spread) =>
            BoundedNormal.Sample(ref rng, mean, spread, math.max(0, mean - 2 * spread), math.min(1000, mean + 2 * spread));

        internal static List<FieldSpecimen> Generate(PlanetConfig planet, RunLayout layout, RunPlan plan, string type,
            ulong seed, Func<float3, float> ground, Func<float3, bool> clear, Func<float3, bool> sea)
        {
            var result = new List<FieldSpecimen>();
            var culture = ArchitectureMeshes.PlanetStyleFor(type, seed);
            var common = new Pcg32(seed, 0xC017u);
            int design = common.NextInt(150, 851), occupation = common.NextInt(150, 851);
            var wear = ArchitectureMeshes.EraWear(culture.Era);
            int ageMean = Nearby(ref common, (int)(1000 * wear.age), 60);
            int damageMean = Nearby(ref common, (int)(1000 * wear.damage), 55);
            for (int r = 0; r < layout.Roads.Count; r++)
            {
                RunRoad road = layout.Roads[r];
                var local = new Pcg32(seed ^ 0xC017ul, (ulong)road.FromRegion);
                int designHere = Nearby(ref local, design, 70), dutyHere = Nearby(ref local, occupation, 120);
                int ageHere = Nearby(ref local, ageMean, 70), damageHere = Nearby(ref local, damageMean, 60);
                var mix = ArchitectureMeshes.MixFor(culture, SettlementKind.Village, plan.Regions[road.FromRegion].ThemeId, seed, road.FromRegion);
                for (int family = 0; family < 3; family++)
                {
                    var rng = new Pcg32(seed ^ 0xA917ul, (ulong)(3 * r + family));
                    int style = Nearby(ref rng, designHere, 85), duty = Nearby(ref rng, dutyHere, 155);
                    int age = Nearby(ref rng, ageHere, 100), damage = Nearby(ref rng, damageHere, 95);
                    int borrow = Nearby(ref rng, (int)(mix.Weight * 1000), 85);
                    var finish = new ArchMix(mix.SecondaryRegion, mix.SecondaryEra, borrow / 1000f, age / 1000f, damage / 1000f);
                    ArchEra detailEra = rng.NextInt(0, 1000) < borrow ? mix.SecondaryEra : culture.Era;
                    for (int attempt = 0; attempt < 14; attempt++)
                    {
                        int j = rng.NextInt(0, road.Points.Count - 1);
                        float3 a = road.Points[j], b = road.Points[j + 1];
                        float length = math.distance(a, b) * planet.Radius;
                        if (length < 1f) continue;
                        float3 side = math.normalizesafe(math.cross(a, b));
                        float offset = family == 0 ? road.WidthM * 0.5f + 3f : family == 2 ? road.WidthM + 3f : 0f;
                        float3 midpoint = math.normalize(a + b);
                        bool water = (road.Bridge[j] && road.Bridge[j + 1]) || sea(midpoint) || ground(midpoint) < 0f;
                        if (water) offset += road.WidthM + 5f;
                        offset *= rng.NextInt(0, 2) == 0 ? -1f : 1f;
                        FieldForm form = Choose(family, culture, duty, water);
                        bool aquatic = form == FieldForm.Fish || form == FieldForm.Skiff;
                        int segments = math.min(family == 1 ? 6 : 1, road.Points.Count - 1 - j);
                        int steps = segments * 7;
                        var route = new float3[steps + 1];
                        var heights = new float[steps + 1];
                        bool valid = true;
                        float routeLength = 0;
                        for (int k = 0; k <= steps; k++)
                        {
                            int segment = math.min(k / 7, segments - 1);
                            float along = k / 7f - segment;
                            float3 from = road.Points[j + segment], to = road.Points[j + segment + 1];
                            float3 dir = math.normalize(math.lerp(from, to, along) + side * (offset / planet.Radius));
                            float alt = ground(dir);
                            if (!clear(dir) || aquatic != (sea(dir) || alt < 0f))
                            {
                                valid = false;
                                break;
                            }
                            route[k] = dir;
                            if (k > 0) routeLength += math.distance(route[k - 1], dir) * planet.Radius;
                            heights[k] = aquatic ? (form == FieldForm.Fish ? -0.47f : 0f) :
                                alt + (form == FieldForm.Balloon ? 4f : form == FieldForm.Heron ? 1.7f : 0.08f);
                        }
                        if (!valid) continue;
                        result.Add(new FieldSpecimen(form, culture, finish, style, age, damage, duty, detailEra, planet,
                            route, heights, routeLength, rng.NextInt(0, 60000)));
                        break;
                    }
                }
            }
            return result;
        }

        private static FieldForm Choose(int family, ArchPlanetStyle culture, int duty, bool water)
        {
            if (family == 0) return duty < 500 ? FieldForm.WindPump : FieldForm.Hoist;
            if (family == 2) return water ? FieldForm.Fish : duty < 345 ? FieldForm.Beetle : duty > 655 ? FieldForm.Heron : FieldForm.Hare;
            if (water) return FieldForm.Skiff;
            if (duty > 760) return FieldForm.Balloon;
            if (culture.Region == ArchRegion.Ice) return FieldForm.Sled;
            if (culture.Era == ArchEra.Steampunk || culture.Era == ArchEra.Industrial)
                return duty < 500 ? FieldForm.MineCart : FieldForm.SteamCar;
            return FieldForm.Wagon;
        }
    }
}
