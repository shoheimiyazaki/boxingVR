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
        foreach (var (name, hi, rear) in new[] { ("round low rear", 0, true), ("front kick rear", 3, true), ("round low lead", 0, false), ("round mid lead", 1, false) })
        {
            var h = KickShin.Heights[hi];
            var tune = KickSpec.Of(h.Technique).TuneFor(rear ? Hand.Right : Hand.Left)!;
            foreach (var d in new[] { -0.10f, -0.05f, -0.025f, 0f, 0.025f, 0.05f, 0.10f })
            {
                var t = tune with { Standoff = tune.Standoff + d };
                var outcomes = KickShin.TuneConditions.Concat(KickShin.ConfirmConditions).Select(c => KickShin.Kick(h, rear, t, c)).ToArray();
                var fractions = string.Join(' ', outcomes.Where(o => o.Landed).Select(o => o.Fraction.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
                TestContext.WriteLine($"{name} standoff {t.Standoff:0.000} (d {d:+0.000;-0.000;0}): landed {outcomes.Count(o => o.Landed)} onTarget {outcomes.Count(o => KickShin.InBand(h, o))} fell {outcomes.Count(o => o.Fell)} fractions {fractions}");
            }
        }
    }
}
