using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 力学モデルの性質: 種類で場合分けせず、位置関係と向きだけで「体重が乗る／手打ち」が連続的に決まること。
/// </summary>
[TestClass]
public sealed class PunchMechanicsTests
{
    private const float Mass = 6.8f;
    private static readonly BodyInertia Body = BodyInertia.Cylinder(Mass, 0.6f, 0.053f);

    [TestMethod]
    public void ForceLineThroughTheAxis_CarriesTheWholeBody()
    {
        // 重心から前へ、前向きの撃力（作用線が重心を通る）= 体重がそのまま乗る。
        var m = PunchMechanics.BodyEffectiveMass(Body, Quaternion.Identity, new Vector3(0f, 0f, 0.15f), Vector3.UnitZ);

        Assert.AreEqual(Mass, m, 1e-3f);
    }

    [TestMethod]
    public void TheFurtherTheForceLineMissesTheAxis_TheLessBodyIsBehindIt()
    {
        float At(float lateral) => PunchMechanics.BodyEffectiveMass(Body, Quaternion.Identity, new Vector3(lateral, 0.1f, 0.15f), Vector3.UnitZ);

        Assert.IsGreaterThan(At(0.03f), At(0f));
        Assert.IsGreaterThan(At(0.08f), At(0.03f));
        Assert.IsLessThan(Mass * 0.3f, At(0.08f), "off-axis push is mostly arm");
    }

    [TestMethod]
    public void RotatingAboutTheAxis_UsesRotationalInertia_AndTighterIsHeavier()
    {
        // フック: 体軸まわりの接線方向。有効質量 = 1 / (1/M + r²/I_yaw)。
        float Hook(float r) => PunchMechanics.BodyEffectiveMass(Body, Quaternion.Identity, new Vector3(r, 0f, 0f), Vector3.UnitZ);

        var expected = 1f / ((1f / Mass) + (0.1f * 0.1f / Body.LocalInertia.Y));
        Assert.AreEqual(expected, Hook(0.1f), 1e-4f);
        Assert.IsGreaterThan(Hook(0.1f), Hook(0.05f), "a compact hook (closer to the axis) carries more of the body");
    }

    [TestMethod]
    public void DrivingAlongTheAxis_IsHeavierThanSwingingAroundIt()
    {
        // 体軸方向（突き上げ）は前後に倒す慣性が効くので、同じ距離の体軸まわりの回転より重い。
        var uppercut = PunchMechanics.BodyEffectiveMass(Body, Quaternion.Identity, new Vector3(0f, 0f, 0.08f), Vector3.UnitY);
        var hook = PunchMechanics.BodyEffectiveMass(Body, Quaternion.Identity, new Vector3(0.08f, 0f, 0f), Vector3.UnitZ);

        Assert.IsGreaterThan(hook, uppercut);
    }

    [TestMethod]
    public void EffectiveMass_FollowsTheTorsoOrientation()
    {
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        var local = PunchMechanics.BodyEffectiveMass(Body, Quaternion.Identity, new Vector3(0.06f, 0.1f, 0.12f), new Vector3(0.2f, 0f, 1f));
        var world = PunchMechanics.BodyEffectiveMass(Body, turned,
            Vector3.Transform(new Vector3(0.06f, 0.1f, 0.12f), turned), Vector3.Transform(new Vector3(0.2f, 0f, 1f), turned));

        Assert.AreEqual(local, world, 1e-4f);
    }

    [TestMethod]
    public void Coupling_PushAndPullAlongTheArmAreFull_SidewaysIsTheLever()
    {
        var arm = new Vector3(0f, 0f, 0.2f);

        Assert.AreEqual(1f, PunchMechanics.Coupling(arm, Vector3.UnitZ, 1f), 1e-5f);
        Assert.AreEqual(1f, PunchMechanics.Coupling(arm, -Vector3.UnitZ, 1f), 1e-5f);
        Assert.AreEqual(PunchMechanics.LeverTransfer, PunchMechanics.Coupling(arm, Vector3.UnitX, 1f), 1e-5f);
        Assert.AreEqual(0.5f, PunchMechanics.Coupling(arm, Vector3.UnitZ, 0.5f), 1e-5f);
    }

    [TestMethod]
    public void ImpactMass_IsContinuousInDirection_NoCaseSplits()
    {
        // 方向を 0.1° ずつ回しても有効質量が飛ばない（種類の境目が無い）。作用線が軸を通る付近は鋭い山（芯）だが連続。
        var r = new Vector3(-0.04f, 0.1f, 0.14f);
        var arm = new Vector3(0.03f, 0f, 0.18f);
        var previous = float.NaN;
        for (var step = 0; step <= 3600; step++)
        {
            var degrees = step * 0.1f;
            var a = degrees * MathF.PI / 180f;
            var n = new Vector3(MathF.Sin(a), 0.3f * MathF.Cos(a * 2f), MathF.Cos(a));
            var m = PunchMechanics.ImpactMass(0.3f, PunchMechanics.BodyEffectiveMass(Body, Quaternion.Identity, r, n), PunchMechanics.Coupling(arm, n, 1f));
            if (!float.IsNaN(previous))
            {
                Assert.IsLessThan(0.15f, MathF.Abs(m - previous), $"jump at {degrees} deg");
            }

            previous = m;
        }
    }

    [TestMethod]
    public void Damage_HasAThreshold_IsCompressed_AndCapped()
    {
        Assert.AreEqual(0f, PunchMechanics.Damage(PunchMechanics.MomentumThreshold));
        var arm = PunchMechanics.Damage(0.9f);
        var body = PunchMechanics.Damage(9f);

        Assert.IsGreaterThan(arm, body);
        Assert.IsLessThan(arm * 7f, body, "10x the momentum must not be 10x the damage");
        Assert.AreEqual(PunchMechanics.MaxDamage, PunchMechanics.Damage(1000f));
    }

    [TestMethod]
    public void TorsoTwist_FollowsTheHands()
    {
        var guardL = new Vector3(0.2f, -0.1f, 0.5f);
        var guardR = new Vector3(-0.2f, -0.1f, 0.5f);
        const float max = 0.6f;

        Assert.AreEqual(0f, PunchMechanics.TorsoTwist(guardL, guardR, 1f, max), 1e-5f);
        Assert.IsGreaterThan(0.1f, PunchMechanics.TorsoTwist(guardL, guardR with { Z = 1f }, 1f, max), "right straight brings the right shoulder forward");
        Assert.IsLessThan(-0.1f, PunchMechanics.TorsoTwist(guardL with { Z = 1f }, guardR, 1f, max), "left straight is the mirror");
        Assert.IsGreaterThan(0.1f, PunchMechanics.TorsoTwist(guardL, guardR with { X = 0.5f }, 1f, max), "right hand crossing to the left turns the body (hook)");
        Assert.IsLessThanOrEqualTo(max + 1e-6f, PunchMechanics.TorsoTwist(guardL, new Vector3(5f, 0f, 9f), 1f, max));
    }
}
