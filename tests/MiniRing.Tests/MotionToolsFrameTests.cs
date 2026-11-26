using System.Numerics;
using MiniRing.Arena;
using MiniRing.Arena.MotionTools;
using MiniRing.Motion;

namespace MiniRing.Tests;

/// <summary>
/// 動きの道具の残り（docs/plans/MOTION_TOOLS_PLAN.md §4）: 人の幅（mocap）の式、並べ比べのそろえる時刻、練習の共通の枠の手順（練習で選び、
/// 使わなかった条件で比べ直す）、四足の後ろ脚と鎌の刃先の記録。
/// </summary>
[TestClass]
public sealed class MotionToolsFrameTests
{
    private static string KickDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "mocap", "kyokushin")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir is null ? throw new DirectoryNotFoundException("data/mocap/kyokushin") : Path.Combine(dir, "data", "mocap", "kyokushin");
        }
    }

    [TestMethod]
    public void HumanRangeIsMinMedianMaxAndClassifies()
    {
        var r = HumanRange.Of([3d, 1d, 2d, double.NaN, 4d]);
        Assert.AreEqual(1d, r.Min);
        Assert.AreEqual(2.5d, r.Median);
        Assert.AreEqual(4d, r.Max);
        Assert.AreEqual(4, r.Count);
        Assert.AreEqual("inside", r.Classify(2d));
        Assert.AreEqual("below", r.Classify(0.5d));
        Assert.AreEqual("above", r.Classify(5d));
        Assert.AreEqual("n/a", r.Classify(double.NaN));
    }

    [TestMethod]
    public void QuantileInterpolatesBetweenSortedValues()
    {
        double[] v = [10d, 0d, 20d];
        Assert.AreEqual(0d, MotionHuman.Quantile(v, 0d));
        Assert.AreEqual(10d, MotionHuman.Quantile(v, 0.5d));
        Assert.AreEqual(15d, MotionHuman.Quantile(v, 0.75d), 1e-9);
        Assert.AreEqual(20d, MotionHuman.Quantile(v, 1d));
        Assert.IsTrue(double.IsNaN(MotionHuman.Quantile([], 0.5d)));
    }

    private static MotionSample Sample(float t, string? hit) => new(t, new Vector3[MotionTrace.PointNames.Length], default, default,
        new bool[MotionTrace.ContactNames.Length], 0f, "", float.NaN, 1f, false, hit, 0, Vector3.UnitZ, default, 0f);

    [TestMethod]
    public void AlignTimeIsFirstHitInsideTheWindowElseThePeak()
    {
        var trace = new MotionTrace("t", "red", 1, 1f, 1f);
        trace.Frames.AddRange([Sample(0.1f, "Left:Head"), Sample(0.5f, null), Sample(0.8f, "Right:Body"), Sample(0.9f, "Right:Head")]);
        Assert.AreEqual(0.8f, MotionCommand.AlignTime(trace, 0.3f, 1f, 0.6f));
        Assert.AreEqual(0.6f, MotionCommand.AlignTime(trace, 0.3f, 0.7f, 0.6f));
    }

    [TestMethod]
    public void PracticeFrameSelectsOnTrainingAndRechecksOnHeldOut()
    {
        // 候補 = 足す値。練習の条件では 3 がいちばん良いが、held-out では 3 は基準 0 より悪い（練習に合わせ込んだだけ）→ 採らない。
        var frame = new PracticeFrame<int, int>
        {
            Tool = "test",
            Question = "q",
            Train = [1, 2, 3],
            HeldOut = [10, 11, 12, 13],
            Score = (c, k, heldOut) => heldOut ? -c + (0.01 * k) : c,
            Describe = c => c.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Log = false,
        };
        var outcome = frame.Run([1, 2, 3], 0, TextWriter.Null);
        Assert.AreEqual(3, outcome.Selected);
        Assert.IsFalse(outcome.Adopt);
        Assert.IsTrue(outcome.HeldOut.MeanDiff < 0d);

        // held-out でも一貫して良いなら採る。
        var adopt = new PracticeFrame<int, int>
        {
            Tool = "test",
            Question = "q",
            Train = frame.Train,
            HeldOut = frame.HeldOut,
            Score = (c, k, _) => c + (0.01 * k * k),
            Describe = frame.Describe,
            Log = false,
        }.Run([1, 2, 3], 0, TextWriter.Null);
        Assert.IsTrue(adopt.Adopt, adopt.HeldOut.ToString());
    }

    [TestMethod]
    public void MocapKickReadsAsATraceWithFeetOnTheFloorAndProximalFirstChain()
    {
        var file = Directory.GetFiles(KickDir, "kick_S03-*.csv").Order(StringComparer.Ordinal).First();
        var trace = MotionHuman.FromCapture(KickCapture.Load(file));
        Assert.IsTrue(trace.Frames.Count > 100);
        Assert.AreEqual(1f / 200f, trace.Dt, 1e-6f);
        // 立ち足（左）はどこかで床に着いている。蹴る足のつま先は振りの間に床から離れる。
        Assert.IsTrue(trace.Frames.Any(f => f.Contacts[0] || f.Contacts[1]), "standing foot never on the floor");
        Assert.IsTrue(trace.Frames.Any(f => !f.Contacts[3]), "kicking toe never leaves the floor");
        // 人の蹴り: 腰 → 股関節 → 膝 → 足首 → つま先の山はほぼ順のとおり（運動連鎖）。つま先は 10 m/s を超える。
        var n = MotionWords.Natural(trace, trace.Frames[0].Time, trace.Frames[^1].Time, "toeR");
        Assert.IsTrue(n.ChainOrder >= 0.75f, n.Format());
        Assert.IsTrue(n.PeakSpeed > 10f, n.Format());
    }

    [TestMethod]
    [TestCategory("slow")]
    public void GalvaTraceRecordsRearLegsAndTheClawTipOutrunsTheHand()
    {
        MotionTrace? trace = null;
        ClawLab.Run("jin", 13, 0.94f, 0, Hand.Right, 0.31f, 1f, afterStep: (game, t) =>
        {
            trace ??= new MotionTrace("claw", "red", 13, game.Player.LegLength, game.Player.ArmReach);
            trace.Record(game, game.Player, t);
        });
        Assert.IsNotNull(trace);
        Assert.IsTrue(trace.Quadruped);
        var rearToe = trace.Series("rtoeL");
        var frontToe = trace.Series("toeL");
        Assert.IsTrue(rearToe.Zip(frontToe).Min(p => Vector3.Distance(p.First, p.Second)) > 0.1f, "rear toe is a copy of the front toe");
        // 鎌の刃先は手（鎌の付け根）より先にあり、振りでは手より速く動く（てこの先端）。
        var tip = MotionMetrics.Speeds(trace.Series("tip"), trace.Dt).Max();
        var hand = MotionMetrics.Speeds(trace.Series("handR"), trace.Dt).Max();
        Assert.IsTrue(tip > 1.5f * hand, $"tip {tip} hand {hand}");
    }
}
