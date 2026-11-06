namespace Aigis.Platform;

/// <summary>
/// Remembers that a button went down, until someone says the frame is over.
///
/// <para><b>Why a latch is needed at all.</b> Silk reports input as STATE: "is this key down right
/// now". A caller polling once per update tick can therefore only ever see a press that is still held
/// at the instant it happens to look, and at this engine's 60 Hz update cadence a press and release
/// that both fall inside one ~17 ms tick is invisible. That is the shape of 2026-08-28's report that
/// menu buttons 「クリックではなく、長押しでないと反応しない」: nothing was dropping the input, the
/// question was simply being asked at the wrong moments. Devices also deliver a down EVENT, which
/// cannot be missed however short the press; this is where those events are kept until the tick that
/// owns them has run.</para>
///
/// <para><b>Pulled out of <see cref="InputSnapshot"/> so it can be tested at all.</b> That type needs
/// a live <c>IInputContext</c>, which needs a window — the same reason
/// <see cref="InputSnapshot.ConvertPosition"/> is a static on its own. What is worth pinning here is
/// the CONTRACT (a press survives until the frame ends, and not past it), and that is pure. The
/// device subscription that feeds it stays untested, exactly as the scroll accumulator beside it
/// is.</para>
/// </summary>
/// <typeparam name="TButton">The device's own button type — <c>Key</c> or <c>MouseButton</c>.</typeparam>
public sealed class PressLatch<TButton>
    where TButton : notnull
{
    private readonly HashSet<TButton> _pressed = [];

    /// <summary>Records a press. Repeats within one frame collapse: the question this answers is
    /// "did it go down", not "how many times".</summary>
    public void Note(TButton button) => _pressed.Add(button);

    /// <summary>Whether <paramref name="button"/> went down since the last <see cref="EndFrame"/>.</summary>
    public bool WasPressed(TButton button) => _pressed.Contains(button);

    /// <summary>
    /// Everything that went down this frame.
    /// <para>
    /// Added 2026-09-21 for the input trace: a diagnostic that asks "what did the person actually
    /// press" cannot be built out of <see cref="WasPressed"/>, because that needs the answer in order
    /// to ask the question — a key nothing is bound to is a key nobody polls, and those are precisely
    /// the presses a mis-press analysis is looking for.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<TButton> Pressed => _pressed;

    /// <summary>Ends the frame that owns these presses.
    /// <para>Called once per update tick, AFTER the tick has consumed its input — see
    /// <see cref="InputSnapshot.EndFrame"/>, which is what places that requirement on the host.</para></summary>
    public void EndFrame() => _pressed.Clear();
}
