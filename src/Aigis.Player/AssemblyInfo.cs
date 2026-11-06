using System.Runtime.CompilerServices;

// Lets Aigis.Tests reach GameHost's internal AIGIS_FRAME_CAP parser, so the env-var contract
// (a number, "display", "off"/"none"/"uncapped", invariant-culture decimals, and an unrecognised
// value falling back rather than throwing) is pinned without opening a real window — same pattern
// already used by Aigis.Platform and Aigis.Render (see their own AssemblyInfo.cs).
[assembly: InternalsVisibleTo("Aigis.Tests")]
