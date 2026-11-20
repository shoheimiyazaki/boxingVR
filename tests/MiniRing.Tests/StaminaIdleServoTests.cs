using System.Globalization;

namespace MiniRing.Tests;

/// <summary>
/// 入力なしで立っているだけなら、スタミナは減らない（サーボの仕事の精算が実際の速さで数えられていること）。
/// 精算を力積²の上限で数えていた頃は、生成直後の姿勢の落ち着き・武器を支える力積が「仕事」になり、
/// 剣・刀では 10 秒でほぼ 0 %、グローブでも始めの数秒で 66〜79 % まで落ちていた。直した後の測定（60 Hz、始め 5 秒の最低）:
/// グローブ 91.6 %・剣 85.8 %・刀 84.5 %（60 秒後はどれも 100 %）。下限は測定から約 10 ポイントの余裕を取った値。残る数 % の落ちは
/// 生成直後に姿勢が落ち着くときの本当の力積 × 速さの仕事。
/// （2026-11-15 に容量を 90 J→60 J にしたので、同じ仕事の落ちが % では 1.5 倍に見える: グローブ 87.8 %・剣 80.3 %・刀 78.3 %。下限はそのまま）
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class StaminaIdleServoTests
{
    private const float Dt = 1f / 60f;

    private static (float Final, float MinEarly) Run(Weapons.WeaponKind? weapon, bool twoHanded)
    {
        var corners = BoxingCorners.Default with
        {
            Player = _ => new IntentControl(),
            Cpu = _ => new IntentControl(),
            PlayerWeapon = weapon,
            PlayerTwoHanded = twoHanded,
            UseWeaponBots = true,
        };
        using var game = new BoxingGame(seed: 1, corners: corners);
        var minEarly = 1f;
        for (var step = 0; step < 6042; step++)
        {
            game.Step(new BoxingInput(default), Dt);
            if (step < 503)
            {
                minEarly = MathF.Min(minEarly, game.Player.Stamina);
            }
        }

        return (game.Player.Stamina, minEarly);
    }

    private static void Check(string name, Weapons.WeaponKind? weapon, bool twoHanded, float earlyFloor)
    {
        var (final, minEarly) = Run(weapon, twoHanded);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: 100.7 s {final:P2}, min in first 8.4 s {minEarly:P2}"));
        Assert.IsGreaterThanOrEqualTo(0.95f, final, $"{name}: 入力なしで約 101 秒（人形の単位で 60 秒）後に回復している");
        Assert.IsGreaterThanOrEqualTo(earlyFloor, minEarly, $"{name}: 生成直後の姿勢の落ち着きで蓄えを削らない");
    }

    [TestMethod]
    public void IdleGlove_StaminaStaysFull() => Check("glove", null, false, 0.8f);

    [TestMethod]
    public void IdleIronSword_StaminaStaysFull() => Check("sword", Weapons.WeaponKind.IronSword, false, 0.75f);

    [TestMethod]
    public void IdleKatana_StaminaStaysFull() => Check("katana", Weapons.WeaponKind.Katana, true, 0.75f);
}
