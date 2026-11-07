using System.Runtime.CompilerServices;

// Definitions are written only by the importer (ADR-0004), so their setters are internal and the
// Editor-only importer is the single non-test assembly allowed to call them.
[assembly: InternalsVisibleTo("Cosmimera.GameData.Editor")]
[assembly: InternalsVisibleTo("Cosmimera.GameData.Tests.EditMode")]
// Creatures adapter tests build an in-memory GameDatabase instead of importing JSON (Sol P2-05/07 review C7).
[assembly: InternalsVisibleTo("Cosmimera.Creatures.Tests.EditMode")]
