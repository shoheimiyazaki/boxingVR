namespace Aigis.Foundation;

/// <summary>
/// A fixed simulation timestep. The engine is deterministic-by-construction only
/// when advanced in fixed increments, so the timestep is a first-class value that
/// tests and the agent harness set explicitly (Triage's verify runners use 1/60).
/// </summary>
/// <param name="Dt">Seconds per fixed step.</param>
public readonly record struct FixedStep(float Dt)
{
    /// <summary>60 Hz — the default cadence used by reference verify runners.</summary>
    public static readonly FixedStep Default = new(1f / 60f);

    /// <summary>Wall-clock time (seconds) reached after <paramref name="stepCount"/> steps.</summary>
    public float TimeAt(int stepCount) => Dt * stepCount;

    /// <summary>Number of whole fixed steps that fit into <paramref name="seconds"/>.</summary>
    public int StepsFor(float seconds) => (int)MathF.Round(seconds / Dt);
}
