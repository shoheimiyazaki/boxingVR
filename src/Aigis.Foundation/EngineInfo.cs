namespace Aigis.Foundation;

/// <summary>
/// Static identity of the engine. Kept trivial on purpose — a seam so tools,
/// the agent driver, and the harness can report a stable name/version.
/// </summary>
public static class EngineInfo
{
    public const string Name = "Aigis";

    /// <summary>Semantic version of the engine build.</summary>
    public const string Version = "0.0.1-p0";
}
