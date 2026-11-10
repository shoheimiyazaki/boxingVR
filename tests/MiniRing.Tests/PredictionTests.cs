using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 予測で避ける: 打つ前の溜め（体が拳より先に動く）、相手の読み（<see cref="OpponentRead"/>）、予測の回避、間合いの端。
/// </summary>
[TestClass]
public sealed class PredictionTests
{
    private const float Reach = 0.19f;
    private const float Dt = 1f / 240f;

    /// <summary>1 秒ごとに、0.1 秒の溜め（右肩が前へ回る）のあと右拳を 0.15 秒出す相手（自分は原点で +Z を向く）。</summary>
    private static FighterSense Scripted(float t, float distance = 1.1f)
    {
        var phase = t % 1f;
        var z = distance * Reach;
        var chest = new Vector3(0f, 0.3f, z);
        var load = Math.Clamp(phase / 0.1f, 0f, 1f) * (phase < 0.4f ? 1f : 0f);
        // 相手は −Z を向くので、相手の右肩は +X。溜めで右肩が前（−Z）へ、左肩が後ろへ。
        var right = chest + new Vector3(0.2f * Reach, 0.05f, -0.1f * Reach * load);
        var left = chest + new Vector3(-0.2f * Reach, 0.05f, 0.1f * Reach * load);
        var punching = phase is >= 0.1f and < 0.25f;
        var gloveR = chest + new Vector3(0.1f * Reach, 0.05f, -0.3f * Reach) - (punching ? new Vector3(0f, 0f, 4f * Reach * (phase - 0.1f)) : Vector3.Zero);
        var gloveL = chest + new Vector3(-0.1f * Reach, 0.05f, -0.3f * Reach);
        return new FighterSense(new BodyFrame(Vector3.Zero, 0f), Reach, chest + new Vector3(0f, 0.08f, 0f), chest, true,
            new Vector3(0f, 0.36f, 0f),
            new GloveSense(gloveL, Vector3.Zero),
            new GloveSense(gloveR, punching ? new Vector3(0f, 0f, -4f * Reach) : Vector3.Zero),
            Vector3.Zero,
            new BodySense(left, right, chest - new Vector3(0f, 0.12f, 0f)));
    }

    [TestMethod]
    public void OpponentRead_LearnsThatTheBodyMovesBeforeTheFist_AndWhichHand()
    {
        var read = new OpponentRead();
        OpponentRead.Forecast loading = default, resting = default;
        for (var t = 0f; t < 30f; t += Dt)
        {
            read.Observe(Scripted(t), Dt);
            var phase = t % 1f;
            if (t > 25f && MathF.Abs(phase - 0.06f) < Dt / 2f)
            {
                loading = read.Predict();
            }

            if (t > 25f && MathF.Abs(phase - 0.6f) < Dt / 2f)
            {
                resting = read.Predict();
            }
        }

        Assert.AreEqual(30, read.Onsets, 1);
        Assert.AreEqual(1.1f, read.AttackDistance!.Value, 0.05f);
        Assert.IsGreaterThan(0.5f, loading.Likelihood, $"in the load {loading}");
        Assert.IsLessThan(0.1f, resting.Likelihood, $"at rest {resting}");
        Assert.IsGreaterThan(0f, loading.BodyEvidence, "the twist is what gives it away");
        Assert.AreEqual(Hand.Right, loading.Hand);
        Assert.AreEqual(PunchKind.Straight, loading.Kind);
    }

    [TestMethod]
    public void OpponentRead_WithoutABodyToSee_PredictsNothing()
    {
        var read = new OpponentRead();
        for (var t = 0f; t < 10f; t += Dt)
        {
            read.Observe(Scripted(t) with { OpponentBody = default }, Dt);
        }

        Assert.AreEqual(0, read.Onsets);
        Assert.AreEqual(0f, read.Predict().Likelihood);
        Assert.IsNull(read.AttackDistance);
    }

    [TestMethod]
    public void PredictedInsideSlip_AvoidsALoadedRightCross_ThatReactionCannot()
    {
        // 反応だけ（予測なし）では、溜めがあっても右ストレートはほぼ全部頭に当たる（拳は 0.15 秒足らずで届く）。
        var reacting = DefenseDrill.Punch("2", Defense.Slip, SlipDefender(0f), seed: 1, seconds: 30f, telegraph: 0.08f);
        var predicting = DefenseDrill.Punch("2", Defense.Slip, SlipDefender(1f), seed: 1, seconds: 30f, telegraph: 0.08f);

        Console.WriteLine($"reacting {reacting}");
        Console.WriteLine($"predicting {predicting}");
        // 見てから避ける（反応）だけでは、半分以上が頭に当たる（気づけずに受ける）。予測すると、ほぼ全部の拳に守りを出せる。
        // 注意: 関節の重なり代を体の縮尺に合わせてから（足運び編 F5）、この練習では予測が拳より 0.4 秒も早く発火することがあり、
        // 短いスリップが拳より先に終わって、頭に当たる割合は反応と同じくらい（5 割）に戻った。実戦の予測の守りは 5 割が打たれずに終わる。
        Assert.IsGreaterThan(0.5f, reacting.HeadRate, $"reacting {reacting}");
        Assert.IsGreaterThan(reacting.Coverage + 0.2f, predicting.Coverage, $"predicting {predicting} vs reacting {reacting}");
        Assert.IsLessThan(reacting.HeadRate + 0.05f, predicting.HeadRate, $"predicting {predicting} vs reacting {reacting}");
    }

