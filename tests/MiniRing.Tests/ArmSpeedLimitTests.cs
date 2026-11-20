using System.Numerics;
using Aigis.Physics;
using MiniRing.Motion.Stick;

namespace MiniRing.Tests;

/// <summary>筋の速度を変えた体に、古い縮尺の関節速度の上限を残さない。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class ArmSpeedLimitTests
{
    [TestMethod]
    public void MotionReview_DoesNotFlagSpeedsAllowedByEitherArmMuscle()
    {
        var tuning = FighterTuning.Default with { ShoulderMuscleMaxSpeed = 55f, ElbowMuscleMaxSpeed = 30f, ArmServoMaxSpeed = 4f };
        Assert.AreEqual(55f, StickReviewSettings.From(tuning).ArmSpeed,
            "腕の警報は手首・固定用の値ではなく、肩・肘が出せる速さから決める");
    }

    [TestMethod]
    public void ShoulderAndElbow_UseTheirMuscles_WithoutTheUnrelatedWristAndLinkLimit()
    {
        var normal = FighterTuning.Default;
        var low = Reach(normal with { ArmServoMaxSpeed = 1f });
        var high = Reach(normal with { ArmServoMaxSpeed = 100f });
        Assert.AreEqual(high, low, "手首・固定用の速度を変えても、肩・肘の筋が同じなら拳の動きは同じ");

        var slowShoulder = Reach(normal with { ShoulderMuscleMaxSpeed = 1f });
        var slowElbow = Reach(normal with { ElbowMuscleMaxSpeed = 1f });
        Assert.IsGreaterThan(1e-4f, Vector3.Distance(high, slowShoulder), "肩の筋の速さは腕の動きに効く");
        Assert.IsGreaterThan(1e-4f, Vector3.Distance(high, slowElbow), "肘の筋の速さは腕の動きに効く");
    }

    private static Vector3 Reach(FighterTuning tuning)
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var fighter = Fighter.Spawn(world, tuning, "arm", Vector3.Zero, 0f);
        var left = AiControl.Guard(Hand.Left, fighter.ArmReach);
        var guard = AiControl.Guard(Hand.Right, fighter.ArmReach);
        const float dt = 1f / 240f;
        for (var step = 0; step < 240; step++)
        {
            var right = step < 200 ? guard : guard + (Vector3.UnitZ * fighter.ArmReach);
            fighter.ApplyControl(new FighterCommand(Vector3.Zero, Vector3.UnitZ,
                new HandCommand(left, false), new HandCommand(right, false), Vector3.Zero), dt);
            world.Step(dt);
        }
        return fighter.GloveCenter(Hand.Right);
    }
}
