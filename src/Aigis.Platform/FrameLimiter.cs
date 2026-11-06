using System.Diagnostics;

namespace Aigis.Platform;

/// <summary>
/// Caps how often a window presents, by waiting out the remainder of each frame's period before the
/// next one is drawn.
/// <para>
/// <b>This is NOT what stops the loop running away — vsync is.</b> The presentation ceiling every
/// windowed host gets by default comes from the swapchain's present mode
/// (<c>FrameThrottle.VSync</c>, made the default in the same change that added this type), and the
/// driver enforces it far more precisely than any sleep-based wait can. Measured on Triage's
/// four-seat Level4 round against a 120 Hz display (2026-09-03): the old uncapped mailbox default ran
/// at <b>434 fps, 88% of a CPU core and 41 MB/s of allocation</b>; vsync runs the same round at
/// <b>119 fps, 41% and 16.5 MB/s</b> for the same picture. A software cap set AT the refresh rate was
/// measured too, and was <i>worse</i> than vsync — it held p50 exactly but produced a p99 of 58 ms
/// against vsync's 11 ms, because <see cref="Thread.Sleep(int)"/> overshoots by whatever the OS timer
/// granularity happens to be. So this type is deliberately OFF by default.
/// </para>
/// <para>
/// <b>What it is for is the one ceiling a present mode cannot express: a rate BELOW the display's.</b>
/// On a machine that cannot hold 60 fps, choosing 30 turns an uneven round into a steady one and
/// halves the power drawn again. There the jitter above stops mattering, because vsync still lands
/// each frame on a real refresh boundary — and on a machine already slower than the cap it costs
/// nothing at all, since the wait is simply never reached.
/// </para>
/// <para>
/// <b>Why not <c>WindowOptions.FramesPerSecond</c>.</b> Silk's own limiter was tried first and
/// measured to do nothing at all on this path — 499 fps and 103% of a core with it set to 60 — so
/// relying on it would have been a silent no-op.
/// </para>
/// <para>
/// <b>The wait is sleep-dominant and self-calibrating</b>, which is the whole difficulty. A spin-wait
/// would hold the frame time precisely and burn the core it was supposed to save, and
/// <see cref="Thread.Sleep(int)"/> overshoots by however coarse the OS timer currently is (15.6 ms by
/// default on Windows, ~1 ms once anything in the process has asked for better — a process can and
/// does move between those two while running). So this measures its own sleeps instead of assuming
/// either: it sleeps only while the remaining time exceeds the largest overshoot it has actually
/// observed, and yields for the short tail. On a coarse-timer machine that degrades to yielding, not
/// to missing the deadline.
/// </para>
/// <para>
/// <b>Determinism is unaffected</b> (hard rule 5): this waits, and never reports a time to anybody.
/// The simulation is advanced by <c>FixedStep</c> increments from the accumulating loop as before, so
/// a capped run and an uncapped one step the world identically.
/// </para>
/// </summary>
public sealed class FrameLimiter
{
    /// <summary>Below this many frames per second a cap is treated as a mistake rather than a
    /// request — see <see cref="PlatformWindowOptions.FramesPerSecond"/>, which clamps into this
    /// band.</summary>
    public const double MinimumFramesPerSecond = 10d;

    /// <summary>Above this, a cap cannot express anything a display can show and the wait would be
    /// pure overhead.</summary>
    public const double MaximumFramesPerSecond = 1000d;

    /// <summary>The overshoot budget assumed before a single sleep has been measured. One
    /// millisecond is the optimistic end (a process whose timer resolution has already been raised);
    /// the first sleep that does worse than this immediately widens
    /// <see cref="_observedSleepOvershootMs"/>, so being wrong here costs one frame of jitter and
    /// never a busy-wait.</summary>
    private const double InitialSleepOvershootMs = 1d;

