using System.Numerics;

namespace MiniRing;

/// <summary>Front-leg input for a quadruped. The controller chooses a leg once per strike.</summary>
public sealed class FrontLegStrike
{
    private const float Duration = 0.72f;
    private Hand? _side;
    private Vector3 _aim;
    private float _elapsed;
    private bool _sweep;
    private bool _pressed;

    public static Vector3 Aim(in FighterSense sense)
    {
        var hip = sense.OpponentBody == default ? sense.OpponentChest : sense.OpponentBody.Pelvis;
        return hip - Vector3.UnitY * (sense.SelfLeg(Hand.Left).Shin * 0.55f);
    }

    public static Hand? Choose(in FighterSense sense, Vector3 aimWorld)
    {
        var aim = sense.Self.WorldToLocal(aimWorld);
        var left = sense.SelfLeg(Hand.Left);
        var right = sense.SelfLeg(Hand.Right);
        if (!left.Available && !right.Available)
        {
            return null;
        }

        return !right.Available || (left.Available && ReachCost(left) <= ReachCost(right)) ? Hand.Left : Hand.Right;

        float ReachCost(LegBody leg) => Vector3.DistanceSquared(leg.Hip, aim) / MathF.Pow(leg.Thigh + leg.Shin, 2f);
    }

    public static LegCommand At(LegBody leg, Vector3 aim, float phase, bool sweep, Hand side)
    {
        var length = leg.Thigh + leg.Shin;
        var rest = leg.Hip + new Vector3(0f, -0.92f, 0.10f) * length;
        var chamber = leg.Hip + new Vector3(side.LocalSide() * (sweep ? 0.38f : 0.08f), -0.53f, 0.14f) * length;
        var approach = aim - leg.Hip;
        var distance = approach.Length();
        var reach = length * 0.98f;
        var end = leg.Hip + (distance > 1e-6f ? approach * (MathF.Min(distance + 0.15f * length, reach) / distance) : Vector3.UnitZ * reach);
        var t = Math.Clamp(phase, 0f, 1f);
        static float Smooth(float x) => x * x * (3f - (2f * x));
        var target = t < 0.28f ? Vector3.Lerp(rest, chamber, Smooth(t / 0.28f))
            : t < 0.55f ? Vector3.Lerp(chamber, end, Smooth((t - 0.28f) / 0.27f))
            : Vector3.Lerp(end, rest, Smooth((t - 0.55f) / 0.45f));
        return new LegCommand(target, Orientation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.38f));
    }

    public FighterCommand Read(FighterCommand basis, in FighterSense sense, bool pressed, bool sweep, float dt)
    {
        var start = pressed && !_pressed;
        _pressed = pressed;
        if (_side is null && start && sense.SelfBuild?.FourLegged == true && sense.OpponentAlive)
        {
            _aim = Aim(sense);
            _side = Choose(sense, _aim);
            _elapsed = 0f;
            _sweep = sweep;
        }

        if (_side is not { } side)
        {
            return basis;
        }

        var leg = At(sense.SelfLeg(side), sense.Self.WorldToLocal(_aim), _elapsed / Duration, _sweep, side);
        _elapsed += dt;
        if (_elapsed >= Duration)
        {
            _side = null;
        }

        return side == Hand.Left ? basis with { LeftLeg = leg } : basis with { RightLeg = leg };
    }
}
