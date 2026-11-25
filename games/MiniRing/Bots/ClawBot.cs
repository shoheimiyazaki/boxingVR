using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Bots;

/// <summary>刃が届く位置に近づき、見えている部位を通る切断面を順に選ぶ。</summary>
internal sealed class ClawBot(BotStats stats)
{
    private readonly ClawMotion[] _arms = [new(), new()];
    private int _strokeCount;

    /// <summary>狙いごとに溜まった重み（首・腕・胴。<see cref="BotStyle.ClawAimNeck"/>）。</summary>
    private readonly float[] _aimCredit = new float[3];

    /// <summary>次の斬り払いの狙い（0 首・1 腕・2 胴）。斬り始めた後に次を決める。</summary>
    private int _nextAim = -1;

    /// <summary>両腕の鎌が構えに戻ってからの時間 [s]（<see cref="BotStyle.ClawRest"/>）。</summary>
    private float _idle = float.PositiveInfinity;

    public FighterCommand Read(FighterCommand basis, in FighterSense sense, float dt, BotStyle style)
    {
        var to = sense.OpponentChest - sense.Self.Origin;
        to.Y = 0f;
        var distance = to.Length();
        var forward = distance > 0.001f ? to / distance : sense.Self.Forward;
        var range = sense.ArmReach + ClawSegment.Length * style.ClawReach;
        var occupied = _arms[0].Active || _arms[1].Active;
        _idle = occupied ? 0f : _idle + dt;
        var kick = basis.LeftLeg is not null || basis.RightLeg is not null;
        var ready = style.Attacks && !occupied && !kick && sense.OpponentAlive && distance < range + 0.10f && _idle >= style.ClawRest;
        var side = (_strokeCount & 1) == 0 ? Hand.Right : Hand.Left;
        if (_nextAim < 0)
        {
            _nextAim = NextAim(style);
        }

        var aim = Aim(sense, _nextAim);
        var left = _arms[0].Read(Hand.Left, ready && side == Hand.Left, true, sense, aim, dt, style.ClawDriveSeconds, style.ClawWindup);
        var right = _arms[1].Read(Hand.Right, ready && side == Hand.Right, true, sense, aim, dt, style.ClawDriveSeconds, style.ClawWindup);
        if (ready)
        {
            _strokeCount++;
            _nextAim = NextAim(style);
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

    /// <summary>
    /// 次の狙いを重みの比で選ぶ（重み付きの順番回し: 各狙いに重みを足し、いちばん溜まった狙いを選んで合計を引く。乱数を使わない）。
    /// 重みが全部 1 なら 0 → 1 → 2 の繰り返し（等しいときは前の狙い）。
    /// </summary>
    internal int NextAim(BotStyle style)
    {
        Span<float> w = [style.ClawAimNeck, style.ClawAimArm, style.ClawAimTorso];
        var total = w[0] + w[1] + w[2];
        if (!(total > 0f))
        {
            w[0] = w[1] = w[2] = 1f;
            total = 3f;
        }

        var best = -1;
        for (var i = 0; i < 3; i++)
        {
            _aimCredit[i] += w[i];
            best = w[i] > 0f && (best < 0 || _aimCredit[i] > _aimCredit[best]) ? i : best;
        }

        _aimCredit[best] -= total;
        return best;
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
