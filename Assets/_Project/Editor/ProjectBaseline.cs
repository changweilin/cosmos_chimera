using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cosmimera.EditorTools
{
    /// <summary>
    /// Re-applies the project-wide baseline (folder layout, Windows/IL2CPP player settings,
    /// PC + SteamDeck quality tiers) so the configuration is reproducible from code instead of
    /// living only in hand-edited ProjectSettings YAML.
    /// </summary>
    public static class ProjectBaseline
    {
        private const string Root = "Assets/_Project";
        private const string RenderingDir = Root + "/Settings/Rendering";
        private const string InputDir = Root + "/Settings/Input";
        private const string BootstrapDir = Root + "/Bootstrap";
        private const string BootstrapScene = BootstrapDir + "/Bootstrap.unity";

        // REQ: docs/design/vision.md#範圍 — Steam Deck is the performance floor, PC targets 60 fps.
        private const float PcShadowDistance = 150f;
        private const int PcShadowCascades = 4;
        private const int PcMsaa = 4;
        // Deck runs 1280x800 on an APU; shadows and MSAA are the largest GPU costs in URP forward.
        private const float DeckShadowDistance = 60f;
        private const int DeckShadowCascades = 2;
        private const int DeckMsaa = 1;

        /// <summary>Applies the baseline. Safe to run repeatedly.</summary>
        /// <remarks>
        /// Pre: called on the main thread inside the Editor (menu or -executeMethod); no Play Mode.
        /// Post: template content is relocated under Assets/_Project; quality level 0 is "SteamDeck",
        ///       level 1 is "PC" (Standalone default); Standalone uses IL2CPP; product name is COSMIMERA.
        /// Side effects: moves/renames/deletes assets, rewrites ProjectSettings and EditorBuildSettings, saves assets.
        /// </remarks>
        [MenuItem("COSMIMERA/Apply Project Baseline")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[ProjectBaseline] Refusing to run in Play Mode.");
                return;
            }

            RelocateTemplateContent();
            ConfigureQualityTiers();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBaseline] Applied.");
        }

        private static void RelocateTemplateContent()
        {
            EnsureFolder(RenderingDir);
            EnsureFolder(InputDir);
            EnsureFolder(BootstrapDir);

            MoveIfExists("Assets/Settings/PC_RPAsset.asset", RenderingDir + "/PC_RPAsset.asset");
            MoveIfExists("Assets/Settings/PC_Renderer.asset", RenderingDir + "/PC_Renderer.asset");
            // Renamed rather than recreated: AssetDatabase.MoveAsset keeps the GUID, so QualitySettings
            // and the renderer list keep pointing at the same objects.
            MoveIfExists("Assets/Settings/Mobile_RPAsset.asset", RenderingDir + "/SteamDeck_RPAsset.asset");
            MoveIfExists("Assets/Settings/Mobile_Renderer.asset", RenderingDir + "/SteamDeck_Renderer.asset");
            MoveIfExists("Assets/Settings/DefaultVolumeProfile.asset", RenderingDir + "/DefaultVolumeProfile.asset");
            MoveIfExists("Assets/Settings/SampleSceneProfile.asset", RenderingDir + "/BootstrapVolumeProfile.asset");
            MoveIfExists("Assets/Settings/UniversalRenderPipelineGlobalSettings.asset",
                RenderingDir + "/UniversalRenderPipelineGlobalSettings.asset");
            MoveIfExists("Assets/InputSystem_Actions.inputactions", InputDir + "/InputSystem_Actions.inputactions");
            MoveIfExists("Assets/Scenes/SampleScene.unity", BootstrapScene);

            DeleteIfEmptyOrTemplate("Assets/Settings");
            DeleteIfEmptyOrTemplate("Assets/Scenes");
            DeleteIfEmptyOrTemplate("Assets/TutorialInfo");
            if (AssetExists("Assets/Readme.asset")) AssetDatabase.DeleteAsset("Assets/Readme.asset");

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapScene, true) };
        }

        private static void ConfigureQualityTiers()
        {
            var deck = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderingDir + "/SteamDeck_RPAsset.asset");
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderingDir + "/PC_RPAsset.asset");
            if (deck == null || pc == null)
            {
                Debug.LogError("[ProjectBaseline] URP assets missing; quality tiers not configured.");
                return;
            }

            ApplyTier(pc, PcShadowDistance, PcShadowCascades, PcMsaa);
            ApplyTier(deck, DeckShadowDistance, DeckShadowCascades, DeckMsaa);

            // QualitySettings exposes no API to rename a level; the serialized asset is the only route.
            var qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qualityAsset.Length == 0)
            {
                Debug.LogError("[ProjectBaseline] QualitySettings.asset not found.");
                return;
            }

            var so = new SerializedObject(qualityAsset[0]);
            var levels = so.FindProperty("m_QualitySettings");
            if (levels == null || levels.arraySize != 2)
            {
                Debug.LogError("[ProjectBaseline] Expected exactly 2 quality levels from the URP template; leaving as-is.");
                return;
            }

            SetLevel(levels.GetArrayElementAtIndex(0), "SteamDeck", deck);
            SetLevel(levels.GetArrayElementAtIndex(1), "PC", pc);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyTier(UniversalRenderPipelineAsset asset, float shadowDistance, int cascades, int msaa)
        {
            asset.shadowDistance = shadowDistance;
            asset.shadowCascadeCount = cascades;
            asset.msaaSampleCount = msaa;
            asset.renderScale = 1f;
            EditorUtility.SetDirty(asset);
        }

        private static void SetLevel(SerializedProperty level, string name, RenderPipelineAsset pipeline)
        {
            level.FindPropertyRelative("name").stringValue = name;
            level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.productName = "COSMIMERA";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static bool AssetExists(string path) =>
            !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets));

        private static void MoveIfExists(string from, string to)
        {
            if (!AssetExists(from) || AssetExists(to)) return;
            var error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error)) Debug.LogError($"[ProjectBaseline] Move {from} -> {to} failed: {error}");
        }

        private static void DeleteIfEmptyOrTemplate(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            // TutorialInfo is template-only content; the other folders are deleted only once emptied by the moves above.
            var isTemplate = folder == "Assets/TutorialInfo";
            if (isTemplate || AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
