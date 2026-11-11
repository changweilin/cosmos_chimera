using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: destructibles.md#物理 / ADR-0009 — woken objects and debris fall toward the planet centre, hits shove light
    // objects, caps hold, and nothing falls through to the core where the terrain has no collider.
    public sealed class ObjectPhysicsTests
    {
        private const float R = 40f;
        private static readonly PlanetConfig Planet = new PlanetConfig(R, float3.zero);
        private static readonly float3 Top = new float3(0f, 1f, 0f);
        private readonly List<GameObject> _roots = new List<GameObject>();
        private PlanetGravity _gravity;

        // A static sphere stands in for the terrain; colliderRadius < R leaves "ground" (altitude 0) without a collider.
        private ObjectPhysics Rig(float colliderRadius, ObjectPhysicsTuning tuning, params Collider[] ignore)
        {
            var ground = new GameObject("ground");
            _roots.Add(ground);
            ground.AddComponent<SphereCollider>().radius = colliderRadius;
            var host = new GameObject("physics");
            _roots.Add(host);
            _gravity = host.AddComponent<PlanetGravity>();
            _gravity.Configure(Planet, 9.81f);
            ObjectPhysics physics = host.AddComponent<ObjectPhysics>();
            physics.Configure(Planet, _gravity, _ => 0f, ignore, tuning);
            return physics;
        }

        private GameObject Block(float3 direction, float altitude, float edge = 1f)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _roots.Add(go);
            go.transform.position = (Vector3)Planet.ToWorld(direction, altitude);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, (Vector3)direction);
            go.transform.localScale = Vector3.one * edge;
            return go;
        }

        private static float3 Dir(float alongM) => new float3(math.sin(alongM / R), math.cos(alongM / R), 0f);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject go in _roots)
            {
                Object.Destroy(go);
            }

            _roots.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator A_woken_block_is_pushed_away_from_the_caster_and_settles_on_the_planet()
        {
            Vector3 flatGravity = Physics.gravity;
            ObjectPhysics physics = Rig(R, ObjectPhysicsTuning.Default);
            GameObject block = Block(Top, 0.5f);
            float3 start = (float3)block.transform.position;
            float3 caster = Planet.ToWorld(Dir(-2f), 0.5f);
            float3 impulse = ObjectImpulse.Compute(Planet, caster, start, 40, 100, 100f, physics.Tuning);
            Assert.That(physics.Wake(7, block, 100f, impulse, start), Is.True);
            Assert.That(physics.WokenRoot(7), Is.SameAs(block));
            Assert.That(block.GetComponent<Rigidbody>().useGravity, Is.False, "planet gravity, not Physics.gravity");

            yield return new WaitForSeconds(2.5f);
            float3 end = (float3)block.transform.position;
            float3 east = math.normalize(Planet.ToWorld(Dir(1f), 0f) - Planet.ToWorld(Dir(0f), 0f));
            Assert.That(math.dot(end - start, east), Is.GreaterThan(0.5f), "moved away from the caster");
            Assert.That(Planet.Altitude(end), Is.InRange(0.3f, 1.0f), "resting on the surface, not floating or sunk");
            Assert.That(Physics.gravity, Is.EqualTo(flatGravity), "the global setting is untouched");
        }

        [UnityTest]
        public IEnumerator A_heavy_block_never_wakes()
        {
            ObjectPhysics physics = Rig(R, ObjectPhysicsTuning.Default);
            GameObject block = Block(Top, 0.5f);
            float3 impulse = ObjectImpulse.Compute(Planet, Planet.ToWorld(Dir(-2f), 0.5f), (float3)block.transform.position,
                120, 100, physics.Tuning.MaxMovableKg * 2f, physics.Tuning);
            Assert.That(physics.Wake(1, block, physics.Tuning.MaxMovableKg * 2f, impulse, (float3)block.transform.position),
                Is.False);
            yield return null;
            Assert.That(block.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(physics.AwakeCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Waking_past_the_cap_freezes_the_oldest()
        {
            ObjectPhysicsTuning tuning = ObjectPhysicsTuning.Default;
            tuning.MaxAwake = 2;
            ObjectPhysics physics = Rig(R, tuning);
            var blocks = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                blocks[i] = Block(Dir(i * 3f), 0.5f);
                float3 at = (float3)blocks[i].transform.position;
                physics.Wake(i, blocks[i], 50f, ObjectImpulse.Compute(Planet, Planet.ToWorld(Dir(i * 3f - 2f), 0.5f), at, 10,
                    100, 50f, tuning), at);
            }

            yield return new WaitForFixedUpdate();
            Assert.That(physics.AwakeCount, Is.EqualTo(2));
            Assert.That(blocks[0].GetComponent<Rigidbody>().isKinematic, Is.True, "oldest frozen in place");
            Assert.That(blocks[2].GetComponent<Rigidbody>().isKinematic, Is.False);
        }

        [UnityTest]
        public IEnumerator Shattering_spawns_debris_that_falls_and_then_disappears()
        {
            ObjectPhysicsTuning tuning = ObjectPhysicsTuning.Default;
            tuning.DebrisMaxSeconds = 1.5f;
            ObjectPhysics physics = Rig(R, tuning);
            GameObject tree = Block(Top, 1.5f);
            float3 center = (float3)tree.transform.position;
            physics.Shatter(3, tree, center, 1f, 300f, float3.zero);
            Object.Destroy(tree); // the caller removes the intact object, as RunSession does
            Assert.That(physics.DebrisCount, Is.InRange(3, 8));

            yield return new WaitForSeconds(0.8f);
            Assert.That(physics.DebrisCount, Is.GreaterThan(0));
            foreach (Rigidbody b in physics.GetComponentsInChildren<Rigidbody>())
            {
                Assert.That(Planet.Altitude(b.position), Is.InRange(-0.2f, 4f), "falls onto the planet, not along a world axis");
            }

            yield return new WaitForSeconds(1.2f);
            Assert.That(physics.DebrisCount, Is.EqualTo(0));
            Assert.That(physics.GetComponentsInChildren<Rigidbody>().Length, Is.EqualTo(0));
            Assert.That(physics.WokenRoot(3), Is.Null);
        }

        [UnityTest]
        public IEnumerator Bodies_over_a_missing_terrain_collider_stop_at_the_sampled_ground()
        {
            // The collider is 10 m below the sampled ground: the terrain chunk there has no collider yet.
            ObjectPhysics physics = Rig(R - 10f, ObjectPhysicsTuning.Default);
            GameObject block = Block(Top, 0.5f);
            float3 at = (float3)block.transform.position;
            physics.Wake(9, block, 10f, ObjectImpulse.Compute(Planet, Planet.ToWorld(Dir(-1f), 0.5f), at, 1, 100, 10f,
                physics.Tuning), at);
            GameObject rock = Block(Dir(5f), 0.5f);
            physics.Shatter(10, rock, Planet.ToWorld(Dir(5f), 0.5f), 1f, 50f, float3.zero);
            Object.Destroy(rock);

            yield return new WaitForSeconds(2f);
            Rigidbody body = block.GetComponent<Rigidbody>();
            Assert.That(body.isKinematic, Is.True, "parked instead of falling to the core");
            Assert.That(Planet.Altitude(body.position), Is.EqualTo(0f).Within(0.05f));
            Assert.That(physics.DebrisCount, Is.EqualTo(0), "debris that fell through is removed");
        }

        private static float3 Lane(float alongM, float lateralM) =>
            math.normalize(new float3(math.sin(alongM / R), math.cos(alongM / R), lateralM / R));

        [UnityTest]
        public IEnumerator A_creature_pusher_shoves_loose_bodies_but_not_ignored_colliders()
        {
            // Two lanes 5 m apart, so the loose block is never shoved into the character (a sandwich would push it).
            GameObject character = Block(Lane(0f, 5f), 0.5f);
            Rigidbody characterBody = character.AddComponent<Rigidbody>();
            ObjectPhysics physics = Rig(R, ObjectPhysicsTuning.Default, character.GetComponent<Collider>());
            _gravity.Register(characterBody);
            GameObject loose = Block(Lane(0f, 0f), 0.5f, 0.6f);
            Rigidbody looseBody = loose.AddComponent<Rigidbody>();
            looseBody.mass = 5f;
            _gravity.Register(looseBody);
            yield return new WaitForSeconds(0.5f);

            float3 looseStart = (float3)loose.transform.position;
            float3 characterStart = (float3)character.transform.position;
            for (float t = 0f; t < 1.5f; t += Time.fixedDeltaTime)
            {
                float along = -3f + t * 4f; // each pusher sweeps from -3 m to +3 m along its lane
                physics.SyncPusher(1, true, Planet.ToWorld(Lane(along, 0f), 0.5f), 0.5f);
                physics.SyncPusher(2, true, Planet.ToWorld(Lane(along, 5f), 0.5f), 0.5f);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(math.distance((float3)loose.transform.position, looseStart), Is.GreaterThan(0.3f), "pushed aside");
            Assert.That(math.distance((float3)character.transform.position, characterStart), Is.LessThan(0.1f),
                "the character's collision stays rule-side");
            physics.SyncPusher(1, false, float3.zero, 0f);
            Assert.That(physics.transform.Find("Pusher_1").gameObject.activeSelf, Is.False);
        }
    }
}
