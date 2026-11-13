using System.Collections;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests.PlayMode
{
    public sealed class CharacterModelRigTests
    {
        [UnityTest]
        public IEnumerator PlanetRig_Build_with_character_prefab_attaches_driver_and_strips_visual_colliders()
        {
            var prefab = new GameObject("MockProvisionalChar");
            var modelChild = new GameObject("Model");
            modelChild.transform.SetParent(prefab.transform, false);
            modelChild.AddComponent<BoxCollider>();
            modelChild.AddComponent<Animator>();

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var config = new PlanetConfig(320f, float3.zero);
            var height = new PlanetHeight(42u, 4f, 0.02f, 3);
            PlanetRig rig = null;

            try
            {
                rig = PlanetRig.Build(config, height, new float3(0f, 1f, 0f), mat, null, prefab);
                yield return null; // allow Destroy on stripped visual collider to process

                Transform player = rig.Character.transform;
                Transform spawnedVisual = player.Find("MockProvisionalChar");
                Assert.That(spawnedVisual, Is.Not.Null);
                Assert.That(spawnedVisual.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(player.GetComponent<CharacterAnimatorDriver>(), Is.Not.Null);
                Assert.That(player.GetComponent<CapsuleCollider>(), Is.Not.Null);
            }
            finally
            {
                rig?.Dispose();
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(mat);
            }
        }
    }
}
