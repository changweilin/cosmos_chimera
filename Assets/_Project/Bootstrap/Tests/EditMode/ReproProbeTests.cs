using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class ReproProbeTests
    {
        private PlanetTypeDef _type;
        private CreatureDef _adult;
        private CreatureDef _juvenile;
        private readonly RunPlanVocabulary _vocabulary = new RunPlanVocabulary(
            new[] { "forest", "coast", "ruins" }, new[] { "fog" }, new[] { "chain" });

        [SetUp]
        public void SetUp()
        {
            _type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            JsonUtility.FromJsonOverwrite("{\"id\":\"probe\",\"size\":1,\"terrainTags\":[\"forest\"],\"elementBias\":[0]}", _type);
            _adult = Species("adult", 2);
            _juvenile = Species("juvenile", 0);
        }

        private static CreatureDef Species(string id, int stage)
        {
            var creature = ScriptableObject.CreateInstance<CreatureDef>();
            JsonUtility.FromJsonOverwrite("{\"id\":\"" + id + "\",\"stage\":" + stage +
                ",\"mastery\":[{\"element\":0,\"value\":15}]}", creature);
            return creature;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_type);
            Object.DestroyImmediate(_adult);
            Object.DestroyImmediate(_juvenile);
        }

        private string Report(PlanetTypeDef[] types, CreatureDef[] pool, StringBuilder dump = null,
            Func<PlanetSize, float> radius = null) => ReproProbe.Report(types, pool, _vocabulary,
            Array.Empty<TemplateSource>(), Array.Empty<IReadOnlyList<string>>(), radius ?? (_ => 640f), "test", dump);

        [Test]
        public void Null_type_is_rejected_before_generation_or_dump_mutation()
        {
            var dump = new StringBuilder("sentinel");
            Assert.Throws<ContractException>(() => Report(new[] { _type, null }, new[] { _adult }, dump,
                _ => throw new AssertionException("generation started before validation")));
            Assert.That(dump.ToString(), Is.EqualTo("sentinel"));
        }

        [TestCase("empty")]
        [TestCase("no-adult")]
        [TestCase("null")]
        [TestCase("no-mastery")]
        public void Invalid_wild_pool_is_rejected_before_generation(string invalid)
        {
            CreatureDef[] pool = invalid == "empty" ? Array.Empty<CreatureDef>() :
                invalid == "no-adult" ? new[] { _juvenile } : invalid == "null" ? new[] { _adult, null } : new[] { _adult };
            if (invalid == "no-mastery") JsonUtility.FromJsonOverwrite("{\"mastery\":[]}", _adult);
            Assert.Throws<ContractException>(() => Report(new[] { _type }, pool, radius:
                _ => throw new AssertionException("generation started before validation")));
        }

        [Test]
        public void Report_includes_exact_landing_bits_and_is_deterministic()
        {
            string first = Report(new[] { _type }, new[] { _adult });
            Assert.That(Report(new[] { _type }, new[] { _adult }), Is.EqualTo(first));
            StringAssert.Contains("planDiscrete landingBits\n", first);
            foreach (string row in first.Split('\n'))
            {
                if (row.Length == 0 || row[0] == '#') continue;
                string[] columns = row.Split(' ');
                ulong seed = ulong.Parse(columns[1], CultureInfo.InvariantCulture);
                RunPlan plan = RunPlanner.Generate(new PlanetConfig(640f, float3.zero), seed, _type,
                    CreatureStage.Adult, new[] { _adult }, _vocabulary, null);
                string expected = math.asuint(plan.LandingDirection.x).ToString("x8") + "," +
                    math.asuint(plan.LandingDirection.y).ToString("x8") + "," + math.asuint(plan.LandingDirection.z).ToString("x8");
                Assert.That(columns[10], Is.EqualTo(expected));
            }
        }

        [Test]
        public void Repro_build_refuses_interactive_editor_before_building_or_exiting()
        {
            Type type = Type.GetType("Cosmimera.EditorTools.ReproBuild, Assembly-CSharp-Editor", true);
            MethodInfo build = type.GetMethod("BuildAll", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(bool) }, null);
            var target = UnityEditor.Build.NamedBuildTarget.Standalone;
            var backend = UnityEditor.PlayerSettings.GetScriptingBackend(target);
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => build.Invoke(null, new object[] { false }));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(UnityEditor.PlayerSettings.GetScriptingBackend(target), Is.EqualTo(backend));
        }
    }
}
