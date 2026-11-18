using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 予測で避ける: 打つ前の溜め（体が拳より先に動く）、相手の読み（<see cref="OpponentRead"/>）、予測の回避、間合いの端。
/// </summary>
[TestClass]
[TestCategory("slow")]
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
        // 予測の差を測る。疲労による攻撃停止・速度低下を混ぜない。
        // 2026-11-18（PhysicsVersion 45）: 脳しんとうの係数を 100 倍にしたので、側頭への 1 発（12〜16 点）で受け手が倒れ、起き上がるまでの 9 秒ほど
        // 攻め手の拳が出ず、読み（OpponentRead）は倒れたときに覚えた拳を捨てて 30 秒の間に学び直せなかった（種 2・3 は予測の有無で結果が同じ、
        // 守りを出した割合 predicting 0.83 → 0.38。reacting は 0.21 → 0.19）。予測の差を測るテストなので、疲労と同じく脳しんとうも毎歩除く
        // （除いた後: reacting 頭 0.53・守り 0.04、predicting 頭 0.34・守り 1.05。版 43 は reacting 0.52・0.21、predicting 0.32・0.83）。
        static void KeepFresh(BoxingGame game, BoxerBot _)
        {
            game.Player.Stamina = 1f;
            game.Cpu.Stamina = 1f;
            game.Player.Concussion = 0f;
        }

        // 1 つの種では頭に当たる割合が 0.4〜0.7 の間で揺れる（PhysicsVersion 29 の後、種 1 は 0.53、種 2〜5 は 0.41・0.63・0.64・0.69）ので、
        // 5 つの種の合計で見る。判定の形（反応だけは大半が頭に当たる・予測は守りを出して頭の割合がはっきり減る）は元のまま。
        var seeds = new[] { 1, 2, 3, 4, 5 };
        var reactingRuns = new PunchDrillResult[seeds.Length];
        var predictingRuns = new PunchDrillResult[seeds.Length];
        Parallel.For(0, seeds.Length, i =>
        {
            reactingRuns[i] = DefenseDrill.Punch("2", Defense.Slip, SlipDefender(0f), seed: seeds[i], seconds: 30f, telegraph: 0.08f, observe: KeepFresh);
            predictingRuns[i] = DefenseDrill.Punch("2", Defense.Slip, SlipDefender(1f), seed: seeds[i], seconds: 30f, telegraph: 0.08f, observe: KeepFresh);
        });

        static float Rate(PunchDrillResult[] runs, Func<PunchDrillResult, int> count) => runs.Sum(count) / (float)runs.Sum(r => r.Punches);
        var reactingHead = Rate(reactingRuns, r => r.HeadHits + r.OpenHeadHits);
        var predictingHead = Rate(predictingRuns, r => r.HeadHits + r.OpenHeadHits);
        var reactingCover = Rate(reactingRuns, r => r.Defenses);
        var predictingCover = Rate(predictingRuns, r => r.Defenses);
        Console.WriteLine($"reacting head {reactingHead:0.00} cover {reactingCover:0.00}; predicting head {predictingHead:0.00} cover {predictingCover:0.00}");
        // 見てから避ける（反応）だけでは、大半が頭に当たる（気づけずに受ける）。予測すると、ほぼ全部の拳に守りを出し、頭に当たる割合がはっきり減る。
        // 相手は溜めの始めに狙いを決めるので、溜めより前に動くと動いた先を狙われる。外せるのは溜めの始め近くに動き出したときだけ（足運び編 F6）。
        // PhysicsVersion 31: 直した頭（boxer.vox）は顔の面が 1 セル（5 mm）後ろへ下がり、鼻だけが前に出るので、反応だけの頭の割合は 0.57 → 0.48、
        // 予測は 0.40 → 0.19。差（予測の効き）はむしろ大きい。下限は「半分近くが頭に当たる」に合わせる。
        Assert.IsGreaterThan(0.4f, reactingHead, "reacting: about half the punches reach the head");
        Assert.IsGreaterThan(reactingCover + 0.2f, predictingCover, $"predicting cover {predictingCover} vs reacting {reactingCover}");
        Assert.IsLessThan(reactingHead - 0.1f, predictingHead, $"predicting head {predictingHead} vs reacting {reactingHead}");
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
    public void LoadedRightCross_WindsTheRightShoulderBack_BeforeTheFistMoves()
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
                // 溜めの終わり近く（拳はまだ出ていない）: 右肩を後ろへ引いて胴のバネを縮め（攻撃編 A1。以前は前へ回していた）、拳は前へ出ていない（少し引いている）。
                // 体の動きは拳より先に出るので、相手はこの引きを読める（OpponentRead は向きによらず、拳の前の体の動きを覚える）。
                Assert.IsLessThan(-0.01f, Twist(game) - twistAtThrow, "the right shoulder winds back first"); // 溜め 0.12 秒では引ききる（0.2 秒）手前
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
