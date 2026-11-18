using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// 戦略の層（<see cref="GamePlan"/>、LogicVersion 50）: 得意な間合いの表・傷み・疲れ・意識・試合の時刻と点から、好む距離 d* と手数の倍率を連続に決める。
/// 層が無い・重み 0 なら判断はビット単位で今まで通り。相手の表は試合の相手のキャラから入り、記録の再生で同じになる。
/// </summary>
[TestClass]
public sealed class GamePlanTests
{
    private const float Dt = 1f / 60f;

    /// <summary>距離 [cm] ごとの値の山（ガウスの形）を、2 cm の帯の表にする。</summary>
    private static RangeBin[] Table(Func<float, float> dealt, Func<float, float> taken, float from = 13f, float to = 43f)
    {
        var bins = new List<RangeBin>();
        for (var cm = from; cm <= to + 1e-3f; cm += 2f)
        {
            bins.Add(new RangeBin { Cm = cm, Dealt = dealt(cm), Taken = taken(cm) });
        }

        return [.. bins];
    }

    private static float Bump(float cm, float center, float height, float width = 4f) => height * MathF.Exp(-0.5f * MathF.Pow((cm - center) / width, 2f));

    /// <summary>自分は 25 cm で最もよく当て、相手は 17 cm で最もよく当てる（近いほど危ない）。</summary>
    private static GamePlanStyle Plan(bool foe = true) => new GamePlanStyle
    {
        Own = Table(cm => Bump(cm, 25f, 20f), cm => Bump(cm, 17f, 10f, 5f) + 1f),
        Foe = foe ? Table(cm => Bump(cm, 17f, 20f, 5f) + 2f, cm => Bump(cm, 25f, 15f)) : [],
    }.Validated();

    private static readonly PlanInputs Fresh = new(Opening: 0f, OpponentWinded: 0f, OpponentInjury: 0f, Stamina: 1f, Faintness: 0f, SelfInjury: 0f, Match: default);

    private static PlanChoice Decide(GamePlanStyle plan, PlanInputs input) => GamePlan.Decide(plan, input) ?? throw new AssertFailedException("the plan has tables");

    private static float Cm(PlanChoice c) => c.Distance * 100f;

    [TestMethod]
    public void TheDistance_SitsNearTheMeritPeak_OfTheOwnTable()
    {
        var plan = new GamePlanStyle { Own = Table(cm => Bump(cm, 25f, 20f), _ => 1f) }.Validated();
        var d = Cm(Decide(plan, Fresh));
        Assert.AreEqual(25f, d, 1.5f, $"d* = {d:0.0} cm");
    }

    [TestMethod]
    public void TheDistance_FromAMeasuredTable_SitsNearItsBestNet()
    {
        // マーカスの表（docs/arena/maai_roster_ranges_bins.csv）: 差し引きの最大は 23〜25 cm、与えた量の最大は 25 cm。
        var marcus = CharacterRoster.Get("marcus").Style.GamePlan!;
        var d = Cm(Decide(marcus with { Foe = [] }, Fresh));
        Console.WriteLine($"marcus alone d* = {d:0.0} cm");
        Assert.IsTrue(d is > 22f and < 28f, $"d* = {d:0.0} cm");
    }

    [TestMethod]
    public void AThreatAtTheFoesBestDistance_PushesTheDistanceAway()
    {
        var alone = Cm(Decide(Plan(foe: false), Fresh));
        var withFoe = Cm(Decide(Plan(foe: true), Fresh));
        Console.WriteLine($"alone {alone:0.0} cm, knowing the foe {withFoe:0.0} cm");
        Assert.IsGreaterThan(alone, withFoe, "the foe is best at 17 cm: stand farther from it");
    }

