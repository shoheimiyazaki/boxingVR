using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 拳の狙いは自分の胸の前（<see cref="PunchPath.NearestStrike"/>）より手前に来ない（LogicVersion 55、2026-11-19）。
/// 詰まった間で相手の頭が自分の肩の横・後ろへ回ると、狙いの点をそのまま打ち抜いて目標が背中側へ伸び、腕が後ろへ回っていた（ユーザー指摘）。
/// </summary>
[TestClass]
public sealed class PunchAimTests
{
    private const float Reach = 0.62f;

    [TestMethod]
    public void AimBehindTheShoulders_NeverPullsTheFistBehind_ForAnyTechnique()
    {
        // 頭が並ぶほど詰まった時の狙い: 自分の右肩の横、肩の中心より少し後ろ。
        var aim = new Vector3(-0.3f, 0.15f, -0.25f) * Reach;
        foreach (var technique in Enum.GetValues<Technique>())
        {
            var spec = PunchBook.Default.Of(technique);
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                var guard = TechniqueExecution.Guard(hand, Hand.Left, Reach, new Vector3(0f, 0.1f * Reach, 0f), 1f, 1f, 1f, 0f);
                for (var t = 0f; t <= spec.Duration; t += spec.Duration / 40f)
                {
                    var action = new PunchAction(technique, spec, t, spec.Duration, false);
                    var target = TechniqueExecution.Punch(action, hand, Hand.Left, guard, aim, Reach);
                    Assert.IsGreaterThan(0f, target.Z, $"{technique} {hand} at {t:F3}s: the fist target stays in front of the shoulders ({target})");
                }
            }
        }
    }

    [TestMethod]
    public void TheWindUpBeforeALoadedPunch_AlsoStaysInFront()
    {
        // 溜め（Time < 0、Loaded）の間も、狙いが背中側でも拳の目標は肩より前（溜めの引きは 0.1 腕まで）。
        var aim = new Vector3(0.3f, 0.1f, -0.4f) * Reach;
        foreach (var technique in Enum.GetValues<Technique>())
        {
            var spec = PunchBook.Default.Of(technique);
            var guard = TechniqueExecution.Guard(Hand.Right, Hand.Left, Reach, new Vector3(0f, 0.1f * Reach, 0f), 1f, 1f, 1f, 0f);
            for (var t = -0.3357f; t <= spec.Duration; t += 0.01678f)
            {
                var action = new PunchAction(technique, spec, t, spec.Duration, false, Load: 0.3357f);
                var target = TechniqueExecution.Punch(action, Hand.Right, Hand.Left, guard, aim, Reach);
                Assert.IsGreaterThan(0f, target.Z, $"{technique} loaded at {t:F2}s ({target})");
            }
        }
    }

    [TestMethod]
    public void Through_ClampsForEveryCaller_TheOldCpuAndClassicKeysToo()
    {
        // AiControl（古い CPU）と従来のキーボード操作は PunchPath.Through を直に使う。狙いが背中側でも打ち抜く点は胸の前。
        var through = PunchPath.Through(new Vector3(-0.2f, 0.1f, -0.6f) * Reach, 0.3f, Reach);
        Assert.IsGreaterThanOrEqualTo(PunchPath.NearestStrike * Reach, through.Z, through.ToString());
        for (var t = 0f; t <= 1f; t += 0.025f)
        {
            var point = PunchPath.Point(AiControl.Guard(Hand.Right, Reach), PunchPath.Windup(Hand.Right, 0.5f, 0f, Reach), through, t);
            Assert.IsGreaterThan(0f, point.Z, $"the shared path at {t:F3}");
        }
    }

    [TestMethod]
    public void AnElbowAimedBehind_StrikesForward_AtContact()
    {
        var aim = new Vector3(-0.3f, 0.2f, -0.3f) * Reach;
        foreach (var technique in Enum.GetValues<ElbowTechnique>())
        {
            var spec = ElbowSpec.Of(technique);
            var guard = TechniqueExecution.Guard(Hand.Right, Hand.Left, Reach, new Vector3(0f, 0.1f * Reach, 0f), 1f, 1f, 1f, 0f);
            var command = TechniqueExecution.Elbow(spec, Hand.Right, ElbowSpec.ContactShare * spec.Duration, spec.Duration, guard, aim, Reach);
            Assert.IsGreaterThan(0f, command.ElbowTarget!.Value.Z, $"{technique}: the elbow drives forward at contact ({command.ElbowTarget})");
        }
    }

    [TestMethod]
    public void Strikable_KeepsAimsInFront_AndMovesOnlyTheDepth()
    {
        var front = new Vector3(0.1f, 0.2f, 0.9f);
        Assert.AreEqual(front, PunchPath.Strikable(front, 1f), "an aim in front is untouched (bit for bit)");
        var behind = PunchPath.Strikable(new Vector3(-0.3f, 0.4f, -0.5f), 1f);
        Assert.AreEqual(new Vector3(-0.3f, 0.4f, PunchPath.NearestStrike), behind);
    }
}
