using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 攻撃判断に使える観測と、その時点で分かっていた相手の体（胴）の速度。拳の狙いは、続く動き（足で運ばれる体の動き）だけを先へ延ばし、
/// 頭が体の上で揺れる分（すぐ向きが変わる）は延ばさない（2026-11-17、詰めのドリル finishlab: 頭の速さで 0.4 秒先へ延ばすと、
/// ふらつく相手の頭から狙いが腕の長さの 0.7 倍ずれた）。
/// </summary>
internal readonly record struct AttackObservation(FighterSense Sense, Vector3 BodyVelocity)
{
    public Vector3 Aim(PunchSpec spec, AimGoal goal, Vector3 selfHead, float reach, float lead)
    {
        var natural = spec.ToBody ? Sense.OpponentChest : Sense.OpponentHead;
        var chosen = Sense.OpponentTargets?.Point(goal, selfHead);
        var point = chosen is { } target ? Targeting.Clamp(natural, target, selfHead, reach) : natural;
        return point + (BodyVelocity * lead);
    }
}

internal sealed class AttackPerception
{
    internal const float DelaySeconds = 0.2f;
    private readonly Queue<(double Time, AttackObservation Observation)> _history = new();
    private double _time;
    private AttackObservation? _visible;

    /// <summary>相手の体の速さを見てとる時間 [s]（目が動きを捉える 0.15 秒ほど。1 歩ごとの速さの細かいぶれを先へ延ばさない）。</summary>
    internal const float VelocitySeconds = 0.15f;

    private Vector3 _bodyVelocity;

    public AttackObservation? Update(in FighterSense sense, float dt)
    {
        _bodyVelocity += (sense.OpponentVelocity - _bodyVelocity) * (1f - MathF.Exp(-dt / VelocitySeconds));
        var snapshot = sense with
        {
            OpponentTargets = sense.OpponentTargets?.Perceived(sense.OpponentHead, sense.OpponentHead, sense.SelfHead, 1f),
        };
        _history.Enqueue((_time, new AttackObservation(snapshot, _bodyVelocity)));
        // 240 Hz の48歩と0.2秒の浮動小数点誤差を吸収する。
        while (_history.TryPeek(out var next) && _time - next.Time >= DelaySeconds - 1e-7)
        {
            _visible = _history.Dequeue().Observation;
        }
        _time += dt;
        return _visible;
    }
}
