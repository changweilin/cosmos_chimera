using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Player-facing text for the D0 demo (Terra copy staged by tools/demo-data/merge.py).</summary>
    /// <remarks>
    /// Pre: constructed from the staged TextAssets. Post: lookups fall back to the id, never throw, so a missing
    /// string shows up visibly in the UI instead of breaking a run.
    /// Side effects: none after construction.
    /// </remarks>
    public sealed class DemoCopy
    {
        private readonly Dictionary<string, string> _ui = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _affixName = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _affixDesc = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _themes = new Dictionary<string, string>();
        private readonly Dictionary<string, string[]> _steps = new Dictionary<string, string[]>();
        private readonly Dictionary<string, string[]> _epithets = new Dictionary<string, string[]>();
        private readonly List<string> _chainIds = new List<string>();
        private readonly Dictionary<string, string> _chainTitles = new Dictionary<string, string>();
        private readonly List<string> _themeIds = new List<string>();
        private readonly List<string> _genericAffixes = new List<string>();

        /// <summary>Parses the staged copy files.</summary>
        /// <remarks>
        /// Pre: every argument is the text of the same-named file in Assets/_Project/Data/Copy; lang is "zh-TW" or "en".
        /// Post: lookups populated. Side effects: allocates.
        /// </remarks>
        public DemoCopy(string lang, string strings, string names, string affixes, string themes, string quests,
            string epithets)
        {
            Contract.Requires(lang == "zh-TW" || lang == "en", "lang must be zh-TW or en");
            foreach (KeyValuePair<string, JToken> kv in JObject.Parse(strings))
            {
                _ui[kv.Key] = (string)kv.Value[lang];
            }

            foreach (JToken e in JObject.Parse(names)["entries"])
            {
                _names[(string)e["id"]] = (string)e[lang == "zh-TW" ? "zh" : "en"];
            }

            foreach (JToken a in JArray.Parse(affixes))
            {
                string id = (string)a["id"];
                _affixName[id] = (string)a["name"][lang];
                _affixDesc[id] = (string)a["desc"][lang];
                if (!id.EndsWith("_surge"))
                {
                    _genericAffixes.Add(id);
                }
            }

            foreach (JToken t in JArray.Parse(themes))
            {
                _themeIds.Add((string)t["id"]);
                _themes[(string)t["id"]] = (string)t["name"][lang];
            }

            JObject q = JObject.Parse(quests);
            foreach (JToken s in q["steps"])
            {
                var variants = new List<string>();
                foreach (JToken v in s["variants"])
                {
                    variants.Add((string)v[lang]);
                }

                _steps[(string)s["type"]] = variants.ToArray();
            }

            foreach (JToken c in q["chains"])
            {
                _chainIds.Add((string)c["id"]);
                _chainTitles[(string)c["id"]] = (string)c["title"][lang];
            }

            foreach (JToken b in JArray.Parse(epithets))
            {
                var list = new List<string>();
                foreach (JToken e in b["epithets"])
                {
                    list.Add((string)e[lang]);
                }

                _epithets[(string)b["planet_type"]] = list.ToArray();
            }
        }

        /// <summary>Region theme ids in file order.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<string> ThemeIds => _themeIds;

        /// <summary>Affix ids that are not element surges.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<string> GenericAffixIds => _genericAffixes;

        /// <summary>Quest chain ids.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<string> ChainIds => _chainIds;

        /// <summary>UI string by key.</summary>
        /// <remarks>Pre: none. Post: the string, or the key when missing. Side effects: none.</remarks>
        public string Ui(string key) => _ui.TryGetValue(key, out string s) && !string.IsNullOrEmpty(s) ? s : key;

        /// <summary>Display name of a creature/move/trait id.</summary>
        /// <remarks>Pre: none. Post: the name, or the id. Side effects: none.</remarks>
        public string Name(string id) => id != null && _names.TryGetValue(id, out string s) ? s : id;

        /// <summary>Affix name, or empty for null.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string AffixName(string id) => id == null ? string.Empty : _affixName.TryGetValue(id, out string s) ? s : id;

        /// <summary>Affix description, or empty for null.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string AffixDesc(string id) => id == null ? string.Empty : _affixDesc.TryGetValue(id, out string s) ? s : id;

        /// <summary>Theme name.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string Theme(string id) => _themes.TryGetValue(id, out string s) ? s : id;

        /// <summary>Chain title.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string ChainTitle(string id) => _chainTitles.TryGetValue(id, out string s) ? s : id;

        /// <summary>Quest step text variant (step type snake_case, variant index wraps).</summary>
        /// <remarks>Pre: none. Post: non-null; placeholders unreplaced. Side effects: none.</remarks>
        public string Step(string type, int variant)
        {
            return _steps.TryGetValue(type, out string[] v) && v.Length > 0 ? v[(variant % v.Length + v.Length) % v.Length] : type;
        }

        /// <summary>Boss epithet for a planet type.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string Epithet(string planetType, int variant)
        {
            return _epithets.TryGetValue(planetType, out string[] e) && e.Length > 0 ? e[(variant % e.Length + e.Length) % e.Length] : string.Empty;
        }
    }
}
