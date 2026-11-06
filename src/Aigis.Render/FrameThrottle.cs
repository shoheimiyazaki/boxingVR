namespace Aigis.Render;

/// <summary>
/// How hard a windowed renderer is allowed to push frames — the presentation-rate policy
/// <see cref="WindowedRenderer"/> maps onto a Vulkan swapchain present mode.
/// <para>
/// A separate engine-level enum rather than exposing <c>PresentModeKHR</c> directly, for the same
/// reason the physics backend is isolated (hard rule 1): the names below say what the caller WANTS,
/// and each maps to a preference list with a fallback, because only FIFO is guaranteed to exist on
/// every driver/surface. A host asks for a policy; the renderer picks the best mode that this
/// machine actually supports.
/// </para>
/// </summary>
public enum FrameThrottle
{
    /// <summary>FIFO — every frame waits for the display's vertical blank. No tearing, and the
    /// frame rate is hard-capped at the monitor's refresh rate no matter how much headroom the
    /// machine has. Guaranteed supported by the Vulkan spec, so this is also every other mode's
    /// final fallback.</summary>
    VSync,

    /// <summary>
    /// Mailbox if available, else FIFO. The renderer draws as fast as it can and the most recent
    /// finished frame replaces any queued one, so — unlike <see cref="VSync"/> — a frame is never
    /// blocked waiting for the display, yet the image is still swapped only on the vertical blank,
    /// so there is <b>no tearing</b>: uncapped rendering, tear-free output, and the lowest
    /// input-to-photon latency of the three.
    /// <para>
    /// <b>This was the engine's default until 2026-09-03, on the claim that it was "strictly better
    /// than VSync wherever it exists". That claim was wrong, and measurement is what showed it.</b>
    /// Uncapped means exactly that: with nothing else in the loop imposing a ceiling, a machine with
    /// headroom builds frames that are overwritten before they are ever scanned out, and pays for
    /// every one of them in CPU, GPU and allocation. Triage's four-seat Level4 round on a 120 Hz
    /// display measured 434 fps, 88% of a CPU core and 41 MB/s under this policy, against 119 fps,
    /// 41% and 16.5 MB/s under <see cref="VSync"/> — for the same picture. On a thermally or
    /// battery-limited machine that surplus is not free headroom; it is taken out of the frames that
    /// ARE seen. So this is better only where latency is worth more than power, which is a game's
    /// decision and no longer the default one.
    /// </para>
    /// </summary>
    LowLatency,

    /// <summary>
    /// Immediate if available, else Mailbox, else FIFO. The absolute maximum frame rate this machine
    /// can produce, at the cost of visible tearing: a presented image can replace the one being
    /// scanned out mid-scan. Worth choosing to <i>measure</i> a machine's true headroom (see
    /// <c>Aigis.Player.FrameProfiler</c>), and for a player who genuinely prefers latency over a
    /// clean image; not a good default for a game a person is looking at.
    /// </summary>
    Unlimited,
}
