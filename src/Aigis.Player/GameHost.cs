using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Foundation;
using Aigis.Platform;
using Aigis.Render;
using Silk.NET.Input;

namespace Aigis.Player;

/// <summary>
/// Wires window + input + fixed-step loop + camera + renderer + startup-profiler + F9 debug
/// capture for an <see cref="IGame{TInput}"/>, so each game's <c>Program.cs</c> only supplies
/// what is genuinely game-specific: title/help text, camera setup, and the per-frame
/// device-input-to-<c>TInput</c> mapping. Everything else — Escape-to-quit, drag-to-orbit,
/// scroll-to-zoom, F9 debug snapshot, status reporting, the loop/renderer/profiler plumbing —
/// lives here exactly once.
/// </summary>
public static class GameHost
{
    /// <summary>Host-owned square resolution for one spawned-patient card photograph — the DEFAULT,
    /// used when a game asks for no size of its own. See
    /// <see cref="SpawnPortraitCaptureRequest.SizePixels"/>.</summary>
    public const int SpawnPortraitSizePixels = 72;

    /// <summary>
    /// Shadow-map edge length for the spawn-portrait renderer, in texels.
    /// <para>
    /// A renderer's shadow maps do not scale with its render target, so this 72-pixel-square renderer
    /// was allocating the same <b>84 MB</b> of device-local memory as the one drawing the whole
    /// window (one sun map plus four fixture maps, 2048&#178; at D32) — and holding it for the entire
    /// session, since the renderer is built once at startup and kept warm. That was over a quarter of
    /// this game's measured 299 MB working set, spent on detail a 72-pixel thumbnail cannot show.
    /// </para>
    /// <para>
    /// <b>1024 rather than smaller, and the difference was measured rather than assumed.</b> Against
    /// a 2048 render of the same portrait, pixel-for-pixel: 512 differs on 36% of pixels (mean
    /// channel delta 19), 1024 on 23% (mean 13) — and before the depth bias was made to scale with
    /// the map size, 512 differed on 87%, because a bias tuned at 2048 under-biases a coarser map
    /// into acne rather than a softer edge. What remains at 1024 is a wider PCF footprint, i.e. a
    /// softer shadow, on a 72-pixel card; the subject reads identically. That takes this renderer's
    /// shadow maps from 84 MB to 21 MB, and 1024 is still fourteen times the output's own
    /// resolution.
    /// </para>
    /// <para>The window's renderer is untouched — see <see cref="ShadowMapConfig.MapSize"/>. Lowering
    /// THAT is the largest memory lever left in this game (another 84 MB), and is deliberately not
    /// taken here: it changes the picture the player is actually looking at, which is a decision for
    /// someone with eyes on it, not a side effect of a memory pass.</para>
    /// </summary>
    public const uint SpawnPortraitShadowMapSize = 1024;

    /// <summary>Background a portrait is photographed against. Named here rather than repeated at
    /// the capture site and the warm-up site, so the throwaway warm-up frame provably exercises the
    /// same pipeline configuration the real captures use.</summary>
    private static readonly Vector4 SpawnPortraitClearColor = new(0.05f, 0.06f, 0.07f, 1f);
    /// <summary>Per-game setup that does not vary per frame.</summary>
    /// <param name="Title">Window title and the line printed after the engine banner.</param>
    /// <param name="HelpText">Controls line printed under the title.</param>
    /// <param name="Camera">The game's camera, already positioned/targeted by the caller — an
    /// <see cref="OrbitCamera"/> for a mouse-orbited game, or a static one-shot fit such as
    /// <c>Aigis.Player.StageFitGameCamera</c>. See <see cref="IGameCamera"/> for why this is an
    /// interface rather than the concrete <see cref="OrbitCamera"/> type.</param>
    /// <param name="DebugLabel">Filename prefix for F9 debug snapshots (e.g. "ballroll"), written to
    /// <c>.visualcheck/debug/</c>.</param>
    /// <param name="OrbitButton">Which mouse button drags the camera — left for every game except
    /// Ragdoll Playground, which reserves left-click for grabbing. Meaningless for a camera that
    /// no-ops <see cref="IGameCamera.ApplyDrag"/>.</param>
    /// <param name="SplitScreenLayout">Presentation layout. Defaults to one full-window view.</param>
    /// <param name="SplitScreenCameras">Cameras for the selected layout. Null uses <paramref name="Camera"/>
    /// for the default full-window layout.</param>
    /// <param name="FrameThrottle">How hard to push presentation. Defaults to
    /// <see cref="Aigis.Render.FrameThrottle.VSync"/> — one presented frame per display refresh.
    /// <para>
    /// <b>This default changed on 2026-09-03, and the reason is power rather than smoothness.</b> It
    /// used to be <see cref="Aigis.Render.FrameThrottle.LowLatency"/> (mailbox), which by design
    /// never blocks: with nothing else in the loop imposing a ceiling, the host drew every frame the
    /// machine could produce and the display showed the newest one. Measured on Triage's four-seat
    /// Level4 round against a 120 Hz display: <b>434 fps, 88% of a CPU core and 41 MB/s of
    /// allocation</b> — about three of every four frames were overwritten before anyone saw them,
    /// having been paid for in full by the CPU, the GPU and the allocator. The same round under vsync
    /// is <b>119 fps, 41% of a core and 16.5 MB/s</b>, and looks identical, because what was removed
    /// was invisible. On the low-specification machines this default is chosen for, that surplus is
    /// not free headroom: it is fan noise, battery, and thermal throttling paid out of the frames
    /// that ARE seen.
    /// </para>
    /// <para>
    /// A game that genuinely wants the lower input latency of mailbox can still ask for it here, and
    /// <c>AIGIS_FRAME_THROTTLE</c> overrides it for a whole run so a measurement pass can ask for
    /// <see cref="Aigis.Render.FrameThrottle.Unlimited"/> without a rebuild.
    /// </para></param>
    /// <param name="DynamicSplitScreen">TASK-180 (tall-building extension, Phase 5): re-queried
    /// EVERY rendered frame instead of using the fixed <see cref="SplitScreenLayout"/>/
    /// <see cref="SplitScreenCameras"/> pair below — the escape hatch for a game whose own view
    /// COUNT changes at runtime (e.g. Triage's ground/roof pair growing a third rider's-eye inset
    /// camera only while an elevator cabin is moving). <see langword="null"/> (the default) keeps
    /// every existing game's behavior byte-for-byte: a fixed layout/camera list captured once, for
    /// the window's whole lifetime, exactly as before this parameter existed. When supplied, this
    /// delegate's returned camera list length MUST equal
    /// <see cref="SplitScreenLayouts.RequiredCameraCount"/> for its returned layout on every call —
    /// checked every frame, so an inconsistent implementation fails loudly rather than silently
    /// misdrawing. <see cref="Camera"/> (not this delegate's cameras) still drives mouse drag/scroll,
    /// since a dynamic list's identity/order can change frame to frame and there is no single
    /// stable "the primary one" to orbit.</param>
    /// <param name="DynamicViews">
    /// TASK-194: the escape hatch for a game that needs to place its own viewport RECTANGLES, not just
    /// pick from <see cref="SplitScreenLayout"/>'s fixed partitions. Queried every rendered frame with
    /// the framebuffer size in pixels; returning null or an empty list (the default, and what a game
    /// without this concept always returns) falls through to
    /// <see cref="Options.DynamicSplitScreen"/>/<see cref="Options.SplitScreenLayout"/> exactly as
    /// before, so no existing game's behaviour changes by a pixel.
    /// <para>
    /// This exists because a layout enum cannot express a rectangle whose position is a GAME's own
    /// layout decision — Triage's result screen draws a picture-in-picture replay window beside a HUD
    /// panel, and the bezel the player sees and the viewport the renderer scissors to have to be the
    /// same rectangle. Putting that rectangle in <see cref="SplitScreenLayouts"/> would put one game's
    /// HUD geometry in the engine; letting the game hand over finished <see cref="CameraView"/>s keeps
    /// it where it belongs. The renderer still validates them (containment and non-overlap), so a
    /// malformed set fails loudly.
    /// </para>
    /// <para>
    /// Unlike a multi-view SPLIT, views supplied this way get <b>full-frame</b> overlay coordinates:
    /// the HUD is laid out against the whole window and is not translated into view[0]'s rectangle,
    /// because a game placing its own viewports is by definition not asking for "the HUD belongs to
    /// the primary view". <see cref="Options.Camera"/> still drives mouse drag/scroll.
    /// </para></param>
    /// <param name="FrameRateCap">An ADDITIONAL software ceiling in frames per second, on top of
    /// whatever <see cref="Options.FrameThrottle"/> already imposes.
    /// <see cref="PlatformWindowOptions.Uncapped"/> by default, which is the right default precisely
    /// because vsync above already caps at the refresh rate, and does it in the driver — far more
    /// precisely than a sleep-based wait can (measured: a software cap set to the refresh rate held
    /// p50 exactly and still produced a p99 of 58 ms, against vsync's 11 ms, because
    /// <see cref="Thread.Sleep(int)"/> overshoots by whatever the OS timer granularity currently is).
    /// <para>
    /// It exists for the one ceiling a present mode cannot express: a rate BELOW the display's. On a
    /// machine that cannot hold 60 fps, asking for 30 turns a stuttering round into an even one and
    /// halves the power drawn again — and there, the limiter's jitter is absorbed, because vsync
    /// still lands each frame on a real refresh boundary. It costs nothing on a machine that is
    /// already slower than the cap: the wait is simply never reached.
    /// </para>
    /// <para><c>AIGIS_FRAME_CAP</c> overrides it for a whole run so a measurement pass needs no
    /// rebuild. See <see cref="FrameLimiter"/> for how the wait itself is kept off the CPU.</para></param>
    /// <param name="HidePointerWhenUnused">Hide the mouse pointer from the tick a key or pad button
    /// goes down until the mouse moves or clicks again — see <see cref="PointerVisibilityTracker"/>
    /// for the rule and why it matches Triage's menu hover. Off by default: a reference scene that
    /// orbits its camera with the mouse while steering with the keyboard would lose its pointer for
    /// nothing.</param>
    /// <param name="CursorImageForScale">The game's own pointer picture, built for a display scale
    /// (1 = 100%). Asked for once at startup and again whenever the window lands on a display with a
    /// different scale, so the picture is always drawn 1:1 (see <see cref="CursorImage"/>). Null keeps
    /// the system arrow, which is what every game had before this existed.</param>
    /// <param name="TimeScale">2026-11-11 (Mini Ring Boxing's slow motion): game seconds per real second,
    /// asked every frame so a game can switch it live. The host still takes one step per
    /// <see cref="FixedStep.Default"/> of REAL time (the picture stays as smooth as at full speed) and passes
    /// each step <c>Dt × TimeScale</c>, so a game that subdivides its step (fixed physics substeps) slows
    /// down without a coarser picture. Null (the default) is 1: every existing game is unchanged.
    /// Non-finite or non-positive values are treated as 1.</param>
    public sealed record Options(
        string Title,
        string HelpText,
        IGameCamera Camera,
        string DebugLabel,
        MouseButton OrbitButton = MouseButton.Left,
        SplitScreenLayout SplitScreenLayout = SplitScreenLayout.Full,
        IReadOnlyList<IGameCamera>? SplitScreenCameras = null,
        FrameThrottle FrameThrottle = FrameThrottle.VSync,
        Func<(SplitScreenLayout Layout, IReadOnlyList<IGameCamera> Cameras)>? DynamicSplitScreen = null,
        Func<Vector2, IReadOnlyList<CameraView>?>? DynamicViews = null,
        double FrameRateCap = PlatformWindowOptions.Uncapped,
        bool HidePointerWhenUnused = false,
        Func<float, CursorImage>? CursorImageForScale = null,
        Func<float>? TimeScale = null);

