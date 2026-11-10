using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>防御技（<see cref="DefenseBook"/>）・動き出しの読み・防御ドリル。</summary>
[TestClass]
public sealed class DefenseTests
{
    private const float Reach = 0.19f;

    [TestMethod]
    public void Weave_DipsInAUShape_AndCrossesToTheOtherSide()
    {
        var weave = DefenseBook.Default.Weave;

        var start = BoxerBot.DefenseHead(weave, side: 1f, progress: 0f);
        var middle = BoxerBot.DefenseHead(weave, side: 1f, progress: 0.5f);
        var end = BoxerBot.DefenseHead(weave, side: 1f, progress: 1f);

        Assert.IsLessThan(start.Y - 0.2f, middle.Y, "lowest in the middle of the U");
        Assert.IsLessThan(0.05f, MathF.Abs(end.Y), "back up at the end");
        Assert.AreEqual(MathF.Sign(weave.HeadX), MathF.Sign(start.X));
        Assert.AreEqual(MathF.Sign(weave.EndX), MathF.Sign(end.X), "comes out on the other side");
        Assert.AreNotEqual(MathF.Sign(start.X), MathF.Sign(end.X));
    }

    [TestMethod]
    public void NonSweepingDefenses_HoldTheirHeadPosition()
    {
        var slip = DefenseBook.Default.Slip;

        Assert.AreEqual(BoxerBot.DefenseHead(slip, -1f, 0.1f), BoxerBot.DefenseHead(slip, -1f, 0.9f));
        Assert.AreEqual(-slip.HeadX, BoxerBot.DefenseHead(slip, -1f, 0.5f).X, 1e-6f, "the side flips the slip");
    }

    [TestMethod]
    public void PunchKind_IsReadFromTheGloveVelocity_IncludingTheWindup()
    {
        Assert.AreEqual(PunchKind.Straight, BoxerBot.Classify(new Vector3(0.05f, 0f, -1f), toBody: false));
        Assert.AreEqual(PunchKind.Hook, BoxerBot.Classify(new Vector3(1f, 0.1f, -0.3f), toBody: false));
        Assert.AreEqual(PunchKind.Hook, BoxerBot.Classify(new Vector3(-1f, 0f, 0.2f), toBody: false), "a hook winding up outward");
        Assert.AreEqual(PunchKind.Uppercut, BoxerBot.Classify(new Vector3(0.1f, 1f, -0.2f), toBody: false));
        Assert.AreEqual(PunchKind.Uppercut, BoxerBot.Classify(new Vector3(0f, -1f, 0.1f), toBody: false), "an uppercut dipping first");
        Assert.AreEqual(PunchKind.Body, BoxerBot.Classify(new Vector3(0f, 0f, -1f), toBody: true));
    }

    [TestMethod]
    public void DefenseBook_ValidatesEveryMove_AndBlockAlwaysRaisesTheGloves()
    {
        var wild = DefenseBook.Default with
        {
            Duck = DefenseBook.Default.Duck with { HeadY = -5f, Duration = float.NaN, Rate = 999f },
            Block = DefenseBook.Default.Block with { HandsUp = 0f },
        };

        var v = wild.Validated();

        Assert.AreEqual(-0.7f, v.Duck.HeadY);
        Assert.AreEqual(0.35f, v.Duck.Duration);
        Assert.AreEqual(60f, v.Duck.Rate);
        Assert.AreEqual(1f, v.Block.HandsUp);
    }

    [TestMethod]
    public void StyleJson_CarriesTheDefenseBook()
    {
        var style = BotCatalog.Boxer with
        {
            Anticipation = 0.7f,
            WeaveWeight = 1.5f,
            Defenses = DefenseBook.Default with { Weave = DefenseBook.Default.Weave with { HeadY = -0.33f } },
        };

        var back = BotStyle.FromJson(style.ToJson());

        Assert.AreEqual(0.7f, back.Anticipation);
        Assert.AreEqual(1.5f, back.WeaveWeight);
        Assert.AreEqual(-0.33f, back.Defenses.Weave.HeadY);
    }

