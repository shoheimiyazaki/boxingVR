using System.Numerics;
using Aigis.Platform;

namespace Aigis.Player;

/// <summary>
/// One update tick's evidence of which input device the player touched — the input to
/// <see cref="PointerVisibilityTracker"/>.
/// </summary>
/// <param name="PointerPixels">The mouse position this tick, window client pixels.</param>
/// <param name="PointerButtonPressed">Any mouse button went down this tick.</param>
/// <param name="PointerScrolled">The wheel turned this tick.</param>
/// <param name="KeyPressed">Any keyboard key went down this tick.</param>
/// <param name="GamepadHeldMask">Every pad's held buttons and deflected sticks, packed by
/// <see cref="GamepadActivityMask.Of"/>. A HELD mask, not an edge mask: the tracker takes the edge
/// itself against the previous tick's mask.</param>
public readonly record struct PointerActivitySample(
    Vector2 PointerPixels,
    bool PointerButtonPressed,
    bool PointerScrolled,
    bool KeyPressed,
    ulong GamepadHeldMask)
{
    /// <summary>Reads one tick's sample off a live <see cref="InputSnapshot"/>. Must run BEFORE
    /// <see cref="InputSnapshot.EndFrame"/>, which clears the press latches this reads.</summary>
    public static PointerActivitySample From(InputSnapshot input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new PointerActivitySample(
            input.MousePosition,
            input.MouseButtonsPressedThisFrame.Count > 0,
            // ScrollDelta is the wheel accumulated up to the previous EndFrame, so this lags the
            // wheel by one tick — invisible at 60 Hz, and a scroll is never the ONLY sign of a mouse.
            input.ScrollDelta != 0f,
            input.KeysPressedThisFrame.Count > 0,
            GamepadActivityMask.Of(new SnapshotGamepads(input)));
    }

    /// <summary>A struct adapter, so the per-tick read allocates no delegates.</summary>
    private readonly struct SnapshotGamepads(InputSnapshot input) : IGamepadReader
    {
        public int Count => input.GamepadCount;

        public bool IsButtonDown(int pad, GamepadButton button) => input.IsGamepadButtonDown(pad, button);

        public Vector2 LeftStick(int pad) => input.GetGamepadLeftStick(pad);
    }
}

/// <summary>What <see cref="GamepadActivityMask.Of"/> needs to know about the connected pads — an
/// interface so a test can drive it without a device.</summary>
public interface IGamepadReader
{
    public int Count { get; }

    public bool IsButtonDown(int pad, GamepadButton button);

    public Vector2 LeftStick(int pad);
}

/// <summary>
/// Packs every connected pad's held buttons and deflected left stick into one <see cref="ulong"/>,
/// so "did anything on any pad go down this tick" is one AND-NOT against the previous tick.
/// </summary>
public static class GamepadActivityMask
{
    /// <summary>Pads beyond this many are not looked at. Four is the living-room ceiling this engine's
    /// seat model addresses, and 4 × <see cref="BitsPerPad"/> fits a <see cref="ulong"/>.</summary>
    public const int MaxPads = 4;

    /// <summary>
    /// How far the stick has to be pushed before it counts as the player using the pad.
    /// <para>Half travel rather than a small dead zone: a resting stick drifts, and a drifting stick
    /// that hid the cursor every few seconds would be worse than the bug this fixes. A deliberate push
    /// clears half travel at once.</para>
    /// </summary>
    public const float StickDeflection = 0.5f;

    private static readonly GamepadButton[] Buttons = Enum.GetValues<GamepadButton>();

    /// <summary>One bit per <see cref="GamepadButton"/> plus one for the stick.</summary>
    public static int BitsPerPad => Buttons.Length + 1;

    /// <summary>Builds the mask. Generic over a struct reader so the per-tick call is allocation-free.</summary>
    public static ulong Of<TReader>(TReader pads)
        where TReader : IGamepadReader
    {
        var mask = 0UL;
        var count = Math.Min(pads.Count, MaxPads);
        for (var pad = 0; pad < count; pad++)
        {
            var baseBit = pad * BitsPerPad;
            for (var b = 0; b < Buttons.Length; b++)
            {
                if (pads.IsButtonDown(pad, Buttons[b]))
                {
                    mask |= 1UL << (baseBit + b);
                }
            }

            if (pads.LeftStick(pad).Length() >= StickDeflection)
            {
                mask |= 1UL << (baseBit + Buttons.Length);
            }
        }

        return mask;
    }
}

/// <summary>
/// Whether the mouse pointer should be showing: shown while the mouse is what the player last
/// used, hidden from the moment they press a key or a pad button, shown again the moment the mouse
/// moves or clicks (user test-play report: 「ジョイパッド操作時もメニューでカーソル残り続ける」).
/// <para>
/// <b>"Used" means the same thing Triage's menus mean by it.</b> Their hover only follows a pointer
/// that moved more than one pixel or clicked this tick (TriageShell's <c>pointerMoved</c> /
/// <c>pointerClicked</c>) — a mouse lying still on the desk does not steal the highlight from the
/// pad. The cursor obeys the same rule, so the two cannot disagree: a pointer that is showing is
/// one whose hover is live, and a still mouse does not bring the cursor back.
/// </para>
/// <para>
/// <b>Edges, not levels, for the other devices.</b> A key or button counts on the tick it goes DOWN;
/// a stick held over from before does not keep re-hiding a cursor the player is visibly moving.
/// When both happen in one tick the mouse wins, because the player has just put their hand on it.
/// </para>
/// <para>Presentation only: nothing here reaches a simulation step (hard rule 5), so it is free to
/// run off the real-frame device poll.</para>
/// </summary>
public sealed class PointerVisibilityTracker
{
    /// <summary>The motion that counts as moving the mouse, in pixels — the same threshold as
    /// Triage's menu hover (<c>TriageShell.PointerMovedEpsilonPixels</c>), for the reason in the
    /// class comment.</summary>
    public const float MovedThresholdPixels = 1f;

    private Vector2? _lastPointerPixels;
    private ulong _lastGamepadMask;

    /// <summary>Starts visible: until the player has touched anything, the mouse is as likely as
    /// anything else, and a game that opens with no cursor looks broken to a mouse user.</summary>
    public bool Visible { get; private set; } = true;

    /// <summary>Folds in one tick and returns the resulting <see cref="Visible"/>.</summary>
    public bool Update(in PointerActivitySample sample)
    {
        var moved = _lastPointerPixels is { } previous
            && Vector2.Distance(sample.PointerPixels, previous) > MovedThresholdPixels;
        _lastPointerPixels = sample.PointerPixels;

        var gamepadPressed = (sample.GamepadHeldMask & ~_lastGamepadMask) != 0;
        _lastGamepadMask = sample.GamepadHeldMask;

        Visible = Next(
            Visible,
            pointerUsed: moved || sample.PointerButtonPressed || sample.PointerScrolled,
            otherDeviceUsed: sample.KeyPressed || gamepadPressed);
        return Visible;
    }

    /// <summary>The rule itself: the mouse wins a tie, another device hides, nothing keeps.</summary>
    public static bool Next(bool visible, bool pointerUsed, bool otherDeviceUsed) =>
        pointerUsed || (!otherDeviceUsed && visible);
}
