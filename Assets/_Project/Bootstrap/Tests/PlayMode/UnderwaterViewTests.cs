using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // planet-world.md 潛水視覺: the view follows the camera under the surface, and the water's fog gives way to the
    // weather's again on surfacing.
    public sealed class UnderwaterViewTests
    {
        private const float Radius = 8000f;
        private readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _owned) Object.Destroy(o);
            _owned.Clear();
            Shader.SetGlobalVector("_CosmiUnderwater", Vector4.zero);
        }

        [Test]
        public void Submerged_follows_depth_and_the_global_carries_the_sea_radius()
        {
            var planet = new PlanetConfig(Radius, new float3(0f, -Radius, 0f));
            var root = new GameObject("dive");
            _owned.Add(root);
            var camera = new GameObject("eye").transform;
            camera.SetParent(root.transform, false);
            var view = new UnderwaterView(root.transform);
            try
            {
                float3 toSun = new float3(0f, 1f, 0f);
                view.Tick(0.02f, planet, camera, float3.zero, 3f, Radius - 0.12f, toSun, 1f);
                Assert.That(view.Submerged, Is.True);
                Vector4 g = Shader.GetGlobalVector("_CosmiUnderwater");
                Assert.That(g.x, Is.EqualTo(1f));
                Assert.That(g.y, Is.EqualTo(3f).Within(1e-4f));
                Assert.That(g.z, Is.EqualTo(Radius - 0.12f).Within(1e-2f));

                view.Tick(0.02f, planet, camera, float3.zero, -1f, Radius - 0.12f, toSun, 1f);
                Assert.That(view.Submerged, Is.False);
                // No water sea (indoors, lava): never submerged and no caustics, whatever the depth says.
                view.Tick(0.02f, planet, camera, float3.zero, 5f, 0f, toSun, 1f);
                Assert.That(view.Submerged, Is.False);
                Assert.That(Shader.GetGlobalVector("_CosmiUnderwater").z, Is.EqualTo(0f));
                Assert.Throws<ContractException>(() => view.Tick(-1f, planet, camera, float3.zero, 1f, Radius, toSun, 1f));
            }
            finally
            {
                view.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Water_fog_closes_in_underwater_and_lifts_on_surfacing()
        {
            var planet = new PlanetConfig(Radius, new float3(0f, -Radius, 0f));
            var root = new GameObject("atmosphere");
            _owned.Add(root);
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.transform.position = new Vector3(0f, -3f, 0f);
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.SetParent(root.transform, false);
            var atmosphere = root.AddComponent<AtmosphereDriver>();
            atmosphere.Configure(camera, sun, planet, new float3(0f, 1f, 0f), PlanetLook.Sky("verdant_planet"), 360f, 1.7f);
            atmosphere.SetVisibilityRadius(180f);
            float weatherEnd = RenderSettings.fogEndDistance;

            atmosphere.SetUnderwater(3f, new Color(0.2f, 0.55f, 0.75f), new Color(0.08f, 0.22f, 0.45f));
            yield return null;
            Assert.That(RenderSettings.fogEndDistance, Is.LessThanOrEqualTo(26f));
            // A weather update while diving must not pull the fog back out.
            atmosphere.SetVisibilityRadius(150f);
            yield return null;
            Assert.That(RenderSettings.fogEndDistance, Is.LessThanOrEqualTo(26f));

            atmosphere.SetUnderwater(-0.5f, Color.cyan, Color.blue);
            yield return null;
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(math.min(150f, weatherEnd)).Within(1e-3f));
        }
    }
}