    [TestMethod]
    public void BeingHurt_Faint_OrTired_MovesAwayFromTheThreat_AndSlowsTheHands()
    {
        var plan = Plan();
        var fresh = Decide(plan, Fresh);
        foreach (var (name, input) in new[]
                 {
                     ("injured", Fresh with { SelfInjury = 0.6f }),
                     ("faint", Fresh with { Faintness = 0.6f }),
                     ("tired", Fresh with { Stamina = 0.2f }),
                 })
        {
            var worse = Decide(plan, input);
            Console.WriteLine($"{name}: {Cm(fresh):0.0} → {Cm(worse):0.0} cm, tempo {fresh.Tempo:0.00} → {worse.Tempo:0.00}");
            Assert.IsGreaterThan(Cm(fresh), Cm(worse), name + ": farther from the threat at 17 cm");
            Assert.IsLessThan(fresh.Tempo, worse.Tempo, name + ": fewer punches");
        }
    }

    [TestMethod]
    public void AHurtWindedOrOpenOpponent_DrawsTowardTheMeritPeak_AndSpeedsTheHands()
    {
        var plan = Plan();
        var fresh = Decide(plan, Fresh);
        foreach (var (name, input) in new[]
                 {
                     ("injured", Fresh with { OpponentInjury = 0.6f }),
                     ("winded", Fresh with { OpponentWinded = 0.8f }),
                     ("open", Fresh with { Opening = 0.8f }),
                 })
        {
            var better = Decide(plan, input);
            Console.WriteLine($"{name}: {Cm(fresh):0.0} → {Cm(better):0.0} cm, tempo {fresh.Tempo:0.00} → {better.Tempo:0.00}");
            Assert.IsLessThan(MathF.Abs(Cm(fresh) - 25f), MathF.Abs(Cm(better) - 25f), name + ": nearer the merit peak at 25 cm");
            Assert.IsGreaterThan(fresh.Tempo, better.Tempo, name + ": more punches");
        }
    }

    [TestMethod]
    public void AStall_ALosingScore_AndTheEndOfTheRound_PushTowardAttack_ContinuouslyWithoutCaseSplits()
    {
        var plan = Plan();
        var fresh = Decide(plan, Fresh);
        var stall = Decide(plan, Fresh with { Match = new MatchSense(Quiet: 10f) });
        var losingEarly = Decide(plan, Fresh with { Match = new MatchSense(RoundElapsed: 3f, RoundLength: 30f, DamageLead: -60f) });
        var losingLate = Decide(plan, Fresh with { Match = new MatchSense(RoundElapsed: 28f, RoundLength: 30f, DamageLead: -60f) });
        var winningLate = Decide(plan, Fresh with { Match = new MatchSense(RoundElapsed: 28f, RoundLength: 30f, DamageLead: 60f) });
        Console.WriteLine($"fresh {Cm(fresh):0.0}/{fresh.Tempo:0.00}, stall {Cm(stall):0.0}/{stall.Tempo:0.00}, losing early {Cm(losingEarly):0.0}/{losingEarly.Tempo:0.00}, late {Cm(losingLate):0.0}/{losingLate.Tempo:0.00}, winning late {Cm(winningLate):0.0}/{winningLate.Tempo:0.00}");

        Assert.IsLessThan(MathF.Abs(Cm(fresh) - 25f), MathF.Abs(Cm(stall) - 25f), "a stall draws toward the attack");
        Assert.IsGreaterThan(fresh.Tempo, stall.Tempo, "a stall speeds the hands");
        Assert.IsGreaterThan(losingEarly.Tempo, losingLate.Tempo, "losing late urges more than losing early");
        Assert.IsLessThan(MathF.Abs(Cm(losingEarly) - 25f), MathF.Abs(Cm(losingLate) - 25f));
        Assert.AreEqual(fresh.Tempo, winningLate.Tempo, 1e-6f, "the clock alone does not urge the fighter who is ahead");
        Assert.AreEqual(1f, GamePlan.Losing(new MatchSense(DownLead: -10)), 1e-3f, "many downs behind: fully losing");
        Assert.AreEqual(0f, GamePlan.Losing(new MatchSense(DamageLead: 5f)));

        // 打ち合いの無い時間につれて、急ぐ度合いは連続に増える（段階で切り替えない）。
        var previous = GamePlan.Urgency(plan, default);
        Assert.AreEqual(0f, previous, "no stall, no score: no urgency");
        for (var quiet = 0.5f; quiet <= 30f; quiet += 0.5f)
        {
            var u = GamePlan.Urgency(plan, new MatchSense(Quiet: quiet));
            Assert.IsGreaterThan(previous, u, $"quiet {quiet}");
            Assert.IsLessThan(previous + 0.1f, u, $"no jump at quiet {quiet}");
            previous = u;
        }
    }

