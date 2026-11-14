#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Builds <see cref="ConceptPortraitTable"/> inside <c>Assets/_Project/Bootstrap/Demo/</c>
    /// by referencing existing 2D concept art under <c>Assets/_Project/Art/</c> without modifying Art assets.
    /// </summary>
    public static class ConceptPortraitTableBuilder
    {
        public const string CharactersDirectory = "Assets/_Project/Art/Characters";
        public const string CreaturesDirectory = "Assets/_Project/Art/Creatures";
        public const string TableAssetPath = "Assets/_Project/Bootstrap/Demo/ConceptPortraitTable.asset";

        /// <summary>Scans main-game Concept folders and updates <see cref="TableAssetPath"/>.</summary>
        /// <remarks>Pre: editor only. Post: ConceptPortraitTable.asset created/updated and saved. Side effects: writes ConceptPortraitTable.asset.</remarks>
        [MenuItem("COSMIMERA/Build Demo Concept Portrait Table")]
        public static ConceptPortraitTable Build()
        {
            var chars = CollectCharacters();
            var creatures = CollectCreatures();

            var table = AssetDatabase.LoadAssetAtPath<ConceptPortraitTable>(TableAssetPath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<ConceptPortraitTable>();
                table.SetEntries(chars, creatures);
                AssetDatabase.CreateAsset(table, TableAssetPath);
            }
            else
            {
                table.SetEntries(chars, creatures);
                EditorUtility.SetDirty(table);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ConceptPortraitTableBuilder] Updated {TableAssetPath} ({chars.Count} characters, {creatures.Count} creatures).");
            return table;
        }

        private static List<CharacterPortraitEntry> CollectCharacters()
        {
            var list = new List<CharacterPortraitEntry>();
            if (!Directory.Exists(CharactersDirectory))
            {
                return list;
            }

            string[] dirs = Directory.GetDirectories(CharactersDirectory);
            Array.Sort(dirs, StringComparer.Ordinal);
            foreach (string dir in dirs)
            {
                string charId = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(charId) || charId.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                string conceptDir = $"{dir.Replace('\\', '/')}/Concept";
                Texture2D tex = LoadFirstExisting(
                    $"{conceptDir}/{charId}_main.png",
                    $"{conceptDir}/{charId}_front.png");
                if (tex != null)
                {
                    list.Add(new CharacterPortraitEntry(charId, tex));
                }
            }

            return list;
        }

        private static List<CreaturePortraitEntry> CollectCreatures()
        {
            var list = new List<CreaturePortraitEntry>();
            if (!Directory.Exists(CreaturesDirectory))
            {
                return list;
            }

            string[] lineDirs = Directory.GetDirectories(CreaturesDirectory);
            Array.Sort(lineDirs, StringComparer.Ordinal);
            foreach (string lineDir in lineDirs)
            {
                string lineId = Path.GetFileName(lineDir);
                if (string.IsNullOrEmpty(lineId) || lineId.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] stageDirs = Directory.GetDirectories(lineDir);
                Array.Sort(stageDirs, StringComparer.Ordinal);
                foreach (string stageDir in stageDirs)
                {
                    string stageName = Path.GetFileName(stageDir);
                    if (!TryParseStage(stageName, out CreatureStage stage))
                    {
                        continue;
                    }

                    string conceptDir = $"{stageDir.Replace('\\', '/')}/Concept";
                    string prefix = $"cr_{lineId}_{stageName}";
                    Texture2D tex = LoadFirstExisting(
                        $"{conceptDir}/{prefix}_main.png",
                        $"{conceptDir}/{prefix}_front.png");
                    if (tex != null)
                    {
                        list.Add(new CreaturePortraitEntry(lineId, stage, tex));
                    }
                }
            }

            return list;
        }

        private static Texture2D LoadFirstExisting(params string[] assetPaths)
        {
            foreach (string path in assetPaths)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null)
                {
                    return tex;
                }
            }

            return null;
        }

        private static bool TryParseStage(string folderName, out CreatureStage stage)
        {
            switch (folderName.ToLowerInvariant())
            {
                case "juvenile":
                    stage = CreatureStage.Juvenile;
                    return true;
                case "adolescent":
                    stage = CreatureStage.Adolescent;
                    return true;
                case "adult":
                    stage = CreatureStage.Adult;
                    return true;
                default:
                    stage = default;
                    return false;
            }
        }
    }
}
#endif
