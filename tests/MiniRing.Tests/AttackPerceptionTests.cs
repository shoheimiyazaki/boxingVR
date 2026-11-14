using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
public sealed class AttackPerceptionTests
{
    [TestMethod]
    public void DuckRemainsUnseenFor48Steps()
    {
        var perception = new AttackPerception();
        var standing = new FighterSense(default, 0.2f, Vector3.UnitY, Vector3.Zero, true);
        const float dt = 1f / 240f;

        for (var step = 0; step < 48; step++)
        {
            Assert.IsNull(perception.Update(standing, dt));
        }

        var ducking = standing with { OpponentHead = Vector3.UnitY * 0.7f };
        for (var step = 0; step < 48; step++)
        {
            Assert.AreEqual(standing.OpponentHead, perception.Update(ducking, dt)!.Value.Sense.OpponentHead);
        }
        Assert.AreEqual(ducking.OpponentHead, perception.Update(ducking, dt)!.Value.Sense.OpponentHead);
    }

    [TestMethod]
    public void AimProjectsObservedHeadMotionTowardImpact()
    {
        var sense = new FighterSense(default, 0.2f, Vector3.UnitY, Vector3.Zero, true);
        var observed = new AttackObservation(sense, -Vector3.UnitY);
        var aim = observed.Aim(PunchSpec.Of(Technique.Jab), AimGoal.Center, Vector3.Zero, sense.ArmReach, 0.25f);
        Assert.AreEqual(Vector3.UnitY * 0.75f, aim);
    }
}
