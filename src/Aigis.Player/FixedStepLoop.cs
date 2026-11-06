namespace Aigis.Player;

/// <summary>
/// Decouples real-time frame delivery from the engine's fixed-step determinism contract
/// (Hard rule 5): accumulates the real (possibly jittery) delta a game loop host measures
/// and reports how many <c>dt</c>-sized physics steps should run this tick. Physics is
/// still advanced with the same constant <c>dt</c> every call — only the step *count*
/// varies with real time.
/// </summary>
public sealed class FixedStepLoop
{
    private readonly float _dt;
    private readonly int _maxStepsPerTick;
    private double _accumulator;

    /// <param name="dt">Seconds per fixed physics step (e.g. <c>Aigis.Foundation.FixedStep.Default.Dt</c>).</param>
    /// <param name="maxStepsPerTick">Caps how many steps a single call to <see cref="Advance"/>
    /// can emit, so a long stall (GC pause, breakpoint, alt-tab) drops time instead of firing
    /// an unbounded burst of catch-up steps (the classic "spiral of death").</param>
    public FixedStepLoop(float dt, int maxStepsPerTick = 5)
    {
        _dt = dt;
        _maxStepsPerTick = maxStepsPerTick;
    }

    /// <summary>When true, <see cref="Advance"/> ignores elapsed real time entirely (no
    /// accumulation, no steps) — resuming does not fast-forward through the paused interval.</summary>
    public bool Paused { get; set; }

    /// <summary>Call once per real-time update tick. Returns how many fixed-dt physics
    /// steps the caller should run right now (0 if paused or not enough time has accrued).</summary>
    public int Advance(double realDeltaSeconds)
    {
        if (Paused)
        {
            return 0;
        }

        _accumulator += realDeltaSeconds;
        var steps = 0;
        while (_accumulator >= _dt && steps < _maxStepsPerTick)
        {
            _accumulator -= _dt;
            steps++;
        }

        // A stall long enough to hit the cap would otherwise leave the accumulator growing
        // unbounded (each subsequent tick re-hits the cap forever); drop the excess instead.
        if (steps == _maxStepsPerTick)
        {
            _accumulator = 0;
        }

        return steps;
    }
}
