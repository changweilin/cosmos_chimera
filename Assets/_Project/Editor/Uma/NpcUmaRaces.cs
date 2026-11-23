using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Newtonsoft.Json.Linq;
using UMA;
using UMA.Editors.ModelToRace;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cosmimera.EditorTools
{
    /// <summary>Builds the NPC UMA races from the per-race base FBX (ADR-0015, docs/design/npc-uma-looks.md#種族基底).</summary>
    public static class NpcUmaRaces
    {
        /// <summary>Folder holding the race tables, the base FBX and the generated races.</summary>
        public const string Root = "Assets/_Project/Art/Characters/_UMA/Npc";

        /// <summary>Folder of the generated races, one subfolder per base id.</summary>
        public const string RacesDir = Root + "/Races";

        const string BasesDir = Root + "/Bases";

        // UMA reserves "Root" for the avatar container it creates above "Global"; the MPFB game_engine rig names its
        // top bone Root as well, so the import renames it. Nothing in the bases is weighted to it.
        /// <summary>Name the bases' top bone takes inside UMA.</summary>
        public const string RigRoot = "Rig_Root";

        static string[] BasePaths() =>
            Directory.Exists(BasesDir)
                ? Directory.GetFiles(BasesDir, "*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray()
                : new string[0];

        /// <summary>Sets every base FBX to a readable Humanoid model built from its own bind pose.</summary>
        /// <remarks>
        /// Pre: base FBX files sit in _UMA/Npc/Bases (forge npc_bases.py bake).
        /// Post: each base imports with AnimationType.Human, CreateFromThisModel, Read/Write and shape keys on, no
        /// animation; logs avatar validity.
        /// Side effects: rewrites the FBX .meta files and reimports them.
        /// </remarks>
        public static void Configure()
        {
            foreach (var path in BasePaths())
            {
                if (AssetImporter.GetAtPath(path) is not ModelImporter mi)
                {
                    Debug.LogError($"[NpcUmaRaces] not a model: {path}");
                    continue;
                }
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false;
                // UMA's slot builder copies vertices and shape keys off the mesh.
                mi.isReadable = true;
                mi.importBlendShapes = true;
                mi.SaveAndReimport();
                var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
                Debug.Log($"[NpcUmaRaces] {Path.GetFileNameWithoutExtension(path)} avatar valid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman}");
            }
        }

        /// <summary>Rebuilds one UMA race per base FBX, with one DNA per channel of NpcMeshes.UmaChannels.</summary>
        /// <remarks>
        /// Pre: Configure() has run; race_bases.json and race_keys.json are current (UmaRaceBaseTests pass).
        /// Post: Races/&lt;id&gt;/ holds race &lt;id&gt; (body slots carrying every baked key, TPose, base recipe) and
        /// &lt;id&gt;_DNA, whose DNA default to the race mean and drive each "&lt;channel&gt;+"/"-" key at weight
        /// (value − mean) / per; the UMA Global Library is rebuilt. Logs every channel that drives nothing.
        /// Side effects: deletes and recreates Races/ (asset GUIDs change); rewrites the UMA asset index.
        /// Throws when a base fails UMA's import checks.
        /// </remarks>
        public static void Build()
        {
            var bases = Load("race_bases.json");
            var keys = Load("race_keys.json");
            if (AssetDatabase.IsValidFolder(RacesDir))
                AssetDatabase.DeleteAsset(RacesDir);
            AssetDatabase.CreateFolder(Root, "Races");
            var built = new List<(string id, RaceData race)>();
            foreach (var path in BasePaths())
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (bases[id] == null || keys[id] == null)
                    throw new InvalidOperationException($"{id}: missing from race_bases.json or race_keys.json");
                built.Add((id, BuildRace(path, id)));
            }
            AssetDatabase.SaveAssets();
            // The base recipe names its slots; they resolve only once the library indexes them, and AddDna re-saves it.
            UmaLibrary.Rebuild();
            foreach (var (id, race) in built)
            {
                var unused = AddDna(race, id, (JObject)bases[id], (JObject)keys[id]);
                Debug.Log($"[NpcUmaRaces] {id} slots={((UMATextRecipe)race.baseRaceRecipe).PackedLoad().slotsV3.Count(s => !string.IsNullOrEmpty(s.id))} " +
                          $"dna={NpcMeshes.UmaChannels.Count} inert={string.Join(",", unused)}");
            }
            AssetDatabase.SaveAssets();
        }

        static JObject Load(string file) =>
            (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Root, file)))["bases"];

        static RaceData BuildRace(string fbxPath, string id)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var source = Object.Instantiate(prefab);
            source.hideFlags = HideFlags.HideAndDontSave;
            source.name = id;
            try
            {
                foreach (var t in source.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Root" && t != source.transform)
                        t.name = RigRoot;
                var plan = new ModelToRacePlan
                {
                    source = source, mode = ModelImportMode.NewRace, outputParent = RacesDir,
                    registerAssets = false, createAvatarPrefab = false,
                };
                plan.Scan();
                plan.name = id;
                // Our DNA replaces the wizard's generic per-bone and per-shape sliders (AddDna); the shapes still ride along.
                plan.boneDNA = false;
                plan.blendshapeDNA = false;
                foreach (var s in plan.shapes)
                    s.include = true;
                return ModelToRaceBuilder.Build(plan).race;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        // Returns the channels that got no effect: bone, slot and shader channels (built later) and keys the bake lacks.
        static List<string> AddDna(RaceData race, string id, JObject row, JObject baked)
        {
            var group = ScriptableObject.CreateInstance<DNAGroup>();
            group.name = id + "_DNA";
            group.DNAArea = "NPC";
            AssetDatabase.CreateAsset(group, $"{RacesDir}/{id}/{group.name}.asset");
            var inert = new List<string>();
            foreach (var c in NpcMeshes.UmaChannels)
            {
                var dna = ScriptableObject.CreateInstance<DNA>();
                dna.name = c.Name;
                dna.displayName = c.Name;
                float mean = (float)((JArray)row[c.Name])[0];
                dna.defaultValue = Mathf.Clamp01(c.Dna01(mean));
                dna.description = $"{c.Source}; 0..1 spans {c.Min}..{c.Max}; race mean {mean}";
                var k = (JObject)baked[c.Name];
                foreach (bool plus in new[] { true, false })
                {
                    var side = (JObject)k?[plus ? "plus" : "minus"];
                    var perToken = side?["per"];
                    if (perToken == null || perToken.Type == JTokenType.Null || (float)perToken <= 0f)
                        continue;
                    var effect = NpcUmaDna.KeyEffect(c, mean, (float)perToken, plus);
                    if (effect != null)
                        dna.effects.Add(effect);
                }
                if (dna.effects.Count == 0)
                    inert.Add(c.Name);
                AssetDatabase.AddObjectToAsset(dna, group);
                group.dnaList.Add(dna);
            }
            EditorUtility.SetDirty(group);
            race.DNACollection.DNAGroups.Add(group);
            race.DNACollection.Reset();
            EditorUtility.SetDirty(race);
            // The wizard saved the base recipe before this DNA existed; re-save it so new avatars start at the race mean.
            var recipe = new UMAData.UMARecipe();
            recipe.SetRace(race);
            race.baseRaceRecipe.Load(recipe);
            if (recipe.slotDataList.Length == 0 || recipe.slotDataList.Any(x => x == null))
                throw new InvalidOperationException($"{id}: base recipe slots do not resolve; is {Root} in the UMA library's folders?");
            recipe.dnaInstanceCollection = race.DNACollection.GetDefaultDNA(race);
            ((UMATextRecipe)race.baseRaceRecipe).Save(recipe);
            EditorUtility.SetDirty(race.baseRaceRecipe);
            return inert;
        }
    }

    /// <summary>How an NPC DNA value maps to the baked keys of one race base (docs/design/npc-uma-looks.md#種族基底).</summary>
    public static class NpcUmaDna
    {
        /// <summary>Effect driving key "&lt;channel&gt;+" (or "-") at weight (value − mean) / per on its side of the mean.</summary>
        /// <remarks>
        /// Why a curve: UMA maps a DNA value through one curve per effect, so a two-segment linear curve with its knee
        /// at the race mean gives zero on the far side and the exact linear weight on the near side.
        /// Pre: per &gt; 0. Post: null when the mean sits at that side's end of the range (the key can never show);
        /// otherwise the curve evaluates to the weight (0..1 = 0..100 %) for any DNA value.
        /// Side effects: allocates.
        /// </remarks>
        internal static DNAEffect_BlendShape KeyEffect(NpcMeshes.UmaChannel c, float mean, float per, bool plus)
        {
            float d0 = Mathf.Clamp01(c.Dna01(mean));
            var curve = new AnimationCurve();
            if (plus)
            {
                if (d0 >= 1f)
                    return null;
                float end = (c.Max - mean) / per;
                float slope = end / (1f - d0);
                if (d0 > 0f)
                {
                    curve.AddKey(new Keyframe(0f, 0f, 0f, 0f));
                    curve.AddKey(new Keyframe(d0, 0f, 0f, slope));
                }
                else
                {
                    curve.AddKey(new Keyframe(0f, 0f, slope, slope));
                }
                curve.AddKey(new Keyframe(1f, end, slope, slope));
            }
            else
            {
                if (d0 <= 0f)
                    return null;
                float start = (mean - c.Min) / per;
                float slope = -start / d0;
                curve.AddKey(new Keyframe(0f, start, slope, slope));
                if (d0 < 1f)
                {
                    curve.AddKey(new Keyframe(d0, 0f, slope, 0f));
                    curve.AddKey(new Keyframe(1f, 0f, 0f, 0f));
                }
                else
                {
                    curve.AddKey(new Keyframe(1f, 0f, slope, slope));
                }
            }
            return new DNAEffect_BlendShape
            {
                BlendShapeName = c.Name + (plus ? "+" : "-"),
                EffectName = c.Name + (plus ? "+" : "-"),
                curve = curve,
                minMapping = 0f,
                maxMapping = 1f,
            };
        }
    }
}
