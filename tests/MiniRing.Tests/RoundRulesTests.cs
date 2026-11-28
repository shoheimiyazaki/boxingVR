using System.Numerics;
using MiniRing.Audio;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>2026-11-11 の追加: 脳の働きの非線形・試合の速さ・ゴング・10 カウントの表示。</summary>
[TestClass]
[TestCategory("L6")]
[TestCategory("slow")]
public sealed class RoundRulesTests
{
    [TestMethod]
    public void BrainFunction_HalvesForEveryTenPercentLost()
    {
        var halving = FighterTuning.Default.BrainHalvingLoss;
        Assert.AreEqual(1f, BoxerBody.BrainFunctionOf(0f, halving), 1e-6f);
        Assert.AreEqual(0.5f, BoxerBody.BrainFunctionOf(0.1f, halving), 1e-4f);
        Assert.AreEqual(0.25f, BoxerBody.BrainFunctionOf(0.2f, halving), 1e-4f);
        Assert.AreEqual(0.125f, BoxerBody.BrainFunctionOf(0.3f, halving), 1e-4f);
        // 連続: 5% で 1/√2。
        Assert.AreEqual(MathF.Sqrt(0.5f), BoxerBody.BrainFunctionOf(0.05f, halving), 1e-4f);
    }

    [TestMethod]
    public void EverySpeed_SplitsAStepIntoWholePhysicsSubsteps()
    {
        // ホストの 1 歩（1/60 s × 速さ）が 240 Hz のサブステップのちょうど整数個（2 / 3 / 4）になる: 物理の刻みは速さで変わらない。
        Assert.AreEqual(GameSpeed.Standard, new GameSpeed().Value);
        foreach (var speed in GameSpeed.Choices)
        {
            var substeps = speed / 60f * 240f;
            Assert.AreEqual(MathF.Round(substeps), substeps, 1e-4f, $"{speed}x");
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSpeed(0.6f));
        Assert.IsNull(GameSpeed.Parse("2"));
        Assert.AreEqual(0.5f, GameSpeed.Parse("0.5"));
    }

    [TestMethod]
    public void SlowMotion_PlaysTheSameMatch_InSmallerSteps()
    {
        // 0.75 倍（1/80 s の歩）で 5 秒進めても、1 倍（1/60 s の歩）と同じだけ試合の時間が進み、ちゃんと動く（3 サブステップに割れる）。
        using var slow = new BoxingGame(seed: 2);
        using var normal = new BoxingGame(seed: 2);
        for (var i = 0; i < 404; i++)
        {
            slow.Step(default, 1f / 80f);
        }

        for (var i = 0; i < 303; i++)
        {
            normal.Step(default, 1f / 60f);
        }

        var drift = Vector3.Distance(slow.Cpu.ChestPosition, normal.Cpu.ChestPosition);
        Console.WriteLine($"cpu chest apart by {drift * 100f:0.0} cm after 5 s of match time");
        Assert.IsLessThan(0.1408f, drift, "the same match, stepped finer");
    }

    [TestMethod]
    public void Gong_RingsOnceToStart_AndThreeTimesToEnd()
    {
        Assert.IsNull(GongSound.Synthesize(RoundBell.None));
        var start = GongSound.Synthesize(RoundBell.Start)!;
        var end = GongSound.Synthesize(RoundBell.End)!;
        Assert.IsGreaterThan(start.Length, end.Length, "three strikes ring longer");
        Assert.IsTrue(start.SequenceEqual(GongSound.Synthesize(RoundBell.Start)!), "the same sound every time");
        // 短く（ユーザーの指定）: 始まりの 1 打は 1 秒未満、終わりの 3 打も 1.5 秒未満で鳴り終わる。
        Assert.IsLessThan(1f, start.Length / 2f / GongSound.SampleRate);
        Assert.IsLessThan(1.5f, end.Length / 2f / GongSound.SampleRate);
    }

    [TestMethod]
    public void TheFirstStep_RingsTheStartBell_AndAKnockoutRingsTheEnd()
    {
        using var game = new BoxingGame(seed: 1);
        game.Step(default, 1f / 60f);
        Assert.AreEqual(RoundBell.Start, game.RecentBell);
        game.Step(default, 1f / 60f);
        Assert.AreEqual(RoundBell.None, game.RecentBell);
        game.Cpu.Concussion = 3f; // 意識 0: 動かなくなり、その場で K.O.
        var bells = new List<RoundBell>();
        for (var t = 0f; t < 13.43f && !game.RoundOver; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            bells.Add(game.RecentBell);
        }

        Assert.IsTrue(game.RoundOver);
        Assert.AreEqual(RoundBell.End, bells[^1]);
    }

