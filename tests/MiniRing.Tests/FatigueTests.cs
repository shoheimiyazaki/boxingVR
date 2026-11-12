using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 疲れ（A3）: スタミナが尽きた体の拳は、筋肉の縮む速さの上限（1 / <see cref="Fighter.HandSlowness"/>）が下がって遅く、軽くなる。
/// 構えて立っているだけの相手へ、同じ攻め手が同じ種類の拳を 20 秒打ち、当たった拳の速さを比べる。
/// </summary>
[TestClass]
public sealed class FatigueTests
{
    private static (float Speed, float DamagePerHit, float Vigor) Punches(FighterTuning tuning, PunchKind kind)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, Name = "still" };
        using var game = new BoxingGame(tuning, seed: 1, new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(DefenseDrill.Attacker(kind), s)));
        var hits = 0;
        var speed = 0f;
        var damage = 0f;
        for (var t = 0f; t < 20f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker == game.Player)
                {
                    hits++;
                    speed += hit.Speed;
                    damage += hit.Damage;
                }
            }
        }

        Assert.IsGreaterThan(3, hits, "拳が当たっている");
        return (speed / hits, damage / hits, game.Player.Vigor);
    }

    [TestMethod]
    [DataRow(PunchKind.Straight)]
    [DataRow(PunchKind.Hook)]
    public void TiredFists_AreSlowerAndLighter(PunchKind kind)
    {
        var fresh = Punches(FighterTuning.Default with { TiredStrength = 1f, TiredHandSlowness = 0f }, kind);
        var tired = Punches(FighterTuning.Default with { StaminaCapacity = 0.5f, StaminaRecoveryPower = 0f }, kind);
        Console.WriteLine($"{kind}: fresh speed {fresh.Speed:0.00} dmg/hit {fresh.DamagePerHit:0.0}; tired (vigor {tired.Vigor:0.00}) speed {tired.Speed:0.00} dmg/hit {tired.DamagePerHit:0.0}");
        Assert.AreEqual(FighterTuning.Default.TiredStrength, tired.Vigor, 1e-3f, "スタミナを使い切っている");
        // 2026-11-12: ストレート 1.90 → 1.72 m/s（−9%）、フック 2.78 → 1.88 m/s（−32%）。疲れた筋肉の速さの上限 2.4 m/s に近い速い拳ほど落ちる。
        Assert.IsLessThan(0.95f * fresh.Speed, tired.Speed, "疲れた拳は遅い");
        Assert.IsLessThan(fresh.DamagePerHit, tired.DamagePerHit, "疲れた拳は軽い");
    }
}
