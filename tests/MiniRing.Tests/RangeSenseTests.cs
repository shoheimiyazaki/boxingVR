using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>間合いの層（docs/LEARNING_PLAN.md の R）: 危険度は位置関係と速さだけから連続に決まる。</summary>
[TestClass]
public sealed class RangeSenseTests
{
    private const float Reach = 0.188f;

    /// <summary>自分の頭が原点の高さ、相手の胸が前方 <paramref name="gap"/> [m] にいる状況（相手の肩 = 胸 + 0.25 × 腕の長さ上）。</summary>
    private static FighterSense Sense(float gap, Vector3 opponentVelocity = default, bool alive = true)
    {
        var head = new Vector3(0f, 1f, 0f);
        var chest = new Vector3(0f, 1f - (0.25f * Reach), gap);
        return new FighterSense(new BodyFrame(Vector3.Zero, 0f), Reach, chest + new Vector3(0f, 0.3f, 0f), chest, alive, head, OpponentVelocity: opponentVelocity);
    }

    private static float Danger(float gap, Vector3 velocity = default, bool alive = true)
    {
        var range = new RangeSense();
        var sense = Sense(gap, velocity, alive);
        range.Update(sense, sense.SelfHead, attacking: false, BotCatalog.Boxer, 1f / 60f);
        return range.Danger;
    }

    [TestMethod]
    public void Danger_IsZeroFarAway_OneUpClose_AndMonotonicBetween()
    {
        var threat = BotCatalog.Boxer.RangeThreat * Reach;
        Assert.AreEqual(0f, Danger(threat + (RangeSense.Width * Reach) + 0.01f), 1e-6f);
        Assert.AreEqual(1f, Danger(threat - (RangeSense.Width * Reach) - 0.01f), 1e-6f);
        var last = -1f;
        for (var gap = threat + (0.3f * Reach); gap >= threat - (0.3f * Reach); gap -= 0.002f)
        {
            var d = Danger(gap);
            Assert.IsGreaterThanOrEqualTo(last - 1e-6f, d, $"gap {gap}");
            last = d;
        }
    }

    /// <summary>相手が詰めてくると、反応する間に詰められる分だけ危ない所が遠くまで伸びる（離れていくときは伸びない）。</summary>
    [TestMethod]
    public void Closing_ExtendsTheDangerZone()
    {
        var gap = (BotCatalog.Boxer.RangeThreat + 0.2f) * Reach; // 止まっている相手なら届かない
        Assert.AreEqual(0f, Danger(gap), 1e-6f);
        Assert.IsGreaterThan(0.5f, Danger(gap, new Vector3(0f, 0f, -0.5f)), "詰めてくる相手（0.5 m/s）は届く");
        Assert.AreEqual(0f, Danger(gap, new Vector3(0f, 0f, 0.5f)), 1e-6f, "離れていく相手は伸びない");
    }

    /// <summary>さらされ: 打っていない間は危険度を積み、打っている間は積まない。倒れた相手は危なくない。</summary>
    [TestMethod]
    public void Exposure_GrowsOnlyWhileNotAttacking()
    {
        var sense = Sense(0.5f * Reach);
        var idle = new RangeSense();
        var attacking = new RangeSense();
        for (var i = 0; i < 60; i++)
        {
            idle.Update(sense, sense.SelfHead, attacking: false, BotCatalog.Boxer, 1f / 60f);
            attacking.Update(sense, sense.SelfHead, attacking: true, BotCatalog.Boxer, 1f / 60f);
        }

        Assert.IsGreaterThan(0.5f, idle.Exposure);
        Assert.AreEqual(0f, attacking.Exposure, 1e-6f);
        Assert.AreEqual(0f, Danger(0.5f * Reach, alive: false), 1e-6f);
    }

    /// <summary>
    /// 普段は届かない所に立つボット（<see cref="BotStyle.RangeKeepOut"/> = 1・<see cref="BotStyle.RangeExit"/> = 1）は、同じ相手・同じ種で、
    /// 危ない所にいる時間が短い（打つときだけ入って出る）。
    /// </summary>
    [TestMethod]
    public void KeepOut_SpendsLessTimeInDanger()
    {
        float DangerTime(BotStyle style)
        {
            var total = 0f;
            foreach (var seed in new[] { 1, 2 })
            {
                total += BotArena.Run(new BoutSpec(Contender.Of(style), Contender.Named("boxer"), Seed: seed, Seconds: 20f)).Red.Stats.IdleDangerTime;
            }

            return total;
        }

        var off = DangerTime(BotCatalog.Style("boxer"));
        var keep = DangerTime(BotCatalog.Style("boxer") with { RangeKeepOut = 1f, RangeExit = 1f });
        Console.WriteLine($"idle danger time: off {off:0.0}s, keep out {keep:0.0}s");
        Assert.IsLessThan(0.8f * off, keep);
    }

    /// <summary>
    /// 危ない所に居続けることを打って解消するボット（<see cref="BotStyle.RangePress"/>）は、同じ相手・同じ種で、打つ数が増え、
    /// 打たずに危ない所にいる時間が減る（下がらずに、攻めで居続けを解消する）。1 組だけでは試合の流れのずれで打つ数が逆転した
    /// （ボクサー同士 2 種: 38 → 29）ので、相手を 3 スタイルにして合わせて見る（総当たりでは 81 → 96、R3）。
    /// </summary>
    [TestMethod]
    public void Press_ThrowsMore_AndIdlesLessInDanger()
    {
        (int Thrown, float Idle) Run(BotStyle style)
        {
            var thrown = 0;
            var idle = 0f;
            foreach (var opponent in new[] { "boxer", "swarmer", "jabber" })
            {
                foreach (var seed in new[] { 1, 2 })
                {
                    var stats = BotArena.Run(new BoutSpec(Contender.Of(style), Contender.Named(opponent), Seed: seed, Seconds: 20f)).Red.Stats;
                    thrown += stats.Techniques.Values.Sum(c => c.Thrown);
                    idle += stats.IdleDangerTime;
                }
            }

            return (thrown, idle);
        }

        var off = Run(BotCatalog.Style("boxer"));
        var press = Run(BotCatalog.Style("boxer") with { RangePress = 2f });
        Console.WriteLine($"thrown {off.Thrown} -> {press.Thrown}, idle danger {off.Idle:0.0}s -> {press.Idle:0.0}s");
        Assert.IsGreaterThan(off.Thrown, press.Thrown);
        Assert.IsLessThan(off.Idle, press.Idle);
    }

    /// <summary>間合いの層の使い方の値が 0（既定）なら、ボットの選び方の乱数の引き方は変わらない（同じ種で同じ試合）。</summary>
    [TestMethod]
    public void RangeOff_IsTheDefault()
    {
        var style = BotCatalog.Boxer.Validated();
        Assert.AreEqual(0f, style.RangeGuard);
        Assert.AreEqual(0f, style.RangeHeadMove);
        Assert.AreEqual(0f, style.RangeGuess);
        Assert.AreEqual(0f, style.RangeExit);
        Assert.AreEqual(0f, style.RangeKeepOut);
        Assert.AreEqual(0f, style.RangePress);
    }
}
