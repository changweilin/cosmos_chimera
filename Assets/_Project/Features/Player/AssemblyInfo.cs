using System.Runtime.CompilerServices;

// Internal/ types are `internal`, which is what makes a cross-feature reference to them a compile
// error (CS0122). Tests are the only other assembly allowed to see them.
[assembly: InternalsVisibleTo("Cosmimera.Player.Tests.EditMode")]
[assembly: InternalsVisibleTo("Cosmimera.Player.Tests.PlayMode")]
