using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UMA;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: ADR-0015, docs/design/npc-uma-looks.md#種族基底 — the built UMA races must carry every baked key and their DNA must
    // drive each key at weight (value − race mean) / per, the weight UmaRaceBaseTests.BakedKeysReachAndShowEveryResident
    // gates, so residents look on the race what the sampling drew.
    public sealed class UmaNpcRaceTests
    {
        private const string Root = "Assets/_Project/Art/Characters/_UMA/Npc";
        private const int Residents = 200;
        // Curves are evaluated in float; a weight off by 0.1 % of a key is far below one surface millimetre.
        private const float WeightTolerance = 1e-3f;

        private static JObject Table(string file) =>
            (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Root, file)))["bases"];

        private static RaceData RaceOf(string id) => AssetDatabase.LoadAssetAtPath<RaceData>($"{Root}/Races/{id}/{id}.asset");

        private static IEnumerable<(NpcRace race, NpcSex sex)> Bases() =>
            UmaRaceBaseTests.Races().SelectMany(r => new[] { (r, NpcSex.Male), (r, NpcSex.Female) });

        [Test]
        public void EveryBaseHasARaceWithOneDnaPerChannel()
        {
            Assume.That(Directory.Exists($"{Root}/Races"), Is.True,
                "NPC UMA races not built; run tools/build-npc-races");
            var problems = new List<string>();
            foreach ((NpcRace race, NpcSex sex) in Bases())
            {
                string id = UmaRaceBaseTests.BaseId(race, sex);
                RaceData data = RaceOf(id);
                if (data == null)
                {
                    problems.Add(id + ": no race; run Cosmimera.EditorTools.NpcUmaRaces.Build");
                    continue;
                }

                data.DNACollection.LoadDictionary();
                foreach (NpcMeshes.UmaChannel c in NpcMeshes.UmaChannels)
                {
                    if (!data.DNACollection.HasDNA(c.Name))
                    {
                        problems.Add($"{id}: no DNA {c.Name}");
                    }
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        // The DNA default is the race mean, so a fresh avatar is the base body with every key at zero, and each resident's
        // DNA value lands each key at the weight the bake measured for it.
        [Test]
        public void DnaDrivesEveryKeyAtTheBakedWeight()
        {
            JObject bases = Table("race_bases.json");
            JObject keys = Table("race_keys.json");
            var problems = new List<string>();
            foreach ((NpcRace race, NpcSex sex) in Bases())
            {
                string id = UmaRaceBaseTests.BaseId(race, sex);
                RaceData data = RaceOf(id);
                Assume.That(data, Is.Not.Null, id + ": no race; see EveryBaseHasARaceWithOneDnaPerChannel");
                data.DNACollection.LoadDictionary();
                var row = (JObject)bases[id];
                var baked = (JObject)keys[id];
                var rng = new Pcg32(0x554D41ul ^ ((ulong)race << 8) ^ (ulong)sex, 0x524143ul);
                var residents = Enumerable.Range(0, Residents)
                    .Select(_ => NpcMeshes.UmaRecipeOf(UmaRaceBaseTests.SpecOf(ref rng, race, sex, false))).ToArray();
                for (int i = 0; i < NpcMeshes.UmaChannels.Count; i++)
                {
                    NpcMeshes.UmaChannel c = NpcMeshes.UmaChannels[i];
                    float mean = (float)((JArray)row[c.Name])[0];
                    DNA dna = data.DNACollection.dnaDictionary[c.Name];
                    if (Mathf.Abs(dna.defaultValue - Mathf.Clamp01(c.Dna01(mean))) > WeightTolerance)
                    {
                        problems.Add($"{id} {c.Name}: default {dna.defaultValue} is not the race mean");
                    }

                    foreach (bool plus in new[] { true, false })
                    {
                        var per = baked[c.Name]?[plus ? "plus" : "minus"]?["per"];
                        string key = c.Name + (plus ? "+" : "-");
                        var effect = dna.effects.OfType<DNAEffect_BlendShape>().SingleOrDefault(e => e.BlendShapeName == key);
                        if (per == null || per.Type == JTokenType.Null)
                        {
                            if (effect != null)
                            {
                                problems.Add($"{id} {key}: drives a key the bake did not make");
                            }

                            continue;
                        }

                        foreach (NpcUmaRecipe r in residents)
                        {
                            float v = r.Dna(i);
                            float side = plus ? v - mean : mean - v;
                            float want = side > 0f ? side / (float)per : 0f;
                            float got = effect == null ? 0f : effect.GetMappedValue(Mathf.Clamp01(c.Dna01(v)));
                            if (Mathf.Abs(got - want) > WeightTolerance)
                            {
                                problems.Add($"{id} {key}: value {v} drives {got:0.0000}, wants {want:0.0000}");
                                break;
                            }
                        }
                    }
                }
            }

            Assert.That(problems.Take(40), Is.Empty, $"{problems.Count} problems:\n" + string.Join("\n", problems.Take(40)));
        }

        // Generating through UMA proves the slots carry the keys onto the combined mesh and the DNA reaches them; one
        // humanoid-headed and one beast-headed base keep the run short (each body mesh holds about 70 keys).
        [TestCase(NpcRace.Human, NpcSex.Female)]
        [TestCase(NpcRace.Werewolf, NpcSex.Male)]
        public void GeneratedAvatarShowsTheResidentsKeys(NpcRace race, NpcSex sex)
        {
            string id = UmaRaceBaseTests.BaseId(race, sex);
            RaceData data = RaceOf(id);
            Assume.That(data, Is.Not.Null, id + ": no race; see EveryBaseHasARaceWithOneDnaPerChannel");
            var rng = new Pcg32(0x47454Eul, (ulong)race);
            NpcUmaRecipe resident = NpcMeshes.UmaRecipeOf(UmaRaceBaseTests.SpecOf(ref rng, race, sex, false));
            var go = new GameObject("npc race test");
            var gen = UMAAssetIndexer.Instance.Generator;
            var settings = ScriptableObject.CreateInstance<UMARendererAsset>();
            // Same harness as UMA's own ModelToRaceTests: the transient editor generator has no combiner or texture merge.
            var textureMerge = ScriptableObject.CreateInstance<TextureMerge>();
            var oldCombiner = gen.meshCombiner;
            var oldRenderer = gen.defaultRendererAsset;
            var oldMerge = gen.textureMerge;
            var combiner = gen.gameObject.AddComponent<UMADefaultMeshCombiner>();
            gen.meshCombiner = combiner;
            gen.defaultRendererAsset = settings;
            gen.textureMerge = textureMerge;
            try
            {
                var uma = go.AddComponent<UMAData>();
                uma.defaultRendererAsset = settings;
                uma.umaRecipe = new UMAData.UMARecipe();
                uma.umaRecipe.SetRace(data);
                data.baseRaceRecipe.Load(uma.umaRecipe);
                uma.umaRecipe.dnaInstanceCollection = data.DNACollection.GetDefaultDNA(data);
                uma.blendShapeSettings.ignoreBlendShapes = false;
                uma.blendShapeSettings.loadAllFrames = true;
                uma.isMeshDirty = uma.isTextureDirty = uma.isShapeDirty = true;
                Assert.That(gen.GenerateSingleUMA(uma, false), id + " did not generate");
                SkinnedMeshRenderer body = uma.GetRenderers().OrderByDescending(r => r.sharedMesh.vertexCount).First();
                data.DNACollection.LoadDictionary();
                int checkedKeys = 0;
                for (int i = 0; i < NpcMeshes.UmaChannels.Count; i++)
                {
                    NpcMeshes.UmaChannel c = NpcMeshes.UmaChannels[i];
                    DNA dna = data.DNACollection.dnaDictionary[c.Name];
                    foreach (DNAEffect_BlendShape e in dna.effects.OfType<DNAEffect_BlendShape>())
                    {
                        int index = body.sharedMesh.GetBlendShapeIndex(e.BlendShapeName);
                        if (index < 0)
                        {
                            continue;
                        }

                        float value = Mathf.Clamp01(c.Dna01(resident.Dna(i)));
                        dna.PostApply(uma, value);
                        Assert.That(body.GetBlendShapeWeight(index), Is.EqualTo(100f * e.GetMappedValue(value)).Within(0.01f),
                            $"{id} {e.BlendShapeName}");
                        checkedKeys++;
                    }
                }

                Assert.That(checkedKeys, Is.GreaterThan(40), id + ": too few keys reached the body mesh");
            }
            finally
            {
                Object.DestroyImmediate(go);
                gen.meshCombiner = oldCombiner;
                gen.defaultRendererAsset = oldRenderer;
                gen.textureMerge = oldMerge;
                Object.DestroyImmediate(combiner);
                Object.DestroyImmediate(textureMerge);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
