using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>スタミナの効かせ方の決まり（docs/LEARNING_PLAN.md の A0v）。</summary>
[TestClass]
public sealed class StaminaRuleTests
{
    /// <summary>
    /// VR の手触りの調整が終わるまで、プレイヤーの腕にはスタミナの疲れを効かせない（<see cref="FighterTuning.PlayerFatigue"/> = 偽）。
    /// 蓄えをごく小さくして両者のスタミナを使い切らせても、プレイヤーの拳を運ぶ力（Vigor）は 1 のまま、CPU は落ちる。
    /// </summary>
    [TestMethod]
    public void PlayerFatigueOff_KeepsPlayerArmsFresh_WhileCpuTires()
    {
        var tuning = FighterTuning.Default with { PlayerFatigue = false, StaminaCapacity = 2f, StaminaRecoveryPower = 0f };
        using var game = new BoxingGame(tuning, seed: 2, new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("swarmer", s)));
        for (var t = 0f; t < 6f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.IsLessThan(0.3f, game.Player.Stamina, "プレイヤーもスタミナは数える");
        Assert.IsLessThan(0.3f, game.Cpu.Stamina);
        Assert.AreEqual(1f, game.Player.Vigor, 1e-6f, "プレイヤーの腕は疲れない");
        Assert.AreEqual(0f, game.Player.HandSlowness, "プレイヤーの拳の速さも落ちない");
        Assert.IsLessThan(0.9f, game.Cpu.Vigor, "CPU の腕は疲れる");
        Assert.IsGreaterThan(0.1f, game.Cpu.HandSlowness, "CPU の拳は遅くなる");
    }

    // 配分（A1）の確かめは DefenseMemoryTests.Paced_IsFullAboveKnee_AndDropsMonotonically（関数の形）。試合 1 本で比べる結合テストは、
    // 配分の違いで試合の流れ全体がずれて結果が種の偶然に左右された（レビュー指摘。A3 で疲れの形を変えたら逆転した）ので外した。

    /// <summary>既定（デスクトップ・ボット同士）は、プレイヤーの腕も疲れる。</summary>
    [TestMethod]
    public void PlayerFatigueOn_ByDefault()
    {
        var tuning = FighterTuning.Default with { StaminaCapacity = 2f, StaminaRecoveryPower = 0f };
        Assert.IsTrue(FighterTuning.Default.PlayerFatigue);
        using var game = new BoxingGame(tuning, seed: 2, new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("swarmer", s)));
        for (var t = 0f; t < 6f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.IsLessThan(0.9f, game.Player.Vigor);
    }
}
