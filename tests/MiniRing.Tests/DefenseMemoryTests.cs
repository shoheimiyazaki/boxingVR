using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>守りの効き目の記憶（docs/LEARNING_PLAN.md の B1）。</summary>
[TestClass]
public sealed class DefenseMemoryTests
{
    private static BotStyle Style(float prior = 4f) => BotCatalog.Style("counter") with { LearnDefense = true, LearnPrior = prior };

    [TestMethod]
    public void Prior_IsTheDrillEffect()
    {
        var style = Style().Validated();
        var memory = new DefenseMemory(style);
        foreach (var defense in DefenseBook.Techniques)
        {
            var expected = Math.Clamp(style.Defenses.Of(defense).Against(PunchKind.Straight), 0.05f, 0.95f);
            Assert.AreEqual(expected, memory.Mean(PunchKind.Straight, defense), 1e-5f, defense.ToString());
            Assert.AreEqual(0f, memory.Experience(PunchKind.Straight, defense), 1e-5f);
        }
    }

    /// <summary>守れなかった（報酬 0）が続くと効き目の見込みが下がり、守れた（報酬 1）が続くと上がる。他の組は変わらない。</summary>
    [TestMethod]
    public void Update_MovesOnlyThatPair()
    {
        var memory = new DefenseMemory(Style().Validated());
        var duck = memory.Mean(PunchKind.Hook, Defense.Duck);
        var slip = memory.Mean(PunchKind.Straight, Defense.Slip);
        var block = memory.Mean(PunchKind.Straight, Defense.Block);
        for (var i = 0; i < 4; i++)
        {
            memory.Update(PunchKind.Straight, Defense.Slip, 0f);
            memory.Update(PunchKind.Straight, Defense.Block, 1f);
        }

        Assert.IsLessThan(slip, memory.Mean(PunchKind.Straight, Defense.Slip));
        Assert.IsGreaterThan(block, memory.Mean(PunchKind.Straight, Defense.Block));
        Assert.AreEqual(duck, memory.Mean(PunchKind.Hook, Defense.Duck), 1e-6f);
        Assert.AreEqual(4f, memory.Experience(PunchKind.Straight, Defense.Slip), 1e-4f);
    }

    /// <summary>事前の強さが大きい（頑固な）記憶ほど、同じ経験で見込みが動かない。</summary>
    [TestMethod]
    public void StrongPrior_ChangesLess()
    {
        var open = new DefenseMemory(Style(prior: 2f).Validated());
        var stubborn = new DefenseMemory(Style(prior: 20f).Validated());
        var start = open.Mean(PunchKind.Straight, Defense.Slip);
        for (var i = 0; i < 4; i++)
        {
            open.Update(PunchKind.Straight, Defense.Slip, 0f);
            stubborn.Update(PunchKind.Straight, Defense.Slip, 0f);
        }

        Assert.IsGreaterThan(start - stubborn.Mean(PunchKind.Straight, Defense.Slip), start - open.Mean(PunchKind.Straight, Defense.Slip));
    }

    /// <summary>抜く値は種が同じなら同じ（決定的）で、平均は事後の平均に近い。</summary>
    [TestMethod]
    public void Sample_IsDeterministic_AndCentredOnTheMean()
    {
        var memory = new DefenseMemory(Style().Validated());
        memory.Update(PunchKind.Straight, Defense.Slip, 1f);
        memory.Update(PunchKind.Straight, Defense.Slip, 0.3f);
        var a = new Random(11);
        var b = new Random(11);
        var sum = 0f;
        const int Count = 4000;
        for (var i = 0; i < Count; i++)
        {
            var x = memory.Sample(PunchKind.Straight, Defense.Slip, a);
            Assert.AreEqual(x, memory.Sample(PunchKind.Straight, Defense.Slip, b));
            Assert.IsTrue(x is >= 0f and <= 1f);
            sum += x;
        }

        Assert.AreEqual(memory.Mean(PunchKind.Straight, Defense.Slip), sum / Count, 0.02f);
    }

