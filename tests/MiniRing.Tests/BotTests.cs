using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>ボクシングの技を使うボット（MiniRing.Bots）: 技の経路、人並みの制約、守りの反応、対戦の決定性。</summary>
[TestClass]
public sealed class BotTests
{
    private const float Reach = 0.19f;
    private static readonly Vector3 GuardLocal = new(0.03f, 0f, 0.1f);
    private static readonly Vector3 AimLocal = new(0f, 0.05f, 0.25f);

    [TestMethod]
    public void EveryPunch_StartsAndEndsAtTheGuard_AndReachesTowardTheAim()
    {
        foreach (var technique in Enum.GetValues<Technique>())
        {
            var spec = PunchSpec.Of(technique);
            var hand = spec.Lead ? Hand.Left : Hand.Right;
            var duration = spec.Duration;
            var start = spec.Evaluate(hand, 0f, duration, GuardLocal, AimLocal, Reach);
            var end = spec.Evaluate(hand, duration, duration, GuardLocal, AimLocal, Reach);
            var extended = spec.Evaluate(hand, duration * PunchSpec.OutboundShare, duration, GuardLocal, AimLocal, Reach);

            Assert.IsLessThan(1e-5f, Vector3.Distance(start, GuardLocal), $"{technique} starts at the guard");
            Assert.IsLessThan(1e-5f, Vector3.Distance(end, GuardLocal), $"{technique} returns to the guard");
            if (technique == Technique.Feint)
            {
                Assert.IsLessThan(Vector3.Distance(GuardLocal, AimLocal) * 0.6f, Vector3.Distance(extended, GuardLocal), "a feint stops short");
            }
            else
            {
                Assert.IsGreaterThan(AimLocal.Z - (0.05f * Reach), extended.Z, $"{technique} reaches the aim");
            }
        }
    }

    [TestMethod]
    public void HookWindsUpFromTheSide_UppercutFromBelow_OverhandFromAbove()
    {
        static Vector3 Quarter(Technique t)
        {
            var spec = PunchSpec.Of(t);
            return spec.Evaluate(spec.Lead ? Hand.Left : Hand.Right, spec.Duration * 0.15f, spec.Duration, GuardLocal, AimLocal, Reach);
        }

        var jab = Quarter(Technique.Jab);
        Assert.IsGreaterThan(jab.X + (0.05f * Reach), Quarter(Technique.LeadHook).X, "the lead hook swings out to the lead (+X) side");
        Assert.IsLessThan(jab.Y - (0.05f * Reach), Quarter(Technique.LeadUppercut).Y, "the uppercut dips below the guard first");
        Assert.IsGreaterThan(jab.Y + (0.03f * Reach), Quarter(Technique.Overhand).Y, "the overhand loops over the top");
    }

    [TestMethod]
    public void Combos_AreAllNamedUniquely_AndStylesReferOnlyToKnownCombos()
    {
        Assert.AreEqual(Combo.All.Count, Combo.All.Select(c => c.Name).Distinct().Count());
        foreach (var style in BotCatalog.Styles)
        {
            Assert.AreEqual(style.Name, style.Validated().Name);
        }

        var bad = BotCatalog.Boxer with { ComboWeights = new Dictionary<string, float> { ["9-9"] = 1f } };
        Assert.ThrowsExactly<ArgumentException>(() => bad.Validated());
    }

    [TestMethod]
    public void Validated_EnforcesHumanLimits_SoTuningCannotBuildASuperhuman()
    {
        var cheat = BotCatalog.Boxer with { ReactionTime = 0f, DefenseRate = 1f, PunchSpeed = 3f, Aggression = 5f, AttackRange = float.NaN };

        var v = cheat.Validated();

        Assert.AreEqual(BotStyle.MinReactionTime, v.ReactionTime);
        Assert.AreEqual(BotStyle.MaxDefenseRate, v.DefenseRate);
        Assert.AreEqual(BotStyle.MaxPunchSpeed, v.PunchSpeed);
        Assert.AreEqual(1f, v.Aggression);
        Assert.IsTrue(float.IsFinite(v.AttackRange));
    }

    [TestMethod]
    public void StyleJson_RoundTrips()
    {
        var style = BotCatalog.Swarmer with { ReadGuard = 0.4f };

        var back = BotStyle.FromJson(style.ToJson());

        Assert.AreEqual(style.Aggression, back.Aggression);
        Assert.AreEqual(style.ReadGuard, back.ReadGuard);
        CollectionAssert.AreEquivalent(style.ComboWeights.ToList(), back.ComboWeights.ToList());
    }

