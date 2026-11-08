using System.Runtime.CompilerServices;

// The D0 run is glue with no rule layer of its own; the PlayMode smoke test reads its state instead of adding public API.
[assembly: InternalsVisibleTo("Cosmimera.Bootstrap.Tests.PlayMode")]
