using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>狩りのドリル（huntlab）の部品: 鎌の狙いの重み（教育の値）と、抵抗しない入力元。</summary>
[TestClass]
public sealed class HuntTests
{
    private static readonly int[] Cycle = [0, 1, 2, 0, 1, 2, 0, 1, 2];
    private static readonly int[] OneCycle = [0, 1, 2];

    [TestMethod]
    public void ClawAims_EqualWeightsKeepTheOldNeckArmTorsoCycle()
    {
        // 教育が空（重み 1・1・1）なら、今までの「首 → 腕 → 胴」の繰り返しと同じ（教育が空 ≡ 教科書）。
        var bot = new ClawBot(new BotStats());
        var style = new BotStyle();
        var aims = Enumerable.Range(0, 9).Select(_ => bot.NextAim(style)).ToArray();
        CollectionAssert.AreEqual(Cycle, aims);
    }

    [TestMethod]
    public void ClawAims_FollowTheWeightRatioAndSkipZeroWeights()
    {
        var bot = new ClawBot(new BotStats());
        var style = new BotStyle { ClawAimNeck = 2f, ClawAimArm = 0f, ClawAimTorso = 1f };
        var aims = Enumerable.Range(0, 30).Select(_ => bot.NextAim(style)).ToArray();
        Assert.AreEqual(0, aims.Count(a => a == 1), "重み 0 の腕は選ばない");
        Assert.AreEqual(20, aims.Count(a => a == 0), "首:胴 = 2:1");
        Assert.AreEqual(10, aims.Count(a => a == 2));
    }

    [TestMethod]
    public void ClawAims_AllZeroFallsBackToTheCycle()
    {
        var bot = new ClawBot(new BotStats());
        var style = new BotStyle { ClawAimNeck = 0f, ClawAimArm = 0f, ClawAimTorso = 0f };
        CollectionAssert.AreEqual(OneCycle, Enumerable.Range(0, 3).Select(_ => bot.NextAim(style)).ToArray());
    }

    [TestMethod]
    public void ClawReach_IsValidatedIntoItsRange()
    {
        Assert.AreEqual(1f, (new BotStyle { ClawReach = 4f }).Validated().ClawReach);
        Assert.AreEqual(0.56f, (new BotStyle { ClawReach = float.NaN }).Validated().ClawReach);
        Assert.AreEqual(0.56f, new BotStyle().ClawReach);
    }

    [TestMethod]
    public void Passive_StandsStillWithArmsDownAndFacesTheOpponent()
    {
        var self = new BodyFrame(new Vector3(0f, 1.4f, 0f), 0f);
        var sense = new FighterSense(self, 0.7f, new Vector3(1f, 1.6f, 1f), new Vector3(1f, 1.3f, 1f), true);
        var command = new PassiveControl().Read(sense, 1f / 60f);
        Assert.AreEqual(Vector3.Zero, command.MoveWorld);
        Assert.AreEqual(Vector3.Zero, command.Step);
        Assert.AreEqual(0f, Vector3.Dot(command.FaceDirection, Vector3.UnitY), 1e-6f);
        Assert.IsGreaterThan(0.99f, Vector3.Dot(command.FaceDirection, Vector3.Normalize(new Vector3(1f, 0f, 1f))));
        foreach (var hand in new[] { command.Left, command.Right })
        {
            Assert.IsFalse(hand.Grip);
            Assert.IsLessThan(-0.5f * 0.7f, hand.TargetLocal.Y, "手は肩よりずっと下（腕を下ろしている）");
            Assert.IsLessThan(0.1f * 0.7f, hand.TargetLocal.Z, "手を前へ出さない（守らない）");
        }

        Assert.IsNull(command.LeftLeg);
        Assert.IsNull(command.RightLeg);
        Assert.AreEqual(-command.Left.TargetLocal.X, command.Right.TargetLocal.X, 1e-6f, "左右対称");
    }

    [TestMethod]
    public void Passive_IsNotWrappedWithTheGroundStrikeInAMatchWithoutReferee()
    {
        // 抵抗しない入力元は、レフェリーのいない試合でも倒れた相手を攻めない（GroundStriker で包まない）。
        var corners = BoxingCorners.Default with { Player = _ => new PassiveControl(), Referee = false };
        using var game = new BoxingGame(seed: 3, corners: corners);
        Assert.IsInstanceOfType<PassiveControl>(game.PlayerControl);
        Assert.IsInstanceOfType<GroundStriker>(game.CpuControl);
    }
}