    /// <summary>
    /// Reads the <c>AIGIS_FRAME_THROTTLE</c> override (<c>vsync</c>/<c>lowlatency</c>/<c>unlimited</c>,
    /// case-insensitive), falling back to <paramref name="fromOptions"/> when unset or unrecognised.
    /// <para>
    /// An env var rather than a CLI flag because every windowed host has to honour it identically for
    /// a measurement to be comparable, and because it pairs with <c>AIGIS_PROFILE_FRAMES</c>: "run
    /// this game uncapped for ten seconds and tell me the frame-time distribution" then needs no
    /// per-game plumbing at all. An unrecognised value is ignored rather than fatal — a typo in a
    /// diagnostic switch must not stop a game from launching.
    /// </para>
    /// </summary>
    private static FrameThrottle ResolveFrameThrottle(FrameThrottle fromOptions) =>
        Environment.GetEnvironmentVariable("AIGIS_FRAME_THROTTLE")?.Trim().ToLowerInvariant() switch
        {
            "vsync" => FrameThrottle.VSync,
            "lowlatency" => FrameThrottle.LowLatency,
            "unlimited" => FrameThrottle.Unlimited,
            _ => fromOptions,
        };

    /// <summary>
    /// Reads the <c>AIGIS_FRAME_CAP</c> override — a number of frames per second,
    /// <c>display</c> for the monitor's own rate, or <c>off</c>/<c>none</c>/<c>uncapped</c> for no
    /// ceiling — falling back to <paramref name="fromOptions"/> when unset or unparseable.
    /// <para>
    /// An env var for the same reason <c>AIGIS_FRAME_THROTTLE</c> is one: a frame-time measurement is
    /// only comparable if every windowed host honours the switch identically, and the pair
    /// <c>AIGIS_FRAME_CAP=off AIGIS_PROFILE_FRAMES=10</c> is exactly the question "what headroom does
    /// this machine really have", which the shipped ceiling would otherwise hide. Parsed with the
    /// invariant culture so a comma-decimal locale reads the same value as everyone else, and an
    /// unrecognised value is ignored rather than fatal — a typo in a diagnostic switch must not stop
    /// a game from launching.
    /// </para>
    /// </summary>
    internal static double ResolveFrameRateCap(double fromOptions)
    {
        var raw = Environment.GetEnvironmentVariable("AIGIS_FRAME_CAP")?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return fromOptions;
        }

