using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調べもの: 蹴りの手本の間合いをずらすと、狙いの帯に当たる数がどう変わるか。</summary>
[TestClass]
public sealed class KickScanProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Scan()
    {
        foreach (var (name, hi, rear) in new[] { ("round low rear", 0, true), ("front kick rear", 3, true), ("round low lead", 0, false), ("round mid lead", 1, false), ("round mid rear", 1, true), ("round high rear", 2, true), ("front kick lead", 3, false) })
        {
            var h = KickShin.Heights[hi];
            var tune = KickSpec.Of(h.Technique).TuneFor(rear ? Hand.Right : Hand.Left)!;
            foreach (var d in new[] { 0f, 0.15f, 0.25f, 0.35f, 0.45f, 0.6f })
            {
                var t = tune with { Standoff = tune.Standoff + d };
                var outcomes = KickShin.TuneConditions.Concat(KickShin.ConfirmConditions).Select(c => KickShin.Kick(h, rear, t, c)).ToArray();
                TestContext.WriteLine($"{name} standoff {t.Standoff:0.000} (d {d:0.000}): landed {outcomes.Count(o => o.Landed)} onTarget {outcomes.Count(o => KickShin.InBand(h, o))} fell {outcomes.Count(o => o.Fell)}");
            }
        }
    }
}
