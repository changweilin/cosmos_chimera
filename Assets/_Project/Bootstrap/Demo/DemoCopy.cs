using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Player-facing text for the D0 demo (Terra copy staged by tools/demo-data/merge.py).</summary>
    /// <remarks>
    /// Pre: constructed from the staged TextAssets. Post: missing translations fall back to English; missing keys
    /// fall back to their ids, so missing copy is visible instead of breaking a run.
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
        private readonly Dictionary<string, string[]> _planetWeatherAffixes = new Dictionary<string, string[]>();
        private readonly Dictionary<string, string[]> _planetTerrainAffixes = new Dictionary<string, string[]>();
        private readonly string _lang;
        private string _planetNameFormat = "{weather}的{terrain}星";
        private readonly List<string> _chainIds = new List<string>();
        private readonly Dictionary<string, string> _chainTitles = new Dictionary<string, string>();
        private readonly List<string> _themeIds = new List<string>();
        private readonly List<string> _genericAffixes = new List<string>();

        /// <summary>Parses the staged copy files.</summary>
        /// <remarks>
        /// Pre: every argument is the text of the same-named file in Assets/_Project/Data/Copy; lang is "zh-TW", "en" or "ja".
        /// Post: lookups populated. Side effects: allocates.
        /// </remarks>
        public DemoCopy(string lang, string strings, string names, string affixes, string themes, string quests,
            string epithets)
        {
            Contract.Requires(lang == "zh-TW" || lang == "en" || lang == "ja", "lang must be zh-TW, en or ja");
            _lang = lang;
            foreach (KeyValuePair<string, JToken> kv in JObject.Parse(strings))
            {
                _ui[kv.Key] = Localized(kv.Value, lang);
            }
            var encyclopedia = UnityEngine.Resources.Load<EncyclopediaTable>("Encyclopedia/Table");
            if (encyclopedia != null)
                foreach (KeyValuePair<string, JToken> kv in JObject.Parse(encyclopedia.UiJson))
                    _ui.Add(kv.Key, Localized(kv.Value, lang));
            UnityEngine.TextAsset craftingCopy = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Crafting/copy");
            if (craftingCopy == null) throw new System.IO.InvalidDataException("Missing staged crafting copy");
            foreach (KeyValuePair<string, JToken> kv in JObject.Parse(craftingCopy.text))
                _ui.Add(kv.Key, Localized(kv.Value, lang));

            foreach (JToken e in JObject.Parse(names)["entries"])
            {
                _names[(string)e["id"]] = Localized(e, lang == "zh-TW" ? "zh" : lang);
            }

            foreach (JToken a in JArray.Parse(affixes))
            {
                string id = (string)a["id"];
                _affixName[id] = Localized(a["name"], lang);
                _affixDesc[id] = Localized(a["desc"], lang);
                if (!id.EndsWith("_surge"))
                {
                    _genericAffixes.Add(id);
                }
            }

            foreach (JToken t in JArray.Parse(themes))
            {
                _themeIds.Add((string)t["id"]);
                _themes[(string)t["id"]] = Localized(t["name"], lang);
            }

            JObject q = JObject.Parse(quests);
            foreach (JToken s in q["steps"])
            {
                var variants = new List<string>();
                foreach (JToken v in s["variants"])
                {
                    variants.Add(Localized(v, lang));
                }

                _steps[(string)s["type"]] = variants.ToArray();
            }

            foreach (JToken c in q["chains"])
            {
                _chainIds.Add((string)c["id"]);
                _chainTitles[(string)c["id"]] = Localized(c["title"], lang);
            }

            foreach (JToken b in JArray.Parse(epithets))
            {
                var list = new List<string>();
                foreach (JToken e in b["epithets"])
                {
                    list.Add(Localized(e, lang));
                }

                _epithets[(string)b["planet_type"]] = list.ToArray();
            }

            UnityEngine.TextAsset planetAffixes = UnityEngine.Resources.Load<UnityEngine.TextAsset>("PlanetAffixes/affixes");
            if (planetAffixes != null)
            {
                LoadPlanetAffixes(planetAffixes.text);
            }
        }

        /// <summary>Loads or replaces the planet weather/terrain affix library from JSON.</summary>
        /// <remarks>
        /// Pre: json is non-null JSON matching PlanetAffixes/affixes structure.
        /// Post: planet weather/terrain affix dictionaries populated for this instance's language.
        /// Side effects: mutates internal planet affix tables; allocates.
        /// </remarks>
        public void LoadPlanetAffixes(string json)
        {
            Contract.Requires(json != null, "json must not be null");
            _planetWeatherAffixes.Clear();
            _planetTerrainAffixes.Clear();
            JObject root = JObject.Parse(json);
            if (root["format"] is JObject fmt)
            {
                string f = Localized(fmt, _lang);
                if (!string.IsNullOrEmpty(f))
                {
                    _planetNameFormat = f;
                }
            }

            ReadAffixMap(root["weather_affixes"] as JObject, _planetWeatherAffixes, _lang);
            ReadAffixMap(root["terrain_affixes"] as JObject, _planetTerrainAffixes, _lang);
        }

        private static void ReadAffixMap(JObject source, Dictionary<string, string[]> target, string lang)
        {
            if (source == null)
            {
                return;
            }

            foreach (KeyValuePair<string, JToken> kv in source)
            {
                if (kv.Value is not JArray arr)
                {
                    continue;
                }

                var items = new List<string>(arr.Count);
                foreach (JToken entry in arr)
                {
                    items.Add(Localized(entry, lang));
                }

                target[kv.Key] = items.ToArray();
            }
        }

        private static string Localized(JToken value, string lang)
        {
            string selected = (string)value[lang];
            return string.IsNullOrEmpty(selected) ? (string)value["en"] : selected;
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

        /// <summary>Player-facing planet type name (never show the internal id).</summary>
        /// <remarks>Pre: planetTypeId e.g. "lava_planet". Post: the name, or the key when missing. Side effects: none.</remarks>
        public string Planet(string planetTypeId) => Ui("planet." + planetTypeId);

        /// <summary>Player-facing element name (never show the enum identifier).</summary>
        /// <remarks>Pre: elementName is the enum name, any casing. Post: the name, or the key when missing. Side effects: none.</remarks>
        public string Element(string elementName) => Ui("element." + (elementName ?? string.Empty).ToLowerInvariant());

        /// <summary>Player-facing evolution material name, e.g. "光素材 T1".</summary>
        /// <remarks>Pre: key constructed. Post: ui.material.label with {element} and {tier} filled. Side effects: allocates.</remarks>
        public string Material(Creatures.MaterialKey key) =>
            Ui("ui.material.label").Replace("{element}", Element(key.Element.ToString())).Replace("{tier}", key.Tier.ToString());

        /// <summary>Display name of a creature/move/trait id.</summary>
        /// <remarks>Pre: none. Post: the selected name (including ja), English fallback, or the id if absent in both. Side effects: none.</remarks>
        public string Name(string id) => id != null && _names.TryGetValue(id, out string s) && !string.IsNullOrEmpty(s) ? s : id;

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

        /// <summary>Deterministic "XX的OO星" name drawn from the planet type's weather and terrain affix libraries.</summary>
        /// <remarks>
        /// Pre: none (unknown planetTypeId falls back to <see cref="Planet"/>).
        /// Post: non-null, non-empty localized planet name; identical (planetTypeId, seed) returns identical name.
        /// Side effects: allocates formatted string.
        /// </remarks>
        public string PlanetName(string planetTypeId, ulong seed)
        {
            if (string.IsNullOrEmpty(planetTypeId)
                || !_planetWeatherAffixes.TryGetValue(planetTypeId, out string[] weathers) || weathers.Length == 0
                || !_planetTerrainAffixes.TryGetValue(planetTypeId, out string[] terrains) || terrains.Length == 0)
            {
                return Planet(planetTypeId);
            }

            WorldGen.PlanetNameIndices pick = WorldGen.PlanetNameDraw.Draw(planetTypeId, seed, weathers.Length, terrains.Length);
            return _planetNameFormat
                .Replace("{weather}", weathers[pick.WeatherIndex])
                .Replace("{terrain}", terrains[pick.TerrainIndex]);
        }
    }
}
