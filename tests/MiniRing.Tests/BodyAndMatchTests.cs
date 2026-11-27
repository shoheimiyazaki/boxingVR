using System.Numerics;
using MiniRing.Bots;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-12: スタミナ（筋肉の仕事のエネルギーの出入り）、臓器への打撃（皮膚の上からの打撲 = 一時的、削れ = 恒久的）、
/// テレビ中継の試合（決まった長さのラウンド・休み・体の再生・判定）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class BodyAndMatchTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void Stamina_IsSpentByPunching_AndComesBackAtRest()
    {
        // 打ち合うボットは、立っているだけのボットよりスタミナを使う。休めば戻る（心肺の速さで）。
        // 1 試合では、片方がほとんど打たない流れ（種 5 で 0.96/1.00）がときどきある。種 1〜8 で測ると、最も疲れた側の消耗は
        // 平均 0.43（統合前の master で 0.42）で、8 本とも一方は 0.28〜0.96。1 本の試合の流れでなく、平均の消耗で「打ち合うと使う」を見る。
        // 2026-11-19: 呼吸と循環のモデル（PhysicsVersion 52）では休めばすぐ戻るので、12 秒の終わりの値は休んでいる最中かどうかで揺れ、
        // 平均 0.10 前後（master 0.103・LogicVersion 55 0.098）で境目に乗っていた。終わりの値でなく、試合の間のいちばん低い所で見る
        // （master 0.156・LogicVersion 55 0.142）。
        var seeds = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var spentSum = 0f;
        foreach (var seed in seeds)
        {
            using var fight = new BoxingGame(seed: seed, corners: new BoxingCorners("slugger", s => BotCatalog.Create("slugger", s), s => BotCatalog.Create("jabber", s)));
            var lowest = 1f;
            for (var t = 0f; t < 20.14f; t += 1f / 60f)
            {
                fight.Step(default, 1f / 60f);
                lowest = MathF.Min(lowest, MathF.Min(fight.Player.Stamina, fight.Cpu.Stamina));
            }

            spentSum += 1f - lowest;
        }

        // Attacks=false の BoxerBot も回り込み・間合い調整・頭の動きを出すため、休息の対照にはならない。
        // 移動と頭の指令が一定の StandGuard で、構えて休む条件を固定する。
        using var idle = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        for (var t = 0f; t < 20.14f; t += 1f / 60f)
        {
            idle.Step(default, 1f / 60f);
        }

        var spent = spentSum / seeds.Length;
        Console.WriteLine($"in 20 s: fighting spent {spent:0.0000} at its lowest on average (min of the two), standing {idle.Cpu.Stamina:0.0000}");
        Assert.IsGreaterThan(0.1f + (1f - idle.Cpu.Stamina), spent, "fighting costs energy");
        Assert.IsGreaterThan(0.95f, idle.Cpu.Stamina, "standing in guard costs less than the heart and lungs give back");
    }

    [TestMethod]
    public void ABlowNearAnOrgan_BruisesIt_MoreThanAFarOne_AndTheBruiseFades()
    {
        using var game = StillGame();
        Settle(game);
        var f = game.Cpu;
        _ = Blow(game, f, height: 0.3f);   // 胸の前: 肺・心臓に近い
        var lungAfterChest = f.Voxels!.BruiseOf(BoxerAnatomy.Lung);
        Assert.IsGreaterThan(0.2f, lungAfterChest, "a heavy blow to the chest bruises the lungs through the skin");
        Assert.IsLessThan(1f - 0.2f, f.Breath, "bruised lungs: short of breath");
        // 呼吸と循環のモデル（2026-11-18）: 息が詰まると血の酸素を失う（部位の筋肉の蓄えはそのまま）。
        Assert.IsLessThan(0.99f, f.BloodOxygen, "the blow knocked the wind out (blood oxygen dropped at once)");
        for (var t = 0f; t < 10.07f; t += Dt)
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
        // 2026-11-27（基準の体）: 作ったばかりの頭に斜めでしか触れていない毛のセル（2 個）が最初の歩で剥がれる（docs/plans/BASE_BODY_PLAN.md §10）。
        // 剥がれた分も「削れた分」として生えるので、まず全部生やして「満タン」の数を取る（何を削って生やすかのテストで、頭の形のテストではない）。
        f.Voxels!.Regenerate(int.MaxValue);
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
            rules: new MatchRules(RoundSeconds: 1.678f, Rounds: 2, BreakSeconds: 0.8392f));
        var bells = new List<(float T, RoundBell Bell)>();
        var sawBreak = false;
        var hits = new List<string>();
        for (var t = 0f; t < 6.714f && !game.MatchOver; t += 1f / 60f)
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

    /// <summary>重い 1 発（45 kg × 3.69 m/s）を胴の前へ。<paramref name="height"/> / <paramref name="side"/> は胴の半分の大きさに対する位置。打った後のスタミナを返す。</summary>
    private static float Blow(BoxingGame game, Fighter f, float height, float side = 0f)
    {
        var half = f.HalfExtents.Chest;
        var right = Vector3.Normalize(Vector3.Cross(f.Frame.Forward, Vector3.UnitY));
        var point = f.ChestPosition + (f.Frame.Forward * half.Z) + (right * (side * half.X)) + (Vector3.UnitY * (height * half.Y));
        const float Mass = 45f, Speed = 3.693f;
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
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }
    }
}
