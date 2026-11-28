using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 物差し M1（計画 §1.1・段 0）: 構えて立っているだけの体に、接触と関節の外から掛けている「立たせる力」（ホバー・起こすねじり・運ぶ力。
/// <c>Fighter.RecordSupportForce</c> の入口）の大きさを、体重との比で記録する。今は 0 でない（地図 V1）。合格の条件は今の値で通る緩い上限で、
/// 段 3（支えの関節トルク化）で「前より小さい」を同じビルドの比べで見るための記録。
/// </summary>
[TestClass]
[TestCategory("L3b")]
[TestCategory("slow")]
public sealed class BodySanityTests
{
    private const float Dt = 1f / 240f;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void M1_TheOutsideLift_OnAStandingBody_IsRecorded()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var cmd = new FighterCommand(Vector3.Zero, Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
        var (lift, torque, steps) = (0f, 0f, 0);
        for (var i = 0; i < 240 * 3; i++)
        {
            f.ApplyControl(cmd, Dt);
            world.Step(Dt);
            if (i >= 240)
            {
                lift += f.SupportLift;
                torque += f.SupportTorqueWanted.Length();
                steps++;
            }
        }

        var weight = f.TotalMass * 9.81f;
        var liftShare = lift / (steps * Dt) / weight;
        var meanTorque = torque / steps;
        TestContext.WriteLine($"M1: 体重 {weight:0} N、外から立たせる上向きの力の平均 {liftShare:P1}（体重比）、起こすねじりの平均 {meanTorque:0.0} N·m（1〜3 秒、構えて立つだけ）");
        Assert.AreEqual(FighterState.Standing, f.State);
        Assert.IsTrue(float.IsFinite(liftShare) && float.IsFinite(meanTorque));
        Assert.IsLessThan(1.5f, liftShare, "外の力が体重の 1.5 倍を超えた（段 0 の記録の上限。P4 では 0 が目標）");
    }
}
