using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("L3c")]
public sealed class AttackPerceptionTests
{
    [TestMethod]
    public void DuckRemainsUnseenForTheDelay()
    {
        var perception = new AttackPerception();
        var standing = new FighterSense(default, 0.5634f, Vector3.UnitY * 2.817f, Vector3.Zero, true);
        const float dt = 1f / 240f;
        // 見えるまでの遅れの歩数（240 Hz。人形の単位で 0.2 秒 = 48 歩）。
        var delaySteps = (int)MathF.Ceiling((AttackPerception.DelaySeconds / dt) - 1e-3f);

        for (var step = 0; step < delaySteps; step++)
        {
            Assert.IsNull(perception.Update(standing, dt));
        }

        var ducking = standing with { OpponentHead = Vector3.UnitY * 1.972f };
        for (var step = 0; step < delaySteps; step++)
        {
            Assert.AreEqual(standing.OpponentHead, perception.Update(ducking, dt)!.Value.Sense.OpponentHead);
        }
        Assert.AreEqual(ducking.OpponentHead, perception.Update(ducking, dt)!.Value.Sense.OpponentHead);
    }

    [TestMethod]
    public void AimProjectsObservedHeadMotionTowardImpact()
    {
        var sense = new FighterSense(default, 0.5634f, Vector3.UnitY * 2.817f, Vector3.Zero, true);
        var observed = new AttackObservation(sense, -Vector3.UnitY * 1.678f);
        var aim = observed.Aim(PunchSpec.Of(Technique.Jab), AimGoal.Center, Vector3.Zero, sense.ArmReach, 0.4196f);
        Assert.AreEqual(2.817f - (1.678f * 0.4196f), aim.Y, 1e-4f); // 頭の高さ − 見えた速さ × 先読み（人形の単位では 1 − 1 × 0.25 = 0.75）
        Assert.AreEqual(0f, aim.X, 1e-6f);
        Assert.AreEqual(0f, aim.Z, 1e-6f);
    }
}
