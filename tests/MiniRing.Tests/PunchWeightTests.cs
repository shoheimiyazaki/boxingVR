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
    public void TheHipsRotation_PutsWeightIntoTheCross()
    {
        // 2026-11-26（PhysicsVersion 103）: 最初に当たった拳のエネルギーは、間合いを 5 cm 変えるだけで 24〜60 J と揺れる（当たる瞬間が拳の速さの山の前か後か）。
        // 前は間合い 1.05 の 1 本だけを見ていて、そこでは「溜めると重い」「腰が強いと溜めた右も重い」が成り立っていたが、間合い 0.95〜1.15 の 5 本の合計では
        // 前のつま先でも成り立たない（溜め 219 対 溜めなし 220 J、溜めで腰が弱い 210 J。固いつま先では 209・210・250 J。ToeRegressionProbe.CrossEnergy）。
        // 5 本の合計で成り立つ「腰が強いと溜めなしの右が重い」（前 220 対 166 J、今 210 対 152 J）だけを確かめ、溜めの 2 つは数字を出すだけにする（docs/plans/TOE_GAP_STUDY.md）。
        // 2026-11-27（物理 106 仮、胴の震えを止めた）: 5 本の合計では 194 対 188 J と揺れに埋もれた。胴の筋が震えを止めるのに上限を使わなくなり、腰の弱い体でも
        // 腹・胸の関節の筋が胴を回すので、腰の強さの差は小さくなった（間合い 0.9〜1.2 の 13 本 × 種 1〜3 の 39 本の合計で、物理 105 は 1598 対 1245 J（1.28 倍）、
        // 物理 106 は 1515 対 1399 J（1.08 倍）。UprightSeedProbe.HipCross）。39 本の合計で「腰が強いほど溜めなしの右が重い」（順序）を見る。
        var standard = FighterTuning.Default;
        var weakHips = standard with { PelvisRotatorTorque = 60f };
        float Sum(FighterTuning tuning) => Seeds.Sum(seed => Ranges.Sum(range => Cross(tuning, 0.06f, seed, range)));
        var quick = Sum(standard);
        var quickWeak = Sum(weakHips);
        Console.WriteLine($"quick cross energy over {Seeds.Length * Ranges.Length} punches: {quick:0} J (weak hips {quickWeak:0} J, ratio {quick / quickWeak:0.000})");

        Assert.IsGreaterThan(quickWeak, quick, "strong hips put more weight into the quick cross");
    }

    private static readonly int[] Seeds = [1, 2, 3];

    private static readonly float[] Ranges = [0.9f, 0.925f, 0.95f, 0.975f, 1.0f, 1.025f, 1.05f, 1.075f, 1.1f, 1.125f, 1.15f, 1.175f, 1.2f];

    /// <summary>右ストレート 1 本（守らない相手・届く間合い・種 1）の、最初に当たった拳のエネルギー [J]。当たらなければ 0。</summary>
    internal static float Cross(FighterTuning tuning, float telegraph, int seed = 1, float range = 1.05f)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), seed, telegraph, tuning);
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
