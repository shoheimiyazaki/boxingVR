using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 弱った相手を詰める層の読み（<see cref="HurtRead"/>）: 見えている構えの下がりと自分の手応えだけから、相手の開きを連続に出す。
/// </summary>
[TestClass]
public sealed class HurtReadTests
{
    private const float Reach = 1.69f;
    private const float Dt = 1f / 60f;

    /// <summary>相手の頭の高さ 4.2 m、両拳を頭から <paramref name="drop"/> × 腕の長さ下に構えた見え方。</summary>
    private static FighterSense Sense(float drop, float gloveDepth = 1.69f / Reach)
    {
        var head = new Vector3(0f, 4.225f, 2.254f);
        var gloveY = head.Y - (drop * Reach);
        return new FighterSense(new BodyFrame(Vector3.Zero, 0f), Reach, head, head - new Vector3(0f, 0.8451f, 0f), OpponentAlive: true,
            OpponentLeft: new GloveSense(new Vector3(0.2817f, gloveY, (gloveDepth - 0.4f) * Reach), Vector3.Zero), OpponentRight: new GloveSense(new Vector3(-0.2817f, gloveY, (gloveDepth - 0.4f) * Reach), Vector3.Zero),
            OpponentArmReach: Reach);
    }

    private static void Run(HurtRead read, float drop, float seconds)
    {
        var style = new BotStyle();
        for (var t = 0f; t < seconds; t += Dt)
        {
            read.Observe(Sense(drop), style, Dt);
        }
    }

    [TestMethod]
    public void SteadyGuard_ReadsClosed_EvenIfTheGuardIsLowByStyle()
    {
        var high = new HurtRead();
        var low = new HurtRead();
        Run(high, 0.26f, 8.392f);
        Run(low, 0.40f, 8.392f);

        Assert.IsLessThan(0.02f, high.Opening);
        Assert.IsLessThan(0.02f, low.Opening, "普段から低い構えの相手を、弱ったと取り違えない（その相手の普段と比べる）");
    }

    [TestMethod]
    public void GuardSagsLikeAFaintingBody_OpensContinuously()
    {
        // 健康 0.26 → 意識が薄れ始め 0.43・深く 0.49（HurtCueProbe の測定）。
        var openings = new List<float>();
        foreach (var sag in new[] { 0.30f, 0.43f, 0.49f })
        {
            var read = new HurtRead();
            Run(read, 0.26f, 8.392f);
            Run(read, sag, 1.678f);
            openings.Add(read.Opening);
        }

        Console.WriteLine(string.Join(", ", openings.Select(o => o.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))));
        Assert.IsLessThan(0.05f, openings[0], "健康な揺らぎ（+0.04）では開かない");
        Assert.IsGreaterThan(0.6f, openings[1], "薄れ始めの下がりで大きく開く");
        Assert.IsGreaterThanOrEqualTo(openings[1], openings[2], "深いほど開く（連続）");
    }

    [TestMethod]
    public void LandedBlows_Open_AndTheFeelingFades()
    {
        var read = new HurtRead();
        Run(read, 0.26f, 5.035f);
        read.Landed(15f);
        read.Landed(15f);
        Run(read, 0.26f, 1.678f);
        var right = read.Opening;
        Run(read, 0.26f, 16.78f);

        Assert.IsGreaterThan(0.4f, right, "重いのを 2 発当てた手応えで開く");
        Assert.IsLessThan(0.05f, read.Opening, "手応えは忘れる");
    }

    [TestMethod]
    public void DefaultStyle_UsesTheMeasuredFinishLayer()
    {
        var style = new BotStyle();
        Assert.AreEqual(1.5f, style.FinishTempo, "docs/arena/finish_*.md で採った値");
        Assert.AreEqual(1.5f, style.FinishPress);
        Assert.AreEqual(0.6f, style.FinishSpend);
        Assert.AreEqual(1.5f, BotCatalog.Swarmer.FinishPress, "スタイルの JSON に書かなければ既定値");
    }

    /// <summary>
    /// 立ち回り: 相手の構えが落ちると、層を入れたボットは足の運びが目指す距離を打つ間合いへ縮める（層なしは普段の立ち位置のまま）。
    /// 歩いて詰められるか（体が付いてくるか）は試合で測る（docs/arena/finish_*.md）。
    /// </summary>
    [TestMethod]
    public void SaggingOpponent_TheFinishLayerAimsCloser()
    {
        static (float Before, float After) Desired(BotStyle style)
        {
            var bot = new BoxerBot(style with { Attacks = false }, seed: 3);
            var before = 0f;
            for (var t = 0f; t < 10.07f; t += Dt)
            {
                var sense = Sense(t < 6.714f ? 0.26f : 0.47f) with { OpponentChest = new Vector3(0f, 3.38f, 1.5f * Reach), OpponentHead = new Vector3(0f, 4.225f, 1.5f * Reach), SelfHead = new Vector3(0f, 4.225f, 0f) };
                bot.Read(sense, Dt);
                if (t < 6.714f)
                {
                    before = bot.ProbeState.Desired;
                }
            }

            return (before, bot.ProbeState.Desired);
        }

        var on = Desired(new BotStyle());
        var off = Desired(new BotStyle() with { FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f });
        Console.WriteLine($"desired distance (×reach) before/after the guard drops: layer on {on.Before:0.00}/{on.After:0.00}, off {off.Before:0.00}/{off.After:0.00}");
        Assert.AreEqual(off.Before, on.Before, 1e-4f, "構えが普段どおりなら層は効かない");
        Assert.AreEqual(off.Before, off.After, 0.02f, "層なしは立ち位置を変えない");
        Assert.IsLessThan(on.Before - 0.15f, on.After, "層ありは打つ間合いへ詰める");
    }

    [TestMethod]
    public void VisibleTirednessAndInjury_OpenContinuously_OnlyWithTheEducatedWeights()
    {
        // 2026-11-24: 見える息の上がり（TiredLook）・見えている傷み（折れた腕）も開きの手がかり。係数（教育）が 0 なら従来どおり開かない。
        static float OpenAfter(float winded, float injury, BotStyle style)
        {
            var read = new HurtRead();
            for (var t = 0f; t < 3f; t += Dt)
            {
                read.Observe(Sense(0.26f) with { OpponentWinded = winded, OpponentInjury = injury }, style, Dt);
            }

            return read.Opening;
        }

        var plain = new BotStyle();
        var taught = new BotStyle { HurtWinded = 1f, HurtInjury = 1f };
        Assert.IsLessThan(0.02f, OpenAfter(0.8f, 0.6f, plain), "係数 0 なら息・傷みでは開かない（従来の判断）");
        var weak = OpenAfter(0.3f, 0f, taught);
        var strong = OpenAfter(0.8f, 0f, taught);
        var hurt = OpenAfter(0.8f, 0.5f, taught);
        Assert.IsGreaterThan(0.2f, weak);
        Assert.IsGreaterThan(weak, strong, "息が上がるほど開く（連続）");
        Assert.IsGreaterThan(strong, hurt, "傷みが重なるとさらに開く");
    }
}
