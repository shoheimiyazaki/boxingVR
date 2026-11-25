using System.Numerics;
using MiniRing.Arena;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("slow")]
public sealed class ClawTrajectoryTests
{
    [TestMethod]
    public void Galva_ClawChordIsTwiceTheOriginalLength()
    {
        var original = new Vector2(0.46f, 0.226f).Length();
        Assert.AreEqual(2f * original, ClawSegment.Length, 1e-5f);
    }
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

    /// <summary>
    /// 長い鎌の 1 振りは、首・腕・胴のどの狙いでも相手に食い込む（セルを削る）。2026-11-25: 物理 97（刃は斬り抜けた奥行きの分だけ断つ、
    /// docs/BLADE_CUT_STUDY.md）とのマージから、1 振りで頭・腕・胴が離れることは無くなった。台本の振りの切っ先は 10〜23 m/s 出るが、
    /// はめた鎌の向きが構えで約 33° 目標から外れ（ClawAimProbe）、刃筋が通らない。離れる所まで期待するのは、体の側（手首と鎌の向き）を直してから。
    /// </summary>
    [TestMethod]
    public void LongClaw_OneStrokeCutsIntoEveryAim()
    {
        var trials = Enumerable.Range(0, 3).Select(aim =>
            ClawLab.Run("albert", 119, 1.24f, aim, aim == 1 ? Hand.Left : Hand.Right, 0.42f, 0.75f, moving: true)).ToArray();
        Assert.IsTrue(trials.All(t => t.Standing));
        foreach (var t in trials)
        {
            Console.WriteLine($"aim {t.Aim}: removed {t.Removed} (arm {t.Arm}, neck {t.Neck}, torso {t.Torso}) tip {t.TipSpeed:0.00} m/s plane {t.PlaneError * 100f:0.0} cm separated {t.HeadSeparated}/{t.ArmSeparated}/{t.TorsoSeparated}");
        }

        Assert.IsTrue(trials.All(t => t.Removed > 0), "every aimed stroke carves cells");
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
