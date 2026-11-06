namespace Aigis.Platform;

/// <summary>Window creation options. <see cref="Default"/>'s <see cref="UpdatesPerSecond"/>
/// aligns with <see cref="Aigis.Foundation.FixedStep.Default"/> so the update cadence
/// matches the engine's fixed-step convention out of the box.</summary>
/// <remarks>Primary-constructor parameters deliberately have no default values: for a
/// record struct, <c>new PlatformWindowOptions()</c> always resolves to the implicit
/// zero-initialized constructor, not the primary constructor with substituted defaults
/// (a well-known C# struct gotcha) — so parameter defaults here would silently never apply.
/// <see cref="Default"/> is instead a fully-specified named instance.</remarks>
public readonly record struct PlatformWindowOptions(
    int WidthPx,
    int HeightPx,
    string Title,
    float UpdatesPerSecond,
    bool VSync,
    double FramesPerSecond)
{
    /// <summary>The <see cref="FramesPerSecond"/> value meaning "whatever this window's display
    /// actually refreshes at, resolved when the window opens". The default, and the only value that
    /// stays correct when the same build is run on a 60 Hz laptop and a 165 Hz desktop.</summary>
    public const double MatchDisplayRefreshRate = -1d;

    /// <summary>The <see cref="FramesPerSecond"/> value meaning "this window applies no ceiling of
    /// its own". The default — and NOT the same thing as "runs unbounded", because the presentation
    /// rate is normally capped by the swapchain's present mode instead (see
    /// <c>Aigis.Render.FrameThrottle</c>, whose vsync policy blocks in the driver and so caps at the
    /// refresh rate far more precisely than any sleep-based wait can). The software limiter exists
    /// for the one thing a present mode cannot express: a ceiling BELOW the refresh rate.</summary>
    public const double Uncapped = 0d;

    /// <summary>The refresh rate assumed when the display declines to report one — a headless CI
    /// box, a remote session, or a driver that returns nothing. 60 rather than "uncapped", because
    /// the failure this whole path exists to prevent is an unbounded loop, and being wrong at 60 on
    /// a 144 Hz panel costs smoothness the player can ask back (see <c>AIGIS_FRAME_CAP</c>) while
    /// being wrong the other way costs them a pinned core with no way to notice.</summary>
    public const double FallbackDisplayRefreshRate = 60d;

    public static readonly PlatformWindowOptions Default = new(
        WidthPx: 1280,
        HeightPx: 720,
        Title: "Aigis",
        UpdatesPerSecond: 60f,
        VSync: true,
        FramesPerSecond: Uncapped);

    /// <summary>
    /// Turns this option's <see cref="FramesPerSecond"/> — which may be
    /// <see cref="MatchDisplayRefreshRate"/>, <see cref="Uncapped"/>, or an explicit rate — into the
    /// concrete ceiling a <see cref="FrameLimiter"/> is built from, given what the display reported.
    /// </summary>
    /// <param name="displayRefreshRateHz">The refresh rate the window's monitor reports, or null
    /// when it reports none.</param>
    public double ResolveFrameRateCap(double? displayRefreshRateHz)
    {
        if (FramesPerSecond == Uncapped)
        {
            return Uncapped;
        }

        var requested = FramesPerSecond == MatchDisplayRefreshRate
            ? displayRefreshRateHz is > 0d and < FrameLimiter.MaximumFramesPerSecond
                ? displayRefreshRateHz.Value
                : FallbackDisplayRefreshRate
            : FramesPerSecond;

        return Math.Clamp(requested, FrameLimiter.MinimumFramesPerSecond, FrameLimiter.MaximumFramesPerSecond);
    }
}
