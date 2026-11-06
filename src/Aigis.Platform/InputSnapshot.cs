using System.Numerics;
using Silk.NET.Input;

namespace Aigis.Platform;

/// <summary>The gamepad buttons this engine actually needs (TASK-053) — deliberately not a
/// 1:1 mirror of Silk.NET's full <see cref="ButtonName"/> set (no D-pad, no stick-click, no
/// home/share/paddles: nothing in this engine's games binds them yet, and speculatively
/// exposing them would just be untested surface). Named by physical layout position
/// (South/East/West/North), not by an Xbox/PlayStation-specific letter, since Silk.NET itself
/// normalizes different pads to the same <see cref="ButtonName.A"/>/<see cref="ButtonName.B"/>/etc.
/// regardless of brand.</summary>
public enum GamepadButton
{
    /// <summary>Xbox A / PlayStation Cross.</summary>
    South,

    /// <summary>Xbox B / PlayStation Circle.</summary>
    East,

    /// <summary>Xbox X / PlayStation Square.</summary>
    West,

    /// <summary>Xbox Y / PlayStation Triangle.</summary>
    North,

    LeftShoulder,
    RightShoulder,
    Start,

    /// <summary>Xbox "Back"/"View" / PlayStation "Select"/"Share" — Silk.NET normalizes all of
    /// these to <see cref="ButtonName.Back"/> regardless of the pad's actual silkscreened label.</summary>
    Select,

    // The directional pad (2026-09-21). A pad's DIGITAL direction, as four buttons rather than as a
    // second thumbstick, because that is what the hardware reports and what a menu wants: a stick has
    // a dead zone and an analogue magnitude a cursor has to threshold, and a d-pad press is already
    // exactly one unambiguous step.
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
}

/// <summary>Polling-style input reader over the connected keyboard, mouse and gamepads.
/// <para><b>One keyboard and one mouse, but ALL gamepads</b> (2026-08-20). The asymmetry is not an
/// oversight: an operating system merges every attached keyboard into one event stream, so "the second
/// keyboard" is not a thing a game can read — local keyboard co-op is done by splitting the KEYS (see
/// Triage's WASD/arrow split). Gamepads are genuinely enumerable and distinguishable, so a four-pad
/// living room is addressable and <see cref="GamepadCount"/>/<see cref="IsGamepadButtonDown(int, GamepadButton)"/>/
/// <see cref="GetGamepadLeftStick"/> address it. The index-free overloads mean gamepad 0 and are what
/// every single-player caller should keep using.</para>
/// <see cref="MousePosition"/>/<see cref="MouseDelta"/> are in window client-space
/// pixels (origin top-left, Y-down) — callers that need world-space angles (e.g. an
/// orbit camera) convert explicitly at their own boundary, not here.
/// <para><b>Gamepad degradation (TASK-053):</b> every gamepad query below mirrors the
/// keyboard/mouse convention already established here — "no device connected" returns a sane
/// neutral value (<see langword="false"/>, <see cref="Vector2.Zero"/>), it never throws. This
/// mirrors <see cref="IsKeyDown"/>/<see cref="IsMouseButtonDown"/>'s own <c>Count > 0 &amp;&amp;
/// ...</c> pattern exactly, just against <see cref="IInputContext.Gamepads"/> instead.</para>
/// <para><b>No tap/hold or consumed-edge state lives here, deliberately.</b> This type is a
/// stateless, per-real-frame device poll — it has no notion of a fixed simulation step at all,
/// by design (see the class doc above about pixels/angles). Triage's Carry/Drag actions need a
/// tap-vs-hold split and one-shot "consumed edge" query
/// (<c>Game.Triage.TapHoldGesture</c>, docs/plans/triage-full-port.md's Part 2 spec), both of
/// which are inherently duration-based and must run off the FIXED step (Hard rule 5) to stay
/// deterministic — a real-frame-driven poller like this one cannot supply that without becoming
/// wall-clock-timed itself. That gesture logic therefore lives game-side, driven once per
/// <c>TriageGame.Step</c> tick, not here — see <c>TapHoldGesture</c>'s own doc comment for the
/// full reasoning.</para></summary>
public sealed class InputSnapshot : IDisposable
{
    private static readonly Dictionary<GamepadButton, ButtonName> ButtonNameByGamepadButton = new()
    {
        [GamepadButton.South] = ButtonName.A,
        [GamepadButton.East] = ButtonName.B,
        [GamepadButton.West] = ButtonName.X,
        [GamepadButton.North] = ButtonName.Y,
        [GamepadButton.LeftShoulder] = ButtonName.LeftBumper,
        [GamepadButton.RightShoulder] = ButtonName.RightBumper,
        [GamepadButton.Start] = ButtonName.Start,
        [GamepadButton.Select] = ButtonName.Back,
        [GamepadButton.DPadUp] = ButtonName.DPadUp,
        [GamepadButton.DPadDown] = ButtonName.DPadDown,
        [GamepadButton.DPadLeft] = ButtonName.DPadLeft,
        [GamepadButton.DPadRight] = ButtonName.DPadRight,
    };