        if (raw.Equals("off", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("none", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("uncapped", StringComparison.OrdinalIgnoreCase))
        {
            return PlatformWindowOptions.Uncapped;
        }

        if (raw.Equals("display", StringComparison.OrdinalIgnoreCase))
        {
            return PlatformWindowOptions.MatchDisplayRefreshRate;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps)
            ? fps
            : fromOptions;
    }

    /// <summary>
    /// Runs <paramref name="game"/> in a live window until it closes. <paramref name="profiler"/> is
    /// caller-owned (not constructed here) so a caller can mark its own pre-window milestones — call
    /// <c>profiler.Mark("process start")</c> before constructing the game, then pass it here.
    /// <paramref name="readInput"/> maps raw device state (plus the current aspect ratio and window
    /// pixel size, for cases like Ragdoll Playground's screen-to-ray picking) to the game's own
    /// <typeparamref name="TInput"/> each frame — the one piece every game does differently, so it
    /// stays a delegate. The pixel size is the window's raw size, unclamped (it can be zero, e.g.
    /// while minimized): a caller that divides by it decides for itself what a degenerate size means.
    /// <para>
    /// The game's own <see cref="IGame{TInput}.StatusLine"/> is printed whenever it changes — that is
    /// why it must be low-cardinality — so win/lose/score reporting lives in the game rather than
    /// being re-implemented as bespoke bookkeeping in each <c>Program.cs</c>.
    /// <see cref="IGame{TInput}.BuildOverlayText"/> receives the exact view-projection this frame is
    /// rendered with (computed once in the <see cref="PlatformWindow.Rendered"/> handler below) and
    /// that same frame's pixel size: a world-anchored HUD element is only correctly placed if it uses
    /// the SAME matrix the 3D scene beneath it uses, so passing both through directly makes that a
    /// structural guarantee instead of a "these two usually agree" hope.
    /// </para>
    /// </summary>
    public static HostExit Run<TInput>(
        StartupProfiler profiler,
        Options options,
        IGame<TInput> game,
        Func<InputSnapshot, float, Vector2, TInput> readInput)
    {
        ArgumentNullException.ThrowIfNull(profiler);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(readInput);

        var presentationCameras = options.SplitScreenCameras ?? [options.Camera];
        if (presentationCameras.Count != SplitScreenLayouts.RequiredCameraCount(options.SplitScreenLayout))
        {
            throw new ArgumentException($"{options.SplitScreenLayout} requires exactly {SplitScreenLayouts.RequiredCameraCount(options.SplitScreenLayout)} presentation cameras.", nameof(options));
        }
        for (var i = 0; i < presentationCameras.Count; i++)
        {
            if (presentationCameras[i] is null)
            {
                throw new ArgumentException("Presentation cameras cannot contain null.", nameof(options));
            }
        }

        var primaryPresentationCamera = presentationCameras[0];

        // The fault-containment layer. From here down, every call this host makes INTO game code
        // goes through it, so a bug in one of them costs that call its result rather than costing
        // the player their session — see FaultBarrier's own doc comment for what that does and does
        // not promise, and for AIGIS_STRICT_ERRORS=1, which restores the old "throw and die"
        // behaviour for a debugger session. The argument checks above are deliberately OUTSIDE it:
        // a wrong camera count is a programming error in a Program.cs, caught once at startup, and
        // has no per-frame fallback that would mean anything.
        var barrier = new FaultBarrier(options.DebugLabel);
        FaultBarrier.InstallProcessBackstop(barrier);
        var exit = HostExit.Clean;
        var presentFailed = false;
        var failingSince = new Stopwatch();


        Console.WriteLine($"{EngineInfo.Name} {EngineInfo.Version} — {options.Title}");
        Console.WriteLine(options.HelpText);
        if (StartupProfiler.Enabled)
        {
            Console.WriteLine("(AIGIS_PROFILE_STARTUP=1 — will report timings and exit after the first rendered frame.)");
        }

        var loop = new FixedStepLoop(FixedStep.Default.Dt);
        var frameProfiler = new FrameProfiler();
        var reportedFirstFrame = false;
        var f9WasDown = false;
        var debugCaptureRequested = false;
        var lastStatusLine = barrier.Guard("game.StatusLine", game, static g => g.StatusLine, string.Empty);
        // Rebuilt only when the fault count moves, so the on-screen notice costs nothing per frame
        // in the common case where it is not showing and nothing per frame while it is.
        var noticeFaultCount = 0;
        var noticeText = string.Empty;

        // Set once the renderer has had to leave a decal sheet out (over WorldDecalPipeline.MaxTextures
        // in one frame). The renderer degrades quietly and at no cost per frame; the host reports it
        // ONCE, so the defect still reaches the crash log without a report per frame.
        var decalOverflowReported = false;
        Console.WriteLine(lastStatusLine);

        using var window = PlatformWindow.Open(PlatformWindowOptions.Default with
        {
            Title = options.Title,
            FramesPerSecond = ResolveFrameRateCap(options.FrameRateCap),
        });

        // A frame that fails EVERY time for this long is not going to recover (a lost device, a
        // driver that stopped presenting): end the session with a reason the host can show, rather
        // than leave a frozen or black window that only closing by hand ends -- and that then
        // looked like a clean exit. 600 frames AND ten seconds; a transient failure resets both.
        void EscalateIfPersistent(string site, Exception error)
        {
            if (barrier.ConsecutiveFaultsAt(site) == 1)
            {
                failingSince.Restart();
            }

            // Frames AND wall time: uncapped, a frame that throws at once reaches the frame count in
            // well under a second, and a brief stall (a resize, a minimise) must never end the game.
            if (barrier.ConsecutiveFaultsAt(site) < PersistentRenderFaultFrames
                || failingSince.Elapsed < PersistentRenderFaultTime
                || exit != HostExit.Clean)
            {
                return;
            }

            exit = HostExit.RenderingFailedPersistently;
            barrier.Report(site + " (giving up)", new InvalidOperationException(
                $"'{site}' failed on {PersistentRenderFaultFrames} consecutive frames; closing the window. Last error: {error.GetType().Name}: {error.Message}",
                error));
            window.Close();
        }
        profiler.Mark(window.FrameRateCap > 0d
            ? $"PlatformWindow.Open returned (presentation capped at {window.FrameRateCap:0.#} fps)"
            : "PlatformWindow.Open returned (presentation uncapped)");
        WindowedRenderer? renderer = null;
        // The last frame description presented — what IBackgroundLoadingGame's loading frames redraw.
        HeldFrame? heldFrame = null;
        HeadlessRenderer? portraitRenderer = null;
        // The square edge portraitRenderer was built for. A game may ask for a different one per
        // request (SpawnPortraitCaptureRequest.SizePixels), and a renderer's render target is fixed at
        // construction, so the size it currently holds has to be remembered to know when to rebuild.
        var portraitRendererSize = 0;
        var pointerVisibility = new PointerVisibilityTracker();
        // The display scale the current cursor picture was built for; NaN until one has been built.
        var cursorImageScale = float.NaN;

        void RefreshCursorImage()
        {
            if (options.CursorImageForScale is not { } buildCursor)
            {
                return;
            }

            var scale = window.ContentScale;
            if (scale == cursorImageScale)
            {
                return;
            }

            // Recorded before the build, so a factory that throws is asked once per scale rather
            // than once per tick; the system arrow stays up meanwhile.
            cursorImageScale = scale;
            var image = barrier.Guard("options.CursorImageForScale", (buildCursor, scale), static s => s.buildCursor(s.scale), null!);
            if (image is not null)
            {
                window.SetCursorImage(image);
            }
        }
        // A replacement portrait renderer being built on a worker for a new size (a resize, a
        // fullscreen switch), and the size it is for. Null when none is in flight.
        (Task<HeadlessRenderer> Build, int Size)? portraitRebuild = null;
        // A size whose background rebuild failed: not retried every frame. The request-time rebuild
        // below (the pre-2026-10-02 path) still covers it.
        var failedPortraitRebuildSize = 0;

        window.Loaded += () =>
        {
            profiler.Mark("window Loaded event fired");
            RefreshCursorImage();

            // Startup PREREQUISITES are the one place this layer still ends the session, and only
            // because there is no session left to save: without a renderer nothing can ever be
            // presented, and a black window the player cannot even read an error in is worse than a
            // clear message and a clean close. Every PER-FRAME fault below is contained instead —
            // the window stays up, whatever else is broken.
            if (!barrier.Guard("WindowedRenderer constructor", () =>
                renderer = new WindowedRenderer(window.RawWindow, ResolveFrameThrottle(options.FrameThrottle))))
            {
                Console.Error.WriteLine(
                    "The renderer could not be created, so this window has nothing to draw; closing. "
                    + "The error above (and the report it names) says why.");
                exit = HostExit.RendererUnavailable;
                window.Close();
                return;
            }

            FaultBarrier.SetEnvironmentDetail("gpu", renderer!.DeviceDescription);

            profiler.Mark("WindowedRenderer constructed (Vulkan instance/device/swapchain)");

            // A game that photographs spawned patients gets its offscreen portrait renderer built
            // and WARMED here, not on the frame it first asks for a photograph. Measured on a real
            // Level6 Triage run (AIGIS_PROFILE_FRAMES=20, reproducible to within 0.01 s across
            // runs): leaving it lazy produced a 79 ms frame the moment the first portrait was
            // requested and a 168 ms frame right after — a visible mid-round freeze against a 1.5 ms
            // steady-state frame — because that one frame paid for a second Vulkan device plus its
            // whole pipeline set. The work is unavoidable; WHEN it happens is a choice, and startup
            // (already measured and reported by StartupProfiler) is the honest place for it.
            // Contained rather than fatal, unlike the swapchain renderer above: portraits are one
            // HUD decoration, and a game whose card photographs are missing is still a game. The
            // per-frame capture path below re-attempts the construction anyway, so this failing is
            // a lost warm-up, not a lost feature.
            if (game is ISpawnPortraitCaptureSource portraitSource)
            {
                barrier.Guard("spawn-portrait renderer warm-up", () =>
                {
                    // At the size the game says it will ask for (2026-10-02): warming at the fixed default
                    // and then meeting a different request rebuilt the whole renderer on the gameplay
                    // frame after the first patient arrived — 272-614 ms measured at 1080p.
                    var size = PortraitSizeFor(portraitSource, FramebufferMetrics(window).SizePixels);
                    portraitRenderer = BuildWarmPortraitRenderer(size, game.BuildShadow());
                    portraitRendererSize = size;
                });
                profiler.Mark("spawn-portrait renderer constructed and pipelines warmed");
            }
        };
        window.Resized += _ => barrier.Guard("renderer.NotifyFramebufferResized", () => renderer?.NotifyFramebufferResized());

        window.Updated += realDeltaSeconds =>
        {
            // Plain try/catch rather than a barrier.Guard lambda: this is the CATCH-ALL for the tick,
            // and a closure over realDeltaSeconds would allocate on every one of them. The guards
            // inside are the fine-grained ones — each gives a single game-facing call its own
            // fallback, so a frame usually survives intact; this only catches what they do not
            // cover (input, camera, the loop itself) and keeps that from unwinding out of
            // window.Run() and ending the process.
            var updateStarted = Stopwatch.GetTimestamp();
            try
            {
                var input = window.Input;

                // The pointer, first and unconditionally: it reads this tick's press latches, which
                // EndFrame at the bottom clears, and an early return below (a quit) must not leave a
                // stale state. Presentation only — nothing here reaches Step.
                if (options.HidePointerWhenUnused)
                {
                    window.PointerVisible = pointerVisibility.Update(PointerActivitySample.From(input));
                }

                RefreshCursorImage();

                // TASK-053's binding table maps gamepad Start to "Pause". TASK-130 gives a game the
                // chance to make that literal (its own pause/system menu) via HandleSystemMenuKey
                // rather than always meaning "quit" — see IGame's own doc comment. This is called
                // every tick (not edge-detected) so an override can do its own rising-edge detection
                // and see the key release too; the default implementation always returns false, which
                // reproduces this block's exact pre-TASK-130 behavior for every game that does not
                // override it: held key (or Start) with nothing claiming to have handled it closes the
                // window, checked every tick (not edge-detected) because Close() is idempotent to call
                // repeatedly while the button/key stays held.
                var systemMenuKeyHeld = input.IsKeyDown(Key.Escape) || input.IsGamepadButtonDown(GamepadButton.Start);
                // Fallback false, and that choice is the important one: if the game's own pause-menu
                // handling is what is broken, "nobody handled Escape" is exactly what the player needs,
                // because it falls through to closing the window. The escape hatch must be the last
                // thing that depends on game code working.
                var systemMenuKeyHandled = barrier.Guard(
                    "game.HandleSystemMenuKey",
                    (game, systemMenuKeyHeld),
                    static s => s.game.HandleSystemMenuKey(s.systemMenuKeyHeld),
                    false);
                if (systemMenuKeyHeld && !systemMenuKeyHandled)
                {
                    window.Close();
                    return;
                }

                // A game-driven quit (e.g. "quit game" chosen from a system menu opened above) closes
                // the window through this same one call, rather than the game reaching for a window
                // reference it is never given.
                if (barrier.Guard("game.QuitRequested", game, static g => g.QuitRequested, false))
                {
                    window.Close();
                    return;
                }

                // The same shape for the display mode, which is a preference rather than an event:
                // read it every tick and assign it, and let the window ignore what it is already at
                // (PlatformWindow.IsFullscreen's setter is a no-op when unchanged). Re-asserting is
                // what makes the source of truth unambiguous — the game's setting wins, whatever the
                // window may have been left at. Fallback: the window's own current state, i.e. "leave
                // it alone", because a game whose settings screen is what threw must not also drag the
                // player's window between modes every frame.
                window.IsFullscreen = barrier.Guard(
                    "game.PrefersFullscreen", game, static g => g.PrefersFullscreen, window.IsFullscreen);

                if (input.IsMouseButtonDown(options.OrbitButton))
                {
                    primaryPresentationCamera.ApplyDrag(input.MouseDelta);
                }

                primaryPresentationCamera.ApplyScroll(input.ScrollDelta);

                var f9IsDown = input.IsKeyDown(Key.F9);
                if (f9IsDown && !f9WasDown)
                {
                    debugCaptureRequested = true;
                }

                f9WasDown = f9IsDown;

                var (aspect, windowSizePixels) = WindowMetrics(window);
                // Fallback default(TInput): every game's input struct is built so that "all defaults"
                // means "the player is asking for nothing this tick", so a broken device-mapping leaves
                // the character standing still for one tick instead of running at a wall — and instead
                // of taking the window down.
                var gameInput = barrier.Guard(
                    "readInput",
                    (readInput, input, aspect, windowSizePixels),
                    static s => s.readInput(s.input, s.aspect, s.windowSizePixels),
                    default!);
                var steps = loop.Advance(realDeltaSeconds);
                var timeScale = options.TimeScale?.Invoke() ?? 1f;
                var stepDt = FixedStep.Default.Dt * (float.IsFinite(timeScale) && timeScale > 0f ? timeScale : 1f);
                var stepsStarted = Stopwatch.GetTimestamp();
                for (var i = 0; i < steps; i++)
                {
                    // Guarded per STEP, not per frame: a single bad catch-up step must not silently eat
                    // the rest of this frame's steps as well. A step that throws every time is collapsed
                    // by the barrier's own rate limiter, not by skipping work here.
                    //
                    // Containing this one does NOT mean the round is still correct — a step that threw
                    // halfway has left the world however it left it (see FaultBarrier's doc comment).
                    // It means the player still has a window, a HUD and a way out.
                    barrier.Guard(
                        "game.Step",
                        (game, gameInput, stepDt),
                        static s => s.game.Step(s.gameInput, s.stepDt));
                }

                frameProfiler.AddPhase("step", Stopwatch.GetElapsedTime(stepsStarted).TotalMilliseconds);

                // Fallback: whatever was last printed, so a broken StatusLine reports nothing rather
                // than spamming a transition line every tick.
                var statusLine = barrier.Guard("game.StatusLine", game, static g => g.StatusLine, lastStatusLine);
                if (!string.Equals(statusLine, lastStatusLine, StringComparison.Ordinal))
                {
                    lastStatusLine = statusLine;
                    Console.WriteLine(statusLine);
                }

                input.EndFrame();
            }
            catch (Exception error) when (!barrier.Strict)
            {
                barrier.Report("update", error);
            }

            // The whole tick, steps included: "update" minus "step" is the host's own input/status work.
            frameProfiler.AddPhase("update", Stopwatch.GetElapsedTime(updateStarted).TotalMilliseconds);
        };

        window.Rendered += _ =>
        {
            // Same shape as the update handler above: a plain catch-all around the frame (no closure,
            // so it costs nothing per frame), with fine-grained guards inside giving each game-facing
            // call its own fallback. A game whose HUD throws still gets its 3D scene drawn; a game
            // whose scene throws still gets its HUD, and either way the window keeps running.
            try
            {
                if (renderer is null)
                {
                    return;
                }

                // A game building its next world on another thread is not asked to describe this one —
                // see IBackgroundLoadingGame for why that is the contract rather than an optimisation.
                if (game is IBackgroundLoadingGame loadingGame
                    && barrier.Guard("game.IsLoading", loadingGame, static g => g.IsLoading, false))
                {
                    PresentHeldFrame(loadingGame);
                    return;
                }

                var renderStarted = Stopwatch.GetTimestamp();
                // Fallback: an empty scene. One frame of nothing is a flicker; an exception here used to
                // be the end of the process.
                var (instances, lines) = barrier.Guard(
                    "game.BuildRenderables",
                    game,
                    static g => g.BuildRenderables(),
                    ([], []));
                frameProfiler.AddPhase("buildScene", Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds);
                if (game is ISpawnPortraitCaptureSource portraitCapture
                    && PortraitRendererReady(portraitCapture)
                    && portraitCapture.TryDequeueSpawnPortrait(out var portraitRequest))
                {
                    var portraitStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        portraitRequest.Validate();
                        // 2026-09-19 (「肖像の解像度上げてください」): the game may ask for a size of its
                        // own, because only it knows how large the picture will be drawn. Rebuilding
                        // costs a second Vulkan pipeline set (the warm-up above exists precisely to
                        // keep that off a gameplay frame), so it happens ONLY when the requested size
                        // actually differs -- in practice once per session, on the first portrait,
                        // after which every request asks for the same number until the window resizes.
                        var portraitSize = portraitRequest.SizePixels > 0
                            ? portraitRequest.SizePixels
                            : SpawnPortraitSizePixels;
                        if (portraitRenderer is null || portraitRendererSize != portraitSize)
                        {
                            // Built BEFORE the old one is let go, and swapped in only once it exists: a
                            // constructor that threw between the Dispose and the assignment would leave
                            // portraitRenderer holding a disposed instance and the size still
                            // disagreeing, so the next frame would Dispose that same instance again --
                            // HeadlessRenderer.Dispose is not idempotent, and the handles it destroys
                            // are live Vulkan objects. The catch below swallows the exception; it
                            // cannot repair the field.
                            var rebuilt = new HeadlessRenderer((uint)portraitSize, (uint)portraitSize, SpawnPortraitShadowMapSize);
                            portraitRenderer?.Dispose();
                            portraitRenderer = rebuilt;
                            portraitRendererSize = portraitSize;
                        }

                        var portraitInstances = game is ISpawnPortraitRenderableSource portraitGeometry
                            ? portraitGeometry.BuildSpawnPortraitRenderables(portraitRequest.SubjectId)
                            : instances;
                        var pixels = portraitRenderer.RenderPrimitiveScene(
                            portraitRequest.ClearColor ?? SpawnPortraitClearColor, portraitRequest.ViewProjection, portraitInstances, [],
                            lighting: game.BuildLighting(), shadow: game.BuildShadow(), cameraPosition: portraitRequest.CameraPosition);
                        portraitCapture.CompleteSpawnPortrait(portraitRequest, new TextureData(portraitSize, portraitSize, pixels));
                    }
                    catch (Exception error) when (!barrier.Strict)
                    {
                        // The game is told first (it has its own placeholder for a photograph that never
                        // arrived), and the barrier is told too, so a portrait failing repeatedly shows
                        // up in the same console line and the same report as everything else instead of
                        // being visible only as a missing picture.
                        barrier.Report("spawn portrait", error);
                        barrier.Guard(
                            "ISpawnPortraitCaptureSource.FailSpawnPortrait",
                            (portraitCapture, portraitRequest, error),
                            static s => s.portraitCapture.FailSpawnPortrait(s.portraitRequest, s.error));
                    }

                    frameProfiler.AddPhase("portrait", Stopwatch.GetElapsedTime(portraitStarted).TotalMilliseconds);
                }
                var overlayStarted = Stopwatch.GetTimestamp();
                var (renderAspect, renderSizePixels) = FramebufferMetrics(window);
                if (renderSizePixels.X <= 0f || renderSizePixels.Y <= 0f)
                {
                    return;
                }

                // This frame's viewport rectangles — see BuildFrameViews, which owns the whole
                // DynamicViews/DynamicSplitScreen decision. A game's own layout decision is game code
                // and can therefore be the thing that breaks, so a failure falls back to the FIXED
                // layout the game declared at construction, which by definition cannot depend on this
                // frame's state (for every shipped game but Triage's tall-shaft levels that is the
                // ordinary single full-window view). Only if even that cannot be built is the frame
                // given up on, because at that point there is no rectangle to draw into.
                var frameViews = barrier.Guard(
                    "view layout",
                    (options, presentationCameras, renderSizePixels),
                    static s => BuildFrameViews(s.options, s.presentationCameras, s.renderSizePixels),
                    null)
                    ?? barrier.Guard(
                        "view layout (declared fallback)",
                        (options, presentationCameras, renderSizePixels),
                        static s => ((IReadOnlyList<CameraView> Views, bool UsingExplicitViews, bool CoversWholeTarget)?)(
                            SplitScreenLayouts.Build(s.options.SplitScreenLayout, s.presentationCameras, (int)s.renderSizePixels.X, (int)s.renderSizePixels.Y),
                            false,
                            SplitScreenLayouts.CoversTheWholeTarget(s.options.SplitScreenLayout)),
                        null);
                if (frameViews is not { Views.Count: > 0 } resolvedViews)
                {
                    return;
                }

                var views = resolvedViews.Views;
                var usingExplicitViews = resolvedViews.UsingExplicitViews;
                // TASK-180 follow-up: every world-anchored overlay is still BUILT through view[0] only.
                // every world-anchored overlay (HUD gauges/cards/score popups) is projected through
                // view[0]'s camera ONLY, unconditionally -- for a single-view game this is simply "the"
                // camera, but for TriageShell's tall-building dual-camera split (HorizontalTwo/
                // HorizontalTwoPlusInset) it means every world-anchored HUD element is drawn as if it
                // were visible in view[0]'s floor, even a patient/object actually on the OTHER floor
                // TryWorldToScreen now rejects a point outside that view's Vulkan [0,1] depth band, so
                // another floor cannot overlap this viewport with a false marker. Selecting the matching
                // non-primary viewport for a valid other-floor marker remains a game-owned HUD concern.
                var viewProjection = views[0].ViewProjection;
                var primaryViewport = views[0].Viewport;
                var primaryOverlaySize = views.Count == 1 || usingExplicitViews
                    ? renderSizePixels
                    : new Vector2(primaryViewport.WidthPx, primaryViewport.HeightPx);
                // Guarded one layer at a time rather than as a block: a HUD is three independent
                // descriptions of the same frame, so a game whose text layout throws still gets its
                // panels and its icons drawn, and the player can still read the shape of the screen
                // they were on. Empty is the honest fallback for each — that layer is simply absent
                // this frame.
                var overlayText = barrier.Guard(
                    "game.BuildOverlayText",
                    (game, viewProjection, primaryOverlaySize),
                    static s => s.game.BuildOverlayText(s.viewProjection, s.primaryOverlaySize),
                    []);
                var overlayImages = barrier.Guard(
                    "game.BuildOverlayImages",
                    (game, viewProjection, primaryOverlaySize),
                    static s => s.game.BuildOverlayImages(s.viewProjection, s.primaryOverlaySize),
                    []);
                var overlayShapes = barrier.Guard(
                    "game.BuildOverlayShapes",
                    (game, viewProjection, primaryOverlaySize),
                    static s => s.game.BuildOverlayShapes(s.viewProjection, s.primaryOverlaySize),
                    []);
                // Not drawn in an automatic split layout: the layer's camera is fitted to the whole
                // render size and there is no cheap equivalent of TranslateOverlayShapes for a 3D
                // projection (see IGame.BuildForegroundLayer).
                var foreground = views.Count > 1 && !usingExplicitViews
                    ? null
                    : barrier.Guard(
                        "game.BuildForegroundLayer",
                        (game, renderSizePixels),
                        static s => s.game.BuildForegroundLayer(s.renderSizePixels),
                        null);

                // The one thing the HOST draws over the game: a player in a window has no console, so
                // without this a contained fault is invisible to the only person who could report it.
                // Kept to one line, in both shipped languages, and it clears itself once the game has
                // been healthy for FaultBarrier.NoticeDuration.
                if (barrier.HasFaultWithin(FaultBarrier.NoticeDuration))
                {
                    if (noticeFaultCount != barrier.TotalFaults)
                    {
                        noticeFaultCount = barrier.TotalFaults;
                        noticeText = $"エラーを検出しましたが続行しています ({noticeFaultCount}) / Error contained ({noticeFaultCount}) — the game is still running";
                    }

                    overlayText = WithFaultNotice(overlayText, noticeText, renderSizePixels);
                }

                if (views.Count > 1 && !usingExplicitViews)
                {
                    var primaryOrigin = new Vector2(primaryViewport.XPx, primaryViewport.YPx);
                    overlayText = TranslateOverlayText(overlayText, primaryOrigin);
                    overlayImages = TranslateOverlayImages(overlayImages, primaryOrigin);
                    overlayShapes = TranslateOverlayShapes(overlayShapes, primaryOrigin);
                }

                if (debugCaptureRequested)
                {
                    // Cleared BEFORE the attempt, not after: a capture that throws must not re-arm
                    // itself and then throw again on every subsequent frame.
                    debugCaptureRequested = false;
                    var snapshotPath = barrier.Guard(
                        "F9 debug capture",
                        (game, instances, lines, viewProjection, options, views),
                        static s => DebugCapture.Capture(s.game.World, (s.instances, s.lines), s.viewProjection, Path.Combine(".visualcheck", "debug"), s.options.DebugLabel,
                            lighting: s.game.BuildLighting(), shadow: s.game.BuildShadow(), cameraPosition: s.views[0].Position),
                        null);
                    if (snapshotPath is not null)
                    {
                        Console.WriteLine($"Debug snapshot saved: {snapshotPath}");
                    }
                }

                // The scene-description calls the renderer needs are guarded individually so that one
                // broken description degrades to its documented default instead of costing the frame:
                // no particles, no decals, IGame's own default lighting, no sun shadow pass, the host's
                // default clear colour.
                var clearColor = barrier.Guard("game.BuildClearColor", game, static g => g.BuildClearColor(), IGame<TInput>.DefaultClearColor);
                var particles = barrier.Guard("game.BuildParticles", game, static g => g.BuildParticles(), []);
                var decals = barrier.Guard("game.BuildDecals", game, static g => g.BuildDecals(), []);
                var voxels = barrier.Guard("game.BuildVoxelVolumes", game, static g => g.BuildVoxelVolumes(), []);
                var lighting = barrier.Guard("game.BuildLighting", game, static g => g.BuildLighting(), SceneLighting.Default);
                var shadow = barrier.Guard("game.BuildShadow", game, static g => g.BuildShadow(), null);
                var postProcess = barrier.Guard("game.BuildPostProcess", game, static g => g.BuildPostProcess(), null);
                renderer.RenderScale = barrier.Guard("game.RenderScale", game, static g => g.RenderScale, 1f);
                frameProfiler.AddPhase("overlay", Stopwatch.GetElapsedTime(overlayStarted).TotalMilliseconds);

                // Kept for IBackgroundLoadingGame: while the game is loading, THIS is what is drawn
                // again, so the window keeps a picture without the game being asked for one.
                var frame = new HeldFrame(
                    views, SingleView: views.Count == 1 && !usingExplicitViews,
                    // A split layout deliberately leaves its divider/gutter uncovered and wants it
                    // black, so storeys read as distinct screens rather than one continuous image; a
                    // layout that COVERS the whole target wants the game's own colour, because that is
                    // its backdrop rather than a border. See SplitScreenLayouts.CoversTheWholeTarget for
                    // the day painting it black unconditionally turned Triage's sky black.
                    ClearColor: clearColor,
                    SplitClearColor: resolvedViews.CoversWholeTarget ? clearColor : new Vector4(0f, 0f, 0f, 1f),
                    instances, lines, overlayText, overlayImages, overlayShapes, particles, decals, lighting, shadow, foreground, postProcess, voxels);
                heldFrame = frame;
                var presented = PresentFrame(frame, overlayText, overlayShapes);
                FinishFrame(presented);
                barrier.MarkSucceeded("render");

                if (!decalOverflowReported && renderer!.DecalFramesOverBudget > 0)
                {
                    decalOverflowReported = true;
                    barrier.Report("decal budget", new InvalidOperationException(
                        $"A frame named more distinct decal sheets than the renderer holds; {renderer.DecalSurfacesDroppedLastFrame} " +
                        "were left out of it (and of any later frame that does the same — reported once per launch). The usual cause " +
                        "is a DynamicTexture built every frame instead of once per surface; otherwise raise WorldDecalPipeline.MaxTextures."));
                }
            }
            catch (Exception error) when (!barrier.Strict)
            {
                barrier.Report("render", error);
                EscalateIfPersistent("render", error);
            }
        };

        // Whether a portrait may be taken this frame. False while a renderer for the size the game now
        // wants is still being built on a worker: the request simply waits in the game's queue a few
        // frames rather than the frame stopping for a whole Vulkan device to be created (measured
        // 0.25-0.6 s). Building one on another thread is sound — a HeadlessRenderer is its own
        // VkInstance and device, sharing nothing with the window's renderer — and the swap happens here,
        // on the render thread, where the old one is not in use.
        bool PortraitRendererReady(ISpawnPortraitCaptureSource source)
        {
            if (portraitRebuild is { } pending)
            {
                if (!pending.Build.IsCompleted)
                {
                    return false;
                }

                portraitRebuild = null;
                if (pending.Build.IsCompletedSuccessfully)
                {
                    // The new one is installed BEFORE the old one is let go, so a Dispose that throws
                    // cannot leave the field on a half-disposed renderer (to be disposed again next
                    // frame) with the new device leaked.
                    var previous = portraitRenderer;
                    portraitRenderer = pending.Build.Result;
                    portraitRendererSize = pending.Size;
                    barrier.Guard("spawn-portrait renderer Dispose (replaced)", () => previous?.Dispose());
                }
                else
                {
                    failedPortraitRebuildSize = pending.Size;
                    barrier.Report("spawn-portrait renderer rebuild", pending.Build.Exception!.GetBaseException());
                }
            }

            // No renderer at all means the warm-up failed; the request-time path below owns that case.
            var wanted = barrier.Guard(
                "PreferredSpawnPortraitSizePixels", (source, window),
                static s => PortraitSizeFor(s.source, FramebufferMetrics(s.window).SizePixels),
                portraitRendererSize);
            if (portraitRenderer is null || wanted == portraitRendererSize || wanted == failedPortraitRebuildSize)
            {
                return true;
            }

            // Read on THIS thread: the shadow config is game code, the worker only renders with it.
            var shadow = barrier.Guard("game.BuildShadow", game, static g => g.BuildShadow(), null);
            portraitRebuild = (Task.Run(() => BuildWarmPortraitRenderer(wanted, shadow)), wanted);
            return false;
        }

        // The held frame plus the game's loading panels on top — see IBackgroundLoadingGame. Nothing
        // is asked of the game but the two overlay lists, so its world is never described on this
        // thread while a worker is building the next one.
        void PresentHeldFrame(IBackgroundLoadingGame loadingGame)
        {
            if (renderer is null)
            {
                return;
            }

            var (_, sizePixels) = FramebufferMetrics(window);
            if (sizePixels.X <= 0f || sizePixels.Y <= 0f)
            {
                return;
            }

            // A load that began before anything was ever presented has no frame to hold: draw the
            // loading panels over an empty scene rather than presenting nothing, which is the blank
            // window this contract exists to avoid.
            var held = heldFrame ?? EmptyFrame(sizePixels);

            // Guarded like every other game-facing call: a loading screen that throws costs its own
            // panel, never the window, and the held frame is still a picture.
            var loadingShapes = barrier.Guard(
                "game.BuildLoadingOverlayShapes", (loadingGame, sizePixels),
                static s => s.loadingGame.BuildLoadingOverlayShapes(s.sizePixels), []);
            var loadingText = barrier.Guard(
                "game.BuildLoadingOverlayText", (loadingGame, sizePixels),
                static s => s.loadingGame.BuildLoadingOverlayText(s.sizePixels), []);
            FinishFrame(PresentFrame(
                held,
                loadingText.Count == 0 ? held.Text : [.. held.Text, .. loadingText],
                loadingShapes.Count == 0 ? held.Shapes : [.. held.Shapes, .. loadingShapes]));
        }

        // Plain try/catch, not a guard: the renderer call takes eleven arguments that would have to be
        // packed into a tuple to keep the lambda static, and Report gives it the same console line,
        // repeat counter and report entry either way. `false` is the correct fallback — nothing reached
        // the screen, so nothing downstream may count this as a frame.
        bool PresentFrame(HeldFrame frame, IReadOnlyList<ScreenTextDraw> text, IReadOnlyList<UiShapeDraw> shapes)
        {
            var presentStarted = Stopwatch.GetTimestamp();
            presentFailed = false;
            try
            {
                return frame.SingleView
                    ? renderer!.RenderPrimitiveSceneAndPresent(
                        frame.ClearColor, frame.Views[0].ViewProjection, frame.Instances, frame.Lines,
                        text, frame.Particles, frame.Lighting,
                        shadow: frame.Shadow, cameraPosition: frame.Views[0].Position,
                        images: frame.Images,
                        shapes: shapes,
                        decals: frame.Decals,
                        foreground: frame.Foreground,
                        postProcess: frame.PostProcess,
                        voxels: frame.Voxels)
                    : renderer!.RenderPrimitiveViewsAndPresent(
                        frame.SplitClearColor,
                        frame.Views, frame.Instances, frame.Lines,
                        particles: frame.Particles,
                        text: text,
                        images: frame.Images,
                        lighting: frame.Lighting,
                        shadow: frame.Shadow,
                        shapes: shapes,
                        decals: frame.Decals,
                        foreground: frame.Foreground,
                        postProcess: frame.PostProcess,
                        voxels: frame.Voxels);
            }
            catch (Exception renderFailure) when (!barrier.Strict)
            {
                presentFailed = true;
                barrier.Report("renderer present", renderFailure);
                EscalateIfPersistent("renderer present", renderFailure);
                return false;
            }
            finally
            {
                frameProfiler.AddPhase("present", Stopwatch.GetElapsedTime(presentStarted).TotalMilliseconds);
            }
        }

        void FinishFrame(bool presented)
        {
            if (!presentFailed)
            {
                barrier.MarkSucceeded("renderer present");
            }

            if (presented && !reportedFirstFrame)
            {
                reportedFirstFrame = true;
                profiler.Mark("first frame presented (includes first-time shader/pipeline compile)");

                // The first frame pays for every pipeline compile and first-time upload in the
                // process, which would dominate a six-phase mean of a few hundred frames. Dropped
                // here rather than being excluded phase by phase, so the render breakdown covers the
                // same steady state AIGIS_PROFILE_FRAMES samples.
                renderer!.RenderPhases.Reset();
                if (StartupProfiler.Enabled)
                {
                    profiler.Report();
                    window.Close();
                }
            }

            // Steady-state frame timing (AIGIS_PROFILE_FRAMES) — only counts frames that actually
            // presented, so a skipped frame (minimized window, swapchain recreation) is not
            // recorded as an impossibly fast one. Inert unless the env var is set.
            if (presented && frameProfiler.RecordFrameAndCheckDone())
            {
                frameProfiler.Report();

                // Printed HERE, not by the renderer: Aigis.Render is a library and may not touch the
                // console (hard rule 2). Empty unless AIGIS_PROFILE_RENDER=1 asked for it.
                if (renderer!.RenderPhases.Report() is { Length: > 0 } renderReport)
                {
                    Console.WriteLine($"  target: {renderer.TargetSize.Width}x{renderer.TargetSize.Height} px");
                    Console.WriteLine(renderReport);
                }

                window.Close();
            }
        }

        try
        {
            window.Run();
        }
        catch (Exception error) when (!barrier.Strict)
        {
            // Last resort. Everything the host itself calls is guarded above, so reaching this means
            // the platform's own loop threw — a driver callback, a window-system failure. The window
            // is over either way; what this buys is that the session ends with a described fault and
            // a report file instead of an unhandled-exception dialog the player cannot copy.
            barrier.Report("window.Run", error);
            exit = HostExit.LoopFailed;
        }
        finally
        {
            // Guarded because a finally that throws replaces whatever was actually wrong with a
            // Dispose failure, and because the second Dispose must still run if the first one fails.
            barrier.Guard("portrait renderer Dispose", () => portraitRenderer?.Dispose());
            // A rebuild still in flight finishes and is released too — it holds a whole Vulkan device.
            barrier.Guard("portrait renderer rebuild Dispose", () => portraitRebuild?.Build.GetAwaiter().GetResult().Dispose());
            barrier.Guard("renderer Dispose", () => renderer?.Dispose());

            // Null for a clean session, so an ordinary run's console is exactly as it was.
            if (barrier.Summary() is { } summary)
            {
                Console.Error.WriteLine(summary);
            }
        }

        return exit;
    }