    [TestMethod]
    public void Personality_BoldnessAndCaution_TiltTheDistance()
    {
        var bold = Cm(Decide(Plan() with { Boldness = 2f }, Fresh));
        var neutral = Cm(Decide(Plan(), Fresh));
        var careful = Cm(Decide(Plan() with { Caution = 2f }, Fresh));
        Console.WriteLine($"bold {bold:0.0}, neutral {neutral:0.0}, careful {careful:0.0} cm");
        Assert.IsGreaterThan(bold, neutral);
        Assert.IsGreaterThan(neutral, careful);
    }

    [TestMethod]
    public void Tables_AreInterpolatedBetweenBins_AndHeldAtTheEnds()
    {
        RangeBin[] bins = [new() { Cm = 20f, Dealt = 2f, Taken = 10f }, new() { Cm = 24f, Dealt = 6f, Taken = 2f }];
        Assert.AreEqual(4f, GamePlan.At(bins, 22f, dealt: true), 1e-5f);
        Assert.AreEqual(6f, GamePlan.At(bins, 22f, dealt: false), 1e-5f);
        Assert.AreEqual(2f, GamePlan.At(bins, 5f, dealt: true));
        Assert.AreEqual(2f, GamePlan.At(bins, 99f, dealt: false));
        Assert.IsNull(GamePlan.Decide(new GamePlanStyle(), Fresh), "no table, no plan");

        var messy = new GamePlanStyle { Weight = 7f, Boldness = -1f, Own = [new() { Cm = 30f, Dealt = -1f, Taken = 1f }, new() { Cm = float.NaN }, new() { Cm = 20f, Dealt = 1f, Taken = 1f }] }.Validated();
        Assert.AreEqual(1f, messy.Weight);
        Assert.AreEqual(0f, messy.Boldness);
        Assert.AreEqual("20,30", string.Join(",", messy.Own.Select(b => b.Cm.ToString(System.Globalization.CultureInfo.InvariantCulture))), "sorted by distance, broken rows dropped");
        Assert.AreEqual(0f, messy.Own[1].Dealt, "damage is never negative");
    }

    // ---- ボットへの入れ方 ----

    /// <summary>相手が 0.3 m 前に立って構えている見え方（試合の時刻・傷みは引数で）。</summary>
    private static FighterSense Sense(float t, MatchSense match = default, float selfInjury = 0f)
    {
        var reach = 0.19f;
        var chest = new Vector3(0f, 0.9f, 0.3f + (0.02f * MathF.Sin(t * 3f)));
        var head = chest + new Vector3(0f, 0.12f, 0f);
        return new FighterSense(new BodyFrame(new Vector3(0f, 0.95f, 0f), 0f), reach, head, chest, OpponentAlive: true,
            SelfHead: new Vector3(0f, 1.07f, 0f),
            OpponentLeft: new GloveSense(head + new Vector3(0.05f, -0.03f, -0.08f), Vector3.Zero),
            OpponentRight: new GloveSense(head + new Vector3(-0.05f, -0.04f, -0.05f), Vector3.Zero),
            OpponentArmReach: reach, SelfStamina: 1f, SelfInjury: selfInjury, Match: match);
    }

    private static List<FighterCommand> Commands(BotStyle style, int seed, int steps, Func<float, MatchSense>? match = null)
    {
        var bot = new BoxerBot(style, seed);
        var commands = new List<FighterCommand>();
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            commands.Add(bot.Read(Sense(t, match?.Invoke(t) ?? default), Dt));
        }

