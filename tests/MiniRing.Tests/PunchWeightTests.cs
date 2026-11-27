using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 腰の回旋（docs/BOXING.md §1-26）: 今のテストは骨盤の回る速さだけを確かめる（腰の回りが拳に乗らない穴は docs/DEBT.md）。
/// <see cref="Cross"/> は調べのプローブ（ToeRegressionProbe など）が使う右ストレート 1 本のエネルギー。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class PunchWeightTests
{
    [TestMethod]
    public void StrongerHipRotators_SpinThePelvisFaster_OnlyThePelvisIsChecked()
    {
        // 確かめているのは「骨盤の回旋筋のトルクの上限を上げると、右ストレートの間の骨盤の最大の角速度が増える」だけ（2026-11-27、統合役の判断）。
        // 当たりの胴の角運動量・相手へ渡った運動量は腰のトルクに対して平らで、腰の回りは拳に乗っていない（master から。docs/DEBT.md「腰の回りが拳に乗らない（master から）」、
        // 測った数字は CrossRangeProbe.HipSweep と docs/plans/BASE_BODY_PLAN.md §9.3）。拳の重さの順序は、それを直すまで確かめない。
        var standard = FighterTuning.Default;
        var factors = new[] { 0.4f, 1.0f, 1.6f };
        var peaks = factors.Select(k => PelvisPeak(standard with { PelvisRotatorTorque = standard.PelvisRotatorTorque * k })).ToArray();
        Console.WriteLine("pelvis peak yaw rate [rad/s] at rotator x" + string.Join(", ", factors.Zip(peaks, (k, w) => $"{k:0.0}: {w:0.00}")));
        for (var i = 1; i < peaks.Length; i++)
        {
            Assert.IsGreaterThan(peaks[i - 1], peaks[i], $"pelvis spins faster with stronger rotators (x{factors[i - 1]:0.0} -> x{factors[i]:0.0})");
        }
    }

    /// <summary>あごの間合いの右ストレート（守らない相手・種 1）を溜め 3 通りで打ち、打ち出しから 0.45 s の骨盤の鉛直軸まわりの最大の角速度の平均 [rad/s]。</summary>
    private static float PelvisPeak(FighterTuning tuning)
    {
        var sum = 0f;
        var telegraphs = new[] { 0.03f, 0.09f, 0.15f };
        foreach (var telegraph in telegraphs)
        {
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "chin", 0f, false), 1, telegraph, tuning);
            while (bout.PunchTime is null)
            {
                bout.Step();
            }

            var pelvis = bout.Game.Player.Parts.Pelvis;
            var peak = 0f;
            while (bout.PunchTime is { } t && t < 0.45f)
            {
                bout.Step();
                peak = MathF.Max(peak, MathF.Abs(bout.Game.World.GetState(pelvis).AngularVelocity.Y));
            }

            sum += peak;
        }

        return sum / telegraphs.Length;
    }

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
