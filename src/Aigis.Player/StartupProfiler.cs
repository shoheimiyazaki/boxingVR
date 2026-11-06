using System.Diagnostics;

namespace Aigis.Player;

/// <summary>
/// Opt-in wall-clock checkpoint recorder for diagnosing a host's startup time (window
/// creation, Vulkan device/pipeline setup, time-to-first-frame). Gated by the
/// <c>AIGIS_PROFILE_STARTUP</c> env var so it costs nothing (not even a <see cref="Stopwatch"/>
/// tick) during normal play — a host calls <see cref="Mark"/> at its own checkpoints and
/// <see cref="Report"/> once, typically right after the first rendered frame.
/// </summary>
/// <remarks>Lives in <c>Aigis.Player</c> (a host-adjacent library the games already
/// reference), not <c>Aigis.Foundation</c>/<c>Aigis.Render</c> — it prints, which Hard
/// rule 2 only allows in hosts/games, not engine libraries.</remarks>
public sealed class StartupProfiler
{
    /// <summary>Set <c>AIGIS_PROFILE_STARTUP=1</c> before running any game/Aigis.Player to
    /// see a stage-by-stage startup timing report on exit (or after the first frame, if the
    /// host calls <see cref="Report"/> there — see games/Game.BallRoll/Program.cs for the
    /// reference wiring). No effect otherwise: <see cref="Mark"/>/<see cref="Report"/> are
    /// no-ops, so shipping this call in a hot path is free.</summary>
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("AIGIS_PROFILE_STARTUP") == "1";

    private readonly Stopwatch? _stopwatch = Enabled ? Stopwatch.StartNew() : null;
    private readonly List<(string Label, TimeSpan Elapsed)> _checkpoints = [];

    public void Mark(string label)
    {
        if (_stopwatch is null)
        {
            return;
        }

        _checkpoints.Add((label, _stopwatch.Elapsed));
    }

    /// <summary>Prints the recorded checkpoints (cumulative and per-stage) to stdout. A
    /// no-op if <see cref="Enabled"/> is false.</summary>
    public void Report()
    {
        if (_stopwatch is null)
        {
            return;
        }

        Console.WriteLine("--- AIGIS_PROFILE_STARTUP ---");
        var previous = TimeSpan.Zero;
        foreach (var (label, elapsed) in _checkpoints)
        {
            var delta = elapsed - previous;
            Console.WriteLine($"  {elapsed.TotalMilliseconds,8:0.0} ms cumulative | +{delta.TotalMilliseconds,7:0.0} ms | {label}");
            previous = elapsed;
        }
    }
}
