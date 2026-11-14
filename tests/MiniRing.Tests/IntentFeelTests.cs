using System.Numerics;
using MiniRing.Bots;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>意図操作の「手触り」（回避の速さ・連打・打ち返し・横ステップ・カメラ基準の移動）を物理で測る。</summary>
[TestClass]
public sealed class IntentFeelTests
{
    private const float Dt = 1f / 60f;
    public TestContext TestContext { get; set; } = null!;

    /// <summary>ある回避だけが効く守りの本（来る拳の読みに関わらずその回避が選ばれる）。</summary>
    private static BotStyle OnlyEvasion(Defense chosen)
    {
        var book = DefenseBook.Default;
        foreach (var d in DefenseBook.Evasions)
        {
            var move = book.Of(d);
            book = book.With(d, move with { VsStraight = d == chosen ? 1f : 0f, VsHook = d == chosen ? 1f : 0f });
        }

        return BotCatalog.Boxer with { Defenses = book };
    }

    private static (BoxingGame Game, IntentControl Control) Settled(Defense? only = null, int seed = 43, FighterTuning? tuning = null)
    {
        var game = new BoxingGame(tuning, seed: seed);
        var control = only is { } d ? new IntentControl(style: OnlyEvasion(d)) : new IntentControl();
        for (var i = 0; i < 60; i++)
        {
            game.Step(new BoxingInput(control.Read(game.PlayerSense, Dt)), Dt);
        }

        return (game, control);
    }

    private static FighterSense Incoming(FighterSense sense)
        => sense with { OpponentRight = new GloveSense(sense.OpponentChest + new Vector3(0f, 0.1f, 0f), (sense.Self.Origin - sense.OpponentChest) * 4f) };

    /// <summary>回避を押してからの頭の下がり（Drop）と、構えの体の座標での横のずれ（Lateral）の最大 [cm]。</summary>
    private static (float Drop, float Lateral) HeadMotionCm(Defense defense, float seconds)
    {
        var (game, control) = Settled(defense);
        using var _ = game;
        var before = game.Player.HeadPosition.Y;
        var frame = game.PlayerSense.Self;
        var lateral0 = frame.WorldToLocal(game.Player.HeadPosition).X;
        var drop = 0f;
        var lateral = 0f;
        control.Input = new FighterIntent(Vector2.UnitY, Evade: true);
        for (var i = 0; i < (int)MathF.Round(seconds / Dt); i++)
        {
            game.Step(new BoxingInput(control.Read(Incoming(game.PlayerSense), Dt)), Dt);
            drop = MathF.Max(drop, before - game.Player.HeadPosition.Y);
            lateral = MathF.Max(lateral, MathF.Abs(frame.WorldToLocal(game.Player.HeadPosition).X - lateral0));
        }

        Assert.AreEqual(defense, control.CurrentDefense);
        return (drop * 100f, lateral * 100f);
    }

    [TestMethod]
    public void Duck_DropsTheHeadFast_KneesFoldUnderGravity()
    {
        // 改善前（脚のサーボがダンパになって沈みが遅かった・膝より腰のヒンジで下げていた）は 0.2 秒で 4.1 cm。
        var duck = HeadMotionCm(Defense.Duck, 0.2f);
        var weave = HeadMotionCm(Defense.Weave, 0.2f);
        TestContext.WriteLine($"ダッキング 0.2 秒: 頭 {duck.Drop:0.0} cm 下がる / ウィービング {weave.Drop:0.0} cm");
        Assert.IsGreaterThan(7f, duck.Drop, "ダッキングは 0.2 秒で頭が 7 cm 以上沈む（改善前 4.1 cm）");
        Assert.IsGreaterThan(3.5f, weave.Drop, "ウィービングも改善前（2.3 cm）の 1.5 倍以上");
    }

