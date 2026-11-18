using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Break;
using MiniRing.Characters;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// 試合の始め・休み・試合の終わりの動作（エモート。2026-11-18）: 場面ごとの動作の表・勝ち負けとキャラの好みで重みが連続に動く・小物が口・耳・頭の近くへ来る・
/// 始めの映像で腕が真横に伸びない（T 字なし）・終わりのカット割り・記録 → 再生でビット単位に同じ。
/// </summary>
[TestClass]
public sealed class EmoteTests
{
    private const float Dt = 1f / 60f;

    private static readonly MatchRules IntroRules = MatchRules.Tv with { Rounds = 1, RoundSeconds = 8f, IntroSeconds = 7f, EndSeconds = 7f };

    private static BoxingGame NewGame(int seed, MatchRules? rules = null, Func<int, Fighter, IBreakSource?>? source = null, float settle = 0f, string red = "champion", string blue = "swarmer") =>
        new(seed: seed, rules: rules ?? IntroRules, corners: new BoxingCorners("cpu", s => BotCatalog.Create(red, s), s => BotCatalog.Create(blue, s),
            BreakSource: source, SettleSeconds: settle));

    private static Fighter[] Both(BoxingGame game) => [game.Player, game.Cpu];

    private static FighterCommand RestCommand() => new(Vector3.Zero, Vector3.UnitZ, new HandCommand(Vector3.Zero, false), new HandCommand(Vector3.Zero, false));

    // ── 表 ──

    [TestMethod]
    public void ThereAreManyEmotesAndEveryNameIsUniqueAndUsedByAScene()
    {
        var all = BreakBook.All(0.5f);
        Assert.AreEqual(all.Count, all.Select(a => a.Name).Distinct().Count(), "動作の名前は重ならない");
        Assert.IsGreaterThanOrEqualTo(30, BreakEmotes.All().Count, "休みの動作に加えて新しい動作が 30 以上");
        Assert.IsGreaterThanOrEqualTo(60, all.Count, "休みの動作を合わせて 60 以上");
        foreach (var scene in new[] { BreakScene.Start, BreakScene.Break, BreakScene.End })
        {
            Assert.IsGreaterThanOrEqualTo(20, BreakBook.For(scene, 0.5f).Count, $"{scene} で選べる動作が 20 以上");
        }

        Assert.IsTrue(all.All(a => a.Scenes != 0), "どの動作もどれかの場面で選べる");
        var kinds = all.Where(a => a.Prop is not null).Select(a => a.Prop!.Value).ToHashSet();
        foreach (var kind in Enum.GetValues<PropKind>())
        {
            Assert.IsTrue(kinds.Contains(kind), $"小物 {kind} をどれかの動作が使う");
        }
    }

    [TestMethod]
    public void EndSceneOffersJoyToWinnersAndGriefToLosersByWeightNotByBranch()
    {
        var all = BreakBook.For(BreakScene.End, 0.5f);
        var raise = all.First(a => a.Name == BreakEmotes.RaiseArms);
        var headInHands = all.First(a => a.Name == BreakEmotes.HeadInHands);
        Assert.IsGreaterThan(raise.Weights.Of(1f, 0f, -1f) + 1f, raise.Weights.Of(1f, 0f, 1f), "勝者は両手を挙げやすい");
        Assert.IsGreaterThan(headInHands.Weights.Of(1f, 0f, 1f) + 1f, headInHands.Weights.Of(1f, 0f, -1f), "敗者は頭を抱えやすい");
        Assert.AreEqual(raise.Weights.Of(1f, 0f, 0f), raise.Weights.Of(1f, 0f, 0f), "同じ式（気分 0 は中立）");
    }

