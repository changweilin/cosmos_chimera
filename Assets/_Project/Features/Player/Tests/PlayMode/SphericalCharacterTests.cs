using System.Collections;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Player.Tests
{
    // R=320 (asteroid) since Q-D1 (2026-11-11); SOL Q-D1-R3 asked for the native run at that size too.
    [TestFixture(640f)]
    [TestFixture(320f)]
    public sealed class SphericalCharacterTests
    {
        private readonly float _radius;

        public SphericalCharacterTests(float radius)
        {
            _radius = radius;
        }

        private GameObject _root;
        private PlanetConfig _config;
        private PlanetHeight _height;
        private PlanetTerrain _terrain;
        private SphericalCharacter _character;

        [SetUp]
        public void SetUp()
        {
            _config = new PlanetConfig(_radius, new float3(10f, -20f, 30f));
            _height = PlanetHeight.ForPlanet(_config, 42UL);
            _root = new GameObject("SphericalCharacterTest");
            var planet = new GameObject("Planet");
            planet.transform.SetParent(_root.transform, false);
            _terrain = planet.AddComponent<PlanetTerrain>();

            var body = new GameObject("Character");
            body.transform.SetParent(_root.transform, false);
            var capsule = body.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            body.AddComponent<Rigidbody>();
            _character = body.AddComponent<SphericalCharacter>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_root);
        }

        private float3 Surface(float3 dir, float above) => _config.ToWorld(dir, _height.Sample(dir) + above);

        private IEnumerator Spawn(float3 dir, float3 heading)
        {
            dir = math.normalize(dir);
            Transform t = _character.transform;
            t.SetPositionAndRotation(Surface(dir, 0.2f), quaternion.LookRotationSafe(heading, dir));
            _terrain.Configure(_config, _height, t, null);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!_terrain.IsSettled && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(_terrain.IsSettled, "terrain did not settle");
            // Colliders bake one frame after the meshes; the body must not start falling before they exist.
            yield return new WaitForFixedUpdate();
            t.position = Surface(dir, 0.2f);
            _character.Configure(_config);
        }

        // Acceptance P1-05: walking over a pole and over a cube-face corner with real physics on the generated
        // terrain — no jitter (body up follows local up, stays grounded), no flip (facing never jumps).
        [UnityTest]
        public IEnumerator Walks_over_north_pole_without_flip() => WalkThrough(new float3(0f, 1f, 0f));

        [UnityTest]
        public IEnumerator Walks_over_cube_corner_without_flip() => WalkThrough(new float3(1f, 1f, 1f));

        private IEnumerator WalkThrough(float3 target)
        {
            target = math.normalize(target);
            float3 side = math.normalize(math.cross(target, math.abs(target.z) < 0.9f ? new float3(0f, 0f, 1f) : new float3(1f, 0f, 0f)));
            float3 start = math.mul(quaternion.AxisAngle(side, -15f / _config.Radius), target);
            float3 heading = math.normalize(target - start * math.dot(target, start));
            yield return Spawn(start, heading);

            const int steps = 250; // 5 s at 50 Hz, ~25–40 m: crosses the target point.
            int grounded = 0;
            float maxFacingStep = 0f;
            float minUpDot = 1f;
            float maxAltitudeError = 0f;
            Vector3 prevFacing = _character.Facing;
            Vector3 prevPosition = _character.transform.position;
            float pathLength = 0f;
            for (int i = 0; i < steps; i++)
            {
                _character.SetInput(new Vector2(0f, 1f), _character.Facing, sprint: false, jumpPressed: false);
                yield return new WaitForFixedUpdate();

                float3 p = _character.transform.position;
                pathLength += Vector3.Distance(prevPosition, p);
                prevPosition = p;
                float3 up = PlanetFrame.At(_config, p).Up;
                minUpDot = math.min(minUpDot, math.dot(up, (float3)_character.transform.up));
                Vector3 facing = _character.Facing;
                maxFacingStep = math.max(maxFacingStep, Vector3.Angle(prevFacing, facing));
                prevFacing = facing;
                // Skip the settling drop from the 0.2 m spawn height.
                if (i >= 25)
                {
                    grounded += _character.IsGrounded ? 1 : 0;
                    float altitude = _config.Altitude(p) - _height.Sample(up);
                    maxAltitudeError = math.max(maxAltitudeError, math.abs(altitude));
                }
            }

            float3 end = _config.ToDirection(_character.transform.position);
            float travelled = math.acos(math.clamp(math.dot(end, start), -1f, 1f)) * _config.Radius;
            TestContext.WriteLine($"path {pathLength:F1} m, arc from start {travelled:F1} m, grounded {grounded}/{steps - 25}, max facing step {maxFacingStep:F3}°, " +
                                  $"min up·localUp {minUpDot:F6}, max |altitude error| {maxAltitudeError:F3} m");

            // Arc rather than path: hills shorten horizontal progress (25 m walked over a hill near the pole = 14.6 m arc).
            Assert.Greater(travelled, 15f + 1f, "did not walk past the target");
            Assert.Greater(pathLength, 0.85f * 5f * (steps - 10) / 50f, "walked too slowly (friction or snagging)");
            Assert.Less(math.abs(math.dot(end, side)) * _config.Radius, 2f, "veered more than 2 m off the great circle");
            Assert.GreaterOrEqual(grounded, (steps - 25) * 95 / 100, "lost ground contact (jitter/skipping)");
            Assert.Greater(minUpDot, math.cos(math.radians(0.5f)), "body up deviated from local up");
            Assert.Less(maxFacingStep, 1f, "facing flipped or jittered");
            // Collider triangles are chords; ~0.5 m sag allowance matches PlanetTerrainTests.
            Assert.Less(maxAltitudeError, 0.5f, "body left the ground surface");
        }

        [UnityTest]
        public IEnumerator Jump_leaves_and_returns_to_ground()
        {
            float3 dir = math.normalize(new float3(0.3f, 0.9f, -0.2f));
            yield return Spawn(dir, math.cross(dir, new float3(1f, 0f, 0f)));
            for (int i = 0; i < 40; i++)
            {
                _character.SetInput(Vector2.zero, _character.Facing, false, false);
                yield return new WaitForFixedUpdate();
            }

            Assert.IsTrue(_character.IsGrounded, "not grounded before jump");
            _character.SetInput(Vector2.zero, _character.Facing, false, true);
            bool left = false;
            float apex = 0f;
            float baseAltitude = _config.Altitude(_character.transform.position);
            for (int i = 0; i < 150; i++)
            {
                yield return new WaitForFixedUpdate();
                _character.SetInput(Vector2.zero, _character.Facing, false, false);
                left |= !_character.IsGrounded;
                apex = math.max(apex, _config.Altitude(_character.transform.position) - baseAltitude);
            }

            Assert.IsTrue(left, "never left the ground");
            Assert.IsTrue(_character.IsGrounded, "did not land");
            Assert.AreEqual(1.2f, apex, 0.25f, "jump apex");
        }

        [UnityTest]
        public IEnumerator Fly_stops_at_stratosphere_and_levitate_passes_it()
        {
            float3 dir = math.normalize(new float3(0f, 1f, 0f));
            yield return Spawn(dir, new float3(0f, 0f, 1f));

            _character.Teleport(_config.ToWorld(dir, _radius - 8f));

            _character.SetTraversalEnvelope(new TraversalEnvelope(
                aerialActive: true, maxAltitudeM: _radius, airSpeedScale: 4f,
                canSurfaceSwim: false, canDive: false, seaLevelM: -0.12f,
                maxShallowDepthM: 1f, waterSurfaceSpeedScale: 1f, underwaterSpeedScale: 1f,
                enforceWaterBoundary: false));
            for (int i = 0; i < 60; i++)
            {
                _character.SetInput(Vector2.zero, _character.Facing, true, false);
                _character.SetVerticalInput(1f);
                yield return new WaitForFixedUpdate();
                Assert.LessOrEqual(_character.AltitudeM, _radius + 0.1f, "Fly must stay within 1.0 R stratosphere");
            }

            Assert.Greater(_character.AltitudeM, _radius - 1f, "Fly did not climb to stratosphere");

            _character.SetTraversalEnvelope(new TraversalEnvelope(
                aerialActive: true, maxAltitudeM: _radius * 1.6f, airSpeedScale: 4f,
                canSurfaceSwim: false, canDive: false, seaLevelM: -0.12f,
                maxShallowDepthM: 1f, waterSurfaceSpeedScale: 1f, underwaterSpeedScale: 1f,
                enforceWaterBoundary: false));
            for (int i = 0; i < 40; i++)
            {
                _character.SetInput(Vector2.zero, _character.Facing, true, false);
                _character.SetVerticalInput(1f);
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(_character.AltitudeM, _radius + 5f, "Levitate must exceed 1.0 R stratosphere");
            Assert.LessOrEqual(_character.AltitudeM, _radius * 1.6f + 0.1f, "Levitate stays within 1.6 R ceiling");
        }

        [UnityTest]
        public IEnumerator Water_depth_gate_swim_surface_speed_and_dive_submersion()
        {
            float3 dir = math.normalize(new float3(0f, 1f, 0f));
            yield return Spawn(dir, new float3(0f, 0f, 1f));
            float seaLevel = _height.Sample(dir) + 3.5f;

            // Without Swim or Dive, entry past 1 m water depth is blocked above SeaLevel - 1 m.
            _character.Teleport(_config.ToWorld(dir, seaLevel - 1.5f));
            _character.SetTraversalEnvelope(new TraversalEnvelope(
                aerialActive: false, maxAltitudeM: 0f, airSpeedScale: 1f,
                canSurfaceSwim: false, canDive: false, seaLevelM: seaLevel,
                maxShallowDepthM: 1f, waterSurfaceSpeedScale: 0.8f, underwaterSpeedScale: 0f,
                enforceWaterBoundary: true));
            for (int i = 0; i < 10; i++)
            {
                _character.SetInput(new Vector2(0f, 1f), _character.Facing, false, false);
                _character.SetVerticalInput(0f);
                yield return new WaitForFixedUpdate();
            }
            Assert.GreaterOrEqual(_character.AltitudeM, seaLevel - 1.05f, "Without Dive, cannot stay deeper than 1 m");

            // Swim moves faster at surface than Dive alone; neither submerges > 1 m with Swim alone.
            float3 swimStart = dir;
            _character.Teleport(_config.ToWorld(swimStart, seaLevel));
            _character.SetTraversalEnvelope(new TraversalEnvelope(
                aerialActive: false, maxAltitudeM: 0f, airSpeedScale: 1f,
                canSurfaceSwim: true, canDive: false, seaLevelM: seaLevel,
                maxShallowDepthM: 1f, waterSurfaceSpeedScale: 1.15f, underwaterSpeedScale: 0f,
                enforceWaterBoundary: true));
            for (int i = 0; i < 40; i++)
            {
                _character.SetInput(new Vector2(0f, 1f), _character.Facing, false, false);
                _character.SetVerticalInput(-1f);
                yield return new WaitForFixedUpdate();
            }
            float swimArc = math.distance(_config.ToDirection(_character.transform.position), swimStart) * _radius;
            Assert.GreaterOrEqual(_character.AltitudeM, seaLevel - 1.05f, "Swim must not submerge below 1 m");

            float3 diveSurfStart = _config.ToDirection(_character.transform.position);
            _character.Teleport(_config.ToWorld(diveSurfStart, seaLevel));
            _character.SetTraversalEnvelope(new TraversalEnvelope(
                aerialActive: false, maxAltitudeM: 0f, airSpeedScale: 1f,
                canSurfaceSwim: true, canDive: true, seaLevelM: seaLevel,
                maxShallowDepthM: 1f, waterSurfaceSpeedScale: 0.85f, underwaterSpeedScale: 1f,
                enforceWaterBoundary: true));
            for (int i = 0; i < 40; i++)
            {
                _character.SetInput(new Vector2(0f, 1f), _character.Facing, false, false);
                _character.SetVerticalInput(0f);
                yield return new WaitForFixedUpdate();
            }
            float diveSurfArc = math.distance(_config.ToDirection(_character.transform.position), diveSurfStart) * _radius;
            Assert.Greater(swimArc, diveSurfArc, "Swim must move faster on water surface than Dive");

            // With Dive holding descend, player submerges deeper than 1 m.
            _character.Teleport(_config.ToWorld(dir, seaLevel - 0.5f));
            for (int i = 0; i < 30; i++)
            {
                _character.SetInput(Vector2.zero, _character.Facing, false, false);
                _character.SetVerticalInput(-1f);
                yield return new WaitForFixedUpdate();
            }
            Assert.Less(_character.AltitudeM, seaLevel - 1.2f, "Dive should submerge past 1 m");
        }

        [UnityTest]
        public IEnumerator Ceiling_provider_caps_aerial_and_launch_height_and_blocks_high_altitude_entry()
        {
            float3 zoneCenter = math.normalize(new float3(0f, 1f, 0f));
            yield return Spawn(zoneCenter, new float3(0f, 0f, 1f));
            float ground = _height.Sample(zoneCenter);
            float ceiling = ground + 6f;
            float zoneDot = math.cos(12f / _radius);

            _character.SetCeilingProvider(d => math.dot(d, zoneCenter) >= zoneDot ? ceiling : float.PositiveInfinity);

            // 1. Aerial climb inside the capped zone stops at the ceiling.
            _character.SetTraversalEnvelope(new TraversalEnvelope(
                aerialActive: true, maxAltitudeM: _radius, airSpeedScale: 4f,
                canSurfaceSwim: false, canDive: false, seaLevelM: -0.12f,
                maxShallowDepthM: 1f, waterSurfaceSpeedScale: 1f, underwaterSpeedScale: 1f,
                enforceWaterBoundary: false));
            for (int i = 0; i < 60; i++)
            {
                _character.SetInput(Vector2.zero, _character.Facing, true, false);
                _character.SetVerticalInput(1f);
                yield return new WaitForFixedUpdate();
                Assert.LessOrEqual(_character.AltitudeM, ceiling + 0.1f, "Fly inside capped zone must not exceed ceiling");
            }
            Assert.Greater(_character.AltitudeM, ceiling - 1f, "Fly should reach near the ceiling");

            // 2. Ground launch inside the capped zone is also capped at the ceiling.
            _character.SetTraversalEnvelope(TraversalEnvelope.Default);
            _character.Teleport(_config.ToWorld(zoneCenter, ground));
            yield return new WaitForFixedUpdate();
            _character.Launch(Vector3.zero, 20f);
            for (int i = 0; i < 30; i++)
            {
                _character.SetInput(Vector2.zero, _character.Facing, false, false);
                yield return new WaitForFixedUpdate();
                Assert.LessOrEqual(_character.AltitudeM, ceiling + 0.1f, "Launch must not exceed ceiling");
            }

            // 3. Flying outside the capped zone above the ceiling cannot cross into it horizontally.
            float3 outsideDir = math.normalize(zoneCenter + new float3(0f, 0f, 18f / _radius));
            _character.Teleport(_config.ToWorld(outsideDir, ground + 20f));
            _character.SetTraversalEnvelope(new TraversalEnvelope(
                aerialActive: true, maxAltitudeM: _radius, airSpeedScale: 4f,
                canSurfaceSwim: false, canDive: false, seaLevelM: -0.12f,
                maxShallowDepthM: 1f, waterSurfaceSpeedScale: 1f, underwaterSpeedScale: 1f,
                enforceWaterBoundary: false));
            for (int i = 0; i < 40; i++)
            {
                float3 toCenter = math.normalize(zoneCenter - _config.ToDirection(_character.transform.position));
                _character.SetInput(new Vector2(0f, 1f), toCenter, true, false);
                _character.SetVerticalInput(0f);
                yield return new WaitForFixedUpdate();
            }

            float3 finalDir = _config.ToDirection(_character.transform.position);
            Assert.Less(math.dot(finalDir, zoneCenter), zoneDot + 1e-4f,
                "High-altitude flight outside must be blocked from entering the capped zone");
            Assert.Greater(_character.AltitudeM, ceiling + 5f,
                "Blocked high-altitude flyer must not be dropped down to the ceiling");
        }
    }
}
