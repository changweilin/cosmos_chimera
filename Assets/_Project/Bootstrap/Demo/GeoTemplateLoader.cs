using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Reads the geo-templates staged by tools/demo-data/merge.py (meta.json + height/water .bytes).</summary>
    /// <remarks>Pre: see Load. Post: see Load. Side effects: allocates.</remarks>
    public static class GeoTemplateLoader
    {
        /// <summary>Folder the staging script writes to.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const string Folder = "Assets/_Project/Data/GeoTemplates/";

        /// <summary>Parses parallel arrays of staged files into sources and their terrain tags.</summary>
        /// <remarks>
        /// Pre: the three arrays have equal length and index i of each belongs to the same template (CreateScene sorts by id).
        /// Post: one source and one tag list per template; ContractException on a size/length mismatch.
        /// Side effects: allocates.
        /// </remarks>
        public static void Load(TextAsset[] meta, TextAsset[] heights, TextAsset[] water, List<TemplateSource> sources,
            List<IReadOnlyList<string>> tags)
        {
            Contract.Requires(meta != null && heights != null && water != null &&
                meta.Length == heights.Length && meta.Length == water.Length, "template arrays must be parallel");
            for (int i = 0; i < meta.Length; i++)
            {
                JObject m = JObject.Parse(meta[i].text);
                int size = (int)m["size"];
                byte[] hb = heights[i].bytes;
                byte[] wb = water[i].bytes;
                Contract.Requires(hb.Length == size * size * 2 && wb.Length == size * size,
                    "template " + (string)m["id"] + " has the wrong byte count");
                var h = new ushort[size * size];
                for (int k = 0; k < h.Length; k++)
                {
                    h[k] = (ushort)(hb[2 * k] | (hb[2 * k + 1] << 8)); // tools/geodata: uint16 little-endian
                }

                float min = (float)m["elev_min_m"];
                float max = (float)m["elev_max_m"];
                float relief = max - Mathf.Max(min, 0f);
                sources.Add(new TemplateSource((string)m["id"], size, h, wb, Mathf.Clamp((float)m["sea_level_norm"], 0f, 0.999f), relief));
                var t = new List<string>();
                foreach (JToken tag in m["terrain_tags"])
                {
                    t.Add((string)tag);
                }

                tags.Add(t);
            }
        }
    }
}