    /// <summary>Consecutive failing frames after which <see cref="Run{TInput}"/> gives up on a window
    /// that can no longer draw (<see cref="HostExit.RenderingFailedPersistently"/>).</summary>
    public const int PersistentRenderFaultFrames = 600;

    /// <summary>...and for at least this long — see <see cref="PersistentRenderFaultFrames"/>.</summary>
    public static readonly TimeSpan PersistentRenderFaultTime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// This frame's viewport rectangles: TASK-194's explicit <see cref="Options.DynamicViews"/> if
    /// the game supplied any, else TASK-180's <see cref="Options.DynamicSplitScreen"/>, else the
    /// fixed layout the game declared at construction. Extracted from the render handler so the
    /// whole decision — including the validation that fails a malformed set loudly — is ONE guarded
    /// call with one fallback, rather than three branches each needing their own.
    /// </summary>
    private static (IReadOnlyList<CameraView> Views, bool UsingExplicitViews, bool CoversWholeTarget)? BuildFrameViews(
        Options options, IReadOnlyList<IGameCamera> presentationCameras, Vector2 renderSizePixels)
    {
        // TASK-194: a game placing its own viewport rectangles wins over any layout enum — see
        // Options.DynamicViews. Null/empty (every game but Triage's result screen, and Triage's
        // own every other frame) falls through to the pre-existing layout path unchanged.
        var explicitViews = options.DynamicViews?.Invoke(renderSizePixels);
        if (explicitViews is { Count: > 0 })
        {
            for (var i = 0; i < explicitViews.Count; i++)
            {
                explicitViews[i].Validate();
            }

            // Explicit views are the game's own rectangles and it may well leave gaps between them, so
            // the clear colour is a DIVIDER by default -- see SplitScreenLayouts.CoversTheWholeTarget
            // for what that decision is actually about.
            //
            // 2026-09-06: unless one of them covers the whole target, in which case there is no gap for
            // a divider to be and the clear colour is the game's own BACKDROP. That distinction is not
            // cosmetic on a game whose sky is beyond its own far plane (Triage's is: the shell sits at
            // 900 m and the stage fit's far clip is under 100), where clearing to black paints the sky
            // black -- the exact defect CoversTheWholeTarget's own doc comment records from the day the
            // rooftop map grew an overlaid pane. It matters here now because that map's panes moved
            // onto THIS path when there could be several of them, and an OVERLAY view re-clears its own
            // rectangle to this colour before drawing, so a wrong answer blacks out the pane's sky too.
            return (explicitViews, true, AnyViewCoversTheWholeTarget(explicitViews, renderSizePixels));
        }

        var (frameLayout, frameCameras) = options.DynamicSplitScreen is { } dynamicSplitScreen
            ? dynamicSplitScreen()
            : (options.SplitScreenLayout, presentationCameras);
        var requiredCameraCount = SplitScreenLayouts.RequiredCameraCount(frameLayout);
        if (frameCameras.Count != requiredCameraCount)
        {
            throw new InvalidOperationException(
                $"{nameof(options.DynamicSplitScreen)} returned {frameCameras.Count} camera(s) for {frameLayout}, which requires exactly {requiredCameraCount}.");
        }

        return (
            SplitScreenLayouts.Build(frameLayout, frameCameras, (int)renderSizePixels.X, (int)renderSizePixels.Y),
            false,
            SplitScreenLayouts.CoversTheWholeTarget(frameLayout));
    }