    [TestMethod]
    public void MoodAndLikesBendTheEndChoiceContinuously()
    {
        using var game = NewGame(3);
        var rest = RestCommand();
        var sense = game.PlayerSense with { OpponentAlive = true };

        float JoyShare(float mood)
        {
            var joyful = 0;
            var total = 0;
            for (var seed = 1; seed <= 60; seed++)
            {
                var routine = new BreakRoutine(seed, null, scene: BreakScene.End, mood: mood);
                routine.Read(sense, rest, 7f, Dt);
                foreach (var (_, action, _) in routine.Plan!)
                {
                    total++;
                    joyful += action.Weights.Joy > 0f ? 1 : 0;
                }
            }

            return joyful / (float)total;
        }

        var winner = JoyShare(1f);
        var neutral = JoyShare(0f);
        var loser = JoyShare(-1f);
        Assert.IsGreaterThan(neutral, winner, "勝者は喜ぶ動作が多い");
        Assert.IsGreaterThan(loser, neutral, "敗者は喜ぶ動作が少ない");
        Assert.IsLessThan(0.1f, loser, "敗者はほとんど喜ばない（重みが 0 になる）");

        var likes = new Dictionary<string, float> { [BreakEmotes.Wave] = 1e7f };
        var style = BotCatalog.Boxer with { BreakLikes = likes };
        for (var seed = 1; seed <= 12; seed++)
        {
            var routine = new BreakRoutine(seed, style, scene: BreakScene.Start);
            routine.Read(sense, rest, 6f, Dt);
            Assert.IsTrue(routine.Plan!.Any(p => p.Action.Name == BreakEmotes.Wave), "好みの倍率が大きい動作は始めの並びに必ず入る");
        }
    }

    [TestMethod]
    public void TheHandshakeNeedsAStandingPartner()
    {
        using var game = NewGame(3);
        var rest = RestCommand();
        var likes = new Dictionary<string, float> { [BreakEmotes.Handshake] = 1e7f };
        var style = BotCatalog.Boxer with { BreakLikes = likes };
        var alone = new BreakRoutine(1, style, scene: BreakScene.End);
        alone.Read(game.PlayerSense with { OpponentAlive = false }, rest, 7f, Dt);
        Assert.IsFalse(alone.Plan!.Any(p => p.Action.NeedsPartner), "相手が倒れていれば握手・拍手の相手がいない動作は選ばない");
        var together = new BreakRoutine(1, style, scene: BreakScene.End);
        together.Read(game.PlayerSense with { OpponentAlive = true }, rest, 7f, Dt);
        Assert.IsTrue(together.Plan!.Any(p => p.Action.Name == BreakEmotes.Handshake));
    }

    // ── 場面 ──

    [TestMethod]
    public void TheIntroKeepsTheClockStillRingsTheBellAfterwardsAndUsesStartEmotes()
    {
        using var game = NewGame(5);
        Assert.IsTrue(game.InIntro);
        Assert.AreEqual(BreakScene.Start, game.EmoteScene);
        Assert.IsTrue(game.Resting, "始めの間は打撃・時計が動かない休みの扱い");
        var startNames = BreakBook.For(BreakScene.Start, 0.5f).Select(a => a.Name).ToHashSet();
        var seen = new HashSet<string>();
        var bells = 0;
        var roundLeftWhileIntro = game.RoundLeft;
        for (var i = 0; i < 60 * 9 && game.MatchRound == 1; i++)
        {
            game.Step(default, Dt);
            bells += game.RecentBell == RoundBell.Start ? 1 : 0;
            if (game.InIntro)
            {
                Assert.AreEqual(roundLeftWhileIntro, game.RoundLeft, "始めの間はラウンドの時計が動かない");
                foreach (var f in Both(game))
                {
                    if (game.BreakSourceOf(f)?.CurrentName is { } name)
                    {
                        seen.Add(name);
                    }
                }
            }
            else if (bells > 0)
            {
                break;
            }
        }

        Assert.AreEqual(1, bells, "最初のゴングは始めの待ち時間の後に 1 回");
        Assert.IsFalse(game.InIntro);
        Assert.IsNotEmpty(seen, "始めの間に動作をする");
        Assert.IsTrue(seen.IsSubsetOf(startNames), $"始めは始めの表の動作だけ: {string.Join(",", seen.Except(startNames))}");
        game.Step(default, Dt);
        Assert.IsNull(game.BreakSourceOf(game.Player), "ゴングの後は元のボットが操作する");
    }

