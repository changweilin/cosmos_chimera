using System;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: planet-world.md#星球尺度 (Q-D1, 2026-11-11). SOL Q-D1-IMPL-02 M5: no test pinned the shipped tier mapping,
    // so Moon silently falling to the 320 m combat minimum passed every suite.
    public sealed class DemoPlanetSizeTests
    {
        [TestCase(PlanetSize.Asteroid, 320f)]
        [TestCase(PlanetSize.Moon, 640f)]
        [TestCase(PlanetSize.Planet, 960f)]
        [TestCase(PlanetSize.Supergiant, 1280f)]
        public void Each_size_tier_maps_to_its_radius(PlanetSize size, float radius)
        {
            Assert.That(DemoGame.RadiusFor(size), Is.EqualTo(radius));
        }

        [Test]
        public void An_undefined_size_fails_loudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DemoGame.RadiusFor((PlanetSize)99));
        }

        [Test]
        public void Beginner_option_prefers_verdant_asteroid_with_juvenile_boss()
        {
            PlanetTypeDef lava = ScriptableObject.CreateInstance<PlanetTypeDef>();
            PlanetTypeDef verdant = ScriptableObject.CreateInstance<PlanetTypeDef>();
            try
            {
                SetPlanetId(lava, "lava_planet");
                SetPlanetId(verdant, "verdant_planet");
                var option = DemoGame.BuildBeginnerOption(new[] { lava, verdant });
                Assert.That(option.type, Is.SameAs(verdant));
                Assert.That(option.seed, Is.EqualTo(DemoGame.BeginnerQuestSeed));
                Assert.That(option.bossStage, Is.EqualTo(CreatureStage.Juvenile));
                Assert.That(option.radius, Is.EqualTo(320f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lava);
                UnityEngine.Object.DestroyImmediate(verdant);
            }
        }

        private static void SetPlanetId(PlanetTypeDef def, string id)
        {
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
