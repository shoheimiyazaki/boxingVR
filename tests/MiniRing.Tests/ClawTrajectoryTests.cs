using System.Numerics;
using MiniRing.Arena;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("slow")]
public sealed class ClawTrajectoryTests
{
    [TestMethod]
    [DataRow(Hand.Left)]
    [DataRow(Hand.Right)]
    public void LineTarget_RoundTripsBothBladeEndpoints(Hand hand)
    {
        var root = new Vector3(0.1f, 0.24f, 0.4f);
        var tip = root + Vector3.Normalize(new Vector3(-1f, 0.6f, 2f)) * ClawSegment.Length;
        var cmd = new ClawSegment(root, tip, Vector3.UnitY).ToCommand(hand);
        var reconstructed = ClawSegment.FromWrist(hand, cmd.TargetLocal, cmd.Orientation!.Value);
        Assert.IsLessThan(0.0001f, Vector3.Distance(root, reconstructed.Root));
        Assert.IsLessThan(0.0001f, Vector3.Distance(tip, reconstructed.Tip));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ScriptedBladeTargets_CarveCellsThroughVoxelContact(bool sweep)
    {
        var trial = ClawLab.Run("ramon", 77, 0.84f, 2, Hand.Right, 0.31f, 0.75f, moving: true, sweep: sweep);
        Assert.IsTrue(trial.Standing);
        Assert.IsGreaterThan(0, trial.Removed);
    }

    [TestMethod]
    public void RunUp_EntersTheSameCuttingPlaneAtSpeed()
    {
        var stroke = ClawStroke.Create(Hand.Right, 0.75f, new Vector3(0f, 0.1f, 0.85f), true);
        var start = stroke.Sample(stroke.CutStart);
        var end = stroke.Sample(stroke.CutEnd);
        Assert.IsGreaterThan(0.25f, Vector3.Distance(start.Tip, end.Tip));
        const float dt = 0.002f;
        var before = stroke.Sample(stroke.CutStart - dt);
        var after = stroke.Sample(stroke.CutStart + dt);
        Assert.IsGreaterThan(1f, Vector3.Distance(before.Tip, start.Tip) / dt);
        Assert.IsLessThan(0.001f, Vector3.Distance(start.Tip - before.Tip, after.Tip - start.Tip));
        for (var i = 0; i <= 20; i++)
        {
            var sample = stroke.Sample(stroke.CutStart + (stroke.CutEnd - stroke.CutStart) * i / 20f);
            Assert.IsLessThan(0.001f, MathF.Abs(Vector3.Dot(sample.Root - start.Root, stroke.Normal)));
            Assert.IsLessThan(0.001f, MathF.Abs(Vector3.Dot(sample.Tip - start.Root, stroke.Normal)));
        }
    }
}