    [TestMethod]
    public void TheEndScenePutsWinnersAndLosersThroughTheEndTableForEndSeconds()
    {
        var endNames = BreakBook.For(BreakScene.End, 0.5f).Select(a => a.Name).ToHashSet();
        BoxingGame? found = null;
        for (var seed = 1; seed <= 20 && found is null; seed++) // 決着する種は物理の版ごとに変わる（足の運びを変えた版で 1〜6 には無かった）
        {
            var game = NewGame(seed, red: "champion", blue: "swarmer");
            for (var i = 0; i < 60 * 14 && !game.MatchOver; i++)
            {
                game.Step(default, Dt);
            }

            if (game.MatchOver && game.PlayerWins != game.CpuWins)
            {
                found = game;
            }
            else
            {
                game.Dispose();
            }
        }

        Assert.IsNotNull(found, "決着のついた試合を見つけられた");
        using var match = found;
        var winner = match.PlayerWins > match.CpuWins ? match.Player : match.Cpu;
        Assert.AreEqual(1f, match.EndMood(winner));
        Assert.AreEqual(-1f, match.EndMood(winner == match.Player ? match.Cpu : match.Player));
        Assert.AreEqual(BreakScene.End, match.EmoteScene);
        var seen = new HashSet<string>();
        var standing = new HashSet<Fighter>();
        for (var i = 0; i < 60 * 8; i++)
        {
            match.Step(default, Dt);
            foreach (var f in Both(match))
            {
                if (match.BreakSourceOf(f)?.CurrentName is { } name)
                {
                    seen.Add(name);
                    standing.Add(f);
                }
            }
        }

        Assert.IsNotEmpty(seen, "決着の後に立っている選手が終わりのエモートをする");
        Assert.IsTrue(seen.IsSubsetOf(endNames), $"終わりは終わりの表の動作だけ: {string.Join(",", seen.Except(endNames))}");
    }

    [TestMethod]
    public void NoEndSecondsMeansTheOldWalkToTheCorner()
    {
        using var game = NewGame(5, MatchRules.Tv with { Rounds = 1, RoundSeconds = 2f });
        Assert.IsFalse(game.InIntro, "始めの待ち時間 0 なら最初の歩からゴング");
        for (var i = 0; i < 60 * 4 && !game.MatchOver; i++)
        {
            game.Step(default, Dt);
        }

        Assert.IsTrue(game.MatchOver);
        Assert.IsNull(game.EmoteScene, "終わりの動作は EndSeconds が正のときだけ");
        game.Step(default, Dt);
        Assert.IsNull(game.BreakSourceOf(game.Player));
    }

    // ── T 字の姿勢 ──

    [TestMethod]
    public void SettlingRemovesTheStraightOutTPoseBeforeTheFirstFrame()
    {
        float Spread(BoxingGame game, Fighter fighter, Hand hand)
        {
            var local = fighter.Frame.WorldToLocal(fighter.GloveCenter(hand));
            return MathF.Abs(local.X) / fighter.ArmReach; // 肩の中心から横へどれだけ離れているか（腕の長さ比）
        }

        using var raw = NewGame(3, settle: 0f);
        using var settled = NewGame(3, settle: TvShowRules.SettleSeconds);
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var tPose = Spread(raw, raw.Player, hand);
            var guard = Spread(settled, settled.Player, hand);
            Assert.IsGreaterThan(1.0f, tPose, $"生成直後は腕が真横に伸びている（{tPose:F2}）");
            Assert.IsLessThan(0.8f, guard, $"助走の後は構えへ追いついている（{guard:F2}）");
            Assert.IsLessThan(0.8f, Spread(settled, settled.Cpu, hand), "青も構えている");
        }

