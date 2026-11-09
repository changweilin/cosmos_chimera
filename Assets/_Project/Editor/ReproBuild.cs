using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Cosmimera.EditorTools
{
    /// <summary>
    /// P3-10 player builds for the reproducibility probe: Windows x64 Mono and IL2CPP of the Demo scene.
    /// REQ: docs/roadmap/phase-3.md#P3-10, docs/open-questions.md#Q-T6 ("改契約前先在 P3-10 實測 IL2CPP x64").
    /// </summary>
    /// <remarks>
    /// Pre: batchmode; Demo.unity in the build settings. Post: Builds/Repro/&lt;backend&gt;/Cosmimera.exe.
    /// Side effects: writes Builds/; temporarily switches the Standalone scripting backend and restores it, so the
    /// committed ProjectSettings never changes.
    /// </remarks>
    public static class ReproBuild
    {
        /// <summary>Batchmode entry: <c>-executeMethod Cosmimera.EditorTools.ReproBuild.BuildAll</c>.</summary>
        /// <remarks>Pre: as the class. Post: outside batchmode throws before any side effects; otherwise both players
        /// built, or Editor exits 1. Side effects: as the class.</remarks>
        public static void BuildAll() => BuildAll(UnityEngine.Application.isBatchMode);

        private static void BuildAll(bool isBatchMode)
        {
            if (!isBatchMode)
            {
                throw new InvalidOperationException("ReproBuild.BuildAll requires batchmode");
            }

            NamedBuildTarget target = NamedBuildTarget.Standalone;
            ScriptingImplementation previous = PlayerSettings.GetScriptingBackend(target);
            int code = 0;
            try
            {
                code |= Build(ScriptingImplementation.Mono2x, "mono");
                code |= Build(ScriptingImplementation.IL2CPP, "il2cpp");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                code = 1;
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, previous);
            }

            EditorApplication.Exit(code);
        }

        private static int Build(ScriptingImplementation backend, string folder)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Bootstrap/Demo.unity" },
                locationPathName = "Builds/Repro/" + folder + "/Cosmimera.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            UnityEngine.Debug.Log("ReproBuild " + folder + ": " + report.summary.result + ", " + report.summary.totalErrors
                + " errors");
            return report.summary.result == BuildResult.Succeeded ? 0 : 1;
        }
    }
}
