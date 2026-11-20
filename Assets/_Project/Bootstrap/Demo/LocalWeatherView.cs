using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class LocalWeatherView
    {
        private readonly Material _material;
        private readonly Transform _root;
        private readonly ParticleSystem _rain, _snow, _dust, _ash, _embers;
        private readonly ParticleSystem _leaves, _twigs, _wreckage;
        private readonly Mesh _quad;

        internal LocalWeatherView(Transform parent)
        {
            _material = new Material(Resources.Load<Shader>("WeatherMote"));
            _root = new GameObject("Local weather").transform;
            _root.SetParent(parent, false);
            _rain = Layer("Rain", new Color(0.70f, 0.85f, 1f, 0.54f), 0.035f, 14f, 1.1f, true);
            _snow = Layer("Snow", Color.white, 0.09f, 2.6f, 4f);
            _dust = Layer("Windblown dust", new Color(0.77f, 0.64f, 0.43f, 0.48f), 0.13f, 5f, 3f);
            _ash = Layer("Volcanic ash", new Color(0.49f, 0.46f, 0.44f, 0.85f), 0.07f, 1.8f, 5f);
            _embers = Layer("Rising embers", new Color(1f, 0.42f, 0.08f), 0.055f, 1.7f, 2.8f);
            _quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            // REQ: weather.md#風向 暴風碎片 — leaves, snapped twigs and, near settlements, bits of broken buildings.
            _leaves = Debris("Storm leaves", new Vector3(0.16f, 0.09f, 1f), 0.4f,
                new Color(0.36f, 0.52f, 0.2f), new Color(0.62f, 0.5f, 0.22f));
            _twigs = Debris("Storm twigs", new Vector3(0.42f, 0.045f, 1f), 0.6f,
                new Color(0.36f, 0.26f, 0.16f), new Color(0.5f, 0.4f, 0.28f));
            _wreckage = Debris("Storm wreckage", new Vector3(0.38f, 0.2f, 1f), 0.7f,
                new Color(0.52f, 0.4f, 0.27f), new Color(0.6f, 0.62f, 0.66f));
        }

        private ParticleSystem Debris(string name, Vector3 size, float sizeJitter, Color a, Color b)
        {
            ParticleSystem ps = Layer(name, a, 1f, 10f, 3.2f);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(size.x * (1f - sizeJitter), size.x);
            main.startSizeY = new ParticleSystem.MinMaxCurve(size.y * (1f - sizeJitter), size.y);
            main.startSizeZ = 1f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.maxParticles = 160;
            // Tumbling: flat pieces spin on every axis, which is what makes a gust of leaves read as leaves.
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
            spin.y = new ParticleSystem.MinMaxCurve(-7f, 7f);
            spin.z = new ParticleSystem.MinMaxCurve(-11f, 11f);
            var turbulence = ps.noise;
            turbulence.strength = 2.2f;
            turbulence.frequency = 0.6f;
            var shape = ps.shape;
            shape.scale = new Vector3(26f, 3f, 2f); // wide across the wind, thin along it
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = _quad;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            return ps;
        }

        internal void Dispose() => Object.Destroy(_material);

        private ParticleSystem Layer(string name, Color color, float size, float speed, float life, bool streak = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.72f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.55f, size);
            main.startColor = color;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.8f, speed);
            main.maxParticles = streak ? 720 : 280;
            var opacity = new Gradient();
            opacity.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.8f, 0.76f), new GradientAlphaKey(0f, 1f) });
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = opacity;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 16f, 2f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            if (streak)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.023f;
                renderer.lengthScale = 1f;
            }
            else
            {
                var turbulence = ps.noise;
                turbulence.enabled = true;
                turbulence.strength = 0.5f;
                turbulence.frequency = 0.35f;
            }
            return ps;
        }

        /// <summary>Points every layer at the local weather and wind.</summary>
        /// <remarks>
        /// Pre: windTangent unit and tangent to frame.Up; gust in [0, 1].
        /// Post: emitters sit around position with local +x downwind; rates follow the effective strengths.
        /// Side effects: particle system transforms and emission.
        /// </remarks>
        internal void Apply(Vector3 position, PlanetFrame frame, in WeatherState weather, bool volcanic, float3 windTangent,
            float gust, bool foliage, bool settlement)
        {
            // LookRotation's local +x is up × forward; forward = wind × up makes +x the downwind direction.
            _root.SetPositionAndRotation(position, Quaternion.LookRotation(math.cross(windTangent, frame.Up), frame.Up));
            // REQ: weather.md#生效 — particle density follows effective strength, not a single dominant label.
            float rain = weather.RainPermille / 1000f, snow = weather.SnowPermille / 1000f, sand = weather.SandPermille / 1000f;
            float thunder = weather.ThunderPermille / 1000f, wind = weather.WindPermille / 1000f, ash = weather.AshPermille / 1000f;
            Set(_rain, rain * (250f + 260f * thunder), 8f, 0.16f + 0.26f * math.max(thunder, wind), -1f);
            Set(_snow, snow * 84f, 5f, 0.62f, -1f);
            Set(_dust, sand * 80f, 3f, 1f, -0.24f);
            // Volcanic ground keeps its light ash fall in any weather; an ash-fall phenomenon thickens it.
            Set(_ash, (volcanic ? 34f : 0f) + ash * 120f, 5f, 0.36f, -1f);
            Set(_embers, volcanic ? 13f : 0f, 0.6f, 0.24f, 1f);
            // Debris starts once the wind can tear things loose (> 0.5) and surges with each gust, in step with the
            // grass and trees, which bend on the same rule-layer gust (weather.md#風向).
            float tear = math.saturate((wind - 0.5f) * 2f) * (0.35f + 0.65f * gust);
            float speed = 7f + 13f * wind * (0.6f + 0.4f * gust);
            SetDebris(_leaves, foliage ? tear * 70f : tear * 12f, speed);
            SetDebris(_twigs, foliage ? tear * 22f : 0f, speed * 0.85f);
            SetDebris(_wreckage, settlement ? tear * 26f : 0f, speed * 0.75f);
        }

        private static void SetDebris(ParticleSystem ps, float rate, float speed)
        {
            if (rate < 0.5f)
            {
                if (ps.isEmitting) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            // Emitted from a wall 14 m upwind, low over the ground, flying downwind with a slight lift.
            ps.transform.localPosition = new Vector3(-14f, 1.6f, 0f);
            ps.transform.localRotation = Quaternion.LookRotation(new Vector3(1f, 0.12f, 0f), Vector3.up);
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed);
            var emission = ps.emission;
            emission.rateOverTime = rate;
            if (!ps.isEmitting) ps.Play(false);
        }

        private static void Set(ParticleSystem ps, float rate, float height, float drift, float rise)
        {
            if (rate == 0f)
            {
                if (ps.isEmitting) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            ps.transform.localPosition = new Vector3(-drift * 3f, height, 0f);
            ps.transform.localRotation = Quaternion.LookRotation(new Vector3(drift, rise, 0f), Vector3.forward);
            var emission = ps.emission;
            emission.rateOverTime = rate;
            if (!ps.isEmitting) ps.Play(false);
        }
    }
}
