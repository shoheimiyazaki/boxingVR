using System.Numerics;
using Aigis.Foundation.Diagnostics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Aigis.Platform;

/// <summary>A thin wrapper over a Silk.NET <see cref="IWindow"/>. Knows nothing about
/// Vulkan or physics — <see cref="RawWindow"/> exposes the underlying window (including
/// its <c>VkSurface</c>) for <c>Aigis.Render</c> to build a swapchain against, and
/// <see cref="Input"/> exposes polling-style keyboard/mouse state for a game loop host
/// (<c>Aigis.Player</c>) to read from its Update callback.</summary>
public sealed class PlatformWindow : IDisposable
{
    // Each survived display-query failure is reported once per process (2026-10-15 stability pass).
    private static bool s_refreshRateFailureReported;
    private static bool s_displayScaleFailureReported;

    private readonly IWindow _window;
    private readonly FrameLimiter _frameLimiter;
    private InputSnapshot? _input;

    private PlatformWindow(IWindow window, FrameLimiter frameLimiter)
    {
        _window = window;
        _frameLimiter = frameLimiter;
        _window.Load += OnLoad;
        _window.Update += dt => Updated?.Invoke(dt);
        // The cap is spent HERE, at the top of the frame, rather than after the frame is built: the
        // wait has to displace work that has not happened yet, or it is just latency added to a frame
        // that was already finished. See FrameLimiter for why the loop needs a ceiling at all.
        _window.Render += dt =>
        {
            _frameLimiter.WaitForNextFrame();
            Rendered?.Invoke(dt);
        };
        _window.FramebufferResize += size => Resized?.Invoke(new Vector2(size.X, size.Y));
        _window.Closing += OnClosing;
    }

