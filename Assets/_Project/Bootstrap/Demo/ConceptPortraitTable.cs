using System;
using System.Collections.Generic;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>One registered character 2D concept portrait entry.</summary>
    /// <remarks>Pre: serialized in editor. Post: immutable at runtime. Side effects: none.</remarks>
    [Serializable]
    public struct CharacterPortraitEntry
    {
        public string id;
        public Texture2D texture;

        /// <summary>Creates a character portrait entry.</summary>
        /// <remarks>Pre: id non-empty. Post: fields set. Side effects: none.</remarks>
        public CharacterPortraitEntry(string id, Texture2D texture)
        {
            this.id = id;
            this.texture = texture;
        }
    }

    /// <summary>One registered creature 2D concept portrait entry keyed by line and stage.</summary>
    /// <remarks>Pre: serialized in editor. Post: immutable at runtime. Side effects: none.</remarks>
    [Serializable]
    public struct CreaturePortraitEntry
    {
        public string lineId;
        public CreatureStage stage;
        public Texture2D texture;

        /// <summary>Creates a creature portrait entry.</summary>
        /// <remarks>Pre: lineId non-empty. Post: fields set. Side effects: none.</remarks>
        public CreaturePortraitEntry(string lineId, CreatureStage stage, Texture2D texture)
        {
            this.lineId = lineId;
            this.stage = stage;
            this.texture = texture;
        }
    }

    /// <summary>Catalog of 2D concept portraits for Demo UI, referencing main-game art without modifying it.</summary>
    /// <remarks>Pre: populated by ConceptPortraitTableBuilder. Post: queries by character or creature line/stage. Side effects: none.</remarks>
    public sealed class ConceptPortraitTable : ScriptableObject
    {
        [SerializeField] private List<CharacterPortraitEntry> characterEntries = new List<CharacterPortraitEntry>();
        [SerializeField] private List<CreaturePortraitEntry> creatureEntries = new List<CreaturePortraitEntry>();

        /// <summary>All registered character portrait entries.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<CharacterPortraitEntry> CharacterEntries => characterEntries;

        /// <summary>All registered creature portrait entries.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<CreaturePortraitEntry> CreatureEntries => creatureEntries;

        /// <summary>Finds the 2D portrait texture for <paramref name="charId"/>, or null if absent.</summary>
        /// <remarks>Pre: none. Post: texture if found, else null. Side effects: none.</remarks>
        public Texture2D FindCharacter(string charId)
        {
            if (string.IsNullOrEmpty(charId))
            {
                return null;
            }

            for (int i = 0; i < characterEntries.Count; i++)
            {
                if (characterEntries[i].id == charId)
                {
                    return characterEntries[i].texture;
                }
            }

            return null;
        }

        /// <summary>Finds the 2D portrait texture for <paramref name="lineId"/> at <paramref name="stage"/>, or null if absent.</summary>
        /// <remarks>Pre: none. Post: texture if found, else null. Side effects: none.</remarks>
        public Texture2D FindCreature(string lineId, CreatureStage stage)
        {
            if (string.IsNullOrEmpty(lineId))
            {
                return null;
            }

            for (int i = 0; i < creatureEntries.Count; i++)
            {
                if (creatureEntries[i].lineId == lineId && creatureEntries[i].stage == stage)
                {
                    return creatureEntries[i].texture;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        /// <summary>Editor assignment of character and creature portrait entries.</summary>
        /// <remarks>Pre: editor only. Post: internal lists replaced. Side effects: none.</remarks>
        public void SetEntries(List<CharacterPortraitEntry> characters, List<CreaturePortraitEntry> creatures)
        {
            characterEntries = characters ?? new List<CharacterPortraitEntry>();
            creatureEntries = creatures ?? new List<CreaturePortraitEntry>();
        }
#endif
    }
}
