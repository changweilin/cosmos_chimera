using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Imported shared encyclopedia JSON, localized UI and resolved numeric references.</summary>
    /// <remarks>Pre: generated from Data by EncyclopediaImport. Post: immutable at runtime. Side effects: none.</remarks>
    public sealed class EncyclopediaTable : ScriptableObject
    {
        [SerializeField] private string entriesJson;
        [SerializeField] private string valuesJson;
        [SerializeField] private string uiJson;

        /// <summary>Canonical content shared with Forge.</summary>
        /// <remarks>Pre: imported. Post: JSON array. Side effects: none.</remarks>
        public string EntriesJson => entriesJson;
        /// <summary>Resolved gameplay values keyed by encyclopedia ID.</summary>
        /// <remarks>Pre: imported. Post: JSON object. Side effects: none.</remarks>
        public string ValuesJson => valuesJson;
        /// <summary>Externalized interface labels.</summary>
        /// <remarks>Pre: imported. Post: localized JSON object. Side effects: none.</remarks>
        public string UiJson => uiJson;

#if UNITY_EDITOR
        /// <summary>Assigns validated importer output.</summary>
        /// <remarks>Pre: inputs valid JSON. Post: fields equal arguments. Side effects: changes this generated asset.</remarks>
        public void SetImported(string entries, string values, string ui)
        { entriesJson = entries; valuesJson = values; uiJson = ui; }
#endif
    }
}
