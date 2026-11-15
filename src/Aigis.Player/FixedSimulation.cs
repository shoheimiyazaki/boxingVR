using System.Diagnostics;

namespace Aigis.Player;

/// <summary>
/// Single-owner fixed-step simulation. Only complete, immutable snapshots cross to presentation.
/// Wall time schedules work; it never changes the integration delta or enters the game state.
/// Slow steps discard wall-time debt, so readers never wait for catch-up work.
/// </summary>
public sealed class FixedSimulation<TInput, TSnapshot> : IDisposable where TSnapshot : class
{
    private sealed record Pending(TInput Input, float Scale);
    private readonly AutoResetEvent _wake = new(false);
    private readonly Task _worker;
    private readonly Func<TInput, TInput, TInput> _merge;
    private Pending? _pending;
    private TSnapshot _latest;
    private int _stopping;
    private long _completed;
    private double _maximum;

    public FixedSimulation(TSnapshot initial, TInput initialInput,
        Func<TInput, TInput, TInput> mergePending, Func<TInput, TInput> heldInput,
        Func<long, TInput, float, TSnapshot> advance, float stepSeconds = 1f / 240f)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(mergePending);
        ArgumentNullException.ThrowIfNull(heldInput);
        ArgumentNullException.ThrowIfNull(advance);
        if (!float.IsFinite(stepSeconds) || stepSeconds <= 0) { throw new ArgumentOutOfRangeException(nameof(stepSeconds)); }
        _latest = initial;
        _merge = mergePending;
        _worker = Task.Factory.StartNew(() =>
        {
            var input = initialInput;
            var scale = 1f;
            _wake.WaitOne(); // Start only after the first real input, never during construction.
            var due = Stopwatch.GetTimestamp();
            while (Volatile.Read(ref _stopping) == 0)
            {
                var now = Stopwatch.GetTimestamp();
                if (now < due)
                {
                    _wake.WaitOne(TimeSpan.FromSeconds((double)(due - now) / Stopwatch.Frequency));
                    continue;
                }
                if (Interlocked.Exchange(ref _pending, null) is { } pending)
                {
                    input = pending.Input;
                    scale = pending.Scale;
                }
                var start = Stopwatch.GetTimestamp();
                var snapshot = advance(_completed, input, stepSeconds);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Volatile.Write(ref _maximum, Math.Max(_maximum, elapsed));
                Interlocked.Increment(ref _completed);
                Volatile.Write(ref _latest, snapshot);
                input = heldInput(input);
                var period = (long)(Stopwatch.Frequency * (double)stepSeconds / scale);
                due = Math.Max(due + period, Stopwatch.GetTimestamp());
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public TSnapshot Latest
    {
        get
        {
            if (_worker.IsFaulted) { _worker.GetAwaiter().GetResult(); }
            return Volatile.Read(ref _latest);
        }
    }

    public long CompletedSteps => Interlocked.Read(ref _completed);
    public double MaximumStepMilliseconds => Volatile.Read(ref _maximum);

    /// <summary>Latest continuous input wins; mergePending preserves unconsumed edges.
    /// The advance callback receives the actual zero-based application step, which must also be recorded.</summary>
    public void Submit(TInput input, float timeScale = 1f)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
        if (!float.IsFinite(timeScale) || timeScale <= 0) { throw new ArgumentOutOfRangeException(nameof(timeScale)); }
        Pending? previous;
        Pending next;
        do
        {
            previous = Volatile.Read(ref _pending);
            next = new(previous is null ? input : _merge(previous.Input, input), timeScale);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _pending, next, previous), previous));
        _wake.Set();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) { return; }
        _wake.Set();
        try { _worker.GetAwaiter().GetResult(); }
        finally { _wake.Dispose(); }
    }
}
