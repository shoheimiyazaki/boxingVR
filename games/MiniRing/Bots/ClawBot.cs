using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Bots;

/// <summary>刃が届く位置に近づき、見えている部位を通る切断面を順に選ぶ。</summary>
internal sealed class ClawBot(BotStats stats)
{
    private readonly ClawMotion[] _arms = [new(), new()];
    private int _strokeCount;

    public FighterCommand Read(FighterCommand basis, in FighterSense sense, float dt, BotStyle style)
    {
        var to = sense.OpponentChest - sense.Self.Origin;
        to.Y = 0f;
        var distance = to.Length();
        var forward = distance > 0.001f ? to / distance : sense.Self.Forward;
        var range = sense.ArmReach + ClawSegment.Length * 0.56f;
        var occupied = _arms[0].Active || _arms[1].Active;
        var kick = basis.LeftLeg is not null || basis.RightLeg is not null;
        var ready = style.Attacks && !occupied && !kick && sense.OpponentAlive && distance < range + 0.10f;
        var side = (_strokeCount & 1) == 0 ? Hand.Right : Hand.Left;
        var aim = Aim(sense, _strokeCount);
        var left = _arms[0].Read(Hand.Left, ready && side == Hand.Left, true, sense, aim, dt, style.ClawDriveSeconds, style.ClawWindup);
        var right = _arms[1].Read(Hand.Right, ready && side == Hand.Right, true, sense, aim, dt, style.ClawDriveSeconds, style.ClawWindup);
        if (ready)
        {
            _strokeCount++;
            stats.ComboStarted("claw-sweep");
        }

        return basis with
        {
            Left = left,
            Right = right,
            FaceDirection = forward,
            TwistBias = kick ? basis.TwistBias : 0f,
            MoveWorld = style.Attacks && sense.OpponentAlive && !occupied && !ready && !kick
                ? forward * Math.Clamp((distance - range * 0.85f) / (range * 0.28f), -0.38f, 1f)
                : style.Attacks ? Vector3.Zero : basis.MoveWorld,
            Step = Vector3.Zero,
            HeadOffset = Stance(sense.ArmReach),
        };
    }

    internal static Vector3 Stance(float reach) => new(0f, -reach * 0.05f, 0f);

    internal static Vector3 Aim(in FighterSense sense, int stroke) => (stroke % 3) switch
    {
        0 => sense.OpponentTargets?.Point(AimGoal.NeckSeam, sense.Self.Origin) ?? Vector3.Lerp(sense.OpponentChest, sense.OpponentHead, 0.57f),
        1 => sense.OpponentTargets?.Point(AimGoal.GuardArm, sense.Self.Origin) ?? sense.OpponentChest,
        _ => sense.OpponentTargets?.Point(AimGoal.TorsoSeam, sense.Self.Origin)
            ?? (sense.OpponentBody == default ? sense.OpponentChest : Vector3.Lerp(sense.OpponentBody.Pelvis, sense.OpponentChest, 0.60f)),
    };
}
