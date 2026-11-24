using System;
using System.Collections.Generic;
using System.Reflection;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Lobby;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Lobby.Tests
{
    public sealed class StarRiftRulesTests
    {
        private static PlanetTypeDef MakePlanetType(string id, params Element[] bias)
        {
            PlanetTypeDef def = ScriptableObject.CreateInstance<PlanetTypeDef>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(GameDef).GetField("id", flags).SetValue(def, id);
            typeof(PlanetTypeDef).GetField("terrainTags", flags).SetValue(def, new List<string> { "forest" });
            typeof(PlanetTypeDef).GetField("elementBias", flags).SetValue(def, new List<Element>(bias));
            return def;
        }

        [Test]
        public void GenerateRifts_is_deterministic_excludes_cradle_and_respects_unlock_cap()
        {
            PlanetTypeDef cradle = MakePlanetType("cradle_planet", Element.Wood);
            PlanetTypeDef verdant = MakePlanetType("verdant_planet", Element.Wood, Element.Water);
            PlanetTypeDef lava = MakePlanetType("lava_planet", Element.Fire, Element.Earth);
            try
            {
                var types = new[] { cradle, verdant, lava };
                var settlements = new[] { new float3(0f, 1f, 0f), new float3(0f, -1f, 0f) };

                List<StarRiftEntry> a = StarRiftRules.GenerateRifts(
                    20261121UL, 0, PlanetSize.Asteroid, CreatureStage.Adult, types, settlements, 2560f, 8);
                List<StarRiftEntry> b = StarRiftRules.GenerateRifts(
                    20261121UL, 0, PlanetSize.Asteroid, CreatureStage.Adult, types, settlements, 2560f, 8);
                List<StarRiftEntry> epoch1 = StarRiftRules.GenerateRifts(
                    20261121UL, 1, PlanetSize.Moon, CreatureStage.Adolescent, types, settlements, 2560f, 8);

                Assert.That(a.Count, Is.EqualTo(8));
                Assert.That(b.Count, Is.EqualTo(8));
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.That(a[i].RiftIndex, Is.EqualTo(i));
                    Assert.That(a[i].Seed, Is.EqualTo(b[i].Seed));
                    Assert.That(a[i].PlanetTypeId, Is.EqualTo(b[i].PlanetTypeId));
                    Assert.That(a[i].PlanetTypeId, Is.Not.EqualTo("cradle_planet"));
                    Assert.That(a[i].Size, Is.EqualTo(PlanetSize.Asteroid));
                    Assert.That(a[i].BossStage, Is.EqualTo(CreatureStage.Juvenile));
                    Assert.That(math.length(a[i].Direction), Is.EqualTo(1f).Within(1e-3f));
                    Assert.That(a[i].Direction, Is.EqualTo(b[i].Direction));

                    foreach (float3 s in settlements)
                    {
                        float distM = math.distance(a[i].Direction, s) * 2560f;
                        Assert.That(distM, Is.GreaterThanOrEqualTo(StarRiftRules.MinClearanceFromSettlementM - 0.1f));
                    }
                }

                Assert.That(epoch1[0].Seed, Is.Not.EqualTo(a[0].Seed));
                foreach (StarRiftEntry r in epoch1)
                {
                    Assert.That((int)r.Size, Is.LessThanOrEqualTo((int)PlanetSize.Moon));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cradle);
                UnityEngine.Object.DestroyImmediate(verdant);
                UnityEngine.Object.DestroyImmediate(lava);
            }
        }

        [Test]
        public void StarRating_and_TryBuildCustomSeedRift_match_boundaries()
        {
            Assert.That(StarRiftRules.StarRating(PlanetSize.Asteroid), Is.EqualTo(1));
            Assert.That(StarRiftRules.StarRating(PlanetSize.Moon), Is.EqualTo(2));
            Assert.That(StarRiftRules.StarRating(PlanetSize.Planet), Is.EqualTo(3));
            Assert.That(StarRiftRules.StarRating(PlanetSize.Supergiant), Is.EqualTo(4));

            PlanetTypeDef cradle = MakePlanetType("cradle_planet", Element.Wood);
            PlanetTypeDef ice = MakePlanetType("ice_planet", Element.Ice, Element.Water);
            try
            {
                Assert.That(StarRiftRules.TryBuildCustomSeedRift(0UL, ice, PlanetSize.Planet, CreatureStage.Adult, out _), Is.False);
                Assert.That(StarRiftRules.TryBuildCustomSeedRift(1234UL, cradle, PlanetSize.Planet, CreatureStage.Adult, out _), Is.False);

                Assert.That(StarRiftRules.TryBuildCustomSeedRift(98765UL, ice, PlanetSize.Planet, CreatureStage.Adult, out StarRiftEntry entry), Is.True);
                Assert.That(entry.RiftIndex, Is.EqualTo(-1));
                Assert.That(entry.Seed, Is.EqualTo(98765UL));
                Assert.That(entry.PlanetTypeId, Is.EqualTo("ice_planet"));
                Assert.That((int)entry.Size, Is.LessThanOrEqualTo((int)PlanetSize.Planet));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cradle);
                UnityEngine.Object.DestroyImmediate(ice);
            }
        }

        [Test]
        public void Invalid_preconditions_throw_contract_exception()
        {
            PlanetTypeDef cradle = MakePlanetType("cradle_planet", Element.Wood);
            PlanetTypeDef verdant = MakePlanetType("verdant_planet", Element.Wood);
            try
            {
                Assert.Throws<ContractException>(() => StarRiftRules.StarRating((PlanetSize)99));
                Assert.Throws<ContractException>(() => StarRiftRules.GenerateRifts(
                    1UL, -1, PlanetSize.Asteroid, CreatureStage.Juvenile, new[] { verdant }, null, 2560f));
                Assert.Throws<ContractException>(() => StarRiftRules.GenerateRifts(
                    1UL, 0, PlanetSize.Asteroid, CreatureStage.Juvenile, Array.Empty<PlanetTypeDef>(), null, 2560f));
                Assert.Throws<ContractException>(() => StarRiftRules.GenerateRifts(
                    1UL, 0, PlanetSize.Asteroid, CreatureStage.Juvenile, new[] { cradle }, null, 2560f));
                Assert.Throws<ContractException>(() => StarRiftRules.GenerateRifts(
                    1UL, 0, PlanetSize.Asteroid, CreatureStage.Juvenile, new[] { verdant }, null, 0f));
                Assert.Throws<ContractException>(() => StarRiftRules.TryBuildCustomSeedRift(
                    1UL, null, PlanetSize.Asteroid, CreatureStage.Juvenile, out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cradle);
                UnityEngine.Object.DestroyImmediate(verdant);
            }
        }
    }
}