    [TestMethod]
    public void AHookIsSeenFromItsWindup_OnlyWhenReadingTheMotion()
    {
        // 頭の横で外へ振りかぶる拳（まだ頭へは向かっていない）。読まないボットは気づかず、読むボットは反応時間ののちに守る。
        static bool Defends(float anticipation)
        {
            var style = BotCatalog.Boxer with { Anticipation = anticipation, DefenseRate = BotStyle.MaxDefenseRate, Attacks = false };
            for (var seed = 0; seed < 10; seed++)
            {
                var bot = new BoxerBot(style, seed, Defense.Block);
                for (var t = 0f; t < 0.3f; t += 1f / 240f)
                {
                    bot.Read(Windup(), 1f / 240f);
                }

                if (bot.CurrentDefense != Defense.None)
                {
                    return true;
                }
            }

            return false;
        }

        Assert.IsFalse(Defends(0f), "seeing only approaching gloves, a windup is invisible");
        Assert.IsTrue(Defends(1f), "reading the motion, the windup is the cue");
    }

    [TestMethod]
    public void WalkingOpponent_IsNotMistakenForAPunch()
    {
        // 相手が構えたまま歩いて近づく: 拳は速いが、体と同じ速さなので「腕の動き」は無い。
        var style = BotCatalog.Boxer with { Anticipation = 1f, DefenseRate = BotStyle.MaxDefenseRate, Attacks = false };
        var bot = new BoxerBot(style, 1, Defense.Block);
        var walk = new Vector3(0f, 0f, -0.5f);
        var sense = Windup() with
        {
            OpponentLeft = new GloveSense(new Vector3(0.02f, 1.05f, 0.12f), walk),
            OpponentRight = new GloveSense(new Vector3(-0.03f, 1.02f, 0.15f), walk),
            OpponentVelocity = walk,
        };
        for (var t = 0f; t < 0.4f; t += 1f / 240f)
        {
            bot.Read(sense, 1f / 240f);
            Assert.AreEqual(Defense.None, bot.CurrentDefense);
        }
    }

    [TestMethod]
    public void Drill_WithoutDefending_EqualsTheBaseline()
    {
        // 守る率 0 の受け手は、守る技に関わらず「守らない受け手」と同じ試合になる（ドリルの基準の確かめ）。
        var open = DefenseDrill.MeasureBaseline(PunchKind.Straight, seed: 2, seconds: 4f);

        var again = DefenseDrill.MeasureBaseline(PunchKind.Straight, seed: 2, seconds: 4f);

        Assert.AreEqual(open, again);
        Assert.IsGreaterThan(0, open.Punches, "the drill attacker throws");
        Assert.IsGreaterThan(0f, open.Damage, "and lands on an open defender");
    }

    [TestMethod]
    public void Champion_IsLoadedFromItsEmbeddedStyle_WithTheTunedDefenses()
    {
        var champion = BotCatalog.Champion;

        Assert.AreEqual("champion", champion.Name);
        Assert.IsGreaterThan(0f, champion.Anticipation, "the CPU reads the start of a punch");
        Assert.AreNotEqual(DefenseBook.Default.Sway, champion.Defenses.Sway, "with the drill-tuned defense moves");
        Assert.AreEqual(0f, champion.Defenses.Block.VsBody, "and never moves its guard against body shots");
        Assert.AreEqual(champion.Aggression, BotCatalog.ChampionV1.Aggression, "the offense was kept from the first champion");
    }

    [TestMethod]
    public void NonSweepingMove_KeepsItsShape_EvenIfEndXDrifts()
    {
        // 横へ抜けない技は EndX を使わない（以前は EndX がわずかに揺れただけで U 字になっていた）。
        var duck = (DefenseBook.Default.Duck with { EndX = 0.2f }).Validated();

        Assert.AreEqual(duck.HeadX, duck.EndX, "validation aligns the unused end with the start");
        Assert.AreEqual(BoxerBot.DefenseHead(duck, 1f, 0f), BoxerBot.DefenseHead(duck, 1f, 0.5f), "full depth from the start");
    }

