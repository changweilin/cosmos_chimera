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
        private CreatureDef _adolescent;
        private readonly RunPlanVocabulary _vocabulary = new RunPlanVocabulary(
            new[] { "forest", "coast", "ruins" }, new[] { "fog" }, new[] { "chain" });

        [SetUp]
        public void SetUp()
        {
            _type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            JsonUtility.FromJsonOverwrite("{\"id\":\"probe\",\"size\":1,\"terrainTags\":[\"forest\"],\"elementBias\":[0]}", _type);
            _adult = Species("adult", 2);
            _juvenile = Species("juvenile", 0);
            _adolescent = Species("adolescent", 1);
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
            Object.DestroyImmediate(_adolescent);
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
        [TestCase("no-juvenile")]
        [TestCase("no-adolescent")]
        [TestCase("no-adult")]
        [TestCase("null")]
        [TestCase("no-mastery")]
        public void Invalid_wild_pool_is_rejected_before_generation(string invalid)
        {
            CreatureDef[] pool = invalid == "empty" ? Array.Empty<CreatureDef>() :
                invalid == "no-juvenile" ? new[] { _adolescent, _adult } :
                invalid == "no-adolescent" ? new[] { _juvenile, _adult } :
                invalid == "no-adult" ? new[] { _juvenile, _adolescent } :
                invalid == "null" ? new[] { _juvenile, _adolescent, _adult, null } : new[] { _juvenile, _adolescent, _adult };
            if (invalid == "no-mastery") JsonUtility.FromJsonOverwrite("{\"mastery\":[]}", _adult);
            Assert.Throws<ContractException>(() => Report(new[] { _type }, pool, radius:
                _ => throw new AssertionException("generation started before validation")));
        }

        [Test]
        public void Report_includes_exact_landing_bits_and_is_deterministic()
        {
            CreatureDef[] pool = { _juvenile, _adolescent, _adult };
            string first = Report(new[] { _type }, pool);
            Assert.That(Report(new[] { _type }, pool), Is.EqualTo(first));
            StringAssert.Contains("planDiscrete landingBits\n", first);
            foreach (string row in first.Split('\n'))
            {
                if (row.Length == 0 || row[0] == '#') continue;
                string[] columns = row.Split(' ');
                var stage = (CreatureStage)Array.IndexOf(new[] { "juvenile", "adolescent", "adult" }, columns[1]);
                ulong seed = ulong.Parse(columns[2], CultureInfo.InvariantCulture);
                RunPlan plan = RunPlanner.Generate(new PlanetConfig(640f, float3.zero), seed, _type,
                    stage, pool, _vocabulary, null);
                string expected = math.asuint(plan.LandingDirection.x).ToString("x8") + "," +
                    math.asuint(plan.LandingDirection.y).ToString("x8") + "," + math.asuint(plan.LandingDirection.z).ToString("x8");
                Assert.That(columns[11], Is.EqualTo(expected));
            }
        }

        [Test]
        public void Report_has_one_row_per_boss_stage_and_seed_with_the_stage_radius_and_matching_samples()
        {
            Func<PlanetSize, float> radius = s => s == PlanetSize.Planet ? 960f : 640f;
            var dump = new StringBuilder();
            string report = Report(new[] { _type }, new[] { _juvenile, _adolescent, _adult }, dump, radius);
            var keys = new List<string>();
            foreach (string row in report.Split('\n'))
            {
                if (row.Length == 0 || row[0] == '#') continue;
                string[] columns = row.Split(' ');
                Assert.That(columns.Length, Is.EqualTo(12));
                Assert.That(columns[3], Is.EqualTo(columns[1] == "adult" ? "960" : "640"), row);
                keys.Add(columns[0] + " " + columns[1] + " " + columns[2]);
            }

            var expected = new List<string>();
            foreach (string stage in new[] { "juvenile", "adolescent", "adult" })
            {
                foreach (ulong seed in ReproProbe.Seeds)
                {
                    expected.Add("probe " + stage + " " + seed.ToString(CultureInfo.InvariantCulture));
                }
            }

            Assert.That(keys, Is.EqualTo(expected));
            string[] samples = dump.ToString().TrimEnd('\n').Split('\n');
            Assert.That(samples.Length, Is.EqualTo(expected.Count * ReproProbe.SampleCount));
            Assert.That(samples[ReproProbe.SampleCount].Split(' '), Has.Length.EqualTo(6));
            StringAssert.StartsWith(expected[1] + " 0 ", samples[ReproProbe.SampleCount]);
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