    [TestMethod]
    public void IncomingPunch_IsDefended_OnlyAfterTheReactionTime()
    {
        // 頭へまっすぐ向かってくる拳を見せ続ける。反応時間より前には守らず、その後は（守る率に従って）守る。
        var style = BotCatalog.Boxer with { DefenseRate = BotStyle.MaxDefenseRate, BlockWeight = 1f, DuckWeight = 0f, SlipWeight = 0f, SwayWeight = 0f, StepBackWeight = 0f, Aggression = 0f };
        var defended = 0;
        for (var seed = 0; seed < 20; seed++)
        {
            var bot = new BoxerBot(style, seed);
            var sense = Incoming();
            var dt = 1f / 240f;
            var t = 0f;
            for (; t < style.ReactionTime - (2f * dt); t += dt)
            {
                bot.Read(sense, dt);
                Assert.AreEqual(Defense.None, bot.CurrentDefense, $"seed {seed}: no defense before reacting ({t:0.000}s)");
            }

            for (; t < style.ReactionTime + (3f * dt); t += dt)
            {
                bot.Read(sense, dt);
            }

            defended += bot.CurrentDefense == Defense.Block ? 1 : 0;
        }

        Assert.IsGreaterThan(10, defended, "most of the seeds defend (rate 0.85)");
        Assert.IsLessThan(20, defended, "but not superhumanly every time");
    }

    [TestMethod]
    public void BlockRaisesBothGlovesInFrontOfTheFace()
    {
        var style = BotCatalog.Boxer with { DefenseRate = BotStyle.MaxDefenseRate, BlockWeight = 1f, DuckWeight = 0f, SlipWeight = 0f, SwayWeight = 0f, StepBackWeight = 0f, Aggression = 0f };
        var sense = Incoming();
        for (var seed = 0; seed < 20; seed++)
        {
            var bot = new BoxerBot(style, seed);
            FighterCommand command = default;
            for (var t = 0f; t < 0.3f; t += 1f / 240f)
            {
                command = bot.Read(sense, 1f / 240f);
            }

            if (bot.CurrentDefense != Defense.Block)
            {
                continue;
            }

            var head = sense.Self.WorldToLocal(sense.SelfHead);
            var guard = bot.Guard(Hand.Left, Reach, head);
            Assert.IsGreaterThan(guard.Y, command.Left.TargetLocal.Y, "the glove comes up from the guard");
            Assert.IsLessThan(0.15f * Reach, MathF.Abs(command.Left.TargetLocal.Y - head.Y), "to face height");
            return;
        }

        Assert.Fail("no seed blocked");
    }

    [TestMethod]
    public void BotBout_IsDeterministic()
    {
        var spec = new BoutSpec(Contender.Named("boxer"), Contender.Named("swarmer"), Seed: 3, Seconds: 6f);

        var a = BotArena.Run(spec);
        var b = BotArena.Run(spec);

        Assert.AreEqual(a.Red.DamageDealt, b.Red.DamageDealt);
        Assert.AreEqual(a.Blue.DamageDealt, b.Blue.DamageDealt);
        Assert.AreEqual(a.Red.Landed, b.Red.Landed);
    }

    [TestMethod]
    public void ParallelBouts_GiveTheSameResultsAsRunningThemOneByOne()
    {
        var specs = BotArena.RoundRobin([Contender.Named("boxer"), Contender.Named("swarmer"), Contender.Named("counter")], 2, 3f, 5);

        var parallel = BotArena.RunAll(specs);
        var sequential = specs.Select(s => BotArena.Run(s)).ToList();

        for (var i = 0; i < specs.Count; i++)
        {
            Assert.AreEqual(sequential[i].Red.DamageDealt, parallel[i].Red.DamageDealt, $"bout {i}");
            Assert.AreEqual(sequential[i].Blue.DamageDealt, parallel[i].Blue.DamageDealt, $"bout {i}");
        }
    }

    [TestMethod]
    public void RoundRobin_RejectsTwoContendersWithTheSameName()
    {
        var twin = Contender.Of(BotCatalog.Swarmer with { Name = "boxer" });

        Assert.ThrowsExactly<ArgumentException>(() => BotArena.RoundRobin([Contender.Named("boxer"), twin], 2, 3f, 1));
    }