        return commands;
    }

    [TestMethod]
    public void WeightZero_OrNoPlan_GivesBitIdenticalCommands()
    {
        var plain = BotCatalog.Champion;
        var weightZero = plain with { GamePlan = Plan() with { Weight = 0f } };
        var weightOne = plain with { GamePlan = Plan() };
        MatchSense Clock(float t) => new(RoundElapsed: t, Quiet: t);
        var a = Commands(plain, 3, 600, Clock);
        var b = Commands(weightZero, 3, 600, Clock);
        var c = Commands(weightOne, 3, 600, Clock);
        CollectionAssert.AreEqual(a, b, "weight 0 plays exactly the textbook");
        Assert.IsTrue(a.Zip(c).Any(p => p.First != p.Second), "weight 1 changes the commands");
    }

    [TestMethod]
    public void TheBot_StandsAtThePlansDistance_ScaledByTheWeight()
    {
        static float Desired(BotStyle style)
        {
            var bot = new BoxerBot(style with { Attacks = false }, 5);
            for (var i = 0; i < 30; i++)
            {
                bot.Read(Sense(i * Dt), Dt);
            }

            return bot.ProbeState.Desired;
        }

        var reach = 0.19f;
        var plan = Plan();
        var target = Decide(plan, Fresh).Distance / reach;
        var plain = Desired(BotCatalog.Champion);
        var full = Desired(BotCatalog.Champion with { GamePlan = plan });
        var half = Desired(BotCatalog.Champion with { GamePlan = plan with { Weight = 0.5f } });
        Console.WriteLine($"plain {plain:0.000}, half {half:0.000}, full {full:0.000}, plan {target:0.000} (reach units)");
        Assert.AreEqual(target, full, 0.05f, "weight 1: the plan's distance");
        Assert.AreEqual(0.5f * (plain + full), half, 0.05f, "weight 0.5: half way");
    }

    // ---- 相手の表・試合の入力・記録 ----

    [TestMethod]
    public void TheFoesTable_ComesFromTheOpponentsCharacter()
    {
        var marcus = CharacterRoster.Get("marcus");
        var ramon = CharacterRoster.Get("ramon");
        Assert.IsEmpty(marcus.Resolve().GamePlan!.Foe, "no opponent: no foe table");
        CollectionAssert.AreEqual(ramon.Style.GamePlan!.Own.ToArray(), marcus.Resolve("ramon").GamePlan!.Foe.ToArray());
        CollectionAssert.AreEqual(ramon.Style.GamePlan!.Own.ToArray(), marcus.Resolve(ramon).GamePlan!.Foe.ToArray());
        CollectionAssert.AreEqual(CharacterRoster.Get("jin").Style.GamePlan!.Own.ToArray(), marcus.Resolve("jin").GamePlan!.Foe.ToArray(), "a weapon fighter's table is known too");
        Assert.IsEmpty(marcus.Resolve("nobody").GamePlan!.Foe, "an opponent outside the roster: own table only");

        var corners = new MatchSelection(MatchMode.Spectate, marcus, ramon).Corners;
        var recorded = BotStyle.FromJson(corners.CpuStyleJson!);
        CollectionAssert.AreEqual(marcus.Style.GamePlan!.Own.ToArray(), recorded.GamePlan!.Foe.ToArray(), "the recorded blue style carries red's table as its foe");
        CollectionAssert.AreEqual(ramon.Style.GamePlan!.Own.ToArray(), corners.RedBotStyle!.GamePlan!.Foe.ToArray());
    }

    [TestMethod]
    public void TheMatch_TellsTheClock_TheQuietTime_AndNoInjuryAtTheStart()
    {
        using var game = new BoxingGame(seed: 2, corners: BotCatalog.Corners(BotCatalog.Boxer with { Attacks = false }), rules: new MatchRules(RoundSeconds: 20f, Rounds: 3));
        for (var i = 0; i < 60; i++)
        {
            game.Step(default, Dt);
        }

        var sense = game.CpuSense;
        Console.WriteLine(sense.Match);
        Assert.AreEqual(20f, sense.Match.RoundLength);
        Assert.AreEqual(2, sense.Match.RoundsLeft);
        Assert.IsTrue(sense.Match.RoundElapsed is > 0.9f and < 1.1f, $"elapsed {sense.Match.RoundElapsed}");
        Assert.AreEqual(sense.Match.RoundElapsed, sense.Match.Quiet, 1e-4f, "nobody has scored yet");
        Assert.AreEqual(0f, sense.SelfInjury);
        Assert.AreEqual(0f, sense.OpponentInjury);
        Assert.AreEqual(0f, sense.Match.DamageLead);
        Assert.AreEqual(0, sense.Match.DownLead);
        Assert.AreEqual(game.Player.Winded, sense.OpponentWinded);
    }

    [TestMethod]
    public void Injury_CountsTheMuscleLeft_AndTheFeltOneAlsoTheNerves()
    {
        using var game = new BoxingGame(seed: 4);
        var f = game.Cpu;
        Assert.AreEqual(0f, f.Injury(felt: true));
        var part = f.Voxels!.PartOf(f.ForearmId(Hand.Left))!;
        var grid = part.Body.Grid;
        var removed = new List<Aigis.Foundation.Voxels.RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            var material = grid.MaterialAt(i);
            if (material != 0 && grid.Palette[material].Tissue == Aigis.Foundation.Voxels.VoxelTissue.Flesh)
            {
                grid.Break(i, removed);
            }
        }

        // 前腕 1 本の肉が全部無い: 腕 1 本の半分 → 4 本の手足の 1/8。
        Assert.AreEqual(0.125f, f.Injury(felt: false), 0.01f);
        Assert.AreEqual(f.Injury(felt: false), game.PlayerSense.OpponentInjury, 1e-6f, "the opponent sees the missing flesh");
        Assert.AreEqual(f.Injury(felt: true), game.CpuSense.SelfInjury, 1e-6f);
    }

    [TestMethod]
    [TestCategory("slow")]
    public void GamePlanCharacters_RecordAndReplay_Identically()
    {
        var corners = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get("marcus"), CharacterRoster.Get("ramon"), Referee: false).Corners;
        using var game = new BoxingGame(seed: 7, corners: corners);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "gameplan"))
        {
            for (var i = 0; i < 4 * ReplayRecorder.ChecksumInterval; i++)
            {
                recorder.Step(game, default, Dt);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.IsNotEmpty(data.Header.Corners.CpuStyle!.GamePlan!.Foe, "the header holds the resolved foe table");
        var result = ReplayPlayer.Verify(data);
        Assert.IsTrue(result.Reproduced);
        Assert.AreEqual(4, result.ChecksumsMatched);
    }

    [TestMethod]
    [TestCategory("slow")]
    public void WeightZero_PlaysTheSameMatch_AsNoPlan()
    {
        static ulong Run(BotStyle red, BotStyle blue)
        {
            using var game = new BoxingGame(seed: 9, corners: BotCatalog.Corners(blue) with { RedBotStyle = red });
            for (var i = 0; i < 360; i++)
            {
                game.Step(default, Dt);
            }

            return game.StateChecksum();
        }

        var plan = CharacterRoster.Get("marcus").Resolve("ramon").GamePlan!;
        var plain = Run(BotCatalog.Champion, BotCatalog.Swarmer);
        Assert.AreEqual(plain, Run(BotCatalog.Champion with { GamePlan = plan with { Weight = 0f } }, BotCatalog.Swarmer with { GamePlan = plan with { Weight = 0f } }));
        Assert.AreNotEqual(plain, Run(BotCatalog.Champion with { GamePlan = plan }, BotCatalog.Swarmer));
    }
}