    [TestMethod]
    public void HeldDuck_StaysStanding_EvenWithACounterFromTheSink()
    {
        // 沈んだまま 1.5 秒押し続けても、体を打ち返しても、ダウンにならない（膝を折るだけ。腰・手・上体がマットに着かない）。
        foreach (var attack in new[] { false, true })
        {
            var (game, control) = Settled(Defense.Duck);
            using var _ = game;
            var lowest = float.PositiveInfinity;
            var minUpright = 1f;
            for (var i = 0; i < 90; i++)
            {
                control.Input = new FighterIntent(Vector2.UnitY, Heavy: attack && i is >= 30 and < 33, Evade: true);
                game.Step(new BoxingInput(control.Read(Incoming(game.PlayerSense), Dt)), Dt);
                lowest = MathF.Min(lowest, game.Player.HeadPosition.Y);
                minUpright = MathF.Min(minUpright, game.Player.Uprightness);
                Assert.AreEqual(FighterState.Standing, game.Player.State, $"step {i} attack {attack}");
            }

            TestContext.WriteLine($"長押しのダッキング（打ち返し {attack}）: 最も低い頭 {lowest:0.000} m、最小の直立度 {minUpright:0.00}");
        }
    }

    [TestMethod]
    public void Slip_MovesTheHeadSideways_Quickly()
    {
        var slip = HeadMotionCm(Defense.Slip, 0.2f);
        TestContext.WriteLine($"スリップ 0.2 秒: 横 {slip.Lateral:0.0} cm");
        Assert.IsGreaterThan(5f, slip.Lateral, "スリップは 0.2 秒で頭が 5 cm 以上横へずれる");
    }

    [TestMethod]
    public void TapJabs_FireRapidly_AndTapIsQuickerThanHold()
    {
        var tapStarts = StartSteps(heldSteps: 1, gapSteps: 5, count: 4);
        var holdStarts = StartSteps(heldSteps: 9, gapSteps: 5, count: 4);
        TestContext.WriteLine($"短押しの連打の打ち出し間隔 {Interval(tapStarts):0.000} s / 0.15 秒押してから離す連打 {Interval(holdStarts):0.000} s");
        Assert.HasCount(4, tapStarts, "短押しは 4 回とも打ち出される");
        Assert.IsLessThan(0.2f, Interval(tapStarts), "短押しのジャブは 0.2 秒より短い間隔で連打できる（改善前は 0.3 秒以上）");
        Assert.IsLessThan(Interval(holdStarts), Interval(tapStarts), "押して溜めたジャブは重く、間隔が長い");
    }

    private static List<int> StartSteps(int heldSteps, int gapSteps, int count)
    {
        var control = new IntentControl();
        var sense = new FighterSense(new BodyFrame(Vector3.Zero, 0f), 0.2f,
            new(0f, 0.06f, 0.27f), new(0f, 0f, 0.27f), true, new(0f, 0.06f, 0f));
        var starts = new List<int>();
        var period = heldSteps + gapSteps;
        var seen = 0;
        for (var step = 0; step < period * count + 60; step++)
        {
            var phase = step % period;
            control.Input = new FighterIntent(Vector2.Zero, Light: step < period * count && phase < heldSteps);
            control.Read(sense, Dt);
            if (control.AttacksStarted > seen)
            {
                seen = control.AttacksStarted;
                starts.Add(step);
            }
        }

        return starts;
    }

    private static float Interval(List<int> starts) => starts.Count < 2 ? float.PositiveInfinity : (starts[^1] - starts[0]) * Dt / (starts.Count - 1);

    [TestMethod]
    public void JabJabStraight_ComeOutInThePressedOrder()
    {
        var control = new IntentControl();
        var sense = new FighterSense(new BodyFrame(Vector3.Zero, 0f), 0.2f,
            new(0f, 0.06f, 0.27f), new(0f, 0f, 0.27f), true, new(0f, 0.06f, 0f));
        // J・J・K を 0.1 秒おきに短く押す。
        for (var step = 0; step < 90; step++)
        {
            control.Input = new FighterIntent(Vector2.Zero,
                Light: step is 0 or 1 or 6 or 7, Heavy: step is 12 or 13);
            control.Read(sense, Dt);
        }

        CollectionAssert.AreEqual(new[] { Technique.Jab, Technique.Jab, Technique.Cross }, control.StartedTechniques.ToArray());
        Assert.AreEqual("1-1-2", control.LastCombination);
        Assert.AreEqual(0, control.QueuedCount);
    }

