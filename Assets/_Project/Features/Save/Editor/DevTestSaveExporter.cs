using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Save.Editor
{
    /// <summary>Exports built-in developer test saves to Data/TestSaves/*.json.</summary>
    /// <remarks>
    /// REQ: docs/design/lobby-meta.md#進度保存.
    /// Pre: DevTestSaves.All contains valid presets.
    /// Post: Data/TestSaves/*.json files exist and match DevTestSaves.All.
    /// Side effects: creates Data/TestSaves directory and writes UTF-8 JSON files.
    /// </remarks>
    public static class DevTestSaveExporter
    {
        /// <summary>Writes all <see cref="DevTestSaves.All"/> presets to <c>Data/TestSaves/</c>.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: all preset JSON files written to Data/TestSaves/.
        /// Side effects: filesystem write under project root Data/TestSaves/.
        /// </remarks>
        [MenuItem("Cosmimera/Export Dev Test Saves")]
        public static void ExportAll()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "TestSaves"));
            Directory.CreateDirectory(dir);
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            foreach (DevTestSavePreset preset in DevTestSaves.All)
            {
                string path = Path.Combine(dir, preset.FileName);
                File.WriteAllText(path, preset.Build().ToJson(), utf8NoBom);
            }

            Debug.Log($"DevTestSaveExporter: exported {DevTestSaves.All.Count} test saves to {dir}");
        }
    }
}
