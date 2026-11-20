using Cosmimera.Core.Contracts;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// The wind sea (P1-13, planet-world.md#海浪): four Gerstner swells travelling down the local wind, shared by the
    /// water shader (displacement, normals, whitecaps) and the CPU (crest spray, rock bursts), so spray leaves the
    /// crest the player sees. Presentation only: nothing in the rules reads wave height.
    /// </summary>
    /// <remarks>
    /// Why phases live on the CPU: a plane wave's phase k·dot(d, P) with |P| ≈ planet radius jumps by k·R·Δθ when the
    /// wind turns by Δθ, so the sea would lurch every time the heading eased. Each step re-anchors the phase at the
    /// player's sea point (continuous there, exact when nothing turned), which makes a turning wind rotate the field
    /// about the player instead.
    /// </remarks>
    internal sealed class SeaWaves
    {
        internal const int Count = 4;
        private const float Gravity = 9.81f;
        // Swell spread around the wind: a real wind sea is a fan, not one sine; offsets keep crests from lining up.
        private static readonly float[] Spread = { 0f, 0.38f, -0.54f, 0.82f };
        private static readonly float[] LengthShare = { 1f, 0.62f, 0.41f, 0.27f };
        private static readonly float[] HeightShare = { 0.52f, 0.26f, 0.14f, 0.08f };

        private readonly float3[] _direction = new float3[Count];
        private readonly float[] _k = new float[Count];
        private readonly double[] _phase = new double[Count];
        private readonly float[] _amplitude = new float[Count];
        private float3 _anchor;
        private float3 _up;
        private float3 _wind;
        private double _drift;
        private float _steepness;
        private float _sea;
        private bool _started;

        private static readonly int DirId = Shader.PropertyToID("_CosmiWaveDir");
        private static readonly int WaveId = Shader.PropertyToID("_CosmiWave");
        private static readonly int AnchorId = Shader.PropertyToID("_CosmiWaveAnchor");
        private static readonly int SeaId = Shader.PropertyToID("_CosmiSea");
        private readonly Vector4[] _dirs = new Vector4[Count];
        private readonly Vector4[] _waves = new Vector4[Count];

        /// <summary>Effective sea wind 0..1 of the last step (whitecaps start at 0.35).</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        internal float Sea => _sea;

        /// <summary>Significant wave height (m) for an effective wind: grows with wind² as wave energy does.</summary>
        /// <remarks>Pre: wind01 in [0, 1]. Post: in [0.06, 1.36]. Side effects: none.</remarks>
        internal static float HeightM(float wind01) => 0.06f + 1.3f * wind01 * wind01;

        /// <summary>Wavelength (m) of the longest swell; fully developed seas lengthen with wind² too.</summary>
        /// <remarks>Pre: wind01 in [0, 1]. Post: in [5, 29]. Side effects: none.</remarks>
        internal static float PeakLengthM(float wind01) => 5f + 24f * wind01 * wind01;

        /// <summary>Advances the sea by <paramref name="dt"/> under the wind at <paramref name="seaPoint"/>.</summary>
        /// <remarks>
        /// Pre: up unit; downwind unit and tangent to up; wind01, gust in [0, 1]; dt ≥ 0.
        /// Post: phases continuous at seaPoint; swells travel downwind at deep-water speed √(g/k).
        /// Side effects: internal state only.
        /// </remarks>
        internal void Step(float3 seaPoint, float3 up, float3 downwind, float wind01, float gust, float dt)
        {
            Contract.Requires(dt >= 0f && wind01 >= 0f && wind01 <= 1f && gust >= 0f && gust <= 1f, "dt >= 0; wind, gust in [0,1]");
            // Re-anchor with the old directions first, so the phase at the new anchor equals what it was a moment ago.
            if (_started)
            {
                float3 shift = seaPoint - _anchor;
                for (int i = 0; i < Count; i++) _phase[i] += _k[i] * math.dot(shift, _direction[i]);
                _drift -= math.dot(shift, _wind);
            }

            _started = true;
            _anchor = seaPoint;
            _up = up;
            _wind = downwind;
            _sea = wind01;
            float3 across = math.cross(up, downwind);
            float peak = PeakLengthM(wind01);
            float height = HeightM(wind01);
            // Gusts sharpen crests (planet-world.md#海浪); Σ Q·k·A stays < 1 so the Gerstner surface never loops.
            _steepness = math.lerp(0.45f, 0.85f, gust) * math.saturate(0.3f + wind01);
            for (int i = 0; i < Count; i++)
            {
                _direction[i] = downwind * math.cos(Spread[i]) + across * math.sin(Spread[i]);
                _k[i] = 2f * math.PI / (peak * LengthShare[i]);
                _amplitude[i] = 0.5f * height * HeightShare[i];
                _phase[i] = (_phase[i] - math.sqrt(Gravity * _k[i]) * dt) % (2.0 * math.PI_DBL);
            }

            _drift = (_drift + dt * (0.6f + 4f * wind01)) % 4096.0;
        }

        /// <summary>Height (m) of the sea surface above sea level at <paramref name="position"/>, as the shader draws it.</summary>
        /// <remarks>Pre: Step called once. Post: |result| ≤ Σ amplitudes. Side effects: none.</remarks>
        internal float HeightAt(float3 position) => Crest(position, out _);

        /// <summary>Like <see cref="HeightAt"/>, also returning how near the dominant swell is to its crest (−1..1).</summary>
        /// <remarks>Pre: Step called once. Post: crest01 = sin of the longest swell's phase. Side effects: none.</remarks>
        internal float Crest(float3 position, out float crest01)
        {
            float3 p = position - _anchor;
            float h = 0f;
            crest01 = 0f;
            for (int i = 0; i < Count; i++)
            {
                float s = math.sin(_k[i] * math.dot(p, _direction[i]) + (float)_phase[i]);
                h += _amplitude[i] * s;
                if (i == 0) crest01 = s;
            }

            return h;
        }

        /// <summary>Total of the four amplitudes (m): the highest a crest can rise.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        internal float MaxAmplitudeM
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < Count; i++) sum += _amplitude[i];
                return sum;
            }
        }

        /// <summary>Publishes the sea to the water and terrain shaders.</summary>
        /// <remarks>
        /// Pre: Step called once. Post: _CosmiWaveDir/_CosmiWave/_CosmiWaveAnchor/_CosmiSea globals set.
        /// Side effects: global shader state.
        /// </remarks>
        internal void Publish(float gust)
        {
            for (int i = 0; i < Count; i++)
            {
                float3 d = _direction[i];
                _dirs[i] = new Vector4(d.x, d.y, d.z, _k[i]);
                // x phase, y amplitude, z Gerstner Q (per swell, so the sum stays below 1).
                float q = _steepness / (_k[i] * math.max(1e-4f, _amplitude[i]) * Count);
                _waves[i] = new Vector4((float)_phase[i], _amplitude[i], math.min(q, 1f), 0f);
            }

            Shader.SetGlobalVectorArray(DirId, _dirs);
            Shader.SetGlobalVectorArray(WaveId, _waves);
            Shader.SetGlobalVector(AnchorId, new Vector4(_anchor.x, _anchor.y, _anchor.z, (float)_drift));
            Shader.SetGlobalVector(SeaId, new Vector4(_sea, gust, HeightM(_sea), MaxAmplitudeM));
        }

        /// <summary>Clears the globals so a later scene without a sea draws flat water.</summary>
        /// <remarks>Pre: none. Post: _CosmiSea is zero. Side effects: global shader state.</remarks>
        internal static void Clear()
        {
            Shader.SetGlobalVector(SeaId, Vector4.zero);
            Shader.SetGlobalVectorArray(WaveId, new Vector4[Count]);
        }
    }
}
