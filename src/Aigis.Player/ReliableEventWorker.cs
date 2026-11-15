using System.Collections.Concurrent;

namespace Aigis.Player;

/// <summary>Consumes every accepted event on one dedicated thread, independently of replaceable scene snapshots.</summary>
public sealed class ReliableEventWorker<T> : IDisposable
{
    private readonly ConcurrentQueue<T> _events = new();
    private readonly AutoResetEvent _ready = new(false);
    private readonly object _lifecycle = new();
    private readonly Task _worker;
    private int _stopping;

    public ReliableEventWorker(Action<T> consume)
    {
        ArgumentNullException.ThrowIfNull(consume);
        _worker = Task.Factory.StartNew(() =>
        {
            while (true)
            {
                while (_events.TryDequeue(out var item)) { consume(item); }
                if (Volatile.Read(ref _stopping) != 0)
                {
                    // A producer may have enqueued between the empty check and the stop publication.
                    while (_events.TryDequeue(out var final)) { consume(final); }
                    return;
                }
                _ready.WaitOne();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Enqueue(T item)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_stopping != 0, this);
            ThrowIfFaulted();
            _events.Enqueue(item);
            _ready.Set();
        }
    }

    public void ThrowIfFaulted()
    {
        if (_worker.IsFaulted) { _worker.GetAwaiter().GetResult(); }
    }

    /// <summary>Stops accepting events, then drains accepted events and joins the consumer.</summary>
    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_stopping != 0) { return; }
            Volatile.Write(ref _stopping, 1);
            _ready.Set();
        }
        try { _worker.GetAwaiter().GetResult(); }
        finally { _ready.Dispose(); }
    }
}
