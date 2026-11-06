using System.Runtime.CompilerServices;

// Lets GlyphRasterizerTests (TASK-051 code-review follow-up) assert on the cache's internal
// entry count without exposing it as public API — same pattern as tools/Aigis.Evolve's own
// AssemblyInfo.cs.
[assembly: InternalsVisibleTo("Aigis.Tests")]
