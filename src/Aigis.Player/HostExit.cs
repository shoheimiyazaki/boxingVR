namespace Aigis.Player;

/// <summary>
/// How <see cref="GameHost.Run{TInput}"/> ended (2026-10-15). It used to return nothing, so a window
/// that could not create its renderer, or whose platform loop threw, ended exactly like a player
/// closing it — and the host marked the session a clean exit with no word to the player. A host now
/// tells a player-facing failure from an ordinary close and can say so (a dialog, an unclean-exit
/// marker left in place).
/// </summary>
public enum HostExit
{
    /// <summary>The window was closed normally (by the player or by the game).</summary>
    Clean,

    /// <summary>No renderer could be created (no usable Vulkan device or driver), so nothing could
    /// ever be drawn.</summary>
    RendererUnavailable,

    /// <summary>The platform's own window loop threw; the session ended there.</summary>
    LoopFailed,

    /// <summary>Drawing or presenting failed on every frame for
    /// <see cref="GameHost.PersistentRenderFaultFrames"/> frames running (a lost device, a driver
    /// that stopped presenting), and the window was closed rather than left frozen.</summary>
    RenderingFailedPersistently,
}
