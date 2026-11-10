using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>関節の位置: 子の部位の箱が関節から親の側へ大きく飛び出さない（前腕が肘から後ろへ、かかとが足首から後ろへ）。</summary>
[TestClass]
public sealed class JointLayoutTests
{
    [TestMethod]
    public void ChildBoxes_BarelyReachBackPastTheirJoints_AndTheReachIsKept()
    {
        using var world = new PhysicsWorld();
        var tuning = FighterTuning.Default;
        var f = Fighter.Spawn(world, tuning, "f", Vector3.Zero, 0f);
        var forearm = world.GetState(f.ForearmId(Hand.Right));
        var half = f.HalfExtents;

        // 前腕の箱の体側の端から肘まで（T ポーズ、腕は X 軸）。
        var elbowToInnerEnd = half.Forearm.X - (Vector3.Distance(forearm.Position, f.ShoulderPosition(Hand.Right)) - LengthOfUpperArm(f));
        Console.WriteLine($"forearm half {half.Forearm.X:0.000}, juts behind the elbow {elbowToInnerEnd:0.000} m, reach {f.ArmReach:0.000}");
        Assert.IsLessThan(0.25f * half.Forearm.X, elbowToInnerEnd, "the forearm hardly juts out behind the elbow (was 0.03 m, 60 % of its half-length)");
        Assert.AreEqual(0.188f, f.ArmReach, 0.004f, "the arm still reaches as far as before");

        // かかとは足首から後ろへ、足の長さの 1/4 ほどまで（人の足の比率）。
        foreach (var foot in new[] { Footwork.Lead, Footwork.Rear })
        {
            var heel = world.GetState(f.HeelId(foot));
            var shin = world.GetState(foot == Footwork.Lead ? f.Parts.RightShin : f.Parts.LeftShin);
            var behind = shin.Position.Z - (heel.Position.Z - half.Foot.Z);
            var footLength = 2f * half.Foot.Z / (1f - FootGeometry.ToeShare);
            Console.WriteLine($"foot {foot}: heel behind the ankle {behind:0.000} m of a {footLength:0.000} m foot");
            Assert.IsLessThan(0.3f * footLength, behind, "the heel does not stick out far behind the ankle");
            Assert.IsGreaterThan(0f, behind, "but there is a heel behind the ankle");
        }
    }

    private static float LengthOfUpperArm(Fighter f) => f.ArmReach - (2f * f.HalfExtents.Forearm.X - FighterTuning.Default.JointOverlap);
}
