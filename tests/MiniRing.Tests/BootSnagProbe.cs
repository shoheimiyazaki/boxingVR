namespace MiniRing.Tests;

/// <summary>調べもの: 1 体がリングの上を歩くときの足の引っかかり・体の停滞の表（docs/arena/boot_snag.md の元）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class BootSnagProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Table()
    {
        TestContext.WriteLine("| 道 | 足の引っかかり 回 | 合計 [s] | 停滞 回 | 合計 [s] | 最長 [s] | 歩いた時間 [s] | 足の歩数 | 骨盤の速さ / 指令 | 回りきった |");
        TestContext.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            foreach (var (name, waypoints) in BootSnagDrill.Paths(seed))
            {
                if (seed > 1 && !name.StartsWith("random", StringComparison.Ordinal))
                {
                    continue;
                }

                var r = BootSnagDrill.Walk(name, waypoints);
                TestContext.WriteLine($"| {r.Name} | {r.FootSnags} | {r.FootSnagSeconds:0.00} | {r.Stalls} | {r.StallSeconds:0.00} | {r.LongestStall:0.00} | {r.Seconds:0.0} | {r.FootSteps} | {r.SpeedRatio:0.00} | {(r.Finished ? "yes" : "NO")} |");
            }
        }
    }
}
