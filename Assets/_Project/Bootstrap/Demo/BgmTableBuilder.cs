#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Builds and updates the BgmTable ScriptableObject from Assets/_Project/Audio/BGM.
    /// </summary>
    public static class BgmTableBuilder
    {
        public const string BgmDirectory = "Assets/_Project/Audio/BGM";
        public const string TableAssetPath = "Assets/_Project/Audio/BgmTable.asset";

        [MenuItem("COSMIMERA/Build BGM Table")]
        public static void Build()
        {
            if (!Directory.Exists(BgmDirectory))
            {
                Debug.LogWarning($"[BgmTableBuilder] Directory not found: {BgmDirectory}");
                return;
            }

            AssetDatabase.Refresh();

            var entries = new List<BgmEntry>();
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { BgmDirectory });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                string filename = Path.GetFileNameWithoutExtension(path);
                entries.Add(new BgmEntry(filename, clip));
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            var table = AssetDatabase.LoadAssetAtPath<BgmTable>(TableAssetPath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<BgmTable>();
                table.SetEntries(entries);
                AssetDatabase.CreateAsset(table, TableAssetPath);
            }
            else
            {
                table.SetEntries(entries);
                EditorUtility.SetDirty(table);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[BgmTableBuilder] Updated {TableAssetPath} with {entries.Count} tracks.");
        }
    }
}
#endif
