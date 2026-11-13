using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>One registered character model entry (provisional or final).</summary>
    /// <remarks>Pre: serialized in editor. Post: immutable at runtime. Side effects: none.</remarks>
    [Serializable]
    public struct CharacterModelEntry
    {
        public string id;
        public string displayName;
        public GameObject prefab;
        public RuntimeAnimatorController animatorController;
        public bool isProvisional;
        public string sourceTaskId;

        /// <summary>Creates a character model entry.</summary>
        /// <remarks>Pre: id non-empty. Post: fields set. Side effects: none.</remarks>
        public CharacterModelEntry(string id, string displayName, GameObject prefab,
            RuntimeAnimatorController animatorController, bool isProvisional, string sourceTaskId)
        {
            this.id = id;
            this.displayName = displayName;
            this.prefab = prefab;
            this.animatorController = animatorController;
            this.isProvisional = isProvisional;
            this.sourceTaskId = sourceTaskId;
        }
    }

    /// <summary>Catalog of playable character model prefabs (including provisional forge assets).</summary>
    /// <remarks>Pre: populated by CharacterModelTableBuilder. Post: queries by character id. Side effects: none.</remarks>
    public sealed class CharacterModelTable : ScriptableObject
    {
        [SerializeField] private List<CharacterModelEntry> entries = new List<CharacterModelEntry>();

        /// <summary>All registered character model entries.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<CharacterModelEntry> Entries => entries;

        /// <summary>Finds the entry registered under <paramref name="id"/>.</summary>
        /// <remarks>Pre: none. Post: returns true and sets entry iff found. Side effects: none.</remarks>
        public bool TryFind(string id, out CharacterModelEntry entry)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].id == id)
                    {
                        entry = entries[i];
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        /// <summary>Finds the prefab registered under <paramref name="id"/>, or null if absent.</summary>
        /// <remarks>Pre: none. Post: prefab if found, else null. Side effects: none.</remarks>
        public GameObject FindPrefab(string id) => TryFind(id, out CharacterModelEntry entry) ? entry.prefab : null;

#if UNITY_EDITOR
        /// <summary>Editor assignment of character model entries.</summary>
        /// <remarks>Pre: editor only. Post: internal list replaced. Side effects: none.</remarks>
        public void SetEntries(List<CharacterModelEntry> newEntries)
        {
            entries = newEntries ?? new List<CharacterModelEntry>();
        }
#endif
    }
}