    /// <summary>効き目の小さい組（事前の α &lt; 1: ガンマの形 &lt; 1 の枝）からも、0..1 の値が決定的に抜ける。</summary>
    [TestMethod]
    public void Sample_SmallShape_StaysInRange()
    {
        var style = Style().Validated();
        var memory = new DefenseMemory(style);
        var weak = DefenseBook.Techniques.First(d => style.Defenses.Of(d).Against(PunchKind.Uppercut) <= 0.05f);
        var random = new Random(3);
        var sum = 0f;
        for (var i = 0; i < 2000; i++)
        {
            var x = memory.Sample(PunchKind.Uppercut, weak, random);
            Assert.IsTrue(x is >= 0f and <= 1f && float.IsFinite(x));
            sum += x;
        }

        Assert.AreEqual(0.05f, sum / 2000f, 0.02f);
    }

    /// <summary>新しいスタイルの値（配分・学び）は JSON を行き来しても変わらない。</summary>
    [TestMethod]
    public void NewStyleFields_RoundTripThroughJson()
    {
        var style = BotCatalog.Style("counter") with { PaceKnee = 0.4f, PaceDrop = 0.3f, LearnDefense = true, LearnPrior = 7f, LearnDamageScale = 12f };
        var back = BotStyle.FromJson(style.ToJson());
        Assert.AreEqual(0.4f, back.PaceKnee);
        Assert.AreEqual(0.3f, back.PaceDrop);
        Assert.IsTrue(back.LearnDefense);
        Assert.AreEqual(7f, back.LearnPrior);
        Assert.AreEqual(12f, back.LearnDamageScale);
    }

    /// <summary>
    /// 試合の中で学ぶ: 学ぶボットは守るたびに記憶を更新し、ラウンドが変わって作り直されても同じ記憶を引き継ぐ（BoxingGame → CarryOver）。
    /// </summary>
    [TestMethod]
    public void LearningBot_UpdatesMemory_AndCarriesItAcrossRounds()
    {
        var learner = Style() with { Attacks = false, ClinchRate = 0f, ThrowRate = 0f };
        var bots = new List<BoxerBot>();
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s =>
        {
            var bot = new BoxerBot(learner, s);
            bots.Add(bot);
            return bot;
        }));
        // ボットが作り直されるのは、K.O. などでラウンドを始め直したとき（テレビ中継の決まりのラウンドの区切りでは作り直さない）。
        // ここでは 10 秒ごとに始め直しを頼んで、その時と同じ道を通す。
        for (var round = 0; round < 3; round++)
        {
            for (var t = 0f; t < 10f; t += 1f / 60f)
            {
                game.Step(default, 1f / 60f);
            }

            game.Step(new BoxingInput(default, ResetRequested: true), 1f / 60f);
        }

        Assert.IsGreaterThanOrEqualTo(2, bots.Count, "ラウンドが変わってボットが作り直された");
        var memory = bots[0].Memory!;
        Assert.IsTrue(bots.All(b => ReferenceEquals(b.Memory, memory)), "同じ記憶を引き継ぐ");
        var experience = Enum.GetValues<PunchKind>().Sum(k => DefenseBook.Techniques.Sum(d => memory.Experience(k, d)));
        Console.WriteLine($"bots {bots.Count}, experience {experience:0.0}");
        Assert.IsGreaterThan(0f, experience, "守った結果を覚えた");
    }

    /// <summary>手数の配分（A1）: スタミナが膝より上なら手数はそのまま、0 で (1 − PaceDrop) 倍、その間は単調に下がる。</summary>
    [TestMethod]
    public void Paced_IsFullAboveKnee_AndDropsMonotonically()
    {
        var style = (BotCatalog.Style("swarmer") with { PaceDrop = 0.6f }).Validated();
        Assert.AreEqual(style.Aggression, BoxerBot.Paced(style, 1f), 1e-6f);
        Assert.AreEqual(style.Aggression, BoxerBot.Paced(style, style.PaceKnee), 1e-6f);
        Assert.AreEqual(style.Aggression * (1f - style.PaceDrop), BoxerBot.Paced(style, 0f), 1e-6f);
        var last = float.PositiveInfinity;
        for (var s = 0f; s <= 1f; s += 0.05f)
        {
            var value = BoxerBot.Paced(style, 1f - s);
            Assert.IsLessThanOrEqualTo(last + 1e-6f, value);
            last = value;
        }
    }

    /// <summary>学ばないボット（既定）は記憶を持たない（選び方は以前と同じ）。</summary>
    [TestMethod]
    public void NonLearningBot_HasNoMemory()
    {
        Assert.IsNull(new BoxerBot(BotCatalog.Style("counter"), 1).Memory);
        Assert.IsNotNull(new BoxerBot(Style(), 1).Memory);
    }
}
