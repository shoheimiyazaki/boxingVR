namespace MiniRing.Tests;

/// <summary>
/// 移動の出だしの引っかかり（2026-11-26、docs/arena/foot_snag.md）: 意図操作で、直す前に足が出ずに止まった・進まなかった足と重心の状態から方向キーを入れても、
/// 体が止まらず進み、足が空回り（下ろしては同じ所へ踏み出す）しない。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class FootSnagTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// 直す前の値（FootSnagProbe、5 種の最悪）: ステップの後に前へ 0.37 秒止まった・後ろ足の前蹴りの後に左後ろへ進みが歩く速さの 38 %・ステップの後に後ろへ 47 %。
    /// 直した後は全組で止まりが 0.17 秒以下・進みが 51 % 以上。1.2 秒に踏む歩数は 4〜6 歩で、計画し直しの空回り（31 歩）が無い。
    /// </summary>
    [TestMethod]
    public void AfterSteppingKickingOrPunching_TheFirstMoveDoesNotStall()
    {
        var cases = new (FootSnagDrill.Setup Setup, string Direction)[]
        {
            (FootSnagDrill.Setup.Step, "F"), (FootSnagDrill.Setup.Step, "B"), (FootSnagDrill.Setup.KickRear, "BL"),
            (FootSnagDrill.Setup.KickRear, "B"), (FootSnagDrill.Setup.Reverse, "F"), (FootSnagDrill.Setup.Straight, "B"),
        };
        foreach (var (setup, name) in cases)
        {
            var direction = FootSnagDrill.Directions.First(d => d.Name == name).Direction;
            var r = FootSnagDrill.Run(setup, name, direction);
            TestContext.WriteLine(FootSnagDrill.Row(r));
            Assert.IsTrue(r.Standing, $"{setup} {name}: stays on its feet");
            Assert.IsLessThan(0.25f, r.LongestStall, $"{setup} {name}: the body stood still for {r.LongestStall:0.00} s");
            Assert.IsGreaterThan(0.5f, r.Progress, $"{setup} {name}: pelvis speed along the command / walking speed {r.Progress:0.00}");
            Assert.IsLessThanOrEqualTo(10, r.Steps, $"{setup} {name}: {r.Steps} steps in {FootSnagDrill.MeasureSeconds} s (stepping in place)");
        }
    }
}
