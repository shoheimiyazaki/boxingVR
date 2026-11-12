using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調べもの: 斬り抜く袈裟を、振りの速さと抜ける先を少しずつ変えて出し、狙った胸を斬れたセルを並べる（1 本の試し斬りの揺らぎを見る）。</summary>
[TestClass]
public sealed class SwordAimProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void KesaVariants()
    {
        var basic = SwordTechnique.Basics.First(t => t.Name == "kesa");
        foreach (var time in new[] { 0.08f, 0.1f, 0.12f })
        {
            foreach (var depth in new[] { 0.3f, 0.35f, 0.4f })
            {
                var through = basic with { Swing = basic.Swing with { StrikeFist = basic.Swing.StrikeFist + new Vector3(0.1f, -0.2f, depth), StrikeTime = time } };
                var r = SwordDrill.Run(through);
                TestContext.WriteLine($"time {time} depth {depth}: target {r.TargetCells} self {r.SelfCells} first {r.FirstPart}");
            }
        }
    }
}
