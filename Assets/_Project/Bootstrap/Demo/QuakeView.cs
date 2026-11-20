using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Presentation of rule-layer quakes (P3-22, planet-world.md#地震噴發): foreshock dust and shake, glowing cracks,
    /// lava fountains and smoke through the eruption, lava that spreads beside each crack and cools to black rock,
    /// and volcanic bombs arcing onto their warning circles.
    /// </summary>
    /// <remarks>
    /// Reads Encounter.Quakes / PendingBombs; never writes rule state. The terrain is not deformed: cracks and rock
    /// are ribbons laid on the ground. Black rock stays until the player leaves the quake's region.
    /// </remarks>
    internal sealed class QuakeView
    {
        private const int RibbonPoints = 28;
        private const int RingPoints = 36;
        private const int MaxBombs = 16;
        private const float BombArcM = 12f;

        private sealed class Scar
        {
            public int Region;
            public long Key;
            public Quake Quake;
            public readonly List<GameObject> Objects = new List<GameObject>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<ParticleSystem> Jets = new List<ParticleSystem>();
            public readonly List<ParticleSystem> Smoke = new List<ParticleSystem>();
            public readonly List<Light> Glows = new List<Light>();
            public bool Opened;
        }

        private readonly Transform _root;
        private readonly Material _lavaTemplate;
        private readonly Material _fireMote;
        private readonly Material _smokeMote;
        private readonly Material _ringMat;
        private readonly Material _bombMat;
        private readonly List<Scar> _scars = new List<Scar>();
        private readonly ParticleSystem _dust;
        private readonly ParticleSystem _trail;
        private readonly LineRenderer[] _rings = new LineRenderer[MaxBombs];
        private readonly Transform[] _balls = new Transform[MaxBombs];
        private readonly Vector3[] _ribbon = new Vector3[RibbonPoints * 2];
        private float _clock;

        /// <summary>Number of quakes with something drawn (for tests).</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        internal int ScarCount => _scars.Count;

        /// <summary>Creates the pools under <paramref name="parent"/>.</summary>
        /// <remarks>Pre: parent non-null. Post: nothing drawn. Side effects: GameObjects and materials.</remarks>
        internal QuakeView(Transform parent)
        {
            Contract.Requires(parent != null, "parent required");
            _root = new GameObject("Quakes").transform;
            _root.SetParent(parent, false);
            _lavaTemplate = new Material(Resources.Load<Shader>("LavaFlow"));
            _fireMote = new Material(Resources.Load<Shader>("FireMote"));
            _smokeMote = new Material(Resources.Load<Shader>("WeatherMote"));
            _ringMat = new Material(Resources.Load<Shader>("LightningBolt"));
            _bombMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(1f, 0.55f, 0.15f) };
            _dust = Emitter("Foreshock dust", _root, _smokeMote, 300, 2.4f, new Color(0.55f, 0.48f, 0.4f, 0.5f), 0.4f, 1.4f);
            _trail = Emitter("Bomb trails", _root, _smokeMote, 400, 2.2f, new Color(0.18f, 0.15f, 0.14f, 0.7f), 0.5f, 1.2f);
            for (int i = 0; i < MaxBombs; i++)
            {
                var go = new GameObject("Bomb ring " + i);
                go.transform.SetParent(_root, false);
                LineRenderer ring = go.AddComponent<LineRenderer>();
                ring.sharedMaterial = _ringMat;
                ring.useWorldSpace = true;
                ring.loop = true;
                ring.positionCount = RingPoints;
                ring.alignment = LineAlignment.View;
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.enabled = false;
                _rings[i] = ring;
                GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(ball.GetComponent<Collider>());
                ball.name = "Bomb " + i;
                ball.transform.SetParent(_root, false);
                ball.transform.localScale = Vector3.one * 0.9f;
                ball.GetComponent<Renderer>().sharedMaterial = _bombMat;
                ball.SetActive(false);
                _balls[i] = ball.transform;
            }
        }

        /// <summary>Releases materials (GameObjects go with the parent).</summary>
        /// <remarks>Pre: none. Post: materials destroyed. Side effects: destroys assets.</remarks>
        internal void Dispose()
        {
            foreach (Scar s in _scars) Clear(s);
            _scars.Clear();
            Object.Destroy(_lavaTemplate);
            Object.Destroy(_fireMote);
            Object.Destroy(_smokeMote);
            Object.Destroy(_ringMat);
            Object.Destroy(_bombMat);
        }

        /// <summary>Draws the quakes and bombs for this frame and returns the camera shake they cause.</summary>
        /// <remarks>
        /// Pre: quakes and bombs from the same Encounter tick; groundAltitude maps a unit direction to metres above the
        ///      radius; dt ≥ 0; darkness in [0, 1] (1 = night); playerRegion is the player's region now.
        /// Post: one scar per quake the player has seen start; scars of other regions are removed once their lava has
        ///       cooled. Returns shake in metres (0 when no quake is near).
        /// Side effects: GameObjects, meshes, particles, lights.
        /// </remarks>
        internal float Sync(IReadOnlyList<Quake> quakes, IReadOnlyList<VolcanicBomb> bombs, in PlanetConfig planet,
            Func<float3, float> groundAltitude, long nowMs, float3 player, int playerRegion, float darkness, float dt)
        {
            Contract.Requires(quakes != null && bombs != null && groundAltitude != null && dt >= 0f, "inputs required; dt >= 0");
            _clock += dt;
            float3 up = planet.ToDirection(player);
            float shake = 0f;
            foreach (Quake q in quakes)
            {
                if (nowMs < q.StartMs) continue;
                long key = q.Region * 1_000_000_000L + q.Window;
                Scar scar = Find(key);
                if (scar == null)
                {
                    scar = new Scar { Region = q.Region, Key = key, Quake = q };
                    _scars.Add(scar);
                }

                scar.Quake = q;
            }

            for (int i = _scars.Count - 1; i >= 0; i--)
            {
                Scar s = _scars[i];
                Quake q = s.Quake;
                if (s.Region != playerRegion && nowMs > q.CoolEndMs)
                {
                    Clear(s);
                    _scars.RemoveAt(i);
                    continue;
                }

                float near = math.saturate(1f - NearestM(q, up, planet.Radius) / 160f);
                if (nowMs < q.CrackMs)
                {
                    // Foreshock: builds from a tremor to a heave just before the ground splits.
                    float build = (nowMs - q.StartMs) / (float)QuakeSchedule.ForeshockMs;
                    shake = math.max(shake, near * math.lerp(0.015f, 0.09f, build * build));
                    Dust(planet, player, up, near * (0.3f + build), dt);
                    continue;
                }

                if (!s.Opened)
                {
                    Open(s, planet, groundAltitude);
                    if (nowMs - q.CrackMs < 600) shake = math.max(shake, near * 0.22f);
                }

                bool erupting = nowMs < q.EruptEndMs;
                if (erupting) shake = math.max(shake, near * (nowMs - q.CrackMs < 600 ? 0.22f : 0.035f));
                Animate(s, nowMs, erupting, darkness, planet, up);
            }

            DrawBombs(bombs, planet, groundAltitude, nowMs);
            return shake;
        }

        private Scar Find(long key)
        {
            foreach (Scar s in _scars) if (s.Key == key) return s;
            return null;
        }

        private static float NearestM(Quake q, float3 up, float radius)
        {
            float best = float.MaxValue;
            foreach (QuakeCrack c in q.Cracks) best = math.min(best, Encounter.CrackDistanceM(c, up, radius));
            return best;
        }

        private void Open(Scar s, in PlanetConfig planet, Func<float3, float> groundAltitude)
        {
            s.Opened = true;
            int index = 0;
            foreach (QuakeCrack crack in s.Quake.Cracks)
            {
                // Presentation-only jitter: a stable per-crack seed so the zigzag does not shimmer between frames.
                var random = new System.Random(unchecked((int)(s.Key * 31 + index++)));
                Material lava = new Material(_lavaTemplate);
                lava.SetFloat("_Seed", (float)random.NextDouble() * 100f);
                s.Materials.Add(lava);
                var go = new GameObject("Lava ribbon");
                go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>().sharedMesh = Ribbon(crack, planet, groundAltitude, random);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = lava;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s.Objects.Add(go);

                float3 mid = planet.ToWorld(crack.Center, groundAltitude(crack.Center));
                Quaternion along = Quaternion.LookRotation((Vector3)crack.Along, (Vector3)crack.Center);
                ParticleSystem jet = Emitter("Lava fountain", _root, _fireMote, 500, 1.8f, new Color(1f, 0.6f, 0.15f, 0.95f), 0.4f, 1.3f);
                Place(jet, mid, along, crack.LengthM);
                var jm = jet.main;
                jm.startSpeed = new ParticleSystem.MinMaxCurve(9f, 20f);
                // Streaks, not motes: a fountain of molten rock reads by its arcs.
                var streak = jet.GetComponent<ParticleSystemRenderer>();
                streak.renderMode = ParticleSystemRenderMode.Stretch;
                streak.velocityScale = 0.12f;
                streak.lengthScale = 1f;
                SetForce(jet, -crack.Center * 9.81f);
                var jetEmission = jet.emission;
                jetEmission.enabled = true;
                jetEmission.rateOverTime = 4f * crack.LengthM;
                ParticleSystem smoke = Emitter("Eruption smoke", _root, _smokeMote, 300, 7f, new Color(0.16f, 0.14f, 0.14f, 0.75f), 2f, 5f);
                Place(smoke, mid, along, crack.LengthM);
                var sm = smoke.main;
                sm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                SetForce(smoke, crack.Center * 0.6f);
                var smokeEmission = smoke.emission;
                smokeEmission.enabled = true;
                smokeEmission.rateOverTime = 1.2f * crack.LengthM;
                s.Jets.Add(jet);
                s.Smoke.Add(smoke);
                s.Objects.Add(jet.gameObject);
                s.Objects.Add(smoke.gameObject);

                var light = new GameObject("Lava glow").AddComponent<Light>();
                light.transform.SetParent(_root, false);
                light.transform.position = (Vector3)(mid + crack.Center * 2f);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.45f, 0.15f);
                light.range = 6f + crack.LengthM * 0.6f;
                light.shadows = LightShadows.None;
                s.Glows.Add(light);
                s.Objects.Add(light.gameObject);
            }
        }

        private void Animate(Scar s, long nowMs, bool erupting, float darkness, in PlanetConfig planet, float3 up)
        {
            Quake q = s.Quake;
            float spread = erupting
                ? math.lerp(0.2f, 1f, math.saturate((nowMs - q.CrackMs) / (float)(q.EruptEndMs - q.CrackMs)))
                : 1f;
            float heat = erupting ? 1f : math.saturate(1f - (nowMs - q.EruptEndMs) / (float)QuakeSchedule.LavaCoolMs);
            foreach (Material m in s.Materials)
            {
                m.SetFloat("_Spread", spread);
                m.SetFloat("_Heat", heat);
            }

            foreach (ParticleSystem jet in s.Jets)
            {
                var e = jet.emission;
                e.enabled = erupting;
            }

            foreach (ParticleSystem smoke in s.Smoke)
            {
                // Steam keeps rising off the cooling flow, thinning as it goes black.
                var e = smoke.emission;
                e.rateMultiplier = erupting ? 1f : heat * 0.4f;
            }

            foreach (Light glow in s.Glows)
            {
                // Bright pools only where the scene is dark: by day a toon-shaded point light reads as a hard disc.
                float flicker = 0.85f + 0.15f * math.sin(_clock * 13f + glow.range);
                glow.intensity = heat * (0.4f + 2.6f * darkness) * flicker;
                glow.enabled = heat > 0.02f;
            }
        }

        private Mesh Ribbon(in QuakeCrack crack, in PlanetConfig planet, Func<float3, float> groundAltitude, System.Random random)
        {
            float3 across = math.normalize(math.cross(crack.Center, crack.Along));
            float halfWidth = QuakeSchedule.LavaHalfWidthM;
            float wander = 0f;
            var uvs = new Vector2[RibbonPoints * 2];
            var normals = new Vector3[RibbonPoints * 2];
            for (int i = 0; i < RibbonPoints; i++)
            {
                float t = i / (float)(RibbonPoints - 1);
                float alongM = (t - 0.5f) * crack.LengthM;
                // The fissure zigzags a little either side of the rule-layer line.
                wander = math.clamp(wander + ((float)random.NextDouble() - 0.5f) * 0.8f, -1f, 1f);
                float taper = math.sqrt(math.max(0.05f, 1f - math.pow(math.abs(2f * t - 1f), 3f)));
                for (int side = 0; side < 2; side++)
                {
                    float acrossM = wander + (side == 0 ? -halfWidth : halfWidth) * taper;
                    float3 d = math.normalize(crack.Center + (crack.Along * alongM + across * acrossM) / planet.Radius);
                    _ribbon[i * 2 + side] = planet.ToWorld(d, groundAltitude(d) + 0.12f);
                    normals[i * 2 + side] = d;
                    uvs[i * 2 + side] = new Vector2(t, side);
                }
            }

            var indices = new int[(RibbonPoints - 1) * 6];
            int k = 0;
            for (int i = 0; i < RibbonPoints - 1; i++)
            {
                int a = i * 2;
                indices[k++] = a;
                indices[k++] = a + 2;
                indices[k++] = a + 1;
                indices[k++] = a + 1;
                indices[k++] = a + 2;
                indices[k++] = a + 3;
            }

            var mesh = new Mesh { name = "LavaRibbon" };
            mesh.vertices = _ribbon;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Dust(in PlanetConfig planet, float3 player, float3 up, float amount, float dt)
        {
            if (amount <= 0f) return;
            SetForce(_dust, -up * 2f);
            int count = (int)math.ceil(amount * 60f * dt);
            PlanetFrame frame = PlanetFrame.FromUp(up);
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                float2 o = UnityEngine.Random.insideUnitCircle * 14f;
                emit.position = player + frame.East * o.x + frame.North * o.y + up * UnityEngine.Random.Range(0f, 0.6f);
                emit.velocity = up * UnityEngine.Random.Range(0.8f, 2.4f) + (float3)(UnityEngine.Random.insideUnitSphere * 0.6f);
                _dust.Emit(emit, 1);
            }
        }

        private void DrawBombs(IReadOnlyList<VolcanicBomb> bombs, in PlanetConfig planet, Func<float3, float> groundAltitude, long nowMs)
        {
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < MaxBombs; i++)
            {
                bool on = i < bombs.Count;
                _rings[i].enabled = on;
                _balls[i].gameObject.SetActive(on);
                if (!on) continue;
                VolcanicBomb b = bombs[i];
                float p = math.saturate(1f - (b.LandsAtMs - nowMs) / (float)QuakeSchedule.BombWarningMs);
                float3 from = planet.ToWorld(b.From, groundAltitude(b.From) + 0.5f);
                float3 to = planet.ToWorld(b.Direction, groundAltitude(b.Direction) + 0.3f);
                float3 lift = b.Direction * (4f * p * (1f - p) * BombArcM);
                float3 at = math.lerp(from, to, p) + lift;
                _balls[i].position = at;
                emit.position = at;
                emit.velocity = b.Direction * 0.3f;
                _trail.Emit(emit, 1);

                PlanetFrame frame = PlanetFrame.FromUp(b.Direction);
                for (int k = 0; k < RingPoints; k++)
                {
                    float a = k * (2f * math.PI / RingPoints);
                    float3 d = math.normalize(b.Direction + (frame.East * math.cos(a) + frame.North * math.sin(a)) * (QuakeSchedule.BombRadiusM / planet.Radius));
                    _rings[i].SetPosition(k, planet.ToWorld(d, groundAltitude(d) + 0.15f));
                }

                // Hotter and faster as the bomb comes down, like the lightning rings, but in fire colours.
                float pulse = 0.55f + 0.45f * math.sin(_clock * math.lerp(8f, 30f, p));
                var color = new Color(1f, 0.42f, 0.12f, math.lerp(0.3f, 0.95f, p) * pulse);
                _rings[i].startColor = color;
                _rings[i].endColor = color;
                _rings[i].widthMultiplier = math.lerp(0.14f, 0.26f, p);
            }
        }

        private static void Clear(Scar s)
        {
            foreach (GameObject go in s.Objects)
            {
                MeshFilter f = go != null ? go.GetComponent<MeshFilter>() : null;
                if (f != null) Object.Destroy(f.sharedMesh);
                if (go != null) Object.Destroy(go);
            }

            foreach (Material m in s.Materials) Object.Destroy(m);
            s.Objects.Clear();
            s.Materials.Clear();
        }

        private static void Place(ParticleSystem ps, float3 at, Quaternion rotation, float lengthM)
        {
            ps.transform.position = at;
            ps.transform.rotation = rotation;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // A box emits along its own +z; turned −90° about x that is the transform's up (out of the ground), and
            // its y (now running along the crack) and z (now vertical) are the crack's length and a thin slab.
            shape.rotation = new Vector3(-90f, 0f, 0f);
            shape.scale = new Vector3(0.6f, lengthM * 0.85f, 0.1f);
            var main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * math.PI);
        }

        private static void SetForce(ParticleSystem ps, float3 f)
        {
            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = new ParticleSystem.MinMaxCurve(f.x);
            force.y = new ParticleSystem.MinMaxCurve(f.y);
            force.z = new ParticleSystem.MinMaxCurve(f.z);
        }

        private static ParticleSystem Emitter(string name, Transform parent, Material material, int max, float life, Color color,
            float minSize, float maxSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startColor = color;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.8f));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }
    }
}
