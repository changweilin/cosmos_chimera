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
                var unused = AddDna(race, id, (JObject)bases[id], (JObject)keys[id], $"{BasesDir}/{id}.fbx");
                Debug.Log($"[NpcUmaRaces] {id} slots={((UMATextRecipe)race.baseRaceRecipe).PackedLoad().slotsV3.Count(s => !string.IsNullOrEmpty(s.id))} " +
                          $"dna={NpcMeshes.UmaChannels.Count} inert={string.Join(",", unused)}");
            }
            AssetDatabase.SaveAssets();
        }

        static JObject Load(string file) =>
            (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Root, file)))["bases"];

        // REQ: npc-uma-looks.md#眼睛 — the baked iris texture is one painted brown eye for every base; the eye shader
        // draws each resident's type, size and colours from parameters instead.
        static void UseEyeShader(string id)
        {
            var shader = Shader.Find("Cosmimera/NpcEye") ?? throw new InvalidOperationException("Cosmimera/NpcEye shader not found");
            var eyes = AssetDatabase.FindAssets("t:Material", new[] { $"{RacesDir}/{id}" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileName(p).Contains("_eye_")).ToList();
            if (eyes.Count != 1)
                throw new InvalidOperationException($"{id}: expected one eye material, found {eyes.Count}");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(eyes[0]);
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
        }

        // Face decals are flat strips on the skin: an inverted hull around them would draw black rims.
        static readonly string[] NoOutline = { "sclera", "brow", "lash", "mouth", "nose" };

        // The wizard's materials are URP Lit, which renders the skin darker and flatter than the cel-shaded scene. The
        // base's cel table (forge race.py, <id>_materials.json) carries the authored base, shade and band count; the
        // conversion is the hero importer's (AcgCharacterImport.RemapToonMaterials), which lives in another assembly.
        static void UseToonMaterials(string id)
        {
            var shader = Shader.Find("Cosmimera/ToonOutlined") ?? throw new InvalidOperationException("Cosmimera/ToonOutlined shader not found");
            var table = (JArray)JObject.Parse(File.ReadAllText($"{BasesDir}/{id}_materials.json"))["materials"];
            foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { $"{RacesDir}/{id}" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                string file = Path.GetFileNameWithoutExtension(path);
                var row = table.OfType<JObject>().Where(r => file.EndsWith("_" + (string)r["name"], StringComparison.Ordinal))
                    .OrderByDescending(r => ((string)r["name"]).Length).FirstOrDefault();
                if (row == null)
                    throw new InvalidOperationException($"{id}: {file} has no row in {id}_materials.json");
                string name = (string)row["name"];
                if (name.StartsWith("eye_", StringComparison.Ordinal))
                    continue;
                if (!ColorUtility.TryParseHtmlString((string)row["base"], out var lit))
                    throw new InvalidOperationException($"{id}/{name}: bad colour {row["base"]}");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                mat.shader = shader;
                mat.SetColor("_BaseColor", lit);
                // CosmiToon darkens the unlit side to albedo × _Shadow × 0.6.
                var tint = Color.white / 0.6f;
                if (row["shade"]?.Type == JTokenType.String && ColorUtility.TryParseHtmlString((string)row["shade"], out var shade))
                    tint = new Color(ShadeRatio(shade.r, lit.r), ShadeRatio(shade.g, lit.g), ShadeRatio(shade.b, lit.b), 1f);
                mat.SetColor("_Shadow", tint);
                mat.SetFloat("_Bands", Mathf.Max(2, (int?)row["bands"] ?? 2));
                bool bare = NoOutline.Any(k => name.StartsWith(k, StringComparison.Ordinal));
                mat.SetFloat("_OutlineWidth", bare ? 0f : name.StartsWith("skin_", StringComparison.Ordinal) ? 0.0012f : 0.0018f);
                mat.SetColor("_OutlineColor", new Color(0.16f, 0.11f, 0.12f, 1f));
                mat.SetFloat("_OutlineDepthBias", 0.006f);
                EditorUtility.SetDirty(mat);
            }
        }

        static float ShadeRatio(float shade, float lit) => Mathf.Clamp(shade / Mathf.Max(lit, 0.02f) / 0.6f, 0f, 1.7f);

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
                var race = ModelToRaceBuilder.Build(plan).race;
                UseEyeShader(id);
                UseToonMaterials(id);
                return race;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        // Returns the channels that got no effect: slot and shader channels, bone channels another channel carries, and
        // keys the bake lacks.
        static List<string> AddDna(RaceData race, string id, JObject row, JObject baked, string fbxPath)
        {
            var rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
            rig.hideFlags = HideFlags.HideAndDontSave;
            var bones = rig.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            float heightM = (float)((JArray)row["body.height"])[0];
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
                try
                {
                    dna.effects.AddRange(NpcUmaDna.BoneEffects(c, mean, heightM, bones));
                }
                catch (KeyNotFoundException e)
                {
                    throw new InvalidOperationException($"{id}: {c.Name} needs a bone the base lacks", e);
                }

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
            Object.DestroyImmediate(rig);
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
        static readonly string[] Sides = { "_l", "_r" };

        /// <summary>Bone effects of one bone channel on a race base; empty for every other channel.</summary>
        /// <remarks>
        /// REQ: docs/design/npc-uma-looks.md#骨骼通道. Every effect is zero at the race mean (the base already stands
        /// there) and follows the procedural rig's ratio for any other value: lengths and widths move child joints
        /// (translation, so the skin between joints stretches instead of shearing), thickness and size scale bones.
        /// Pre: bones maps the base's bone names to an instance of the base FBX at the origin; heightM is the race mean
        /// height. Post: each effect maps DNA 0..1 through its curve to the amount. Side effects: allocates.
        /// Throws KeyNotFoundException when a needed bone is missing.
        /// </remarks>
        internal static List<DNAEffect> BoneEffects(NpcMeshes.UmaChannel c, float mean, float heightM, IReadOnlyDictionary<string, Transform> bones)
        {
            var effects = new List<DNAEffect>();
            Func<float, float> ratio = v => v / mean - 1f;
            switch (c.Name)
            {
                // The rig's top bone sits on the ground, so scaling it keeps the feet there.
                case "body.height":
                    effects.Add(Scale(NpcUmaRaces.RigRoot, Vector3.one, c, ratio));
                    break;
                case "body.headRatio":
                    effects.Add(Scale(bones["head"].name, Vector3.one, c, ratio));
                    break;
                case "body.legShare":
                    foreach (var s in Sides)
                    {
                        effects.Add(Lengthen(bones["calf" + s], c, ratio));
                        effects.Add(Lengthen(bones["foot" + s], c, ratio));
                    }
                    break;
                case "body.armLength":
                    foreach (var s in Sides)
                    {
                        effects.Add(Lengthen(bones["lowerarm" + s], c, ratio));
                        effects.Add(Lengthen(bones["hand" + s], c, ratio));
                    }
                    break;
                case "body.hipWidth":
                    foreach (var s in Sides)
                    {
                        var thigh = bones["thigh" + s];
                        var outward = new Vector3(Mathf.Sign(thigh.position.x) * heightM, 0f, 0f);
                        effects.Add(Translate(thigh, thigh.parent.InverseTransformVector(outward), c, v => v - mean));
                    }
                    break;
                // Shoulder width rides on the chest: the rig sets the trunk to 0.92 of the shoulder span, so widening the
                // chest carries the clavicles out with it. The bones above take the inverse back, or the neck, head and
                // arms would widen too.
                case "body.trunkWidth":
                {
                    var chest = bones["spine_01"];
                    effects.Add(Scale(chest.name, Axis(chest, Vector3.right), c, ratio));
                    foreach (var child in Above(chest))
                        effects.Add(Scale(child.name, Axis(child, Vector3.right), c, v => mean / v - 1f));
                    break;
                }
                // Rig.Bulk is the build left after muscle and fat, which have keys of their own; limb radii scale with it.
                case "body.bulk":
                    foreach (var s in Sides)
                    {
                        foreach (var limb in new[] { bones["thigh" + s], bones["upperarm" + s] })
                        {
                            Vector3 along = Axis(limb, limb.GetChild(0).position - limb.position);
                            effects.Add(Scale(limb.name, Vector3.one - along, c, ratio));
                        }
                    }
                    break;
                case "spine.lean":
                {
                    var spine = bones["spine_01"];
                    effects.Add(new DNAEffect_BoneRotate
                    {
                        BoneName = spine.name, EffectName = c.Name, RotationAxis = Quaternion.Inverse(spine.rotation) * Vector3.right,
                        RotationAngle = Mathf.Rad2Deg, curve = Curve(c, v => v - mean), minMapping = 0f, maxMapping = 1f,
                    });
                    break;
                }
            }
            return effects;
        }

        // The bones resting on the top of the spine chain (neck, clavicles).
        static IEnumerable<Transform> Above(Transform spine)
        {
            var t = spine;
            while (t.Cast<Transform>().Any(x => x.name.StartsWith("spine", StringComparison.Ordinal)))
                t = t.Cast<Transform>().First(x => x.name.StartsWith("spine", StringComparison.Ordinal));
            return t.Cast<Transform>().Where(x => x != spine).ToList();
        }

        // The local axis of `bone` closest to a world direction.
        static Vector3 Axis(Transform bone, Vector3 world)
        {
            Vector3 l = bone.InverseTransformDirection(world);
            Vector3 a = new Vector3(Mathf.Abs(l.x), Mathf.Abs(l.y), Mathf.Abs(l.z));
            return a.x >= a.y && a.x >= a.z ? Vector3.right : a.y >= a.z ? Vector3.up : Vector3.forward;
        }

        static DNAEffect_BoneScale Scale(string bone, Vector3 axes, NpcMeshes.UmaChannel c, Func<float, float> amount) =>
            new DNAEffect_BoneScale { BoneName = bone, EffectName = c.Name, ScaleFactor = axes, curve = Curve(c, amount), minMapping = 0f, maxMapping = 1f };

        // Moves a joint along its own offset from the parent: offset × (1 + amount).
        static DNAEffect_BoneTranslate Lengthen(Transform joint, NpcMeshes.UmaChannel c, Func<float, float> amount) =>
            Translate(joint, joint.localPosition, c, amount);

        static DNAEffect_BoneTranslate Translate(Transform joint, Vector3 perUnit, NpcMeshes.UmaChannel c, Func<float, float> amount) =>
            new DNAEffect_BoneTranslate { BoneName = joint.name, EffectName = c.Name, Translation = perUnit, curve = Curve(c, amount), minMapping = 0f, maxMapping = 1f };

        // Samples amount(value) over the channel's DNA range; 16 linear segments keep 1/x within 0.2 % of exact.
        static AnimationCurve Curve(NpcMeshes.UmaChannel c, Func<float, float> amount)
        {
            const int N = 16;
            var keys = new Keyframe[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float x = i / (float)N;
                keys[i] = new Keyframe(x, amount(c.Min + x * (c.Max - c.Min)));
            }
            for (int i = 0; i <= N; i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(N, i + 1);
                float slope = (keys[b].value - keys[a].value) / (keys[b].time - keys[a].time);
                keys[i].inTangent = keys[i].outTangent = slope;
            }
            return new AnimationCurve(keys);
        }

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
