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

        internal void Apply(Vector3 position, PlanetFrame frame, in WeatherState weather, bool volcanic)
        {
            _root.SetPositionAndRotation(position, Quaternion.LookRotation(frame.North, frame.Up));
            // REQ: weather.md#生效 — particle density follows effective strength, not a single dominant label.
            float rain = weather.RainPermille / 1000f, snow = weather.SnowPermille / 1000f, sand = weather.SandPermille / 1000f;
            float thunder = weather.ThunderPermille / 1000f, wind = weather.WindPermille / 1000f, ash = weather.AshPermille / 1000f;
            Set(_rain, rain * (250f + 260f * thunder), 8f, 0.16f + 0.26f * math.max(thunder, wind), -1f);
            Set(_snow, snow * 84f, 5f, 0.62f, -1f);
            Set(_dust, sand * 80f, 3f, 1f, -0.24f);
            // Volcanic ground keeps its light ash fall in any weather; an ash-fall phenomenon thickens it.
            Set(_ash, (volcanic ? 34f : 0f) + ash * 120f, 5f, 0.36f, -1f);
            Set(_embers, volcanic ? 13f : 0f, 0.6f, 0.24f, 1f);
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
