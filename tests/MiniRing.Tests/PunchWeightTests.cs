using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 腰の回旋で拳に体重が乗る（docs/BOXING.md §1-26、PhysicsVersion 84）: 標準の腰から打つ体では、腰（骨盤）の回旋が関節を通って腹・胸・肩・拳へ伝わる。
/// 腰の回す力を弱めると、同じ右ストレートでも拳が遅く・軽くなる。溜めた本は溜めない本より重い。値は固定せず、順序だけを見る（同じビルドの中の比べ）。
/// 測った値（種 1）: 溜め 0.06 s で 234 J（腰が弱い 177 J）、溜め 0.25 s で 301 J（腰が弱い 95 J）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class PunchWeightTests
{
    [TestMethod]
    public void TheHipsRotation_PutsWeightIntoTheCross_AndLoadingAddsMore()
    {
        var standard = FighterTuning.Default;
        var weakHips = standard with { PelvisRotatorTorque = 60f };
        var quick = Cross(standard, 0.06f);
        var loaded = Cross(standard, 0.25f);
        var quickWeak = Cross(weakHips, 0.06f);
        var loadedWeak = Cross(weakHips, 0.25f);
        Console.WriteLine($"cross energy: quick {quick:0} J (weak hips {quickWeak:0}), loaded {loaded:0} J (weak hips {loadedWeak:0})");

        Assert.IsGreaterThan(1.15f * quickWeak, quick, "strong hips put more weight into the quick cross");
        Assert.IsGreaterThan(1.15f * loadedWeak, loaded, "strong hips put more weight into the loaded cross");
        Assert.IsGreaterThan(quick, loaded, "loading the hips first makes the cross heavier");
    }

    /// <summary>右ストレート 1 本（守らない相手・届く間合い・種 1）の、最初に当たった拳のエネルギー [J]。当たらなければ 0。</summary>
    private static float Cross(FighterTuning tuning, float telegraph)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), 1, telegraph, tuning);
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
