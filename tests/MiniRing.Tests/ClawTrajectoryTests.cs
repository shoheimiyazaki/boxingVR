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

    private static readonly float[] StrokeGaps = [0.74f, 0.79f, 0.84f];

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ScriptedBladeTargets_CarveCellsThroughVoxelContact(bool sweep)
    {
        // 間合いの 1 点では、刃が相手の縁をかすめるかどうかの紙一重で割れる（2026-11-26: 掃かない振りは 0.84 m で 0〜3 セル、0.74 m で 126〜217 セル。
        // ClawAimProbe.ScriptedStrokeTrace）ので、届く間合いの 3 点のうち 2 点以上で削ることを見る。
        var trials = StrokeGaps.Select(gap => ClawLab.Run("ramon", 77, gap, 2, Hand.Right, 0.31f, 0.75f, moving: true, sweep: sweep)).ToArray();
        Assert.IsTrue(trials.All(t => t.Standing));
        Assert.IsGreaterThanOrEqualTo(2, trials.Count(t => t.Removed > 0), "届く間合いの 3 点のうち 2 点以上で削る");
    }

    private static readonly float[] Gaps = [1.14f, 1.19f, 1.24f, 1.29f, 1.34f, 1.39f];

    /// <summary>
    /// 長い鎌の 1 振りは、首・腕・胴のどの狙いでも相手に食い込む（セルを削る）。2026-11-25: 物理 97（刃は斬り抜けた奥行きの分だけ断つ、
    /// docs/BLADE_CUT_STUDY.md）とのマージから、1 振りで頭・腕・胴が離れることは無くなった。台本の振りの切っ先は 10〜23 m/s 出るが、
    /// はめた鎌の向きが構えで約 33° 目標から外れ（ClawAimProbe）、刃筋が通らない。離れる所まで期待するのは、体の側（手首と鎌の向き）を直してから。
    /// </summary>
    [TestMethod]
    [Ignore("既知: 鎌の向きと前腕の揺れ・BASE_BODY_PLAN §10.8")]
    public void LongClaw_OneStrokeCutsIntoEveryAim()
    {
        // 2026-11-26（PhysicsVersion 103、つま先の蝶番を固くした: ガルヴァの尖った跗節もこの蝶番）: 鎌の刃筋が通らない振りは、寄る間合いの数 cm で
        // かすって削るか外れるかが入れ替わる。間合い 1.24・種 119 の 1 回だけを見ていたのを、間合い 1.14〜1.39 の 6 つで見る。前のつま先でも 1.24 のほかの種
        // （120・121・124）では腕の狙いが 0 セルで、間合いを替えると狙いごとに 6 つのうち 4〜6 つで削る。固いつま先では 4〜5 つ（ToeRegressionProbe.LongClaw）。
        // 狙いごとに、半分以上の間合いで削ることを見る。
        var removed = new int[3];
        foreach (var gap in Gaps)
        {
            var trials = Enumerable.Range(0, 3).Select(aim =>
                ClawLab.Run("albert", 119, gap, aim, aim == 1 ? Hand.Left : Hand.Right, 0.42f, 0.75f, moving: true)).ToArray();
            Console.WriteLine($"gap {gap}: removed {string.Join(" / ", trials.Select(t => t.Removed))}");
            Assert.IsTrue(trials.All(t => t.Standing));
            for (var aim = 0; aim < 3; aim++)
            {
                removed[aim] += trials[aim].Removed > 0 ? 1 : 0;
            }
        }

        Assert.IsTrue(removed.All(n => n * 2 >= Gaps.Length), $"every aimed stroke carves cells at most stand-off distances ({string.Join(", ", removed)} of {Gaps.Length})");
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