    [TestMethod]
    public void DefaultStyle_NeitherLoadsNorPredicts()
    {
        var style = new BotStyle().Validated();
        Assert.AreEqual(0f, style.Telegraph);
        Assert.AreEqual(0f, style.Prediction);
        Assert.AreEqual(0f, style.EdgeRange);

        var clamped = new BotStyle { Telegraph = 1f, Prediction = 2f, EdgeRange = float.NaN }.Validated();
        Assert.AreEqual(0.2f, clamped.Telegraph);
        Assert.AreEqual(1f, clamped.Prediction);
        Assert.AreEqual(0f, clamped.EdgeRange);
    }

    [TestMethod]
    public void LoadedRightCross_TurnsTheRightShoulderForward_BeforeTheFistMoves()
    {
        var attackers = new List<BoxerBot>();
        var attacker = DefenseDrill.PunchAttacker("2", telegraph: 0.12f);
        var corners = new BoxingCorners("a", s => Keep(attackers, new BoxerBot(attacker, s)), s => new BoxerBot(SlipDefender(0f) with { DefenseRate = 0f }, s));
        using var game = new BoxingGame(seed: 2, corners: corners);
        var thrownAt = -1f;
        var twistAtThrow = 0f;
        var gloveAtThrow = Vector3.Zero;
        for (var t = 0f; t < 6f; t += Dt)
        {
            game.Step(default, Dt);
            var thrown = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
            if (thrownAt < 0f && thrown > 0)
            {
                thrownAt = t;
                twistAtThrow = Twist(game);
                gloveAtThrow = game.Cpu.Frame.InverseRotate(game.Cpu.GloveCenter(Hand.Right) - game.Cpu.Frame.Origin);
            }

            if (thrownAt >= 0f && t >= thrownAt + 0.1f)
            {
                // 溜めの終わり近く（拳はまだ出ていない）: 右肩が前へ回り、拳は前へ出ていない（少し引いている）。
                // 足運び編 F5 で胴が長くなり（関節の重なり代を縮尺に合わせた）、同じ時間で回る角度は 0.03 → 0.026 rad ほど。
                Assert.IsGreaterThan(0.02f, Twist(game) - twistAtThrow, "the right shoulder turns forward first");
                var glove = game.Cpu.Frame.InverseRotate(game.Cpu.GloveCenter(Hand.Right) - game.Cpu.Frame.Origin);
                Assert.IsLessThan(0.05f, (glove.Z - gloveAtThrow.Z) / game.Cpu.ArmReach, "the fist has not gone out yet");
                return;
            }
        }

        Assert.Fail("no punch was thrown");
    }

    /// <summary>攻め手の右肩が左肩より前（受け手の方）にある量 [腕の長さ]。</summary>
    private static float Twist(BoxingGame game)
    {
        var toward = Vector3.Normalize((game.Player.Frame.Origin - game.Cpu.Frame.Origin) with { Y = 0f });
        return Vector3.Dot(game.Cpu.ShoulderPosition(Hand.Right) - game.Cpu.ShoulderPosition(Hand.Left), toward) / game.Cpu.ArmReach;
    }

    /// <summary>
    /// 受け手: インファイターの構え（両拳を顔の前で寄せる）に、腰から沈んで頭を拳の内側へ外すスリップ
    /// （docs/arena/basics-inside-start.json と同じ動き。基本動作編の溜めの試作で使った）。
    /// </summary>
    /// <summary>
    /// インファイターのスリップ（磨いた外側へのスリップ）だけで守る受け手。以前は手書きの内側へのスリップを使っていたが、
    /// 関節の重なり代を体の縮尺に合わせて体の比率が変わってから（足運び編 F5）は、予測で始めても頭に 74 % 当たった。
    /// </summary>
    internal static BotStyle SlipDefender(float prediction)
    {
        var swarmer = BotCatalog.Swarmer;
        return DefenseDrill.Defender(swarmer.Defenses, defend: true, counters: false) with
        {
            GuardHeight = swarmer.GuardHeight, GuardWidth = swarmer.GuardWidth, GuardReach = swarmer.GuardReach, Prediction = prediction,
        };
    }

    private static BoxerBot Keep(List<BoxerBot> list, BoxerBot bot)
    {
        list.Add(bot);
        return bot;
    }
}