    [TestMethod]
    public void BoxerBot_OutpointsTheLegacyCpu_AndThrowsItsTechniques()
    {
        // ゲームの CPU（champion）で確かめる。手書きのままの boxer は、頭を人の比率に小さくしてから legacy と 5-4-1 になった:
        // legacy の拳は当たるまで頭を追い続ける（BoxerBot は打ち出したら直せない。基本動作編 B2）ので、小さな頭ほど有利になる。
        // legacy は比べる基準と昔の記録の再生のため、そのまま残す。
        // 1 試合では、相手がふらついてガードを下げている間はジャブとストレートだけで足りることもある（ダメージ編 D2）ので、3 つの種を合わせて見る。
        var stats = new BotStats();
        var landed = 0;
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var result = BotArena.Run(new BoutSpec(Contender.Named(BotCatalog.DefaultCpu), Contender.Named(BotCatalog.Legacy), Seed: seed, Seconds: 20f));
            Assert.AreEqual(1, result.Winner, $"seed {seed}: champion {result.Red.DamageDealt:0} vs legacy {result.Blue.DamageDealt:0}");
            stats.Merge(result.Red.Stats);
            landed += result.Red.Stats.Techniques.Values.Sum(t => t.Landed);
        }

        Assert.IsGreaterThan(2, stats.Techniques.Count, $"several different punches are thrown ({string.Join(", ", stats.Techniques.Select(t => $"{t.Key} {t.Value.Thrown}"))})");
        Assert.IsGreaterThan(0, landed);
    }

    [TestMethod]
    public void Infighter_DoesNotFightCrouchedTheWholeTime()
    {
        // 以前のインファイターは試合の 3 割をしゃがんで過ごし、続けて 4 秒しゃがんでいたこともあった（足運び編 F3）。
        // 1 試合では相手と種しだいで揺れる（同じ形で 19〜28 %）ので、3 つの種を足して見る。
        var stats = new BotStats();
        foreach (var seed in new[] { 1, 2, 3 })
        {
            stats.Merge(BotArena.Run(new BoutSpec(Contender.Named("swarmer"), Contender.Named("slugger"), Seed: seed, Seconds: 30f)).Red.Stats);
        }

        Assert.IsGreaterThan(60f, stats.Time);
        Assert.IsLessThan(0.25f, stats.CrouchShare, $"crouched {stats.CrouchShare:P0}");
        Assert.IsLessThan(1.5f, stats.LongestCrouch, $"longest crouch {stats.LongestCrouch:0.0}s");
    }

    [TestMethod]
    public void BotOnThePlayerSide_DrivesThePlayer_IgnoringTheHostInput()
    {
        var corners = new BoxingCorners("boxer", seed => BotCatalog.Create("boxer", seed), seed => BotCatalog.Create("boxer", seed));
        using var game = new BoxingGame(seed: 1, corners: corners);
        var start = game.Player.PelvisPosition;
        var away = new FighterCommand(new Vector3(0f, 0f, -1f), Vector3.UnitZ, default, default);
        for (var i = 0; i < 120; i++)
        {
            game.Step(new BoxingInput(away), 1f / 60f); // ホストの「下がれ」は無視され、ボットが前へ出る
        }

        Assert.IsNotNull(game.PlayerControl);
        Assert.IsGreaterThan(start.Z, game.Player.PelvisPosition.Z, "the player bot steps in toward the CPU");
    }

    /// <summary>原点で +Z を向いた自分に、正面 0.15m から頭へ 0.6m/s で向かってくる左の拳。</summary>
    private static FighterSense Incoming()
    {
        var self = new BodyFrame(new Vector3(0f, 1f, 0f), 0f);
        var head = new Vector3(0f, 1.06f, 0.01f);
        var glove = head + new Vector3(0f, 0f, 0.15f);
        return new FighterSense(self, Reach, OpponentHead: new Vector3(0f, 1.06f, 0.24f), OpponentChest: new Vector3(0f, 0.98f, 0.24f), OpponentAlive: true,
            SelfHead: head, OpponentLeft: new GloveSense(glove, new Vector3(0f, 0f, -0.6f)), OpponentRight: new GloveSense(new Vector3(0.05f, 1.0f, 0.3f), Vector3.Zero));
    }
}
