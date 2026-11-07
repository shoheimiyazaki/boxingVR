using System.Numerics;

namespace MiniRing.Tests;

[TestClass]
public sealed class MappingAndMathTests
{
    private const float Eps = 1e-4f;

    [TestMethod]
    public void BodyFrame_FollowsYawConvention_AndRoundTrips()
    {
        var frame = new BodyFrame(new Vector3(1f, 2f, 3f), MathF.PI * 0.5f);

        AssertClose(Vector3.UnitX, frame.Forward);
        AssertClose(Vector3.UnitZ, frame.Right);                              // +X を向いた人の右は +Z
        AssertClose(Vector3.Cross(frame.Forward, Vector3.UnitY), frame.Right); // 右 = 前 × 上
        AssertClose(frame.Forward, frame.Rotate(Vector3.UnitZ));
        AssertClose(frame.Forward, Vector3.Transform(Vector3.UnitZ, frame.Rotation));
        var p = new Vector3(-0.3f, 0.5f, 0.9f);
        AssertClose(p, frame.WorldToLocal(frame.LocalToWorld(p)));
    }

    [TestMethod]
    public void DeltaAngle_TakesShortestPath()
    {
        Assert.AreEqual(0.2f, BodyFrame.DeltaAngle(MathF.PI - 0.1f, -MathF.PI + 0.1f), Eps);
        Assert.AreEqual(-0.2f, BodyFrame.DeltaAngle(-MathF.PI + 0.1f, MathF.PI - 0.1f), Eps);
    }

    [TestMethod]
    public void HandTarget_PunchStraightAhead_MapsToPuppetForward_WhateverThePlayerFaces()
    {
        foreach (var yaw in new[] { 0f, 1.1f, -2.5f })
        {
            var shoulders = new BodyFrame(new Vector3(0.4f, 1.4f, -0.2f), yaw);
            var local = PuppetMapping.HandTargetLocal(shoulders.Origin + (shoulders.Forward * 0.6f), shoulders, 0.5f);
            AssertClose(new Vector3(0f, 0f, 0.3f), local);
        }
    }

    [TestMethod]
    public void HandTargetGain_DoublesTheMotionAroundTheGuard_NotTheReachFromTheShoulder()
    {
        var shoulders = new BodyFrame(new Vector3(0.4f, 1.4f, -0.2f), 0.7f);
        const float height = 1.7f;
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var guardWorld = shoulders.LocalToWorld(PuppetMapping.GuardLocal(hand, height));
            var atGuard = PuppetMapping.HandTargetLocal(guardWorld, shoulders, 0.5f, hand, height, gain: 2f);
            AssertClose(PuppetMapping.GuardLocal(hand, height) * 0.5f, atGuard); // 構えたままなら増幅しない

            var jab = guardWorld + (shoulders.Forward * 0.2f);
            var amplified = PuppetMapping.HandTargetLocal(jab, shoulders, 0.5f, hand, height, gain: 2f);
            AssertClose(new Vector3(0f, 0f, 0.4f * 0.5f), amplified - atGuard); // 20cm 突けば 40cm 分届く
        }
    }

    [TestMethod]
    public void HeightCalibration_AndScale_AreSane()
    {
        Assert.AreEqual(1.0f, PuppetMapping.PlayerHeightFromEye(0.2f));
        Assert.AreEqual(1.7f, PuppetMapping.PlayerHeightFromEye(1.581f), 1e-3f);
        Assert.AreEqual(0.2f / (1.7f * 0.36f), PuppetMapping.Scale(0.2f, 1.7f), Eps);
    }

    [TestMethod]
    public void Stick_HasDeadZone_AndIsViewRelative()
    {
        Assert.AreEqual(Vector3.Zero, PuppetMapping.StickToWorld(new Vector2(0.1f, 0f), 0f));
        AssertClose(Vector3.UnitX, PuppetMapping.StickToWorld(new Vector2(0f, 1f), MathF.PI * 0.5f));
        // スティック右は体の右（+Z を向いていれば −X）。
        AssertClose(-Vector3.UnitX, PuppetMapping.StickToWorld(new Vector2(1f, 0f), 0f));
    }

    [TestMethod]
    public void Hover_CancelsGravityAtRest_NeverPullsDown_AndKeepsGravityWhenWeak()
    {
        Assert.AreEqual(9.81f, FighterMath.HoverAccel(9.81f, 0f, 0f, 100f, 14f, 30f, 1f), Eps);
        Assert.AreEqual(0f, FighterMath.HoverAccel(9.81f, -1f, 5f, 100f, 14f, 30f, 1f));
        Assert.AreEqual(30f, FighterMath.HoverAccel(9.81f, 1f, -5f, 100f, 14f, 30f, 1f));
        Assert.AreEqual(9.81f, FighterMath.HoverAccel(9.81f, 0.1f, 0.5f, 100f, 14f, 30f, 0f), Eps);
    }

    [TestMethod]
    public void RotationError_IsAxisTimesAngle_ShortestPath()
    {
        var target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        AssertClose(new Vector3(0f, 0.5f, 0f), FighterMath.RotationError(Quaternion.Identity, target));
        var nearlyFull = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 2f * MathF.PI - 0.1f);
        AssertClose(new Vector3(0f, -0.1f, 0f), FighterMath.RotationError(Quaternion.Identity, nearlyFull));
    }

    [TestMethod]
    public void Gait_NeutralAtZeroBlend_KneeLiftsOnForwardSwingOnly()
    {
        var still = FighterMath.Gait(1.3f, 0f, -0.1f, 0.2f, 0.5f, 0.7f);
        Assert.AreEqual(new LegPose(-0.1f, -0.1f, 0.2f, 0.2f), still);

        var mid = FighterMath.Gait(MathF.PI, 1f, 0f, 0f, 0.5f, 0.7f);
        var next = FighterMath.Gait(MathF.PI + 0.01f, 1f, 0f, 0f, 0.5f, 0.7f);
        Assert.IsLessThan(mid.LeftHip, next.LeftHip); // 左股は −X（前）へ動いている最中
        Assert.AreEqual(0.7f, mid.LeftKnee, Eps);
        Assert.AreEqual(0f, mid.RightKnee, Eps);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
        => Assert.IsLessThan(Eps, Vector3.Distance(expected, actual), $"expected {expected} actual {actual}");
}
