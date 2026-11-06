using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Base of every imported definition: an asset identified by a globally unique, never-reused id.</summary>
    /// <remarks>
    /// Pre: instances are created and filled only by the data importer (ADR-0004); hand edits are overwritten.
    /// Post: <see cref="Id"/> matches the JSON id and the asset file name.
    /// Side effects: none.
    /// </remarks>
    public abstract class GameDef : ScriptableObject
    {
        [SerializeField] private string id;

        /// <summary>The data id (snake_case, unique across all kinds).</summary>
        /// <remarks>Pre: none. Post: non-empty for any imported asset. Side effects: none.</remarks>
        public string Id => id;

        internal void SetId(string value)
        {
            id = value;
        }
    }
}
