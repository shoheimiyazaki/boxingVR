namespace Aigis.Physics;

public readonly record struct ProbeResult(string Name, bool Pass, float Value, float Threshold, string Detail);

public readonly record struct Anomaly(int Step, int BodyId, string Kind, float Value);

public sealed record ProbeOptions(
    float FloorTopY = 0f,
    float AnchorEpsilon = 0.02f,
    float LimitEpsilonRadians = 0.035f,
    float ClearanceEpsilon = 0.03f);
