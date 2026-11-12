using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 狙った部位を打つ（<see cref="MotionAim"/>・<see cref="SwordsmanBot.Aim"/>）: 技の通り道を、相手の今の姿勢でのその部位の位置へずらす。
/// </summary>
[TestClass]
public sealed class AimTests
{
    private const float Reach = 0.17f;

    private static readonly MotionFrame Guard = new(new Vector3(-0.2f, -0.57f, 0.78f), new Vector3(0.03f, -0.86f, 0.5f), Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f)),
        Vector3.Normalize(new Vector3(0f, -0.8f, 0.6f)), Vector3.Zero, 0f, Vector3.Zero);

    private static readonly MotionTechnique Men = new("men", WeaponKind.Katana, SwordForms.Katana()[0], 0.45f);

    /// <summary>相手は <paramref name="distance"/> 前。頭・胸・腰の高さと、両拳の位置（ワールド）。</summary>
    private static FighterSense Opponent(float distance, float headY = 1.1f, Vector3? leftFist = null, Vector3? rightFist = null)
    {
        var at = RingLayout.Boxing.Center with { Y = 1f };
        var chest = at + new Vector3(0f, 0f, distance);
        return new(new BodyFrame(at, 0f), Reach, chest with { Y = headY }, chest, OpponentAlive: true,
            OpponentLeft: new GloveSense(leftFist ?? chest + new Vector3(0.03f, 0.02f, -0.08f), Vector3.Zero),
            OpponentRight: new GloveSense(rightFist ?? chest + new Vector3(-0.03f, 0.02f, -0.08f), Vector3.Zero),
            OpponentBody: new BodySense(chest + new Vector3(0.05f, 0.03f, 0f), chest + new Vector3(-0.05f, 0.03f, 0f), chest - new Vector3(0f, 0.15f, 0f)),
            SelfArmed: true);
    }

    [TestMethod]
    public void AnAimedTechnique_LandsWhereTheTargetIs_UpDownAndSideways()
    {
        var (point, frame) = MotionAim.Landing(Men, Reach);
        var target = point + new Vector3(0.2f, -0.4f, 0f);
        var aimed = MotionAim.Aimed(Men, target, Reach, new AimTuning(Gain: 1f, Lead: 0.6f, MaxShift: 1f));
        var shift = aimed.Motion.Frames[frame].Right - Men.Motion.Frames[frame].Right;
        Assert.AreEqual(0.2f, shift.X, 1e-4f);
        Assert.AreEqual(-0.4f, shift.Y, 1e-4f);
        Assert.AreEqual(0f, shift.Z, 1e-6f, "forward is matched by the distance, not by the hands");
        // 構えからは跳ばない: 始めのフレームは同じ。
        Assert.AreEqual(Men.Motion.Frames[0].Right, aimed.Motion.Frames[0].Right);
        Assert.AreEqual(Men.Motion.Frames[0].Blade, aimed.Motion.Frames[0].Blade, "the blade's direction and edge stay as they were");
    }

    [TestMethod]
    public void TheShift_IsCapped_SoAFarTargetIsLeftToAnotherTechnique()
    {
        var (point, frame) = MotionAim.Landing(Men, Reach);
        var aimed = MotionAim.Aimed(Men, point + new Vector3(0f, -3f, 0f), Reach, new AimTuning(MaxShift: 0.6f));
        Assert.AreEqual(-0.6f, (aimed.Motion.Frames[frame].Right - Men.Motion.Frames[frame].Right).Y, 1e-4f);
    }

    [TestMethod]
    public void AimingAtTheFists_StrikesFromFartherOut()
    {
        // 前に出した拳（小手）を狙うなら、胸を狙うより遠くから出す（拳が胸より手前にある分だけ）。
        var sense = Opponent(0.6f);
        var fist = MotionAim.TargetPoint(sense, BodyPartKind.Forearm);
        var forward = MotionAim.ForwardOffset(sense, fist, Vector3.UnitZ);
        Assert.IsGreaterThan(0.03f, forward);
        Assert.AreEqual(0f, MotionAim.ForwardOffset(sense, MotionAim.TargetPoint(sense, BodyPartKind.Chest), Vector3.UnitZ), 1e-5f);
    }

    [TestMethod]
    public void AnAimedStrike_FollowsTheTargetsPosture()
    {
        // 同じ技・同じ「頭」の狙いでも、相手が屈んでいれば打ちの通り道は下を通る。
        float LandingY(float headY)
        {
            var bot = new SwordsmanBot([Men], Guard, seed: 1, rate: 1.77f, exact: true, aim: new AimTuning(MaxShift: 1.5f)) { Aim = BodyPartKind.Head };
            var sense = Opponent(0.45f, headY);
            for (var i = 0; i < 10 && bot.Current is null; i++)
            {
                _ = bot.Read(sense, 1f / 60f);
            }

            Assert.IsNotNull(bot.Current);
            var (_, frame) = MotionAim.Landing(Men, Reach);
            return bot.Current.Motion.Frames[frame].Right.Y;
        }

        var upright = LandingY(1.1f);
        var crouched = LandingY(1.06f);
        Assert.AreEqual(-0.04f / Reach, crouched - upright, 0.01f, "the path drops by as much as the head did");
    }

    [TestMethod]
    public void TheBot_AimsWhereTheGuardLeavesAnOpening()
    {
        var skilled = Men with { Skill = new Dictionary<BodyPartKind, float> { [BodyPartKind.Head] = 0.8f, [BodyPartKind.Chest] = 0.8f } };
        BodyPartKind? AimAgainst(FighterSense sense)
        {
            var bot = new SwordsmanBot([skilled], Guard, seed: 1, rate: 1.77f) { AutoAim = true };
            _ = bot.Read(sense, 1f / 60f);
            return bot.Aim;
        }

        // 拳を額の前へ上げた相手: 頭は守られ、胸が空く。
        var chest = RingLayout.Boxing.Center with { Y = 1f } + new Vector3(0f, 0f, 0.7f);
        var high = Opponent(0.7f, leftFist: chest + new Vector3(0.02f, 0.1f, -0.03f), rightFist: chest + new Vector3(-0.02f, 0.1f, -0.03f));
        Assert.AreEqual(BodyPartKind.Chest, AimAgainst(high));
        // 拳を胸の前に下げた相手: 胸が守られ、頭が空く。
        var low = Opponent(0.7f, leftFist: chest + new Vector3(0.02f, -0.01f, -0.04f), rightFist: chest + new Vector3(-0.02f, -0.01f, -0.04f));
        Assert.AreEqual(BodyPartKind.Head, AimAgainst(low));
    }

    [TestMethod]
    public void WithoutAim_TheTechniqueIsUntouched()
    {
        var bot = new SwordsmanBot([Men], Guard, seed: 1, rate: 1.77f, exact: true);
        var sense = Opponent(0.45f);
        for (var i = 0; i < 10 && bot.Current is null; i++)
        {
            _ = bot.Read(sense, 1f / 60f);
        }

        Assert.AreEqual(Men.Motion.Frames[5].Right, bot.Current!.Motion.Frames[5].Right);
    }
}
