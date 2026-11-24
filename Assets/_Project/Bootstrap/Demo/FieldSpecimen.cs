using System;
using System.Collections.Generic;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Demo
{
    // Animals from Hare on (FieldSpecimen.Animal); the six after Fish follow the land they live on (planet-world.md#自然生物).
    internal enum FieldForm { WindPump, Hoist, Wagon, Sled, MineCart, SteamCar, Skiff, Balloon, Hare, Heron, Beetle, Fish,
        Deer, Goat, Fox, Lizard, Frog, Crab }

    internal sealed class FieldSpecimen
    {
        internal const int GeneratorVersion = 4;
        internal const int LoopPercent = 30;
        internal readonly FieldForm Form;
        internal readonly int Style, Age, Damage, Duty;
        /// <summary>True: drives one way round a closed road cycle (route[0] == route[^1]); false: shuttles end to end.</summary>
        internal readonly bool Loop;
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
        internal bool Drafted => Form == FieldForm.Wagon || Form == FieldForm.Sled;
        // Drafted vehicles span ~4.6 m (cart + beast ahead along Facing); one sphere centred mid-length covers both.
        internal float Radius => (Animal ? 0.65f : Drafted ? 2.3f : 1.55f) * Scale;
        internal float3 Center => Position + Direction * ((Animal ? 0.44f : 1f) * Scale) + (Drafted ? Facing * (1.1f * Scale) : float3.zero);
        internal int RouteCount => _route.Length;
        internal float3 RouteUp(int k) => _route[k];
        internal float3 RouteSurface(int k) => _planet.ToWorld(_route[k], _heights[k]);

        private FieldSpecimen(FieldForm form, ArchPlanetStyle culture, ArchMix mix, int style, int age, int damage,
            int duty, ArchEra detailEra, PlanetConfig planet, float3[] route, float[] heights, float routeLength, int phase,
            bool loop = false)
        {
            Loop = loop;
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
            _phaseMs = phase % (loop ? _legMs : 2 * _legMs);
            Step(0);
        }

        internal void Step(long nowMs)
        {
            int time = (int)((nowMs + _phaseMs) % (Loop ? _legMs : 2 * _legMs));
            float t = Form <= FieldForm.Hoist ? 0f : Loop ? time / (float)_legMs : 1f - math.abs(time - _legMs) / (float)_legMs;
            float sample = t * (_route.Length - 1);
            int i = math.min((int)sample, _route.Length - 2);
            Direction = math.normalize(math.lerp(_route[i], _route[i + 1], sample - i));
            float3 tangent = _route[i + 1] - _route[i];
            Facing = math.normalize(tangent - Direction * math.dot(tangent, Direction));
            if (!Loop && time >= _legMs && Form > FieldForm.Hoist) Facing = -Facing;
            Position = _planet.ToWorld(Direction, math.lerp(_heights[i], _heights[i + 1], sample - i));
        }

        private static int Nearby(ref Pcg32 rng, int mean, int spread) =>
            BoundedNormal.Sample(ref rng, mean, spread, math.max(0, mean - 2 * spread), math.min(1000, mean + 2 * spread));

        // terrainAt (landform id of a direction) picks wildlife that belongs where it stands; null keeps the v3 roadside
        // trio (hare, heron, beetle) everywhere.
        internal static List<FieldSpecimen> Generate(PlanetConfig planet, RunLayout layout, RunPlan plan, string type,
            ulong seed, Func<float3, float> ground, Func<float3, bool> clear, Func<float3, bool> sea,
            Func<float3, string> terrainAt = null)
        {
            var result = new List<FieldSpecimen>();
            var culture = ArchitectureMeshes.PlanetStyleFor(type, seed);
            var common = new Pcg32(seed, 0xC017u);
            int design = common.NextInt(150, 851), occupation = common.NextInt(150, 851);
            var wear = ArchitectureMeshes.EraWear(culture.Era);
            int ageMean = Nearby(ref common, (int)math.round(1000f * wear.age), 60);
            int damageMean = Nearby(ref common, (int)math.round(1000f * wear.damage), 55);
            List<(int road, bool forward)>[] cycleOf = Cycles(layout);
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
                        if (family == 2 && !water && terrainAt != null)
                        {
                            float3 at = math.normalize(midpoint + side * (offset / planet.Radius));
                            form = Native(terrainAt(at), math.abs(at.y), ground(at) / planet.Radius, duty);
                        }
                        bool aquatic = form == FieldForm.Fish || form == FieldForm.Skiff;
                        if (family == 1 && !aquatic && cycleOf[r] != null)
                        {
                            // Own stream per road: choosing loop vs shuttle must not shift any earlier draw.
                            var pick = new Pcg32(seed ^ 0x100Ful, (ulong)r);
                            if (pick.NextInt(0, 100) < LoopPercent &&
                                Polyline(cycleOf[r], layout, out List<float3> ring, out List<bool> ringBridge) &&
                                Sample(ring, ringBridge, form, ground, clear, sea, out float3[] dirs, out float[] alts,
                                    out bool[] ok) && Array.TrueForAll(ok, x => x))
                            {
                                float ringLength = 0f;
                                for (int k = 1; k < dirs.Length; k++) ringLength += math.distance(dirs[k - 1], dirs[k]) * planet.Radius;
                                result.Add(new FieldSpecimen(form, culture, finish, style, age, damage, duty, detailEra, planet,
                                    dirs, alts, ringLength, rng.NextInt(0, 60000), loop: true));
                                break;
                            }
                        }
                        if (family == 1 && !aquatic)
                        {
                            // REQ: docs/design/settlement-structures.md#交通工具 — land vehicles run the whole road
                            // (settlement to settlement, bridges included). The road does not depend on j, so a retry
                            // cannot help: no clear run means the slot is skipped.
                            if (LongRoute(planet, road, form, ground, clear, sea, out float3[] longRoute, out float[] longHeights,
                                    out float longLength))
                                result.Add(new FieldSpecimen(form, culture, finish, style, age, damage, duty, detailEra, planet,
                                    longRoute, longHeights, longLength, rng.NextInt(0, 60000)));
                            break;
                        }
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

        internal const float MinLongRouteM = 20f;
        private const int LongStepsPerSegment = 2; // road vertices are ≤ ~8 m apart; 4 m samples keep the cart on the curve
        private const float BridgeDeckM = 0.6f, RoadLiftM = 0.12f; // mirror SettlementView so carts ride the drawn deck

        // Longest contiguous clear stretch along the road centre line. Ends inside settlements, at the landing gap or at
        // walls are trimmed rather than rejecting the whole road.
        private static bool LongRoute(PlanetConfig planet, RunRoad road, FieldForm form, Func<float3, float> ground,
            Func<float3, bool> clear, Func<float3, bool> sea, out float3[] route, out float[] heights, out float length)
        {
            Sample(road.Points, road.Bridge, form, ground, clear, sea, out float3[] dirs, out float[] alts, out bool[] ok);
            int samples = dirs.Length, bestStart = 0, bestCount = 0;
            for (int k = 0, start = 0; k <= samples; k++)
            {
                if (k < samples && ok[k]) continue;
                if (k - start > bestCount) { bestStart = start; bestCount = k - start; }
                start = k + 1;
            }
            route = null;
            heights = null;
            length = 0f;
            if (bestCount < 2) return false;
            route = new float3[bestCount];
            heights = new float[bestCount];
            for (int k = 0; k < bestCount; k++)
            {
                route[k] = dirs[bestStart + k];
                heights[k] = alts[bestStart + k];
                if (k > 0) length += math.distance(route[k - 1], route[k]) * planet.Radius;
            }
            return length >= MinLongRouteM;
        }

        // Densifies a road-centre polyline and classifies each sample; a row touching a bridge vertex rides the deck.
        private static bool Sample(IReadOnlyList<float3> points, IReadOnlyList<bool> bridges, FieldForm form,
            Func<float3, float> ground, Func<float3, bool> clear, Func<float3, bool> sea,
            out float3[] dirs, out float[] alts, out bool[] ok)
        {
            int segments = points.Count - 1, samples = segments * LongStepsPerSegment + 1;
            dirs = new float3[samples];
            alts = new float[samples];
            ok = new bool[samples];
            for (int k = 0; k < samples; k++)
            {
                int segment = math.min(k / LongStepsPerSegment, segments - 1);
                float along = k / (float)LongStepsPerSegment - segment;
                float3 dir = math.normalize(math.lerp(points[segment], points[segment + 1], along));
                bool bridge = bridges[segment] || bridges[segment + 1];
                float alt = ground(dir);
                bool wet = sea(dir) || alt < 0f;
                dirs[k] = dir;
                ok[k] = clear(dir) && (!wet || bridge || form == FieldForm.Balloon);
                alts[k] = form == FieldForm.Balloon ? math.max(alt, 0f) + 4f :
                    bridge ? math.max(BridgeDeckM, alt + RoadLiftM) : alt + 0.08f;
            }
            return samples >= 2;
        }

        // REQ: docs/design/settlement-structures.md#交通工具 — each Loop road plus the tree path between its ends is one
        // closed cycle; every road on it (the loop road included) may host a loop vehicle. First cycle wins on overlap.
        private static List<(int road, bool forward)>[] Cycles(RunLayout layout)
        {
            var cycleOf = new List<(int, bool)>[layout.Roads.Count];
            for (int l = 0; l < layout.Roads.Count; l++)
            {
                RunRoad loop = layout.Roads[l];
                if (loop.Class != RoadClass.Loop) continue;
                var via = new Dictionary<int, (int road, bool forward, int from)>();
                var queue = new Queue<int>();
                via[loop.ToRegion] = (-1, true, -1);
                queue.Enqueue(loop.ToRegion);
                while (queue.Count > 0 && !via.ContainsKey(loop.FromRegion))
                {
                    int at = queue.Dequeue();
                    for (int r = 0; r < layout.Roads.Count; r++)
                    {
                        RunRoad road = layout.Roads[r];
                        if (road.Class == RoadClass.Loop) continue;
                        int next = road.FromRegion == at ? road.ToRegion : road.ToRegion == at ? road.FromRegion : -1;
                        if (next < 0 || via.ContainsKey(next)) continue;
                        via[next] = (r, road.FromRegion == at, at);
                        queue.Enqueue(next);
                    }
                }
                if (!via.ContainsKey(loop.FromRegion)) continue;
                // via[x] records travel from -> x outward from ToRegion, which is exactly the ToRegion -> FromRegion leg.
                var back = new List<(int, bool)>();
                for (int at = loop.FromRegion; at != loop.ToRegion; at = via[at].from)
                    back.Add((via[at].road, via[at].forward));
                back.Reverse();
                var cycle = new List<(int, bool)> { (l, true) };
                cycle.AddRange(back);
                foreach ((int road, bool _) in cycle)
                    if (cycleOf[road] == null) cycleOf[road] = cycle;
            }
            return cycleOf;
        }

        // Concatenates the cycle's oriented polylines; the shared joint vertex appears once and the ring closes on itself.
        private static bool Polyline(List<(int road, bool forward)> cycle, RunLayout layout, out List<float3> points,
            out List<bool> bridges)
        {
            points = new List<float3>();
            bridges = new List<bool>();
            foreach ((int index, bool forward) in cycle)
            {
                RunRoad road = layout.Roads[index];
                int n = road.Points.Count;
                for (int k = points.Count == 0 ? 0 : 1; k < n; k++)
                {
                    int at = forward ? k : n - 1 - k;
                    points.Add(road.Points[at]);
                    bridges.Add(road.Bridge[at]);
                }
            }
            return points.Count >= 4 && math.distance(points[0], points[points.Count - 1]) < 1e-4f;
        }

        // REQ: planet-world.md#自然生物 緯度×海拔 — wildlife follows landform first, then cold latitudes and high ground.
        // absLat is |up.y|; altShare is ground altitude over the radius (highland ≥ 0.012, as ScatterPlanner).
        internal static FieldForm Native(string terrain, float absLat, float altShare, int duty)
        {
            bool cold = absLat > 0.7f || terrain is "tundra" or "glacier" or "ice_field" or "cirque" or "moraine";
            bool high = altShare >= 0.012f || terrain is "mountain" or "slate_cliffs" or "granite_domes";
            switch (terrain)
            {
                case "swamp":
                case "river_valley":
                    return duty < 400 ? FieldForm.Frog : duty < 750 ? FieldForm.Heron : FieldForm.Beetle;
                case "desert":
                case "canyon":
                case "rift_valley":
                case "basalt_terrace":
                case "volcano_foothills":
                    return duty < 620 ? FieldForm.Lizard : FieldForm.Beetle;
                case "archipelago":
                case "fjord":
                    return duty < 500 ? FieldForm.Crab : FieldForm.Heron;
            }

            if (cold) return duty < 550 ? FieldForm.Fox : FieldForm.Hare;
            if (high) return duty < 600 ? FieldForm.Goat : FieldForm.Hare;
            return duty < 380 ? FieldForm.Deer : duty < 620 ? FieldForm.Hare : duty < 820 ? FieldForm.Beetle : FieldForm.Heron;
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
