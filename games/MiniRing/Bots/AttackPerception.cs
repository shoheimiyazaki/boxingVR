using System.Numerics;

namespace MiniRing.Bots;

/// <summary>攻撃判断に使える観測と、その時点で分かっていた頭の速度。</summary>
internal readonly record struct AttackObservation(FighterSense Sense, Vector3 HeadVelocity)
{
    public Vector3 Aim(PunchSpec spec, AimGoal goal, Vector3 selfHead, float reach, float lead)
    {
        var natural = spec.ToBody ? Sense.OpponentChest : Sense.OpponentHead;
        var chosen = Sense.OpponentTargets?.Point(goal, selfHead);
        var point = chosen is { } target ? Targeting.Clamp(natural, target, selfHead, reach) : natural;
        return point + (spec.ToBody ? Sense.OpponentVelocity : HeadVelocity) * lead;
    }
}

internal sealed class AttackPerception
{
    internal const float DelaySeconds = 0.2f;
    private readonly Queue<(double Time, AttackObservation Observation)> _history = new();
    private double _time;
    private (double Time, Vector3 Position)? _previousHead;
    private AttackObservation? _visible;

    public AttackObservation? Update(in FighterSense sense, float dt)
    {
        var headVelocity = _previousHead is { } previous
            ? (sense.OpponentHead - previous.Position) / (float)(_time - previous.Time)
            : Vector3.Zero;
        _previousHead = (_time, sense.OpponentHead);
        var snapshot = sense with
        {
            OpponentTargets = sense.OpponentTargets?.Perceived(sense.OpponentHead, sense.OpponentHead, sense.SelfHead, 1f),
        };
        _history.Enqueue((_time, new AttackObservation(snapshot, headVelocity)));
        // 240 Hz の48歩と0.2秒の浮動小数点誤差を吸収する。
        while (_history.TryPeek(out var next) && _time - next.Time >= DelaySeconds - 1e-7)
        {
            _visible = _history.Dequeue().Observation;
        }
        _time += dt;
        return _visible;
    }
}
