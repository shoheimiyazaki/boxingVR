using System.Diagnostics;
using System.Globalization;

namespace Aigis.Player;

/// <summary>
/// Opt-in STEADY-STATE frame-time recorder for a windowed host — the running counterpart to
/// <see cref="StartupProfiler"/>, which only measures the one-shot cold start up to the first
/// presented frame and then stops. Gated by the <c>AIGIS_PROFILE_FRAMES</c> env var (set it to the
/// number of seconds to sample, e.g. <c>AIGIS_PROFILE_FRAMES=10</c>), so it costs nothing — not
/// even a <see cref="Stopwatch"/> tick — during normal play.
/// <para>
/// Exists because "the game feels heavy" is not a measurement and neither headless tests nor
/// <c>tools/Aigis.Benchmark</c> can answer it: no headless test drives a real window/Vulkan
/// swapchain end-to-end (CLAUDE.md's own note), and a BenchmarkDotNet case measures one method in
/// isolation, not the whole present loop with vsync, GC pauses and the OS compositor in it. This
/// records the real interval between presented frames and reports the distribution, because a
/// mean alone hides exactly the thing a player notices — the occasional 30 ms GC stall.
/// </para>
/// <para>
/// Reports GC collection counts alongside the timings for the same reason: a frame-time p99 that
/// tracks the gen2 count is a GC problem, and one that does not is a submission-cost problem.
/// After the sampling window elapses the host closes the window on its own, so this needs no
/// human to watch it (same unattended posture as <c>scripts/smoke-launch.ps1</c>).
/// </para>
/// </summary>
public sealed class FrameProfiler
{
    /// <summary>How many seconds of frames to sample, or <c>null</c> when
    /// <c>AIGIS_PROFILE_FRAMES</c> is unset/unparseable (the profiler is then entirely inert).</summary>
    public static double? SampleSeconds { get; } = ParseSampleSeconds();

    /// <summary>Whether this process was asked to record a frame-time profile.</summary>
    public static bool Enabled => SampleSeconds is not null;

    private readonly Stopwatch? _stopwatch = Enabled ? Stopwatch.StartNew() : null;
    private readonly List<double> _frameMilliseconds = [];
    private double _lastFrameStartMs;
    private double _sampleStartMs = -1d;
    private int _gen0Start;
    private int _gen1Start;
    private int _gen2Start;
    private long _allocatedStart;

    /// <summary>Call once per presented frame. Returns true when the sampling window has elapsed
    /// and the caller should <see cref="Report"/> and close — false always when disabled, so a
    /// host can call this unconditionally from its render callback.</summary>
    public bool RecordFrameAndCheckDone()
    {
        if (_stopwatch is null)
        {
            return false;
        }

        var nowMs = _stopwatch.Elapsed.TotalMilliseconds;

        // The first presented frame starts the window rather than being measured by it: the
        // interval before it includes device/pipeline/shader warm-up (StartupProfiler's subject),
        // which would otherwise land in this distribution as a single enormous outlier.
        if (_sampleStartMs < 0d)
        {
            _sampleStartMs = nowMs;
            _lastFrameStartMs = nowMs;
            _gen0Start = GC.CollectionCount(0);
            _gen1Start = GC.CollectionCount(1);
            _gen2Start = GC.CollectionCount(2);
            _allocatedStart = GC.GetTotalAllocatedBytes();
            return false;
        }

        var frameMs = nowMs - _lastFrameStartMs;
        if (frameMs >= AttributedFrameMilliseconds && _currentPhases.Count > 0)
        {
            _attribution[_frameMilliseconds.Count] = string.Join(
                ",",
                _currentPhases.Select(phase => string.Create(CultureInfo.InvariantCulture, $"{phase.Key}={phase.Value:0}")));
        }

        _currentPhases.Clear();
        _frameMilliseconds.Add(frameMs);
        _lastFrameStartMs = nowMs;
        return nowMs - _sampleStartMs >= SampleSeconds!.Value * 1000d;
    }

    /// <summary>
    /// Charges <paramref name="milliseconds"/> of the frame now being built to <paramref name="phase"/>
    /// — the host's own coarse split (the fixed steps, the portrait capture, the present). A slow frame
    /// then reports WHICH of them owned it next to the second it happened in, which is what turns
    /// "1.4 s at 56 s" into an attributable hitch. Inert unless profiling.
    /// </summary>
    public void AddPhase(string phase, double milliseconds)
    {
        if (_stopwatch is null || _sampleStartMs < 0d)
        {
            return;
        }

        _currentPhases[phase] = _currentPhases.GetValueOrDefault(phase) + milliseconds;
    }

    /// <summary>A frame at least this long keeps its phase split for the report. Below it the split is
    /// dropped, so a long sample does not grow a string per ordinary frame.</summary>
    private const double AttributedFrameMilliseconds = 25d;

    private readonly Dictionary<string, double> _currentPhases = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _attribution = [];

