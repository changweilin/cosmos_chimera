using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class FieldMeshes
    {
        private readonly SceneryMeshes.Part _part = new SceneryMeshes.Part();
        private readonly Color _body, _frame, _accent, _pale;
        private readonly FieldSpecimen _spec;
        private static readonly Color Ink = new Color(0.085f, 0.079f, 0.095f);

        internal FieldMeshes(FieldSpecimen spec)
        {
            _spec = spec;
            var palette = ArchitectureMeshes.PaletteFor(spec.Region, spec.Era, BuildingKind.House, spec.Mix);
            _body = Color.Lerp(palette.Wall, palette.Roof, spec.Style * 0.0008f);
            _body = Color.Lerp(_body, palette.Weathering, spec.Age * 0.00042f);
            _frame = Color.Lerp(palette.Trim, Ink, 0.3f);
            _accent = Color.Lerp(palette.Roof, palette.Glow, 0.32f);
            Color pigment = spec.Region switch
            {
                ArchRegion.Ice => new Color(0.1f,0.39f,0.57f),
                ArchRegion.Lava => new Color(0.72f,0.17f,0.08f),
                ArchRegion.Crystal => new Color(0.17f,0.58f,0.57f),
                ArchRegion.Twilight => new Color(0.47f,0.23f,0.51f),
                ArchRegion.Storm => new Color(0.27f,0.34f,0.56f),
                _ => new Color(0.08f,0.46f,0.40f),
            };
            _accent = Color.Lerp(pigment, _accent, 0.24f + spec.Age * 0.00021f);
            _frame = Color.Lerp(new Color(0.28f,0.15f,0.09f), _frame, 0.39f);
            Color native = spec.Form switch
            {
                FieldForm.Hare => new Color(0.65f,0.36f,0.18f),
                FieldForm.Beetle => new Color(0.12f,0.31f,0.22f),
                FieldForm.Heron => new Color(0.75f,0.82f,0.77f),
                FieldForm.Fish => new Color(0.27f,0.53f,0.56f),
                FieldForm.Deer => new Color(0.62f,0.4f,0.22f),
                FieldForm.Goat => new Color(0.86f,0.83f,0.76f),
                FieldForm.Fox => spec.Region == ArchRegion.Ice ? new Color(0.93f,0.94f,0.96f) : new Color(0.82f,0.42f,0.14f),
                FieldForm.Lizard => new Color(0.55f,0.52f,0.3f),
                FieldForm.Frog => new Color(0.32f,0.56f,0.24f),
                FieldForm.Crab => new Color(0.78f,0.3f,0.18f),
                _ => new Color(0.63f,0.38f,0.17f),
            };
            _body = Color.Lerp(native, _body, 0.26f + spec.Age * 0.00024f);
            _pale = Color.Lerp(_body, new Color(0.98f, 0.91f, 0.74f), 0.65f);
        }

        internal Mesh Build(bool separateMovingParts = false)
        {
            if (_spec.Animal) Animal(separateMovingParts);
            else if (_spec.Form <= FieldForm.Hoist) Machine(separateMovingParts);
            else Vehicle(separateMovingParts);
            if (!_spec.Animal) MakerMark();
            return Finish();
        }

        internal float3 RotorPivot => P(0,1.9f + _spec.Style * 0.00055f,0.22f);

        internal Mesh BuildMovingPart(int side = 0)
        {
            if (_spec.Form == FieldForm.WindPump) Rotor(float3.zero);
            else if (_spec.Form == FieldForm.Heron) Feathers(side, true, float3.zero);
            else Wheel(float3.zero, 0.33f);
            return Finish();
        }

        /// <summary>Builds the mine-cart rail bed under the whole shuttle route, relative to <paramref name="origin"/>.</summary>
        /// <remarks>
        /// Pre: spec.Form == MineCart. Post: mesh spans every route sample; origin == RouteSurface(0) in world space.
        /// Side effects: allocates a Mesh the caller owns.
        /// </remarks>
        internal Mesh BuildTrack(out float3 origin)
        {
            // REQ: docs/design/settlement-structures.md#交通工具（礦車／軌道）
            origin = _spec.RouteSurface(0);
            int n = _spec.RouteCount;
            // Vertices stay origin-relative: absolute coordinates at R=640 m lose ~0.1 mm per ulp, enough to shimmer thin rails.
            var points = new float3[n];
            var sides = new float3[n];
            for (int k = 0; k < n; k++) points[k] = _spec.RouteSurface(k) - origin;
            for (int k = 0; k < n; k++)
            {
                float3 tangent = points[math.min(k + 1, n - 1)] - points[math.max(k - 1, 0)];
                sides[k] = math.normalizesafe(math.cross(_spec.RouteUp(k), tangent));
            }
            // Gauge matches the cart's wheel track (±0.6496 local) so the wheels sit on the rails at every scale.
            float gauge = 0.6496f * _spec.Scale;
            Color rail = Color.Lerp(_frame, new Color(0.42f, 0.44f, 0.47f), 0.6f);
            Color sleeper = Color.Lerp(_frame, _pale, 0.18f);
            float carry = 0f;
            for (int k = 0; k + 1 < n; k++)
            {
                float3 upA = _spec.RouteUp(k), upB = _spec.RouteUp(k + 1);
                for (int s = -1; s <= 1; s += 2)
                    Spar(points[k] + sides[k] * (s * gauge) + upA * 0.03f,
                        points[k + 1] + sides[k + 1] * (s * gauge) + upB * 0.03f, 0.035f, 0.035f, rail, 4);
                float length = math.distance(points[k], points[k + 1]);
                for (; carry < length; carry += 0.8f)
                {
                    float t = carry / length;
                    float3 at = math.lerp(points[k], points[k + 1], t);
                    float3 up = math.normalize(math.lerp(upA, upB, t));
                    float3 side = math.normalizesafe(math.lerp(sides[k], sides[k + 1], t)) * (gauge + 0.22f);
                    Spar(at - side - up * 0.03f, at + side - up * 0.03f, 0.055f, 0.055f, sleeper, 4);
                }
                carry -= length;
            }
            return Finish();
        }

        private Mesh Finish()
        {
            var data = new SceneryMeshes.MeshData();
            data.EmitFaceted(_part, 0, 0.8f, (p, normal, color) =>
            {
                // Long strokes follow the body grain. Exposed ridges catch light; recessed undersides retain the base dye.
                float grain = noise.snoise(p * P(9f, 3f, 2f) + _spec.Style * 0.032f);
                float wash = math.saturate((grain - 0.12f) * 1.8f) * (_spec.Age * 0.00027f);
                Color painted = Color.Lerp(color, _pale, wash);
                float edge = math.saturate(grain * 3f + normal.y - 1.15f) * (_spec.Damage * 0.00046f);
                return (normal, Color.Lerp(painted, _frame, edge));
            });
            return data.ToMesh(_spec.Form.ToString());
        }

        private static float3 P(float x, float y, float z) => new float3(x, y, z);

        private void Oval(float3 center, float3 radii, Color color, float tilt = 0f)
        {
            var ico = SceneryMeshes.Ico.Get(1);
            int first = _part.P.Count;
            foreach (float3 v in ico.Vertices)
            {
                float3 p = v * radii;
                p.z += p.y * tilt;
                _part.Add(center + p, color * new Color(0.93f + 0.07f * v.y, 0.94f + 0.06f * v.y, 1, 1), float2.zero);
            }
            for (int t = 0; t < ico.Triangles.Length; t += 3)
                _part.Tri(first + ico.Triangles[t], first + ico.Triangles[t + 1], first + ico.Triangles[t + 2], center);
        }

        private void Spar(float3 a, float3 b, float foot, float tip, Color color, int sides = 6)
        {
            float3 axis = math.normalize(b - a);
            float3 u = math.normalize(math.cross(axis, math.abs(axis.y) < 0.85f ? P(0,1,0) : P(1,0,0)));
            float3 v = math.cross(axis, u), inside = (a + b) * 0.5f;
            int first = _part.P.Count;
            for (int i = 0; i < sides; i++)
            {
                float theta = 2 * math.PI * i / sides;
                float3 dir = u * math.cos(theta) + v * math.sin(theta);
                _part.Add(a + dir * foot, color, float2.zero);
                _part.Add(b + dir * tip, color, float2.zero);
            }
            for (int i = 0; i < sides; i++)
            {
                int j = first + 2 * i, k = first + 2 * ((i + 1) % sides);
                _part.Tri(j, k, k + 1, inside);
                _part.Tri(j, k + 1, j + 1, inside);
                if (i > 0 && i < sides - 1)
                {
                    _part.Tri(first, j, k, inside);
                    _part.Tri(first + 1, j + 1, k + 1, inside);
                }
            }
        }

        // Four fabric panels meet at a raised seam, giving canvas a belly and a legible folded highlight.
        private void Cloth(float3 a, float3 b, float3 c, float3 d, float3 swell, Color color)
        {
            float3 mid = (a + b + c + d) * 0.25f + swell;
            int first = _part.P.Count;
            foreach (float3 v in new[] { a, b, c, d, mid })
                _part.Add(v, color, float2.zero);
            for (int i = 0; i < 4; i++)
            {
                int next = first + (i + 1) % 4;
                _part.Tri(first + i, next, first + 4, mid - swell);
                int back = _part.P.Count;
                _part.Add(_part.P[first + i], color, float2.zero);
                _part.Add(_part.P[next], color, float2.zero);
                _part.Add(mid, color, float2.zero);
                _part.Tri(back, back + 1, back + 2, mid + swell);
            }
        }

        private void Wheel(float3 hub, float radius)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * math.PI / 6, b = (i + 1) * math.PI / 6;
                float3 rim = hub + P(0, math.sin(a), math.cos(a)) * radius;
                Spar(rim, hub + P(0, math.sin(b), math.cos(b)) * radius, 0.052f, 0.052f, _frame);
                if (i % 2 == 0) Spar(hub, rim, 0.035f, 0.025f, _pale);
            }
            Oval(hub, P(0.12f, 0.1f, 0.1f), _accent);
        }

        private void Machine(bool rigged)
        {
            float height = 1.9f + _spec.Style * 0.00055f;
            float3 crown = P(0,height,0);
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    float3 foot = P(x * 0.55f,0.14f,z * 0.52f);
                    Oval(foot, P(0.21f,0.15f,0.22f), _body);
                    Spar(foot, crown + P(x * 0.12f,0,z * 0.12f), 0.1f, 0.08f, _frame, 4);
                }
                Spar(P(x * 0.4f,0.6f,-0.43f), P(x * 0.25f,1.4f,0.24f), 0.055f, 0.05f, _accent);
            }
            if (_spec.Form == FieldForm.Hoist)
            {
                float3 boom = crown + P(0,0.44f,1.1f);
                Spar(crown - P(0,0,0.6f), boom, 0.16f, 0.095f, _body, 4);
                Spar(P(0,1,0.25f), boom, 0.08f, 0.06f, _frame);
                Spar(boom, boom - P(0,1.27f,0), 0.024f, 0.024f, Ink);
                Oval(boom - P(0,1.39f,0), P(0.32f,0.24f,0.32f), _accent);
                Wheel(P(0.44f,0.85f,0),0.4f);
                return;
            }
            // The rotor drives a vertical pump rod inside the braced timber tower.
            Spar(P(0,0.05f,0),P(0,0.7f,0),0.33f,0.26f,_body,10);
            for (int band = 0; band < 3; band++)
                Spar(P(0,0.12f + band * 0.22f,0),P(0,0.17f + band * 0.22f,0),0.34f - band * 0.022f,0.34f - band * 0.022f,_accent,10);
            Spar(P(0,0.63f,0),crown,0.073f,0.065f,_pale,8);
            Spar(P(0,0.37f,0),P(0,0.37f,0.6f),0.105f,0.105f,_body,8);
            Spar(P(0,0.37f,0.6f),P(0,0.25f,0.69f),0.11f,0.09f,_accent,8);
            Spar(crown + P(0,0,0.18f),crown - P(0,0,0.99f),0.13f,0.055f,_body);
            Cloth(crown - P(0,0.06f,0.54f),crown + P(0,0.24f,-0.83f),
                crown + P(0,0.29f,-1.2f),crown - P(0,0.08f,1.09f),P(0.035f,0,0),_accent);
            if (!rigged) Rotor(RotorPivot);
        }

        private void Rotor(float3 crown)
        {
            Oval(crown, P(0.16f,0.16f,0.18f), _accent);
            for (int i = 0; i < 6; i++)
            {
                float a = i * math.PI / 3f;
                float3 ray = P(math.cos(a),math.sin(a),0), tangent = P(-ray.y,ray.x,0);
                float reach = i == 2 ? 1f - 0.55f * _spec.Damage / 1000f : 1f;
                Spar(crown, crown + ray * reach, 0.035f, 0.03f, _frame);
                float3 inner = crown + ray * 0.32f, outer = crown + ray * reach;
                Cloth(inner, outer, outer + tangent * 0.31f, inner + tangent * 0.1f, P(0,0,0.065f), _pale);
                Cloth(outer - ray * 0.15f, outer, outer + tangent * 0.31f, outer - ray * 0.15f + tangent * 0.28f,
                    P(0,0,0.08f), _accent);
            }
        }

        private void MakerMark()
        {
            bool machine = _spec.Form <= FieldForm.Hoist;
            float3 center = machine ? P(0,1.12f,-0.22f) :
                P(0, _spec.Form == FieldForm.Skiff || _spec.Form == FieldForm.Balloon ? 0.36f : 0.62f, -0.99f);
            if (_spec.DetailEra == ArchEra.Steampunk || _spec.DetailEra == ArchEra.Industrial)
            {
                Spar(center + P(-0.27f,0,0), center + P(0.27f,0,0),0.085f,0.085f,_accent,8);
                for (int side = -1; side <= 1; side += 2)
                    Spar(center + P(side * 0.25f,-0.08f,0), center + P(side * 0.25f,0.16f,0),0.095f,0.095f,_frame);
            }
            else if (_spec.DetailEra == ArchEra.Future)
            {
                Oval(center,P(0.35f,0.11f,0.075f),_frame);
                Oval(center - P(0,0,0.045f),P(0.28f,0.052f,0.07f),_pale);
            }
            else
            {
                for (int i = -1; i <= 1; i++)
                {
                    float3 knot = center + P(i * 0.13f,0,0);
                    Oval(knot,P(0.079f,0.084f,0.074f),_accent);
                    Spar(knot,knot - P(0,0.19f + 0.08f * (1 - math.abs(i)),0),0.043f,0.018f,_pale,4);
                }
            }
        }

        private void Vehicle(bool rigged)
        {
            FieldForm f = _spec.Form;
            bool water = f == FieldForm.Skiff, balloon = f == FieldForm.Balloon;
            float deck = water || balloon ? 0.25f : f == FieldForm.Sled ? 0.28f : 0.55f;
            Oval(P(0,deck,0), P(0.64f,0.27f,1.24f), _frame);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.58f;
                Spar(P(x,deck + 0.16f,-0.85f), P(x,deck + 0.23f,0.93f), 0.11f, 0.08f, _accent);
                if (f == FieldForm.Sled)
                {
                    Spar(P(x,0.08f,-1.1f), P(x,0.08f,1.08f), 0.08f, 0.08f, _frame);
                    Spar(P(x,0.08f,1.08f), P(x,0.27f,1.32f), 0.08f, 0.05f, _pale);
                }
                else if (!water && !balloon && !rigged)
                    for (int z = -1; z <= 1; z += 2) Wheel(P(x * 1.12f,0.35f,z * 0.73f),0.33f);
                if (balloon)
                    for (int z = -1; z <= 1; z += 2)
                        Spar(P(x,deck,z * 0.64f), P(x * 1.55f,2.1f,z * 0.81f), 0.025f, 0.025f, _pale);
            }
            if (balloon)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Cloth(P(side * 0.52f,deck,-0.72f),P(side * 0.52f,deck,0.72f),
                        P(side * 0.59f,deck + 0.43f,0.81f),P(side * 0.59f,deck + 0.43f,-0.81f),P(side * 0.04f,0,0),_body);
                    Cloth(P(side * 0.25f,2.3f,-1.26f),P(side * 0.93f,2.34f,-1.85f),
                        P(side * 0.83f,2.37f,-2.14f),P(side * 0.2f,2.36f,-1.64f),P(0,0.06f,0),_accent);
                }
                Oval(P(0,2.32f,0), P(1.14f,1.02f,1.67f), _accent);
                for (int i = -1; i <= 1; i++)
                    Oval(P(0,2.33f,i * 0.64f), P(i == 0 ? 1.16f : 1.07f,i == 0 ? 1.04f : 0.96f,0.085f), _pale);
            }
            else if (water)
            {
                Spar(P(0,deck,0), P(0,2.12f,0), 0.065f, 0.035f, _frame);
                Spar(P(0,0.73f,-0.06f),P(0,0.85f,0.98f),0.041f,0.028f,_frame);
                Cloth(P(0,0.78f,0.05f),P(0,2.04f,0.04f),P(0,1.82f,0.44f),P(0,0.86f,0.94f),P(0.21f,0,0),_pale);
                Cloth(P(0,1.84f,0.04f),P(0,2.04f,0.04f),P(0,1.82f,0.44f),P(0,1.62f,0.55f),P(0.08f,0,0),_accent);
            }
            else if (f == FieldForm.SteamCar)
            {
                Spar(P(0,0.97f,-0.9f), P(0,0.97f,0.75f), 0.4f, 0.4f, _body, 12);
                Spar(P(0,1.2f,0.46f), P(0,1.85f,0.46f), 0.13f, 0.19f, _frame);
                for (int side = -1; side <= 1; side += 2)
                    Spar(P(side * 0.4f,0.84f,-0.8f),P(side * 0.4f,1.7f,-0.8f),0.052f,0.052f,_frame);
                Oval(P(0,1.71f,-0.67f),P(0.57f,0.12f,0.48f),_accent);
                for (int band = -1; band <= 1; band++)
                    Spar(P(0,0.97f,band * 0.32f),P(0,0.97f,band * 0.32f + 0.044f),0.417f,0.417f,_accent,12);
                Oval(P(0,0.99f,0.81f), P(0.18f,0.18f,0.1f), _pale);
            }
            else
            {
                int bundles = 2 + _spec.Duty / 340;
                for (int i = 0; i < bundles; i++)
                    Oval(P(0,deck + 0.26f,-0.72f + 1.38f * i / (bundles - 1)), P(0.46f,0.34f,0.31f), i % 2 == 0 ? _body : _pale);
                if (f == FieldForm.MineCart)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        for (int row = 0; row < 3; row++)
                            Spar(P(s * (0.48f + row * 0.055f),deck + row * 0.13f,-0.9f),
                                P(s * (0.48f + row * 0.055f),deck + row * 0.13f,0.9f),0.105f,0.105f,_body,4);
                        for (int bolt = -1; bolt <= 1; bolt++)
                            Oval(P(s * 0.66f,deck + 0.26f,bolt * 0.77f),P(0.045f,0.055f,0.055f),_pale);
                    }
                }
                if (_spec.Damage < 410 && f == FieldForm.Wagon)
                {
                    for (int s = -1; s <= 1; s += 2)
                        Spar(P(0,deck,s * 0.71f), P(0,1.56f,s * 0.71f), 0.065f, 0.05f, _frame);
                    Oval(P(0,1.54f,0), P(0.76f,0.25f,1.03f), _accent);
                    for (int s = -1; s <= 1; s += 2)
                        for (int tassel = -2; tassel <= 2; tassel++)
                        {
                            float z = tassel * 0.23f;
                            Spar(P(s * 0.65f,1.52f,z), P(s * 0.66f,1.28f,z),0.065f,0.016f,_pale,4);
                        }
                }
            }
            if (f == FieldForm.Wagon || f == FieldForm.Sled) DraftBeast(deck);
        }

        internal enum DraftKind { Ox, Horse, Reindeer, Lizard }

        internal static DraftKind DraftFor(FieldSpecimen spec) => spec.Region switch
        {
            ArchRegion.Ice => DraftKind.Reindeer,
            ArchRegion.Lava => DraftKind.Lizard,
            _ => spec.Style < 500 ? DraftKind.Ox : DraftKind.Horse,
        };

        // REQ: docs/design/settlement-structures.md#交通工具 — the beast is part of the vehicle mesh so it moves, blocks
        // and breaks with it; no separate destructible. Build and coat reuse the vehicle's own Style/Age draws.
        private void DraftBeast(float deck)
        {
            DraftKind kind = DraftFor(_spec);
            Color coat = kind switch
            {
                DraftKind.Reindeer => new Color(0.46f,0.36f,0.27f),
                DraftKind.Lizard => new Color(0.42f,0.16f,0.11f),
                DraftKind.Horse => new Color(0.52f,0.29f,0.15f),
                _ => new Color(0.36f,0.24f,0.17f),
            };
            coat = Color.Lerp(coat, new Color(0.78f,0.72f,0.62f), _spec.Age * 0.00035f);
            Color hoof = Color.Lerp(coat, Ink, 0.6f);
            bool lizard = kind == DraftKind.Lizard;
            float build = 0.9f + _spec.Style * 0.0002f;
            float legY = lizard ? 0.32f : kind == DraftKind.Horse ? 0.78f : 0.66f;
            float3 torso = P(0, legY + (lizard ? 0.05f : 0.22f), 2.45f);
            float3 bodyR = lizard ? P(0.34f,0.2f,0.78f) * build : P(0.36f,0.36f,0.72f) * build;
            Oval(torso, bodyR, coat);
            for (int s = -1; s <= 1; s += 2)
            {
                Spar(P(s * 0.5f,deck + 0.1f,1.15f), P(s * 0.42f,torso.y,torso.z - 0.25f), 0.04f, 0.035f, _frame);
                for (int end = -1; end <= 1; end += 2)
                {
                    float3 hip = torso + P(s * bodyR.x * 0.62f, -bodyR.y * 0.4f, end * bodyR.z * 0.6f);
                    float3 foot = lizard ? hip + P(s * 0.28f,-legY + 0.04f,0) : P(hip.x,0.06f,hip.z);
                    Spar(hip, foot, lizard ? 0.075f : 0.085f, 0.055f, coat);
                    Oval(foot, P(0.07f,0.05f,0.08f), hoof);
                }
            }
            float3 neck = torso + P(0, lizard ? 0.02f : bodyR.y * 0.6f, bodyR.z * 0.85f);
            float3 head = neck + (lizard ? P(0,0.02f,0.32f) : kind == DraftKind.Horse ? P(0,0.42f,0.3f) : P(0,0.12f,0.34f));
            Spar(neck, head, lizard ? 0.17f : 0.2f, 0.13f, coat);
            Oval(head + P(0,0,0.1f), lizard ? P(0.15f,0.1f,0.24f) : P(0.15f,0.16f,0.26f), coat);
            for (int s = -1; s <= 1; s += 2)
            {
                Oval(head + P(s * 0.13f,0.05f,0.12f), P(0.03f,0.035f,0.03f), Ink);
                if (kind == DraftKind.Ox)
                    Spar(head + P(s * 0.1f,0.12f,0f), head + P(s * 0.34f,0.24f,0.08f), 0.045f, 0.015f, _pale);
                if (kind == DraftKind.Reindeer)
                {
                    float3 beam = head + P(s * 0.27f,0.52f,-0.12f);
                    Spar(head + P(s * 0.08f,0.14f,0f), beam, 0.035f, 0.022f, _pale);
                    Spar(math.lerp(head, beam, 0.6f) + P(0,0.1f,0), beam + P(s * 0.05f,0.12f,0.2f), 0.02f, 0.01f, _pale);
                }
                if (kind == DraftKind.Horse)
                    Spar(head + P(s * 0.06f,0.14f,-0.08f), head + P(s * 0.07f,0.3f,-0.1f), 0.035f, 0.01f, coat);
            }
            float3 tailRoot = torso - P(0, lizard ? 0f : -bodyR.y * 0.2f, bodyR.z * 0.95f);
            Spar(tailRoot, tailRoot + (lizard ? P(0,-0.12f,-0.85f) : P(0,-0.45f,-0.12f)),
                lizard ? 0.13f : 0.045f, 0.015f, kind == DraftKind.Horse ? Color.Lerp(coat, Ink, 0.45f) : coat);
            Oval(torso + P(0,bodyR.y * 0.75f,-0.1f), P(0.22f,0.06f,0.18f), _accent);
        }

        private void Feathers(int side, bool bird, float3 shoulder)
        {
            float span = bird ? 0.82f : 0.23f;
            for (int i = 0; i < 4; i++)
                Spar(shoulder + P(0,0,0.18f - i * 0.09f),
                    shoulder + P(side * (span - i * 0.1f),0.15f,-0.22f - i * 0.085f),0.095f,0.02f,_accent,4);
        }

        // REQ: planet-world.md#自然生物 — the landform-bound animals. Same unit as the others: ground at y = 0, facing +z,
        // about a metre long before FieldSpecimen.Scale.
        private void Wildlife(FieldForm f)
        {
            switch (f)
            {
                case FieldForm.Deer:
                case FieldForm.Goat:
                {
                    bool deer = f == FieldForm.Deer;
                    float hip = deer ? 0.62f : 0.42f;
                    Oval(P(0,hip + 0.1f,0), deer ? P(0.2f,0.21f,0.46f) : P(0.22f,0.22f,0.4f), _body);
                    for (int s = -1; s <= 1; s += 2)
                        for (int e = -1; e <= 1; e += 2)
                        {
                            float3 top = P(s * 0.11f,hip,e * 0.3f);
                            Spar(top,top + P(0,-hip * 0.55f,e * 0.03f),0.06f,0.04f,_body);
                            Spar(top + P(0,-hip * 0.55f,e * 0.03f),P(s * 0.11f,0.02f,e * 0.3f),0.04f,0.03f,_frame);
                        }
                    float3 neck = P(0,hip + (deer ? 0.55f : 0.38f),0.5f);
                    Spar(P(0,hip + 0.15f,0.32f),neck,0.11f,0.07f,_body);
                    float3 head = neck + P(0,0.03f,0.1f);
                    Oval(head, P(0.09f,0.1f,0.17f), _body);
                    Oval(head + P(0,-0.03f,0.14f), P(0.06f,0.06f,0.06f), _pale);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Oval(head + P(s * 0.08f,0.03f,0.04f), P(0.022f,0.026f,0.022f), Ink);
                        Oval(head + P(s * 0.11f,0.09f,-0.06f), P(0.05f,0.025f,0.07f), _body, 0.4f);
                        if (deer && _spec.Style > 400)
                        {
                            // Antlers: a beam with two tines, wider on older stags.
                            float spread = 0.12f + _spec.Age * 0.0001f;
                            float3 b0 = head + P(s * 0.05f,0.08f,-0.04f), b1 = b0 + P(s * spread,0.22f,-0.08f);
                            Spar(b0,b1,0.022f,0.012f,_pale,4);
                            Spar(math.lerp(b0,b1,0.45f),math.lerp(b0,b1,0.45f) + P(0,0.12f,0.07f),0.015f,0.008f,_pale,4);
                            Spar(b1,b1 + P(s * 0.03f,0.1f,0.05f),0.012f,0.006f,_pale,4);
                        }
                        else if (!deer)
                        {
                            // Curved horns swept back over the neck.
                            float3 h0 = head + P(s * 0.05f,0.09f,-0.04f);
                            Spar(h0,h0 + P(s * 0.06f,0.1f,-0.12f),0.025f,0.015f,_frame,4);
                            Spar(h0 + P(s * 0.06f,0.1f,-0.12f),h0 + P(s * 0.08f,0.02f,-0.24f),0.015f,0.006f,_frame,4);
                        }
                    }
                    if (!deer) Spar(head + P(0,-0.07f,0.08f),head + P(0,-0.2f,0.05f),0.03f,0.01f,_pale,4);
                    Oval(P(0,hip + 0.14f,-0.46f), P(0.05f,0.07f,0.05f), deer ? _pale : _body);
                    break;
                }
                case FieldForm.Fox:
                {
                    Oval(P(0,0.3f,0), P(0.14f,0.14f,0.32f), _body);
                    for (int s = -1; s <= 1; s += 2)
                        for (int e = -1; e <= 1; e += 2)
                            Spar(P(s * 0.08f,0.25f,e * 0.2f),P(s * 0.08f,0.01f,e * 0.22f),0.04f,0.025f,e > 0 ? _frame : _body);
                    float3 head = P(0,0.42f,0.36f);
                    Oval(head, P(0.11f,0.1f,0.12f), _body);
                    Spar(head + P(0,-0.02f,0.08f),head + P(0,-0.04f,0.24f),0.06f,0.015f,_pale);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Spar(head + P(s * 0.06f,0.07f,-0.02f),head + P(s * 0.08f,0.2f,-0.03f),0.045f,0.008f,_body,4);
                        Oval(head + P(s * 0.065f,0.03f,0.08f), P(0.018f,0.02f,0.018f), Ink);
                    }
                    // The brush: thick, pale-tipped.
                    Oval(P(0,0.28f,-0.48f), P(0.09f,0.09f,0.24f), _body, -0.5f);
                    Oval(P(0,0.18f,-0.68f), P(0.06f,0.06f,0.07f), _pale);
                    Oval(P(0,0.24f,0.14f), P(0.09f,0.08f,0.1f), _pale);
                    break;
                }
                case FieldForm.Lizard:
                {
                    Oval(P(0,0.08f,0), P(0.1f,0.06f,0.28f), _body);
                    float3 head = P(0,0.09f,0.33f);
                    Oval(head, P(0.07f,0.05f,0.1f), _body);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Oval(head + P(s * 0.05f,0.03f,0.02f), P(0.016f,0.018f,0.016f), Ink);
                        for (int e = -1; e <= 1; e += 2)
                        {
                            float3 hip = P(s * 0.07f,0.07f,e * 0.17f);
                            Spar(hip,hip + P(s * 0.1f,0.02f,e * 0.04f),0.03f,0.022f,_body,4);
                            Spar(hip + P(s * 0.1f,0.02f,e * 0.04f),hip + P(s * 0.13f,-0.07f,e * 0.08f),0.022f,0.012f,_frame,4);
                        }
                    }
                    Spar(P(0,0.07f,-0.24f),P(0.08f,0.03f,-0.62f),0.06f,0.008f,_body);
                    // Banded back: a row of small scutes in the accent colour.
                    for (int k = 0; k < 4; k++) Oval(P(0,0.13f,0.18f - k * 0.12f), P(0.05f,0.015f,0.04f), _accent);
                    break;
                }
                case FieldForm.Frog:
                {
                    Oval(P(0,0.1f,0), P(0.14f,0.09f,0.15f), _body);
                    Oval(P(0,0.07f,0.04f), P(0.12f,0.06f,0.12f), _pale);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Oval(P(s * 0.07f,0.18f,0.08f), P(0.04f,0.04f,0.04f), _body);
                        Oval(P(s * 0.075f,0.2f,0.1f), P(0.022f,0.022f,0.022f), Ink);
                        // Folded hind legs, the frog's silhouette.
                        Oval(P(s * 0.15f,0.06f,-0.08f), P(0.06f,0.05f,0.11f), _body);
                        Spar(P(s * 0.17f,0.02f,-0.02f),P(s * 0.22f,0.01f,0.1f),0.03f,0.02f,_frame,4);
                        Spar(P(s * 0.08f,0.06f,0.1f),P(s * 0.1f,0.01f,0.17f),0.022f,0.015f,_body,4);
                    }
                    for (int k = 0; k < 3; k++) Oval(P(-0.04f + k * 0.04f,0.17f,-0.03f + k * 0.03f), P(0.025f,0.012f,0.025f), _accent);
                    break;
                }
                default:
                {
                    // Crab: wide shell, three legs a side, two raised claws.
                    Oval(P(0,0.11f,0), P(0.2f,0.07f,0.15f), _body);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        for (int k = -1; k <= 1; k++)
                        {
                            float3 hip = P(s * 0.16f,0.1f,k * 0.07f - 0.03f);
                            float3 knee = hip + P(s * 0.12f,0.07f,k * 0.03f);
                            Spar(hip,knee,0.022f,0.018f,_body,4);
                            Spar(knee,knee + P(s * 0.07f,-0.16f,k * 0.02f),0.018f,0.008f,_frame,4);
                        }
                        float3 arm = P(s * 0.12f,0.14f,0.14f);
                        Spar(P(s * 0.08f,0.1f,0.1f),arm,0.03f,0.025f,_body,4);
                        Oval(arm + P(s * 0.02f,0.04f,0.06f), P(0.05f,0.04f,0.07f), _body);
                        Spar(P(s * 0.04f,0.15f,0.14f),P(s * 0.05f,0.21f,0.15f),0.012f,0.01f,_frame,4);
                        Oval(P(s * 0.05f,0.22f,0.15f), P(0.018f,0.018f,0.018f), Ink);
                    }
                    break;
                }
            }
        }

        private void Animal(bool rigged)
        {
            FieldForm f = _spec.Form;
            if (f >= FieldForm.Deer)
            {
                Wildlife(f);
                return;
            }

            bool bird = f == FieldForm.Heron, fish = f == FieldForm.Fish, bug = f == FieldForm.Beetle;
            float y = bug ? 0.2f : 0.43f;
            Oval(P(0,y,0), bug ? P(0.32f,0.18f,0.48f) : P(0.27f,0.28f,0.53f), _body);
            float3 head = P(0, y + (bird ? 0.37f : bug ? 0.01f : 0.16f), 0.46f);
            if (bird) Spar(P(0,y,0.18f),head,0.14f,0.09f,_pale);
            Oval(head, P(0.18f,0.18f,0.22f), _body);
            for (int s = -1; s <= 1; s += 2)
            {
                Oval(head + P(s * 0.153f,0.055f,0.105f), P(0.036f,0.043f,0.038f), Ink);
                if (bird || fish)
                {
                    if (!rigged || fish) Feathers(s, bird, P(s * 0.16f,y,0));
                }
                else if (bug)
                {
                    Oval(P(s * 0.13f,y + 0.11f,-0.06f),P(0.16f,0.14f,0.38f),_accent);
                    for (int i = -1; i <= 1; i++)
                    {
                        float3 knee = P(s * 0.35f,0.2f,i * 0.27f);
                        Spar(P(s * 0.12f,y,i * 0.17f),knee,0.041f,0.03f,_frame);
                        Spar(knee,knee + P(s * 0.13f,-0.18f,-0.06f),0.032f,0.017f,_frame);
                    }
                }
                else
                {
                    float ear = 0.28f + _spec.Style * 0.00017f;
                    if (s < 0) ear *= 1f - _spec.Damage * 0.00045f;
                    Oval(head + P(s * 0.088f,ear * 0.8f,-0.02f),P(0.073f,ear,0.094f),_body,-0.45f);
                    Oval(head + P(s * 0.088f,ear * 0.9f,0.042f),P(0.043f,ear * 0.68f,0.035f),_accent,-0.45f);
                    Oval(P(s * 0.19f,0.25f,-0.25f),P(0.17f,0.24f,0.26f),_body);
                    Oval(P(s * 0.18f,0.08f,0.27f),P(0.087f,0.09f,0.15f),_pale);
                }
            }
            if (bug)
                for (int side = -1; side <= 1; side += 2)
                    Spar(head + P(side * 0.07f,0.1f,0.12f),head + P(side * 0.19f,0.24f,0.27f),0.027f,0.014f,_frame);
            if (bird)
                for (int side = -1; side <= 1; side += 2)
                {
                    Spar(head + P(side * 0.05f,0.15f,-0.08f),head + P(side * 0.09f,0.12f,-0.39f),0.04f,0.012f,_accent);
                    Spar(P(side * 0.07f,y - 0.14f,-0.19f),P(side * 0.1f,y - 0.25f,-0.7f),0.039f,0.018f,_frame);
                }
            if (bird) Spar(head + P(0,0,0.09f),head + P(0,-0.04f,0.51f),0.079f,0.014f,_accent);
            if (fish)
                for (int s = -1; s <= 1; s += 2)
                    Spar(P(0,y,-0.4f),P(0,y + s * 0.2f,-0.75f),0.12f,0.035f,_accent,4);
            if (f == FieldForm.Hare) Oval(P(0,0.46f,-0.52f),P(0.13f,0.14f,0.14f),_pale);
        }
    }
}