    /// <summary>
    /// Whether <paramref name="views"/> leave no pixel of the framebuffer uncovered — see the call
    /// site for why the answer decides what the clear colour MEANS.
    ///
    /// <para>Two shapes answer yes. One full-frame view with panes over it (the tower map's). And,
    /// since 2026-10-10, non-overlay views that TILE the target between them: Triage's third-person
    /// handoff splits one picture into per-player slices that must still read as that one picture, so
    /// the gaps a divider would paint do not exist and the sky must stay the sky. The tiling test is an
    /// area sum over the non-overlay views clipped to the target, which is exact because the renderer
    /// already rejects overlapping non-overlay views (<c>ValidateViewsForSwapchain</c>).</para>
    /// </summary>
    public static bool AnyViewCoversTheWholeTarget(IReadOnlyList<CameraView> views, Vector2 renderSizePixels)
    {
        var width = (int)renderSizePixels.X;
        var height = (int)renderSizePixels.Y;
        long tiledArea = 0;
        foreach (var view in views)
        {
            var viewport = view.Viewport;
            if (viewport.XPx <= 0 && viewport.YPx <= 0
                && viewport.WidthPx >= width && viewport.HeightPx >= height)
            {
                return true;
            }

            if (!view.IsOverlay)
            {
                var clippedWidth = Math.Min(viewport.XPx + viewport.WidthPx, width) - Math.Max(viewport.XPx, 0);
                var clippedHeight = Math.Min(viewport.YPx + viewport.HeightPx, height) - Math.Max(viewport.YPx, 0);
                if (clippedWidth > 0 && clippedHeight > 0)
                {
                    tiledArea += (long)clippedWidth * clippedHeight;
                }
            }
        }

        return width > 0 && height > 0 && tiledArea >= (long)width * height;
    }

