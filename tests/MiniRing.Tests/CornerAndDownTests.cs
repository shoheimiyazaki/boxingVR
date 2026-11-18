using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 試合の場（2026-11-18、ユーザーの指定）: 拳の試合の標準のリングは人の大きさで 5.3 m（以前の 3.4 m と 7.3 m の中間）、ラウンドは両者のコーナーから始まる。ダウンを数えている間、
/// 立っている側はニュートラルコーナーへ下がって待ち、倒れた側はその場で立つ（起き上がりながら相手へ詰めない）。
/// </summary>
[TestClass]
public sealed class CornerAndDownTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void TheStandardRing_Is5Point3MetresInHumanScale_BetweenTheOldAndTheTooLargeOne()
    {
        Assert.AreEqual(5.3f, RingLayout.Boxing.Size / FighterTuning.Default.Scale, 0.01f);
        Assert.IsGreaterThan(RingLayout.DrillBoxing.Size, RingLayout.Boxing.Size);
        Assert.IsLessThan(7.3f * FighterTuning.Default.Scale, RingLayout.Boxing.Size);
        Assert.AreEqual(RingLayout.Boxing.Size, RingLayout.Weapons.Size, "weapon bouts use the same ring");
    }

    [TestMethod]
    public void TheRound_StartsFromTheCorners_FacingEachOtherAcrossTheDiagonal()
    {
        using var game = StillGame();
        var layout = game.Layout;
        Assert.IsLessThan(0.05f, Flat(game.Player.PelvisPosition, layout.Corner(red: true)), "red in the near-left corner");
        Assert.IsLessThan(0.05f, Flat(game.Cpu.PelvisPosition, layout.Corner(red: false)), "blue in the far-right corner");
        var across = Vector3.Normalize((game.Cpu.PelvisPosition - game.Player.PelvisPosition) with { Y = 0f });
        Assert.IsGreaterThan(0.95f, Vector3.Dot(game.Player.Frame.Forward, across));
        Assert.IsGreaterThan(0.95f, Vector3.Dot(game.Cpu.Frame.Forward, -across));

        // ドリル・リーグ（コーナーから始めない既定の形）は、今までどおり中央で StartGap だけ離れる。
        using var drill = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(Still, s), s => new BoxerBot(Still, s), Ring: RingLayout.Boxing));
        Assert.AreEqual(RingLayout.Boxing.StartGap, Flat(drill.Player.PelvisPosition, drill.Cpu.PelvisPosition), 0.05f);
    }

    [TestMethod]
    public void DuringTheCount_TheStandingFighterWaitsInANeutralCorner_AndTheDownedOneRisesWhereItFell()
    {
        using var game = StillGame();
        // 2 人を中央で向き合わせるまで歩かせる（コーナーから始まるので）。
        for (var t = 0f; t < 6f; t += Dt)
        {
            game.Step(default, Dt);
        }

        game.Cpu.Concussion = 0.95f;
        Vector3? fellAt = null;
        var counted = 0f;
        var drift = 0f;
        Vector3? corner = null;
        var closest = float.MaxValue;
        for (var t = 0f; t < 20f; t += Dt)
        {
            game.Step(default, Dt);
            if (game.Cpu.IsKnockedDown)
            {
                counted += Dt;
                corner ??= game.Layout.NeutralCorner(game.Cpu.PelvisPosition); // 数え始めの倒れた所からいちばん遠いニュートラルコーナー
                closest = MathF.Min(closest, Flat(game.Player.PelvisPosition, corner.Value));
                // 数え始めて 1 秒後（倒れきった所）から、立ち上がるまでに腰が水平に動いた距離。
                if (counted >= 1f)
                {
                    fellAt ??= game.Cpu.PelvisPosition;
                    drift = MathF.Max(drift, Flat(game.Cpu.PelvisPosition, fellAt.Value));
                }
            }
            else if (fellAt is not null)
            {
                break; // 立ち直った
            }
        }

        Console.WriteLine($"counted {counted:F1}s, downed drift {drift:F3} m, standing fighter came within {closest:F3} m of the neutral corner");
        Assert.IsNotNull(fellAt, "the downed fighter was counted");
        Assert.IsLessThan(0.3f, closest, "the standing fighter went to the far neutral corner");
        Assert.IsLessThan(0.15f, drift, "the downed fighter gets up where it fell, without stumbling toward the opponent");
    }

    private static BotStyle Still => BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };

    private static BoxingGame StillGame()
        => new(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(Still, s), s => new BoxerBot(Still, s)).StartingAtCorners());

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
