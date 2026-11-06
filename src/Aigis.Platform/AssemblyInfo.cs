using System.Runtime.CompilerServices;

// Lets Aigis.Tests construct InputSnapshot (internal ctor) against a fake IInputContext, so its
// gamepad degradation ("no device connected never throws") and button/stick mapping can be unit
// tested without a real OS window/input backend — same pattern already used by Aigis.Render
// (see its own AssemblyInfo.cs).
[assembly: InternalsVisibleTo("Aigis.Tests")]
