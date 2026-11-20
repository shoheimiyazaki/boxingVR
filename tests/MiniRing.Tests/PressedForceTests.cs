using System.Numerics;
using Aigis.Physics.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 鈍い接触が梁を曲げる力（2026-11-20、<see cref="VoxelContact.PressedForce"/>）: 面が渡せるのは法線の押しと、摩擦の円錐（μ × 押し）の範囲の
/// 引きずり、それに切った分（細い縁の前のセルが降伏して進んだ分）の押しだけ。滑った向きをそのまま梁を曲げる力にしない。
/// </summary>
[TestClass]
public sealed class PressedForceTests
{
    private static readonly Vector3 Inward = -Vector3.UnitX;

    [TestMethod]
    public void AStraightPush_PassesWhole()
    {
        var f = VoxelContact.PressedForce(Inward, Inward, cutShare: 0f);
        Assert.AreEqual(1f, f.Length(), 1e-5f);
        Assert.AreEqual(1f, Vector3.Dot(f, Inward), 1e-5f);
    }

    [TestMethod]
    public void AGloveSlidingUpTheFace_DragsItOnlyAsFarAsFrictionHolds()
    {
        // 45° 上へ滑りながら押す: 押しは cos45、引きずりは μ × 押しまで。
        var motion = Vector3.Normalize(Inward + Vector3.UnitY);
        var f = VoxelContact.PressedForce(motion, Inward, cutShare: 0f);
        var press = Vector3.Dot(f, Inward);
        Assert.AreEqual(MathF.Sqrt(0.5f), press, 1e-5f, "the press is the share of the motion into the face");
        Assert.AreEqual(VoxelContact.BluntFriction * press, f.Y, 1e-5f, "the drag up the face is friction times the press");
    }

    [TestMethod]
    public void AFaceTheMotionLeaves_PassesNothing_UnlessItCuts()
    {
        var away = Vector3.Normalize(-Inward + Vector3.UnitY);
        Assert.AreEqual(0f, VoxelContact.PressedForce(away, Inward, cutShare: 0f).Length(), 1e-6f, "a face cannot pull");
        Assert.IsGreaterThan(0f, VoxelContact.PressedForce(away, Inward, cutShare: 1f).Y, "but an edge that cut on through drags along its cut");
    }

    [TestMethod]
    public void ABladeThatCutsThrough_PushesAlongItsCut_AsBefore()
    {
        var motion = Vector3.Normalize(Inward + (2f * Vector3.UnitY));
        var cut = VoxelContact.PressedForce(motion, Inward, cutShare: 1f);
        Assert.AreEqual(0f, Vector3.Distance(motion, cut), 1e-5f, "all the blow's energy went into cutting: the push runs along the motion");
        var half = VoxelContact.PressedForce(motion, Inward, cutShare: 0.5f);
        var none = VoxelContact.PressedForce(motion, Inward, cutShare: 0f);
        Assert.IsTrue(none.Y < half.Y && half.Y < cut.Y, "the drag grows with the share spent cutting, continuously");
    }
}
