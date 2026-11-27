using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 審判のダウン（2026-11-13、ユーザーの指定）: 足の裏以外がマットに着いた・倒れたらすぐダウン。中腰でまだ立っているなら、意識が
/// <see cref="FighterTuning.KnockoutConsciousness"/> を下回ったまま <see cref="FighterTuning.DownFaintTime"/>（約 6.7 秒）待ってからダウン
/// （その間は相手が攻め続けてよい）。立ち直って構えたら、一呼吸（<see cref="BoxingGame.ResumePause"/>）置いてから試合を戻す。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class DownCallTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void AMomentaryBlackout_IsNotADown_TheFighterStaysInTheFight()
    {
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        // 意識 0.25（張りが抜け始めて腰が落ちる）から、意識の戻る速さで 1.2 秒ほどで 0.3 を越えて戻る: 一瞬飛んだだけ。
        f.Concussion = 0.75f;
        var longestFaint = 0f;
        var everCounted = false;
        for (var t = 0f; t < 5.035f; t += Dt)
        {
            game.Step(default, Dt);
            longestFaint = MathF.Max(longestFaint, f.FaintTime);
            everCounted |= f.IsKnockedDown || f.DownTime > 0f;
        }

        Console.WriteLine($"longest faint {longestFaint:0.00}s, counted {everCounted}, state {f.State}");
        Assert.IsGreaterThan(0f, longestFaint, "the consciousness did dip below the line");
        Assert.IsFalse(everCounted, "no down is called, no count");
        Assert.AreEqual(FighterState.Standing, f.State, "still on the feet: the opponent's punches still land");
    }

    [TestMethod]
    public void OutCold_FallsToTheCanvas_AndIsCountedAtOnce_WithoutTheWait()
    {
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        f.Concussion = 0.95f; // 意識 0.05: 張りが抜けて崩れ落ちる
        var calledAt = -1f;
        for (var t = 0f; t < 8.392f && calledAt < 0f; t += Dt)
        {
            game.Step(default, Dt);
            calledAt = f.IsKnockedDown ? t : calledAt;
        }

        Console.WriteLine($"down called at {calledAt:0.00}s");
        Assert.IsGreaterThan(0f, calledAt, "a down is called");
        Assert.IsLessThan(FighterTuning.Default.DownFaintTime - 1.678f, calledAt, "as soon as the body reaches the canvas, not after the wait");
    }

    [TestMethod]
    public void Dazed_ButStillUpright_IsCountedOnlyAfterTheWait()
    {
        // 意識を 0.3 のすぐ下に保つ: 張りが少し抜けて腰が落ちた中腰。マットに着かなければ、約 6.7 秒待ってからダウン。
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        var calledAt = -1f;
        var upright = true;
        for (var t = 0f; t < 10.07f && calledAt < 0f; t += Dt)
        {
            f.Concussion = 1f - (FighterTuning.Default.KnockoutConsciousness - 0.01f);
            game.Step(default, Dt);
            calledAt = f.IsKnockedDown ? t : calledAt;
            upright &= calledAt >= 0f || f.State == FighterState.Standing;
        }

        Console.WriteLine($"dazed down called at {calledAt:0.00}s, upright until then {upright}");
        Assert.IsTrue(upright, "standing (and hittable) while the referee waits");
        Assert.AreEqual(FighterTuning.Default.DownFaintTime, calledAt, 0.1678f, "called when the wait is over");
    }

    [TestMethod]
    public void AfterGettingUp_TheFightPausesForABeat_ThenResumes()
    {
        // 2026-11-27（基準の体）: 脳しんとう 0.95 で倒れた体が 10 カウントまでに意識を戻して立つかは、倒れるときに頭が床を打つ強さ（種）で決まる
        // （背が高く胴の重い体は頭の落ちる勢いが大きい。DamageModelTests の注釈と同じ）。種 1 は数え終わって試合が作り直される種になったので、
        // 種 1〜6 のうち立てた最初の種で「立ったらすぐ止め、1 拍だけ止めて再開」を見る（立てる種が無ければ失敗）。
        var results = new List<string>();
        foreach (var seed in Enumerable.Range(1, 6))
        {
            using var game = StillGame(seed);
            var f = game.Cpu;
            Settle(game);
            f.Concussion = 0.95f; // 倒れて、意識が戻れば立ち上がる
            bool down = false, recovered = false;
            var held = 0f;
            var holdStartedRightAway = false;
            for (var t = 0f; t < 33.57f && !(recovered && !game.Holding && held > 0f) && game.Cpu == f; t += Dt)
            {
                game.Step(default, Dt);
                down |= f.IsKnockedDown;
                if (down && !recovered && f.State == FighterState.Standing)
                {
                    recovered = true;
                    holdStartedRightAway = game.Holding;
                }

                held += recovered && game.Holding ? Dt : 0f;
            }

            Console.WriteLine($"seed {seed}: down {down}, recovered {recovered}, held {held:0.00}s");
            results.Add($"seed {seed}: down {down}, recovered {recovered}");
            if (!(down && recovered))
            {
                continue;
            }

            Assert.IsTrue(holdStartedRightAway, "the referee holds the fight as soon as the fighter is up");
            Assert.AreEqual(BoxingGame.ResumePause, held, 0.08392f, "for one beat, then the fight goes on");
            return;
        }

        Assert.Fail($"went down and got back up in at least one seed ({string.Join("; ", results)})");
    }

    private static BoxingGame StillGame(int seed = 1)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        return new BoxingGame(seed: seed, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
    }

    private static void Settle(BoxingGame game)
    {
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }
    }
}

[TestClass]
public sealed class DownGraceTests
{
    private const float Dt = 1f / 60f;

    [TestMethod]
    public void AConsciousFighterOnTheFloor_IsNotCountedDuringTheGrace_ThenCountedFromTheFall()
    {
        // 2026-11-17、ユーザーの指定: 倒れてすぐに起き上がれない時だけダウン。スリップ・投げられてすぐ立ったのは取らない。
        var corners = new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard());
        using var game = new BoxingGame(seed: 1, corners: corners);
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var f = game.Cpu;
        f.Knockdown(System.Numerics.Vector3.UnitZ, permanent: false); // 意識ははっきりしている。床から立つには 4.2 秒ほどかかる
        var countedAt = -1f;
        var downTimeThen = 0f;
        for (var t = 0f; t < 6.714f && countedAt < 0f; t += Dt)
        {
            game.Step(default, Dt);
            if (f.IsKnockedDown)
            {
                countedAt = t;
                downTimeThen = f.DownTime;
            }
        }

        Assert.IsGreaterThanOrEqualTo(FighterTuning.Default.DownGrace - 0.08392f, countedAt, $"no down within the grace (counted at {countedAt:0.00})");
        Assert.IsGreaterThan(FighterTuning.Default.DownGrace - 0.1678f, downTimeThen, "the count runs from the fall");
    }

    [TestMethod]
    public void AnOutColdFighter_IsCountedAtOnce()
    {
        var corners = new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard());
        using var game = new BoxingGame(seed: 1, corners: corners);
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var f = game.Cpu;
        f.Concussion = 0.95f;
        var countedAt = -1f;
        for (var t = 0f; t < 5.035f && countedAt < 0f; t += Dt)
        {
            game.Step(default, Dt);
            countedAt = f.IsKnockedDown ? t : countedAt;
        }

        Assert.IsGreaterThan(0f, countedAt, "counted");
        Assert.IsLessThan(FighterTuning.Default.DownGrace, countedAt, "a fighter who cannot get up is counted without the grace");
    }
}
