using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調べもの: 握った剣が胸を斬る振り（<c>SwordTests.AHeldSword_CutsThePartItIsAimedAt</c>）を、接触の掃き直し 1・2・3 回で出して、斬ったセルを比べる。</summary>
[TestClass]
public sealed class HeldSwordPassesProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void KesaThrough_ByPasses()
    {
        var basic = SwordTechnique.Basics.First(t => t.Name == "kesa");
        var through = basic with { Swing = basic.Swing with { StrikeFist = basic.Swing.StrikeFist + new Vector3(0.1f, -0.2f, 0.35f), StrikeTime = 0.1f } };
        foreach (var passes in new[] { 1, 2, 3 })
        {
            BoxingGame.DefaultMaxContactPasses = passes;
            try
            {
                var r = SwordDrill.Run(through);
                TestContext.WriteLine($"passes {passes}: target cells {r.TargetCells}, own cells {r.SelfCells}, other {r.OtherCells}, tip speed {r.TipSpeed:0.0}, miss {r.Miss:0.000}");
            }
            finally
            {
                BoxingGame.DefaultMaxContactPasses = 3;
            }
        }
    }
}
