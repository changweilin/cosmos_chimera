using System.Linq;
using UMA;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.EditorTools
{
    /// <summary>Batch-mode entry points for the gitignored UMA install (ADR-0012).</summary>
    public static class UmaLibrary
    {
        /// <summary>Project folder whose UMA assets the library indexes alongside Assets/UMA.</summary>
        public const string ProjectFolder = "Assets/_Project/Art/Characters/_UMA";

        /// <summary>Rebuilds UMA's Global Library so it indexes every UMA asset in the project, including project-owned recipes.</summary>
        /// <remarks>
        /// Pre: UMA is imported under Assets/UMA (tools/install-uma) and scripts have compiled.
        /// Post: every RaceData, slot, overlay and wardrobe recipe under Assets is indexed; no index entry is null.
        /// Side effects: adds <see cref="ProjectFolder"/> to every type's search folders, then rewrites
        /// Assets/UMA/InternalDataStore/InGame/Resources/AssetIndexer.asset; logs the counts.
        /// </remarks>
        public static void Rebuild()
        {
            // The shipped index references GUIDs from UMA's own authoring tree, so a fresh import
            // resolves several races to null until the library is rebuilt (seen with V3.1.5).
            var indexer = UMAAssetIndexer.Instance;
            // The shipped index only takes slots, overlays, recipes and UMA materials from under Assets/UMA; the project's
            // own UMA assets (NPC races, wardrobe) live outside it.
            foreach (string type in indexer.TypeFolderSearch.Keys.ToList())
                indexer.AddSearchFolder(type, ProjectFolder);
            indexer.Clear(false);
            indexer.AddEverything(false);
            indexer.ForceSave();
            AssetDatabase.SaveAssets();
            Debug.Log($"[UmaLibrary] races={indexer.GetAllAssets<RaceData>().Count} " +
                      $"wardrobe={indexer.GetAllAssets<UMA.CharacterSystem.UMAWardrobeRecipe>().Count} " +
                      $"slots={indexer.GetAllAssets<SlotDataAsset>().Count}");
        }
    }
}