    public static PlatformWindow Open(PlatformWindowOptions options)
    {
        var windowOptions = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(options.WidthPx, options.HeightPx),
            Title = options.Title,
            UpdatesPerSecond = options.UpdatesPerSecond,
            VSync = options.VSync,
        };
        var window = Window.Create(windowOptions);
        return new PlatformWindow(window, new FrameLimiter(options.ResolveFrameRateCap(DisplayRefreshRateHz(window))));
    }

    /// <summary>What this window's own monitor says it refreshes at, or null if it says nothing.
    /// <para>Asked of the WINDOW's monitor rather than the primary one so a player who drags the game
    /// onto a second screen with a different rate is capped by the screen the game is on. Wrapped
    /// because a backend is entitled to have no monitor concept at all (an offscreen or remote
    /// session), and a missing refresh rate must degrade to
    /// <see cref="PlatformWindowOptions.FallbackDisplayRefreshRate"/> rather than take the window
    /// down — this runs before anything has been drawn, so a throw here is a game that never
    /// opens.</para></summary>
    private static double? DisplayRefreshRateHz(IWindow window)
    {
        try
        {
            return window.Monitor?.VideoMode.RefreshRate;
        }
        catch (Exception error)
        {
            if (!s_refreshRateFailureReported)
            {
                s_refreshRateFailureReported = true;
                ErrorSink.Report("platform: display refresh rate", error);
            }

            return null;
        }
    }

    /// <summary>
    /// Whether this window currently covers the whole display.
    /// <para>
    /// Setting it borrows the size and refresh rate of the monitor the window is already on, and
    /// setting it back restores the size and position the window had before — both of which the
    /// windowing backend does, which is the reason this is a two-line property rather than a mode
    /// table of our own. A no-op when the value is unchanged, so a caller may drive it from a
    /// setting it re-asserts every frame (which <c>Aigis.Player.GameHost</c> does) without the
    /// window flickering.
    /// </para>
    /// <para>
    /// Changing it resizes the framebuffer, so <see cref="Resized"/> fires and a renderer rebuilds
    /// its swapchain through the path it already uses for a dragged window edge — there is no
    /// separate fullscreen path to keep working.
    /// </para>
    /// </summary>
    public bool IsFullscreen
    {
        get => _window.WindowState == WindowState.Fullscreen;
        set
        {
            var wanted = value ? WindowState.Fullscreen : WindowState.Normal;
            if (_window.WindowState == wanted)
            {
                return;
            }

            _window.WindowState = wanted;
        }
    }

    /// <summary>The presentation ceiling this window actually resolved to, in frames per second, or
    /// zero when uncapped. Exposed so a host can report it (the startup profile prints it) and so a
    /// test can assert the resolution rule without opening a real window's worth of frames.</summary>
    public double FrameRateCap => _frameLimiter.FramesPerSecond;

    /// <summary>The underlying Silk.NET window — <c>RawWindow.VkSurface</c> is the hook
    /// <c>Aigis.Render.WindowedRenderer</c> uses to create a Vulkan surface/swapchain.</summary>
    public IWindow RawWindow => _window;

    /// <summary>Polling-style input state. Only valid once <see cref="Loaded"/> has fired
    /// (i.e. from inside an <see cref="Updated"/>/<see cref="Rendered"/> callback or later).</summary>
    public InputSnapshot Input => _input ?? throw new InvalidOperationException(
        "PlatformWindow.Input is only available after Load — read it from Updated/Rendered, not before Run().");

    public event Action? Loaded;

    /// <summary>Fired once per update tick with Silk's raw, possibly-jittery measured
    /// delta seconds — a game loop host must not feed it directly into fixed-step physics
    /// (see <c>Aigis.Player.FixedStepLoop</c>).</summary>
    public event Action<double>? Updated;

    public event Action<double>? Rendered;

    public event Action<Vector2>? Resized;

    public event Action? Closing;

    /// <summary>Blocks, pumping the OS event loop and firing Update/Render, until the
    /// window is closed.</summary>
    public void Run() => _window.Run();

    public void Close() => _window.Close();

    public void Dispose()
    {
        // Normal shutdown already disposed _input from OnClosing, while the native window
        // handle was still alive. This is only a safety net for a window that was never
        // run/closed (e.g. disposed right after construction).
        _input?.Dispose();
        _window.Dispose();
    }

    /// <summary>
    /// Whether the mouse pointer is drawn over this window's client area.
    /// <para>
    /// Hidden means invisible ONLY while over the window — the pointer still moves, still reports
    /// positions and clicks, and reappears as soon as it leaves for the desktop or another app (GLFW's
    /// hidden mode, not its captured "disabled" mode). That is the right thing for a game that hides
    /// the pointer because the player is on a pad: alt-tabbing away must not lose the cursor.
    /// </para>
    /// <para>A no-op when the value is unchanged, so a host may assert it every tick (which
    /// <c>Aigis.Player.GameHost</c> does). Settable before <see cref="Loaded"/>; it is applied when the
    /// input context exists.</para>
    /// </summary>
    public bool PointerVisible
    {
        get => _pointerVisible;
        set
        {
            if (_pointerVisible == value)
            {
                return;
            }

            _pointerVisible = value;
            ApplyPointerVisibility();
        }
    }

    private bool _pointerVisible = true;
    private CursorImage? _cursorImage;
    private IInputContext? _inputContext;

    /// <summary>
    /// Replaces the operating system's arrow with <paramref name="image"/> over this window (null puts
    /// the system arrow back). See <see cref="CursorImage"/> for why this is an OS cursor rather than a
    /// drawn sprite. Settable before <see cref="Loaded"/>; applied when the input context exists.
    /// <para>
    /// A backend that cannot make custom cursors leaves the system arrow up rather than throwing: the
    /// pointer is a nicety and its absence must never cost the player a window.
    /// </para>
    /// </summary>
    public void SetCursorImage(CursorImage? image)
    {
        _cursorImage = image;
        ApplyCursorImage();
    }

    /// <summary>
    /// The display scale of the monitor this window is on — 1 at 100%, 1.5 at 150% — or 1 when the
    /// backend does not say.
    /// <para>
    /// Read live rather than once, because a window dragged to a second screen changes it. The window
    /// size alone cannot answer this: a per-monitor-DPI-aware window's client pixels ARE physical
    /// pixels, so its size reports how big the window is, not how dense the display is.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Asked of Win32 directly (<c>GetDpiForWindow</c>) because Silk.NET's GLFW binding does not
    /// expose <c>glfwGetWindowContentScale</c>, only the per-monitor query, and a windowed window has
    /// no GLFW monitor. Any other platform reports 1 — this game ships on Windows, and an un-scaled
    /// cursor is the old behaviour, not a failure.
    /// </remarks>
    public float ContentScale
    {
        get
        {
            try
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
                    || _window.Native?.Win32 is not { } win32)
                {
                    return 1f;
                }

                var dpi = NativeMethods.GetDpiForWindow(win32.Hwnd);
                return dpi > 0 ? dpi / NativeMethods.DefaultDpi : 1f;
            }
            catch (Exception error)
            {
                // Same degradation as DisplayRefreshRateHz: an unanswerable question about the display
                // is a default, not a reason to fail.
                // Asked every frame: said once, not sixty times a second.
                if (!s_displayScaleFailureReported)
                {
                    s_displayScaleFailureReported = true;
                    ErrorSink.Report("platform: display scale", error);
                }

                return 1f;
            }
        }
    }

    private void ApplyPointerVisibility()
    {
        if (_inputContext is null)
        {
            return;
        }

        var mode = _pointerVisible ? CursorMode.Normal : CursorMode.Hidden;
        foreach (var mouse in _inputContext.Mice)
        {
            if (mouse.Cursor.IsSupported(mode))
            {
                mouse.Cursor.CursorMode = mode;
            }
        }
    }

    private void ApplyCursorImage()
    {
        if (_inputContext is null)
        {
            return;
        }

        foreach (var mouse in _inputContext.Mice)
        {
            var cursor = mouse.Cursor;
            if (_cursorImage is not { } image)
            {
                cursor.Type = CursorType.Standard;
                continue;
            }

            // Image and hotspot BEFORE the type: the GLFW backend rebuilds the native cursor on each
            // of these setters while the type is Custom. Dropping to Standard first (a no-op on the
            // first apply) means a rebuild for a new display scale also builds it exactly once, and
            // never pairs the new image with the old hotspot.
            cursor.Type = CursorType.Standard;
            cursor.Image = new Silk.NET.Core.RawImage(image.Width, image.Height, image.CopyPixels());
            cursor.HotspotX = image.HotspotX;
            cursor.HotspotY = image.HotspotY;
            cursor.Type = CursorType.Custom;
        }
    }

    private void OnLoad()
    {
        _inputContext = _window.CreateInput();
        _input = new InputSnapshot(_inputContext);
        ApplyCursorImage();
        ApplyPointerVisibility();
        Loaded?.Invoke();
    }

    private void OnClosing()
    {
        // Must dispose the input context here, before the window teardown that follows
        // this event — disposing it afterward (e.g. from Dispose()) crashes the GLFW
        // backend, which unregisters callbacks against a native window handle that no
        // longer exists by then.
        _input?.Dispose();
        _input = null;
        _inputContext = null;
        Closing?.Invoke();
    }
}
