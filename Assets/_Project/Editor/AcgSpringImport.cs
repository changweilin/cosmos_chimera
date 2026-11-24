using System;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.EditorTools
{
    /// <summary>Adds the Player feature's SpringBones component to ACG character models that carry
    /// swing bone chains ("sw_..."), so hanging cloth swings wherever the prefab is used (ADR-0013).</summary>
    /// <remarks>
    /// Why by type name: this Editor folder compiles into Assembly-CSharp-Editor, and feature assemblies are
    /// not auto-referenced, so the type is resolved at import time instead of referenced at compile time.
    /// </remarks>
    public sealed class AcgSpringImport : AssetPostprocessor
    {
        /// <summary>Assembly-qualified name of the runtime component.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const string ComponentType = "Cosmimera.Player.SpringBones, Cosmimera.Player";

        /// <summary>Resolves the SpringBones type.</summary>
        /// <remarks>
        /// Pre: none. Post: non-null when the Player assembly compiled; null otherwise.
        /// Side effects: none.
        /// </remarks>
        public static Type SpringBonesType() => Type.GetType(ComponentType);

        /// <summary>Adds SpringBones to an ACG model root whose hierarchy holds swing bones.</summary>
        /// <remarks>
        /// Pre: called by Unity after the model hierarchy is built.
        /// Post: under an ACG character folder, a model (not an "@clip" file) with "sw_" transforms has one SpringBones
        /// whose body capsules (torso, neck, legs, arms) are measured from the body mesh.
        /// Side effects: adds a component to the imported prefab; logs an error if the type is missing.
        /// </remarks>
        public void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.Contains("/ACG/") || assetPath.Contains("@"))
                return;
            bool hasSwing = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("sw_"))
                {
                    hasSwing = true;
                    break;
                }
            }

            if (!hasSwing)
                return;
            var type = SpringBonesType();
            if (type == null)
            {
                Debug.LogError($"[AcgSpringImport] {ComponentType} not found; {assetPath} imported without cloth swing");
                return;
            }

            // Explicit null test: Unity's fake-null objects break the ?? operator.
            var springs = root.GetComponent(type);
            if (springs == null)
                springs = root.AddComponent(type);
            SetCapsules(root, springs, type);
        }

        // Rig bone pairs (tools/acg-character, MPFB game_engine names) the cloth must not pass through.
        static readonly (string a, string b)[] CapsuleBones =
        {
            ("pelvis", "spine_01"), ("spine_01", "spine_03"), ("spine_03", "neck_01"), ("neck_01", "head"),
            ("thigh_l", "calf_l"), ("calf_l", "foot_l"), ("thigh_r", "calf_r"), ("calf_r", "foot_r"),
            ("upperarm_l", "lowerarm_l"), ("lowerarm_l", "hand_l"), ("upperarm_r", "lowerarm_r"), ("lowerarm_r", "hand_r"),
        };

        // Measures each capsule's radius from the body mesh while it is readable at import: the median
        // distance from the bone segment of the vertices that bone mostly moves (the limb's own surface).
        static void SetCapsules(GameObject root, Component springs, Type type)
        {
            SkinnedMeshRenderer body = null;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (body == null || smr.sharedMesh.vertexCount > body.sharedMesh.vertexCount)
                    body = smr;
            if (body == null)
                return;
            var mesh = body.sharedMesh;
            var bones = body.bones;
            var bind = mesh.bindposes;
            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            var starts = new System.Collections.Generic.List<Transform>();
            var ends = new System.Collections.Generic.List<Transform>();
            var radii = new System.Collections.Generic.List<float>();
            foreach (var (a, b) in CapsuleBones)
            {
                int ia = Array.FindIndex(bones, t => t != null && t.name == a);
                int ib = Array.FindIndex(bones, t => t != null && t.name == b);
                if (ia < 0 || ib < 0)
                    continue;
                // Bind-pose joint positions in mesh space.
                Vector3 pa = bind[ia].inverse.GetColumn(3);
                Vector3 pb = bind[ib].inverse.GetColumn(3);
                var d = new System.Collections.Generic.List<float>();
                for (int i = 0; i < verts.Length; i++)
                {
                    var w = weights[i];
                    if (w.boneIndex0 != ia || w.weight0 < 0.6f)
                        continue;
                    Vector3 ab = pb - pa;
                    float t = Mathf.Clamp01(Vector3.Dot(verts[i] - pa, ab) / ab.sqrMagnitude);
                    d.Add(Vector3.Distance(verts[i], pa + ab * t));
                }

                if (d.Count < 8)
                    continue;
                d.Sort();
                // Mesh units to the start bone's local units, which SpringBones scales by its lossyScale.
                float scale = body.transform.lossyScale.x / Mathf.Max(1e-6f, bones[ia].lossyScale.x);
                starts.Add(bones[ia]);
                ends.Add(bones[ib]);
                radii.Add(d[d.Count / 2] * scale);
            }

            type.GetMethod("SetBodyCapsules").Invoke(springs, new object[] { starts.ToArray(), ends.ToArray(), radii.ToArray() });
        }
    }
}