    [TestMethod]
    public void BufferedPresses_ExpireInsteadOfFiringLate()
    {
        var control = new IntentControl();
        var sense = new FighterSense(new BodyFrame(Vector3.Zero, 0f), 0.2f,
            new(0f, 0.06f, 0.27f), new(0f, 0f, 0.27f), true, new(0f, 0.06f, 0f));
        // 強攻撃を長押ししている間、次の押下は待たされる。0.5 秒以上待たされたら捨てられる（離したあとに遅れて出ない）。
        for (var step = 0; step < 60; step++)
        {
            control.Input = new FighterIntent(Vector2.Zero, Light: step is 5 or 6, Heavy: true);
            control.Read(sense, Dt);
        }

        Assert.AreEqual(0, control.QueuedCount, "待たされすぎた先行入力は消える");
        control.Input = default;
        for (var i = 0; i < 120; i++) { control.Read(sense, Dt); }
        CollectionAssert.AreEqual(new[] { Technique.Cross }, control.StartedTechniques.ToArray());
    }

    [TestMethod]
    public void AttackDuringDuck_IsABodyBlow_AndSlipGivesTheOppositeHandHook()
    {
        var sense = Incoming(new FighterSense(new BodyFrame(Vector3.Zero, 0f), 0.2f,
            new(0f, 0.06f, 0.27f), new(0f, 0f, 0.27f), true, new(0f, 0.06f, 0f)));
        var duck = new IntentControl(style: OnlyEvasion(Defense.Duck)) { Input = new FighterIntent(Vector2.UnitY, Evade: true) };
        duck.Read(sense, Dt);
        for (var i = 0; i < 8; i++) { duck.Read(sense, Dt); }
        duck.Input = new FighterIntent(Vector2.UnitY, Light: true, Evade: true);
        duck.Read(sense, Dt);
        Assert.AreEqual(Defense.Duck, duck.CurrentDefense, "押している間は沈んだままで打つ");
        Assert.AreEqual(Technique.LeadBodyHook, duck.CurrentTechnique);

        var slip = new IntentControl(style: OnlyEvasion(Defense.Slip)) { Input = new FighterIntent(Vector2.UnitY, Evade: true) };
        slip.Read(sense, Dt);
        var coil = slip.Read(sense, Dt);
        slip.Input = new FighterIntent(Vector2.UnitY, Heavy: true, Evade: true);
        slip.Read(sense, Dt);
        Assert.AreEqual(Defense.Slip, slip.CurrentDefense);
        Assert.IsTrue(slip.CurrentTechnique is Technique.LeadHook or Technique.RearHook);
        Assert.AreNotEqual(0f, coil.TwistBias, "スリップは胴を反対の手側へ巻き込んでバネにする");
    }

    [TestMethod]
    public void BodyBlowFromTheDuck_UsesTheKneeSpringEnergy()
    {
        var (game, control) = Settled(Defense.Duck);
        using var _ = game;
        control.Input = new FighterIntent(Vector2.UnitY, Evade: true);
        for (var i = 0; i < 12; i++)
        {
            game.Step(new BoxingInput(control.Read(Incoming(game.PlayerSense), Dt)), Dt);
        }

        var sunk = (game.PlayerSense.Self.Origin.Y - Settled(Defense.Duck).Game.PlayerSense.Self.Origin.Y) * 100f;
        control.Input = new FighterIntent(Vector2.UnitY, Light: true, Evade: true);
        var peak = 0f;
        for (var i = 0; i < 18; i++)
        {
            var command = control.Read(Incoming(game.PlayerSense), Dt);
            game.Step(new BoxingInput(command), Dt);
            peak = MathF.Max(peak, game.Player.GloveVelocity(Hand.Left).Length());
            control.Input = new FighterIntent(Vector2.UnitY, Evade: true);
        }

        TestContext.WriteLine($"沈んだ体から打つボディの拳のピーク {peak:0.00} m/s（立ったまま 18 歩保持した右手のストレートは約 2 m/s）、体の高さの差 {sunk:0.0} cm");
        Assert.AreEqual(Technique.LeadBodyHook, control.CurrentTechnique);
        Assert.IsGreaterThan(0.5f, peak, "ボディの拳が体のバネに乗って出る");
    }