    /// <summary>
    /// Gamepad <paramref name="gamepadIndex"/>'s directional pad as a direction, X right and Y up,
    /// each axis −1, 0 or +1.
    /// </summary>
    /// <remarks>
    /// <b>Raw axes, like <see cref="GetGamepadLeftStick"/></b> — this type does not know which way a
    /// given game's world faces, so it reports the pad and leaves the conversion to the caller's own
    /// boundary. Opposite directions held at once cancel, which is what a pad that reports both
    /// (a worn d-pad, a rocking thumb) should read as rather than as an arbitrary winner.
    /// <para><see cref="Vector2.Zero"/>, never throwing, for an index with no gamepad on it — this
    /// type's whole degradation convention.</para>
    /// </remarks>
    public Vector2 GetGamepadDPad(int gamepadIndex)
    {
        var x = (IsGamepadButtonDown(gamepadIndex, GamepadButton.DPadRight) ? 1f : 0f)
            - (IsGamepadButtonDown(gamepadIndex, GamepadButton.DPadLeft) ? 1f : 0f);
        var y = (IsGamepadButtonDown(gamepadIndex, GamepadButton.DPadUp) ? 1f : 0f)
            - (IsGamepadButtonDown(gamepadIndex, GamepadButton.DPadDown) ? 1f : 0f);
        return new Vector2(x, y);
    }

    private readonly IInputContext _context;
    private readonly PressLatch<Key> _keyPresses = new();
    private readonly PressLatch<MouseButton> _mouseButtonPresses = new();
    private readonly PressLatch<Key> _keyReleases = new();
    private readonly PressLatch<MouseButton> _mouseButtonReleases = new();
    private Vector2 _previousMousePosition;
    private float _accumulatedScroll;

    internal InputSnapshot(IInputContext context)
    {
        _context = context;
        _previousMousePosition = CurrentMousePosition();
        foreach (var mouse in _context.Mice)
        {
            mouse.Scroll += (_, wheel) => _accumulatedScroll += wheel.Y;
            mouse.MouseDown += (_, button) => _mouseButtonPresses.Note(button);
            mouse.MouseUp += (_, button) => _mouseButtonReleases.Note(button);
        }

        foreach (var keyboard in _context.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => _keyPresses.Note(key);
            keyboard.KeyUp += (_, key, _) => _keyReleases.Note(key);
        }
    }

    /// <summary>
    /// Whether <paramref name="key"/> is down RIGHT NOW.
    ///
    /// <para>For anything that behaves as a press — a menu button, a confirm — prefer
    /// <see cref="WasKeyPressed"/>: a poll can only ever see a key that is still down when it happens
    /// to run, and this engine polls once per update tick.</para>
    /// </summary>
    public bool IsKeyDown(Key key) => _context.Keyboards.Count > 0 && _context.Keyboards[0].IsKeyPressed(key);

    /// <inheritdoc cref="IsKeyDown"/>
    public bool IsMouseButtonDown(MouseButton button) => _context.Mice.Count > 0 && _context.Mice[0].IsButtonPressed(button);

