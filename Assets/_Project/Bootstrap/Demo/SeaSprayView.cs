using System;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Spray off the wind sea (P1-13, planet-world.md#海浪): spindrift torn off the crests and blown downwind once the
    /// wind is strong, and bursts thrown up where a swell hits a steep shore (cliff or rock face at the waterline).
    /// </summary>
    /// <remarks>
    /// Presentation only. Bursts fire when the longest swell's crest reaches each shore spot, from the same
    /// <see cref="SeaWaves"/> the water shader draws, so the spray goes up as the visible wave arrives.
    /// </remarks>
    internal sealed class SeaSprayView
    {
        private const int MaxSpots = 12;
        private const int Bearings = 24;
        private const float SampleStepM = 3f;
        private const float SampleReachM = 42f;
        // A drop of more than this over one 3 m step at the waterline is a face the swell slams into, not a beach.
        private const float CliffDropM = 1.2f;
        private const float RescanMovedM = 8f;
        private const float SpindriftReachM = 35f;

        private readonly Transform _root;
        private readonly Material _mote;
        private readonly ParticleSystem _spindrift;
        private readonly ParticleSystem _burst;
        private readonly float3[] _spots = new float3[MaxSpots];
        private readonly float3[] _seaward = new float3[MaxSpots];
        private readonly float[] _lastCrest = new float[MaxSpots];
        private int _spotCount;
        private float3 _scannedAt;
        private bool _scanned;
        private float _spindriftDebt;
        private float3 _lastBurstForceUp;
        private bool _hasBurstForce;
        private float3 _lastSpindriftForceUp;
        private bool _hasSpindriftForce;

        /// <summary>Shore spots found by the last scan (for tests).</summary>
        /// <remarks>Pre: none. Post: in [0, 12]. Side effects: none.</remarks>
        internal int SpotCount => _spotCount;

        /// <summary>Creates the two emitters under <paramref name="parent"/>.</summary>
        /// <remarks>Pre: parent non-null. Post: nothing emitted. Side effects: GameObjects and one material.</remarks>
        internal SeaSprayView(Transform parent)
        {
            Contract.Requires(parent != null, "parent required");
            _root = new GameObject("Sea spray").transform;
            _root.SetParent(parent, false);
            _mote = new Material(Resources.Load<Shader>("WeatherMote"));
            _spindrift = Emitter("Spindrift", 400, 1.4f, new Color(0.92f, 0.96f, 1f, 0.45f), 0.25f, 0.9f);
            _burst = Emitter("Shore burst", 600, 1.8f, new Color(0.95f, 0.98f, 1f, 0.75f), 0.35f, 1.3f);
        }

        /// <summary>Releases the material.</summary>
        /// <remarks>Pre: none. Post: material destroyed. Side effects: destroys an asset.</remarks>
        internal void Dispose() => Object.Destroy(_mote);

        /// <summary>Emits this frame's spray around <paramref name="player"/>.</summary>
        /// <remarks>
        /// Pre: waves stepped this frame; downwind unit tangent at the player; groundAltitude non-null; dt ≥ 0.
        /// Post: shore spots rescanned once the player has moved 8 m; spindrift only from effective wind 0.45.
        /// Side effects: particle state; up to 312 groundAltitude samples on a rescan.
        /// </remarks>
        internal void Tick(float dt, in PlanetConfig planet, float3 player, float3 downwind, SeaWaves waves,
            Func<float3, float> groundAltitude, float seaLevelM)
        {
            Contract.Requires(waves != null && groundAltitude != null && dt >= 0f, "waves and groundAltitude required; dt >= 0");
            float3 up = planet.ToDirection(player);
            if (!_scanned || math.distance(player, _scannedAt) > RescanMovedM)
            {
                Scan(planet, up, groundAltitude, seaLevelM);
                _scannedAt = player;
                _scanned = true;
            }

            float sea = waves.Sea;
            if ((_spotCount > 0 || _burst.particleCount > 0) &&
                (!_hasBurstForce || math.distancesq(up, _lastBurstForceUp) > 1e-10f))
            {
                // Spray falls toward this planet's centre, not world -Y; over a few dozen metres one local up is exact enough.
                SetForce(_burst, -up * 9.81f);
                _lastBurstForceUp = up;
                _hasBurstForce = true;
            }

            if ((sea > 0.45f || _spindrift.particleCount > 0) &&
                (!_hasSpindriftForce || math.distancesq(up, _lastSpindriftForceUp) > 1e-10f))
            {
                SetForce(_spindrift, -up * 2.5f);
                _lastSpindriftForceUp = up;
                _hasSpindriftForce = true;
            }

            float height = SeaWaves.HeightM(sea);
            for (int i = 0; i < _spotCount; i++)
            {
                waves.Crest(_spots[i], out float crest);
                // Fire once per swell, on the rising edge as the crest reaches the face.
                if (_lastCrest[i] < 0.85f && crest >= 0.85f && sea > 0.08f) Burst(i, up, downwind, sea, height);
                _lastCrest[i] = crest;
            }

            Spindrift(dt, planet, up, downwind, waves, groundAltitude, seaLevelM, sea);
        }

        private void Scan(in PlanetConfig planet, float3 up, Func<float3, float> groundAltitude, float seaLevelM)
        {
            _spotCount = 0;
            float startAlt = groundAltitude(up);
            PlanetFrame frame = PlanetFrame.FromUp(up);
            for (int b = 0; b < Bearings && _spotCount < MaxSpots; b++)
            {
                float angle = 2f * math.PI * b / Bearings;
                float3 heading = frame.East * math.cos(angle) + frame.North * math.sin(angle);
                float previous = startAlt;
                for (float r = SampleStepM; r <= SampleReachM; r += SampleStepM)
                {
                    float3 direction = math.normalize(up * planet.Radius + heading * r);
                    float ground = groundAltitude(direction);
                    // A step from dry ground into water (either way along the ray) that drops steeply.
                    bool crossing = (previous > seaLevelM + 0.2f) != (ground > seaLevelM + 0.2f);
                    if (crossing && math.abs(previous - ground) > CliffDropM)
                    {
                        bool outwardIsSea = ground < previous;
                        float3 seaDir = outwardIsSea ? direction : math.normalize(up * planet.Radius + heading * (r - SampleStepM));
                        _spots[_spotCount] = planet.ToWorld(seaDir, seaLevelM);
                        _seaward[_spotCount] = outwardIsSea ? heading : -heading;
                        _lastCrest[_spotCount] = 1f;
                        _spotCount++;
                        break;
                    }

                    previous = ground;
                }
            }
        }

        private void Burst(int spot, float3 up, float3 downwind, float sea, float height)
        {
            int count = 18 + (int)(70f * sea);
            float3 at = _spots[spot];
            float3 back = -_seaward[spot];
            var emit = new ParticleSystem.EmitParams();
            for (int n = 0; n < count; n++)
            {
                // A sheet thrown up the face and back over it, scattered by the wind.
                float rise = (3f + 8f * height) * UnityEngine.Random.Range(0.5f, 1f);
                float3 side = math.cross(up, back) * UnityEngine.Random.Range(-1.6f, 1.6f);
                float3 v = up * rise + back * UnityEngine.Random.Range(0.3f, 2.2f) + side + downwind * (1f + 5f * sea) * UnityEngine.Random.value;
                emit.position = at + side * 0.8f + up * UnityEngine.Random.Range(0f, 0.4f);
                emit.velocity = v;
                emit.startSize = UnityEngine.Random.Range(0.35f, 1.3f) * (0.6f + height);
                _burst.Emit(emit, 1);
            }
        }

        private void Spindrift(float dt, in PlanetConfig planet, float3 up, float3 downwind, SeaWaves waves,
            Func<float3, float> groundAltitude, float seaLevelM, float sea)
        {
            float rate = 140f * math.smoothstep(0.45f, 0.9f, sea);
            _spindriftDebt += rate * dt;
            if (_spindriftDebt < 1f) return;
            int tries = math.min((int)_spindriftDebt, 12);
            _spindriftDebt -= tries;
            float3 across = math.cross(up, downwind);
            var emit = new ParticleSystem.EmitParams();
            for (int n = 0; n < tries; n++)
            {
                float2 offset = UnityEngine.Random.insideUnitCircle * SpindriftReachM;
                float3 direction = math.normalize(up * planet.Radius + downwind * offset.x + across * offset.y);
                if (groundAltitude(direction) > seaLevelM - 0.5f) continue;
                float3 point = planet.ToWorld(direction, seaLevelM);
                float h = waves.Crest(point, out float crest);
                // Only crests lose their tops to the wind.
                if (crest < 0.6f) continue;
                emit.position = point + up * (h + 0.1f);
                emit.velocity = downwind * (4f + 9f * sea) * UnityEngine.Random.Range(0.6f, 1f) + up * UnityEngine.Random.Range(0.6f, 2.4f);
                emit.startSize = UnityEngine.Random.Range(0.25f, 0.9f);
                _spindrift.Emit(emit, 1);
            }
        }

        private static void SetForce(ParticleSystem ps, float3 f)
        {
            var force = ps.forceOverLifetime;
            force.x = new ParticleSystem.MinMaxCurve(f.x);
            force.y = new ParticleSystem.MinMaxCurve(f.y);
            force.z = new ParticleSystem.MinMaxCurve(f.z);
        }

        private ParticleSystem Emitter(string name, int max, float life, Color color, float minSize, float maxSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startColor = color;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.gravityModifier = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.6f;
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.6f));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _mote;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }
    }
}
