using System.Numerics;

namespace MiniRing;

/// <summary>試合の前の場面（番組の映像）で両者が取る立ち方。</summary>
public enum StagePose
{
    /// <summary>始めの位置（リングの真ん中で向き合う）に立つ。</summary>
    Center,

    /// <summary>それぞれ自分のコーナーへ歩いて、相手の方を向いて立つ。</summary>
    Corners,
}

public sealed partial class BoxingGame
{
    private StagePose? _stagePose;
    private (Vector3 Red, Vector3 Blue)? _stageHome;

    /// <summary>
    /// 試合の前の場面のためだけの「影の試合」を 1 歩進める（番組の映像用。本番の試合とは別の <see cref="BoxingGame"/> で、本番には何も響かない）。
    /// 生成直後の体は腕を真横に広げた姿勢（<see cref="Fighter.Spawn"/>）なので、そのまま映さない。ボットも入力も読まず、
    /// 構えて立つ指令（<see cref="FighterCommand"/>: 相手の方を向き、いつものガードの位置に両手を置く。<paramref name="pose"/> が
    /// <see cref="StagePose.Corners"/> なら自分のコーナーへ歩く）だけを出す。体はバネで追うので、軽く揺れて立つ（姿勢は書き込まない: CLAUDE.md §1.2）。
    /// ラウンドの時計・打撃の数え・ダウンは進めない。
    /// </summary>
    public void StepStage(float dt, StagePose pose)
    {
        _stageHome ??= (Player.PelvisPosition, Cpu.PelvisPosition);
        _stagePose = pose;
        var substeps = Math.Max(1, (int)MathF.Ceiling((dt * SubstepRate) - 1e-3f));
        var sub = dt / substeps;
        for (var i = 0; i < substeps; i++)
        {
            Substep(default, null, sub);
        }

        _lookTime += dt;
    }

    private FighterCommand StageCommand(Fighter self, Fighter opponent, StagePose pose)
    {
        var rules = Rules ?? MatchRules.Tv;
        var red = self == Player;
        var target = pose == StagePose.Corners ? Layout.RestCorner(red, rules.CornerInset) : red ? _stageHome!.Value.Red : _stageHome!.Value.Blue;
        var toTarget = target - self.PelvisPosition;
        toTarget.Y = 0f;
        var distance = toTarget.Length();
        var move = distance > rules.CornerArrive ? Vector3.Normalize(toTarget) * MathF.Min(1f, distance / rules.CornerSlowDown) * rules.RestWalk : Vector3.Zero;
        return new FighterCommand(move, opponent.ChestPosition - self.ChestPosition,
            new HandCommand(AiControl.Guard(Hand.Left, self.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, self.ArmReach), false));
    }
}
