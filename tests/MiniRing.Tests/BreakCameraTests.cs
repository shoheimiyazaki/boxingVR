using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 休み（ラウンドの間）のカメラ割り（<see cref="TvDirector.PlanBreak"/>）と、休みの間にあざが引くこと（2026-11-18、ユーザーの指定）。
/// どちらも見た目だけで、試合の計算には響かない。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class BreakCameraTests
{
    // 動画の 1 歩（VideoMaker.MaxStepDt = 1/60 s、30 fps で 1 コマ 2 歩）。リプレイの長さはこの歩で決まる（1/80 s のままでは 2026-11-24 の遅いリプレイが実際より 3 秒長かった）。
    private static TvDirector Director() => new(1280, 720, 30, 1f / 60f, "赤", "青");

    private static TvHighlight Highlight(int step, float score) => new(step, RedVictim: step % 2 == 0, Point: Vector3.Zero, Score: score, Text: "場面", Round: 1);

    private static TvWound Wound(bool red, float score, bool healing = false) => new(red, Body: null!, Grid: default, Name: "右すね", Score: score) { Healing = healing };

    [TestMethod]
    [DataRow(16.78f)] // 休みの長さ [試合の s]（人形の単位の 10・20・30 s）
    [DataRow(33.57f)]
    [DataRow(50.35f)]
    public void PlanBreak_KeepsToTheBreakRules_ForEveryLengthHighlightAndWoundCount(float length)
    {
        var director = Director();
        for (var highlights = 0; highlights <= 4; highlights++)
        {
            for (var wounds = 0; wounds <= 3; wounds++)
            {
                for (var repeat = 0; repeat < 6; repeat++)
                {
                    var plan = director.PlanBreak(length,
                        [.. Enumerable.Range(0, highlights).Select(i => Highlight(1000 + (i * 2000), 20f + (10f * i)))],
                        redFirst: repeat % 2 == 0,
                        [.. Enumerable.Range(0, wounds).Select(i => Wound(i % 2 == 0, 40f + (30f * i), healing: repeat % 3 == 2 && i == 0))]);
                    var label = $"休み {length}s ハイライト {highlights} 傷 {wounds} 回 {repeat}";

                    // 切れ目なく並び、いつものカメラで挟む。
                    Assert.AreEqual(0f, plan[0].Start, 1e-4f, label);
                    Assert.AreEqual(TvShot.Main, plan[0].Shot, label);
                    Assert.AreEqual(TvShot.Main, plan[^1].Shot, label);
                    for (var i = 1; i < plan.Count; i++)
                    {
                        Assert.AreEqual(plan[i - 1].End, plan[i].Start, 1e-3f, label + " 切れ目");
                        Assert.IsTrue(plan[i].End > plan[i].Start, label + " 長さ");
                        // 同じ種類が続かない（リプレイを続けて流すのは別: 間はワイプ）。
                        Assert.IsTrue(plan[i].Shot != plan[i - 1].Shot || plan[i].Shot == TvShot.Replay, $"{label} 同じ種類が続いた: {plan[i].Shot}");
                    }

                    Assert.AreEqual(length + 5f, plan[^1].End, 1e-3f, label);

                    // リプレイは 1〜2 本（ハイライトが無ければ 0。休みが短くても 1 本は入る）、傷のアップは多くて 1 カット。
                    var replays = plan.Count(s => s.Shot == TvShot.Replay);
                    Assert.IsTrue(replays <= 2, label + $" リプレイ {replays}");
                    Assert.AreEqual(highlights == 0 ? 0 : 1, Math.Min(1, replays), label + $" リプレイ {replays}");
                    Assert.IsTrue(plan.Count(s => s.Shot == TvShot.Wound) <= 1, label + " 傷のアップ");
                    if (wounds == 0)
                    {
                        Assert.IsFalse(plan.Any(s => s.Shot == TvShot.Wound), label);
                    }

                    // 選手を映すカットが 6 割以上（休みの長さのうち）。
                    var onFighter = plan.Where(s => TvDirector.ShowsFighter(s.Shot)).Sum(s => MathF.Min(s.End, length) - MathF.Min(s.Start, length));
                    Assert.IsGreaterThanOrEqualTo(0.6f * length, onFighter, $"{label} 選手を映したのは {onFighter:0.0}s");
                }
            }
        }
    }

    [TestMethod]
    [DataRow(33.57f)]
    [DataRow(44.81f)]
    [DataRow(55.89f)]
    public void PlanBreak_HoldsEachShotLongEnough_NoRapidSwitching(float length)
    {
        // 2026-11-18、ユーザー: インターバル中に選手を正面から映すカメラが短い周期でパカパカ入れ替わる（2.0〜4.0 秒のカットだった）。
        // 選手を映すカット・差し込みは 3 秒以上（端も前のカットに足す）。
        var director = Director();
        for (var repeat = 0; repeat < 12; repeat++)
        {
            var plan = director.PlanBreak(length, [Highlight(1000, 30f), Highlight(3000, 40f)], redFirst: repeat % 2 == 0, [Wound(true, 80f, healing: repeat % 3 == 2)]);
            foreach (var s in plan.Where(s => s.Shot is not (TvShot.Main or TvShot.Replay or TvShot.Wound)))
            {
                Assert.IsGreaterThanOrEqualTo(3f, s.End - s.Start, $"休み {length}s 回 {repeat}: {s.Shot} が {s.End - s.Start:0.00}s");
            }
        }
    }

    [TestMethod]
    public void PlanBreak_UsesTheWideAndHandheldCameras_AndMixesWhichBoxerIsShown()
    {
        var director = Director();
        var shots = new HashSet<TvShot>();
        var reds = new HashSet<bool>();
        for (var i = 0; i < 6; i++)
        {
            // 休み 40 秒（リプレイを 0.25 倍に遅くした 2026-11-24 から、30 秒の休みはリプレイ 1 本と選手のカットで埋まり、差し込みの入る余りが無い）。
            foreach (var s in director.PlanBreak(40f, [Highlight(1000, 30f)], redFirst: true))
            {
                shots.Add(s.Shot);
                if (s.Shot == TvShot.Handheld)
                {
                    reds.Add(s.Red);
                }
            }
        }

        CollectionAssert.IsSubsetOf(new[] { TvShot.Handheld, TvShot.Wide, TvShot.Corner, TvShot.Ringside }, shots.ToList());
        Assert.AreEqual(2, reds.Count, "手持ちは 2 人を交互に映す");
    }

    [TestMethod]
    public void PlanBreak_ShowsAWoundCloseUpOnlySometimes_AndAHealingOneLate()
    {
        var director = Director();
        var withWound = 0;
        for (var i = 0; i < 12; i++)
        {
            var plan = director.PlanBreak(20f, [], redFirst: true, [Wound(false, 60f)]);
            withWound += plan.Count(s => s.Shot == TvShot.Wound && !s.Wound!.Healing);
        }

        Assert.IsGreaterThan(0, withWound, "重い傷はときどき映す");
        Assert.IsLessThan(12, withWound, "毎回は映さない");

        // 治りかけの傷は休みの後ろの方で映す（再生で戻っていくセルを見せる）。
        var healing = director.PlanBreak(30f, [], redFirst: true, [Wound(true, 80f, healing: true)]);
        var late = Enumerable.Range(0, 12).Select(_ => director.PlanBreak(30f, [], redFirst: true, [Wound(true, 80f, healing: true)]))
            .SelectMany(p => p).Where(s => s.Shot == TvShot.Wound).ToList();
        Assert.IsTrue(late.Count > 0, "治りかけの傷も映す");
        Assert.IsTrue(late.All(s => s.Start >= 15f && s.Wound!.Healing), "休みの後ろの半分で");
        Assert.IsNotNull(healing);
    }

    [TestMethod]
    public void SkinBruises_FadeOnlyDuringTheBreak()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var rules = new MatchRules(RoundSeconds: 1.678f, Rounds: 2, BreakSeconds: 6.714f) { BruiseHalfLifeSeconds = 3.357f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)), rules: rules);
        var body = game.Cpu.Voxels!;
        body.BruiseSkin(game.Cpu.ChestPosition + (game.Cpu.Frame.Forward * game.Cpu.HalfExtents.Chest.Z), 112.7f);
        var fresh = body.SkinBruiseTotal();
        Assert.IsGreaterThan(0f, fresh);

        const float Dt = 1f / 60f;
        var roundEnd = 0f;
        for (var t = 0f; t < 1.511f; t += Dt)
        {
            game.Step(default, Dt);
            roundEnd = body.SkinBruiseTotal();
        }

        Assert.IsFalse(game.InBreak, "まだラウンド中");
        Assert.AreEqual(fresh, roundEnd, 1e-3f, "ラウンド中は引かない");

        while (!game.InBreak)
        {
            game.Step(default, Dt);
        }

        var atBreak = body.SkinBruiseTotal();
        for (var t = 0f; t < 3.357f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var later = body.SkinBruiseTotal();
        Assert.IsTrue(game.InBreak || game.BreakLeft >= 0f);
        Assert.IsLessThan(atBreak * 0.6f, later, $"休み 3.4 秒（半減期 3.4 秒）で半分ほどになる: {atBreak:0.00} → {later:0.00}");
        Assert.IsGreaterThan(atBreak * 0.35f, later, "消えてしまわない");
    }
}
