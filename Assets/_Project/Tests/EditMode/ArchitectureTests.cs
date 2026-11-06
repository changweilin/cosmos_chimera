using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Architecture.Tests
{
    /// <summary>Guards the feature boundaries from docs/architecture/overview.md that the compiler alone cannot enforce.</summary>
    /// <remarks>
    /// Pre: run inside the Unity Editor (project assemblies loaded, Application.dataPath valid).
    /// Post: fails if an Internal namespace exposes a public type or an asmdef references outside the allowed graph.
    /// Side effects: reads asmdef files only.
    /// REQ: docs/decisions/ADR-0003-roslyn-analyzers.md
    /// </remarks>
    public sealed class ArchitectureTests
    {
        // REQ: docs/architecture/overview.md#依賴方向 — keep in sync; adding an edge here is an architecture change.
        private static readonly Dictionary<string, string[]> AllowedReferences = new Dictionary<string, string[]>
        {
            ["Cosmimera.Core"] = new string[0],
            // ADR-0004: imported definitions; Planet and Player stay unaware of creatures, so they may not use it.
            ["Cosmimera.GameData"] = new[] { "Cosmimera.Core" },
            ["Cosmimera.GameData.Editor"] = new[] { "Cosmimera.Core", "Cosmimera.GameData" },
            ["Cosmimera.Planet"] = new[] { "Cosmimera.Core" },
            ["Cosmimera.WorldGen"] = new[] { "Cosmimera.Core", "Cosmimera.Planet", "Cosmimera.GameData" },
            ["Cosmimera.Player"] = new[] { "Cosmimera.Core", "Cosmimera.Planet" },
            ["Cosmimera.Creatures"] = new[] { "Cosmimera.Core", "Cosmimera.WorldGen", "Cosmimera.Planet", "Cosmimera.GameData" },
            ["Cosmimera.Combat"] = new[] { "Cosmimera.Core", "Cosmimera.WorldGen", "Cosmimera.Planet", "Cosmimera.GameData" },
            ["Cosmimera.Capture"] = new[] { "Cosmimera.Core", "Cosmimera.WorldGen", "Cosmimera.Planet", "Cosmimera.GameData" },
            ["Cosmimera.Lobby"] = new[] { "Cosmimera.Core", "Cosmimera.Combat", "Cosmimera.Capture", "Cosmimera.Creatures", "Cosmimera.GameData" },
            ["Cosmimera.Codex"] = new[] { "Cosmimera.Core", "Cosmimera.Combat", "Cosmimera.Capture", "Cosmimera.Creatures", "Cosmimera.GameData" },
            ["Cosmimera.Net"] = new[] { "Cosmimera.Core", "Cosmimera.Combat", "Cosmimera.Creatures", "Cosmimera.Planet", "Cosmimera.GameData" },
            ["Cosmimera.Save"] = new[] { "Cosmimera.Core", "Cosmimera.Creatures", "Cosmimera.Capture", "Cosmimera.GameData" },
        };

        [Serializable]
        private sealed class AsmdefFile
        {
            public string name;
            public string[] references;
        }

        private static IEnumerable<AsmdefFile> ProjectAsmdefs()
        {
            string root = Path.Combine(Application.dataPath, "_Project");
            return Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories)
                .Select(p => JsonUtility.FromJson<AsmdefFile>(File.ReadAllText(p)));
        }

        [Test]
        public void Every_runtime_asmdef_is_in_the_dependency_graph()
        {
            var runtime = ProjectAsmdefs()
                .Where(a => a.name.StartsWith("Cosmimera.", StringComparison.Ordinal))
                .Where(a => !a.name.Contains(".Tests") && a.name != "Cosmimera.Bootstrap")
                .Select(a => a.name);
            CollectionAssert.IsSubsetOf(runtime, AllowedReferences.Keys,
                "New feature asmdef: add it to overview.md and AllowedReferences.");
        }

        [Test]
        public void Asmdef_references_follow_the_dependency_direction()
        {
            var violations = new List<string>();
            foreach (AsmdefFile a in ProjectAsmdefs())
            {
                if (!AllowedReferences.TryGetValue(a.name, out string[] allowed))
                {
                    continue; // Bootstrap (composition root) and test assemblies may reference anything.
                }
                foreach (string r in a.references ?? new string[0])
                {
                    if (r.StartsWith("GUID:", StringComparison.Ordinal))
                    {
                        violations.Add($"{a.name} → {r}: use name references so this check can read them");
                    }
                    else if (r.StartsWith("Cosmimera.", StringComparison.Ordinal) && !allowed.Contains(r))
                    {
                        violations.Add($"{a.name} → {r}");
                    }
                }
            }
            Assert.IsEmpty(violations, "Disallowed references (overview.md#依賴方向):\n" + string.Join("\n", violations));
        }

        [Test]
        public void Internal_namespaces_expose_no_public_types()
        {
            var leaks = AppDomain.CurrentDomain.GetAssemblies()
                .Where(asm => asm.GetName().Name.StartsWith("Cosmimera.", StringComparison.Ordinal))
                .SelectMany(asm => asm.GetExportedTypes())
                .Where(t => t.Namespace != null && (t.Namespace.EndsWith(".Internal", StringComparison.Ordinal)
                                                    || t.Namespace.Contains(".Internal.")))
                .Select(t => t.FullName)
                .ToList();
            Assert.IsEmpty(leaks, "Internal/ types must be `internal` so cross-feature use fails to compile:\n"
                                  + string.Join("\n", leaks));
        }
    }
}