    [TestMethod]
    public void SideEvade_StepsQuicklyToTheSide_AndKeepsFacingTheOpponent()
    {
        var (game, control) = Settled();
        using var _ = game;
        var frame = game.PlayerSense.Self;
        var start = frame.WorldToLocal(game.Player.PelvisPosition);
        control.Input = new FighterIntent(Vector2.UnitX, Evade: true);
        for (var i = 0; i < 21; i++)
        {
            game.Step(new BoxingInput(control.Read(game.PlayerSense, Dt)), Dt);
        }

        var moved = frame.WorldToLocal(game.Player.PelvisPosition) - start;
        var faceError = MathF.Abs(BodyFrame.DeltaAngle(game.PlayerSense.Self.YawRadians, BodyFrame.YawOf(game.PlayerSense.OpponentChest - game.PlayerSense.Self.Origin, frame.YawRadians)));
        TestContext.WriteLine($"横＋回避 0.35 秒: 右へ {-moved.X * 100:0.0} cm（前後 {moved.Z * 100:0.0} cm）、相手への向きのずれ {faceError * 57.3f:0.0}°");
        Assert.AreEqual(Defense.SideStep, control.CurrentDefense);
        Assert.IsLessThan(-0.06f, moved.X, "押した右側（体のローカル −X）へ 6 cm 以上動く");
        Assert.IsLessThan(0.35f, faceError, "歩きながらも相手のほうを向いたまま");
    }

    [TestMethod]
    public void CameraRelativeKeys_MoveInTheCameraDirection_NotRelativeToTheOpponent()
    {
        // カメラが相手との線から 90° 回っているとき、奥へのキーは相手との線に直角の向きへ歩く（ラジコン操作にならない）。
        var yaw = (MathF.PI / 2f) + CameraFollow.BehindYaw(new Vector3(0f, 0f, -1f), Vector3.Zero, 0f);
        var world = CameraFollow.ToWorld(Vector2.UnitY, yaw);
        Assert.AreEqual(-MathF.Sin(yaw), world.X, 1e-5f);
        Assert.AreEqual(-MathF.Cos(yaw), world.Y, 1e-5f);
        Assert.AreEqual(1f, world.Length(), 1e-5f);
        // 真後ろのカメラ: 奥 = 相手のほう、右 = プレイヤーの右。
        var behind = CameraFollow.BehindYaw(new Vector3(0f, 0f, -1f), Vector3.Zero, 0f);
        var forward = CameraFollow.ToWorld(Vector2.UnitY, behind);
        Assert.AreEqual(0f, forward.X, 1e-5f);
        Assert.AreEqual(1f, forward.Y, 1e-5f);
        var right = CameraFollow.ToWorld(Vector2.UnitX, behind);
        Assert.AreEqual(-1f, right.X, 1e-5f, "+Z を向いた人の右は −X");
        Assert.IsLessThanOrEqualTo(1f + 1e-5f, CameraFollow.ToWorld(new Vector2(1f, 1f), 0.3f).Length());

        var (game, control) = Settled();
        using var _ = game;
        var before = game.Player.PelvisPosition;
        yaw = (MathF.PI / 2f) + CameraFollow.BehindYaw(game.PlayerSense.Self.Origin, game.PlayerSense.OpponentChest, 0f);
        var expected = CameraFollow.ToWorld(Vector2.UnitY, yaw);
        var line = game.PlayerSense.OpponentChest - game.PlayerSense.Self.Origin;
        var toOpponent = Vector3.Normalize(new Vector3(line.X, 0f, line.Z));
        control.Input = new FighterIntent(Vector2.Zero, World: expected);
        FighterCommand command = default;
        for (var i = 0; i < 60; i++)
        {
            command = control.Read(game.PlayerSense, Dt);
            game.Step(new BoxingInput(command), Dt);
        }

        var moved = game.Player.PelvisPosition - before;
        TestContext.WriteLine($"カメラ基準の奥へ 1 秒: ワールド ({moved.X * 100:0.0}, {moved.Z * 100:0.0}) cm、期待の向き ({expected.X:0.00}, {expected.Y:0.00})");
        Assert.IsGreaterThan(0.1f, Vector2.Dot(new Vector2(moved.X, moved.Z), expected), "カメラの奥の向きへ 10 cm 以上動く");
        Assert.IsLessThan(0.3f, MathF.Abs(Vector3.Dot(Vector3.Normalize(new Vector3(moved.X, 0f, moved.Z)), toOpponent)), "相手との線に対してはほぼ直角に動く");
        Assert.AreEqual(expected.X, command.MoveWorld.X, 1e-4f);
        Assert.AreEqual(expected.Y, command.MoveWorld.Z, 1e-4f);
        var now = game.PlayerSense.OpponentChest - game.PlayerSense.Self.Origin;
        Assert.IsGreaterThan(0.95f, Vector3.Dot(Vector3.Normalize(command.FaceDirection), Vector3.Normalize(now)), "向きは（動いたあとの）相手へ向けたまま");
        Assert.IsGreaterThan(0.9f, Vector3.Dot(Vector3.Normalize(new Vector3(now.X, 0f, now.Z)), Vector3.Normalize(new Vector3(game.PlayerSense.Self.Forward.X, 0f, game.PlayerSense.Self.Forward.Z))), "体も相手のほうを向く");
    }

