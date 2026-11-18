#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using Cosmimera.Bootstrap.Demo;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Bootstrap.Editor
{
    /// <summary>Imports shared encyclopedia drafts for the demo without promoting them to approved data.</summary>
    /// <remarks>Pre: run the data validator first. Post: generated table preserves shared content. Side effects: editor asset I/O.</remarks>
    public static class EncyclopediaImport
    {
        /// <summary>Validates numeric provenance and imports the demo encyclopedia.</summary>
        /// <remarks>Pre: batch and referenced sources exist. Post: references resolve to integers with matching hashes. Side effects: writes the table asset and refreshes Assets.</remarks>
        [MenuItem("Cosmimera/Data/Import Encyclopedia")]
        public static void Import()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string batch = Path.Combine(root, "Data/_incoming/p4-09-encyclopedia-01");
            var load = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
            JArray entries = JArray.Parse(File.ReadAllText(Path.Combine(batch, "encyclopedia/entries.json")), load);
            JObject ui = JObject.Parse(File.ReadAllText(Path.Combine(batch, "ui.json")), load);
            var values = new JObject();
            foreach (JObject entry in entries)
            {
                var resolved = new JArray();
                foreach (JObject reference in (entry["details"]?["value_refs"] as JArray) ?? new JArray())
                {
                    JObject source = null;
                    foreach (JObject candidate in (JArray)entry["sources"])
                        if ((string)candidate["id"] == (string)reference["source"]) source = candidate;
                    if (source == null || (string)source["storage"] != "repository")
                        throw new InvalidDataException("Numeric reference needs a repository source: " + entry["id"]);
                    string path = Path.GetFullPath(Path.Combine(root, (string)source["path"]));
                    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Source outside repository");
                    byte[] raw = File.ReadAllBytes(path);
                    using var sha = SHA256.Create();
                    string hash = BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "").ToLowerInvariant();
                    if (hash != (string)source["sha256"]) throw new InvalidDataException("Stale numeric source: " + path);
                    JToken value = JToken.Parse(System.Text.Encoding.UTF8.GetString(raw), load);
                    foreach (string part in ((string)reference["pointer"]).Substring(1).Split('/'))
                    {
                        string key = part.Replace("~1", "/").Replace("~0", "~");
                        value = value is JArray array ? array[int.Parse(key)] : value[key];
                    }
                    if (value == null || value.Type != JTokenType.Integer)
                        throw new InvalidDataException("Numeric reference does not resolve to an integer: " + reference);
                    resolved.Add(new JObject { ["pointer"] = reference["pointer"], ["value"] = value, ["unit"] = reference["unit"] });
                }
                values[(string)entry["id"]] = resolved;
            }
            const string output = "Assets/_Project/Bootstrap/Resources/Encyclopedia/Table.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            AssetDatabase.Refresh();
            var table = AssetDatabase.LoadAssetAtPath<EncyclopediaTable>(output);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<EncyclopediaTable>();
                AssetDatabase.CreateAsset(table, output);
            }
            table.SetImported(entries.ToString(Formatting.None), values.ToString(Formatting.None), ui.ToString(Formatting.None));
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log("Encyclopedia imported: " + entries.Count + " entries");
        }
    }
}
#endif
