using System.Runtime.CompilerServices;

// The D0 run is glue with no rule layer of its own; the PlayMode smoke test reads its state instead of adding public API.
[assembly: InternalsVisibleTo("Cosmimera.Bootstrap.Tests.PlayMode")]
[assembly: InternalsVisibleTo("Cosmimera.Bootstrap.Tests.EditMode")]
// The NPC UMA race builder reads the DNA channel ranges it must cover (ADR-0015).
[assembly: InternalsVisibleTo("Cosmimera.Uma.Editor")]
