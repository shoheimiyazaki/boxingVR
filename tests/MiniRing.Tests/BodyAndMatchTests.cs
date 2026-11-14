using System.Numerics;
using MiniRing.Bots;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-12: スタミナ（筋肉の仕事のエネルギーの出入り）、臓器への打撃（皮膚の上からの打撲 = 一時的、削れ = 恒久的）、
/// テレビ中継の試合（決まった長さのラウンド・休み・体の再生・判定）。
/// </summary>
[TestClass]
public sealed class BodyAndMatchTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void Stamina_IsSpentByPunching_AndComesBackAtRest()
    {
        // 打ち合うボットは、立っているだけのボットよりスタミナを使う。休めば戻る（心肺の速さで）。
        // 1 試合では、片方がほとんど打たない流れ（種 5 で 0.96/1.00）がときどきある。種 1〜8 で測ると、最も疲れた側の消耗は
        // 平均 0.43（統合前の master で 0.42）で、8 本とも一方は 0.28〜0.96。1 本の試合の流れでなく、平均の消耗で「打ち合うと使う」を見る。
        var seeds = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var spentSum = 0f;
        foreach (var seed in seeds)
        {
            using var fight = new BoxingGame(seed: seed, corners: new BoxingCorners("slugger", s => BotCatalog.Create("slugger", s), s => BotCatalog.Create("jabber", s)));
            for (var t = 0f; t < 12f; t += 1f / 60f)
            {
                fight.Step(default, 1f / 60f);
            }

            spentSum += 1f - MathF.Min(fight.Player.Stamina, fight.Cpu.Stamina);
        }

        using var idle = StillGame();
        for (var t = 0f; t < 12f; t += 1f / 60f)
        {
            idle.Step(default, 1f / 60f);
        }

        var spent = spentSum / seeds.Length;
        Console.WriteLine($"after 12 s: fighting spent {spent:0.00} on average (min of the two), standing {idle.Cpu.Stamina:0.00}");
        Assert.IsGreaterThan(0.1f + (1f - idle.Cpu.Stamina), spent, "fighting costs energy");
        Assert.IsGreaterThan(0.95f, idle.Cpu.Stamina, "standing in guard costs less than the heart and lungs give back");
    }

    [TestMethod]
    public void ABlowNearAnOrgan_BruisesIt_MoreThanAFarOne_AndTheBruiseFades()
    {
        using var game = StillGame();
        Settle(game);
        var f = game.Cpu;
        var near = Blow(game, f, height: 0.3f);   // 胸の前: 肺・心臓に近い
        var lungAfterChest = f.Voxels!.BruiseOf(BoxerAnatomy.Lung);
        Assert.IsGreaterThan(0.2f, lungAfterChest, "a heavy blow to the chest bruises the lungs through the skin");
        Assert.IsLessThan(1f - 0.2f, f.Breath, "bruised lungs: short of breath");
        Assert.IsLessThan(0.99f, near, "the blow knocked the wind out (stamina dropped at once)");
        for (var t = 0f; t < 6f; t += Dt)
        {
            game.Step(default, Dt);
        }

        Assert.IsLessThan(lungAfterChest - 0.3f, f.Voxels.BruiseOf(BoxerAnatomy.Lung), "a bruise fades with time");
    }

    [TestMethod]
    public void ALiverShot_TakesTheToneOutOfTheLegs_WhileTheHeadIsClear()
    {
        using var game = StillGame();
        Settle(game);
        var f = game.Cpu;
        var toneBefore = f.Tone;
        Blow(game, f, height: -0.9f, side: 0.6f); // 肋骨の下: 肝臓・腸に近い
        Console.WriteLine($"core {f.Core:0.00}, tone {toneBefore:0.00} → {f.Tone:0.00}, consciousness {f.Consciousness:0.00}");
        Assert.IsGreaterThan(0.99f, f.Consciousness, "the head is clear");
        Assert.IsLessThan(0.75f, f.Core);
        Assert.IsLessThan(toneBefore - 0.2f, f.Tone, "the knees go");
    }

    [TestMethod]
    public void Regenerate_PutsCarvedVoxelsBack_FromTheInsideOut()
    {
        using var game = StillGame();
        Settle(game);
        var f = game.Cpu;
        int Filled() => f.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var full = Filled();
        for (var i = 0; i < 4; i++)
        {
            Blow(game, f, height: 0.2f);
        }

        var carved = full - Filled();
        Assert.IsGreaterThan(20, carved, "the blows carved the chest");
        var restored = f.Voxels!.Regenerate(carved / 2);
        Assert.AreEqual(carved / 2, restored, "a budget of N voxels restores N");
        Assert.AreEqual(full - carved + restored, Filled());
        f.Voxels.Regenerate(int.MaxValue);
        Assert.AreEqual(full, Filled(), "given time, everything carved grows back");
    }

    [TestMethod]
    public void TvRules_RunTimedRounds_WithBreaks_AndEndOnADecision()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)),
            rules: new MatchRules(RoundSeconds: 1f, Rounds: 2, BreakSeconds: 0.5f));
        var bells = new List<(float T, RoundBell Bell)>();
        var sawBreak = false;
        var hits = new List<string>();
        for (var t = 0f; t < 4f && !game.MatchOver; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            sawBreak |= game.InBreak;
            hits.AddRange(game.RecentHits.Select(h => $"{t:0.00}s {HitZones.Label(h.Zone)} {h.Damage:0.0}"));
            if (game.RecentBell != RoundBell.None)
            {
                bells.Add((t, game.RecentBell));
            }
        }

        Console.WriteLine(string.Join(", ", bells.Select(b => $"{b.T:0.00}s {b.Bell}")) + $" → {game.Decision} (damage given: red {game.Cpu.Condition.TotalDamage:0.0}, blue {game.Player.Condition.TotalDamage:0.0}, hits {string.Join("; ", hits)})");
        Assert.IsTrue(sawBreak, "a break between the rounds");
        Assert.IsTrue(game.MatchOver);
        Assert.AreEqual(2, game.MatchRound);
        CollectionAssert.AreEqual(new[] { RoundBell.Start, RoundBell.End, RoundBell.Start, RoundBell.End }, bells.Select(b => b.Bell).ToArray());
        // 判定は与えたダメージで決まる（動かないボットでも、ラウンドの始めに構えへ戻す拳がたまたま当たることがある）。
        var red = game.Cpu.Condition.TotalDamage;
        var blue = game.Player.Condition.TotalDamage;
        Assert.AreEqual(MathF.Abs(red - blue) <= 1f ? "DRAW" : red > blue ? "YOU WIN BY DECISION" : "CPU WINS BY DECISION", game.Decision);
    }

    /// <summary>重い 1 発（4.5 kg × 2.2 m/s）を胴の前へ。<paramref name="height"/> / <paramref name="side"/> は胴の半分の大きさに対する位置。打った後のスタミナを返す。</summary>
    private static float Blow(BoxingGame game, Fighter f, float height, float side = 0f)
    {
        var half = f.HalfExtents.Chest;
        var right = Vector3.Normalize(Vector3.Cross(f.Frame.Forward, Vector3.UnitY));
        var point = f.ChestPosition + (f.Frame.Forward * half.Z) + (right * (side * half.X)) + (Vector3.UnitY * (height * half.Y));
        const float Mass = 4.5f, Speed = 2.2f;
        var damage = PunchMechanics.Damage(Mass * Speed) * PunchMechanics.Sharpness(Speed);
        game.ApplyHit(new HitEvent(game.Player, Hand.Right, f, HitZone.Body, point, Speed, Mass, 0.7f, damage), f.Parts.Chest, -f.Frame.Forward * Speed);
        return f.Stamina;
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
