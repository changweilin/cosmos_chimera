using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Presentation of burning objects: per fire type flames, smoke and sparks leaning downwind, a flickering light,
    /// kerosene fireballs and scorched ground left behind. REQ: docs/design/destructibles.md#延燒 (外觀欄).
    /// </summary>
    /// <remarks>Never touches rule state; reads the burning set the host's DestructibleTable already holds.</remarks>
    internal sealed class FireView
    {
        /// <summary>One burning object to draw.</summary>
        internal struct Burning
        {
            public long Id;
            public float3 Center;
            public float RadiusM;
            public FireKind Kind;
        }

        private sealed class Slot
        {
            public GameObject Root;
            public ParticleSystem Flames, Smoke, Sparks;
            public Light Glow;
            public long Id = -1;
            public FireKind Kind;
            public float Seed;
        }

        private struct Scar
        {
            public GameObject Go;
            public float Until;
        }

        private const int MaxFires = 20;
        private const int MaxLights = 6;
        private const float ScarSeconds = 240f;

        private readonly Transform _root;
        private readonly Material _flame;
        private readonly Material _smoke;
        private readonly Material _scorch;
        private readonly Mesh _disc;
        private readonly Slot[] _slots = new Slot[MaxFires];
        private readonly List<Scar> _scars = new List<Scar>();
        private readonly ParticleSystem _blast;
        private readonly ParticleSystem _blastSmoke;
        private float _clock;
        private float _darkness;

        /// <summary>Creates the pools under <paramref name="parent"/>.</summary>
        /// <remarks>Pre: parent non-null. Post: nothing drawn. Side effects: GameObjects, materials, one mesh.</remarks>
        internal FireView(Transform parent)
        {
            Contract.Requires(parent != null, "parent required");
            _root = new GameObject("Fires").transform;
            _root.SetParent(parent, false);
            _flame = new Material(Resources.Load<Shader>("FireMote"));
            _smoke = new Material(Resources.Load<Shader>("WeatherMote"));
            _scorch = new Material(Resources.Load<Shader>("WeatherMote"));
            _disc = DiscMesh();
            for (int i = 0; i < MaxFires; i++) _slots[i] = NewSlot(i);
            _blast = Burst("Blast fireball", _flame, 60, new Color(1f, 0.85f, 0.45f), new Color(1f, 0.25f, 0.05f), 2.4f, 9f, 0.7f);
            _blastSmoke = Burst("Blast smoke", _smoke, 30, new Color(0.12f, 0.1f, 0.1f, 0.85f), new Color(0.2f, 0.18f, 0.17f, 0.7f),
                3.2f, 3.5f, 5f);
        }

        /// <summary>Releases materials and the disc mesh.</summary>
        /// <remarks>Pre: none. Post: assets destroyed. Side effects: destroys assets.</remarks>
        internal void Dispose()
        {
            Object.Destroy(_flame);
            Object.Destroy(_smoke);
            Object.Destroy(_scorch);
            Object.Destroy(_disc);
            if (_char != null) Object.Destroy(_char);
        }

        /// <summary>Draws the burning objects nearest the player.</summary>
        /// <remarks>
        /// Pre: burning sorted nearest first (only the first MaxFires are drawn); downwind unit tangent; wind and
        ///      darkness in [0, 1] (darkness 1 = night).
        /// Post: one slot per drawn fire, kept on the same slot while it burns so its particles do not restart.
        /// Side effects: particle and light state.
        /// </remarks>
        internal void Sync(IReadOnlyList<Burning> burning, in PlanetConfig planet, float3 downwind, float wind, float darkness, float dt)
        {
            Contract.Requires(burning != null, "burning required");
            _clock += dt;
            _darkness = math.saturate(darkness);
            int count = math.min(burning.Count, MaxFires);
            foreach (Slot s in _slots)
            {
                bool keep = false;
                for (int i = 0; i < count; i++) keep |= burning[i].Id == s.Id;
                if (!keep && s.Id >= 0) Release(s);
            }

            for (int i = 0; i < count; i++)
            {
                Burning b = burning[i];
                Slot s = Find(b.Id) ?? Claim(b);
                if (s == null) continue;
                Place(s, b, planet, downwind, wind, i < MaxLights);
            }

            for (int i = _scars.Count - 1; i >= 0; i--)
            {
                if (_clock < _scars[i].Until) continue;
                Object.Destroy(_scars[i].Go);
                _scars.RemoveAt(i);
            }
        }

        /// <summary>Plays a kerosene blast: a fireball and a column of black smoke.</summary>
        /// <remarks>Pre: up unit. Post: one burst emitted. Side effects: particles.</remarks>
        internal void Blast(float3 center, float3 up)
        {
            _blast.transform.SetPositionAndRotation(center, Quaternion.LookRotation((Vector3)up));
            _blast.Emit(60);
            _blastSmoke.transform.SetPositionAndRotation(center + up * 1.5f, Quaternion.LookRotation((Vector3)up));
            _blastSmoke.Emit(30);
        }

        /// <summary>Leaves scorched ground (and a charred trunk for a tree) where an object burnt down.</summary>
        /// <remarks>Pre: up unit; radiusM &gt; 0. Post: the scar fades after ScarSeconds. Side effects: GameObjects.</remarks>
        internal void Scorch(float3 ground, float3 up, float radiusM, FireKind kind)
        {
            Contract.Requires(radiusM > 0f, "radiusM must be > 0");
            var go = new GameObject("Scorch");
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(ground + up * 0.04f, Quaternion.FromToRotation(Vector3.up, (Vector3)up));
            float r = radiusM * (kind == FireKind.Grass ? 2.2f : 1.6f);
            go.transform.localScale = new Vector3(r, 1f, r);
            go.AddComponent<MeshFilter>().sharedMesh = _disc;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _scorch;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (kind == FireKind.Crown)
            {
                // REQ: destructibles.md#延燒 樹冠火 — a blackened trunk stays standing.
                GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(trunk.GetComponent<Collider>());
                trunk.transform.SetParent(go.transform, false);
                trunk.transform.localScale = new Vector3(0.22f / r, 1.3f, 0.22f / r);
                trunk.transform.localPosition = new Vector3(0f, 1.3f, 0f);
                trunk.GetComponent<MeshRenderer>().sharedMaterial = CharMaterial();
            }

            _scars.Add(new Scar { Go = go, Until = _clock + ScarSeconds });
        }

        private Material _char;

        private Material CharMaterial()
        {
            if (_char != null) return _char;
            _char = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.07f, 0.06f, 0.05f) };
            return _char;
        }

        private Slot Find(long id)
        {
            foreach (Slot s in _slots)
            {
                if (s.Id == id) return s;
            }

            return null;
        }

        private Slot Claim(in Burning b)
        {
            foreach (Slot s in _slots)
            {
                if (s.Id >= 0) continue;
                s.Id = b.Id;
                s.Kind = b.Kind;
                s.Root.SetActive(true);
                Style(s, b);
                s.Flames.Play();
                s.Smoke.Play();
                if (b.Kind == FireKind.Electrical || b.Kind == FireKind.Crown) s.Sparks.Play();
                return s;
            }

            return null;
        }

        private static void Release(Slot s)
        {
            s.Id = -1;
            s.Flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            s.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            s.Sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            s.Glow.enabled = false;
        }

        // REQ: destructibles.md#延燒 火的類型 — each kind has its own shape, height, smoke and colour.
        private static void Style(Slot s, in Burning b)
        {
            float r = math.max(0.4f, b.RadiusM);
            (float width, float height, float rate, Color hot, Color cool, Color smoke, float smokeRate, float smokeSize) look = b.Kind switch
            {
                FireKind.Grass => (r * 2.2f, 0.9f, 55f, new Color(1f, 0.82f, 0.35f), new Color(1f, 0.3f, 0.05f),
                    new Color(0.55f, 0.53f, 0.5f, 0.5f), 10f, 1.6f),
                FireKind.Crown => (r * 1.6f, 2.6f, 70f, new Color(1f, 0.78f, 0.3f), new Color(0.95f, 0.22f, 0.04f),
                    new Color(0.25f, 0.23f, 0.22f, 0.7f), 14f, 2.8f),
                FireKind.Structure => (r * 1.8f, 2.2f, 80f, new Color(1f, 0.75f, 0.32f), new Color(0.9f, 0.2f, 0.05f),
                    new Color(0.13f, 0.12f, 0.12f, 0.85f), 18f, 3.4f),
                FireKind.Electrical => (r * 1.2f, 1.2f, 30f, new Color(0.75f, 0.9f, 1f), new Color(0.3f, 0.45f, 1f),
                    new Color(0.3f, 0.3f, 0.33f, 0.6f), 8f, 2f),
                FireKind.Lava => (r * 1.6f, 1.8f, 60f, new Color(1f, 0.6f, 0.2f), new Color(0.8f, 0.15f, 0.03f),
                    new Color(0.35f, 0.33f, 0.32f, 0.75f), 14f, 3f),
                _ => (r * 1.4f, 1.6f, 50f, new Color(1f, 0.8f, 0.35f), new Color(1f, 0.25f, 0.05f),
                    new Color(0.2f, 0.2f, 0.2f, 0.7f), 12f, 2.4f),
            };

            var main = s.Flames.main;
            main.startSize = new ParticleSystem.MinMaxCurve(look.width * 0.45f, look.width * 0.8f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(look.height * 1.1f, look.height * 1.8f);
            // Flames shrink as they rise and lick upward: a fat base tapering into tongues.
            var shrink = s.Flames.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));
            var shape = s.Flames.shape;
            shape.scale = new Vector3(look.width, look.width, 0.3f);
            var emission = s.Flames.emission;
            emission.rateOverTime = look.rate * 1.8f;
            var fade = s.Flames.colorOverLifetime;
            fade.color = Ramp(look.hot, look.cool, 0.9f);

            var smokeMain = s.Smoke.main;
            smokeMain.startColor = look.smoke;
            smokeMain.startSize = new ParticleSystem.MinMaxCurve(look.smokeSize * 0.6f, look.smokeSize);
            var smokeEmission = s.Smoke.emission;
            smokeEmission.rateOverTime = look.smokeRate;

            var sparkMain = s.Sparks.main;
            sparkMain.startColor = b.Kind == FireKind.Electrical ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 0.6f, 0.15f);
            var sparkEmission = s.Sparks.emission;
            sparkEmission.rateOverTime = b.Kind == FireKind.Electrical ? 0f : 12f;
            // Electrical fires arc in bursts instead of a steady ember stream.
            sparkEmission.SetBursts(b.Kind == FireKind.Electrical
                ? new[] { new ParticleSystem.Burst(0f, 18, 24, 0, 0.45f) }
                : System.Array.Empty<ParticleSystem.Burst>());
            s.Glow.color = b.Kind == FireKind.Electrical ? new Color(0.55f, 0.7f, 1f) : new Color(1f, 0.55f, 0.2f);
            s.Glow.range = 3f + look.width * 1.2f;
        }

        private void Place(Slot s, in Burning b, in PlanetConfig planet, float3 downwind, float wind, bool lit)
        {
            float3 up = planet.ToDirection(b.Center);
            s.Root.transform.SetPositionAndRotation(b.Center, Quaternion.LookRotation((Vector3)up, (Vector3)downwind));
            // Flames and smoke drift downwind; a gale lays the flames over and drags the smoke out flat.
            Vector3 drift = s.Root.transform.InverseTransformDirection((Vector3)downwind);
            var force = s.Flames.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.Local;
            force.x = drift.x * (2f + 10f * wind);
            force.y = drift.y * (2f + 10f * wind);
            force.z = 0f;
            var smokeForce = s.Smoke.forceOverLifetime;
            smokeForce.enabled = true;
            smokeForce.space = ParticleSystemSimulationSpace.Local;
            smokeForce.x = drift.x * (0.8f + 6f * wind);
            smokeForce.y = drift.y * (0.8f + 6f * wind);
            smokeForce.z = 0f;
            // The toon ramp turns a point light into a hard-edged pool; in daylight that reads as a painted disc, so the
            // glow only grows in as the scene darkens.
            s.Glow.enabled = lit && _darkness > 0.05f;
            if (!s.Glow.enabled) return;
            // Two incommensurate sines read as flicker without per-frame random allocation.
            float flicker = 0.75f + 0.15f * math.sin(_clock * 13f + s.Seed) + 0.1f * math.sin(_clock * 31f + s.Seed * 2f);
            s.Glow.intensity = (s.Kind == FireKind.Grass ? 1.6f : 2.6f) * flicker * _darkness;
        }

        private Slot NewSlot(int index)
        {
            var root = new GameObject("Fire " + index);
            root.transform.SetParent(_root, false);
            var slot = new Slot { Root = root, Seed = index * 1.7f };
            // Local +z is the local up (Place looks along up), so emitters fire along +z.
            slot.Flames = Emitter("Flames", root.transform, _flame, 240, 0.8f);
            var flameRenderer = slot.Flames.GetComponent<ParticleSystemRenderer>();
            flameRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            flameRenderer.velocityScale = 0.12f;
            flameRenderer.lengthScale = 1.3f;
            slot.Smoke = Emitter("Smoke", root.transform, _smoke, 120, 6f);
            var smokeMain = slot.Smoke.main;
            smokeMain.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            var grow = slot.Smoke.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
            var smokeFade = slot.Smoke.colorOverLifetime;
            smokeFade.enabled = true;
            smokeFade.color = Ramp(Color.white, Color.white, 0.8f);
            slot.Smoke.transform.localPosition = new Vector3(0f, 0f, 1.5f);
            slot.Sparks = Emitter("Sparks", root.transform, _flame, 80, 1.6f);
            var sparkMain = slot.Sparks.main;
            sparkMain.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            sparkMain.gravityModifier = 0f;
            var sparkNoise = slot.Sparks.noise;
            sparkNoise.enabled = true;
            sparkNoise.strength = 1.5f;
            slot.Glow = root.AddComponent<Light>();
            slot.Glow.type = LightType.Point;
            slot.Glow.shadows = LightShadows.None;
            slot.Glow.enabled = false;
            root.SetActive(false);
            return slot;
        }

        private static ParticleSystem Emitter(string name, Transform parent, Material material, int max, float life)
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
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1f, 1f, 0.3f);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        private ParticleSystem Burst(string name, Material material, int max, Color a, Color b, float size, float speed, float life)
        {
            ParticleSystem ps = Emitter(name, _root, material, max, life);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.8f;
            var fade = ps.colorOverLifetime;
            fade.color = Ramp(Color.white, Color.white, 0.9f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;
            return ps;
        }

        private static ParticleSystem.MinMaxGradient Ramp(Color hot, Color cool, float peak)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(hot, 0f), new GradientColorKey(cool, 0.55f), new GradientColorKey(cool * 0.6f, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.12f), new GradientAlphaKey(peak * 0.6f, 0.6f),
                    new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        private static Mesh DiscMesh()
        {
            const int n = 20;
            var vertices = new Vector3[n + 1];
            var colors = new Color[n + 1];
            var uvs = new Vector2[n + 1];
            var triangles = new int[n * 3];
            vertices[0] = Vector3.zero;
            colors[0] = new Color(0.05f, 0.04f, 0.035f, 0.85f);
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * Mathf.PI / n;
                // A ragged edge: burnt ground never ends in a perfect circle.
                float r = 1f + 0.18f * Mathf.Sin(a * 3f) + 0.1f * Mathf.Sin(a * 7f + 1.3f);
                vertices[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                colors[i + 1] = new Color(0.12f, 0.1f, 0.08f, 0f);
                uvs[i + 1] = new Vector2(0.5f, 0.5f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + (i + 1) % n;
                triangles[i * 3 + 2] = 1 + i;
            }

            var mesh = new Mesh { name = "Scorch disc", vertices = vertices, colors = colors, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
