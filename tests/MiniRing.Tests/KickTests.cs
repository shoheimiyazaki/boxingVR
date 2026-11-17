using System.Numerics;
using MiniRing.Bots;
namespace MiniRing.Tests;
[TestClass]
public sealed class KickTests
{
    [TestMethod]
    public void FivePathsReachTheTargetAndRetract()
    {
        var target = new Vector3(0.01f, -0.12f, 0.24f);
        var rest = new Vector3(-0.04f, -0.35f, -0.03f);
        foreach (var kind in Enum.GetValues<KickTechnique>())
        {
            var spec = KickSpec.Of(kind);
            var start = spec.Evaluate(Hand.Right, 0f, spec.Duration, rest, target, 0.22f);
            var strike = spec.Evaluate(Hand.Right, spec.Duration * KickSpec.ContactShare, spec.Duration, rest, target, 0.22f);
            var end = spec.Evaluate(Hand.Right, spec.Duration, spec.Duration, rest, target, 0.22f);
            // 足首の経路は構えへ戻る（同じ点）。膝の制御の蹴りは、人の動きの始まりが構えの膝と少し違う（腿が数°傾く）ので 8 cm 以内。
            Assert.IsLessThan(start.IsKneeControl ? 0.08f : 1e-6f, Vector3.Distance(start.TargetLocal, end.TargetLocal));
            Assert.IsGreaterThan(0.05f, Vector3.Distance(start.TargetLocal, strike.TargetLocal));
            Assert.IsTrue(float.IsFinite(strike.TargetLocal.LengthSquared()));
            Assert.IsTrue(strike.KneeTargetLocal is not null || strike.IsKneeControl, "an ankle path points the knee; a knee-controlled kick commands the knee itself");
        }
    }
}