    /// <summary>
    /// Appends the host's contained-error notice to a frame's HUD text. Bottom-left, above the
    /// window edge, in a warm red at full alpha — the design system's own warning that a faded glyph
    /// disintegrates rather than dims (docs/ui-design-system.md) rules out signalling this with
    /// transparency, so the notice simply appears and disappears.
    /// <para>
    /// It is written in both shipped languages on one line rather than localised, because
    /// <see cref="GameHost"/> is engine-side and has no access to a game's string catalog — and
    /// because the one moment a player needs to understand this message is the moment the code that
    /// would have translated it may be what failed.
    /// </para>
    /// </summary>
    private static ScreenTextDraw[] WithFaultNotice(
        IReadOnlyList<ScreenTextDraw> overlayText, string notice, Vector2 renderSizePixels)
    {
        var withNotice = new ScreenTextDraw[overlayText.Count + 1];
        for (var i = 0; i < overlayText.Count; i++)
        {
            withNotice[i] = overlayText[i];
        }

        withNotice[^1] = new ScreenTextDraw(
            notice,
            new Vector2(16f, MathF.Max(0f, renderSizePixels.Y - 34f)),
            18f,
            new Vector4(1f, 0.42f, 0.34f, 1f));
        return withNotice;
    }

    /// <summary>The portrait size <paramref name="source"/> will request at this framebuffer, or the host
    /// default when it does not say (0) or says something out of range.</summary>
    private static int PortraitSizeFor(ISpawnPortraitCaptureSource source, Vector2 framebufferSizePixels)
    {
        var preferred = source.PreferredSpawnPortraitSizePixels(framebufferSizePixels);
        return preferred is > 0 and <= SpawnPortraitCaptureRequest.MaxSizePixels ? preferred : SpawnPortraitSizePixels;
    }

