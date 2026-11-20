using System.Numerics;
using MiniRing.Bots;
namespace MiniRing.Tests;
[TestClass]
public sealed class KickTests
{
    [TestMethod]
    public void FivePathsReachTheTargetAndRetract()
    {
        var target = new Vector3(0.02817f, -0.338f, 0.6761f);
        var rest = new Vector3(-0.1127f, -0.9859f, -0.08451f);
        foreach (var kind in Enum.GetValues<KickTechnique>())
        {
            var spec = KickSpec.Of(kind);
            var start = spec.Evaluate(Hand.Right, 0f, spec.Duration, rest, target, 0.6197f);
            var strike = spec.Evaluate(Hand.Right, spec.Duration * KickSpec.ContactShare, spec.Duration, rest, target, 0.6197f);
            var end = spec.Evaluate(Hand.Right, spec.Duration, spec.Duration, rest, target, 0.6197f);
            // 足首の経路は構えへ戻る（同じ点）。膝の制御の蹴りは、人の動きの始まりが構えの膝と少し違う（腿が数°傾く）ので 23 cm 以内。
            Assert.IsLessThan(start.IsKneeControl ? 0.2254f : 1e-6f, Vector3.Distance(start.TargetLocal, end.TargetLocal));
            Assert.IsGreaterThan(0.1408f, Vector3.Distance(start.TargetLocal, strike.TargetLocal));
            Assert.IsTrue(float.IsFinite(strike.TargetLocal.LengthSquared()));
            Assert.IsTrue(strike.KneeTargetLocal is not null || strike.IsKneeControl, "an ankle path points the knee; a knee-controlled kick commands the knee itself");
        }
    }
}