    [TestMethod]
    public void SoftFollow_ConvergesWithoutOvershoot_AndIsFrameRateIndependent()
    {
        var a = Vector3.Zero;
        var goal = new Vector3(1f, 0f, 2f);
        for (var i = 0; i < 60; i++) { a = CameraFollow.DampTarget(a, goal, CameraFollow.DefaultRate, 1f / 60f); }
        var b = Vector3.Zero;
        for (var i = 0; i < 20; i++) { b = CameraFollow.DampTarget(b, goal, CameraFollow.DefaultRate, 1f / 20f); }
        Assert.AreEqual(a.X, b.X, 1e-4f);
        Assert.IsLessThanOrEqualTo(goal.X, a.X);
        Assert.IsGreaterThan(0.9f, a.X, "1 秒でほぼ追いつく（ゆっくり）");
        Assert.IsLessThan(0.5f, CameraFollow.DampTarget(Vector3.Zero, Vector3.UnitX, CameraFollow.DefaultRate, 0.1f).X, "0.1 秒では一部だけ（ゆっくり）");
        // 向きは最短の回りで寄せる（±π をまたいでも逆回りしない）。
        var yaw = CameraFollow.DampYaw(3f, -3f, CameraFollow.DefaultRate, 0.1f);
        Assert.IsGreaterThan(3f, yaw);
    }

    [TestMethod]
    public void CameraRelativeIntent_RecordsWorldCommands_AndReplaysEveryChecksum()
    {
        using var game = new BoxingGame(seed: 29);
        var control = new IntentControl();
        var stream = new MemoryStream();
        var checksums = new List<ulong>();
        var moves = new List<Vector3>();
        using (var recorder = new ReplayRecorder(stream, game, "intent-camera-test"))
        {
            for (var step = 0; step < 360; step++)
            {
                var phase = step % 120;
                var yaw = 0.4f + (step * 0.01f); // カメラはゆっくり回っている
                var keys = phase < 60 ? Vector2.UnitY : new Vector2(1f, -0.5f);
                control.Input = new FighterIntent(Vector2.Zero, Light: phase is >= 20 and < 23 or >= 26 and < 29, Heavy: phase is >= 32 and < 35,
                    Evade: phase is >= 90 and < 100, World: CameraFollow.ToWorld(keys, yaw));
                var command = control.Read(game.PlayerSense, Dt);
                moves.Add(command.MoveWorld);
                recorder.Step(game, new BoxingInput(command), Dt);
                checksums.Add(game.StateChecksum());
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        for (var i = 0; i < moves.Count; i++)
        {
            Assert.AreEqual(moves[i].X, data.Steps[i].Input.Player.MoveWorld.X, 1e-6f, "記録はカメラの状態ではなくワールドの方向を持つ");
        }

        using var replay = new ReplayPlayer(data);
        foreach (var checksum in checksums)
        {
            replay.StepOnce();
            Assert.AreEqual(checksum, replay.Game.StateChecksum());
        }
    }
}
