using MiniRing.Bots;
using MiniRing.Highlights;

namespace MiniRing.Tests;

/// <summary>
/// 試合の面白さの床（docs/EXCITEMENT.md §4）。決めた組と種で、総合点・打ち合い・よけ・ダウン・緩急が今の値より下がらない。
/// 床は 2026-11-19 の master（PhysicsVersion 59・LogicVersion 57）で測った値の少し下。今の master は前の世代
/// （42a0b8f 以前）より退屈だと考えられているので（77be881 で打撃が 1/3、2ce39a8 以降は打たれずに倒れる）、
/// 「もっと面白くあるべき」目標は警告（<see cref="Soft.Check"/>）にとどめる。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class HighlightRegressionTests
{
    // excitementleague の既定（3 組 × 種 1・2）と同じ 6 試合: 世代の表の「全体」の行と同じ値になる。
    private static readonly (string Red, string Blue, int Seed)[] Cards = [("champion", "swarmer", 1), ("champion", "swarmer", 2), ("slugger", "outboxer", 1), ("slugger", "outboxer", 2), ("counter", "jabber", 1), ("counter", "jabber", 2)];

    private static ExcitementSummary? _cached;

    private static ExcitementSummary Measure()
    {
        if (_cached is null)
        {
            var all = Cards.Select(c => ExcitementRun.Bout(Contender.Named(c.Red), Contender.Named(c.Blue), c.Seed, 67.14f).Summarize()).ToList();
            foreach (var s in all)
            {
                Console.WriteLine(s.Line());
            }

            _cached = ExcitementSummary.Mean(all);
            Console.WriteLine($"まとめ: {_cached.Line()}");
        }

        return _cached;
    }

    private static int Evasions(ExcitementSummary s) => s.Count(HighlightKind.NearMiss) + s.Count(HighlightKind.Graze);

    [TestMethod]
    public void Excitement_StaysAboveTodaysFloor()
    {
        var s = Measure();
        Assert.IsGreaterThanOrEqualTo(FloorScore, s.Score, s.Line());
        Assert.IsGreaterThanOrEqualTo(FloorExchanges, s.Count(HighlightKind.Exchange), "打ち合い");
        Assert.IsGreaterThanOrEqualTo(FloorEvasions, Evasions(s), "よけ（紙一重 + 皮一枚）");
        Assert.IsGreaterThanOrEqualTo(FloorKnockdowns, s.Count(HighlightKind.Knockdown), "ダウン");
        // ドラマの比較は excitementpanel / excitementcompare で行う。固定 6 試合は個々の逆転に敏感。
        Assert.IsGreaterThanOrEqualTo(FloorPacing, s.Pacing, "緩急");
    }

    [TestMethod]
    public void Excitement_ReachesTheLivelierTarget()
    {
        // 目標（警告）: 前の世代（42a0b8f）の値。届いていなければ ⚠ WARNING（失敗ではない）。
        var s = Measure();
        Soft.Check(s.Score >= TargetScore, $"総合 {s.Score:0.0} < 目標 {TargetScore}（前の世代の値）");
        Soft.Check(s.Pacing >= TargetPacing, $"緩急 {s.Pacing:0.0} < 目標 {TargetPacing}");
        Soft.Check(Evasions(s) >= TargetEvasions, $"よけ {Evasions(s)} < 目標 {TargetEvasions}");
    }

    // 床（今の master、6 試合の合計・平均の少し下）と目標（42a0b8f の値）。docs/arena/excitement_generations_2026-11-19.md。
    private const float FloorScore = 115f; // 今 130.7
    private const int FloorExchanges = 19; // 今 21（PhysicsVersion 85。この 6 試合で 29 → 21 だが、標準 3 組 × 種 1〜16 の 48 試合では 197 → 204 で減っていない: 決めた 6 試合の揺れ）
    private const int FloorEvasions = 27; // 今 32
    private const int FloorKnockdowns = 13; // 今 17
    private const float FloorPacing = 22f; // 今 26.1
    private const float TargetScore = 209f; // 42a0b8f
    private const float TargetPacing = 34f; // 42a0b8f
    private const int TargetEvasions = 38; // 42a0b8f（紙一重 31 + 皮一枚 7）
}
