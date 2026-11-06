using System.Runtime.CompilerServices;

// Internal/ types are `internal`, which is what makes a cross-feature reference to them a compile
// error (CS0122). Tests are the only other assembly allowed to see them.
[assembly: InternalsVisibleTo("Cosmimera.Codex.Tests.EditMode")]
[assembly: InternalsVisibleTo("Cosmimera.Codex.Tests.PlayMode")]