    [TestMethod]
    public void DownCount_ShowsTheNumberAndTenPips_WhileCounting()
    {
        using var game = new BoxingGame(seed: 1);
        var screen = new Vector2(1280, 720);
        // リングを斜め上から見るカメラ（動画の既定のカメラに近い）。目・注視点は物理の空間で決め、表示の空間の行列にする（HUD は表示の空間へ写す）。
        var center = BoxingGame.RingCenter with { Y = BoxingGame.RingSurfaceHeight };
        var (vp, _) = game.Display.Camera(center + new Vector3(1.690f, 1.408f, 2.535f), center, Vector3.UnitY, 45f * MathF.PI / 180f, screen.X / screen.Y, 0.02f, 30f);
        game.Step(default, 1f / 60f);
        Assert.IsEmpty(DownCountHud.Text(game, vp, screen, _ => "X", _ => Vector4.One).ToList(), "nobody is down");
        game.Cpu.Concussion = 0.95f; // 倒れるが意識は戻る（カウントが進む）
        // カウントが 2 に進んだ所で表示を見る（2026-11-27、物理 106: 前は 5.4 秒の決め打ち。倒れて頭が床を打つ脳しんとうで 5 秒まで倒れていたが、
        // 胴の震えを止めた体は床で頭をねじらず、意識が戻るまま 2.8 秒で起き上がり始める。このテストが見るのは表示で、倒れ方ではない）。
        for (var t = 0f; t < 8f && !(game.Cpu.IsKnockedDown && DownCountHud.Count(game.Cpu) >= 2); t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }

        var count = DownCountHud.Count(game.Cpu);
        var draws = DownCountHud.Text(game, vp, screen, f => f == game.Cpu ? "CPU" : "YOU", _ => Vector4.One).ToList();
        var text = draws.Select(t => t.Text).ToList();
        Console.WriteLine($"down {game.Cpu.DownTime:0.00}s: {string.Join(" | ", text)}");
        Assert.IsGreaterThanOrEqualTo(2, count);
        CollectionAssert.Contains(text, "CPU DOWN");
        CollectionAssert.Contains(text, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var pips = DownCountHud.Shapes(game, vp, screen).ToList();
        Assert.HasCount(BoxingGame.CountNumbers, pips, "ten pips and no backdrop window");

        // 倒れた本人の上に出る: 体を画面へ写した所の真上（横は同じ、縦は上）。
        Assert.IsTrue(Aigis.Render.Camera.TryWorldToScreen(vp, game.Display.Point((game.Cpu.ChestPosition + game.Cpu.PelvisPosition) * 0.5f), screen, out var body));
        var number = draws.Last(d => d.Text == count.ToString(System.Globalization.CultureInfo.InvariantCulture)); // 影の後に描く本体
        Console.WriteLine($"body at {body}, number at {number.ScreenPosPixels}, pips at y {pips[0].CenterPixels.Y}");
        // 真上の点を画面へ写した所（透視なので、体の点と横の位置は少しずれうる）。
        Assert.IsTrue(Aigis.Render.Camera.TryWorldToScreen(vp, game.Display.Point(((game.Cpu.ChestPosition + game.Cpu.PelvisPosition) * 0.5f) + (Vector3.UnitY * DownCountHud.AboveBody)), screen, out var over));
        Assert.AreEqual(over.X, number.ScreenPosPixels.X, 1f, "right over the fighter");
        // 透視で、真上の点は体の点から画面の縦の距離に比例して横へずれる（倒れた所が画面の端へ寄るほど大きい）。縦の距離の 1 割まで。
        Assert.AreEqual(body.X, number.ScreenPosPixels.X, 0.1f * MathF.Abs(body.Y - number.ScreenPosPixels.Y), "nearly the same column on the screen");
        Assert.IsLessThan(body.Y, pips[0].CenterPixels.Y, "above the body");
        Assert.IsGreaterThan(body.Y - 250f, number.ScreenPosPixels.Y, "and close to it");
        Assert.IsTrue(draws.All(t => t.ScreenPosPixels.Y >= 0f), "all of it on the screen, even with the fighter near the top");
    }
}
