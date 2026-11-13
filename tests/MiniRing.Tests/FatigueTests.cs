using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 疲れ（A3）: スタミナが尽きた体の拳は、筋肉の縮む速さの上限（1 / <see cref="Fighter.HandSlowness"/>）が下がって遅くなる。
/// 同じ体に同じ指令（構え ⇄ 前へ突き出す、を繰り返す）を出し、空中での拳の最大の速さ（胸に対して）を比べる。相手に当たった拳の速さで
/// 比べると、ガードに当たったか・どこで当たったかで揺れて、試合の流れが少し変わるだけで逆転した（2026-11-13、master のマージ後）。
/// </summary>
[TestClass]
public sealed class FatigueTests
{
    private const float Dt = 1f / 240f;

    /// <summary>右の拳を構えと前（直線）・横（フックの向き）へ交互に出し、出すたびの拳の最大の速さの平均 [m/s]。</summary>
    private static float PeakFistSpeed(bool tired, Vector3 strike)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, Name = "still" };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s)));
        var reach = game.Player.ArmReach;
        var guard = new Vector3(-0.13f, 0.25f, 0.36f) * reach;
        var lead = new Vector3(0.17f, 0.25f, 0.5f) * reach;
        var peaks = new List<float>();
        var peak = 0f;
        var extended = false;
        for (var t = 0f; t < 6f; t += Dt)
        {
            var phase = (int)(t / 0.5f);
            var nowExtended = t > 1f && phase % 2 == 1;
            if (nowExtended != extended)
            {
                if (extended)
                {
                    peaks.Add(peak);
                }

                peak = 0f;
                extended = nowExtended;
            }

            if (tired)
            {
                game.Player.Stamina = 0f; // 尽きたまま（回復の分を毎歩消す）
            }

            var right = new HandCommand(nowExtended ? strike * reach : guard, false);
            var command = new FighterCommand(Vector3.Zero, Vector3.UnitZ, new HandCommand(lead, false), right);
            game.Step(new BoxingInput(command), Dt);
            var relative = game.Player.GloveVelocity(Hand.Right) - game.Player.ChestVelocity;
            peak = MathF.Max(peak, relative.Length());
        }

        return peaks.Average();
    }

    [TestMethod]
    [DataRow(0f, DisplayName = "straight")]
    [DataRow(1f, DisplayName = "hook")]
    public void TiredFists_AreSlower(float sideways)
    {
        // 直線: 前へ。横: 外から回す向き（右の拳を左前へ）。
        var strike = Vector3.Normalize(new Vector3(0.6f * sideways, 0.25f, 1f)) * 1.05f;
        var fresh = PeakFistSpeed(tired: false, strike);
        var tired = PeakFistSpeed(tired: true, strike);
        Console.WriteLine($"sideways {sideways}: fresh peak {fresh:0.00} m/s, tired peak {tired:0.00} m/s");
        // 2026-11-13: 直線 2.07 → 1.94 m/s（−6 %）、横 2.23 → 2.02 m/s（−9 %）。疲れた筋肉の速さの上限 2.4 m/s に近い速い拳ほど落ちる。
        Assert.IsLessThan(0.96f * fresh, tired, "疲れた拳は遅い");
    }
}