    /// <summary>
    /// Whether <paramref name="key"/> went down at any point since the last <see cref="EndFrame"/> —
    /// even if it was released again before anyone looked.
    ///
    /// <para><b>Event-sourced, not polled, and that is the whole point.</b> Silk reports key and button
    /// STATE, so <see cref="IsKeyDown"/> can only answer for the instant it is called; a press and
    /// release that both fall between two update ticks is invisible to it. At this engine's 60 Hz
    /// update cadence that is a ~17 ms window, which a deliberate tap clears easily and a quick one
    /// does not — the shape of 2026-08-28's report that menu buttons 「クリックではなく、長押しでないと
    /// 反応しない」. Subscribing to the device's own down event cannot miss a press however short,
    /// because the OS delivered it as an event rather than as a state to be caught in the act.</para>
    ///
    /// <para><b>Cleared by <see cref="EndFrame"/>, which is what makes it a per-tick edge</b> rather
    /// than a flag that latches forever. That places one requirement on the host: call
    /// <see cref="EndFrame"/> once per update tick, AFTER the tick has consumed its input — which
    /// <c>GameHost.Run</c> does, and which is sound because
    /// <see cref="PlatformWindowOptions.Default"/>'s update cadence is deliberately the engine's own
    /// fixed-step rate (see that type's doc comment). A host that stepped the simulation less often
    /// than it pumped the window would need to move this clear to its own step boundary instead.</para>
    /// </summary>
    public bool WasKeyPressed(Key key) => _keyPresses.WasPressed(key);

    /// <inheritdoc cref="WasKeyPressed"/>
    public bool WasMouseButtonPressed(MouseButton button) => _mouseButtonPresses.WasPressed(button);

    /// <summary>
    /// EVERY key that went down this frame, bound or not (2026-09-21).
    /// <para>
    /// <b>For recording what a person did, not for driving a game.</b> Gameplay asks about the keys
    /// it has bound (<see cref="WasKeyPressed"/>); an input trace has to see the ones nothing is
    /// bound to, because "they pressed the wrong key" is exactly the finding a control-scheme
    /// question is after. Every key event the window received is in here — this is a device-level
    /// record and it belongs in a diagnostic file, never in a simulation step (hard rule 5).
    /// </para>
    /// </summary>
    public IReadOnlyCollection<Key> KeysPressedThisFrame => _keyPresses.Pressed;

    /// <summary>Every key that came back up this frame — the other half of a hold's duration.</summary>
    public IReadOnlyCollection<Key> KeysReleasedThisFrame => _keyReleases.Pressed;

    /// <inheritdoc cref="KeysPressedThisFrame"/>
    public IReadOnlyCollection<MouseButton> MouseButtonsPressedThisFrame => _mouseButtonPresses.Pressed;

    /// <inheritdoc cref="KeysReleasedThisFrame"/>
    public IReadOnlyCollection<MouseButton> MouseButtonsReleasedThisFrame => _mouseButtonReleases.Pressed;

    /// <summary>
    /// How many gamepads are connected right now.
    /// <para>Added 2026-08-20 for local multiplayer: a menu that offers "player 3 on a gamepad" has to
    /// know whether a third gamepad exists, and an option that can be picked but cannot work is worse
    /// than one that is visibly unavailable. Re-read on every access rather than cached, because pads
    /// are plugged and unplugged while a game is running.</para>
    /// </summary>
    public int GamepadCount => _context.Gamepads.Count;

    /// <summary>True if <paramref name="button"/> is currently held on the first connected
    /// gamepad — <see langword="false"/> (never throws) with no gamepad connected, or if that
    /// gamepad's backend never reports a button with this name.</summary>
    public bool IsGamepadButtonDown(GamepadButton button) => IsGamepadButtonDown(0, button);

