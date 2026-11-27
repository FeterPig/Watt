using System.Runtime.CompilerServices;

// The automated test project (tests/Watt.Tests) exercises the same internal services the
// tray app uses instead of duplicating them in a separate test assembly.
[assembly: InternalsVisibleTo("Watt.Tests")]
