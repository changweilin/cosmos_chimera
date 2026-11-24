using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Diving visuals (P3-23, planet-world.md 潛水視覺): the splash and screen change when the camera crosses the
    /// surface, a tinted and vignetted view underwater, rising bubbles, drifting motes and slanting light shafts.
    /// Fog, light and the surface seen from below are driven elsewhere (AtmosphereDriver.SetUnderwater, ToonWater).
    /// </summary>
    /// <remarks>Presentation only: nothing here feeds the dive rules (FieldUtilityRules / SphericalCharacter).</remarks>
    internal sealed class UnderwaterView
    {
        private const float WaterIndex = 1.33f;
        private const float BreathIntervalS = 3.4f;

        private static readonly int UnderwaterId = Shader.PropertyToID("_CosmiUnderwater");

        private readonly Transform _root;
        private readonly Material _bubbleMat;
        private readonly Material _moteMat;
        private readonly Material _shaftMat;
        private readonly VolumeProfile _profile;
        private readonly Volume _volume;
        private readonly LensDistortion _lens;
        private readonly ColorAdjustments _grade;
        private readonly ParticleSystem _bubbles;
        private readonly ParticleSystem _motes;
        private readonly ParticleSystem _shafts;
        private readonly ParticleSystem _splash;
        private bool _submerged;
        private bool _started;
        private float _weight;
        private float _pulse;
        private float _breath;
        private float _moteDebt;
        private float _shaftDebt;
        private Vector4 _lastUnderwaterVec;
        private bool _hasUnderwaterVec;
        private float3 _lastForceUp;
        private bool _hasForce;

        /// <summary>Whether the camera was under the surface at the last tick.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        internal bool Submerged => _submerged;

        /// <summary>Screen-effect weight 0..1 (eases in and out over a few frames).</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        internal float Weight => _weight;

        /// <summary>Creates the emitters and the underwater post-process volume under <paramref name="parent"/>.</summary>
        /// <remarks>
        /// Pre: parent non-null. Post: nothing visible until a tick finds the camera under water.
        /// Side effects: GameObjects, three materials, one volume profile.
        /// </remarks>
        internal UnderwaterView(Transform parent)
        {
            Contract.Requires(parent != null, "parent required");
            _root = new GameObject("Underwater").transform;
            _root.SetParent(parent, false);
            _bubbleMat = new Material(Resources.Load<Shader>("Bubble"));
            _moteMat = new Material(Resources.Load<Shader>("WeatherMote"));
            _shaftMat = new Material(Resources.Load<Shader>("LightShaft"));

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _grade = _profile.Add<ColorAdjustments>(true);
            _grade.colorFilter.Override(new Color(0.62f, 0.9f, 1f));
            _grade.saturation.Override(-18f);
            _grade.contrast.Override(6f);
            Vignette vignette = _profile.Add<Vignette>(true);
            vignette.intensity.Override(0.42f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(new Color(0.02f, 0.08f, 0.14f));
            _lens = _profile.Add<LensDistortion>(true);
            _lens.intensity.Override(0f);
            var volumeGo = new GameObject("Underwater volume");
            volumeGo.transform.SetParent(_root, false);
            _volume = volumeGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            // Above the run's own grading volume, so the water tint is not undone by it.
            _volume.priority = 10f;
            _volume.weight = 0f;
            _volume.sharedProfile = _profile;

            _bubbles = Emitter("Bubbles", _bubbleMat, 300, 4f, new Color(0.85f, 0.95f, 1f, 0.8f), 0.05f, 0.16f);
            var wobble = _bubbles.noise;
            wobble.enabled = true;
            wobble.strength = 0.6f;
            wobble.frequency = 1.2f;
            _motes = Emitter("Motes", _moteMat, 400, 9f, new Color(0.8f, 0.9f, 0.85f, 0.35f), 0.03f, 0.08f);
            var drift = _motes.noise;
            drift.enabled = true;
            drift.strength = 0.15f;
            drift.frequency = 0.3f;
            _shafts = Emitter("Light shafts", _shaftMat, 60, 8f, new Color(0.8f, 0.96f, 1f, 0.7f), 1f, 2.2f);
            var stretch = _shafts.GetComponent<ParticleSystemRenderer>();
            stretch.renderMode = ParticleSystemRenderMode.Stretch;
            stretch.lengthScale = 9f;
            stretch.velocityScale = 0f;
            _splash = Emitter("Surface splash", _moteMat, 200, 1.1f, new Color(0.95f, 0.98f, 1f, 0.85f), 0.08f, 0.3f);
        }

        /// <summary>Releases the materials and the volume profile.</summary>
        /// <remarks>Pre: none. Post: assets destroyed; the underwater global cleared. Side effects: destroys assets.</remarks>
        internal void Dispose()
        {
            Object.Destroy(_bubbleMat);
            Object.Destroy(_moteMat);
            Object.Destroy(_shaftMat);
            Object.Destroy(_profile);
            Shader.SetGlobalVector(UnderwaterId, Vector4.zero);
        }

        /// <summary>Draws this frame's diving effects.</summary>
        /// <remarks>
        /// Pre: camera non-null; dt ≥ 0; daylight in [0, 1]; toSun unit; seaRadiusM = planet radius + sea level, or 0
        ///      where there is no water sea (indoors, lava): then nothing is drawn and caustics are off.
        /// Post: Submerged == cameraDepthM > 0 (with a sea); crossing the surface either way splashes and pulses the
        ///       lens once; shafts only by day and shallower than 30 m.
        /// Side effects: particle state, the volume's weight, the _CosmiUnderwater global.
        /// </remarks>
        internal void Tick(float dt, in PlanetConfig planet, Transform camera, float3 player, float cameraDepthM,
            float seaRadiusM, float3 toSun, float daylight)
        {
            Contract.Requires(camera != null && dt >= 0f && daylight >= 0f && daylight <= 1f, "camera required; dt >= 0; daylight in [0,1]");
            bool submerged = seaRadiusM > 0f && cameraDepthM > 0f;
            float3 eye = camera.position;
            float3 up = planet.ToDirection(eye);
            if (_started && submerged != _submerged && seaRadiusM > 0f) Cross(planet, camera, up, submerged);
            _started = true;
            _submerged = submerged;
            var underwaterVec = new Vector4(submerged ? 1f : 0f, math.max(0f, cameraDepthM), seaRadiusM, 0f);
            if (!_hasUnderwaterVec || underwaterVec != _lastUnderwaterVec)
            {
                _lastUnderwaterVec = underwaterVec;
                _hasUnderwaterVec = true;
                Shader.SetGlobalVector(UnderwaterId, underwaterVec);
            }

            if (!submerged && _weight <= 1e-5f && _pulse <= 0f)
            {
                if (_weight != 0f)
                {
                    _weight = 0f;
                    _volume.weight = 0f;
                }

                if (_lens.intensity.value != 0f)
                {
                    _lens.intensity.value = 0f;
                }

                bool anyParticles = _bubbles.particleCount > 0 || _motes.particleCount > 0 || _splash.particleCount > 0;
                if (anyParticles && (!_hasForce || math.distancesq(up, _lastForceUp) > 1e-10f))
                {
                    SyncForces(up);
                }

                return;
            }

            _weight = math.lerp(_weight, submerged ? 1f : 0f, math.saturate(dt * 9f));
            _pulse = math.max(0f, _pulse - dt * 2.2f);
            _volume.weight = _weight;
            // A wobble as water closes over the lens (or runs off it), easing out.
            _lens.intensity.value = -0.45f * _pulse * math.sin(_pulse * 18f);
            float deep = math.saturate(cameraDepthM / 35f);
            _grade.colorFilter.value = Color.Lerp(new Color(0.66f, 0.92f, 1f), new Color(0.42f, 0.66f, 0.95f), deep);
            if (!_hasForce || math.distancesq(up, _lastForceUp) > 1e-10f)
            {
                SyncForces(up);
            }

            if (!submerged) return;

            _breath -= dt;
            if (_breath <= 0f)
            {
                _breath = BreathIntervalS * UnityEngine.Random.Range(0.8f, 1.2f);
                Emit(_bubbles, player + up * 1.6f, up * 0.6f, 6 + UnityEngine.Random.Range(0, 6), 0.15f, 0.6f);
            }

            _moteDebt += dt * 40f;
            var emit = new ParticleSystem.EmitParams();
            while (_moteDebt >= 1f)
            {
                _moteDebt -= 1f;
                emit.position = eye + (float3)(UnityEngine.Random.insideUnitSphere * 10f);
                emit.velocity = up * UnityEngine.Random.Range(-0.03f, 0.05f);
                _motes.Emit(emit, 1);
            }

            float3 down = Refracted(-toSun, up);
            float shaftRate = math.lengthsq(down) > 0f ? 5f * math.saturate((daylight - 0.15f) / 0.5f) * (1f - math.saturate(cameraDepthM / 30f)) : 0f;
            _shaftDebt += dt * shaftRate;
            float surfaceAbove = math.max(0f, cameraDepthM) - 0.6f;
            while (_shaftDebt >= 1f)
            {
                _shaftDebt -= 1f;
                float2 spot = UnityEngine.Random.insideUnitCircle * 12f;
                PlanetFrame frame = PlanetFrame.FromUp(up);
                // Centred a few metres down the ray, so the beam spans from just under the surface into the depth.
                emit.position = eye + up * surfaceAbove + frame.East * spot.x + frame.North * spot.y + down * 6f;
                // Shafts hang along the refracted sun ray; the tiny speed only orients the stretched billboard.
                emit.velocity = down * 0.02f;
                _shafts.Emit(emit, 1);
            }
        }

        // Splash on the surface above the camera, and a cloud of bubbles across the view on the way in.
        private void Cross(in PlanetConfig planet, Transform camera, float3 up, bool goingDown)
        {
            if (!_hasForce || math.distancesq(up, _lastForceUp) > 1e-10f)
            {
                SyncForces(up);
            }

            _pulse = 1f;
            float3 eye = camera.position;
            float3 surface = eye + up * (goingDown ? 0.4f : -0.2f);
            Emit(_splash, surface, up * 3.2f, 50, 1.6f, 2.5f);
            if (!goingDown) return;
            float3 ahead = (float3)camera.forward * 1.2f;
            Emit(_bubbles, eye + ahead - up * 0.3f, up * 1.2f, 70, 0.7f, 1.2f);
        }

        private void SyncForces(float3 up)
        {
            SetForce(_bubbles, up * 1.6f);
            SetForce(_motes, float3.zero);
            SetForce(_splash, -up * 9.81f);
            _lastForceUp = up;
            _hasForce = true;
        }

        private static void Emit(ParticleSystem ps, float3 at, float3 velocity, int count, float spread, float jitter)
        {
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                emit.position = at + (float3)(UnityEngine.Random.insideUnitSphere * spread);
                emit.velocity = velocity * UnityEngine.Random.Range(0.6f, 1.2f) + (float3)(UnityEngine.Random.insideUnitSphere * jitter);
                ps.Emit(emit, 1);
            }
        }

        // Snell refraction of a downward ray through a flat surface; zero when the sun is below the horizon.
        private static float3 Refracted(float3 incident, float3 up)
        {
            float cosIn = -math.dot(up, incident);
            if (cosIn <= 0.02f) return float3.zero;
            float eta = 1f / WaterIndex;
            float k = 1f - eta * eta * (1f - cosIn * cosIn);
            return math.normalize(eta * incident + (eta * cosIn - math.sqrt(k)) * up);
        }

        private static void SetForce(ParticleSystem ps, float3 f)
        {
            var force = ps.forceOverLifetime;
            force.x = new ParticleSystem.MinMaxCurve(f.x);
            force.y = new ParticleSystem.MinMaxCurve(f.y);
            force.z = new ParticleSystem.MinMaxCurve(f.z);
        }

        private ParticleSystem Emitter(string name, Material material, int max, float life, Color color, float minSize, float maxSize)
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
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }
    }
}