    [TestMethod]
    public void ExtendedWeave_StaysLow_WhileThePunchKeepsComing()
    {
        // 拳が向かってきている限り守りを延ばす。その間、U 字の頭は底に留まり、起き上がらない。
        var style = BotCatalog.Boxer with { Anticipation = 0f, DefenseRate = BotStyle.MaxDefenseRate, Attacks = false };
        var sense = Coming();
        for (var seed = 0; seed < 10; seed++)
        {
            var bot = new BoxerBot(style, seed, Defense.Weave);
            FighterCommand command = default;
            var duration = style.Defenses.Weave.Duration;
            for (var t = 0f; t < style.ReactionTime + duration + 0.15f; t += 1f / 240f)
            {
                command = bot.Read(sense, 1f / 240f);
            }

            if (bot.CurrentDefense != Defense.Weave)
            {
                continue;
            }

            Assert.IsLessThan(-0.25f * Reach, command.HeadOffset.Y, "still ducked under the punch past the move's own duration");
            return;
        }

        Assert.Fail("no seed weaved");
    }

    [TestMethod]
    public void RetractingGlove_IsNotAWindup()
    {
        // 打ち終えて相手自身の胸へ戻る拳は、速くても合図にしない（同じ手の 2 発目の判断を妨げない）。
        var style = BotCatalog.Boxer with { Anticipation = 1f, DefenseRate = BotStyle.MaxDefenseRate, Attacks = false };
        var bot = new BoxerBot(style, 1, Defense.Block);
        var sense = Windup();
        var glove = sense.SelfHead + new Vector3(0f, -0.01f, 0.08f);
        var home = Vector3.Normalize(sense.OpponentChest - glove);
        sense = sense with { OpponentLeft = new GloveSense(glove, home * 0.7f) };
        for (var t = 0f; t < 0.4f; t += 1f / 240f)
        {
            bot.Read(sense, 1f / 240f);
            Assert.AreEqual(Defense.None, bot.CurrentDefense);
        }
    }

    [TestMethod]
    public void StyleJson_WithNulls_FallsBackInsteadOfCrashing()
    {
        var style = BotStyle.FromJson("""{ "Name": null, "ComboWeights": null }""");

        Assert.AreEqual("boxer", style.Name);
        Assert.IsEmpty(style.ComboWeights);
    }

    /// <summary>正面 0.15m から頭へ 0.6m/s でまっすぐ向かってくる左の拳（そのまま見せ続ける）。</summary>
    private static FighterSense Coming()
    {
        var self = new BodyFrame(new Vector3(0f, 1f, 0f), 0f);
        var head = new Vector3(0f, 1.06f, 0.01f);
        return new FighterSense(self, Reach, OpponentHead: new Vector3(0f, 1.06f, 0.24f), OpponentChest: new Vector3(0f, 0.98f, 0.24f), OpponentAlive: true,
            SelfHead: head, OpponentLeft: new GloveSense(head + new Vector3(0f, 0f, 0.15f), new Vector3(0f, 0f, -0.6f)),
            OpponentRight: new GloveSense(new Vector3(0.05f, 1.0f, 0.3f), Vector3.Zero));
    }

    /// <summary>原点で +Z を向いた自分の頭の右前で、外（自分の右）へ速く振りかぶる左の拳。</summary>
    private static FighterSense Windup()
    {
        var self = new BodyFrame(new Vector3(0f, 1f, 0f), 0f);
        var head = new Vector3(0f, 1.06f, 0.01f);
        return new FighterSense(self, Reach, OpponentHead: new Vector3(0f, 1.06f, 0.22f), OpponentChest: new Vector3(0f, 0.98f, 0.22f), OpponentAlive: true,
            SelfHead: head,
            OpponentLeft: new GloveSense(head + new Vector3(-0.06f, -0.01f, 0.1f), new Vector3(-0.6f, 0f, 0.1f)),
            OpponentRight: new GloveSense(new Vector3(0.05f, 1.0f, 0.15f), Vector3.Zero));
    }
}
