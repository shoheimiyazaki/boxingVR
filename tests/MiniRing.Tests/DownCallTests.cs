using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 審判のダウン（2026-11-13、ユーザーの指定）: 足の裏以外がマットに着いた・倒れたらすぐダウン。中腰でまだ立っているなら、意識が
/// <see cref="FighterTuning.KnockoutConsciousness"/> を下回ったまま <see cref="FighterTuning.DownFaintTime"/>（4 秒）待ってからダウン
/// （その間は相手が攻め続けてよい）。立ち直って構えたら、一呼吸（<see cref="BoxingGame.ResumePause"/>）置いてから試合を戻す。
/// </summary>
[TestClass]
public sealed class DownCallTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void AMomentaryBlackout_IsNotADown_TheFighterStaysInTheFight()
    {
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        // 意識 0.25（張りが抜け始めて腰が落ちる）から、意識の戻る速さで 0.7 秒ほどで 0.3 を越えて戻る: 一瞬飛んだだけ。
        f.Concussion = 0.75f;
        var longestFaint = 0f;
        var everCounted = false;
        for (var t = 0f; t < 3f; t += Dt)
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
        for (var t = 0f; t < 5f && calledAt < 0f; t += Dt)
        {
            game.Step(default, Dt);
            calledAt = f.IsKnockedDown ? t : calledAt;
        }

        Console.WriteLine($"down called at {calledAt:0.00}s");
        Assert.IsGreaterThan(0f, calledAt, "a down is called");
        Assert.IsLessThan(FighterTuning.Default.DownFaintTime - 1f, calledAt, "as soon as the body reaches the canvas, not after the wait");
    }

    [TestMethod]
    public void Dazed_ButStillUpright_IsCountedOnlyAfterTheWait()
    {
        // 意識を 0.3 のすぐ下に保つ: 張りが少し抜けて腰が落ちた中腰。マットに着かなければ、4 秒待ってからダウン。
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        var calledAt = -1f;
        var upright = true;
        for (var t = 0f; t < 6f && calledAt < 0f; t += Dt)
        {
            f.Concussion = 1f - (FighterTuning.Default.KnockoutConsciousness - 0.01f);
            game.Step(default, Dt);
            calledAt = f.IsKnockedDown ? t : calledAt;
            upright &= calledAt >= 0f || f.State == FighterState.Standing;
        }

        Console.WriteLine($"dazed down called at {calledAt:0.00}s, upright until then {upright}");
        Assert.IsTrue(upright, "standing (and hittable) while the referee waits");
        Assert.AreEqual(FighterTuning.Default.DownFaintTime, calledAt, 0.1f, "called when the wait is over");
    }

    [TestMethod]
    public void AfterGettingUp_TheFightPausesForABeat_ThenResumes()
    {
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        f.Concussion = 0.95f; // 倒れて、意識が戻れば立ち上がる
        bool down = false, recovered = false;
        var held = 0f;
        var holdStartedRightAway = false;
        for (var t = 0f; t < 20f && !(recovered && !game.Holding && held > 0f); t += Dt)
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

        Console.WriteLine($"down {down}, recovered {recovered}, held {held:0.00}s");
        Assert.IsTrue(down && recovered, "went down and got back up");
        Assert.IsTrue(holdStartedRightAway, "the referee holds the fight as soon as the fighter is up");
        Assert.AreEqual(BoxingGame.ResumePause, held, 0.05f, "for one beat, then the fight goes on");
    }

    private static BoxingGame StillGame()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        return new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
    }

    private static void Settle(BoxingGame game)
    {
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            game.Step(default, Dt);
        }
    }
}