    /// <summary>
    /// True if <paramref name="button"/> is held on gamepad <paramref name="gamepadIndex"/>.
    /// <para><see langword="false"/> (never throws) for an index with no gamepad on it, matching this
    /// type's whole degradation convention — a seat bound to a pad that has just been unplugged reads as
    /// a player who is not pressing anything, which is the behaviour that leaves their character
    /// standing still rather than crashing the round.</para>
    /// </summary>
    public bool IsGamepadButtonDown(int gamepadIndex, GamepadButton button)
    {
        if (gamepadIndex < 0 || gamepadIndex >= _context.Gamepads.Count)
        {
            return false;
        }

        var targetName = ButtonNameByGamepadButton[button];
        var buttons = _context.Gamepads[gamepadIndex].Buttons;
        for (var i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].Name == targetName)
            {
                return buttons[i].Pressed;
            }
        }

        return false;
    }

    /// <summary>The first connected gamepad's left thumbstick, magnitude-clamped to 1 —
    /// <see cref="Vector2.Zero"/> (never throws) with no gamepad connected, or if that gamepad
    /// reports no thumbsticks at all. Raw axis values, deliberately unconverted (same "convert
    /// at the caller's own boundary" convention as <see cref="MousePosition"/>/<see cref="MouseDelta"/>
    /// above) — this type doesn't know whether +Y should mean "forward" for a given game's world
    /// axes.</summary>
    public Vector2 GamepadLeftStick => GetGamepadLeftStick(0);

    /// <summary>Gamepad <paramref name="gamepadIndex"/>'s left thumbstick, magnitude-clamped to 1 —
    /// <see cref="Vector2.Zero"/> (never throws) for an index with no gamepad on it. Same raw-axis,
    /// convert-at-the-caller convention as <see cref="GamepadLeftStick"/>.</summary>
    public Vector2 GetGamepadLeftStick(int gamepadIndex)
    {
        if (gamepadIndex < 0 || gamepadIndex >= _context.Gamepads.Count || _context.Gamepads[gamepadIndex].Thumbsticks.Count == 0)
        {
            return Vector2.Zero;
        }

        var stick = _context.Gamepads[gamepadIndex].Thumbsticks[0];
        var raw = new Vector2(stick.X, stick.Y);
        return raw.LengthSquared() > 1f ? Vector2.Normalize(raw) : raw;
    }

    /// <summary>Gamepad <paramref name="gamepadIndex"/>'s RIGHT thumbstick (the backend's second
    /// stick), magnitude-clamped to 1 — <see cref="Vector2.Zero"/> (never throws) for an index with no
    /// gamepad on it or a pad that reports fewer than two sticks. Same raw-axis, convert-at-the-caller
    /// convention as <see cref="GetGamepadLeftStick"/>.</summary>
    public Vector2 GetGamepadRightStick(int gamepadIndex)
    {
        if (gamepadIndex < 0 || gamepadIndex >= _context.Gamepads.Count || _context.Gamepads[gamepadIndex].Thumbsticks.Count < 2)
        {
            return Vector2.Zero;
        }

        var stick = _context.Gamepads[gamepadIndex].Thumbsticks[1];
        var raw = new Vector2(stick.X, stick.Y);
        return raw.LengthSquared() > 1f ? Vector2.Normalize(raw) : raw;
    }

    public Vector2 MousePosition => CurrentMousePosition();

    /// <summary>Pixel delta since the last call to <see cref="EndFrame"/>. Callers advance
    /// the frame boundary explicitly rather than have delta computed as a side effect of
    /// reading position, so repeated reads within one update tick stay consistent.</summary>
    public Vector2 MouseDelta { get; private set; }

    public float ScrollDelta { get; private set; }

    /// <summary>Call once per update tick after consuming this frame's input, so
    /// <see cref="MouseDelta"/>/<see cref="ScrollDelta"/> reflect only that tick's motion and
    /// <see cref="WasKeyPressed"/>/<see cref="WasMouseButtonPressed"/> report only that tick's
    /// presses. See <see cref="WasKeyPressed"/> for what the host owes this method.</summary>
    public void EndFrame()
    {
        var current = CurrentMousePosition();
        MouseDelta = current - _previousMousePosition;
        _previousMousePosition = current;
        ScrollDelta = _accumulatedScroll;
        _accumulatedScroll = 0f;
        _keyPresses.EndFrame();
        _mouseButtonPresses.EndFrame();
        _keyReleases.EndFrame();
        _mouseButtonReleases.EndFrame();
    }

    private Vector2 CurrentMousePosition()
    {
        if (_context.Mice.Count == 0)
        {
            return Vector2.Zero;
        }

        return ConvertPosition(_context.Mice[0].Position);
    }

    /// <summary>The pure X/Y copy at the Silk↔System.Numerics boundary — pulled out so a
    /// test can pin down "no axis flip" without needing a real <see cref="IInputContext"/>.</summary>
    public static Vector2 ConvertPosition(System.Numerics.Vector2 silkPosition) => new(silkPosition.X, silkPosition.Y);

    /// <summary>Disposes the underlying <see cref="IInputContext"/>. Required — the GLFW
    /// backend registers input contexts against the native window handle and does not
    /// unregister them on window disposal alone, so skipping this throws
    /// "More than one input context for window ..." the next time a window happens to
    /// reuse that handle value within the same process (e.g. back-to-back tests).</summary>
    public void Dispose() => _context.Dispose();
}
