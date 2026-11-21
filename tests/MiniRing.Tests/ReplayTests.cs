using System.Numerics;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// 自動記録 → 再生で、試合が最初から完全に（ビット単位で）再現されることを確かめる。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class ReplayTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PunchPreset_ReplaysMatch(bool dynamicPunch)
    {
        var tuning = dynamicPunch ? FighterTuning.DynamicPunch : FighterTuning.CompactPunch;
        using var game = new BoxingGame(tuning, seed: 1);
        using var stream = new MemoryStream();
        var player = game.Player;
        Assert.IsTrue(player.Parts.Abdomen.HasValue);
        var command = new FighterCommand(Vector3.Zero, player.Frame.Forward,
            new HandCommand(new Vector3(0.15f, 0.10f, 0.30f), false),
            new HandCommand(new Vector3(-0.15f, 0.10f, 0.42f), false));
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < ReplayRecorder.ChecksumInterval; i++)
            {
                recorder.Step(game, new BoxingInput(command), 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual(tuning.ElbowMuscleMaxSpeed, data.Header.Tuning.ElbowMuscleMaxSpeed);
        Assert.AreEqual(tuning.WaveHandSeconds, data.Header.Tuning.WaveHandSeconds);
        Assert.AreEqual(tuning.ArmServoMaxSpeed, data.Header.Tuning.ArmServoMaxSpeed);
        Assert.AreEqual(tuning.CompactTorsoWave, data.Header.Tuning.CompactTorsoWave);
        Assert.IsTrue(ReplayPlayer.Verify(data).Reproduced);
    }

    [TestMethod]
    public void LocalVersus_ReplaysBothControllers()
    {
        var corners = Bots.BotCatalog.Corners("boxer") with { ExternalBlue = true };
        using var game = new BoxingGame(corners: corners);
        using var stream = new MemoryStream();
        var blue = new FighterCommand(Vector3.UnitX, -Vector3.UnitZ,
            new HandCommand(Vector3.One, true), new HandCommand(Vector3.Zero, false), Vector3.Zero);
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < ReplayRecorder.ChecksumInterval; i++)
            {
                recorder.Step(game, new BoxingInput(default, Blue: blue), 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual(blue, data.Steps[0].Input.Blue);
        Assert.IsTrue(data.Header.Corners.ExternalBlue);
        var result = ReplayPlayer.Verify(data);
        Assert.IsTrue(result.Reproduced);
        Assert.AreEqual(1, result.ChecksumsMatched);
    }
    [TestMethod]
    public void SpectatorMatch_ReplaysBothBots()
    {
        var corners = Bots.BotCatalog.Corners("boxer") with { RedBotStyle = Bots.BotCatalog.Swarmer };
        using var game = new BoxingGame(corners: corners);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < ReplayRecorder.ChecksumInterval; i++)
            {
                recorder.Step(game, default, 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual("swarmer", data.Header.Corners.RedBotStyle?.Name);
        var result = ReplayPlayer.Verify(data);
        Assert.IsTrue(result.Reproduced);
        Assert.AreEqual(1, result.ChecksumsMatched);
    }

    [TestMethod]
    public void NoReferee_MatchReplaysAndKeepsTheSetting()
    {
        var corners = Bots.BotCatalog.Corners("boxer") with { RedBotStyle = Bots.BotCatalog.Swarmer, Referee = false };
        using var game = new BoxingGame(corners: corners);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            // 倒れた相手への攻めが入る長さまで（ボット同士で 50 秒。人形の単位の 30 秒）。
            for (var i = 0; i < ReplayRecorder.ChecksumInterval * 50; i++)
            {
                recorder.Step(game, default, 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.IsFalse(data.Header.Corners.Referee, "the referee setting is in the replay header");
        var result = ReplayPlayer.Verify(data);
        Assert.IsTrue(result.Reproduced);
        Assert.IsGreaterThan(1, result.ChecksumsMatched);
    }

    private static readonly System.Text.Json.JsonSerializerOptions Fields = new() { IncludeFields = true };

    [TestMethod]
    public void Replay_OldHeaderWithoutRefereeReadsAsReferee()
    {
        var corners = Bots.BotCatalog.Corners("boxer");
        using var game = new BoxingGame(corners: corners);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test")) { }
        var json = System.Text.Json.JsonSerializer.Serialize(ReplayMatchSettings.From(corners, null), Fields);
        var old = json.Replace(",\"Referee\":true", string.Empty, StringComparison.Ordinal);
        Assert.AreNotEqual(json, old, "the new header has the Referee field");
        var parsed = System.Text.Json.JsonSerializer.Deserialize<ReplayMatchSettings>(old, Fields);
        Assert.IsNotNull(parsed);
        Assert.IsTrue(parsed.Referee, "an old replay without the field is read as a match with a referee");
    }

    [TestMethod]
    public void Replay_PreservesCustomMatchSettings()
    {
        var corners = Bots.BotCatalog.Corners("boxer") with
        {
            PlayerWear = Weapons.HandWear.None,
            CpuWear = Weapons.HandWear.Katar,
            PlayerBody = new BodyBuild(1.1f, 1.2f),
            Ring = new RingLayout(5.634f, 2.254f),
        };
        var rules = new MatchRules { RestGuardDrop = new Vector3(0.1f, -0.2f, 0.3f) };
        using var game = new BoxingGame(seed: 11, corners: corners, rules: rules);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test")) { }
        var header = ReplayFile.Read(new MemoryStream(stream.ToArray())).Header;
        Assert.AreEqual(corners.PlayerWear, header.Corners.PlayerWear);
        Assert.AreEqual(corners.CpuWear, header.Corners.CpuWear);
        Assert.AreEqual(corners.PlayerBody, header.Corners.PlayerBody);
        Assert.AreEqual(corners.Ring, header.Corners.Ring);
        Assert.AreEqual(rules.RestGuardDrop, header.MatchSettings!.Rules!.RestGuardDrop);
    }

    [TestMethod]
    public void Replay_PreservesHandShapesAndLegCommands()
    {
        var command = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(Vector3.One, false, Shape: HandShape.KnifeHand),
            new HandCommand(Vector3.Zero, true, Shape: HandShape.SpearHand),
            LeftLeg: new LegCommand(new Vector3(0.1f, 0.2f, 0.3f), new Vector3(0.4f, 0.5f, 0.6f), Quaternion.Identity, 0.7f),
            RightLeg: new LegCommand(new Vector3(-0.1f, 0.2f, 0.3f)));
        using var game = new BoxingGame(seed: 13, corners: Bots.BotCatalog.Corners("boxer") with
        {
            PlayerWear = Weapons.HandWear.None,
            CpuWear = Weapons.HandWear.None,
        });
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < 201; i++)
            {
                recorder.Step(game, new BoxingInput(command), 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual(command, data.Steps[0].Input.Player);
        using var player = new ReplayPlayer(data);
        while (!player.Finished)
        {
            player.StepOnce();
        }

        Assert.IsNull(player.FirstDivergence);
        Assert.IsGreaterThan(0, player.ChecksumsMatched);
        Assert.AreEqual(game.StateChecksum(), player.Game.StateChecksum());
    }

    [TestMethod]
    public void RecordedMatch_ReplaysBitForBit()
    {
        var (bytes, finalChecksum, steps) = RecordMatch(seed: 5, seconds: 33.57f, variableDt: false);

        var data = ReplayFile.Read(new MemoryStream(bytes));
        Assert.AreEqual(steps, data.Steps.Count);
        Assert.IsGreaterThan(10, data.Checksums.Count);

        using var player = new ReplayPlayer(data);
        while (!player.Finished)
        {
            player.StepOnce();
        }

        Assert.IsNull(player.FirstDivergence, $"diverged at step {player.FirstDivergence}");
        Assert.AreEqual(data.Checksums.Count, player.ChecksumsMatched);
        Assert.AreEqual(finalChecksum, player.Game.StateChecksum(), "the final state must be identical");
    }

    [TestMethod]
    public void VrStyleVariableTimesteps_AlsoReplayExactly()
    {
        // VR はフレームごとに時間刻みが変わる（72〜120Hz、ASW）。刻みごと記録するので再現できる。
        var (bytes, finalChecksum, _) = RecordMatch(seed: 9, seconds: 13.43f, variableDt: true);

        var result = ReplayPlayer.Verify(ReplayFile.Read(new MemoryStream(bytes)));
        Assert.IsTrue(result.Reproduced, $"diverged at {result.FirstDivergence}");

        using var player = new ReplayPlayer(ReplayFile.Read(new MemoryStream(bytes)));
        while (!player.Finished)
        {
            player.StepOnce();
        }

        Assert.AreEqual(finalChecksum, player.Game.StateChecksum());
    }

    [TestMethod]
    public void TamperedInput_IsDetectedAsDivergence()
    {
        var (bytes, _, _) = RecordMatch(seed: 5, seconds: 10.07f, variableDt: false);
        var data = ReplayFile.Read(new MemoryStream(bytes));
        var steps = data.Steps.ToArray();
        var i = 100;
        var c = steps[i].Input.Player;
        steps[i] = steps[i] with { Input = steps[i].Input with { Player = c with { MoveWorld = c.MoveWorld + new Vector3(0.5f, 0f, 0f) } } };

        var result = ReplayPlayer.Verify(data with { Steps = steps });

        Assert.IsFalse(result.Reproduced);
        Assert.IsGreaterThan(i, result.FirstDivergence!.Value);
    }

    [TestMethod]
    public void TruncatedFile_StillReplaysUpToTheLastCompleteStep()
    {
        // 強制終了で途中までしか書けていなくても、そこまでは再現できる。
        var (bytes, _, steps) = RecordMatch(seed: 5, seconds: 6.714f, variableDt: false);
        var cut = bytes.AsSpan(0, bytes.Length - 37).ToArray();

        var data = ReplayFile.Read(new MemoryStream(cut));
        Assert.IsGreaterThan(steps - 3, data.Steps.Count);
        Assert.IsTrue(ReplayPlayer.Verify(data).Reproduced);
    }

    [TestMethod]
    public void CorruptTimestep_IsRejectedInsteadOfReachingThePhysics()
    {
        var (bytes, _, _) = RecordMatch(seed: 5, seconds: 0.839f, variableDt: false);
        var data = ReplayFile.Read(new MemoryStream(bytes));
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            ReplayFile.WriteHeader(writer, data.Header);
            ReplayFile.WriteStep(writer, data.Steps[0] with { Dt = float.NaN });
        }

        Assert.ThrowsExactly<InvalidDataException>(() => ReplayFile.Read(new MemoryStream(stream.ToArray())));
    }

    [TestMethod]
    public void Header_CarriesTheSeedAndTheTuning()
    {
        var tuning = FighterTuning.Default with { WalkSpeed = 0.705f, BodyDrive = 1.1f };
        using var game = new BoxingGame(tuning, seed: 77);
        var stream = new MemoryStream();
        using (new ReplayRecorder(stream, game, "test"))
        {
        }

        var header = ReplayFile.Read(new MemoryStream(stream.ToArray())).Header;
        Assert.AreEqual(77, header.Seed);
        Assert.AreEqual("test", header.Host);
        Assert.AreEqual(tuning, header.Tuning);
    }

    [TestMethod]
    public void Header_CarriesTheCpuBot_AndReplaysAgainstTheSameOne()
    {
        // 記録した相手（CPU のボット）が既定と違っても、見出しから同じ相手を作り直して再現する。
        using var game = new BoxingGame(seed: 4, corners: Bots.BotCatalog.Corners("swarmer"));
        var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < 403; i++)
            {
                recorder.Step(game, new BoxingInput(default), 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual("swarmer", data.Header.CpuName);
        Assert.IsTrue(ReplayPlayer.Verify(data).Reproduced);
    }

    [TestMethod]
    public void ReplayStoresTheCpuStyleItself_SoRetuningTheChampionDoesNotBreakOldRecordings()
    {
        // 記録時の champion（ここでは独自の値）は、名前でなくスタイルごと残る。名簿の champion が後で変わっても同じ相手で再生する。
        var oldChampion = Bots.BotCatalog.Champion with { Aggression = 0.93f, NeutralRange = 1.12f, ReadGuard = 0.6f };
        using var game = new BoxingGame(seed: 6, corners: Bots.BotCatalog.Corners(oldChampion));
        var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < 403; i++)
            {
                recorder.Step(game, new BoxingInput(default), 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual("champion", data.Header.CpuName);
        Assert.AreEqual(0.93f, Bots.BotStyle.FromJson(data.Header.CpuStyleJson).Aggression);
        Assert.IsTrue(ReplayPlayer.Verify(data).Reproduced);
    }

    [TestMethod]
    public void ReplayStoresTheCpuPunchBook_SoATunedPunchTableReplaysIdentically()
    {
        // 技表（Punches）を変えた CPU のスタイルで記録して、見出しの JSON から作り直した相手で再生しても一致する（HeadShift を含む）。
        var book = Bots.PunchBook.Default
            .With(Bots.Technique.Jab, Bots.PunchBook.Default.Jab with { Duration = 0.4028f, HeadShiftX = 0.11f, HeadShiftZ = 0.2f })
            .With(Bots.Technique.Cross, Bots.PunchBook.Default.Cross with { Duration = 0.9231f, Overshoot = 0.45f, HeadShiftY = -0.12f });
        var tuned = Bots.BotCatalog.Champion with { Punches = book };
        using var game = new BoxingGame(seed: 8, corners: Bots.BotCatalog.Corners(tuned));
        var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < 806; i++)
            {
                recorder.Step(game, new BoxingInput(default), 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual(0.9231f, Bots.BotStyle.FromJson(data.Header.CpuStyleJson).Punches.Cross.Duration);
        Assert.AreEqual(0.11f, Bots.BotStyle.FromJson(data.Header.CpuStyleJson).Punches.Jab.HeadShiftX);
        Assert.IsTrue(ReplayPlayer.Verify(data).Reproduced);
    }

    [TestMethod]
    public void BotVersusBotMatch_IsNotRecorded_BecauseTheHostInputIsUnused()
    {
        var corners = Bots.BotCatalog.Corners("boxer") with { Player = seed => Bots.BotCatalog.Create("boxer", seed) };
        using var game = new BoxingGame(seed: 1, corners: corners);

        Assert.ThrowsExactly<ArgumentException>(() => new ReplayRecorder(new MemoryStream(), game, "test"));
    }

    [TestMethod]
    public void UnknownCpuWithoutAStyle_IsRejected()
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            ReplayFile.WriteHeader(writer, new ReplayHeader(1, System.Text.Json.JsonSerializer.Serialize(FighterTuning.Default), "x", "0", DateTime.UtcNow, "nobody", ""));
        }

        Assert.ThrowsExactly<InvalidDataException>(() => ReplayFile.Read(new MemoryStream(stream.ToArray())));
    }

    [TestMethod]
    public void OlderFormatReplay_IsRejected_BecauseTheBodyPhysicsChanged()
    {
        // 版 1・2 の記録は、足の運び・関節の位置・非線形のダメージより前の体で記録したもの。同じ入力でも違う試合になるので、黙って再生せずに断る。
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("MRRP"u8.ToArray());
            writer.Write(1);
            writer.Write(3);
            writer.Write(System.Text.Json.JsonSerializer.Serialize(FighterTuning.Default));
            writer.Write("old");
            writer.Write("0.1");
            writer.Write(DateTime.UtcNow.Ticks);
        }

        var error = Assert.ThrowsExactly<InvalidDataException>(() => ReplayFile.Read(new MemoryStream(stream.ToArray())));
        StringAssert.Contains(error.Message, "body physics");
    }

    /// <summary>CPU 同士（プレイヤー側も AI が操作）で打ち合い、途中でラウンドのやり直しも挟んで記録する。</summary>
    private static (byte[] Bytes, ulong FinalChecksum, int Steps) RecordMatch(int seed, float seconds, bool variableDt)
    {
        using var game = new BoxingGame(seed: seed);
        var playerAi = new AiControl(seed * 31, 0.8f);
        var random = new Random(seed);
        var stream = new MemoryStream();
        var steps = 0;
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var t = 0f; t < seconds;)
            {
                var dt = variableDt ? 1f / (72f + (48f * random.NextSingle())) : 1f / 60f;
                var reset = steps == 504;
                recorder.Step(game, new BoxingInput(playerAi.Read(game.PlayerSense, dt), reset), dt);
                t += dt;
                steps++;
            }

            return (stream.ToArray(), game.StateChecksum(), steps);
        }
    }
}
