using System.Collections;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Player.Tests
{
    public sealed class SphericalCharacterTests
    {
        private GameObject _root;
        private PlanetConfig _config;
        private PlanetHeight _height;
        private PlanetTerrain _terrain;
        private SphericalCharacter _character;

        [SetUp]
        public void SetUp()
        {
            _config = new PlanetConfig(640f, new float3(10f, -20f, 30f));
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
    }
}
