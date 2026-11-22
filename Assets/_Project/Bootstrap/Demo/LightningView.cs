using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Presentation-only thunderstorm lightning around the player: cloud flashes, forked cloud-to-ground bolts with
    /// restrikes, a sky/ambient flash and thunder shake that arrives after the sound's travel time.
    /// </summary>
    /// <remarks>
    /// Strikes here do no damage and touch no rule state (Q-W47 decides whether thunderstorms get rule-layer
    /// strikes), so a local System.Random is enough; clients may see different bolts.
    /// </remarks>
    internal sealed class LightningView
    {
        private const int Branches = 4;
        private const float SpeedOfSoundMps = 343f;
        private const float CloudBaseM = 150f;
        private const float NearestM = 45f;
        private const float FarthestM = 420f;
        private const float ShakeReachM = 160f;
        private const float BranchSeconds = 0.12f;
        private const float PulseDecayS = 0.05f;
        private const float AmbientGroundM = 160f;
        private const int RingPoints = 40;
        private const int MaxRings = 6;
        private static readonly float WarningRadiusM = Cosmimera.WorldGen.LightningSchedule.WarningRadiusM;

        private readonly System.Random _random;
        private readonly Material _material;
        private readonly Transform _root;
        private readonly LineRenderer _trunk;
        private readonly LineRenderer[] _branches = new LineRenderer[Branches];
        private readonly LineRenderer[] _rings = new LineRenderer[MaxRings];
        private readonly List<Vector3> _points = new List<Vector3>(80);
        private readonly List<Vector3> _scratch = new List<Vector3>(80);
        private readonly float[] _pulseAt = new float[4];
        private readonly float[] _pulseAmp = new float[4];
        private readonly float3[] _ringCenters = new float3[MaxRings];
        private readonly bool[] _ringHasPos = new bool[MaxRings];
        private readonly List<(float at, float meters)> _thunder = new List<(float, float)>();

        private float _clock;
        private float _nextStrike = -1f;
        private float _strikeAt = -100f;
        private int _pulses;
        private bool _ground;
        private float _strength;

        /// <summary>Current flash, 0..1, for the sky and ambient light.</summary>
        /// <remarks>Pre: none. Post: 0 between strikes. Side effects: none.</remarks>
        internal float Flash { get; private set; }

        /// <summary>Creates the bolt renderers under <paramref name="parent"/>.</summary>
        /// <remarks>Pre: parent non-null. Post: nothing drawn until Tick sees thunder. Side effects: GameObjects, one material.</remarks>
        internal LightningView(Transform parent, int seed)
        {
            Contract.Requires(parent != null, "parent required");
            _random = new System.Random(seed);
            _material = new Material(Resources.Load<Shader>("LightningBolt"));
            _root = new GameObject("Lightning").transform;
            _root.SetParent(parent, false);
            _trunk = Line("Bolt");
            for (int i = 0; i < Branches; i++) _branches[i] = Line("Branch " + i);
            for (int i = 0; i < MaxRings; i++)
            {
                _rings[i] = Line("Warning ring " + i);
                _rings[i].loop = true;
                _rings[i].positionCount = RingPoints;
            }
        }

        /// <summary>Destroys the material (GameObjects go with the parent).</summary>
        /// <remarks>Pre: none. Post: material released. Side effects: destroys an asset.</remarks>
        internal void Dispose() => Object.Destroy(_material);

        private LineRenderer Line(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        /// <summary>Advances the storm by <paramref name="dt"/> and redraws the current bolt.</summary>
        /// <remarks>
        /// Pre: dt ≥ 0; thunder in [0, 1] (effective strength, weather.md#生效); groundAltitude maps a unit direction to
        ///      metres above the planet radius.
        /// Post: Flash in [0, 1]; returns the camera shake (metres) of thunder whose sound reached the player this tick.
        /// Side effects: LineRenderer positions and visibility; internal timers.
        /// </remarks>
        internal float Tick(float dt, in PlanetConfig planet, float3 playerDirection, float thunder, Func<float3, float> groundAltitude)
        {
            Contract.Requires(dt >= 0f && thunder >= 0f && thunder <= 1f && groundAltitude != null, "invalid lightning tick");
            _clock += dt;
            if (thunder > 0.02f)
            {
                // Mean gap shrinks from ~16 s at the threshold to ~2 s in a full storm; exponential gaps read as natural.
                float mean = math.lerp(16f, 2.2f, thunder);
                if (_nextStrike < 0f) _nextStrike = _clock + Gap(mean) * 0.5f;
                if (_clock >= _nextStrike)
                {
                    Strike(planet, playerDirection, thunder, groundAltitude);
                    _nextStrike = _clock + Gap(mean);
                }
            }
            else
            {
                _nextStrike = -1f;
            }

            Draw();
            float shake = 0f;
            for (int i = _thunder.Count - 1; i >= 0; i--)
            {
                if (_clock < _thunder[i].at) continue;
                shake = math.max(shake, _thunder[i].meters);
                _thunder.RemoveAt(i);
            }

            return shake;
        }

        private float Gap(float mean) => -mean * math.log(math.max(1e-4f, 1f - (float)_random.NextDouble()));

        private float Next() => (float)_random.NextDouble();

        private void Strike(in PlanetConfig planet, float3 playerDirection, float thunder, Func<float3, float> groundAltitude)
        {
            // sqrt spreads strikes evenly over the ring's area, so close strikes stay rare.
            float distance = NearestM + (FarthestM - NearestM) * math.sqrt(Next());
            float azimuth = Next() * 2f * math.PI;
            PlanetFrame frame = PlanetFrame.FromUp(playerDirection);
            float3 tangent = frame.North * math.cos(azimuth) + frame.East * math.sin(azimuth);
            float arc = distance / planet.Radius;
            float3 direction = math.normalize(playerDirection * math.cos(arc) + tangent * math.sin(arc));
            // Stronger storms ground more often; cloud-to-cloud flashes dominate weak ones (as in real storms). Close
            // ground strikes belong to the rule layer (weather.md#落雷), which warns first; ambient ones stay far.
            bool ground = distance >= AmbientGroundM && Next() < 0.25f + 0.35f * thunder;
            Fire(planet, direction, tangent, distance, ground, groundAltitude);
        }

        /// <summary>Draws a rule-layer strike's bolt onto <paramref name="direction"/> (the warning circle's centre).</summary>
        /// <remarks>
        /// Pre: direction and playerDirection unit; groundAltitude as in Tick.
        /// Post: a ground bolt with full flash is drawn there; its thunder shake is queued by distance.
        /// Side effects: LineRenderer positions; internal timers.
        /// </remarks>
        internal void StrikeAt(in PlanetConfig planet, float3 direction, float3 playerDirection, Func<float3, float> groundAltitude)
        {
            Contract.Requires(groundAltitude != null, "groundAltitude required");
            float distance = math.distance(direction, playerDirection) * planet.Radius;
            float3 tangent = direction - playerDirection * math.dot(direction, playerDirection);
            tangent = math.lengthsq(tangent) > 1e-10f ? math.normalize(tangent) : PlanetFrame.FromUp(playerDirection).North;
            Fire(planet, direction, tangent, distance, true, groundAltitude);
        }

        /// <summary>Shows a warning ring for each pending rule-layer strike.</summary>
        /// <remarks>
        /// Pre: directions unit; progress values in [0, 1] (1 = about to land); count ≤ the pool size is drawn.
        /// Post: rings beyond count are hidden. Side effects: LineRenderer positions and colours.
        /// </remarks>
        internal void ShowWarnings(in PlanetConfig planet, IReadOnlyList<float3> directions, IReadOnlyList<float> progress,
            Func<float3, float> groundAltitude)
        {
            Contract.Requires(directions != null && progress != null && groundAltitude != null, "inputs required");
            for (int i = 0; i < _rings.Length; i++)
            {
                LineRenderer ring = _rings[i];
                if (i >= directions.Count)
                {
                    ring.enabled = false;
                    _ringHasPos[i] = false;
                    continue;
                }

                float3 center = directions[i];
                if (!_ringHasPos[i] || math.distancesq(_ringCenters[i], center) > 1e-12f)
                {
                    _ringCenters[i] = center;
                    _ringHasPos[i] = true;
                    PlanetFrame frame = PlanetFrame.FromUp(center);
                    for (int k = 0; k < RingPoints; k++)
                    {
                        float a = k * (2f * math.PI / RingPoints);
                        float3 tangent = frame.East * math.cos(a) + frame.North * math.sin(a);
                        float3 d = math.normalize(center + tangent * (WarningRadiusM / planet.Radius));
                        ring.SetPosition(k, planet.Center + d * (planet.Radius + groundAltitude(d) + 0.12f));
                    }
                }

                // Brightens and pulses faster as the strike nears, so the last half second reads as "move now".
                float t = math.saturate(progress[i]);
                float pulse = 0.55f + 0.45f * math.sin(_clock * math.lerp(8f, 30f, t));
                var color = new Color(1f, 0.86f, 0.35f, math.lerp(0.25f, 0.9f, t) * pulse);
                ring.startColor = color;
                ring.endColor = color;
                ring.widthMultiplier = math.lerp(0.12f, 0.24f, t);
                ring.enabled = true;
            }
        }

        private void Fire(in PlanetConfig planet, float3 direction, float3 tangent, float distance, bool ground,
            Func<float3, float> groundAltitude)
        {
            _ground = ground;
            _strength = (_ground ? 1f : 0.55f) * math.lerp(1f, 0.35f, math.saturate((distance - NearestM) / (FarthestM - NearestM)));
            _strikeAt = _clock;
            _pulses = 1 + _random.Next(0, 4);
            _pulseAt[0] = 0f;
            _pulseAmp[0] = 1f;
            for (int i = 1; i < _pulses; i++)
            {
                _pulseAt[i] = _pulseAt[i - 1] + 0.05f + 0.1f * Next();
                _pulseAmp[i] = 0.45f + 0.5f * Next();
            }

            if (_ground)
            {
                float3 foot = planet.Center + direction * (planet.Radius + groundAltitude(direction));
                float3 lean = math.normalize(tangent * (Next() - 0.5f) + math.cross(direction, tangent) * (Next() - 0.5f) + 1e-3f);
                float3 top = foot + direction * CloudBaseM + lean * (CloudBaseM * 0.35f * Next());
                BuildBolt(top, foot, 6, 0.12f, _points);
                SetLine(_trunk, _points, 0.9f + distance * 0.006f);
                for (int i = 0; i < Branches; i++)
                {
                    int from = _random.Next(1, (int)(_points.Count * 0.7f));
                    float3 start = _points[from];
                    float length = math.distance(top, foot) * (0.18f + 0.22f * Next());
                    float3 side = math.normalize(math.cross(direction, tangent) * (Next() - 0.5f) + tangent * (Next() - 0.5f) + 1e-3f);
                    float3 end = start - direction * length * 0.7f + side * length * 0.7f;
                    BuildBolt(start, end, 4, 0.16f, _scratch);
                    SetLine(_branches[i], _scratch, 0.35f + distance * 0.0025f);
                }

                if (distance < ShakeReachM)
                {
                    // REQ: Q-W52 ② — the rumble arrives with the sound, not the light (343 m/s).
                    _thunder.Add((_clock + distance / SpeedOfSoundMps, 0.16f * (1f - distance / ShakeReachM)));
                }
            }
        }

        private void BuildBolt(float3 from, float3 to, int levels, float roughness, List<Vector3> output)
        {
            output.Clear();
            output.Add(from);
            output.Add(to);
            float3 axis = math.normalize(to - from);
            float3 u = math.normalize(math.cross(axis, math.abs(axis.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f)));
            float3 v = math.cross(axis, u);
            float amplitude = math.distance(from, to) * roughness;
            for (int level = 0; level < levels; level++)
            {
                for (int i = output.Count - 1; i > 0; i--)
                {
                    float3 mid = ((float3)output[i - 1] + (float3)output[i]) * 0.5f;
                    mid += (u * (Next() - 0.5f) + v * (Next() - 0.5f)) * 2f * amplitude;
                    output.Insert(i, mid);
                }

                amplitude *= 0.52f;
            }
        }

        private static void SetLine(LineRenderer line, List<Vector3> points, float width)
        {
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.55f));
            line.widthMultiplier = width;
        }

        private void Draw()
        {
            float t = _clock - _strikeAt;
            float envelope = 0f;
            for (int i = 0; i < _pulses; i++)
            {
                if (t >= _pulseAt[i]) envelope = math.max(envelope, _pulseAmp[i] * math.exp(-(t - _pulseAt[i]) / PulseDecayS));
            }

            Flash = math.saturate(envelope * _strength);
            bool bolt = _ground && envelope > 0.08f;
            Paint(_trunk, bolt, envelope);
            for (int i = 0; i < Branches; i++) Paint(_branches[i], bolt && t < BranchSeconds, envelope * 0.8f);
        }

        private static void Paint(LineRenderer line, bool on, float alpha)
        {
            line.enabled = on;
            if (!on) return;
            var color = new Color(0.62f, 0.72f, 1f, math.saturate(alpha));
            line.startColor = color;
            line.endColor = color;
        }
    }
}
