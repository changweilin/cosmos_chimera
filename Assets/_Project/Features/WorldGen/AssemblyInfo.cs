using System.Runtime.CompilerServices;

// Internal/ types are `internal`, which is what makes a cross-feature reference to them a compile
// error (CS0122). Tests are the only other assembly allowed to see them.
[assembly: InternalsVisibleTo("Cosmimera.WorldGen.Tests.EditMode")]
[assembly: InternalsVisibleTo("Cosmimera.WorldGen.Tests.PlayMode")]
// The composition root's WorldObjects bridge is tested against hand-built layouts (RunLayout has no public constructor).
[assembly: InternalsVisibleTo("Cosmimera.Bootstrap.Tests.EditMode")]