    /// <summary>Prints the sampled frame-time distribution and GC activity to stdout. A no-op if
    /// disabled or if no frames were recorded.</summary>
    public void Report()
    {
        if (_stopwatch is null || _frameMilliseconds.Count == 0)
        {
            return;
        }

        var sorted = _frameMilliseconds.ToArray();
        Array.Sort(sorted);
        var elapsedSeconds = (_lastFrameStartMs - _sampleStartMs) / 1000d;
        var mean = _frameMilliseconds.Sum() / _frameMilliseconds.Count;

        Console.WriteLine("--- AIGIS_PROFILE_FRAMES ---");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  frames={_frameMilliseconds.Count} over {elapsedSeconds:0.00} s  =>  {_frameMilliseconds.Count / elapsedSeconds:0.0} fps average"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  frame ms: mean={mean:0.00} p50={Percentile(sorted, 0.50):0.00} p95={Percentile(sorted, 0.95):0.00} p99={Percentile(sorted, 0.99):0.00} max={sorted[^1]:0.00}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  gc: gen0={GC.CollectionCount(0) - _gen0Start} gen1={GC.CollectionCount(1) - _gen1Start} gen2={GC.CollectionCount(2) - _gen2Start} " +
            $"allocated={(GC.GetTotalAllocatedBytes() - _allocatedStart) / 1024d / 1024d:0.0} MB " +
            $"({(GC.GetTotalAllocatedBytes() - _allocatedStart) / 1024d / 1024d / elapsedSeconds:0.0} MB/s)"));

        ReportMemory();

        // The worst frames WITH the second they happened in, not just a max: a hitch that a player
        // feels is one-off lazy work (a pipeline compiled on first use, an asset loaded on first
        // play), and "when" is what makes it attributable to the event that triggered it. A summary
        // statistic alone cannot distinguish that from steady-state cost.
        ReportSlowestFrames();
    }

    /// <summary>
    /// The process's memory FOOTPRINT, as distinct from the allocation RATE reported above them.
    /// <para>
    /// The two answer different questions and neither substitutes for the other: a game can allocate
    /// 40 MB/s and hold 80 MB (a healthy gen0 churn), or allocate nothing and hold 500 MB. The line
    /// that matters on a small machine is the third one — working set against managed heap — because
    /// the difference between them is everything the CLR did not allocate: Vulkan images and buffers
    /// (five 2048x2048 shadow maps alone are tens of megabytes), decoded audio, the driver's own
    /// allocations, and the loaded assemblies. Optimising the managed heap when the gap is what is
    /// large is the standard way to spend a day and move nothing.
    /// </para>
    /// </summary>
    private static void ReportMemory()
    {
        var managedBytes = GC.GetTotalMemory(forceFullCollection: false);
        var info = GC.GetGCMemoryInfo();
        using var process = Process.GetCurrentProcess();
        var workingSetBytes = process.WorkingSet64;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  memory: workingSet={workingSetBytes / 1024d / 1024d:0.0} MB "
            + $"private={process.PrivateMemorySize64 / 1024d / 1024d:0.0} MB "
            + $"managedHeap={managedBytes / 1024d / 1024d:0.0} MB "
            + $"gcCommitted={info.TotalCommittedBytes / 1024d / 1024d:0.0} MB "
            + $"nonManaged~{(workingSetBytes - managedBytes) / 1024d / 1024d:0.0} MB"));
    }

    private void ReportSlowestFrames()
    {
        const int Worst = 5;
        var order = new int[_frameMilliseconds.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => _frameMilliseconds[b].CompareTo(_frameMilliseconds[a]));

        Console.Write("  slowest frames:");
        for (var i = 0; i < Math.Min(Worst, order.Length); i++)
        {
            var index = order[i];
            Console.Write(string.Create(CultureInfo.InvariantCulture,
                $" {_frameMilliseconds[index]:0.0}ms@{SecondsIntoSample(index):0.00}s"));
            if (_attribution.TryGetValue(index, out var split))
            {
                Console.Write($"[{split}]");
            }
        }

        Console.WriteLine();
    }

    /// <summary>When the frame at <paramref name="index"/> ended, in seconds since the first
    /// presented frame — the sum of every frame interval up to and including it, which is exactly
    /// how the sample window itself is measured.</summary>
    private double SecondsIntoSample(int index)
    {
        var total = 0d;
        for (var i = 0; i <= index; i++)
        {
            total += _frameMilliseconds[i];
        }

        return total / 1000d;
    }

    /// <summary>Nearest-rank percentile over an already-sorted sample.</summary>
    private static double Percentile(double[] sorted, double fraction)
    {
        var index = Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static double? ParseSampleSeconds()
    {
        var raw = Environment.GetEnvironmentVariable("AIGIS_PROFILE_FRAMES");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // "1" (the on/off spelling every other AIGIS_* switch uses) means a default sample window
        // rather than a one-second one, so the common case needs no thought about units.
        if (string.Equals(raw, "1", StringComparison.Ordinal))
        {
            return DefaultSampleSeconds;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0d
            ? seconds
            : null;
    }

    /// <summary>Long enough to cover several gen2 collections at the rates this engine actually
    /// produces, short enough that an unattended run is not a hang.</summary>
    private const double DefaultSampleSeconds = 10d;
}