    /// <summary>A portrait renderer of <paramref name="sizePixels"/>, with every pipeline it can need
    /// already compiled: one throwaway frame of an empty scene does that (see
    /// HeadlessRenderer.Primitives.cs's own Ensure* prologue), so no game geometry is needed. Safe off
    /// the main thread — the renderer is its own VkInstance and device.</summary>
    private static HeadlessRenderer BuildWarmPortraitRenderer(int sizePixels, ShadowMapConfig? shadow)
    {
        var renderer = new HeadlessRenderer((uint)sizePixels, (uint)sizePixels, SpawnPortraitShadowMapSize);
        try
        {
            renderer.RenderPrimitiveScene(SpawnPortraitClearColor, Matrix4x4.Identity, [], [], shadow: shadow);
            return renderer;
        }
        catch
        {
            renderer.Dispose();
            throw;
        }
    }

    /// <summary>A frame with nothing in it, for a load that starts before any frame was presented.</summary>
    private static HeldFrame EmptyFrame(Vector2 sizePixels) => new(
        [CameraView.Create(
            new RenderViewport(0, 0, (int)sizePixels.X, (int)sizePixels.Y), Vector3.Zero, static _ => Matrix4x4.Identity)],
        SingleView: true,
        ClearColor: IGame<object>.DefaultClearColor,
        SplitClearColor: IGame<object>.DefaultClearColor,
        Instances: [], Lines: [], Text: [], Images: [], Shapes: [], Particles: [], Decals: [],
        Lighting: SceneLighting.Default, Shadow: null, Foreground: null);

