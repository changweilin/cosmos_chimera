using System;
using System.Collections.Generic;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Presentation of the small wind vortices near the player (weather.md#風向): a spinning dust column for each dust
    /// devil, a tall widening funnel of dust and cloud for each tornado, and a loose ring of leaves for each eddy.
    /// Cyclones are drawn by the sky and cloud shaders, not here.
    /// </summary>
    /// <remarks>
    /// Pre: Sync from the main thread once per tick. Post: at most <see cref="Pool"/> columns, nearest first.
    /// Side effects: creates and owns particle systems under its root; Dispose destroys the material.
    /// </remarks>
    internal sealed class VortexView
    {
        internal const int Pool = 4;

        private readonly Transform _root;
        private readonly Material _material;
        private readonly ParticleSystem[] _columns = new ParticleSystem[Pool];
        private readonly VortexKind[] _kinds = new VortexKind[Pool];
        private readonly List<int> _order = new List<int>();
        private readonly float3[] _centers = new float3[WindField.MaxVortices * 4];

        internal VortexView(Transform parent)
        {
            _material = new Material(Resources.Load<Shader>("WeatherMote"));
            _root = new GameObject("Wind vortices").transform;
            _root.SetParent(parent, false);
            for (int i = 0; i < Pool; i++) _columns[i] = Column(i);
        }

        internal void Dispose() => UnityEngine.Object.Destroy(_material);

        /// <summary>Places a column on each nearest dust devil, tornado or eddy within reach.</summary>
        /// <remarks>
        /// Pre: vortices from WeatherService.VorticesAround; ground gives altitude (m) of a direction.
        /// Post: columns beyond the live vortices stop emitting. Side effects: particle transforms and emission.
        /// </remarks>
        internal void Sync(IReadOnlyList<WindVortex> vortices, in PlanetConfig planet, Vector3 player,
            Func<float3, float> ground)
        {
            _order.Clear();
            float3 p = player;
            for (int i = 0; i < vortices.Count && i < _centers.Length; i++)
            {
                if (vortices[i].Kind == VortexKind.Cyclone || vortices[i].PeakPermille < 60) continue;
                _centers[i] = planet.ToWorld(vortices[i].Center, 0f);
                _order.Add(i);
            }

            // Nearest first: insertion sort over a handful of entries, no comparer allocation.
            for (int a = 1; a < _order.Count; a++)
            {
                int key = _order[a];
                float d = math.distancesq(_centers[key], p);
                int b = a - 1;
                while (b >= 0 && math.distancesq(_centers[_order[b]], p) > d)
                {
                    _order[b + 1] = _order[b];
                    b--;
                }

                _order[b + 1] = key;
            }

            for (int slot = 0; slot < Pool; slot++)
            {
                ParticleSystem ps = _columns[slot];
                if (slot >= _order.Count)
                {
                    if (ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    continue;
                }

                WindVortex v = vortices[_order[slot]];
                float3 up = v.Center;
                float3 foot = planet.ToWorld(up, ground(up));
                PlanetFrame frame = PlanetFrame.FromUp(up);
                ps.transform.SetPositionAndRotation(foot, Quaternion.LookRotation(frame.North, frame.Up));
                if (_kinds[slot] != v.Kind || !ps.isEmitting) Shape(ps, v.Kind);
                _kinds[slot] = v.Kind;
                Drive(ps, v);
                if (!ps.isEmitting) ps.Play(true);
            }
        }

        private ParticleSystem Column(int index)
        {
            var go = new GameObject("Vortex " + index);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            // Local space: particles orbit the column's own axis (the planet's up where it stands) and follow it as it drifts.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 420;
            main.startSpeed = 0f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var opacity = new Gradient();
            opacity.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = opacity;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            // Circle emits in its local XY plane; turn it flat onto the ground (local XZ).
            shape.rotation = new Vector3(90f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.5f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _material;
            return ps;
        }

        // Kind-specific look; called when a slot changes kind so the pool can serve any mix.
        private static void Shape(ParticleSystem ps, VortexKind kind)
        {
            var main = ps.main;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            switch (kind)
            {
                case VortexKind.Tornado:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.42f, 0.44f, 0.75f),
                        new Color(0.62f, 0.56f, 0.48f, 0.7f));
                    // Narrow at the ground, wide where it meets the cloud base: the funnel.
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
                    break;
                case VortexKind.DustDevil:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.66f, 0.46f, 0.55f),
                        new Color(0.66f, 0.56f, 0.4f, 0.45f));
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.8f));
                    break;
                default:
                    // Eddy: a few leaves and seeds wheeling low over the ground.
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.56f, 0.24f, 0.9f),
                        new Color(0.7f, 0.56f, 0.26f, 0.9f));
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
                    break;
            }
        }

        private static void Drive(ParticleSystem ps, in WindVortex v)
        {
            float peak = v.PeakPermille / 1000f;
            var shape = ps.shape;
            var vel = ps.velocityOverLifetime;
            var emission = ps.emission;
            // Orbit speed (rad/s) so the core wall turns at a speed that reads at a glance; sign = spin.
            float spin = v.Spin;
            switch (v.Kind)
            {
                case VortexKind.Tornado:
                    shape.radius = v.CoreM * 0.8f;
                    vel.orbitalY = spin * (2.4f + 1.6f * peak);
                    vel.radial = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                    vel.y = new ParticleSystem.MinMaxCurve(6f, 10f);
                    emission.rateOverTime = 70f * peak;
                    break;
                case VortexKind.DustDevil:
                    shape.radius = v.CoreM;
                    vel.orbitalY = spin * (4f + 3f * peak);
                    vel.radial = new ParticleSystem.MinMaxCurve(-0.15f, 0.2f);
                    vel.y = new ParticleSystem.MinMaxCurve(2f, 4.5f);
                    emission.rateOverTime = 55f * peak;
                    break;
                default:
                    shape.radius = v.CoreM * 1.6f;
                    vel.orbitalY = spin * 0.9f;
                    vel.radial = new ParticleSystem.MinMaxCurve(-0.2f, 0.05f);
                    vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
                    emission.rateOverTime = 14f * peak;
                    break;
            }

            // Unused curve modes must match the active one (Unity rejects mixed modes on velocityOverLifetime).
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalX = 0f;
            vel.orbitalZ = 0f;
        }
    }
}
