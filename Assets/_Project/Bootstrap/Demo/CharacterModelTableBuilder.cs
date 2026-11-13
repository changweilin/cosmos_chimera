#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Builds provisional character AnimatorControllers, prefabs, and <see cref="CharacterModelTable"/>
    /// from <c>Assets/_Project/Art/Characters/</c>.
    /// </summary>
    public static class CharacterModelTableBuilder
    {
        public const string CharactersDirectory = "Assets/_Project/Art/Characters";
        public const string TableAssetPath = "Assets/_Project/Art/Characters/CharacterModelTable.asset";

        private static readonly string[] LoopingClipNames =
        {
            "chr_idle",
            "chr_walk",
            "chr_run",
            "chr_sneak_idle",
            "chr_sneak_move",
        };

        /// <summary>Rebuilds all provisional character controllers, prefabs, and the CharacterModelTable asset.</summary>
        /// <remarks>Pre: editor only. Post: CharacterModelTable.asset updated. Side effects: writes assets and reimports looping clips if needed.</remarks>
        [MenuItem("COSMIMERA/Build Provisional Character Assets")]
        public static void Build()
        {
            if (!Directory.Exists(CharactersDirectory))
            {
                Debug.LogWarning($"[CharacterModelTableBuilder] Directory not found: {CharactersDirectory}");
                return;
            }

            AssetDatabase.Refresh();

            var entries = new List<CharacterModelEntry>();
            string[] charDirs = Directory.GetDirectories(CharactersDirectory);
            Array.Sort(charDirs, StringComparer.Ordinal);

            foreach (string dir in charDirs)
            {
                string charId = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(charId) || charId.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                string assetDir = dir.Replace('\\', '/');
                string modelPath = $"{assetDir}/Model/{charId}.fbx";
                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (modelAsset == null)
                {
                    continue;
                }

                ReadMetadata(assetDir, charId, out string displayName, out string sourceTaskId, out bool flipForward);
                EnsureLoopSettings(assetDir);

                AnimatorController controller = BuildController(assetDir, charId);
                GameObject prefab = BuildPrefab(assetDir, charId, modelAsset, controller, flipForward);

                entries.Add(new CharacterModelEntry(
                    charId,
                    displayName,
                    prefab,
                    controller,
                    isProvisional: true,
                    sourceTaskId));
            }

            var table = AssetDatabase.LoadAssetAtPath<CharacterModelTable>(TableAssetPath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<CharacterModelTable>();
                table.SetEntries(entries);
                AssetDatabase.CreateAsset(table, TableAssetPath);
            }
            else
            {
                table.SetEntries(entries);
                EditorUtility.SetDirty(table);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterModelTableBuilder] Updated {TableAssetPath} with {entries.Count} provisional characters.");
        }

        private static void ReadMetadata(string assetDir, string charId,
            out string displayName, out string sourceTaskId, out bool flipForward)
        {
            displayName = charId;
            sourceTaskId = string.Empty;
            flipForward = false;

            string rigSpecPath = $"{assetDir}/Model/rig_spec.json";
            if (File.Exists(rigSpecPath))
            {
                JObject rig = JObject.Parse(File.ReadAllText(rigSpecPath));
                string zh = rig.Value<string>("character_name_zh");
                if (!string.IsNullOrEmpty(zh))
                {
                    displayName = zh;
                }

                string forward = rig["coordinate_system"]?.Value<string>("forward_axis");
                flipForward = string.Equals(forward, "-Z", StringComparison.OrdinalIgnoreCase);
            }

            string manifestPath = $"{assetDir}/manifest.json";
            if (File.Exists(manifestPath))
            {
                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                sourceTaskId = manifest.Value<string>("task_id") ?? string.Empty;
            }
        }

        private static void EnsureLoopSettings(string assetDir)
        {
            foreach (string clipName in LoopingClipNames)
            {
                string fbxPath = $"{assetDir}/Anim/{clipName}.fbx";
                if (!File.Exists(fbxPath))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    clips = importer.defaultClipAnimations;
                }

                bool changed = false;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (!clips[i].loopTime)
                    {
                        clips[i].loopTime = true;
                        changed = true;
                    }
                }

                if (changed)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                }
            }
        }

        private static AnimatorController BuildController(string assetDir, string charId)
        {
            string controllerPath = $"{assetDir}/Anim/{charId}.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }
            else
            {
                // Clear existing parameters and base layer states so rebuilds stay idempotent.
                while (controller.parameters.Length > 0)
                {
                    controller.RemoveParameter(0);
                }

                AnimatorStateMachine sm = controller.layers[0].stateMachine;
                foreach (ChildAnimatorState child in sm.states)
                {
                    sm.RemoveState(child.state);
                }

                foreach (AnimatorStateTransition t in sm.anyStateTransitions)
                {
                    sm.RemoveAnyStateTransition(t);
                }
            }

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine rootSm = controller.layers[0].stateMachine;

            AnimationClip idleClip = LoadClip($"{assetDir}/Anim/chr_idle.fbx");
            AnimationClip walkClip = LoadClip($"{assetDir}/Anim/chr_walk.fbx");
            AnimationClip runClip = LoadClip($"{assetDir}/Anim/chr_run.fbx");
            AnimationClip jumpClip = LoadClip($"{assetDir}/Anim/chr_jump.fbx");

            AnimatorState idle = rootSm.AddState("Idle");
            idle.motion = idleClip;
            rootSm.defaultState = idle;

            AnimatorState walk = rootSm.AddState("Walk");
            walk.motion = walkClip;

            AnimatorState run = rootSm.AddState("Run");
            run.motion = runClip;

            AnimatorState jump = rootSm.AddState("Jump");
            jump.motion = jumpClip;

            AddConditionTransition(idle, walk, AnimatorConditionMode.Greater, 0.2f, "Speed", 0.1f);
            AddConditionTransition(walk, idle, AnimatorConditionMode.Less, 0.1f, "Speed", 0.1f);
            AddConditionTransition(walk, run, AnimatorConditionMode.Greater, 5.5f, "Speed", 0.12f);
            AddConditionTransition(run, walk, AnimatorConditionMode.Less, 5.0f, "Speed", 0.12f);

            AnimatorStateTransition toJump = rootSm.AddAnyStateTransition(jump);
            toJump.hasExitTime = false;
            toJump.duration = 0.08f;
            toJump.canTransitionToSelf = false;
            toJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");

            AnimatorStateTransition land = jump.AddTransition(idle);
            land.hasExitTime = false;
            land.duration = 0.1f;
            land.AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddConditionTransition(AnimatorState from, AnimatorState to,
            AnimatorConditionMode mode, float threshold, string param, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            t.AddCondition(mode, threshold, param);
        }

        private static AnimationClip LoadClip(string fbxPath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }

        private static GameObject BuildPrefab(string assetDir, string charId, GameObject modelAsset,
            AnimatorController controller, bool flipForward)
        {
            string prefabPath = $"{assetDir}/{charId}_Provisional.prefab";
            var root = new GameObject($"{charId}_Provisional");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                visual.name = "Model";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = flipForward ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

                foreach (Collider col in visual.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(col);
                }

                // Put Animator on the visual (where the skeleton hierarchy lives) so Generic clips bind to bone paths.
                Animator animator = visual.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = visual.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
