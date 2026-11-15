using System.Diagnostics;

namespace MiniRing.Vr;

internal sealed class VrFrameTiming
{
    private readonly double[] _work = new double[300];
    private readonly double[] _intervals = new double[300];
    private int _count, _reused;
    private long _previous;
    private VrScene? _scene;

    public void Record(long start, float refreshHz, VrScene scene, double physicsMaximumMilliseconds = 0d)
    {
        var end = Stopwatch.GetTimestamp();
        if (_previous != 0)
        {
            _work[_count] = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
            _intervals[_count++] = Stopwatch.GetElapsedTime(_previous, end).TotalMilliseconds;
            if (ReferenceEquals(_scene, scene)) { _reused++; }
        }
        _previous = end;
        _scene = scene;

        if (_count == _work.Length)
        {
            Array.Sort(_work);
            Array.Sort(_intervals);
            var budget = 1000d / 90;
            Console.WriteLine($"[vr profile] display {refreshHz:F1} Hz, app {1000d / _intervals.Average():F1} fps, "
                + $"interval p99 {_intervals[296]:F2} ms, max {_intervals[^1]:F2} ms, "
                + $"work p95 {_work[284]:F2} ms, p99 {_work[296]:F2} ms, max {_work[^1]:F2} ms, "
                + $"physics max {physicsMaximumMilliseconds:F2} ms, intervals >11.11 ms {_intervals.Count(t => t > budget)}/{_count}, reused {_reused}/{_count}");
            _count = _reused = 0;
        }
    }
}
