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
            Assert.IsLessThan(1e-6f, Vector3.Distance(start.TargetLocal, end.TargetLocal));
            Assert.IsGreaterThan(0.05f, Vector3.Distance(start.TargetLocal, strike.TargetLocal));
            Assert.IsTrue(float.IsFinite(strike.TargetLocal.LengthSquared()));
            Assert.IsNotNull(strike.KneeTargetLocal);
        }
    }
}
