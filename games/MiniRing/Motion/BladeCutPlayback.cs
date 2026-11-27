using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Motion;

/// <summary>いまの武器と握り。Pose は現在の体フレームで、Support は柄を支える手。</summary>
public readonly record struct BladeGripSense(WeaponKind Kind, BladeKeyPose Pose, float RemainingFraction, Hand? Support,
    Vector3 GripLocal, Vector3? SupportOffset);

/// <summary>生成した目標列をゲームの歩ごとに再生する。開始時の体フレームに軌道を固定する。</summary>
public sealed class BladeCutPlayback
{
    private const float RecoverySeconds = 0.2f;
    private BodyFrame _anchor;
    private float _elapsed;
    private BladeGripSense _initialGrip;

    public BladeCutPlan? Plan { get; private set; }
    public float Elapsed => _elapsed;

    /// <summary>Request の座標は sense.Self 基準。実際の握りと腕の寸法から計画する。</summary>
    public bool TryStart(Hand hand, in FighterSense sense, BladeCutRequest request, float contactFraction = 0.8f)
    {
        if (Plan is not null || sense.SelfDowned || sense.BladeGrip(hand) is not { } grip ||
            sense.BladeArm(hand) is not { } arm ||
            !BladeCutGeometry.TryFromWeapon(grip.Kind, hand, grip.Support.HasValue, out var geometry, contactFraction: contactFraction,
                remainingFraction: grip.RemainingFraction, gripLocal: grip.GripLocal, supportOffset: grip.SupportOffset) ||
            !BladeCutPlan.TryCreate(request, geometry, grip.Pose, arm,
                grip.Support is { } support ? sense.BladeArm(support) : null, out var plan))
        {
            return false;
        }

        Plan = plan;
        _anchor = sense.Self;
        _initialGrip = grip;
        _elapsed = 0f;
        return true;
    }

    public void Cancel() => Plan = null;

    /// <summary>基準の指令に武器の手だけを上書きし、振り抜いた後は基準の構えへ戻る。</summary>
    public bool TryRead(in FighterSense sense, in FighterCommand basis, float dt, out FighterCommand command)
    {
        command = basis;
        if (Plan is not { } plan)
        {
            return false;
        }

        var hand = plan.Geometry.MainHand;
        if (sense.SelfDowned || sense.BladeGrip(hand) is not { } grip || grip.Kind != _initialGrip.Kind ||
            grip.RemainingFraction < _initialGrip.RemainingFraction - 0.001f || grip.GripLocal != _initialGrip.GripLocal ||
            grip.Support != _initialGrip.Support || grip.SupportOffset != _initialGrip.SupportOffset ||
            _elapsed >= plan.Duration + RecoverySeconds || dt < 0f || !float.IsFinite(dt))
        {
            Cancel();
            return false;
        }

        if (_elapsed <= plan.Duration)
        {
            command = plan.ToCommand(_elapsed, basis, _anchor, sense.Self);
        }
        else
        {
            var t = (_elapsed - plan.Duration) / RecoverySeconds;
            var guard = basis.For(hand);
            var from = plan.ToCommand(plan.FollowThrough, basis, _anchor, sense.Self).For(hand);
            var rotation = from.Orientation!.Value;
            var pose = BladeKeyPose.Between(new BladeKeyPose(from.TargetLocal, rotation),
                new BladeKeyPose(guard.TargetLocal, guard.Orientation ?? rotation), t * t * (3f - 2f * t));
            command = plan.ToCommand(pose, basis, sense.Self, sense.Self);
        }

        _elapsed += dt;
        return true;
    }
}
