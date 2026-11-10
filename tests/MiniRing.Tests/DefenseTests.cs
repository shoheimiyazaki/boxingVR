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

    [TestMethod]
    public void CounterDuringTheEvasion_IsThrownWhileStillSlipping()
    {
        // 回避の 35% で打ち始める設定: スリップの姿勢のまま、後ろの手のストレートが出ている。
        var book = DefenseBook.Default with { Slip = DefenseBook.Default.Slip with { CounterStraight = "2", CounterStraightAt = 0.35f } };
        var style = BotCatalog.Boxer with { Defenses = book, DefenseRate = BotStyle.MaxDefenseRate, CounterRate = 1f, Attacks = false };
        Assert.IsTrue(Any(style, Defense.Slip, (bot, rear) => bot.CurrentDefense == Defense.Slip && bot.CurrentTechnique(rear) == Technique.Cross),
            "a cross while the head is still off the line");
    }

    [TestMethod]
    public void CounterAfterTheEvasion_WaitsUntilTheEvasionEnds()
    {
        var book = DefenseBook.Default with { Slip = DefenseBook.Default.Slip with { CounterStraight = "2", CounterStraightAt = 1f } };
        var style = BotCatalog.Boxer with { Defenses = book, DefenseRate = BotStyle.MaxDefenseRate, CounterRate = 1f, Attacks = false };
        Assert.IsFalse(Any(style, Defense.Slip, (bot, rear) => bot.CurrentDefense == Defense.Slip && bot.CurrentTechnique(rear) == Technique.Cross),
            "no punch during the slip");
    }

    [TestMethod]
    public void NewEvasions_AreEvasions_AndUnknownCounterCombosAreRejected()
    {
        CollectionAssert.Contains(DefenseBook.Evasions.ToList(), Defense.SlipIn);
        CollectionAssert.Contains(DefenseBook.Evasions.ToList(), Defense.Roll);
        CollectionAssert.DoesNotContain(DefenseBook.Evasions.ToList(), Defense.Block);
        Assert.IsTrue(DefenseBook.Default.Roll.Sweep, "rolling sweeps the head along with the punch");
        Assert.IsGreaterThan(0f, DefenseBook.Default.SlipIn.Move, "an inside slip steps in");

        var bad = DefenseBook.Default.Slip with { CounterHook = "9-9" };
        Assert.ThrowsExactly<ArgumentException>(() => bad.Validated());
    }

    [TestMethod]
    public void RopeBehind_GrowsNearTheRopes_AndPointsToTheOpenSide()
    {
        var center = BoxingGame.RingCenter;
        var half = BoxingGame.RingSize * 0.5f;
        var dir = Vector3.UnitZ; // 相手は +Z、背中は −Z
        var left = Vector3.Cross(Vector3.UnitY, dir);

        var (middle, _) = BoxerBot.RopeBehind(center, dir, left, Reach);
        var (nearRopes, escape) = BoxerBot.RopeBehind(center + new Vector3(half - 0.05f, 0f, -half + 0.05f), dir, left, Reach);

        Assert.AreEqual(0f, middle, "plenty of room behind in the middle of the ring");
        Assert.IsGreaterThan(0.8f, nearRopes, "the ropes are right behind");
        Assert.AreEqual(MathF.Sign(-left.X), escape, "escape toward the open side (away from the side ropes)");
    }

    [TestMethod]
    public void PunchesThatCannotReach_AreNotEvaded()
    {
        // 相手は胸と胸で腕の長さの 2 倍先。拳が速く向かってきても、肩から頭まで届かないので避けない。
        var style = BotCatalog.Boxer with { EvadeReach = 1.2f, DefenseRate = BotStyle.MaxDefenseRate, Attacks = false };
        var far = Coming() with { OpponentHead = new Vector3(0f, 1.06f, 2f * Reach), OpponentChest = new Vector3(0f, 0.98f, 2f * Reach) };
        for (var seed = 0; seed < 5; seed++)
        {
            var bot = new BoxerBot(style, seed, Defense.Duck);
            for (var t = 0f; t < 0.5f; t += 1f / 240f)
            {
                bot.Read(far, 1f / 240f);
                Assert.AreEqual(Defense.None, bot.CurrentDefense);
            }
        }

        var close = style with { EvadeReach = 0f };
        Assert.IsTrue(Enumerable.Range(0, 5).Any(seed =>
        {
            var bot = new BoxerBot(close, seed, Defense.Duck);
            for (var t = 0f; t < 0.5f; t += 1f / 240f)
            {
                bot.Read(far, 1f / 240f);
            }

            return bot.CurrentDefense == Defense.Duck;
        }), "without the reach check the same punch is evaded");
    }

    [TestMethod]
    public void SkippedOutOfReachPunch_IsEvaded_OnceTheOpponentStepsIn()
    {
        // 振りかぶりの時点では届かない距離 → 相手が踏み込んで届く距離に。見送った拳を見直して避ける。
        var style = BotCatalog.Boxer with { EvadeReach = 1.2f, DefenseRate = BotStyle.MaxDefenseRate, Attacks = false };
        var far = Coming() with { OpponentHead = new Vector3(0f, 1.06f, 2f * Reach), OpponentChest = new Vector3(0f, 0.98f, 2f * Reach) };
        var near = Coming() with { OpponentHead = new Vector3(0f, 1.06f, 0.9f * Reach), OpponentChest = new Vector3(0f, 0.98f, 0.9f * Reach) };
        Assert.IsTrue(Enumerable.Range(0, 10).Any(seed =>
        {
            var bot = new BoxerBot(style, seed, Defense.Duck);
            for (var t = 0f; t < 0.3f; t += 1f / 240f)
            {
                bot.Read(far, 1f / 240f);
            }

            for (var t = 0f; t < 0.1f; t += 1f / 240f)
            {
                bot.Read(near, 1f / 240f);
            }

            return bot.CurrentDefense == Defense.Duck;
        }), "the same punch is evaded once it can reach");
    }

    [TestMethod]
    public void RopeEscape_KeepsOneSide_AcrossTheMiddleOfTheRope()
    {
        // 背中にロープ、相手が近い（下がりたい）。体がロープの真ん中を左右にまたいでも、逃げる向きは変えない。
        var style = BotCatalog.Boxer with { RopeEscape = 1f, Circling = 0f, HeadMovement = 0f, Attacks = false, DefenseRate = 0f };
        var bot = new BoxerBot(style, 1);
        var half = BoxingGame.RingSize * 0.5f;
        var signs = new HashSet<int>();
        for (var i = 0; i < 40; i++)
        {
            var x = (i % 2 == 0 ? 1f : -1f) * 0.01f;
            var origin = BoxingGame.RingCenter + new Vector3(x, 0.9f, -half + 0.05f);
            var sense = new FighterSense(new BodyFrame(origin, 0f), Reach,
                OpponentHead: origin + new Vector3(0f, 0.06f, 0.8f * Reach), OpponentChest: origin + new Vector3(0f, -0.02f, 0.8f * Reach), OpponentAlive: true,
                SelfHead: origin + new Vector3(0f, 0.06f, 0f));
            var command = bot.Read(sense, 1f / 240f);
            signs.Add(MathF.Sign(command.MoveWorld.X));
        }

        Assert.HasCount(1, signs.Where(sign => sign != 0), "one escape direction");
    }

    [TestMethod]
    public void Block_NeverCountersMidBlock_EvenIfTheBookSaysSo()
    {
        var book = DefenseBook.Default with { Block = DefenseBook.Default.Block with { CounterStraight = "2", CounterStraightAt = 0.35f } };
        var style = BotCatalog.Boxer with { Defenses = book, DefenseRate = BotStyle.MaxDefenseRate, CounterRate = 1f, Attacks = false };
        Assert.IsFalse(Any(style, Defense.Block, (bot, rear) => bot.CurrentDefense == Defense.Block && bot.CurrentTechnique(rear) == Technique.Cross),
            "both gloves stay up while blocking");
    }

    [TestMethod]
    public void ComboDrillAttacker_ThrowsCombosContainingItsPunchKind()
    {
        foreach (var kind in Enum.GetValues<PunchKind>())
        {
            var single = DefenseDrill.Attacker(kind).Validated();
            var combos = DefenseDrill.Attacker(kind, combos: true).Validated();

            Assert.IsTrue(single.ComboWeights.Keys.All(n => Combo.Named(n).Steps.Count == 1), $"{kind}: single punches by default");
            Assert.IsTrue(combos.ComboWeights.Keys.Any(n => Combo.Named(n).Steps.Count >= 2), $"{kind}: real combos when asked");
            Assert.IsTrue(combos.ComboWeights.Keys.All(n => Combo.Named(n).Steps.Any(t => Kind(t) == kind)), $"{kind}: every combo contains that kind of punch");
        }

        static PunchKind Kind(Technique t)
        {
            var spec = PunchSpec.Of(t);
            return spec.ToBody ? PunchKind.Body : spec.Low > 0.5f ? PunchKind.Uppercut : spec.Lateral > 0.5f ? PunchKind.Hook : PunchKind.Straight;
        }
    }

    /// <summary>
    /// 頭へまっすぐ来る拳を見せ続け、<paramref name="forced"/> で守らせる。どれかの種で、守りの最中に <paramref name="seen"/> が真になったか。
    /// 拳は動き続けるので、守りが終わって次の判断が入ることもある。見るのは最初の守りの間だけ。
    /// </summary>
    private static bool Any(BotStyle style, Defense forced, Func<BoxerBot, Hand, bool> seen)
    {
        var sense = Coming();
        for (var seed = 0; seed < 10; seed++)
        {
            var bot = new BoxerBot(style, seed, forced);
            var rear = style.Lead == Hand.Left ? Hand.Right : Hand.Left;
            var defended = false;
            for (var t = 0f; t < 1.2f; t += 1f / 240f)
            {
                bot.Read(sense, 1f / 240f);
                if (bot.CurrentDefense != Defense.None)
                {
                    defended = true;
                    if (seen(bot, rear))
                    {
                        return true;
                    }
                }
                else if (defended)
                {
                    break;
                }
            }
        }

        return false;
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