    /// <summary>Never sleep on a remainder smaller than this, whatever the measured overshoot says.
    /// A sleep this short is all overhead and no rest.</summary>
    private const double MinimumSleepRemainderMs = 0.3d;

    /// <summary>How far behind the schedule may fall before the deadline is re-based on now instead
    /// of being caught up. One period: a single slow frame is absorbed by the next one arriving
    /// early, but a 140 ms hitch does not buy the loop eight un-capped frames in a row (which is a
    /// visible surge, and undoes the saving exactly when the machine is least able to afford it).
    /// </summary>
    private const double ResynchronizeLagPeriods = 1d;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double _periodMs;
    private double _nextFrameMs = -1d;
    private double _observedSleepOvershootMs = InitialSleepOvershootMs;

    /// <param name="framesPerSecond">The ceiling, in frames per second. Zero or negative disables the
    /// limiter outright, which is what <see cref="IsEnabled"/> then reports and what makes
    /// <see cref="WaitForNextFrame"/> a no-op — callers therefore never need their own null check.</param>
    public FrameLimiter(double framesPerSecond) =>
        _periodMs = framesPerSecond > 0d && double.IsFinite(framesPerSecond) ? 1000d / framesPerSecond : 0d;

    /// <summary>Whether this limiter caps anything. False for the disabled limiter every uncapped
    /// caller constructs.</summary>
    public bool IsEnabled => _periodMs > 0d;

    /// <summary>The cap in frames per second, or zero when disabled.</summary>
    public double FramesPerSecond => _periodMs > 0d ? 1000d / _periodMs : 0d;

    /// <summary>
    /// Blocks until this frame's slot is due, then returns. Call once per frame <i>before</i> drawing
    /// it, so the frame is presented as soon as its slot opens rather than being finished early and
    /// then held (which would spend the wait as added latency instead of removing it).
    /// </summary>
    public void WaitForNextFrame()
    {
        if (_periodMs <= 0d)
        {
            return;
        }

        var now = _clock.Elapsed.TotalMilliseconds;

        // The first frame sets the schedule rather than being measured against one: there is no
        // previous frame to be early or late relative to, and waiting a full period here would be a
        // period of black window added to the startup time this work is also trying to shorten.
        if (_nextFrameMs < 0d)
        {
            _nextFrameMs = now + _periodMs;
            return;
        }

        while (true)
        {
            now = _clock.Elapsed.TotalMilliseconds;
            var remainingMs = _nextFrameMs - now;
            if (remainingMs <= 0d)
            {
                break;
            }

            if (remainingMs > _observedSleepOvershootMs && remainingMs > MinimumSleepRemainderMs)
            {
                var beforeMs = now;
                Thread.Sleep(1);
                // What that sleep ACTUALLY cost, minus what it was asked for, is this machine's
                // current timer granularity. Kept as a high-water mark rather than a moving average
                // because the failure being avoided is one long sleep blowing through the deadline,
                // and an average is dragged down by the many short ones that did not.
                var overshootMs = _clock.Elapsed.TotalMilliseconds - beforeMs - 1d;
                if (overshootMs > _observedSleepOvershootMs)
                {
                    _observedSleepOvershootMs = overshootMs;
                }

                continue;
            }

            // The tail. Yield rather than spin: on the two-core machine this feature exists for, the
            // other runnable thread is likely to be this game's own audio or the compositor, and
            // handing the core over is worth more than the few microseconds of precision a
            // SpinWait would buy.
            Thread.Yield();
        }

        _nextFrameMs += _periodMs;

        // A frame that ran long (a GC pause, a level load, the first spawn portrait) leaves the
        // schedule in the past. Re-base instead of catching up — see ResynchronizeLagPeriods.
        var lagMs = _clock.Elapsed.TotalMilliseconds - _nextFrameMs;
        if (lagMs > _periodMs * ResynchronizeLagPeriods)
        {
            _nextFrameMs = _clock.Elapsed.TotalMilliseconds + _periodMs;
        }
    }
}
