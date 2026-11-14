using System.Numerics;
using MiniRing.Motion;

namespace MiniRing.Tests;

/// <summary>
/// 剣士が出す目標のうち、刃の線が自分の体を通るもの（<see cref="SelfClearance"/>）を、判断の側で外へ傾ける。物理には「自分には当たらない」を足さない
/// （体は目標をバネで追うので、目標が体を通れば刃は自分を斬る。目標の側で避ける）。
/// </summary>
[TestClass]
public sealed class SelfClearanceTests
{
    private static MotionFrame Frame(Vector3 right, Vector3 blade, Vector3 head = default) =>
        new(right, right + new Vector3(0.08f, -0.05f, -0.1f), Vector3.Normalize(blade), SwordMotion.Perpendicular(-Vector3.UnitY, Vector3.Normalize(blade)), head, 0f, default);

    private static float Tilt(MotionFrame a, MotionFrame b) => MathF.Acos(Math.Clamp(Vector3.Dot(a.Blade, b.Blade), -1f, 1f));

    [TestMethod]
    public void ABladeThatKeepsClear_IsLeftAsItIs()
    {
        var clearance = SelfClearance.Adopted;
        var frame = Frame(new Vector3(-0.2f, -0.2f, 0.6f), new Vector3(0f, 0.15f, 1f));
        Assert.IsGreaterThan(clearance.Margin, clearance.Along(frame.Right, frame.Blade, 2f).Clearance, "the blade points away from the body");
        Assert.AreEqual(frame, clearance.Keep(frame, 2f));
    }

    [TestMethod]
    public void ABladeThroughTheTrunk_IsTiltedOutAboutTheGrip_AndNothingElseMoves()
    {
        var clearance = SelfClearance.Adopted;
        // 前に出した右手から、刃が胴の横を後ろへ（体の箱の中を）通る。
        var frame = Frame(new Vector3(-0.2f, -0.2f, 0.6f), new Vector3(0.1f, 0f, -1f));
        var before = clearance.Along(frame.Right, frame.Blade, 2f).Clearance;
        Assert.IsLessThan(0f, before, "the commanded blade runs through the body");

        var kept = clearance.Keep(frame, 2f);
        var after = clearance.Along(kept.Right, kept.Blade, 2f).Clearance;
        Assert.IsGreaterThan(before, after, "the tilt takes the blade away from the body");
        Assert.IsLessThanOrEqualTo(clearance.MaxTilt + 1e-4f, Tilt(frame, kept), "the path is bent by no more than the limit");
        Assert.AreEqual(frame.Right, kept.Right, "the hand is where the technique puts it");
        Assert.AreEqual(frame.Left, kept.Left);
        Assert.AreEqual(frame.Head, kept.Head);
        Assert.AreEqual(1f, kept.Blade.Length(), 1e-4f);
        Assert.AreEqual(0f, Vector3.Dot(kept.Blade, kept.Edge), 1e-4f, "the edge stays square to the blade");
    }

    [TestMethod]
    public void ADuckedHead_WidensTheBodyTheBladeMustAvoid()
    {
        var clearance = SelfClearance.Adopted;
        var upright = clearance.Along(new Vector3(0f, 0.8f, 0.5f), Vector3.Normalize(new Vector3(0f, -0.2f, -1f)), 2f);
        var leaning = clearance.Along(new Vector3(0f, 0.8f, 0.5f), Vector3.Normalize(new Vector3(0f, -0.2f, -1f)), 2f, new Vector3(0f, 0f, 0.4f));
        Assert.IsLessThanOrEqualTo(upright.Clearance, leaning.Clearance, "the body leans into the blade's line, so it cannot be farther from it");
    }

    [TestMethod]
    public void KeepingTheBladeClear_IsDeterministic_AndDoesNotChangeItsInput()
    {
        var clearance = SelfClearance.Adopted;
        var frame = Frame(new Vector3(-0.2f, -0.2f, 0.6f), new Vector3(0.1f, 0f, -1f));
        var copy = frame;
        var first = clearance.Keep(frame, 2f);
        var second = clearance.Keep(frame, 2f);
        Assert.AreEqual(first, second);
        Assert.AreEqual(copy, frame, "the technique's own frame is never rewritten");
    }
}
