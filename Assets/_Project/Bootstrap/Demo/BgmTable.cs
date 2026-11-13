using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>One registered BGM track.</summary>
    /// <remarks>Pre: serialized in editor. Post: immutable at runtime. Side effects: none.</remarks>
    [Serializable]
    public struct BgmEntry
    {
        public string id;
        public AudioClip clip;

        public BgmEntry(string id, AudioClip clip)
        {
            this.id = id;
            this.clip = clip;
        }
    }

    /// <summary>Catalog of background music audio clips.</summary>
    /// <remarks>Pre: populated by BgmTableBuilder. Post: queries by track id. Side effects: none.</remarks>
    public sealed class BgmTable : ScriptableObject
    {
        [SerializeField] private List<BgmEntry> entries = new List<BgmEntry>();

        /// <summary>All registered track entries.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<BgmEntry> Entries => entries;

        /// <summary>Finds the AudioClip registered under <paramref name="id"/>.</summary>
        /// <remarks>Pre: none. Post: clip if found, else null. Side effects: none.</remarks>
        public AudioClip Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].id == id)
                {
                    return entries[i].clip;
                }
            }
            return null;
        }

#if UNITY_EDITOR
        /// <summary>Editor assignment of track entries.</summary>
        /// <remarks>Pre: editor only. Post: internal list replaced. Side effects: none.</remarks>
        public void SetEntries(List<BgmEntry> newEntries)
        {
            entries = newEntries ?? new List<BgmEntry>();
        }
#endif
    }
}