        Assert.IsTrue(settled.InIntro, "助走は試合の時計を動かさない（始めの待ち時間は残っている）");
        Assert.AreEqual(IntroRules.IntroSeconds, settled.IntroLeft, 1e-4f);
        Assert.AreEqual(IntroRules.RoundSeconds, settled.RoundLeft, 1e-4f);
    }

    // ── 小物 ──

    [TestMethod]
    [DataRow("banana", PropKind.Banana, 0.047f)]
    [DataRow("sportsdrink", PropKind.SportsBottle, 0.06f)]
    [DataRow("coffee", PropKind.Cup, 0.06f)]
    [DataRow("cigarette", PropKind.Cigarette, 0.05f)]
    [DataRow("mouthguard", PropKind.MouthGuard, 0.045f)]
    public void ThePropEndComesToTheMouth(string name, PropKind kind, float limit)
    {
        using var game = NewGame(3, MatchRules.Tv with { Rounds = 2, RoundSeconds = 1f, BreakSeconds = 14f },
            (seed, _) => new BreakRoutine(seed, null, [name + ":R"]));
        for (var i = 0; i < 60 * 40 && !game.InBreak; i++)
        {
            game.Step(default, Dt);
        }

        var self = game.Player;
        var nearest = float.MaxValue;
        for (var i = 0; i < 60 * 14 && game.InBreak; i++)
        {
            game.Step(default, Dt);
            if (game.BreakProps.FirstOrDefault(p => p.Owner == self) is { } prop)
            {
                Assert.AreEqual(kind, prop.Kind);
                var end = Vector3.Transform(new Vector3(prop.Grid.HalfExtents.X * 0.85f, 0f, 0f), prop.GridToWorld());
                var mouth = self.HeadPosition + Vector3.Transform(new Vector3(0f, -0.012f, 0.025f), self.HeadOrientation);
                nearest = MathF.Min(nearest, Vector3.Distance(end, mouth));
            }
        }

        Assert.IsLessThan(limit, nearest, $"{name} の口の側の端が口の近くへ来る（最短 {nearest * 100f:F1} cm）");
    }

    [TestMethod]
    public void TheEarbudGoesToTheEarAndTheHeadphonesSitOnTheHead()
    {
        using var game = NewGame(3, MatchRules.Tv with { Rounds = 2, RoundSeconds = 1f, BreakSeconds = 14f },
            (seed, _) => new BreakRoutine(seed, null, ["earbud:R", "headphones"]));
        for (var i = 0; i < 60 * 40 && !game.InBreak; i++)
        {
            game.Step(default, Dt);
        }

        var self = game.Player;
        var nearestEar = float.MaxValue;
        var headphoneOffset = float.MaxValue;
        for (var i = 0; i < 60 * 14 && game.InBreak; i++)
        {
            game.Step(default, Dt);
            if (game.BreakProps.FirstOrDefault(p => p.Owner == self) is not { } prop)
            {
                continue;
            }

            if (prop.Kind == PropKind.Earbud)
            {
                var ear = self.HeadPosition + Vector3.Transform(new Vector3(-0.025f, 0f, 0f), self.HeadOrientation); // 右手の側の耳（頭の −X）
                nearestEar = MathF.Min(nearestEar, Vector3.Distance(Vector3.Transform(Vector3.Zero, prop.GridToWorld()), ear));
            }
            else if (prop.Kind == PropKind.Headphones)
            {
                headphoneOffset = MathF.Min(headphoneOffset, Vector3.Distance(Vector3.Transform(Vector3.Zero, prop.GridToWorld()), self.HeadPosition));
                Assert.AreEqual(PropAttach.Head, prop.Attach);
            }
        }

        Assert.IsLessThan(0.05f, nearestEar, $"イヤホンが耳の近くへ来る（最短 {nearestEar * 100f:F1} cm）");
        Assert.IsLessThan(0.002f, headphoneOffset, "ヘッドホンは頭の中心に付く");
    }

    [TestMethod]
    public void TheCigaretteMakesSmokeThatLeavesTheMouthOnly()
    {
        using var game = NewGame(3, MatchRules.Tv with { Rounds = 2, RoundSeconds = 1f, BreakSeconds = 14f },
            (seed, _) => new BreakRoutine(seed, null, ["cigarette:R"]));
        for (var i = 0; i < 60 * 40 && !game.InBreak; i++)
        {
            game.Step(default, Dt);
        }

        var smoked = 0;
        var before = new List<PrimitiveCount>();
        for (var i = 0; i < 60 * 14 && game.InBreak; i++)
        {
            game.Step(default, Dt);
            var instances = new List<Aigis.Render.PrimitiveInstance>();
            BreakVaporLook.WriteInstances(game, instances);
            smoked += instances.Count > 0 ? 1 : 0;
            before.Add(new PrimitiveCount(game.BreakVapors.Any(), instances.Count));
        }

        Assert.IsGreaterThan(30, smoked, "煙が数十歩にわたって描かれる");
        Assert.IsTrue(before.All(b => b.Emitting || b.Count == 0), "出していないときに煙は描かれない");
    }

    private readonly record struct PrimitiveCount(bool Emitting, int Count);

    // ── キャラの好み ──

    [TestMethod]
    public void EveryRosterFighterHasGroundedLikesWithKnownNames()
    {
        var names = BreakBook.All(0.5f).Select(a => a.Name).ToHashSet();
        var smokers = new List<string>();
        foreach (var c in CharacterRoster.All)
        {
            var likes = c.Resolve().BreakLikes;
            var liked = likes.Count(p => p.Value > 1f);
            Assert.IsInRange(5, 10, liked, $"{c.Id}: 好き（倍率 > 1）は 5〜10 個");
            foreach (var (name, factor) in likes)
            {
                Assert.IsTrue(names.Contains(name), $"{c.Id}: 知らない動作 '{name}'");
                Assert.IsGreaterThanOrEqualTo(0f, factor);
            }

            if (likes.TryGetValue(BreakEmotes.Cigarette, out var s) && s > 1f)
            {
                smokers.Add(c.Id);
            }
        }

        Assert.AreEqual("yoandry", string.Join(",", smokers), "たばこは葉巻の香りが趣味の選手だけ");
        var taeyun = CharacterRoster.Get("taeyun").Resolve().BreakLikes;
        Assert.IsGreaterThan(1f, taeyun[BreakEmotes.Dance], "趣味にダンスがある人はダンスが好き");
        Assert.IsGreaterThan(1f, CharacterRoster.Get("sean").Resolve().BreakLikes[BreakEmotes.Hum], "歌が好きな人は口ずさむ");
        Assert.IsGreaterThan(1f, CharacterRoster.Get("narong").Resolve().BreakLikes[BreakEmotes.Prayer], "リングへ上がる前に祈る人は祈る");
    }

    [TestMethod]
    public void TheLikedEmotesAreChosenMoreOften()
    {
        using var game = NewGame(3);
        var rest = RestCommand();
        var sense = game.PlayerSense with { OpponentAlive = true };

        int Count(string id, string name, BreakScene scene, float seconds)
        {
            var style = CharacterRoster.Get(id).Resolve();
            var n = 0;
            for (var seed = 1; seed <= 80; seed++)
            {
                var routine = new BreakRoutine(seed, style, scene: scene);
                routine.Read(sense, rest, seconds, Dt);
                n += routine.Plan!.Count(p => p.Action.Name == name);
            }

            return n;
        }

        // IsGreaterThan(下限, 値): 値 > 下限。
        Assert.IsGreaterThan(Count("akira", BreakEmotes.Headphones, BreakScene.Break, 16f), Count("dmitri", BreakEmotes.Headphones, BreakScene.Break, 16f), "民謡を聴くドミトリーはヘッドホンを選びやすい");
        Assert.IsGreaterThan(Count("narong", BreakEmotes.Cigarette, BreakScene.Break, 16f), Count("yoandry", BreakEmotes.Cigarette, BreakScene.Break, 16f), "葉巻が趣味のヨアンドリだけがたばこを選ぶ");
        Assert.IsGreaterThan(Count("joao", BreakEmotes.Bow, BreakScene.Start, 7f), Count("dmitri", BreakEmotes.Bow, BreakScene.Start, 7f), "深く頭を下げるドミトリーは、ふてくされやすいジョアンよりお辞儀を選ぶ");
        Assert.IsGreaterThan(Count("akira", BreakEmotes.Dance, BreakScene.Start, 7f), Count("taeyun", BreakEmotes.Dance, BreakScene.Start, 7f), "趣味がダンスのテユンは、無口な空手家のアキラよりダンスを選ぶ");
    }

    // ── 終わりのカット割り ──

    [TestMethod]
    public void TheEndShotsShowTheWinnerTheLoserAndThenTheWholeRingInEightToTenSeconds()
    {
        foreach (var down in new[] { false, true })
        {
            var plan = TvDirector.PlanEnd(9.3f, winnerRed: true, loserDown: down);
            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(TvShot.Handheld, plan[0].Shot);
            Assert.IsTrue(plan[0].Red, "勝者（赤）のアップから");
            Assert.IsInRange(3f, 4f, plan[0].End - plan[0].Start, "勝者のアップは 3〜4 秒");
            Assert.AreEqual(down ? TvShot.Ringside : TvShot.Handheld, plan[1].Shot, "倒れた敗者は低い所から、立っていれば手持ち");
            Assert.IsFalse(plan[1].Red);
            Assert.IsInRange(2f, 3f, plan[1].End - plan[1].Start, "敗者は 2〜3 秒");
            Assert.AreEqual(TvShot.Wide, plan[2].Shot, "最後は引き");
            Assert.AreEqual(plan[0].End, plan[1].Start, 1e-4f);
            Assert.AreEqual(plan[1].End, plan[2].Start, 1e-4f);
            Assert.IsGreaterThanOrEqualTo(2f, 9.3f - plan[2].Start, "引きは 2 秒以上");
        }

        var draw = TvDirector.PlanEnd(9.3f, winnerRed: null, loserDown: false);
        Assert.AreEqual(TvShot.Wide, draw[^1].Shot);
        Assert.AreNotEqual(draw[0].Red, draw[1].Red, "引き分けは両者を映す");
    }

    [TestMethod]
    public void TheEndAndIntroRulesLengthenTheMatchPlan()
    {
        var rules = TvShowRules.Rules(3, 20f);
        Assert.AreEqual(TvShowRules.IntroSeconds, rules.IntroSeconds);
        Assert.AreEqual(TvShowRules.EndSeconds, rules.EndSeconds);
        Assert.AreEqual(TvShowRules.IntroSeconds + (30f * 3) + (20f * 2), rules.TotalSeconds, 1e-3f);
        Assert.AreEqual(TvShowRules.SettleSeconds, TvShowRules.Stage(BotCatalog.Corners(BotCatalog.DefaultCpu), Organization.Default).SettleSeconds);
    }

    // ── 決定的 ──

    [TestMethod]
    public void AnIntroSettleBreakAndEndReplayBitForBit()
    {
        var rules = MatchRules.Tv with { Rounds = 2, RoundSeconds = 3f, BreakSeconds = 8f, IntroSeconds = 4f, EndSeconds = 4f };
        using var game = new BoxingGame(seed: 11, rules: rules, corners: BoxingCorners.Default with { SettleSeconds = 0.8f });
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            var ai = new AiControl(11 * 31, 0.8f);
            for (var i = 0; i < 60 * 26; i++)
            {
                recorder.Step(game, new BoxingInput(ai.Read(game.PlayerSense, Dt)), Dt);
            }
        }

        Assert.IsTrue(game.MatchOver, "終わりまで記録した（始め・休み・終わりを通る）");
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
