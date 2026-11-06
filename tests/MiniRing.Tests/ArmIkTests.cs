using System.Numerics;

namespace MiniRing.Tests;

[TestClass]
public sealed class ArmIkTests
{
    private const float Eps = 1e-4f;

    [TestMethod]
    public void TwoBone_ReachableTarget_KeepsLengthsAndHitsTarget()
    {
        var shoulder = new Vector3(0f, 1f, 0f);
        var target = new Vector3(0.2f, 0.9f, 0.3f);
        var (elbow, hand) = ArmIk.SolveTwoBone(shoulder, target, shoulder - Vector3.UnitY, 0.3f, 0.25f);

        Assert.AreEqual(0.3f, Vector3.Distance(shoulder, elbow), Eps);
        Assert.AreEqual(0.25f, Vector3.Distance(elbow, hand), Eps);
        Assert.IsLessThan(Eps, Vector3.Distance(hand, target));
    }

    [TestMethod]
    public void TwoBone_UnreachableAndDegenerateTargets_StayFinite()
    {
        var (_, far) = ArmIk.SolveTwoBone(Vector3.Zero, new Vector3(0f, 0f, 2f), -Vector3.UnitY, 0.3f, 0.25f);
        var (elbow, _) = ArmIk.SolveTwoBone(Vector3.Zero, new Vector3(0f, 0f, 0.4f), new Vector3(0f, 0f, 1f), 0.3f, 0.25f);

        Assert.AreEqual(0.55f, far.Z, 1e-3f);
        Assert.IsTrue(float.IsFinite(elbow.X) && float.IsFinite(elbow.Y) && float.IsFinite(elbow.Z));
        Assert.AreEqual(0.3f, elbow.Length(), Eps);
    }

    [TestMethod]
    public void JointTargets_StraightArm_MapsRestAxisOntoArmDirection()
    {
        foreach (var side in new[] { -1f, 1f })
        {
            var shoulder = Vector3.Zero;
            var elbow = new Vector3(0f, 0f, 0.3f);
            var hand = new Vector3(0f, 0f, 0.55f);
            var (rotation, angle) = ArmIk.JointTargets(shoulder, elbow, hand, Vector3.UnitY, side);

            // T ポーズの腕軸（side·X）が前方 +Z を向く。
            var armAxis = Vector3.Transform(side * Vector3.UnitX, rotation);
            Assert.IsLessThan(1e-3f, Vector3.Distance(armAxis, Vector3.UnitZ), $"side {side}: {armAxis}");
            Assert.AreEqual(0f, angle, 1e-3f);
        }
    }

    [TestMethod]
    public void JointTargets_BentArm_RotatingAboutHingeTakesUpperToForearm()
    {
        foreach (var side in new[] { -1f, 1f })
        {
            var shoulder = Vector3.Zero;
            var elbow = new Vector3(side * 0.1f, -0.25f, 0.1f);
            var hand = new Vector3(side * 0.05f, -0.1f, 0.3f);
            var normal = Vector3.Cross(elbow - shoulder, hand - elbow);
            var (rotation, angle) = ArmIk.JointTargets(shoulder, elbow, hand, normal, side);

            // 正の肘角は「ヒンジ軸（左 +Y / 右 −Y のワールド像）回りの回転」で上腕方向を前腕方向へ移す。
            var hinge = Vector3.Transform(side < 0f ? Vector3.UnitY : -Vector3.UnitY, rotation);
            var u = Vector3.Normalize(elbow - shoulder);
            var f = Vector3.Normalize(hand - elbow);
            var rotated = Vector3.Transform(u, Quaternion.CreateFromAxisAngle(hinge, angle));
            Assert.IsLessThan(1e-3f, Vector3.Distance(rotated, f), $"side {side}");
        }
    }

    [TestMethod]
    public void BendNormal_PutsElbowOnPoleSide()
    {
        var shoulder = Vector3.Zero;
        var target = new Vector3(0f, 0f, 0.4f);
        var pole = new Vector3(0f, -1f, 0.1f);
        var (elbow, hand) = ArmIk.SolveTwoBone(shoulder, target, pole, 0.3f, 0.25f);
        var n = ArmIk.BendNormal(shoulder, target, pole);
        var expected = Vector3.Normalize(Vector3.Cross(elbow - shoulder, hand - elbow));

        Assert.IsLessThan(0f, elbow.Y);
        Assert.IsLessThan(1e-3f, Vector3.Distance(n, expected));
    }
}
