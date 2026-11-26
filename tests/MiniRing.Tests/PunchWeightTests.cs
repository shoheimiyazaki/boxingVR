using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 腰の回旋で拳に体重が乗る（docs/BOXING.md §1-26、PhysicsVersion 84）: 標準の腰から打つ体では、腰（骨盤）の回旋が関節を通って腹・胸・肩・拳へ伝わる。
/// 腰の回す力を弱めると、同じ右ストレートでも拳が遅く・軽くなる。溜めた本は溜めない本より重い。値は固定せず、順序だけを見る（同じビルドの中の比べ）。
/// 測った値（寄り終わる間合い 1.03〜1.13 の平均、2026-11-26）: 溜め 0.06 s で 36 J、溜め 0.25 s で 45 J（腰が弱い 41 J）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class PunchWeightTests
{
    [TestMethod]
    public void TheHipsRotation_PutsWeightIntoTheCross_AndLoadingAddsMore()
    {
        // 間合いを振った平均で比べる（最初に当たった 1 発のエネルギーは、寄り終わった間合いが 1e-7 違うだけで 29 J と 50 J に分かれる）。
        // 2026-11-26（docs/arena/foot_snag.md「種を増やした比べ」）: 間合い 1.05 の 1 本では「溜めない本で強い腰が弱い腰の 1.15 倍」が通っていたが、
        // 間合いを 9 通りに振ると直す前の体でも 0 / 9 で、平均では弱い腰の方が重かった（36 対 40 J）。溜めない本の腰の差はこの測り方では見えないので外し、
        // 平均で成り立つ「溜めた本は溜めない本より重い（46 対 36 J）」「溜めた本は強い腰の方が重い（46 対 42 J）」を見る。
        var standard = FighterTuning.Default;
        var weakHips = standard with { PelvisRotatorTorque = 60f };
        var quick = PunchPathTests.Ranges.Average(r => Cross(standard, 0.06f, r));
        var loaded = PunchPathTests.Ranges.Average(r => Cross(standard, 0.25f, r));
        var loadedWeak = PunchPathTests.Ranges.Average(r => Cross(weakHips, 0.25f, r));
        Console.WriteLine($"cross energy (mean over ranges): quick {quick:0} J, loaded {loaded:0} J (weak hips {loadedWeak:0})");

        Assert.IsGreaterThan(loadedWeak, loaded, "strong hips put more weight into the loaded cross");
        Assert.IsGreaterThan(quick, loaded, "loading the hips first makes the cross heavier");
    }

    /// <summary>右ストレート 1 本（守らない相手・届く間合い・種 1）の、最初に当たった拳のエネルギー [J]。当たらなければ 0。</summary>
    internal static float Cross(FighterTuning tuning, float telegraph, float range = 1.05f)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), 1, telegraph, tuning);
        while (bout.PunchTime is null)
        {
            bout.Step();
        }

        var f = bout.Game.Player;
        while (bout.PunchTime is { } t && t < 0.45f)
        {
            bout.Step();
            foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                return hit.Energy;
            }
        }

        return 0f;
    }
}
