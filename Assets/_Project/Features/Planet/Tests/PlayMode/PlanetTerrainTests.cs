using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Planet.Tests
{
    // R=320 (asteroid) since Q-D1 (2026-11-11); SOL Q-D1-R3 asked for the native run at that size too.
    [TestFixture(640f)]
    [TestFixture(320f)]
    public sealed class PlanetTerrainTests
    {
        private readonly float _radius;

        public PlanetTerrainTests(float radius)
        {
            _radius = radius;
        }

        private const float EyeHeight = 1.7f;
        private GameObject _root;
        private PlanetConfig _config;
        private PlanetHeight _height;
        private Transform _viewer;
        private PlanetTerrain _terrain;

        [SetUp]
        public void SetUp()
        {
            _config = new PlanetConfig(_radius, new float3(10f, -20f, 30f));
            _height = PlanetHeight.ForPlanet(_config, 42UL);
            _root = new GameObject("PlanetTerrainTest");
            _viewer = new GameObject("Viewer").transform;
            _viewer.SetParent(_root.transform, false);
            var planet = new GameObject("Planet");
            planet.transform.SetParent(_root.transform, false);
            _terrain = planet.AddComponent<PlanetTerrain>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_root);
        }

        private void PlaceViewer(float3 direction)
        {
            float3 dir = math.normalize(direction);
            _viewer.position = _config.ToWorld(dir, _height.Sample(dir) + EyeHeight);
        }

        // Wall-clock bound, not a frame count: -nographics batchmode spins frames in microseconds while the
        // Burst jobs take real time.
        private IEnumerator Settle(float maxSeconds)
        {
            float deadline = Time.realtimeSinceStartup + maxSeconds;
            while (!_terrain.IsSettled && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(_terrain.IsSettled, $"terrain did not settle within {maxSeconds} s");
        }

        [UnityTest]
        public IEnumerator Settles_with_colliders_only_near_the_viewer_and_ground_under_it()
        {
            PlaceViewer(new float3(0.3f, 0.9f, -0.2f));
            _terrain.Configure(_config, _height, _viewer, null);
            yield return Settle(30f);

            Assert.Greater(_terrain.ShownCount, 6);
            int colliders = _terrain.CountColliders(out float farthest);
            Assert.Greater(colliders, 0);
            Assert.LessOrEqual(farthest, _terrain.ColliderRange);

            yield return new WaitForFixedUpdate();
            float3 up = _config.ToDirection(_viewer.position);
            Assert.IsTrue(Physics.Raycast(_viewer.position, -(Vector3)up, out RaycastHit hit, 10f), "no ground collider under the viewer");
            // Collider triangles are chords of the curved surface, so allow ~a vertex spacing of sag.
            Assert.AreEqual(EyeHeight, hit.distance, 0.5f);
        }

        [UnityTest]
        public IEnumerator Walking_keeps_main_thread_under_budget_and_colliders_in_range()
        {
            float3 start = math.normalize(new float3(1f, 1f, 1f)); // a cube corner: worst case for cross-face LOD
            float3 axis = math.normalize(math.cross(start, new float3(0f, 0f, 1f)));
            PlaceViewer(start);
            _terrain.Configure(_config, _height, _viewer, null);
            yield return Settle(30f);

            // 300 frames × 0.5 m = 150 m along a great circle (sprint speed at 60 fps for 5 s).
            var frameMs = new List<double>();
            float maxFarthest = 0f;
            for (int f = 1; f <= 300; f++)
            {
                float angle = f * 0.5f / _config.Radius;
                PlaceViewer(math.mul(quaternion.AxisAngle(axis, angle), start));
                yield return null;
                frameMs.Add(_terrain.LastFrameMs);
                Assert.AreEqual(0, _terrain.CountEmptyColliders(), $"frame {f}: an enabled collider lost its mesh");
                _terrain.CountColliders(out float farthest);
                maxFarthest = math.max(maxFarthest, farthest);
            }

            frameMs.Sort();
            double p95 = frameMs[(int)(frameMs.Count * 0.95)];
            double max = frameMs[frameMs.Count - 1];
            TestContext.WriteLine($"PlanetTerrain main thread: p95 {p95:F3} ms, max {max:F3} ms; farthest collider {maxFarthest:F1} m");
            Assert.Less(p95, 2.0, "P1-03: main thread per frame must stay under 2 ms");
            // Colliders are dropped on the first frame no bake is in flight, so allow one frame of travel.
            Assert.LessOrEqual(maxFarthest, _terrain.ColliderRange + 1f);
        }
    }
}
