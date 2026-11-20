using MiniRing.Bots;
using MiniRing.Highlights;
using Aigis.Physics.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 試合の面白さの観測（MiniRing.Highlights、docs/EXCITEMENT.md）の速いテスト: 観測は試合を変えない・何もしない試合は何も出ない・
/// 骨折は体の破壊として出る・面白さのモデル（ドラマ・緩急）が直感どおりに並ぶ。
/// </summary>
[TestClass]
public sealed class HighlightTests
{
    private const float Dt = 1f / 60f;

    private static BoxingGame Idle(int seed = 1) => new(seed: seed, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));

    [TestMethod]
    [TestCategory("slow")] // 試合を 2 回、約 15 秒
    public void Observing_DoesNotChangeTheMatch_ChecksumsAreBitIdentical()
    {
        // 観測は読むだけ（§1.6）: 観測しながら回した試合と、観測しない試合のチェックサムが毎歩一致する。
        static List<ulong> Run(bool observe)
        {
            using var game = new BoxingGame(seed: 5, corners: new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s)));
            var h = observe ? new MatchHighlights() : null;
            var sums = new List<ulong>();
            for (var i = 0; i < 604; i++)
            {
                game.Step(default, Dt);
                h?.Observe(game, (i + 1) * Dt, Dt);
                _ = h?.Summarize();
                sums.Add(game.StateChecksum());
            }

            return sums;
        }

        CollectionAssert.AreEqual(Run(false), Run(true));
    }

    [TestMethod]
    public void AnIdleMatch_EmitsNothing_AndScoresZero()
    {
        using var game = Idle();
        var h = new MatchHighlights(matchSeconds: 6.714f);
        for (var i = 0; i < 403; i++)
        {
            game.Step(default, Dt);
            h.Observe(game, (i + 1) * Dt, Dt);
        }

        h.Finish();
        Assert.IsEmpty(h.Events, string.Join("; ", h.Events));
        var s = h.Summarize();
        Assert.AreEqual(0f, s.Spectacle, 1e-4f);
        Assert.AreEqual(0f, s.Pacing, 1e-4f);
        Assert.IsLessThan(1f, s.Score, "何も起きない試合はほぼ 0 点");
    }

    [TestMethod]
    public void AScriptedBoneBreak_EmitsBoneBreak_ForTheVictim()
    {
        using var game = Idle();
        var h = new MatchHighlights();
        for (var i = 0; i < 60; i++)
        {
            game.Step(default, Dt);
            h.Observe(game, (i + 1) * Dt, Dt);
        }

        var forearm = game.Cpu.ForearmId(Hand.Left);
        var slices = game.Cpu.Voxels!.PartOf(forearm)!.Body.Grid.Dimensions.X;
        var broken = game.Cpu.Voxels!.BreakBent(forearm, new VoxelBreak(0, slices / 2, true, 2f));
        Assert.IsNotNull(broken);
        game.BreakBone(game.Cpu, broken, 1f);
        h.Observe(game, 61 * Dt, Dt); // 折れた歩の記録（次の歩で消える）を読む
        var e = h.Events.Single(e => e.Kind == HighlightKind.BoneBreak);
        Assert.IsFalse(e.Red, "折られたのは青");
    }

    private static Highlight E(HighlightKind kind, float t, bool red = true, float intensity = 1f) => new(kind, t, red, intensity, "");

    private static List<(float Time, float P)> Flat(float seconds, float p = 0.5f) =>
        [.. Enumerable.Range(0, (int)(seconds / 0.25f) + 1).Select(i => (i * 0.25f, p))];

    [TestMethod]
    public void IdenticalExchangesEvery2s_ScoreLowerPacing_ThanTheSameCountWithVariety()
    {
        // 慣れ: 同じ打ち合いが 2 秒ごとに 20 回続くより、同じ数で種類と主役が入れ替わり、間に休みがある方が緩急が高い。
        var t = ExcitementTuning.Default;
        var same = Enumerable.Range(0, 20).Select(i => E(HighlightKind.Exchange, 1f + (2f * i), intensity: 3f)).ToList();
        HighlightKind[] kinds = [HighlightKind.Exchange, HighlightKind.NearMiss, HighlightKind.Counter, HighlightKind.Flurry, HighlightKind.HeadSnap];
        // 同じ 20 個を、4 個ずつの山（0.5 秒おき）にまとめ、山と山の間を空ける。
        var varied = Enumerable.Range(0, 20).Select(i => E(kinds[i % kinds.Length], 2f + (8f * (i / 4)) + (0.5f * (i % 4)), i % 2 == 0, 1.5f)).ToList();
        var a = Arousal.From(same, Flat(42f), 0f, 42f, t);
        var b = Arousal.From(varied, Flat(42f), 0f, 42f, t);
        Console.WriteLine($"same: score {a.Score:0.0} peaks {a.Peaks} mean {a.MeanPeak:0.0} bored {a.BoredShare:0.00}");
        Console.WriteLine($"varied: score {b.Score:0.0} peaks {b.Peaks} mean {b.MeanPeak:0.0} bored {b.BoredShare:0.00}");
        Assert.IsGreaterThan(a.Score, b.Score);
    }

    [TestMethod]
    public void ALateComeback_ScoresHigherDrama_ThanTheSameMatchWithoutTheSwing()
    {
        // 同じ流れ（赤が押して p が 0.8 へ）の試合で、終盤に青が逆転して p が 0.15 へ落ちる方がドラマが高い（驚き・最大の揺れ・入れ替わり）。
        var t = ExcitementTuning.Default;
        List<(float, float)> Curve(bool comeback) => [.. Enumerable.Range(0, 161).Select(i =>
        {
            var time = i * 0.25f;
            var p = 0.5f + (0.3f * MathF.Min(1f, time / 20f));
            if (comeback && time > 32f)
            {
                p = 0.8f - (0.65f * MathF.Min(1f, (time - 32f) / 3f));
            }

            return (time, p);
        })];
        var flat = Drama.From(Curve(false), 40f, t);
        var swing = Drama.From(Curve(true), 40f, t);
        Assert.IsGreaterThan(flat.Score, swing.Score);
        Assert.AreEqual(1, swing.LeadChanges);
        Assert.AreEqual(0, flat.LeadChanges);
        Assert.IsGreaterThan(0.6f, swing.LargestSwing);
    }

    [TestMethod]
    public void AnIdleEventStream_ScoresNearZero()
    {
        var s = ExcitementSummary.From([], 0f, 0f, 40f, Flat(40f), ExcitementTuning.Default);
        Assert.AreEqual(0f, s.Score, 1e-3f);
    }

    [TestMethod]
    public void WinModel_FavoursTheSideTheAudienceSeesAhead()
    {
        var m = WinModel.Default;
        Assert.AreEqual(0.5f, m.Predict(WinModel.Features(0, 0f, 0f, 0f, 0f, 0f, 0.5f)), 0.15f, "互角なら五分に近い");
        Assert.IsGreaterThan(0.6f, m.Predict(WinModel.Features(1, 0.5f, 1f, 0.1f, 0.2f, 0.5f, 0.5f)), "赤がラウンドを取り、青が倒れている");
        Assert.IsLessThan(0.4f, m.Predict(WinModel.Features(-1, -0.5f, -1f, -0.1f, -0.2f, -0.5f, 0.5f)));
    }
}