    /// <summary>One presented frame's complete description, kept so an
    /// <see cref="IBackgroundLoadingGame"/>'s loading frames can draw it again without asking the game.
    /// The lists are held BY REFERENCE (some are a game's reused buffers), which is only sound because
    /// nothing refills them while the game is loading — see that interface.</summary>
    private sealed record HeldFrame(
        IReadOnlyList<CameraView> Views,
        bool SingleView,
        Vector4 ClearColor,
        Vector4 SplitClearColor,
        IReadOnlyList<PrimitiveInstance> Instances,
        IReadOnlyList<DebugLine> Lines,
        IReadOnlyList<ScreenTextDraw> Text,
        IReadOnlyList<OverlayImageDraw> Images,
        IReadOnlyList<UiShapeDraw> Shapes,
        IReadOnlyList<ParticleInstance> Particles,
        IReadOnlyList<DecalQuadDraw> Decals,
        SceneLighting Lighting,
        ShadowMapConfig? Shadow,
        ForegroundLayer? Foreground,
        PostProcessSettings? PostProcess = null,
        IReadOnlyList<Aigis.Render.Voxels.VoxelVolumeDraw>? Voxels = null);

    private static (float Aspect, Vector2 SizePixels) WindowMetrics(PlatformWindow window)
    {
        var size = window.RawWindow.Size;
        var aspect = size.Y == 0 ? 1f : (float)size.X / size.Y;
        // SizePixels is passed through raw (can be zero, e.g. a minimized window) rather than
        // clamped here: a consumer that divides by it (Camera.ScreenPointToRay) needs to make its
        // own call on what a degenerate size means for its input, the same way the pre-GameHost
        // Program.cs did (see games/Game.RagdollPlayground/Program.cs's own zero-size branch).
        var sizePixels = new Vector2(size.X, size.Y);
        return (aspect, sizePixels);
    }

    /// <summary>Framebuffer-pixel metrics for Vulkan viewport, projection, and screen-space rendering.</summary>
    private static (float Aspect, Vector2 SizePixels) FramebufferMetrics(PlatformWindow window)
    {
        var size = window.RawWindow.FramebufferSize;
        var aspect = size.Y == 0 ? 1f : (float)size.X / size.Y;
        return (aspect, new Vector2(size.X, size.Y));
    }

    /// <summary>Converts primary-view-local text coordinates into the renderer's full-frame HUD space.</summary>
    private static ScreenTextDraw[] TranslateOverlayText(IReadOnlyList<ScreenTextDraw> draws, Vector2 originPixels)
    {
        var translated = new ScreenTextDraw[draws.Count];
        for (var i = 0; i < draws.Count; i++)
        {
            translated[i] = draws[i] with { ScreenPosPixels = draws[i].ScreenPosPixels + originPixels };
        }

        return translated;
    }

    /// <summary>Converts primary-view-local image coordinates into the renderer's full-frame HUD space.</summary>
    private static OverlayImageDraw[] TranslateOverlayImages(IReadOnlyList<OverlayImageDraw> draws, Vector2 originPixels)
    {
        var translated = new OverlayImageDraw[draws.Count];
        for (var i = 0; i < draws.Count; i++)
        {
            translated[i] = draws[i] with { CenterPixels = draws[i].CenterPixels + originPixels };
        }

        return translated;
    }

    /// <summary>Converts primary-view-local shape coordinates into the renderer's full-frame HUD space.</summary>
    private static UiShapeDraw[] TranslateOverlayShapes(IReadOnlyList<UiShapeDraw> draws, Vector2 originPixels)
    {
        var translated = new UiShapeDraw[draws.Count];
        for (var i = 0; i < draws.Count; i++)
        {
            translated[i] = draws[i] with { CenterPixels = draws[i].CenterPixels + originPixels };
        }

        return translated;
    }
}
