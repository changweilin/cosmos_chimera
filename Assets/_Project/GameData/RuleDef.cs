using System;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>A trigger/effect shell: a type id plus parameters, interpreted by the feature that owns the type.</summary>
    /// <remarks>
    /// Params stay as compact JSON because the type vocabulary is only closed into an enum in P2
    /// (REQ: common.schema.json#/$defs/rule, user decision 2026-11-06); typing them now would be a guess.
    /// Pre: none. Post: <see cref="Type"/> is a non-empty id; <see cref="ParamsJson"/> is a JSON object ("{}" when absent).
    /// Side effects: none.
    /// </remarks>
    [Serializable]
    public sealed class RuleDef
    {
        [SerializeField] private string type;
        [SerializeField] private string paramsJson;

        internal RuleDef(string type, string paramsJson)
        {
            this.type = type;
            this.paramsJson = paramsJson;
        }

        /// <summary>The rule type id.</summary>
        /// <remarks>Pre: none. Post: non-empty for imported data. Side effects: none.</remarks>
        public string Type => type;

        /// <summary>Parameters as a compact JSON object.</summary>
        /// <remarks>Pre: none. Post: never null for imported data. Side effects: none.</remarks>
        public string ParamsJson => paramsJson;
    }
}
