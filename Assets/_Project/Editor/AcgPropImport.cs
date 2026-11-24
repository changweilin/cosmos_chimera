using UnityEditor;
using UnityEngine;

namespace Cosmimera.EditorTools
{
    /// <summary>Disables the renderers of ACG hand props exported as hidden (ADR-0013), so a character
    /// shows only its default prop and the others stay in the prefab ready to be swapped in.</summary>
    /// <remarks>
    /// Why a postprocessor: Unity ignores FBX Visibility on skinned meshes, so tools/acg-character/acg_props.py
    /// writes the FBX user property acg_hidden=1 on every prop except the spec's "hold".
    /// </remarks>
    public sealed class AcgPropImport : AssetPostprocessor
    {
        // Node callbacks may run before the skinned renderers exist, so names are collected here and
        // applied in OnPostprocessModel.
        readonly System.Collections.Generic.HashSet<string> _hidden = new System.Collections.Generic.HashSet<string>();

        /// <summary>Called by the importer for each FBX node carrying user properties.</summary>
        /// <remarks>
        /// Pre: called by Unity during model import.
        /// Post: under an ACG character folder, a node with acg_hidden=1 is remembered for OnPostprocessModel.
        /// Side effects: none outside this postprocessor instance.
        /// </remarks>
        public void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!assetPath.Contains("/ACG/"))
                return;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != "acg_hidden" || System.Convert.ToInt32(values[i]) == 0)
                    continue;
                _hidden.Add(go.name);
            }
        }

        /// <summary>Disables the renderers of the props remembered as hidden.</summary>
        /// <remarks>
        /// Pre: called by Unity after the model hierarchy is built.
        /// Post: every renderer on a remembered node is disabled.
        /// Side effects: edits the imported prefab's renderers.
        /// </remarks>
        public void OnPostprocessModel(GameObject root)
        {
            if (_hidden.Count == 0)
                return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (_hidden.Contains(r.name))
                    r.enabled = false;
        }
    }
}
