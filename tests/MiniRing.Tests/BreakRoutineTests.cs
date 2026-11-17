using System.Numerics;
using MiniRing.Bots;
using MiniRing.Break;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>休み（ラウンドの間）の過ごし方（<see cref="BreakRoutine"/>）: 目標の経路へ手が近づく・小物・元のボットへ戻る・決定的（記録 → 再生で同じ）。</summary>
[TestClass]
public sealed class BreakRoutineTests
{
    private const float Dt = 1f / 60f;

    private static readonly MatchRules ShortRules = MatchRules.Tv with { Rounds = 2, RoundSeconds = 1f, BreakSeconds = 16f };

    private static BoxingGame NewGame(int seed, Func<int, Fighter, IBreakSource?>? source = null, bool actions = true) =>
        new(seed: seed, rules: ShortRules, corners: new BoxingCorners("cpu", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s),
            BreakActions: actions, BreakSource: source));

    private static void StepUntil(BoxingGame game, Func<bool> done, int maxSteps = 60 * 40)
    {
        for (var i = 0; i < maxSteps && !done(); i++)
        {
            game.Step(default, Dt);
        }

        Assert.IsTrue(done(), "条件に達しなかった");
    }

    [TestMethod]
    public void DrinkingBringsTheBottleCapToTheFace()
    {
        using var game = NewGame(3, (seed, _) => new BreakRoutine(seed, null, ["drink:R"]));
        var self = game.Player;
        StepUntil(game, () => game.InBreak);
        var restGlove = float.MaxValue;
        var nearest = float.MaxValue;
        var nearestCap = float.MaxValue;
        for (var i = 0; i < 60 * 14 && game.InBreak; i++)
        {
            game.Step(default, Dt);
            var glove = Vector3.Distance(self.GloveCenter(Hand.Right), self.HeadPosition);
            if (game.BreakSourceOf(self)?.Holding is null)
            {
                restGlove = MathF.Min(restGlove, glove);
            }

            if (game.BreakProps.FirstOrDefault(p => p.Owner == self) is { } bottle)
            {
                Assert.AreEqual(PropKind.Bottle, bottle.Kind);
                nearest = MathF.Min(nearest, glove);
                var cap = Vector3.Transform(new Vector3(0.05f, 0f, 0f), bottle.GridToWorld());
                nearestCap = MathF.Min(nearestCap, Vector3.Distance(cap, self.HeadPosition));
            }
        }

        Assert.IsLessThan(0.1f, nearest, "ボトルを持つ拳が口の近く（頭から 10 cm 以内）へ来る");
        Assert.IsLessThan(0.06f, nearestCap, "ボトルの口（キャップ）が頭の近く（頭の中心から 6 cm 以内）へ来る");
        Assert.IsGreaterThan(0.13f, restGlove, "動作の外（休みの構え）では拳は頭から遠い");
    }

    [TestMethod]
    public void CallingBringsThePhoneToTheEar()
    {
        using var game = NewGame(3, (seed, _) => new BreakRoutine(seed, null, ["call:L"]));
        var self = game.Player;
        StepUntil(game, () => game.InBreak);
        var nearest = float.MaxValue;
        var turned = 0f;
        var forwardAtRest = self.Frame.Forward;
        for (var i = 0; i < 60 * 14 && game.InBreak; i++)
        {
            game.Step(default, Dt);
            if (game.BreakProps.Any(p => p.Owner == self && p.Kind == PropKind.Phone))
            {
                nearest = MathF.Min(nearest, Vector3.Distance(self.GloveCenter(Hand.Left), self.HeadPosition));
                turned = MathF.Max(turned, MathF.Acos(Math.Clamp(Vector3.Dot(forwardAtRest, self.Frame.Forward), -1f, 1f)));
            }
        }

        Assert.IsLessThan(0.075f, nearest, "スマホを持つ拳が耳（頭の横）の近くへ来る");
        Assert.IsGreaterThan(0.1f, turned, "電話の間は体の向きを相手の方から少しそらす");
    }

    [TestMethod]
    public void AfterTheBreakTheBotsAreBackAndThePropsAreGone()
    {
        using var game = NewGame(5);
        StepUntil(game, () => game.InBreak);
        var sawProp = false;
        var names = new HashSet<string>();
        while (game.InBreak)
        {
            game.Step(default, Dt);
            sawProp |= game.BreakProps.Any();
            foreach (var fighter in new[] { game.Player, game.Cpu })
            {
                if (game.BreakSourceOf(fighter) is BreakRoutine { CurrentName: { } name })
                {
                    names.Add(name);
                }
            }
        }

        Assert.IsGreaterThanOrEqualTo(2, names.Count, "1 回の休みの中で複数の動作を行う");
        Assert.IsTrue(sawProp || names.Overlaps([BreakBook.DeepBreath, BreakBook.ShoulderRoll, BreakBook.NeckRoll, BreakBook.SlapFace, BreakBook.Ropes, BreakBook.Glare, BreakBook.ShakeArms]));
        game.Step(default, Dt);
        Assert.IsFalse(game.InBreak);
        Assert.IsEmpty(game.BreakProps.ToList(), "休みが終われば小物は手に無い");
        Assert.IsNull(game.BreakSourceOf(game.Player), "休みが終われば休みの入力元を捨てる（元のボットが操作する）");
        Assert.IsNull(game.BreakSourceOf(game.Cpu));
    }

    [TestMethod]
    public void BreakActionsCanBeTurnedOff()
    {
        using var game = NewGame(5, actions: false);
        StepUntil(game, () => game.InBreak);
        for (var i = 0; i < 60 * 8; i++)
        {
            game.Step(default, Dt);
        }

        Assert.IsNull(game.BreakSourceOf(game.Player));
        Assert.IsEmpty(game.BreakProps.ToList());
    }

    [TestMethod]
    public void PlansHaveTwoToFourActionsThatFitTheBreakAndDifferBySeed()
    {
        var plans = new HashSet<string>();
        for (var seed = 1; seed <= 8; seed++)
        {
            using var game = NewGame(seed);
            StepUntil(game, () => game.InBreak);
            StepUntil(game, () => game.BreakSourceOf(game.Player) is BreakRoutine { Plan: not null });
            var plan = ((BreakRoutine)game.BreakSourceOf(game.Player)!).Plan!;
            Assert.IsInRange(BreakRoutine.MinActions, BreakRoutine.MaxActions, plan.Count);
            var last = plan[^1];
            Assert.IsLessThanOrEqualTo(game.BreakLeft, last.Start + last.Action.Seconds + 0.01f, "最後の動作は休みの中に収まる");
            plans.Add(string.Join(",", plan.Select(p => p.Action.Name)));
        }

        Assert.IsGreaterThan(2, plans.Count, "種ごとに違う並びになる");
    }

    [TestMethod]
    public void TheSamePlanComesFromTheSameSeed()
    {
        string Plan()
        {
            using var game = NewGame(7);
            StepUntil(game, () => game.InBreak);
            StepUntil(game, () => game.BreakSourceOf(game.Cpu) is BreakRoutine { Plan: not null });
            return string.Join(",", ((BreakRoutine)game.BreakSourceOf(game.Cpu)!).Plan!.Select(p => $"{p.Action.Name}@{p.Start:F3}/{p.Hand}"));
        }

        Assert.AreEqual(Plan(), Plan());
    }

    [TestMethod]
    public void TheStyleLikesBendTheChoice()
    {
        var likes = new Dictionary<string, float> { [BreakBook.Drink] = 1000f };
        var style = BotCatalog.Boxer with { BreakLikes = likes };
        using var game = NewGame(3);
        StepUntil(game, () => game.InBreak);
        var rest = new FighterCommand(Vector3.Zero, Vector3.UnitZ, new HandCommand(Vector3.Zero, false), new HandCommand(Vector3.Zero, false));
        var withDrink = 0;
        for (var seed = 1; seed <= 12; seed++)
        {
            var routine = new BreakRoutine(seed, style);
            var sense = game.PlayerSense;
            routine.Read(sense, rest, 16f, Dt);
            if (routine.Plan!.Any(p => p.Action.Name == BreakBook.Drink))
            {
                withDrink++;
            }
        }

        Assert.AreEqual(12, withDrink, "好みの倍率が大きい動作は必ず並びに入る");
    }

    [TestMethod]
    public void TiredFightersBreatheDeeperAndFaster()
    {
        var fresh = BreakBook.All(0f).First(a => a.Name == BreakBook.DeepBreath);
        var tired = BreakBook.All(1f).First(a => a.Name == BreakBook.DeepBreath);
        Assert.IsGreaterThan(fresh.Head.Wave!.Cycles, tired.Head.Wave!.Cycles);
        Assert.IsGreaterThan(fresh.Head.Wave.Amplitude.Y, tired.Head.Wave.Amplitude.Y);
        Assert.IsGreaterThan(fresh.Weights.Of(1f, 0f), tired.Weights.Of(0f, 0f), "息が上がっているほど深呼吸が選ばれやすい");
    }

    [TestMethod]
    public void ARecordedBreakReplaysBitForBit()
    {
        using var game = new BoxingGame(seed: 11, rules: ShortRules);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            var ai = new AiControl(11 * 31, 0.8f);
            for (var i = 0; i < 60 * 12; i++)
            {
                recorder.Step(game, new BoxingInput(ai.Read(game.PlayerSense, Dt)), Dt);
            }
        }

        Assert.IsTrue(game.InBreak || game.MatchRound >= 2, "記録に休みが入っている");
        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        using var player = new ReplayPlayer(data);
        while (!player.Finished)
        {
            player.StepOnce();
        }

        Assert.IsNull(player.FirstDivergence);
        Assert.IsGreaterThan(0, player.ChecksumsMatched);
        Assert.AreEqual(game.StateChecksum(), player.Game.StateChecksum());
    }
}
